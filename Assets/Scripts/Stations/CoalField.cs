using System.Collections.Generic;
using UnityEngine;

// Loose coal. Coal is only a physics object while it's in the air: lumps are pooled bodies that,
// once they come to rest, are absorbed into a pile. Each floor (and the lift platform) stores its
// piles as a row of column heights. Piles slump when too steep, are solid to the operator and to
// lumps through one CustomCollider2D per floor, and are drawn as one stretched sprite per column.
public class CoalField : MonoBehaviour
{
    public enum SurfaceId { Lower, Upper, Lift }

    public class Surface
    {
        public SurfaceId id;
        public float start, end;
        public float[] heights;
        public readonly HashSet<int> dirty = new();
        public Transform root;            // World origin of the column x positions, at floor height.
        public CustomCollider2D collider;
        public SpriteRenderer[] columns;
        public System.Func<float> floor;  // Height of the floor itself.
        // CustomCollider2D shapes are placed relative to the attached Rigidbody2D (the world, for
        // floors without one), not to the collider's own transform. This is world (0, floor) in that space.
        public Vector2 shapeOrigin;
        public float Y => floor();
    }

    class Lump
    {
        public Rigidbody2D body;
        public float amount, rest;
        public bool hitPlayer;
        public bool Active => body.gameObject.activeSelf;
    }

    [SerializeField] Transform liftPlatform;
    [SerializeField] Sprite lumpSprite, columnSprite;
    [SerializeField] Material spriteMaterial;
    [SerializeField] Color coalColor = new(.07f, .07f, .08f);
    [SerializeField] int maxLumps = 240;

    public Surface[] Surfaces { get; private set; }
    public int ActiveLumps { get; private set; }

    ShiftManager game;
    readonly List<Lump> lumps = new();
    PhysicsMaterial2D lumpMaterial;
    float pileTimer, nextCoalInjury;
    readonly PhysicsShapeGroup2D shapes = new();
    readonly List<Vector2> quad = new() { default, default, default, default };

    // Awake, not Start: ShiftManager's Start already counts the coal in the piles. Its own Awake runs first.
    void Awake()
    {
        lumpMaterial = new PhysicsMaterial2D("Coal lump") { friction = .7f, bounciness = .05f };
        game = ShiftManager.Instance;
        Surfaces = new[]
        {
            MakeSurface(SurfaceId.Lower, .4f, 31.6f, transform, Room.Lower),
            MakeSurface(SurfaceId.Upper, 4, Room.Edge, transform, Room.Upper),
            // Rides on the lift: the platform's top surface is .15 above its body.
            MakeSurface(SurfaceId.Lift, .55f, 3.95f, liftPlatform, .15f),
        };
    }

    Surface MakeSurface(SurfaceId id, float start, float end, Transform parent, float y)
    {
        var go = new GameObject($"{id} piles") { layer = GameLayers.Pile };
        go.transform.SetParent(parent, false);
        // Columns are laid out in world x, so cancel the parent's x.
        go.transform.position = new Vector3(0, parent == transform ? y : parent.position.y + y, 0);
        var collider = go.AddComponent<CustomCollider2D>();
        collider.sharedMaterial = new PhysicsMaterial2D("Pile") { friction = .85f };
        int n = Mathf.CeilToInt((end - start) / game.Tune.columnWidth);
        var s = new Surface { id = id, start = start, end = end, heights = new float[n], root = go.transform, collider = collider, columns = new SpriteRenderer[n] };
        s.floor = id == SurfaceId.Lift ? () => game.Lift.Y : () => y;
        s.shapeOrigin = parent == transform ? new Vector2(0, y) : new Vector2(-parent.position.x, y);
        for (int i = 0; i < n; i++)
        {
            var col = new GameObject("Column");
            col.transform.SetParent(go.transform, false);
            var sr = col.AddComponent<SpriteRenderer>();
            sr.sprite = columnSprite;
            sr.color = coalColor;
            sr.sortingOrder = 12;
            if (spriteMaterial) sr.sharedMaterial = spriteMaterial;
            sr.enabled = false;
            s.columns[i] = sr;
        }
        return s;
    }

    // ---- Queries ----------------------------------------------------------------------------

    public Surface SurfaceAt(float x, float y)
    {
        float liftY = game.Lift.Y;
        if (x >= .55f && x <= 3.95f && y >= liftY - .3f && y <= liftY + 2.5f) return Surfaces[2];
        if (x >= 4 && x <= Room.Edge && y >= Room.Upper - .3f) return Surfaces[1];
        return Surfaces[0];
    }

    public int Index(Surface s, float x) => Mathf.Clamp(Mathf.FloorToInt((x - s.start) / game.Tune.columnWidth), 0, s.heights.Length - 1);
    public float PileHeight(Surface s, float x) => s.heights[Index(s, x)];
    // Top of the floor plus any pile, at x.
    public float GroundAt(float x, float y) { var s = SurfaceAt(x, y); return s.Y + PileHeight(s, x); }
    float ToHeight(float coal) => coal * game.Tune.pileVolume / game.Tune.columnWidth;
    float ToCoal(float height) => height * game.Tune.columnWidth / game.Tune.pileVolume;

    // Coal in piles and in the air, for the conservation check.
    public float Loose
    {
        get
        {
            float total = 0;
            foreach (var l in lumps) if (l.Active) total += l.amount;
            foreach (var s in Surfaces) foreach (var h in s.heights) total += ToCoal(h);
            return total;
        }
    }

    // Coal sitting on the lift platform, for its cranking resistance.
    public float LiftCoal { get { float t = 0; foreach (var h in Surfaces[2].heights) t += ToCoal(h); return t; } }

    // How deep the cart's bin sits in coal.
    public float CartBurial(Cart cart)
    {
        var p = cart.Body.position;
        var s = SurfaceAt(p.x, p.y);
        float pile = Mathf.Max(PileHeight(s, p.x - .6f), PileHeight(s, p.x), PileHeight(s, p.x + .6f));
        return Mathf.Max(0, s.Y + pile - (p.y - .6f));
    }

    // ---- Changing the piles -----------------------------------------------------------------

    public void AddToPile(Surface s, float x, float amount)
    {
        // The covered staff entrance returns coal to the bunker, keeping the doorway clear and the total intact.
        if (s.id == SurfaceId.Lower && x >= Room.EntranceStart) { game.Chute.Bunker += amount; return; }
        int i = Index(s, x);
        s.heights[i] += ToHeight(amount);
        Mark(s, i);
    }

    // Takes up to `amount` coal from one column and returns how much it got.
    public float TakeFromPile(Surface s, int i, float amount)
    {
        float q = Mathf.Min(ToCoal(s.heights[i]), amount);
        s.heights[i] -= ToHeight(q);
        Mark(s, i);
        return q;
    }

    // Moves pile height from one column to the next, for the cart bulldozing coal ahead of it.
    public void Shove(Surface s, int from, int to, float height)
    {
        float h = Mathf.Min(s.heights[from], height);
        s.heights[from] -= h;
        s.heights[to] += h;
        Mark(s, from);
        Mark(s, to);
    }

    void Mark(Surface s, int i)
    {
        for (int n = Mathf.Max(0, i - 1); n <= Mathf.Min(s.heights.Length - 1, i + 1); n++) s.dirty.Add(n);
    }

    // ---- Lumps ------------------------------------------------------------------------------

    // Throws `amount` coal into the air as lumps of at most half a unit each.
    public void Emit(float x, float y, float amount, float vx = 0, float vy = 0)
    {
        while (amount > 1e-8f)
        {
            float q = Mathf.Min(.5f, amount);
            amount -= q;
            var lump = lumps.Find(l => !l.Active);
            if (lump == null && lumps.Count >= maxLumps)
            {
                // Pool full: the nearest lump in the air carries the rest.
                Lump nearest = null;
                foreach (var l in lumps)
                    if (l.Active && (nearest == null || Mathf.Abs(l.body.position.x - x) < Mathf.Abs(nearest.body.position.x - x))) nearest = l;
                nearest.amount += q + amount;
                return;
            }
            lump ??= NewLump();
            var p = new Vector2(Mathf.Clamp(x + (Random.value - .5f) * .9f, .6f, 31.4f), y);
            lump.amount = q;
            lump.rest = 0;
            lump.hitPlayer = false;
            lump.body.transform.position = p;
            lump.body.gameObject.SetActive(true);
            lump.body.position = p;
            lump.body.linearVelocity = new Vector2(vx + (Random.value - .5f) * 1.8f, vy);
            lump.body.angularVelocity = 0;
        }
    }

    Lump NewLump()
    {
        var go = new GameObject("Lump") { layer = GameLayers.Coal };
        go.transform.SetParent(transform, false);
        var body = go.AddComponent<Rigidbody2D>();
        body.angularDamping = 2.5f; // Rolling resistance. Without it a lump can roll across the whole room on bare floor.
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
        var circle = go.AddComponent<CircleCollider2D>();
        circle.radius = .115f;
        circle.sharedMaterial = lumpMaterial;
        body.mass = Mathf.PI * .115f * .115f * .8f;
        var art = new GameObject("Art");
        art.transform.SetParent(go.transform, false);
        art.transform.localScale = Vector3.one * .26f;
        var sr = art.AddComponent<SpriteRenderer>();
        sr.sprite = lumpSprite;
        sr.color = coalColor;
        sr.sortingOrder = 13;
        if (spriteMaterial) sr.sharedMaterial = spriteMaterial;
        var lump = new Lump { body = body };
        lumps.Add(lump);
        return lump;
    }

    void Retire(Lump lump)
    {
        lump.amount = 0;
        lump.body.gameObject.SetActive(false);
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        StepLumps(dt);
        pileTimer += dt;
        if (pileTimer >= .1f) { pileTimer = 0; UpdatePiles(); }
    }

    void StepLumps(float dt)
    {
        var cart = game.Cart;
        var player = game.Player;
        var t = game.Tune;
        Vector2 cp = cart.Body.position, pp = player.Position;
        int active = 0;
        foreach (var lump in lumps)
        {
            if (!lump.Active) continue;
            active++;
            Vector2 p = lump.body.position, v = lump.body.linearVelocity;
            if (p.x >= Room.EntranceStart && p.y <= Room.EntranceRoof)
            {
                game.Chute.Bunker += lump.amount;
                Retire(lump);
                continue;
            }
            // A lump falling onto the operator's head and shoulders hurts.
            if (player.Lane == 0 && player.Body.simulated && !lump.hitPlayer && Mathf.Abs(p.x - pp.x) < .42f && p.y > pp.y + .4f && p.y < pp.y + 1.1f && v.y < -5)
            {
                lump.hitPlayer = true;
                if (game.Elapsed >= nextCoalInjury)
                {
                    player.Hurt(Mathf.Min(1, lump.amount * 1.4f));
                    nextCoalInjury = game.Elapsed + .25f;
                }
            }
            // The cart catches lumps that fall into its bin; what it can't hold goes over the side.
            Vector2 local = cart.Body.GetPoint(p);
            if (Mathf.Abs(local.x) < .92f && local.y > -.1f && local.y < 1.05f && v.y <= cart.Body.linearVelocity.y + .5f && Mathf.Abs(cart.Angle) < .48f)
            {
                float q = Mathf.Min(lump.amount, Mathf.Max(0, t.overfillCapacity - cart.Coal));
                cart.Coal += q;
                lump.amount -= q;
                if (lump.amount < 1e-7f) { Retire(lump); continue; }
                float side = local.x < 0 ? -1 : 1;
                lump.body.position = new Vector2(cp.x + side * 1.15f, Mathf.Max(p.y, cp.y + .6f));
                lump.body.linearVelocity = new Vector2(side, 0);
            }
            // A lump that has settled joins the pile under it.
            var s = SurfaceAt(p.x, p.y);
            float ground = s.Y + PileHeight(s, p.x);
            if (v.magnitude < 1.2f && p.y <= ground + .28f) lump.rest += dt; else lump.rest = 0;
            if (lump.rest > .15f || p.y < Room.Lower - .4f)
            {
                AddToPile(s, p.x, lump.amount);
                Retire(lump);
            }
        }
        ActiveLumps = active;
    }

    void UpdatePiles()
    {
        // A platform resting on the floor picks up whatever coal lay in the shaft.
        if (game.Lift.Y <= Room.Lower + .02f)
        {
            Surface lower = Surfaces[0], lift = Surfaces[2];
            for (int i = 0; i < lower.heights.Length; i++)
            {
                float x = lower.start + (i + .5f) * game.Tune.columnWidth, h = lower.heights[i];
                if (h <= 0 || x < lift.start || x > lift.end) continue;
                lower.heights[i] = 0;
                Mark(lower, i);
                AddToPile(lift, x, ToCoal(h));
            }
        }
        foreach (var s in Surfaces)
        {
            // Slump: anything steeper than .3 between neighbours flows downhill.
            for (int pass = 0; pass < 4; pass++)
                for (int i = 0; i < s.heights.Length - 1; i++)
                {
                    float d = s.heights[i] - s.heights[i + 1];
                    if (Mathf.Abs(d) <= .3f) continue;
                    float flow = (Mathf.Abs(d) - .3f) * .38f * Mathf.Sign(d);
                    s.heights[i] -= flow;
                    s.heights[i + 1] += flow;
                    Mark(s, i);
                    Mark(s, i + 1);
                }
            if (s.id == SurfaceId.Lower)
                for (int i = 0; i < s.heights.Length; i++)
                {
                    if (s.start + i * game.Tune.columnWidth < Room.EntranceStart - 1e-6f || s.heights[i] <= 0) continue;
                    game.Chute.Bunker += ToCoal(s.heights[i]);
                    s.heights[i] = 0;
                    Mark(s, i);
                }
            // The upper floor sheds coal off both ends, the lift platform only off its open side.
            if (s.id != SurfaceId.Lower)
                foreach (int i in s.id == SurfaceId.Lift ? new[] { s.heights.Length - 1 } : new[] { 0, s.heights.Length - 1 })
                {
                    if (s.heights[i] <= .22f) continue;
                    float q = Mathf.Min(.5f, ToCoal(s.heights[i] - .22f));
                    s.heights[i] -= ToHeight(q);
                    Mark(s, i);
                    Emit(i == 0 ? s.start - .3f : s.end + .3f, s.Y + .4f, q, i == 0 ? -1 : 1);
                }
            if (s.dirty.Count > 0) Rebuild(s);
        }
    }

    // Rebuilds a floor's pile collider and column art. One quad per column, its top sloping to the
    // average height of each neighbour so the surface reads as a slope rather than steps.
    void Rebuild(Surface s)
    {
        float w = game.Tune.columnWidth;
        shapes.Clear();
        for (int i = 0; i < s.heights.Length; i++)
        {
            float h = s.heights[i];
            var column = s.columns[i];
            column.enabled = h >= .018f;
            if (!column.enabled) continue;
            float x = s.start + i * w;
            column.transform.localPosition = new Vector3(x + w / 2, h / 2, 0);
            column.transform.localScale = new Vector3(w, h, 1);
            float left = i == 0 ? h : (h + s.heights[i - 1]) / 2;
            float right = i == s.heights.Length - 1 ? h : (h + s.heights[i + 1]) / 2;
            var o = s.shapeOrigin;
            quad[0] = o + new Vector2(x, -.05f);
            quad[1] = o + new Vector2(x + w, -.05f);
            quad[2] = o + new Vector2(x + w, Mathf.Max(.02f, right));
            quad[3] = o + new Vector2(x, Mathf.Max(.02f, left));
            shapes.AddPolygon(quad);
        }
        s.collider.SetCustomShapes(shapes);
        s.dirty.Clear();
    }
}

using System.Collections.Generic;
using System;
using CoalRun.Core;
using UnityEngine;
using Random = UnityEngine.Random;

namespace CoalRun.Gameplay.Coal
{
    // Loose coal. Coal is only a physics object while it's in the air: lumps are pooled bodies that,
    // once they come to rest, are absorbed into a pile. Each floor (and the lift platform) stores its
    // piles as a row of column heights. Piles slump when too steep, are solid to the operator and to
    // lumps through one CustomCollider2D per floor, and are drawn as one stretched sprite per column.
    [UnityEngine.Scripting.APIUpdating.MovedFrom("CoalRun.Stations")]
    public class CoalField : MonoBehaviour
    {
        public enum SurfaceId
        {
            Lower,
            Upper,
            Lift
        }

        // The least coal that goes into the air as a lump (see Emit).
        private const float MinLump = .05f;

        [SerializeField] private Transform liftPlatform;
        [SerializeField] private Rigidbody2D lumpPrefab;
        [SerializeField] private PhysicsMaterial2D pileMaterial;
        [SerializeField] private Sprite columnSprite;

        [Tooltip("A few lump shapes, one picked for each lump. Empty: they all use the lump sprite.")] [SerializeField]
        private Sprite[] lumpVariants;

        [SerializeField] private Material spriteMaterial;
        [SerializeField] private Color coalColor = new(.07f, .07f, .08f);
        [SerializeField] private int maxLumps = 240;

        [Tooltip("How fast a lump on the ground loses its sideways speed, in m/s per second.")] [SerializeField]
        private float groundDrag = 30;

        private readonly List<Lump> _lumps = new();
        private readonly List<Vector2> _quad = new() { default, default, default, default };
        private readonly PhysicsShapeGroup2D _shapes = new();

        private ShiftManager _game;
        private float _pileTimer, _nextCoalInjury;

        public Surface[] Surfaces { get; private set; }
        public int ActiveLumps { get; private set; }

        // Coal in piles and in the air, for the conservation check.
        public float Loose
        {
            get
            {
                float total = 0;
                foreach (var l in _lumps)
                    if (l.Active)
                        total += l.Amount;
                foreach (var s in Surfaces)
                foreach (var h in s.Heights)
                    total += ToCoal(h);
                return total;
            }
        }

        // Coal sitting on the lift platform, for its cranking resistance.
        public float LiftCoal
        {
            get
            {
                float t = 0;
                foreach (var h in Surfaces[2].Heights) t += ToCoal(h);
                return t;
            }
        }

        // Awake, not Start: ShiftManager's Start already counts the coal in the piles. Its own Awake runs first.
        private void Awake()
        {
            _game = ShiftManager.Instance;
            Surfaces = new[]
            {
                MakeSurface(SurfaceId.Lower, _game.Room.LowerLeft + .05f, _game.Room.LowerRight - .1f, transform, _game.Room.Lower),
                MakeSurface(SurfaceId.Upper, _game.Room.UpperLeft, _game.Room.Edge, transform, _game.Room.Upper),
                // Rides on the lift: the platform's top surface is .15 above its body.
                MakeSurface(SurfaceId.Lift, _game.Room.LiftLeft + .05f, _game.Room.LiftRight - .05f, liftPlatform, .15f)
            };
            // Coal in the shaft is drawn behind the platform, which comes down onto it and sinks into it a little.
            for (var i = 0; i < Surfaces[0].Columns.Length; i++)
                if (InShaft(i))
                    Surfaces[0].Columns[i].sortingOrder = 2;
        }

        private void FixedUpdate()
        {
            var dt = Time.fixedDeltaTime;
            StepLumps(dt);
            _pileTimer += dt;
            if (_pileTimer < .1f) return;
            _pileTimer = 0;
            DrainHopperGrate();
            UpdatePiles();
        }

        private Surface MakeSurface(SurfaceId id, float start, float end, Transform parent, float y)
        {
            var go = new GameObject($"{id} piles") { layer = GameLayers.Pile };
            go.transform.SetParent(parent, false);
            // Columns are laid out in world x, so cancel the parent's x.
            go.transform.position = new Vector3(0, parent == transform ? y : parent.position.y + y, 0);
            var collider = go.AddComponent<CustomCollider2D>();
            collider.sharedMaterial = pileMaterial;
            var n = Mathf.CeilToInt((end - start) / _game.Tune.columnWidth);
            var s = new Surface
            {
                Id = id, Start = start, End = end, Heights = new float[n], Collider = collider,
                Columns = new SpriteRenderer[n],
                Floor = id == SurfaceId.Lift ? () => _game.Lift.Y : () => y,
                ShapeOrigin = parent == transform ? new Vector2(0, y) : new Vector2(-parent.position.x, y)
            };
            for (var i = 0; i < n; i++)
            {
                var col = new GameObject("Column");
                col.transform.SetParent(go.transform, false);
                var sr = col.AddComponent<SpriteRenderer>();
                sr.sprite = columnSprite;
                sr.color = coalColor;
                sr.sortingOrder = 16;
                if (spriteMaterial) sr.sharedMaterial = spriteMaterial;
                sr.enabled = false;
                s.Columns[i] = sr;
            }

            return s;
        }

        // ---- Queries ----------------------------------------------------------------------------

        public Surface SurfaceAt(float x, float y)
        {
            var liftY = _game.Lift.Y;
            var room = _game.Room;
            if (x >= room.LiftLeft + .05f && x <= room.LiftRight - .05f && y >= liftY - .3f && y <= liftY + 2.5f)
                return Surfaces[2];
            if (x >= room.UpperLeft && x <= room.Edge && y >= room.Upper - .3f) return Surfaces[1];
            return Surfaces[0];
        }

        // Over the shaft, falling coal lands on the platform below it.
        private Surface SurfaceBelow(float x, float y)
        {
            return x >= _game.Room.LiftLeft + .05f && x <= _game.Room.LiftRight - .05f && y >= _game.Lift.Y - .3f
                ? Surfaces[2] : SurfaceAt(x, y);
        }

        public int Index(Surface s, float x)
        {
            return Mathf.Clamp(Mathf.FloorToInt((x - s.Start) / _game.Tune.columnWidth), 0, s.Heights.Length - 1);
        }

        public float PileHeight(Surface s, float x)
        {
            return s.Heights[Index(s, x)];
        }

        // Top of the floor plus any pile, at x.
        public float GroundAt(float x, float y)
        {
            var s = SurfaceAt(x, y);
            return s.Y + PileHeight(s, x);
        }

        // The highest pile top under a walker's next step. Unlike GroundAt this looks at every surface whose columns
        // cover x: the lift's last column pokes past the edge SurfaceAt switches floors at, and its lip blocked the step-up.
        public float WalkGroundAt(float x, float y)
        {
            var best = GroundAt(x, y);
            var width = _game.Tune.columnWidth;
            foreach (var s in Surfaces)
            {
                if (x < s.Start || x >= s.Start + s.Heights.Length * width ||
                    Mathf.Abs(s.Y - (y - .72f)) > .35f) continue;
                best = Mathf.Max(best, s.Y + s.Heights[Index(s, x)]);
            }

            return best;
        }

        private float ToHeight(float coal)
        {
            return coal * _game.Tune.pileVolume / _game.Tune.columnWidth;
        }

        private float ToCoal(float height)
        {
            return height * _game.Tune.columnWidth / _game.Tune.pileVolume;
        }

        // How deep the cart's bin sits in coal.
        public float CartBurial(CoalRun.Gameplay.Cart.Cart cart)
        {
            var p = cart.Body.position;
            var s = SurfaceAt(p.x, p.y);
            var pile = Mathf.Max(PileHeight(s, p.x - .6f), PileHeight(s, p.x), PileHeight(s, p.x + .6f));
            return Mathf.Max(0, s.Y + pile - (p.y - .6f));
        }

        // ---- Changing the piles -----------------------------------------------------------------

        public void AddToPile(Surface s, float x, float amount)
        {
            // The covered staff entrance returns coal to the bunker, keeping the doorway clear and the total intact.
            if (s.Id == SurfaceId.Lower && x >= _game.Room.EntranceStart)
            {
                _game.Chute.Bunker += amount;
                return;
            }

            // The hopper grate passes coal down into the hopper; once it's full, coal heaps up on the grate.
            if (OnHopperGrate(s, x)) amount -= _game.Furnace.Feed(amount);
            if (amount <= 0) return;
            var i = Index(s, x);
            s.Heights[i] += ToHeight(amount);
            Mark(s, i);
        }

        // Whether the lower floor's column i is in the lift shaft, under the platform.
        private bool InShaft(int i)
        {
            var x = Surfaces[0].Start + (i + .5f) * _game.Tune.columnWidth;
            return x >= Surfaces[2].Start && x <= Surfaces[2].End;
        }

        // How thick the coal on the floor of the shaft would be flattened out: the lowest the platform can
        // squash it to.
        public float ShaftLayer()
        {
            var s = Surfaces[0];
            float sum = 0, count = 0;
            for (var i = 0; i < s.Heights.Length; i++)
            {
                if (!InShaft(i)) continue;
                sum += s.Heights[i];
                count++;
            }

            return count > 0 ? sum / count : 0;
        }

        // The tallest heap on the floor of the shaft, where the platform first touches coal.
        public float ShaftPeak()
        {
            var s = Surfaces[0];
            float peak = 0;
            for (var i = 0; i < s.Heights.Length; i++)
                if (InShaft(i))
                    peak = Mathf.Max(peak, s.Heights[i]);
            return peak;
        }

        // The platform pressing coal in the shaft down to `room` deep: what's above it is pushed sideways
        // into the lower parts of the shaft, flattening the heap. The lift stops at ShaftLayer, so it fits;
        // past the open side, the floor's own slump squeezes it out.
        public void Squash(float room)
        {
            var s = Surfaces[0];
            float excess = 0, space = 0;
            for (var i = 0; i < s.Heights.Length; i++)
            {
                if (!InShaft(i)) continue;
                if (s.Heights[i] > room)
                {
                    excess += s.Heights[i] - room;
                    s.Heights[i] = room;
                    Mark(s, i);
                }
                else
                {
                    space += room - s.Heights[i];
                }
            }

            if (excess <= 0) return;
            // Shared out in proportion to the room each column has, so the hollows fill evenly.
            var share = Mathf.Min(1, excess / Mathf.Max(space, 1e-6f));
            for (var i = 0; i < s.Heights.Length; i++)
            {
                if (!InShaft(i) || s.Heights[i] >= room) continue;
                var add = (room - s.Heights[i]) * share;
                s.Heights[i] += add;
                excess -= add;
                Mark(s, i);
            }

            // Any rounding left over goes out of the open side.
            if (excess > 0) AddToPile(s, Surfaces[2].End + _game.Tune.columnWidth / 2, ToCoal(excess));
        }

        // Anything steeper than .3 between neighbouring columns flows downhill.
        private static void Slump(Surface a, int i, Surface b, int j)
        {
            var d = a.Heights[i] - b.Heights[j];
            if (Mathf.Abs(d) <= .3f) return;
            var flow = (Mathf.Abs(d) - .3f) * .38f * Mathf.Sign(d);
            a.Heights[i] -= flow;
            b.Heights[j] += flow;
            Mark(a, i);
            Mark(b, j);
        }

        private bool OnHopperGrate(Surface s, float x)
        {
            return s.Id == SurfaceId.Upper && x >= _game.Room.HopperStart && x <= _game.Room.HopperEnd;
        }

        // Coal heaped on the hopper grate drops through as the hopper burns down and makes room.
        private void DrainHopperGrate()
        {
            var s = Surfaces[1];
            for (var i = Index(s, _game.Room.HopperStart); i <= Index(s, _game.Room.HopperEnd); i++)
            {
                var room = _game.Tune.hopperCapacity - _game.Furnace.Hopper;
                if (room <= 0) return;
                if (s.Heights[i] > 0) _game.Furnace.Feed(TakeFromPile(s, i, room), false);
            }
        }

        // Sends every lump and pile back to the bunker, keeping the total intact. For tests and debugging.
        public void ReturnAll()
        {
            foreach (var lump in _lumps)
                if (lump.Active)
                {
                    _game.Chute.Bunker += lump.Amount;
                    Retire(lump);
                }

            foreach (var surface in Surfaces)
                for (var i = 0; i < surface.Heights.Length; i++)
                    _game.Chute.Bunker += TakeFromPile(surface, i, float.MaxValue);
        }

        // Takes up to `amount` coal from one column and returns how much it got.
        public float TakeFromPile(Surface s, int i, float amount)
        {
            var q = Mathf.Min(ToCoal(s.Heights[i]), amount);
            s.Heights[i] -= ToHeight(q);
            Mark(s, i);
            return q;
        }

        // Moves pile height from one column to the next, for the cart bulldozing coal ahead of it.
        public static void Shove(Surface s, int from, int to, float height)
        {
            var h = Mathf.Min(s.Heights[from], height);
            s.Heights[from] -= h;
            s.Heights[to] += h;
            Mark(s, from);
            Mark(s, to);
        }

        private static void Mark(Surface s, int i)
        {
            for (var n = Mathf.Max(0, i - 1); n <= Mathf.Min(s.Heights.Length - 1, i + 1); n++) s.Dirty.Add(n);
        }

        // ---- Lumps ------------------------------------------------------------------------------

        // Throws `amount` coal into the air as lumps of at most half a unit each. Less than `MinLump` is
        // too little to see and goes straight onto the pile below: the slump feeds a floor's edge by
        // specks, and shedding each one as a lump made an endless stream of empty lumps.
        public void Emit(float x, float y, float amount, float vx = 0, float vy = 0, float spread = 1)
        {
            if (amount < MinLump)
            {
                if (amount > 0) AddToPile(SurfaceBelow(x, y), x, amount);
                return;
            }

            while (amount > 1e-8f)
            {
                var q = amount - .5f < MinLump ? amount : .5f; // The last lump takes any speck left over.
                amount -= q;
                var lump = _lumps.Find(l => !l.Active);
                if (lump == null && _lumps.Count >= maxLumps)
                {
                    // Pool full, so every lump is in the air: the nearest carries the rest.
                    var nearest = _lumps[0];
                    foreach (var l in _lumps)
                        if (Mathf.Abs(l.Body.position.x - x) < Mathf.Abs(nearest.Body.position.x - x))
                            nearest = l;
                    nearest.Amount += q + amount;
                    return;
                }

                lump ??= NewLump();
                var p = new Vector2(Mathf.Clamp(x + (Random.value - .5f) * .9f * spread, _game.Room.LowerLeft + .25f, _game.Room.LowerRight - .3f), y);
                lump.Amount = q;
                lump.Rest = 0;
                lump.HitPlayer = lump.HitHelper = false;
                lump.Body.transform.position = p;
                lump.Body.gameObject.SetActive(true);
                lump.Body.position = p;
                lump.Body.linearVelocity = new Vector2(vx + (Random.value - .5f) * 1.8f * spread, vy);
                lump.Body.angularVelocity = 0;
            }
        }

        private Lump NewLump()
        {
            var body = Instantiate(lumpPrefab, transform);
            if (lumpVariants is { Length: > 0 })
                body.GetComponentInChildren<SpriteRenderer>().sprite = lumpVariants[Random.Range(0, lumpVariants.Length)];
            var lump = new Lump { Body = body };
            _lumps.Add(lump);
            return lump;
        }

        private static void Retire(Lump lump)
        {
            lump.Amount = 0;
            lump.Body.gameObject.SetActive(false);
        }

        private void StepLumps(float dt)
        {
            var cart = _game.Cart;
            var player = _game.Player;
            var t = _game.Tune;
            Vector2 cp = cart.Body.position, pp = player.Position;
            var active = 0;
            foreach (var lump in _lumps)
            {
                if (!lump.Active) continue;
                active++;
                Vector2 p = lump.Body.position, v = lump.Body.linearVelocity;
                // The return grate sends landed coal back to the bunker.
                if (p.x >= _game.Room.EntranceStart && p.y <= _game.Room.Lower + .2f)
                {
                    _game.Chute.Bunker += lump.Amount;
                    Retire(lump);
                    continue;
                }

                if (p.x >= _game.Room.HopperStart && p.x <= _game.Room.HopperEnd &&
                    p.y <= _game.Room.Upper + .2f && p.y >= _game.Room.Upper - .3f)
                {
                    lump.Amount -= _game.Furnace.Feed(lump.Amount);
                    if (lump.Amount <= 0)
                    {
                        Retire(lump);
                        continue;
                    }
                }

                // A lump falling onto the operator's head and shoulders hurts.
                if (player.Lane == 0 && player.Body.simulated && !lump.HitPlayer && Mathf.Abs(p.x - pp.x) < .42f &&
                    p.y > pp.y + .4f && p.y < pp.y + 1.1f && v.y < -5)
                {
                    lump.HitPlayer = true;
                    if (_game.Elapsed >= _nextCoalInjury)
                    {
                        player.Hurt(Mathf.Min(1, lump.Amount * 1.4f));
                        _nextCoalInjury = _game.Elapsed + .25f;
                    }
                }

                // And the tutorial's helper, standing under the chute.
                if (!lump.HitHelper && v.y < -1 && _game.Tutorial.HitHelper(p, lump.Amount)) lump.HitHelper = true;
                // The cart catches lumps that fall into its bin; what it can't hold goes over the side.
                var local = cart.Body.GetPoint(p);
                if (Mathf.Abs(local.x) < .92f && local.y is > -.1f and < 1.05f &&
                    v.y <= cart.Body.linearVelocity.y + .5f && Mathf.Abs(cart.Angle) < .48f)
                {
                    var q = Mathf.Min(lump.Amount, Mathf.Max(0, t.overfillCapacity - cart.Coal));
                    cart.Coal += q;
                    lump.Amount -= q;
                    if (lump.Amount < 1e-7f)
                    {
                        Retire(lump);
                        continue;
                    }

                    float side = local.x < 0 ? -1 : 1;
                    lump.Body.position = new Vector2(cp.x + side * 1.15f, Mathf.Max(p.y, cp.y + .6f));
                    lump.Body.linearVelocity = new Vector2(side, 0);
                }

                // A lump that has settled joins the pile under it.
                var s = SurfaceAt(p.x, p.y);
                var ground = s.Y + PileHeight(s, p.x);
                // Coal thuds where it lands instead of rolling like a ball, so thrown coal goes where it's aimed.
                if (p.y <= ground + .3f && v.y < 1)
                {
                    v.x = Mathf.MoveTowards(v.x, 0, groundDrag * dt);
                    lump.Body.linearVelocity = v;
                }

                if (v.magnitude < 1.2f && p.y <= ground + .28f) lump.Rest += dt;
                else lump.Rest = 0;
                if (lump.Rest <= .15f && p.y >= _game.Room.Lower - .4f) continue;
                AddToPile(s, p.x, lump.Amount);
                Retire(lump);
            }

            ActiveLumps = active;
        }

        private void UpdatePiles()
        {
            Surface lowerFloor = Surfaces[0], upperFloor = Surfaces[1], platform = Surfaces[2];
            var edge = platform.Heights.Length - 1;
            // A platform resting on the floor picks up whatever coal lay in the shaft, and its open side
            // joins the floor.
            var resting = _game.Lift.Y <= _game.Room.Lower + .02f;
            if (resting)
            {
                for (var i = 0; i < lowerFloor.Heights.Length; i++)
                {
                    var h = lowerFloor.Heights[i];
                    if (h <= 0 || !InShaft(i)) continue;
                    lowerFloor.Heights[i] = 0;
                    Mark(lowerFloor, i);
                    AddToPile(platform, lowerFloor.Start + (i + .5f) * _game.Tune.columnWidth, ToCoal(h));
                }

                Slump(platform, edge, lowerFloor, Index(lowerFloor, platform.End + _game.Tune.columnWidth / 2));
            }

            // The platform up level with the upper floor joins it.
            var level = Mathf.Abs(_game.Lift.Y - _game.Room.Upper) < .15f;
            if (level) Slump(platform, edge, upperFloor, 0);
            foreach (var s in Surfaces)
            {
                for (var pass = 0; pass < 4; pass++)
                for (var i = 0; i < s.Heights.Length - 1; i++)
                    Slump(s, i, s, i + 1);
                if (s.Id == SurfaceId.Lower)
                    for (var i = 0; i < s.Heights.Length; i++)
                    {
                        if (s.Start + i * _game.Tune.columnWidth < _game.Room.EntranceStart - 1e-6f || s.Heights[i] <= 0)
                            continue;
                        _game.Chute.Bunker += ToCoal(s.Heights[i]);
                        s.Heights[i] = 0;
                        Mark(s, i);
                    }

                // The upper floor sheds coal off both ends, the lift platform only off its open side. Joined
                // to a floor (see above), the platform and the floor are one surface, so neither sheds there:
                // resting, the platform shed onto the floor, the floor slumped it back into the shaft and the
                // platform picked it up again, round and round.
                if (s.Id != SurfaceId.Lower)
                    foreach (var i in s.Id == SurfaceId.Lift
                                 ? new[] { s.Heights.Length - 1 }
                                 : new[] { 0, s.Heights.Length - 1 })
                    {
                        if (s.Id == SurfaceId.Lift ? level || resting : level && i == 0) continue;
                        if (s.Heights[i] <= .22f) continue;
                        var q = Mathf.Min(.5f, ToCoal(s.Heights[i] - .22f));
                        s.Heights[i] -= ToHeight(q);
                        Mark(s, i);
                        // Straight past the edge with no scatter: scattered, it could land back where it fell
                        // from, and the platform and the floor would pass it back and forth for ever.
                        Emit(i == 0 ? s.Start - .15f : s.End + .15f, s.Y + .4f, q, i == 0 ? -1 : 1, 0, 0);
                    }

                if (s.Dirty.Count > 0) Rebuild(s);
            }
        }

        private static bool Solid(Surface s, int i)
        {
            return s.Heights[i] >= .05f;
        }

        // Rebuilds a floor's pile collider and column art. One quad per column, its top sloping to the
        // average height of each neighbour so the surface reads as a slope rather than steps.
        private void Rebuild(Surface s)
        {
            var w = _game.Tune.columnWidth;
            _shapes.Clear();
            for (var i = 0; i < s.Heights.Length; i++)
            {
                var h = s.Heights[i];
                var column = s.Columns[i];
                column.enabled = h >= .018f;
                if (!column.enabled) continue;
                var x = s.Start + i * w;
                column.transform.localPosition = new Vector3(x + w / 2, h / 2, 0);
                column.transform.localScale = new Vector3(w, h, 1);
                // Coal thinner than the step-up threshold is only drawn: a lip that low stopped the operator
                // dead (the step-up ignores anything under about 4 cm, and the body rests that far off the floor).
                if (!Solid(s, i)) continue;
                // Where the pile ends on bare floor it ramps down to nothing: a lip there, however thin,
                // stopped the operator dead against a sliver of coal.
                var left = i == 0 ? h : Solid(s, i - 1) ? (h + s.Heights[i - 1]) / 2 : 0;
                var right = i == s.Heights.Length - 1 ? h : Solid(s, i + 1) ? (h + s.Heights[i + 1]) / 2 : 0;
                var o = s.ShapeOrigin;
                _quad[0] = o + new Vector2(x, -.05f);
                _quad[1] = o + new Vector2(x + w, -.05f);
                _quad[2] = o + new Vector2(x + w, right);
                _quad[3] = o + new Vector2(x, left);
                _shapes.AddPolygon(_quad);
            }

            s.Collider.SetCustomShapes(_shapes);
            s.Dirty.Clear();
        }

        public class Surface
        {
            public readonly HashSet<int> Dirty = new();
            public CustomCollider2D Collider;
            public SpriteRenderer[] Columns;
            public Func<float> Floor; // Height of the floor itself.
            public float[] Heights;

            public SurfaceId Id;

            // CustomCollider2D shapes are placed relative to the attached Rigidbody2D (the world, for
            // floors without one), not to the collider's own transform. This is world (0, floor) in that space.
            public Vector2 ShapeOrigin;
            public float Start, End;
            public float Y => Floor();
        }

        private class Lump
        {
            public float Amount, Rest;
            public Rigidbody2D Body;
            public bool HitPlayer, HitHelper;
            public bool Active => Body.gameObject.activeSelf;
        }
    }
}
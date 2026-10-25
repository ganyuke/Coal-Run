using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// The operator. Walks with forces rather than by setting velocity, so the body still gets shoved
// around by the cart and the lift. Gripping the cart is a soft spring between the two bodies.
// Lane 0 is the work floor; lane 1 is behind the control room glass, where only terrain collides.
[RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
public class PlayerController : MonoBehaviour
{
    [SerializeField] InputReader input;
    [SerializeField] MachineInput machines;
    [Tooltip("Sorting group on the body art, pushed behind the glass in the control room.")]
    [SerializeField] SortingGroup visual;
    [SerializeField] int frontOrder = 20, backOrder = 4;

    public Rigidbody2D Body { get; private set; }
    public Vector2 Position => Body.position;
    public int Facing { get; private set; } = -1;
    public int Lane { get; private set; }
    public bool Holding => grip != null;
    public Machine Machine => machines.Active;
    public float Stagger { get; set; }
    // Coal on the shovel.
    public float Hands { get; private set; }
    public float Injury { get; private set; }
    public float Burial { get; private set; }

    BoxCollider2D box;
    SpringJoint2D grip;
    bool gripBlocked;
    ShiftManager game;

    void Awake()
    {
        Body = GetComponent<Rigidbody2D>();
        box = GetComponent<BoxCollider2D>();
    }

    void Start() => game = ShiftManager.Instance;

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        Stagger = Mathf.Max(0, Stagger - dt);
        Injury = Mathf.Max(0, Injury - dt * .8f);
        if (!game.CanAct) { ReleaseGrip(); return; }
        var c = input.Current;
        if (c.lane != 0) ChangeLane(c.lane > 0 ? 1 : 0);
        Move(c, dt);
        Shovel(c, dt);
        if (Lane == 1) Body.position = new Vector2(Mathf.Clamp(Body.position.x, 23.1f, 30.6f), Body.position.y);
        CheckBurial(dt);
    }

    void Move(Controls c, float dt)
    {
        var cart = game.Cart;
        var t = game.Tune;
        if (!c.grip) gripBlocked = false;
        if (c.move != 0) Facing = (int)Mathf.Sign(c.move);
        bool digging = Digging(c);
        Vector2 p = Body.position, cp = cart.Body.position;

        if (grip && (!c.grip || digging || Machine || Lane != 0 || cart.InCradle)) ReleaseGrip();
        if (!gripBlocked && !grip && c.grip && !digging && !Machine && Lane == 0 && CartInGrip() && !cart.InCradle && Mathf.Cos(cart.Angle) > .3f)
            TakeGrip();
        bool holding = grip != null;
        // The operator only bumps into the cart while holding it; otherwise they walk past it.
        box.excludeLayers = Lane == 1 ? ~GameLayers.Mask(GameLayers.Terrain) : holding ? 0 : GameLayers.Mask(GameLayers.Cart);

        float cold = 1; // Slows the operator when the building freezes; arrives with the furnace.
        float target = Machine || digging ? 0 : c.move * t.walkSpeed * cold * (Stagger > 0 ? .25f : 1);
        var v = Body.linearVelocity;
        // Walking into a low pile steps up onto it instead of stopping dead against it.
        if (c.move != 0 && !Machine && !digging && Mathf.Abs(v.y) < 1 && Mathf.Abs(v.x) < 2.5f)
        {
            float ahead = p.x + c.move * .36f;
            float step = game.Coal.GroundAt(ahead, p.y) - (p.y - .66f);
            if (step > .03f && step < .48f) Body.position = new Vector2(p.x, p.y + step + .035f);
        }
        // Coal around the cart slows the haul. Past 1.2 deep it doesn't move at all.
        float bury = Mathf.Max(0, 1 - cart.Burial / 1.2f);
        float effective = holding ? c.move * Mathf.Min(t.walkSpeed, 4.8f / (1 + cart.Coal * .038f)) * bury : target;
        Body.AddForce(new Vector2(Mathf.Clamp((effective - v.x) * Body.mass * 18, -800, 800), 0));

        if (holding && c.move != 0)
        {
            var cv = cart.Body.linearVelocity;
            // The pull is as hard as on a clear floor, so a part-buried cart crawls instead of sticking fast.
            float force = Mathf.Clamp((effective - cv.x) * 75 / Mathf.Max(bury, .2f), -340, 340);
            cart.Body.AddForceAtPosition(new Vector2(force, 0), cart.Body.GetRelativePoint(new Vector2(0, -.02f)));
            if (Mathf.Abs(p.x - cp.x) > 1.65f) Body.AddForce(new Vector2(-Mathf.Sign(p.x - cp.x) * 200, 0));
            game.Sound(cart.Wheels < 2 ? "scrape" : "roll", .12f, cart.Coal);
            Bulldoze(dt, (int)Mathf.Sign(c.move));
        }
        else if (c.move != 0 && !Machine && !digging) game.Sound("step", .28f);
        // Holding the cart keeps it upright and stops it when the operator stops.
        if (holding) cart.Body.AddTorque(-Mathf.Sin(cart.Angle) * cart.Body.mass * 22 - cart.AngularVelocity * cart.Body.mass * 5);
        if (holding && c.move == 0) cart.Body.AddForce(new Vector2(Mathf.Clamp(-cart.Body.linearVelocity.x * 300, -350, 350), 0));
    }

    bool Digging(Controls c) => c.shovel && Lane == 0 && !Machine;

    // The cart pushes loose coal ahead of it. That's harder work than shoveling it out of the way:
    // the pile pushes back, and past 1.2 high it doesn't budge.
    void Bulldoze(float dt, int direction)
    {
        var coal = game.Coal;
        var p = game.Cart.Body.position;
        var s = coal.SurfaceAt(p.x, p.y);
        int i = coal.Index(s, p.x + direction * 1f), j = i + direction;
        if (j < 0 || j >= s.heights.Length || s.heights[i] < .03f) return;
        game.Cart.Body.AddForce(new Vector2(-direction * s.heights[i] * 95, 0));
        if (s.heights[i] > 1.2f) return;
        coal.Shove(s, i, j, dt * .9f);
    }

    // Holding F fills the shovel from the coal within reach (the deepest spot around the cart first),
    // then throws it the way the operator faces: into the cart when it's there, onto the floor otherwise.
    void Shovel(Controls c, float dt)
    {
        var t = game.Tune;
        if (!Digging(c)) { ThrowHands(); return; }
        if (Hands >= t.handCapacity - .001f) { ThrowHands(); return; }
        var coal = game.Coal;
        Vector2 p = Body.position, cp = game.Cart.Body.position;
        spots.Clear();
        void Reach(float x, float y) { var s = coal.SurfaceAt(x, y); spots.Add((s, coal.Index(s, x))); }
        foreach (float dx in new[] { Facing * .65f, 0, -Facing * .65f }) Reach(p.x + dx, p.y);
        if (Mathf.Abs(cp.x - p.x) < 2.4f && Mathf.Abs(cp.y - p.y) < 2)
        {
            foreach (float x in new[] { cp.x - .6f, cp.x, cp.x + .6f }) if (Mathf.Abs(x - p.x) < 2.6f) Reach(x, cp.y);
            spots.Sort((a, b) => b.s.heights[b.i].CompareTo(a.s.heights[a.i]));
        }
        foreach (var (s, i) in spots)
        {
            if (s.heights[i] <= .001f) continue;
            Hands += coal.TakeFromPile(s, i, Mathf.Min(t.shovelRate * dt, t.handCapacity - Hands));
            game.Sound("shovel", .22f);
            return;
        }
        ThrowHands();
    }

    readonly List<(CoalField.Surface s, int i)> spots = new();

    // Empties the shovel. Coal a full cart can't take is thrown harder, so it clears the cart and lands beyond it.
    public void ThrowHands()
    {
        if (Hands <= 0) return;
        var cart = game.Cart;
        Vector2 p = Body.position, cp = cart.Body.position;
        float side = cp.x - p.x, speed = 4;
        bool nearCart = Mathf.Abs(side) < 2.2f && Mathf.Abs(cp.y - p.y) < 2;
        if (nearCart && Mathf.Cos(cart.Angle) > .7f && (Mathf.Abs(side) < .5f || Mathf.Sign(side) == Facing))
        {
            float amount = Mathf.Min(Hands, Mathf.Max(0, game.Tune.overfillCapacity - cart.Coal));
            cart.Coal += amount;
            Hands -= amount;
            speed = 6.5f;
        }
        if (Hands > 1e-6f) game.Coal.Emit(p.x + Facing * .7f, p.y + .7f, Hands, Facing * speed, speed * .75f);
        Hands = 0;
        game.Sound("coal");
    }

    public void Hurt(float amount)
    {
        Injury += amount;
        Stagger = .35f;
        game.Sound("hurt", .4f);
    }

    // Coal over the operator's head for five seconds kills; so does enough coal falling on them.
    void CheckBurial(float dt)
    {
        if (Lane != 0) return;
        var p = Body.position;
        float depth = game.Coal.GroundAt(p.x, p.y) - (p.y - .66f);
        if (grip && depth > .35f)
        {
            gripBlocked = true;
            ReleaseGrip();
            box.excludeLayers = GameLayers.Mask(GameLayers.Cart);
            game.Say("Too much coal underfoot to hold the cart. Shovel a path.", 3);
        }
        Burial = depth > 1.5f ? Burial + dt : Mathf.Max(0, Burial - dt * 2);
        if (Injury > 2.5f) Stagger = .8f;
        if (Burial > 5 || Injury > 7) game.Die(Burial > 5 ? "Buried under coal" : "Crushed by falling coal");
    }

    public void Respawn(Vector2 at)
    {
        ReleaseGrip();
        machines.Release();
        Hands = 0;
        Injury = Burial = Stagger = 0;
        Lane = 0;
        if (visual) visual.sortingOrder = frontOrder;
        Body.position = at;
        transform.position = at;
        Body.linearVelocity = Vector2.zero;
    }

    void TakeGrip()
    {
        Vector2 p = Body.position, cp = game.Cart.Body.position;
        grip = gameObject.AddComponent<SpringJoint2D>();
        grip.autoConfigureConnectedAnchor = false;
        grip.autoConfigureDistance = false;
        grip.connectedBody = game.Cart.Body;
        grip.anchor = Vector2.zero;
        grip.connectedAnchor = Vector2.zero;
        // Taking hold from a step away draws the operator in to arm's length.
        grip.distance = Mathf.Min(Vector2.Distance(p, cp), 1.75f);
        grip.frequency = 5;
        grip.dampingRatio = .95f;
        grip.enableCollision = true;
    }

    public void ReleaseGrip()
    {
        if (!grip) return;
        grip.enabled = false; // Destroy waits for the end of the frame; disabling takes effect this step.
        Destroy(grip);
        grip = null;
    }

    void ChangeLane(int lane)
    {
        if (!AtDoor || Lane == lane) return;
        // Stepping out of the control room into coal deep enough to bury the operator is refused.
        if (lane == 0 && DoorBlocked) { game.Say("Coal is piled against this door.", 3); return; }
        ThrowHands();
        ReleaseGrip();
        machines.Release();
        Lane = lane;
        if (visual) visual.sortingOrder = lane == 1 ? backOrder : frontOrder;
        game.Sound("step");
    }

    public bool AtDoor
    {
        get
        {
            var p = Body.position;
            return p.y < 4.3f && (p.x > 29.4f || p.x > 22.7f && p.x < 24.2f);
        }
    }

    bool DoorBlocked
    {
        get { var c = game.Coal; return c.PileHeight(c.Surfaces[0], Body.position.x) > 1; }
    }

    bool PileInReach()
    {
        var c = game.Coal;
        var p = Body.position;
        foreach (float dx in new[] { 0, .65f, -.65f })
            if (c.PileHeight(c.SurfaceAt(p.x + dx, p.y), p.x + dx) > .04f) return true;
        return false;
    }

    // The cart is taken hold of from either end, not from inside its footprint. 1.24 is the distance
    // between centres when the operator's body touches the end of the bin.
    public bool CartInGrip()
    {
        Vector2 p = Body.position, cp = game.Cart.Body.position;
        float dx = Mathf.Abs(p.x - cp.x);
        return dx > 1.05f && dx < 1.24f + game.Tune.gripReach && Mathf.Abs(p.y - cp.y) < 1.4f;
    }

    // Up to two key prompts for whatever is within reach, most urgent first.
    public List<(string key, string label)> Prompts()
    {
        var list = new List<(string, string)>();
        if (!game.CanAct || Machine) return list;
        if (Lane == 1)
        {
            if (AtDoor && !DoorBlocked) list.Add(("S", "Leave"));
            return list;
        }
        if (AtDoor) list.Add(("W", "Enter"));
        if (PileInReach()) list.Add(("F", "Shovel"));
        if (!grip && CartInGrip() && Mathf.Cos(game.Cart.Angle) > .3f) list.Add(("Space", "Grip cart"));
        foreach (var m in Machine.All)
            if (m is LiftCrank && m.CanOperate(this)) { list.Add(("W S", "Crank")); break; }
        if (list.Count > 2) list.RemoveRange(2, list.Count - 2);
        return list;
    }
}

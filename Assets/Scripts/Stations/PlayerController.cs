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
        if (!game.CanAct) { ReleaseGrip(); return; }
        var c = input.Current;
        if (c.lane != 0) ChangeLane(c.lane > 0 ? 1 : 0);
        Move(c);
        if (Lane == 1) Body.position = new Vector2(Mathf.Clamp(Body.position.x, 23.1f, 30.6f), Body.position.y);
    }

    void Move(Controls c)
    {
        var cart = game.Cart;
        var t = game.Tune;
        if (!c.grip) gripBlocked = false;
        if (c.move != 0) Facing = (int)Mathf.Sign(c.move);
        bool digging = false; // Shoveling arrives with the coal field.
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
        }
        else if (c.move != 0 && !Machine && !digging) game.Sound("step", .28f);
        // Holding the cart keeps it upright and stops it when the operator stops.
        if (holding) cart.Body.AddTorque(-Mathf.Sin(cart.Angle) * cart.Body.mass * 22 - cart.AngularVelocity * cart.Body.mass * 5);
        if (holding && c.move == 0) cart.Body.AddForce(new Vector2(Mathf.Clamp(-cart.Body.linearVelocity.x * 300, -350, 350), 0));
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
            if (AtDoor) list.Add(("S", "Leave"));
            return list;
        }
        if (AtDoor) list.Add(("W", "Enter"));
        if (!grip && CartInGrip() && Mathf.Cos(game.Cart.Angle) > .3f) list.Add(("Space", "Grip cart"));
        foreach (var m in Machine.All)
            if (m is LiftCrank && m.CanOperate(this)) { list.Add(("W S", "Crank")); break; }
        if (list.Count > 2) list.RemoveRange(2, list.Count - 2);
        return list;
    }
}

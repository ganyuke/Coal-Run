using System.Collections.Generic;
using UnityEngine;

// Hand-cranked freight lift: a kinematic platform. Speed follows how fast the wheel is spun; the
// load only adds resistance. It refuses to come down on anything in the shaft, and refuses to
// rise with something hanging off its open side into the upper floor.
[RequireComponent(typeof(Rigidbody2D))]
public class Lift : MonoBehaviour
{
    [SerializeField] InputReader input;
    [SerializeField] LiftCrank[] cranks;
    [SerializeField] Transform wheelVisual;

    // Height of the platform's top surface.
    public float Y { get; private set; }
    public float Speed { get; private set; }

    Rigidbody2D body;
    ShiftManager game;
    readonly List<Collider2D> colliders = new();

    void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        Y = body.position.y + .15f;
    }

    void Start() => game = ShiftManager.Instance;

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        var t = game.Tune;
        var player = game.Player;

        float requested = 0;
        var spun = System.Array.Find(cranks, c => c.InUse);
        if (spun) requested = Mathf.Clamp(spun.Rate, -22, 22);
        else if (input.Current.crankKey != 0 && !player.Machine && player.Lane == 0 && game.CanAct && CrankInReach(player))
            requested = input.Current.crankKey * t.liftKeyCrank;
        requested *= t.liftGear / (1 + Load() / 90);

        float floor = Floor(out var under), ceiling = Ceiling(out var hangingOff);
        if (requested < 0 && floor > Room.Lower + .01f && Y - floor < .05f)
            game.Say(under == "operator" ? "Stand clear of the shaft." : $"The {under} is under the lift.", 2);
        if (requested > 0 && ceiling < Room.Upper - .01f && ceiling - Y < .05f)
            game.Say(hangingOff == "operator" ? "Stand fully on the lift or clear of it." : $"The {hangingOff} is hanging off the side of the lift.", 2);

        // The fastest speed from which the platform can still brake to a stop in the distance left.
        float distance = requested > 0 ? ceiling - Y : Y - floor;
        float brake = t.liftAcceleration * dt / 2;
        float limit = Mathf.Min(t.liftMaxSpeed, Mathf.Max(0, Mathf.Sqrt(2 * t.liftAcceleration * Mathf.Max(0, distance) + brake * brake) - brake));
        float target = Mathf.Clamp(requested, -limit, limit);
        Speed += Mathf.Clamp(target - Speed, -t.liftAcceleration * dt, t.liftAcceleration * dt);
        float next = Mathf.Clamp(Y + Speed * dt, floor, ceiling);
        Speed = (next - Y) / dt;

        if (wheelVisual) wheelVisual.Rotate(0, 0, -Speed * dt / 1.4f * Mathf.Rad2Deg);
        if (Mathf.Abs(Speed) > .02f) game.Sound("crank", .1f, Mathf.Abs(Speed));

        // Going down, whatever stands on the platform is carried with it instead of floating off.
        if (Speed < 0)
            foreach (var (rider, _) in Riders())
            {
                var p = rider.position;
                if (Mathf.Abs(p.x - Room.LiftX) < 1.55f && Mathf.Abs(p.y - Y - .65f) < .25f)
                    rider.linearVelocity = new Vector2(rider.linearVelocity.x, Speed);
            }

        body.MovePosition(new Vector2(body.position.x, next - .15f));
        Y = next;
    }

    bool CrankInReach(PlayerController player) => System.Array.Exists(cranks, c => c.CanOperate(player));

    IEnumerable<(Rigidbody2D body, string name)> Riders()
    {
        yield return (game.Cart.Body, "cart");
        if (game.Player.Body.simulated && game.Player.Lane == 0) yield return (game.Player.Body, "operator");
    }

    float Load()
    {
        float mass = 0;
        foreach (var (rider, _) in Riders())
        {
            var p = rider.position;
            if (Mathf.Abs(p.x - Room.LiftX) < 1.6f && Mathf.Abs(p.y - Y) < 2) mass += rider.mass;
        }
        return mass;
    }

    // The lowest the platform may go without coming down on the cart, the operator, or a part.
    float Floor(out string blocker)
    {
        float floor = Room.Lower;
        blocker = "";
        foreach (var (rider, name) in Riders())
        {
            float half = name == "cart" ? 1.02f : .25f, top = name == "cart" ? .75f : .7f;
            var p = rider.position;
            float clear = p.y + top + .35f;
            if (p.x + half > .5f && p.x - half < 4 && p.y < Y - .25f && clear > floor) { floor = clear; blocker = name; }
        }
        return Mathf.Min(floor, Y);
    }

    // The highest the platform may go with something hanging off its open side, which would otherwise
    // be driven into the upper floor. From the bottom it barely leaves the ground, so the reason shows at once.
    float Ceiling(out string blocker)
    {
        float ceiling = Room.Upper;
        blocker = "";
        foreach (var (rider, name) in Riders())
        {
            var b = BodyBounds(rider);
            if (!(b.max.x > 4.1f && b.min.x < 3.9f && b.min.y > Y - .4f && b.max.y < Room.Upper - .05f)) continue;
            float limit = Y < Room.Lower + .35f ? Room.Lower + .3f : Y + Room.Upper - .25f - b.max.y;
            if (limit < ceiling) { ceiling = limit; blocker = name; }
        }
        return Mathf.Max(ceiling, Y);
    }

    Bounds BodyBounds(Rigidbody2D rider)
    {
        rider.GetAttachedColliders(colliders);
        var bounds = new Bounds(rider.position, Vector3.zero);
        foreach (var c in colliders) if (c.enabled) bounds.Encapsulate(c.bounds);
        return bounds;
    }
}

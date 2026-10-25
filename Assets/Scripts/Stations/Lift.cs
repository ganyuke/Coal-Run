using System.Collections.Generic;
using CoalRun.Core;
using UnityEngine;

namespace CoalRun.Stations
{
    // Hand-cranked freight lift: a kinematic platform. Speed follows how fast the wheel is spun; the
    // load only adds resistance. Cranked down onto something in the shaft, or up with something hanging
    // off its open side into the upper floor, it jams against it with a bang: a cart has coal jolted out,
    // and the operator is hurt, and crushed if the cranking carries on. It's kinematic, so it stops short
    // of actually pushing into them (that would squeeze bodies through floors). Coal in the shaft it
    // squashes flat as it comes down, slowly, and stops on the flattened layer.
    [RequireComponent(typeof(Rigidbody2D))]
    public class Lift : MonoBehaviour
    {
        [SerializeField] private InputReader input;
        [SerializeField] private LiftCrank[] cranks;

        // Height of the platform's top surface.
        public float Y { get; private set; }
        public float Speed { get; private set; }

        private Rigidbody2D _body;
        private ShiftManager _game;
        private readonly List<Collider2D> _colliders = new();
        // The crank that last drove the lift. Its wheel turns with the platform, coasting included.
        private LiftCrank _driver;

        private void Awake()
        {
            _body = GetComponent<Rigidbody2D>();
            Y = _body.position.y + .15f;
        }

        private void Start() => _game = ShiftManager.Instance;

        private void FixedUpdate()
        {
            var dt = Time.fixedDeltaTime;
            var t = _game.Tune;
            var player = _game.Player;

            float requested = 0;
            var spun = System.Array.Find(cranks, c => c.InUse);
            if (spun)
            {
                requested = Mathf.Clamp(spun.Rate, -22, 22);
                _driver = spun;
            }
            else if (input.Current.CrankKey != 0 && !player.Machine && player.Lane == 0 && _game.CanAct && CrankInReach(player) is { } keyed)
            {
                requested = input.Current.CrankKey * t.liftKeyCrank;
                _driver = keyed;
            }
            requested *= t.liftGear / (1 + Load() / 90);

            float floor = Floor(out var under), ceiling = Ceiling(out var hangingOff);
            switch (requested)
            {
                case < 0 when floor > Room.Lower + .01f && Y - floor < .05f:
                    _game.Say(under == "operator" ? "Stand clear of the shaft." : $"The {under} is under the lift.", 2);
                    break;
                case > 0 when ceiling < Room.Upper - .01f && ceiling - Y < .05f:
                    _game.Say(hangingOff == "operator" ? "Stand fully on the lift or clear of it." : $"The {hangingOff} is hanging off the side of the lift.", 2);
                    break;
            }

            // Cranking on into something it's jammed against: the cart takes no more harm, the operator does.
            switch (requested)
            {
                case < 0 when under == "operator" && Y - floor < .02f:
                    player.Hurt(t.liftCrush * Time.fixedDeltaTime, "Crushed by the lift", false);
                    break;
                case > 0 when hangingOff == "operator" && ceiling - Y < .02f:
                    player.Hurt(t.liftCrush * Time.fixedDeltaTime, "Crushed against the floor", false);
                    break;
            }

            // The fastest speed from which the platform can still brake to a stop in the distance left.
            // It only brakes for the ends of the shaft: something in the way is hit at full speed.
            var distance = requested > 0 ? Room.Upper - Y : Y - Room.Lower;
            var brake = t.liftAcceleration * dt / 2;
            var limit = Mathf.Min(t.liftMaxSpeed, Mathf.Max(0, Mathf.Sqrt(2 * t.liftAcceleration * Mathf.Max(0, distance) + brake * brake) - brake));
            var target = Mathf.Clamp(requested, -limit, limit);
            Speed += Mathf.Clamp(target - Speed, -t.liftAcceleration * dt, t.liftAcceleration * dt);
            // Coming down into coal in the shaft, it bogs down at once, then slower as the coal packs flat.
            if (Speed < 0 && Y + Speed * dt < Room.Lower + _game.Coal.ShaftPeak())
                Speed = Mathf.Max(Speed, -t.liftSquashSpeed * Mathf.Clamp01((Y - floor) / .5f) - .05f);
            var next = Y + Speed * dt;
            if (next > ceiling) { Jam(Speed, hangingOff); next = ceiling; }
            if (next < floor) { Jam(-Speed, under); next = floor; }
            Speed = (next - Y) / dt;
            // Down in its pit, the platform's top is flush with the floor, so the coal under it has Y - Lower of room.
            _game.Coal.Squash(next - Room.Lower);

            if (_driver) _driver.Turn(-Speed * dt / 1.4f * Mathf.Rad2Deg);
            if (Mathf.Abs(Speed) > .02f) _game.Sound("crank", .1f, Mathf.Abs(Speed));

            // Going down, whatever stands on the platform is carried with it instead of floating off.
            if (Speed < 0)
                foreach (var (rider, _) in Riders())
                {
                    var p = rider.position;
                    if (Mathf.Abs(p.x - Room.LiftX) < 1.55f && Mathf.Abs(p.y - Y - .65f) < .25f)
                        rider.linearVelocity = new Vector2(rider.linearVelocity.x, Speed);
                }

            _body.MovePosition(new Vector2(_body.position.x, next - .15f));
            Y = next;
        }

        // Running into something in the way. Slow enough, it just stops.
        private void Jam(float speed, string what)
        {
            if (speed < .3f) return;
            _game.Sound("jam", .3f, speed);
            switch (what)
            {
                case "cart": _game.Cart.Knock(speed * _game.Tune.liftKnock); break;
                case "operator": _game.Player.Hurt(speed * _game.Tune.liftHit, Speed < 0 ? "Crushed by the lift" : "Crushed against the floor"); break;
            }
        }

        private LiftCrank CrankInReach(PlayerController player) => System.Array.Find(cranks, c => c.CanOperate(player));

        private IEnumerable<(Rigidbody2D body, string kind)> Riders()
        {
            yield return (_game.Cart.Body, "cart");
            if (_game.Player.Body.simulated && _game.Player.Lane == 0) yield return (_game.Player.Body, "operator");
        }

        private float Load()
        {
            float mass = 0;
            foreach (var (rider, _) in Riders())
            {
                var p = rider.position;
                if (Mathf.Abs(p.x - Room.LiftX) < 1.6f && Mathf.Abs(p.y - Y) < 2) mass += rider.mass;
            }
            return mass + _game.Coal.LiftCoal * _game.Tune.coalMass;
        }

        // The lowest the platform may go without coming down on the cart, the operator, or a part.
        private float Floor(out string blocker)
        {
            var floor = Room.Lower;
            blocker = "";
            foreach (var (rider, kind) in Riders())
            {
                float half = kind == "cart" ? 1.02f : .25f, top = kind == "cart" ? .75f : .7f;
                var p = rider.position;
                // The platform's underside, .3 below its top, touching the top of the cart or the operator's head.
                var clear = p.y + top + .32f;
                if (!(p.x + half > .5f && p.x - half < 4 && p.y < Y - .25f && clear > floor)) continue;
                floor = clear;
                blocker = kind;
            }
            // Coal in the shaft can be squashed flat, but no further. A thin layer is just shoved out.
            var layer = Room.Lower + _game.Coal.ShaftLayer();
            if (layer > Room.Lower + .1f && layer > floor)
            {
                floor = layer;
                blocker = "coal";
            }
            return Mathf.Min(floor, Y);
        }

        // The highest the platform may go with something hanging off its open side, which would otherwise
        // be driven into the upper floor. From the bottom, whatever hangs off tips and slides off onto the
        // lower floor as it rises.
        private float Ceiling(out string blocker)
        {
            var ceiling = Room.Upper;
            blocker = "";
            foreach (var (rider, kind) in Riders())
            {
                var b = BodyBounds(rider);
                if (!(b.max.x > 4.1f && b.min.x < 3.9f && b.min.y > Y - .4f && b.max.y < Room.Upper - .05f)) continue;
                // The upper floor's slab is .4 thick; the overhang stops just under it.
                var limit = Y + Room.Upper - .42f - b.max.y;
                if (limit >= ceiling) continue;
                ceiling = limit;
                blocker = kind;
            }
            return Mathf.Max(ceiling, Y);
        }

        private Bounds BodyBounds(Rigidbody2D rider)
        {
            rider.GetAttachedColliders(_colliders);
            var bounds = new Bounds(rider.position, Vector3.zero);
            foreach (var c in _colliders) if (c.enabled) bounds.Encapsulate(c.bounds);
            return bounds;
        }
    }
}

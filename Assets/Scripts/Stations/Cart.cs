using System.Collections.Generic;
using CoalRun.Core;
using UnityEngine;

namespace CoalRun.Stations
{
    // The cart. A dynamic body whose colliders are rebuilt whenever a wheel comes or goes, and whose
    // mass follows the coal in it. With one wheel the back end drags on a high-friction skid.
    [RequireComponent(typeof(Rigidbody2D))]
    public class Cart : MonoBehaviour
    {
        [SerializeField] private GameObject frontWheel, backWheel, skid;
        [Tooltip("Scaled vertically with the load.")]
        [SerializeField]
        private Transform fill;
        [Tooltip("At the bottom of the skid. Emits while the skid scrapes along the floor, more the faster it goes.")]
        [SerializeField]
        private ParticleSystem sparks;
        [SerializeField] private float sparksPerMetre = 18;
        [Tooltip("Optional. Looped while the skid scrapes; louder the faster it goes.")]
        [SerializeField]
        private AudioSource scrape;

        public Rigidbody2D Body { get; private set; }
        public int Wheels { get; private set; }
        public float Coal { get; set; }
        public float Angle => Body.rotation * Mathf.Deg2Rad;
        public float AngularVelocity => Body.angularVelocity * Mathf.Deg2Rad;
        // Set while the tip cradle holds the cart.
        public TipCradle Cradle { get; set; }
        public bool InCradle => Cradle;
        // How deep the bin sits in coal.
        public float Burial => _game.Coal.CartBurial(this);
        // How fast the skid is sliding along the floor; 0 when it's off the floor or there's no skid.
        private float Scraping { get; set; }
        // The skid end held up off the floor, wheelbarrow style.
        public bool Heaving => _heaveTarget > 0;
        public bool SkidClear => _skidCollider && !_skidCollider.IsTouchingLayers(GameLayers.Mask(GameLayers.Terrain));
        // On its side or upside down: too far over to hold, so it has to be righted (E).
        public bool Overturned => !InCradle && Mathf.Cos(Angle) < .7f;
        // Half the cart's width as it lies, from its bin: 1 upright, less on its side.
        public float HalfWidth => _colliders.Count > 0 ? _colliders[0].bounds.extents.x : 1;
        // Which side of the cart the skid is on, in world x: -1 or 1.
        public float SkidSide => Mathf.Sign(Body.GetRelativeVector(Vector2.left).x);

        private ShiftManager _game;
        private readonly List<Collider2D> _colliders = new();
        private Collider2D _skidCollider;

        private PhysicsMaterial2D _binMaterial, _wheelMaterial, _skidMaterial, _dragMaterial;
        // How far up the operator is trying to lift the skid end, in radians, and whether they did this step.
        private float _heaveTarget, _spillAccumulator;

        private bool _heaved;
        // Whether the skid touched the floor last step, and how fast it was coming down, for the jolt.
        private bool _skidWasDown = true;
        private float _skidFall;

        private void Awake()
        {
            Body = GetComponent<Rigidbody2D>();
            Body.useAutoMass = false;
            _binMaterial = new PhysicsMaterial2D("Cart bin") { friction = .55f };
            // Wheels and skid slide without friction; GroundFriction stands in for it. Contact friction acts
            // at the floor, below the centre of mass, and rocks the cart over its wheel: pulled skid-first
            // it digs the skid in until a full cart won't move at all.
            _wheelMaterial = new PhysicsMaterial2D("Cart wheel") { friction = 0 };
            _skidMaterial = new PhysicsMaterial2D("Cart skid") { friction = 0 };
            _dragMaterial = new PhysicsMaterial2D("Cart no wheels") { friction = 0 };
        }

        private void Start()
        {
            _game = ShiftManager.Instance;
            Wheels = _game.Day.startingWheels;
            Rebuild(false);
        }

        private void FixedUpdate()
        {
            UpdateMass();
            if (!_heaved) _heaveTarget = 0;
            _heaved = false;
            GroundFriction(Time.fixedDeltaTime);
            Spill(Time.fixedDeltaTime);
            // Only steel on the floor sparks; dragging through coal doesn't.
            var onFloor = _skidCollider && _skidCollider.IsTouchingLayers(GameLayers.Mask(GameLayers.Terrain));
            var skidVelocity = Body.GetRelativePointVelocity(new Vector2(-.58f, -.64f));
            Scraping = onFloor ? Mathf.Abs(skidVelocity.x) : 0;
            if (onFloor && !_skidWasDown && Wheels == 1) Jolt(_skidFall);
            _skidWasDown = onFloor;
            _skidFall = -skidVelocity.y;
            if (sparks)
            {
                var emission = sparks.emission;
                emission.rateOverTime = Scraping > .3f ? Scraping * sparksPerMetre : 0;
            }
            if (!scrape) return;
            scrape.volume = Mathf.Clamp01(Scraping / 3);
            switch (Scraping)
            {
                case > .3f when !scrape.isPlaying: scrape.Play(); break;
                case <= .3f when scrape.isPlaying: scrape.Stop(); break;
            }
        }

        private void Update()
        {
            if (!fill) return;
            var f = Mathf.Clamp01(Coal / _game.Tune.overfillCapacity);
            fill.localScale = new Vector3(fill.localScale.x, f * .7f, 1);
            fill.gameObject.SetActive(f > .01f);
        }

        // Rolling resistance on wheels, a steel skid dragging under the missing one: a purely sideways
        // force through the centre of mass, so it slows the cart without tipping it.
        private void GroundFriction(float dt)
        {
            var t = _game.Tune;
            var grounded = false;
            foreach (var c in _colliders)
                if (c != _colliders[0] && c.IsTouchingLayers(GameLayers.Mask(GameLayers.Terrain))) { grounded = true; break; }
            if (!grounded) return;
            // Wheels share the weight evenly, so a lone skid carries half of it. Heaved clear, it carries none.
            var mu = Wheels >= 2 || Wheels == 1 && SkidClear ? t.rollingFriction : Wheels == 1 ? (t.rollingFriction + t.skidFriction) / 2 : t.dragFriction;
            var vx = Body.linearVelocity.x;
            var most = mu * Body.mass * -Physics2D.gravity.y * Mathf.Cos(Angle);
            // Never more than it takes to stop the cart this step, or it would push it back the other way.
            Body.AddForce(new Vector2(-Mathf.Sign(vx) * Mathf.Min(most, Body.mass * Mathf.Abs(vx) / dt), 0));
        }

        // Called every step the operator holds the skid end of a one-wheel cart. Holding Shift raises it;
        // letting go drops it, so a high heave lands hard (see Jolt). It's lifted by the rim the operator holds, so the cart rocks up onto
        // its wheel; the force is capped, so a full cart is slow to come up.
        public void Heave(bool lifting, float dt)
        {
            if (Wheels != 1) return;
            var t = _game.Tune;
            _heaved = true;
            // A heavier cart comes up (and goes down) slower.
            float lift = Lift, rate = t.heaveRate * Mathf.Deg2Rad * t.cartMass / Body.mass;
            // The target never runs far ahead of the cart, so a heavy one doesn't fly up once it gets going.
            _heaveTarget = lifting ? Mathf.Min(_heaveTarget + rate * dt, lift + .1f, t.heaveMax * Mathf.Deg2Rad) : 0;
            if (_heaveTarget <= 0) return;
            // Enough to hold the cart's weight about its wheel (.58 from the middle; the hands are 1 from it,
            // the other way), plus a spring toward the target.
            var liftRate = -AngularVelocity;
            var force = Body.mass * (-Physics2D.gravity.y * .58f / 1.58f + (_heaveTarget - lift) * 120 - liftRate * 17);
            var hands = Body.GetRelativePoint(new Vector2(-1, .64f));
            Body.AddForceAtPosition(new Vector2(0, Mathf.Clamp(force, 0, t.heaveForce)), hands);
        }

        // How far the skid end (local -x) is raised, in radians: turning clockwise lifts it.
        private float Lift => -Angle;

        // A yank bounces the skid off the floor for a moment (turning clockwise lifts it), and the cart
        // rolls on its wheel until it comes down. A heavier cart hops less.
        public void Hop()
        {
            if (Wheels != 1) return;
            Body.angularVelocity -= _game.Tune.lurchHop * _game.Tune.cartMass / Body.mass;
        }

        // Tipped far enough, the load slides out over the low end of the bin. The fuller the cart, the
        // less it takes: coal heaped above the rim goes first.
        private float SpillAngle => Mathf.Lerp(_game.Tune.spillAngleEmpty, _game.Tune.spillAngleFull, Coal / _game.Tune.overfillCapacity);

        private void Spill(float dt)
        {
            var t = _game.Tune;
            // Measured from how the cart rests: with no wheels it sits 23° over on its bin corner, and its
            // coal stays put. The cradle holds it by its middle, so there it's measured from level.
            var over = Mathf.Abs(Angle) * Mathf.Rad2Deg - (Wheels == 0 && !InCradle ? 23 : 0) - SpillAngle;
            if (over <= 0 || Coal <= 0) { _spillAccumulator = 0; return; }
            _spillAccumulator += Mathf.Min(Coal, t.spillRate * over / 10 * dt);
            if (_spillAccumulator < .25f) return;
            var q = Mathf.Min(Coal, _spillAccumulator);
            _spillAccumulator -= q;
            Coal -= q;
            // The rim at the low end: rotating clockwise (negative) lowers the +x end.
            float low = Angle < 0 ? 1 : -1;
            // In the cradle, what pours over the hopper side goes into the hopper; what doesn't fit goes over.
            if (Cradle) q -= Cradle.Pour(q, low);
            if (q <= 0) return;
            var lip = Body.GetRelativePoint(new Vector2(low * 1.08f, .6f));
            var v = Body.linearVelocity + Body.GetRelativeVector(new Vector2(low * 1.5f, 0));
            _game.Coal.Emit(lip.x, lip.y, q, v.x, v.y, .3f);
            _game.Sound("spill", .3f);
        }

        // Stands the cart back up on its wheels where it lies, as the JS rightCart does: no animation yet.
        public void Right()
        {
            var p = Body.position;
            var floor = _game.Coal.SurfaceAt(p.x, p.y).Floor();
            Body.position = new Vector2(Mathf.Clamp(p.x, 1.45f, 30.55f), floor + .7f);
            Body.rotation = 0;
            Body.linearVelocity = Vector2.zero;
            Body.angularVelocity = 0;
            transform.SetPositionAndRotation(Body.position, Quaternion.identity);
            _game.Sound("repair");
        }

        // The skid coming down hard bounces coal out over both rims: dropping it from high costs coal,
        // lowering it gently or keeping it barely off the floor doesn't. Heaped coal goes first.
        private void Jolt(float speed) => Knock(speed - _game.Tune.joltSpeed);

        // A knock of `speed` m/s (the lift jamming against it, a hard landing) bounces coal out.
        public void Knock(float speed)
        {
            var t = _game.Tune;
            var q = Mathf.Min(Coal, t.joltSpill * speed * Coal / t.overfillCapacity);
            if (q <= .01f) return;
            Coal -= q;
            foreach (var end in new[] { -1f, 1f })
            {
                var lip = Body.GetRelativePoint(new Vector2(end * 1.08f, .7f));
                var v = Body.linearVelocity + Body.GetRelativeVector(new Vector2(end * 1.2f, 2.5f));
                _game.Coal.Emit(lip.x, lip.y, q / 2, v.x, v.y, .3f);
            }
            _game.Sound("jolt", .2f, q);
        }

        public void SetWheels(int wheels)
        {
            Wheels = Mathf.Clamp(wheels, 0, 2);
            Rebuild(true);
            _game.Sound("repair");
        }

        private void Rebuild(bool clearWheelOverlap)
        {
            // Disabled first: Destroy only happens at the end of the frame, after this physics step.
            foreach (var c in _colliders) { c.enabled = false; Destroy(c); }
            _colliders.Clear();
            _skidCollider = null;
            var noParts = GameLayers.Mask(GameLayers.Part);

            var bin = gameObject.AddComponent<PolygonCollider2D>();
            bin.points = new Vector2[] { new(-.83f, -.12f), new(.83f, -.12f), new(1, .64f), new(-1, .64f) };
            bin.sharedMaterial = _binMaterial;
            _colliders.Add(bin);
            if (Wheels >= 1) _colliders.Add(Wheel(.58f));
            if (Wheels >= 2) _colliders.Add(Wheel(-.58f));
            else
            {
                var box = gameObject.AddComponent<BoxCollider2D>();
                // Reaches as low as the wheel, so the cart sits level and the skid always drags. (The JS skid
                // stopped .11 short, and pulling lifted it clear of the floor.)
                box.offset = new Vector2(-.58f, -.36f);
                box.size = new Vector2(.32f, .56f);
                box.sharedMaterial = Wheels > 0 ? _skidMaterial : _dragMaterial;
                box.excludeLayers = noParts;
                _colliders.Add(box);
                _skidCollider = box;
            }

            if (frontWheel) frontWheel.SetActive(Wheels >= 1);
            if (backWheel) backWheel.SetActive(Wheels >= 2);
            if (skid) skid.SetActive(Wheels < 2);
            UpdateMass();

            if (!clearWheelOverlap) return;
            // A newly fitted wheel can reach below the skid that held the bin up, so lift the cart just
            // enough to clear the floor.
            float rise = 0;
            foreach (var c in _colliders)
                if (c is CircleCollider2D)
                {
                    var center = Body.GetRelativePoint(c.offset);
                    rise = Mathf.Max(rise, FloorBelow(center) + .325f - center.y);
                }
            if (rise > 0)
            {
                Body.position += new Vector2(0, rise);
                Body.linearVelocity = new Vector2(Body.linearVelocity.x, Mathf.Max(0, Body.linearVelocity.y));
            }
            Body.WakeUp();
            return;

            CircleCollider2D Wheel(float x)
            {
                var wheel = gameObject.AddComponent<CircleCollider2D>();
                wheel.offset = new Vector2(x, -.34f);
                wheel.radius = .3f;
                wheel.sharedMaterial = _wheelMaterial;
                wheel.excludeLayers = noParts;
                return wheel;
            }
        }

        private float FloorBelow(Vector2 p)
        {
            return p switch
            {
                { x: >= .55f and <= 3.95f } when p.y >= _game.Lift.Y - .3f => _game.Lift.Y,
                { x: >= 4 and <= Room.Edge, y: >= Room.Upper - .3f } => Room.Upper,
                _ => Room.Lower
            };
        }

        private void UpdateMass()
        {
            var t = _game.Tune;
            var mass = t.cartMass + Coal * t.coalMass;
            Body.mass = mass;
            Body.centerOfMass = new Vector2(0, Coal * .012f);
            Body.inertia = mass * .62f;
        }
    }
}

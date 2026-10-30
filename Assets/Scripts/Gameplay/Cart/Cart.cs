using System.Collections.Generic;
using System.Collections;
using CoalRun.Core;
using CoalRun.Gameplay.Lift;
using UnityEngine;

namespace CoalRun.Gameplay.Cart
{
    // The cart. Wheel and skid colliders switch with its wheel count; mass follows the coal load.
    // GroundFriction supplies the resistance of rolling wheels and a dragging skid.
    [RequireComponent(typeof(Rigidbody2D))]
    [UnityEngine.Scripting.APIUpdating.MovedFrom("CoalRun.Stations")]
    public class Cart : MonoBehaviour
    {
        [SerializeField] private GameObject frontWheel, backWheel, skid;

        [Tooltip("The wheels' radius in metres: a wheel turns once for every 2 pi radius the cart travels.")]
        [SerializeField]
        private float wheelRadius = .29f;

        [Tooltip(
            "Columns across the inside of the bin, left to right, drawn as a heap of the load (see Update). Only a picture: the load is still the one number, Coal.")]
        [SerializeField]
        private Transform[] heap;

        [Tooltip("At the bottom of the skid. Emits while the skid scrapes along the floor, more the faster it goes.")]
        [SerializeField]
        private ParticleSystem sparks;

        [SerializeField] private float sparksPerMetre = 18;

        [Tooltip("Optional. Looped while the skid scrapes; louder the faster it goes.")] [SerializeField]
        private AudioSource scrape;

        [Tooltip("A wheel off the cart, spawned when one pops off in a crash or the operator puts one down.")]
        [SerializeField]
        private Rigidbody2D wheelPrefab;

        private readonly List<Collider2D> _colliders = new();

        [Header("Physics colliders")]
        [SerializeField] private PolygonCollider2D binCollider;
        [SerializeField] private CircleCollider2D frontWheelCollider, backWheelCollider;
        [SerializeField] private BoxCollider2D skidCollider;

        private ShiftManager _game;

        // The heap's column heights, eased toward their shape so the coal sloshes rather than jumps.
        private float[] _heap;

        // How far up the operator is trying to lift the skid end, in radians, and whether they did this step.
        private float _heaveTarget, _spillAccumulator;

        private bool _heaved;

        // The hardest hit since last step, handled in FixedUpdate rather than mid-contact, since a crash
        // switches the active colliders. Hits right after a crash or a fitting don't count again.
        private float _impact, _crashCooldown;

        private bool _liftAsked;

        // The lift: the point it turns about (the far wheel's foot), the pose it started from, its rotation
        // now and the one asked for this step, in degrees, and the wheels it had when it was picked up.
        private Vector2 _liftPivot, _liftFrom;
        private float _liftRotation, _liftAngle, _liftTarget;
        private int _liftWheels;

        // The wheels turn with the cart's travel along its length, as if rolling on the floor. The skid doesn't.
        private float _roll;
        private Collider2D _skidCollider;

        private float _skidFall;

        // Whether the skid touched the floor last step, and how fast it was coming down, for the jolt.
        private bool _skidWasDown = true;

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
        public bool Overturned => !InCradle && !Righting && Mathf.Cos(Angle) < .7f;

        // Being heaved back onto its wheels (see Right).
        public bool Righting { get; private set; }

        // One end held up off the floor by the operator while a wheel is fitted (see HoldUp).
        public bool Lifted { get; private set; }

        // Held still where it stands, for the tutorial (see Park).
        public bool Parked { get; private set; }

        // Moved by hand rather than by physics, or not at all: no gripping, hooking on or spilling meanwhile.
        public bool Busy => Righting || Lifted || Parked;

        // Half the cart's width as it lies, from its bin: 1 upright, less on its side.
        public float HalfWidth => _colliders.Count > 0 ? _colliders[0].bounds.extents.x : 1;

        // Which side of the cart the skid is on, in world x: -1 or 1.
        public float SkidSide => Mathf.Sign(Body.GetRelativeVector(Vector2.left).x);

        // Wheels lying about the room, off the cart.
        public List<Rigidbody2D> LooseWheels { get; } = new();

        // Why a wheel can't be bolted on right now, or null if it can.
        public string WheelBlocker =>
            Wheels >= 2 ? _game.Lines.speech.bothWheels :
            Parked ? _game.Lines.speech.notNow :
            InCradle ? _game.Lines.speech.offCradleFirst :
            Mathf.Cos(Angle) < .87f ? _game.Lines.speech.standCartFirst :
            !Lifted && Body.linearVelocity.magnitude > 1 ? _game.Lines.speech.cartMoving : null;

        // How far the skid end (local -x) is raised, in radians: turning clockwise lifts it.
        private float Lift => -Angle;

        // Tipped far enough, the load slides out over the low end of the bin. The fuller the cart, the
        // less it takes: coal heaped above the rim goes first.
        private float SpillAngle => Mathf.Lerp(_game.Tune.spillAngleEmpty, _game.Tune.spillAngleFull,
            Coal / _game.Tune.overfillCapacity);

        private void Awake()
        {
            Body = GetComponent<Rigidbody2D>();
        }

        private void Start()
        {
            _game = ShiftManager.Instance;
            Wheels = _game.Day.startingWheels;
            UpdateWheelState(false);
        }

        // A full cart (cartCapacity) fills the bin flat to the rim; less is a mound in the middle, more heaps
        // above the rim. The top stays roughly level with the world as the cart tilts, so the coal slides to
        // the low end, the way it's about to pour out.
        private void Update()
        {
            Roll();
            if (heap == null || heap.Length == 0) return;
            const float bottom = -.06f, depth = .7f, halfWidth = .85f;
            _heap ??= new float[heap.Length];
            var n = heap.Length;
            var level = Coal / _game.Tune.cartCapacity * depth;
            // A pile that's mostly mound when there's little coal, flattening out as the bin fills, then
            // mounding again once it's heaped.
            var mound = Mathf.Min(level, .22f) *
                        (Coal > _game.Tune.cartCapacity ? 1.4f : Mathf.Lerp(1, .35f, level / depth));
            var slope = -Mathf.Tan(Mathf.Clamp(Angle, -.7f, .7f));
            var width = halfWidth * 2 / n;
            for (var i = 0; i < n; i++)
            {
                var u = (i + .5f) / n * 2 - 1;
                // 1 - u² averages 2/3 across the bin, so taking that off keeps the area the same.
                var target = Coal < .02f
                    ? 0
                    : Mathf.Clamp(level + mound * (1 - u * u - 2f / 3) + slope * u * halfWidth, 0, depth + .35f);
                _heap[i] = Mathf.Lerp(_heap[i], target, 1 - Mathf.Exp(-12 * Time.deltaTime));
                var h = _heap[i];
                heap[i].gameObject.SetActive(h > .01f);
                heap[i].localPosition = new Vector3(-halfWidth + (i + .5f) * width, bottom + h / 2, 0);
                heap[i].localScale = new Vector3(width + .01f, h, 1);
            }
        }

        private void FixedUpdate()
        {
            UpdateMass();
            if (Lifted) StepLift(Time.fixedDeltaTime);
            _crashCooldown -= Time.fixedDeltaTime;
            if (_impact >= _game.Tune.crashSpeed && _crashCooldown <= 0) Crash(_impact);
            _impact = 0;
            if (!_heaved) _heaveTarget = 0;
            _heaved = false;
            GroundFriction(Time.fixedDeltaTime);
            if (!Busy) Spill(Time.fixedDeltaTime);
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

            // Heard through the AudioDirector: the skid dragging, and the wheels rolling along the floor.
            if (Scraping > .3f) _game.PlayCue("scrape", .05f, Scraping);
            var rolling = Wheels > 0 && !Busy && Mathf.Abs(Body.linearVelocity.y) < .6f
                ? Mathf.Abs(Vector2.Dot(Body.linearVelocity, Body.transform.right))
                : 0;
            if (rolling > .3f) _game.PlayCue("roll", .05f, rolling);
            if (!scrape) return;
            scrape.volume = Mathf.Clamp01(Scraping / 3);
            switch (Scraping)
            {
                case > .3f when !scrape.isPlaying: scrape.Play(); break;
                case <= .3f when scrape.isPlaying: scrape.Stop(); break;
            }
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            var layer = collision.collider.gameObject.layer;
            if (InCradle || layer == GameLayers.Player || layer == GameLayers.Part) return;
            // Only the speed straight into the surface: sliding along a floor isn't a crash.
            for (var i = 0; i < collision.contactCount; i++)
                _impact = Mathf.Max(_impact,
                    Mathf.Abs(Vector2.Dot(collision.relativeVelocity, collision.GetContact(i).normal)));
        }

        private void Roll()
        {
            if (Righting || Lifted || Parked) return;
            var along = Vector2.Dot(Body.linearVelocity, Body.transform.right);
            _roll -= along / wheelRadius * Mathf.Rad2Deg * Time.deltaTime;
            var turn = Quaternion.Euler(0, 0, _roll);
            if (frontWheel) frontWheel.transform.localRotation = turn;
            if (backWheel) backWheel.transform.localRotation = turn;
        }

        // Rolling resistance on wheels, a steel skid dragging under the missing one: a purely sideways
        // force through the centre of mass, so it slows the cart without tipping it.
        private void GroundFriction(float dt)
        {
            var t = _game.Tune;
            var grounded = false;
            foreach (var c in _colliders)
                if (c != _colliders[0] && c.IsTouchingLayers(GameLayers.Mask(GameLayers.Terrain)))
                {
                    grounded = true;
                    break;
                }

            if (!grounded) return;
            // Wheels share the weight evenly, so a lone skid carries half of it. Heaved clear, it carries none.
            var mu = Wheels >= 2 || (Wheels == 1 && SkidClear) ? t.rollingFriction :
                Wheels == 1 ? (t.rollingFriction + t.skidFriction) / 2 : t.dragFriction;
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

        // A yank bounces the skid off the floor for a moment (turning clockwise lifts it), and the cart
        // rolls on its wheel until it comes down. A heavier cart hops less.
        public void Hop()
        {
            if (Wheels != 1) return;
            Body.angularVelocity -= _game.Tune.lurchHop * _game.Tune.cartMass / Body.mass;
        }

        private void Spill(float dt)
        {
            var t = _game.Tune;
            // Measured from how the cart rests: with no wheels it sits 23° over on its bin corner, and its
            // coal stays put. The cradle holds it by its middle, so there it's measured from level.
            var over = Mathf.Abs(Angle) * Mathf.Rad2Deg - (Wheels == 0 && !InCradle ? 23 : 0) - SpillAngle;
            if (over <= 0 || Coal <= 0)
            {
                _spillAccumulator = 0;
                return;
            }

            _spillAccumulator += Mathf.Min(Coal, t.spillRate * over / 10 * dt);
            if (_spillAccumulator < .25f) return;
            var q = Mathf.Min(Coal, _spillAccumulator);
            _spillAccumulator -= q;
            Coal -= q;
            // The rim at the low end: rotating clockwise (negative) lowers the +x end.
            float low = Angle < 0 ? 1 : -1;
            var lip = Body.GetRelativePoint(new Vector2(low * 1.08f, .6f));
            var v = Body.linearVelocity + Body.GetRelativeVector(new Vector2(low * 1.5f, 0));
            _game.Coal.Emit(lip.x, lip.y, q, v.x, v.y, .3f);
            _game.PlayCue("spill", .3f);
        }

        // Stands the cart back up on its wheels where it lies, as the JS rightCart does.
        public void Right()
        {
            if (!Righting) StartCoroutine(Heave());
        }

        public void Park(bool parked)
        {
            Parked = parked;
            Body.bodyType = parked ? RigidbodyType2D.Kinematic : RigidbodyType2D.Dynamic;
            Body.linearVelocity = Vector2.zero;
            Body.angularVelocity = 0;
        }

        // Tipped back up over half a second rather than snapped upright: lifted a little on the way so it
        // clears the floor, turning the short way round. Held kinematic, so nothing knocks it off course and
        // the coal doesn't spill as it passes through steep angles.
        private IEnumerator Heave()
        {
            Righting = true;
            _game.PlayCue("repair");
            var p = Body.position;
            var floor = _game.Coal.SurfaceAt(p.x, p.y).Floor();
            Vector2 to = new(Mathf.Clamp(p.x, 1.45f, 30.55f), floor + .7f);
            float from = Body.rotation, upright = Mathf.Round(from / 360) * 360;
            Body.bodyType = RigidbodyType2D.Kinematic;
            Body.linearVelocity = Vector2.zero;
            Body.angularVelocity = 0;
            for (float k = 0; k < 1;)
            {
                yield return new WaitForFixedUpdate();
                k = Mathf.Min(1, k + Time.fixedDeltaTime / .5f);
                var e = k * k * (3 - 2 * k);
                Body.MovePosition(Vector2.Lerp(p, to, e) + new Vector2(0, .35f * Mathf.Sin(k * Mathf.PI)));
                Body.MoveRotation(Mathf.Lerp(from, upright, e));
            }

            yield return new WaitForFixedUpdate();
            Body.bodyType = RigidbodyType2D.Dynamic;
            Body.rotation = 0;
            _crashCooldown = 1;
            Righting = false;
        }

        // The skid coming down hard bounces coal out over both rims: dropping it from high costs coal,
        // lowering it gently or keeping it barely off the floor doesn't. Heaped coal goes first.
        private void Jolt(float speed)
        {
            Knock(speed - _game.Tune.joltSpeed);
        }

        // A knock of `speed` m/s (the lift jamming against it, a hard landing) bounces coal out.
        public void Knock(float speed)
        {
            var t = _game.Tune;
            var q = Mathf.Min(Coal, t.joltSpill * speed * Coal / t.overfillCapacity);
            if (q <= 0) return;
            Coal -= q;
            foreach (var end in new[] { -1f, 1f })
            {
                var lip = Body.GetRelativePoint(new Vector2(end * 1.08f, .7f));
                var v = Body.linearVelocity + Body.GetRelativeVector(new Vector2(end * 1.2f, 2.5f));
                _game.Coal.Emit(lip.x, lip.y, q / 2, v.x, v.y, .3f);
            }

            _game.PlayCue("jolt", .2f, q);
        }

        public void SetWheels(int wheels)
        {
            Wheels = Mathf.Clamp(wheels, 0, 2);
            UpdateWheelState(true);
            _crashCooldown = 1;
            _game.PlayCue("repair");
        }

        // Asked every step while the operator works a wheel on: turns the cart about the other end until the
        // end missing the wheel is `degrees` above level. Measured from level rather than from how it lay,
        // since a cart with no wheels rests nose down. Once nobody asks, it's let down gently: level if the
        // wheel went on, back as it lay if not, and then it's back to physics.
        public void HoldUp(int slot, float degrees)
        {
            if (!Lifted)
            {
                Lifted = true;
                Body.bodyType = RigidbodyType2D.Kinematic;
                Body.linearVelocity = Vector2.zero;
                Body.angularVelocity = 0;
                _liftFrom = Body.position;
                _liftRotation = _liftAngle = Body.rotation;
                _liftPivot = Body.GetRelativePoint(new Vector2(slot == 0 ? -.58f : .58f, -.64f));
                _liftWheels = Wheels;
            }

            // The front slot is on the right: raising it turns the cart anticlockwise.
            _liftTarget = (slot == 0 ? 1 : -1) * degrees;
            _liftAsked = true;
        }

        private void StepLift(float dt)
        {
            var asked = _liftAsked;
            if (!asked) _liftTarget = Wheels > _liftWheels ? 0 : _liftRotation;
            _liftAsked = false;
            _liftAngle = Mathf.MoveTowards(_liftAngle, _liftTarget, 45 * dt);
            Body.MovePosition(_liftPivot +
                              (Vector2)(Quaternion.Euler(0, 0, _liftAngle - _liftRotation) * (_liftFrom - _liftPivot)));
            Body.MoveRotation(_liftAngle);
            if (asked || !Mathf.Approximately(_liftAngle, _liftTarget)) return;
            Body.bodyType = RigidbodyType2D.Dynamic;
            Body.linearVelocity = Vector2.zero;
            Body.angularVelocity = 0;
            _crashCooldown = 1;
            Lifted = false;
        }

        // Where a wheel goes on the cart: the front one first, then the back.
        public Vector2 WheelSlot(int index)
        {
            return Body.GetRelativePoint(new Vector2(index == 0 ? .58f : -.58f, -.34f));
        }

        // Hitting hard pops off the last wheel fitted. Coal isn't thrown out here: a cart that lands tilted
        // spills by itself.
        private void Crash(float speed)
        {
            _crashCooldown = 1.2f;
            _game.PlayCue("crash", .4f, speed);
            PopWheel();
        }

        // The last wheel fitted comes off and bounces away as a loose wheel.
        public void PopWheel()
        {
            if (Wheels == 0) return;
            var slot = WheelSlot(Wheels - 1);
            var away = Mathf.Sign(slot.x - Body.position.x);
            Wheels--;
            UpdateWheelState(false);
            DropWheel(slot, Body.linearVelocity * .3f + new Vector2(away * 2, 4));
            _game.Say(_game.Lines.speech.wheelOff);
        }

        public void DropWheel(Vector2 at, Vector2 velocity)
        {
            var wheel = Instantiate(wheelPrefab, at, Quaternion.identity);
            wheel.linearVelocity = velocity;
            LooseWheels.Add(wheel);
        }

        public void RemoveWheel(Rigidbody2D wheel)
        {
            LooseWheels.Remove(wheel);
            Destroy(wheel.gameObject);
        }

        private void UpdateWheelState(bool clearWheelOverlap)
        {
            frontWheelCollider.enabled = Wheels >= 1;
            backWheelCollider.enabled = Wheels >= 2;
            skidCollider.enabled = Wheels < 2;
            _skidCollider = skidCollider.enabled ? skidCollider : null;

            _colliders.Clear();
            _colliders.Add(binCollider);
            if (frontWheelCollider.enabled) _colliders.Add(frontWheelCollider);
            if (backWheelCollider.enabled) _colliders.Add(backWheelCollider);
            if (skidCollider.enabled) _colliders.Add(skidCollider);

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
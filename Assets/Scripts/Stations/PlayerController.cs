using System.Collections;
using System.Collections.Generic;
using CoalRun.Core;
using UnityEngine.Rendering;
using UnityEngine;

namespace CoalRun.Stations
{
    // The operator. Walks with forces rather than by setting velocity, so the body still gets shoved
    // around by the cart and the lift. Gripping the cart is a soft spring between the two bodies.
    // Lane 0 is the work floor; lane 1 is the control room up the stairs, where only terrain collides.
    [RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
    public partial class PlayerController : MonoBehaviour
    {
        [SerializeField] private InputReader input;
        [SerializeField] private MachineInput machines;
        [Tooltip("Sorting group on the body art, pushed behind the glass in the control room.")]
        [SerializeField]
        private SortingGroup visual;
        [SerializeField] private int frontOrder = 20, backOrder = 4;
        [Tooltip("What the operator says, as little text above their head.")]
        [SerializeField]
        private Speaker voice;
        [Tooltip("Sorting order while riding down the staff lift: behind its door (-16), in front of the car (-18).")]
        [SerializeField]
        private int liftOrder = -17;
        [Tooltip("The wheel in the operator's hands, shown while carrying one.")]
        [SerializeField]
        private Transform heldWheel;
        [Tooltip("Flint and steel, shown while striking at the firebox to light it.")]
        [SerializeField]
        private Transform striker;
        [Tooltip("Sparks thrown off each strike, toward the firebox.")]
        [SerializeField]
        private ParticleSystem strikeSparks;
        [Tooltip("The shovel in the operator's hands, pivoting at the hands with the blade pointing down its local -y. Shown while digging or carrying coal.")]
        [SerializeField]
        private Transform heldShovel;
        [Tooltip("The black heap on the blade, scaled with the coal on it.")]
        [SerializeField]
        private Transform shovelLoad;
        [Tooltip("Where the shovel reaches, from the feet, facing right (mirrored facing left): coal piled into it can be dug, the tallest column first.")]
        [SerializeField]
        private Rect scoopBox = new(-.15f, -.3f, 1.45f, 1.7f);
        [Tooltip("Optional. Outlines the scoop box while F is held, for tuning it. Brighter with coal in it.")]
        [SerializeField]
        private LineRenderer scoopOutline;

        [Header("Shovel arms")]
        [Tooltip("The body sprite, swapped for the armless one while the shovel is out so its arms can be swung onto the handle.")]
        [SerializeField]
        private SpriteRenderer bodyArt;
        [SerializeField] private Sprite armlessBody;
        [Tooltip("Pivots at the shoulders, each with an arm sprite hanging down its local -y, `armLength` long.")]
        [SerializeField]
        private Transform frontArm, backArm;
        [SerializeField] private float armLength = .4f;
        [Tooltip("Nods a beat behind the body as it walks.")]
        [SerializeField]
        private Transform head;
        [Tooltip("The shoulders, in the art's space facing right (mirrored facing left): front arm, back arm.")]
        [SerializeField]
        private Vector2 frontShoulder = new(.1f, .2f), backShoulder = new(-.08f, .2f);
        [Tooltip("How far down the handle from the top hand the lower hand grips.")]
        [SerializeField]
        private float gripSpacing = .3f;

        [Header("Walking")]
        [Tooltip("Metres a step: the bob comes round once a step, so it keeps time with however fast they go.")]
        [SerializeField]
        private float stepLength = .55f;
        [SerializeField] private float stepBob = .045f;
        [Tooltip("Degrees leaned into the walk at full pace.")]
        [SerializeField]
        private float walkLean = 4;
        [Tooltip("How far behind the body the head is in each step, as a fraction of a step: it nods rather than riding along stiff.")]
        [SerializeField]
        private float headLag = .15f;

        public Rigidbody2D Body { get; private set; }
        public Speaker Voice => voice;
        // Out in the room, not dead or waiting out of the world.
        public bool Visible => visual && visual.gameObject.activeInHierarchy;
        public Vector2 Position => Body.position;
        public int Facing { get; private set; } = -1;
        public int Lane { get; private set; }
        // Holding the cart. Not a joint: a joint would also hold the cart up, and then a missing wheel's
        // skid never touches the floor. The grip only works along the floor (see Hold).
        public bool Holding { get; private set; }
        public Machine Machine => machines.Active;
        private float Stagger { get; set; }
        // Coal on the shovel.
        public float Hands { get; private set; }
        public float Injury { get; private set; }
        public float Burial { get; private set; }
        // A wheel off the cart, in hands. Hands full: no gripping, shoveling, levers or doors.
        // Coal shoveled up this shift; the tutorial watches it.
        public float Scooped { get; private set; }
        public bool CarryingWheel { get; private set; }
        // A body carried in the wheel's place: heavier, slower and shorter to throw (see PickUpBody).
        private Rigidbody2D _carriedBody;
        private CorpseBody _carriedCorpse;
        // Going up or down the control room stairs (see Climb).
        public bool Climbing { get; private set; }

        private BoxCollider2D _box;
        // How far the operator may be from touching the cart; closes to zero right after taking hold.
        private float _slack;
        // Centre to centre when the operator's hands are on the bin's rim (cart half width 1 + operator .24).
        private const float Touching = 1.26f;

        // Seconds E has been held to bolt the wheel on, and whether the wheel is on its way into the hands
        // or from them onto the cart (see Fly).
        private float _fit;
        private bool _flying;
        // Striking at the firebox this step (see Furnace.Kindle), and the time of the last strike.
        private bool _striking;
        private float _lastStrike;

        private bool _gripBlocked;
        // A double-tap dash (not holding the cart) or lurch (holding it): time left, direction, and how
        // far ahead the cart was last step, to tell running into it from walking out of it.
        private float _dashFor, _lurchFor, _dashCooldown, _lastAhead, _dashStart;
        [Tooltip("Dust kicked up off the floor behind a dash or a lurch.")]
        [SerializeField]
        private ParticleSystem dashDust;
        private bool _ramming;
        private int _dashDir;
        private ShiftManager _game;

        private void Awake()
        {
            Body = GetComponent<Rigidbody2D>();
            _box = GetComponent<BoxCollider2D>();
            if (head) _headRest = head.localPosition;
            if (bodyArt) _armedBody = bodyArt.sprite;
        }

        private void Start() => _game = ShiftManager.Instance;

        private void FixedUpdate()
        {
            var dt = Time.fixedDeltaTime;
            Stagger = Mathf.Max(0, Stagger - dt);
            _dashFor = Mathf.Max(0, _dashFor - dt);
            _lurchFor = Mathf.Max(0, _lurchFor - dt);
            _dashCooldown = Mathf.Max(0, _dashCooldown - dt);
            Injury = Mathf.Max(0, Injury - dt * .8f);
            if (!_game.CanAct) { ReleaseGrip(); _striking = false; _fit = 0; return; }
            if (Climbing) return;
            var c = input.Current;
            if (c.Lane != 0) ChangeLane(c.Lane > 0 ? 1 : 0);
            // At the firebox, E is held to light the fire rather than pressed.
            var atFirebox = _game.Furnace.CanLight(this);
            _striking = _game.Furnace.Kindle(atFirebox && c.InteractHeld, dt);
            if (_striking) Facing = (int)Mathf.Sign(_game.Furnace.transform.position.x - Body.position.x);
            if (c.Interact && !atFirebox) Interact();
            if (CarryingWheel && !_flying && c.Click) ThrowWheel(c.Aim);
            Fit(c, dt);
            Move(c, dt);
            Effects(c, dt);
            Shovel(c, dt);
            if (Lane == 1) Body.position = new Vector2(Mathf.Clamp(Body.position.x, Room.BoothStart + .3f, Room.Width - .55f), Body.position.y);
            CheckBurial(dt);
        }

        // Freezing hurries the operator up rather than slowing them down: they're already about to die, so
        // making them slower too would punish them twice. Up to freezeRush faster as the grace runs out.
        private float Rush => _game.Freezing ? 1 + _game.Tune.freezeRush * _game.Chill : 1;

        private void Move(Controls c, float dt)
        {
            var cart = _game.Cart;
            var t = _game.Tune;
            if (!c.Grip) _gripBlocked = false;
            var digging = Digging(c);
            _digging = digging;
            // With the shovel out they face the cursor, even walking backwards; otherwise the way they walk.
            if (digging && Mathf.Abs(c.Aim.x - Body.position.x) > .05f) Facing = (int)Mathf.Sign(c.Aim.x - Body.position.x);
            else if (c.Move != 0) Facing = (int)Mathf.Sign(c.Move);
            Vector2 p = Body.position, cp = cart.Body.position;

            if (Holding && (!c.Grip || digging || Machine || Lane != 0 || cart.InCradle || cart.Busy || CarryingWheel)) ReleaseGrip();
            if (!_gripBlocked && !Holding && c.Grip && !digging && !Machine && !CarryingWheel && Lane == 0 && CartInGrip() && !cart.InCradle && !cart.Busy && Mathf.Cos(cart.Angle) > .3f)
                TakeGrip();
            if (c.Dash != 0) Dash(c.Dash);
            else if (_dashFor > 0) DashCancel(c);
            var holding = Holding;
            if (holding) Hold(ref p, dt);
            // From the skid end of a one-wheel cart, Shift lifts it off the floor like a wheelbarrow.
            if (holding && cart.Wheels == 1 && Mathf.Approximately(Mathf.Sign(p.x - cp.x), cart.SkidSide)) cart.Heave(c.Heave, dt);
            // The operator never bumps into the cart: they walk past it, and while holding it Hold does the
            // pushing. A body contact would push on the bin's top corner and lever it over its wheel.
            _box.excludeLayers = Lane == 1 ? ~GameLayers.Mask(GameLayers.Terrain) : GameLayers.Mask(GameLayers.Cart);

            var target = c.Move * t.walkSpeed * (_carriedBody ? .6f : 1) * (Machine ? t.machineWalk : digging ? t.digWalk : 1) * Rush * (Stagger > 0 ? .25f : 1) * _game.Tutorial.WalkFactor;
            // The dash speeds up as it goes (a run-up), set directly so it follows the ramp exactly.
            if (_dashFor > 0)
            {
                var ramp = Mathf.Lerp(_dashStart, t.dashSpeed, 1 - _dashFor / t.dashTime);
                if (Random.value < .6f) DashDust(1, .7f);
                Body.linearVelocity = new Vector2(_dashDir * Mathf.Max(ramp, Body.linearVelocity.x * _dashDir), Body.linearVelocity.y);
                target = Body.linearVelocity.x;
            }
            var v = Body.linearVelocity;
            // Walking into a low pile steps up onto it instead of stopping dead against it.
            if (c.Move != 0 && !Machine && Mathf.Abs(v.y) < 1 && Mathf.Abs(v.x) < 2.5f)
            {
                var ahead = p.x + c.Move * .36f;
                var step = _game.Coal.WalkGroundAt(ahead, p.y) - (p.y - .66f);
                if (step is > .005f and < .48f) Body.position = new Vector2(p.x, p.y + step + .035f);
            }
            // Coal around the cart slows the haul. Past 1.2 deep it doesn't move at all.
            var bury = Mathf.Max(0, 1 - cart.Burial / 1.2f);
            // A dragging skid makes it slow work: the operator can't haul it at walking pace however hard they
            // push. Heaved off the floor, it nearly rolls like a two-wheel cart.
            var wheels = cart.Wheels >= 2 ? 1 : cart.Wheels == 1 ? cart.SkidClear ? t.heaveHaul : t.oneWheelHaul : t.noWheelHaul;
            var effective = holding ? c.Move * Mathf.Min(t.walkSpeed, 4.8f / (1 + cart.Coal * .038f)) * wheels * bury * Rush : target;
            // Mid-lurch, neither the operator nor the haul force holds the cart back to the hauling pace.
            if (holding && _lurchFor > 0) effective = _dashDir * Mathf.Max(effective * _dashDir, cart.Body.linearVelocity.x * _dashDir);
            Body.AddForce(new Vector2(Mathf.Clamp((effective - v.x) * Body.mass * 18, -800, 800), 0));
            Tether(c, p, v);
            // The ram stays live while the dash's speed carries on past its end, or a cart just out of
            // dash range would be run straight through at nearly full speed.
            if (_ramming && (_dashFor > 0 || Body.linearVelocity.x * _dashDir > t.walkSpeed) && !holding && !cart.InCradle) Ram(p);
            else _ramming = false;

            if (holding && (c.Move != 0 || _lurchFor > 0))
            {
                var cv = cart.Body.linearVelocity;
                // The pull is as hard as on a clear floor, so a part-buried cart crawls instead of sticking fast.
                var force = Mathf.Clamp((effective - cv.x) * t.haulGain / Mathf.Max(bury, .2f), -t.haulForce, t.haulForce);
                // Through the centre of mass: a purely sideways shove. Anywhere else it would rock the cart
                // over its wheel, loading or unloading the skid depending on the direction.
                cart.Body.AddForce(new Vector2(force, 0));
                _game.Sound(cart.Wheels < 2 ? "scrape" : "roll", .12f, cart.Coal);
                Bulldoze(dt, (int)Mathf.Sign(effective));
            }
            else if (c.Move != 0 && !Machine && !digging) _game.Sound("step", .28f);
            // Holding the cart keeps it from tipping over and stops it when the operator stops. A few degrees
            // of lean are left alone, so a one-wheel cart still rests on its skid.
            // Not mid-lurch or with the skid in the air either, or it would hold the hopping skid down and
            // soften a dropped one's landing.
            if (holding && !cart.Heaving && _lurchFor <= 0 && !(cart.Wheels == 1 && cart.SkidClear))
            {
                var lean = Mathf.Sign(cart.Angle) * Mathf.Max(0, Mathf.Abs(cart.Angle) - .12f);
                cart.Body.AddTorque(-Mathf.Sin(lean) * cart.Body.mass * 22 - cart.AngularVelocity * cart.Body.mass * 5);
            }
            if (holding && c.Move == 0 && _lurchFor <= 0) cart.Body.AddForce(new Vector2(Mathf.Clamp(-cart.Body.linearVelocity.x * 300, -350, 350), 0));
        }

        // Coal kicked up where it's walked on or pushed through, and the hurt of hitting the floor hard. The
        // dust is the lift's, so squashed coal and trampled coal look alike.
        private void Effects(Controls c, float dt)
        {
            var t = _game.Tune;
            var v = Body.linearVelocity;
            // A sudden stop from a fast fall is a landing; the lift's own braking is gentler than that.
            if (Body.bodyType == RigidbodyType2D.Dynamic && _lastFall < -t.safeFall && v.y - _lastFall > 5)
            {
                var hurt = (-_lastFall - t.safeFall) * t.fallHurt;
                Hurt(hurt, "Fell from the upper floor");
                _game.Say(_game.Lines.speech.fell, 3);
            }
            _lastFall = v.y;

            if (Lane != 0 || Body.bodyType != RigidbodyType2D.Dynamic) return;
            Vector2 p = Body.position;
            var surface = _game.Coal.SurfaceAt(p.x, p.y);
            var pile = _game.Coal.PileHeight(surface, p.x);
            var feet = new Vector2(p.x, _game.Coal.GroundAt(p.x, p.y));
            // Walking through coal throws it up behind them, more the deeper it is and the faster they go.
            if (pile > .05f && Mathf.Abs(v.x) > .6f && Mathf.Abs(v.y) < 1)
            {
                _footDust += Mathf.Abs(v.x) * (1 + pile * 6) * 2 * dt;
                for (; _footDust >= 1; _footDust--)
                    _game.CoalDust(feet, new Vector2(-Mathf.Sign(v.x) * Random.Range(.2f, 1.2f), Random.Range(.3f, 1.1f)), Random.Range(.06f, .12f), Random.Range(.5f, .9f));
            }
            // Hauling a cart that's stuck in it: coal heaves up around its end, so it's plain what's holding it.
            var cart = _game.Cart;
            if (Holding && c.Move != 0 && cart.Burial > .3f && Mathf.Abs(cart.Body.linearVelocity.x) < .8f)
            {
                _stuckDust += (14 + cart.Burial * 30) * dt;
                var side = Mathf.Sign(c.Move);
                for (; _stuckDust >= 1; _stuckDust--)
                {
                    var x = cart.Body.position.x + side * Random.Range(.7f, 1.1f);
                    _game.CoalDust(new Vector2(x, _game.Coal.GroundAt(x, cart.Body.position.y)), new Vector2(Random.Range(-1.2f, 1.2f) - side * .5f, Random.Range(1.5f, 3.5f)), Random.Range(.1f, .2f), Random.Range(.7f, 1.2f));
                }
            }
        }

        private float _lastFall, _footDust, _stuckDust;

        private bool CartNear() => Vector2.Distance(Body.position, _game.Cart.Body.position) < 2.6f;

        private bool CartOverturnedInReach() => _game.Cart.Overturned && CartNear();

        // Walking while holding a machine works it (a floor lever follows the operator), but only within
        // the machine's walk range.
        private void Tether(Controls c, Vector2 p, Vector2 v)
        {
            var m = Machine;
            if (m != _held)
            {
                _held = m;
                _grabbedAt = _lastX = p.x;
            }
            if (!m) return;
            var range = m.WalkRange(_grabbedAt);
            if (p.x < range.x || p.x > range.y)
            {
                p.x = Mathf.Clamp(p.x, range.x, range.y);
                Body.position = p;
                Body.linearVelocity = new Vector2(0, v.y);
            }
            // Only the distance actually walked counts, not pushing against the end of the range.
            if (c.Move != 0) m.Walk(p.x - _lastX);
            _lastX = p.x;
        }

        private Machine _held;
        private float _grabbedAt, _lastX;

        private void TakeGrip()
        {
            Vector2 p = Body.position, cp = _game.Cart.Body.position;
            Holding = true;
            _slack = Mathf.Max(0, Mathf.Abs(p.x - cp.x) - Touching);
        }

        // Keeps the operator's hands on the rim, exactly touching: taking hold a step away draws them in,
        // and after that walking away pulls the cart and walking into it pushes it. Purely sideways and
        // through the cart's centre of mass, so it never lifts or rocks the cart.
        private void Hold(ref Vector2 p, float dt)
        {
            var cart = _game.Cart.Body;
            _slack = Mathf.MoveTowards(_slack, 0, 2.5f * dt);
            float side = Mathf.Sign(p.x - cart.position.x), distance = Mathf.Abs(p.x - cart.position.x);
            var far = Touching + _slack;
            if (distance >= Touching && distance <= far) return;
            var tooFar = distance > far;
            p.x = cart.position.x + side * (tooFar ? far : Touching);
            Body.position = p;
            // Share the speed they're parting (or closing) at out by mass, as an inelastic collision would.
            var apart = (Body.linearVelocity.x - cart.linearVelocity.x) * side;
            if (tooFar ? apart <= 0 : apart >= 0) return;
            var impulse = apart / (1 / Body.mass + 1 / cart.mass);
            Body.linearVelocity -= new Vector2(side * impulse / Body.mass, 0);
            cart.linearVelocity += new Vector2(side * impulse / cart.mass, 0);
        }

        public void ReleaseGrip()
        {
            Holding = false;
        }

        private void ChangeLane(int lane)
        {
            if (!AtDoor || Lane == lane) return;
            if (CarryingWheel) { _game.Say(_game.Lines.speech.handsFull, 2); return; }
            ThrowHands();
            ReleaseGrip();
            machines.Release();
            StartCoroutine(Climb(lane));
        }

        // Up or down the stairs in one quick glide, kinematic so nothing on the way knocks the operator
        // off: the foot of the stairs, the top, then a step in through the door (or the same backwards).
        private IEnumerator Climb(int lane)
        {
            Climbing = true;
            _game.Sound("step");
            float lower = Room.Lower + .72f, upper = Room.BoothFloor + .72f;
            Vector2[] path = { new(Room.StairsFoot, lower), new(Room.BoothStart, upper), new(Room.BoothStart + .6f, upper) };
            if (lane == 0)
            {
                // Coal piled at the foot of the stairs is stepped down onto, not into.
                path[0].y = _game.Coal.GroundAt(Room.StairsFoot, lower) + .72f;
                System.Array.Reverse(path);
                Lane = 0;
                if (visual) visual.sortingOrder = frontOrder;
            }
            path[0] = Body.position;
            Facing = lane == 1 ? 1 : -1;
            Body.bodyType = RigidbodyType2D.Kinematic;
            Body.linearVelocity = Vector2.zero;
            var length = Vector2.Distance(path[0], path[1]) + Vector2.Distance(path[1], path[2]);
            var time = Mathf.Max(.3f, length / 12);
            for (float k = 0; k < 1;)
            {
                yield return new WaitForFixedUpdate();
                k = Mathf.Min(1, k + Time.fixedDeltaTime / time);
                // Eased at both ends, then walked along the two legs by distance.
                var d = k * k * (3 - 2 * k) * length;
                var first = Vector2.Distance(path[0], path[1]);
                Body.MovePosition(d < first ? Vector2.Lerp(path[0], path[1], d / first) : Vector2.Lerp(path[1], path[2], (d - first) / (length - first)));
            }
            yield return new WaitForFixedUpdate();
            Body.bodyType = RigidbodyType2D.Dynamic;
            Body.linearVelocity = Vector2.zero;
            if (lane == 1)
            {
                Lane = 1;
                if (visual) visual.sortingOrder = backOrder;
            }
            Climbing = false;
        }

        // At the foot of the stairs on the work floor, or at the control room door up top.
        private bool AtDoor => Lane == 0
            ? Body.position is { y: < 4.3f } p && Mathf.Abs(p.x - Room.StairsFoot) < .8f
            : Body.position.x < Room.BoothStart + 1.2f;
    }
}

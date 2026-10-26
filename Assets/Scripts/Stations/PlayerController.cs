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
    public class PlayerController : MonoBehaviour
    {
        [SerializeField] private InputReader input;
        [SerializeField] private MachineInput machines;
        [Tooltip("Sorting group on the body art, pushed behind the glass in the control room.")]
        [SerializeField]
        private SortingGroup visual;
        [SerializeField] private int frontOrder = 20, backOrder = 4;
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

        public Rigidbody2D Body { get; private set; }
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
        private bool _ramming;
        private int _dashDir;
        private ShiftManager _game;

        private void Awake()
        {
            Body = GetComponent<Rigidbody2D>();
            _box = GetComponent<BoxCollider2D>();
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

            var target = c.Move * t.walkSpeed * (Machine ? t.machineWalk : digging ? t.digWalk : 1) * Rush * (Stagger > 0 ? .25f : 1) * _game.Tutorial.WalkFactor;
            // The dash speeds up as it goes (a run-up), set directly so it follows the ramp exactly.
            if (_dashFor > 0)
            {
                var ramp = Mathf.Lerp(_dashStart, t.dashSpeed, 1 - _dashFor / t.dashTime);
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

        // A double tap. Free, the operator dashes and can ram the cart; holding it, they heave it along in
        // one lurch, which a full cart barely feels.
        private void Dash(int direction)
        {
            var t = _game.Tune;
            if (_dashCooldown > 0 || Lane != 0 || Machine || Mathf.Abs(Body.linearVelocity.y) > 1) return;
            _dashCooldown = t.dashCooldown;
            _dashDir = direction;
            Facing = direction;
            var cart = _game.Cart.Body;
            if (Holding)
            {
                // Shared between both bodies, since Hold keeps them together.
                var kick = new Vector2(direction * t.lurchImpulse / (cart.mass + Body.mass), 0);
                cart.linearVelocity += kick;
                Body.linearVelocity += kick;
                _game.Cart.Hop();
                _lurchFor = .3f;
                _game.Sound("lurch", .2f);
                return;
            }
            _dashFor = t.dashTime;
            _dashStart = Mathf.Max(t.dashStartSpeed, Body.linearVelocity.x * direction);
            _ramming = true;
            _lastAhead = float.MaxValue;
            Body.linearVelocity = new Vector2(direction * _dashStart, Body.linearVelocity.y);
            _game.Sound("dash", .2f);
        }

        // Short dash: tapping the other way mid-dash stops it dead, so how soon you tap sets the distance.
        // Letting go doesn't: a double tap is usually quick, and every dash would come out short.
        private void DashCancel(Controls c)
        {
            if (c.Tap != -_dashDir) return;
            _dashFor = 0;
            Body.linearVelocity = new Vector2(0, Body.linearVelocity.y);
        }

        // Running into the end of the cart mid-dash: an inelastic hit through its centre of mass, so it
        // shoves the cart along without tipping it. An overturned cart is rolled over the way it was hit
        // instead, so ramming it from the right side stands it back up (and from the wrong side, turns it
        // further over). The operator bounces off and staggers.
        private void Ram(Vector2 p)
        {
            var cart = _game.Cart.Body;
            var over = _game.Cart.Overturned;
            float ahead = (cart.position.x - p.x) * _dashDir, reach = over ? _game.Cart.HalfWidth + .24f : Touching;
            var closing = ahead < _lastAhead;
            _lastAhead = ahead;
            // Further in than this the operator is already walking through the cart, not running into it.
            if (!closing || ahead > reach || ahead < reach - .45f || Mathf.Abs(cart.position.y - p.y) > 1.4f) return;
            float vp = Body.linearVelocity.x, vc = cart.linearVelocity.x;
            if ((vp - vc) * _dashDir <= 0) return;
            var shared = (Body.mass * vp + cart.mass * vc) / (Body.mass + cart.mass);
            cart.linearVelocity = new Vector2(shared, cart.linearVelocity.y);
            if (over)
            {
                // Hit high on its side, it rolls: clockwise when hit from the left. A small hop lets it
                // pivot over its corner instead of grinding on it.
                var hit = (vp - vc) * _dashDir / _game.Tune.dashSpeed * _game.Tune.cartMass / cart.mass;
                cart.angularVelocity = -_dashDir * _game.Tune.rightingSpin * hit;
                cart.linearVelocity += new Vector2(0, 2 * hit);
            }
            Body.position = new Vector2(cart.position.x - _dashDir * reach, p.y);
            Body.linearVelocity = new Vector2(shared - _dashDir * 1.5f, Body.linearVelocity.y);
            _dashFor = 0;
            _ramming = false;
            Stagger = .3f;
            _game.Sound("ram", .2f, cart.mass);
        }

        // E: one press, one action, picked by what the operator is standing next to: answering the phone,
        // righting the cart, putting a carried wheel down, picking one up, hooking the cart onto the tip
        // cradle or lowering it, or clocking out at the staff lift.
        private void Interact()
        {
            if (Machine || Lane != 0) return;
            if (_game.Tutorial.ShovelInReach(this)) { _game.Tutorial.TakeShovel(); return; }
            if (_game.Tutorial.Restricts) return;
            if (_game.Phone.CanAnswer(this)) { _game.Phone.Answer(); return; }
            if (CartOverturnedInReach()) { RightCart(); return; }
            if (CarryingWheel)
            {
                // Away from the cart, E puts the wheel down; at the cart, holding it bolts it on (see Fit).
                if (!CartNear() || _game.Cart.Wheels >= 2) ReleaseWheel(Body.linearVelocity + new Vector2(Facing * 1.5f, 1));
                else if (_game.Cart.WheelBlocker != null) _game.Say(_game.Cart.WheelBlocker, 2);
                return;
            }
            if (WheelInReach() is { } wheel) { PickUp(wheel); return; }
            if (CartNear() && _game.Cradle.Interact()) return;
            if (CanClockOut) _game.ClockOut();
        }

        private void RightCart()
        {
            var cart = _game.Cart;
            var cp = cart.Body.position;
            var floor = _game.Coal.SurfaceAt(cp.x, cp.y).Floor();
            // A cart at rest has landed, even when it came to rest hung up on something.
            if (cart.Body.linearVelocity.magnitude > (cp.y - floor > 1.8f ? .3f : 1)) { _game.Say("The cart has to land first.", 2); return; }
            if (cart.Burial > .8f) { _game.Say("The cart is buried. Dig it out first.", 3); return; }
            ReleaseGrip();
            cart.Right();
        }

        private Rigidbody2D WheelInReach()
        {
            Rigidbody2D best = null;
            var reach = _game.Tune.wheelReach;
            foreach (var w in _game.Cart.LooseWheels)
            {
                var d = Vector2.Distance(w.position, Body.position);
                if (d >= reach) continue;
                best = w;
                reach = d;
            }
            return best;
        }

        private void PickUp(Rigidbody2D wheel)
        {
            ThrowHands();
            ReleaseGrip();
            Vector3 from = wheel.position;
            var spin = wheel.transform.rotation;
            _game.Cart.RemoveWheel(wheel);
            CarryingWheel = true;
            if (heldWheel) heldWheel.gameObject.SetActive(true);
            StartCoroutine(Fly(from, spin, () => HandPoint, .2f, null));
            _game.Sound("clank");
        }

        private void ReleaseWheel(Vector2 velocity)
        {
            CarryingWheel = false;
            if (heldWheel) heldWheel.gameObject.SetActive(false);
            _game.Cart.DropWheel(HandPoint, velocity);
            _game.Sound("clank");
        }

        // Thrown to pass through the cursor, like the shovel, but heavier: it falls short of far aims.
        private void ThrowWheel(Vector2 aim)
        {
            if (Mathf.Abs(aim.x - Body.position.x) > .05f) Facing = (int)Mathf.Sign(aim.x - Body.position.x);
            ReleaseWheel(ThrowVelocity(HandPoint, aim, _game.Tune.wheelThrowSpeed));
        }

        private Vector2 HandPoint => Body.position + new Vector2(Facing * .45f, .05f);

        // Holding E at the cart bolts the carried wheel on; letting go starts over. The operator lifts the
        // end missing the wheel and works it about while the wheel is lined up on its slot (see Update),
        // then it goes on with a clunk and the cart is let down.
        private void Fit(Controls c, float dt)
        {
            var cart = _game.Cart;
            if (!CarryingWheel || _flying || !c.InteractHeld || Lane != 0 || !CartNear() || cart.WheelBlocker != null || CartOverturnedInReach())
            {
                _fit = 0;
                return;
            }
            _fit += dt;
            var t = _game.Tune;
            var k = _fit / t.fitTime;
            Facing = (int)Mathf.Sign(cart.WheelSlot(cart.Wheels).x - Body.position.x);
            // Up in the first quarter, then rocked about, settling as the wheel lines up.
            cart.HoldUp(cart.Wheels, t.fitLift * Mathf.SmoothStep(0, 1, k * 4) + Mathf.Sin(_fit * 11) * 2.5f * Mathf.Min(1, k * 4) * (1 - k));
            _game.Sound("wrench", .3f);
            if (_fit < t.fitTime) return;
            _fit = 0;
            StartCoroutine(Fly(heldWheel ? heldWheel.position : HandPoint, heldWheel ? heldWheel.rotation : Quaternion.identity, () => cart.WheelSlot(cart.Wheels), .1f, () =>
            {
                CarryingWheel = false;
                if (heldWheel) heldWheel.gameObject.SetActive(false);
                cart.SetWheels(cart.Wheels + 1);
            }));
        }

        // Moves the held wheel from where it was to a point that may itself be moving (the hands, a cart
        // slot), eased in and out and turning upright on the way, so nothing jumps into place.
        private IEnumerator Fly(Vector3 from, Quaternion spin, System.Func<Vector2> to, float time, System.Action done)
        {
            _flying = true;
            for (float k = 0; k < 1; k += Time.deltaTime / time)
            {
                var e = k * k * (3 - 2 * k);
                if (heldWheel) heldWheel.SetPositionAndRotation(Vector3.Lerp(from, to(), e), Quaternion.Slerp(spin, Quaternion.identity, e));
                yield return null;
            }
            if (heldWheel) heldWheel.rotation = Quaternion.identity;
            _flying = false;
            done?.Invoke();
        }

        private void Update()
        {
            HeldWheel();
            Strike();
            HeldShovel();
            ScoopOutline();
            // Crouched to strike at the firebox; shivering in the cold, harder the nearer to freezing.
            var art = visual.transform;
            var crouch = _striking ? .82f : 1;
            var shiver = _game.Ended ? 0 : _game.Freezing ? .015f + .035f * _game.Chill : _game.Temperature < 6 ? .008f : 0;
            art.localScale = new Vector3(1, Mathf.MoveTowards(art.localScale.y, crouch, Time.deltaTime * 2), 1);
            art.localPosition = new Vector3(shiver * Mathf.Sin(Time.time * 55), -.66f * (1 - art.localScale.y), 0);
        }

        // In the hands, or while a wheel is being fitted, offered up to its slot and jiggled about, less and
        // less, until it lines up.
        private void HeldWheel()
        {
            if (!heldWheel || _flying) return;
            var fit = _fit / _game.Tune.fitTime;
            var hand = (Vector3)HandPoint + (heldWheel.parent.position - transform.position);
            if (fit <= 0)
            {
                heldWheel.SetPositionAndRotation(hand, Quaternion.identity);
                return;
            }
            var cart = _game.Cart;
            var loose = 1 - fit;
            var jiggle = new Vector2(Mathf.Sin(Time.time * 13), .5f * Mathf.Cos(Time.time * 9)) * (.12f * loose);
            var e = Mathf.SmoothStep(0, 1, fit * 3);
            heldWheel.SetPositionAndRotation(Vector3.Lerp(hand, cart.WheelSlot(cart.Wheels) + jiggle, e), Quaternion.Euler(0, 0, 25 * loose * Mathf.Sin(Time.time * 7)));
        }

        // Flint and steel: a quick scrape every .3 s, each throwing a few sparks at the firebox.
        private void Strike()
        {
            if (striker) striker.gameObject.SetActive(_striking);
            if (!_striking) return;
            var phase = Time.time % .3f / .3f;
            if (striker)
            {
                striker.localPosition = new Vector3(Facing * .38f, -.05f, 0);
                // Wound back slowly, then struck down fast.
                var swing = phase < .7f ? Mathf.Lerp(-20, 50, phase / .7f) : Mathf.Lerp(50, -40, (phase - .7f) / .3f);
                striker.localRotation = Quaternion.Euler(0, 0, Facing * -swing);
            }
            if (!strikeSparks || Time.time - _lastStrike < .3f) return;
            _lastStrike = Time.time;
            var at = Body.position + new Vector2(Facing * .5f, -.2f);
            for (var i = 0; i < 6; i++)
                strikeSparks.Emit(new ParticleSystem.EmitParams { position = at, velocity = new Vector2(Facing * Random.Range(1.5f, 4f), Random.Range(-.5f, 2.5f)) }, 1);
            _game.Sound("strike", .1f);
        }

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

        private bool Digging(Controls c) => c.Shovel && Lane == 0 && !Machine && !CarryingWheel && _game.Tutorial.CanDig;

        // The cart pushes loose coal ahead of it. That's harder work than shoveling it out of the way:
        // the pile pushes back, and past 1.2 high it doesn't budge.
        private void Bulldoze(float dt, int direction)
        {
            var coal = _game.Coal;
            var p = _game.Cart.Body.position;
            var s = coal.SurfaceAt(p.x, p.y);
            int i = coal.Index(s, p.x + direction * 1f), j = i + direction;
            if (j < 0 || j >= s.Heights.Length || s.Heights[i] < .03f) return;
            _game.Cart.Body.AddForce(new Vector2(-direction * s.Heights[i] * 95, 0));
            if (s.Heights[i] > 1.2f) return;
            CoalField.Shove(s, i, j, dt * .9f);
        }

        // Holding F fills the shovel from the coal within reach (the deepest spot around the cart first),
        // then throws it the way the operator faces: into the cart when it's there, onto the floor otherwise.
        private void Shovel(Controls c, float dt)
        {
            var t = _game.Tune;
            if (!Digging(c)) { ThrowHands(); return; }
            if (Hands >= t.handCapacity - .001f) { ThrowHands(); return; }
            // Held toward the cursor (they face it) while there's nothing to dig.
            _digSide = Facing;
            if (ScoopTarget(Facing) is not { } target) { ThrowHands(); return; }
            var (s, i) = target;
            var dx = s.Start + (i + .5f) * t.columnWidth - Body.position.x;
            _digSide = Mathf.Abs(dx) > .1f ? (int)Mathf.Sign(dx) : Facing;
            var taken = _game.Coal.TakeFromPile(s, i, Mathf.Min(t.shovelRate * dt, t.handCapacity - Hands));
            Hands += taken;
            Scooped += taken;
            _game.Sound("shovel", .22f);
        }

        // The scoop box in the world, on the side they face.
        private Rect ScoopRect(int side)
        {
            var feet = Body.position + new Vector2(0, -.72f);
            var x = side > 0 ? feet.x + scoopBox.xMin : feet.x - scoopBox.xMax;
            return new Rect(x, feet.y + scoopBox.yMin, scoopBox.width, scoopBox.height);
        }

        // The tallest column of coal piled into the scoop box, on any surface: the floors or the lift.
        private (CoalField.Surface s, int i)? ScoopTarget(int side)
        {
            var box = ScoopRect(side);
            var width = _game.Tune.columnWidth;
            (CoalField.Surface, int)? best = null;
            var bestTop = float.MinValue;
            foreach (var s in _game.Coal.Surfaces)
            {
                var floor = s.Y;
                if (floor > box.yMax) continue;
                for (var i = 0; i < s.Heights.Length; i++)
                {
                    var x = s.Start + (i + .5f) * width;
                    var top = floor + s.Heights[i];
                    if (x < box.xMin || x > box.xMax || s.Heights[i] <= .001f || top < box.yMin || top <= bestTop) continue;
                    best = (s, i);
                    bestTop = top;
                }
            }
            return best;
        }

        private void ScoopOutline()
        {
            if (!scoopOutline) return;
            scoopOutline.enabled = _digging;
            if (!_digging) return;
            var b = ScoopRect(Facing);
            scoopOutline.positionCount = 4;
            scoopOutline.loop = true;
            scoopOutline.SetPosition(0, new Vector3(b.xMin, b.yMin));
            scoopOutline.SetPosition(1, new Vector3(b.xMin, b.yMax));
            scoopOutline.SetPosition(2, new Vector3(b.xMax, b.yMax));
            scoopOutline.SetPosition(3, new Vector3(b.xMax, b.yMin));
            var color = ScoopTarget(Facing) != null ? new Color(1, .85f, .3f, .7f) : new Color(1, 1, 1, .25f);
            scoopOutline.startColor = scoopOutline.endColor = color;
        }

        // Empties the shovel toward the cursor. The throw is aimed to pass through the cursor, so coal
        // lands where you point; it only goes in the cart if the lumps actually land in the bin.
        public void ThrowHands()
        {
            if (Hands <= 0) return;
            var t = _game.Tune;
            var aim = input.Current.Aim;
            if (Mathf.Abs(aim.x - Body.position.x) > .05f) Facing = (int)Mathf.Sign(aim.x - Body.position.x);
            var from = Body.position + new Vector2(Facing * .7f, .7f);
            var v = ThrowVelocity(from, aim, t.throwSpeed);
            _game.Coal.Emit(from.x, from.y, Hands, v.x, v.y, t.throwSpread);
            Hands = 0;
            _thrownAt = Time.time;
            _game.Sound("coal");
        }

        // Out while digging or carrying coal: jabbed into the pile on whichever side it's digging as it fills,
        // the heap on the blade growing, then swung across and up toward the throw and back down.
        private void HeldShovel()
        {
            if (!heldShovel) return;
            var throwing = Time.time - _thrownAt;
            var shown = (_digging || Hands > 0 || throwing < .4f) && Lane == 0 && !CarryingWheel && !Machine;
            heldShovel.gameObject.SetActive(shown);
            if (!shown) return;
            var full = Hands / _game.Tune.handCapacity;
            // Degrees the shaft is swung from straight down, positive to the right: low toward the pile being
            // dug, coming round to point at the cursor as it fills, so you can see where the throw will go.
            var hand = Body.position + new Vector2(heldShovel.localPosition.x, -.08f);
            var toAim = input.Current.Aim - hand;
            var aimed = Mathf.Clamp(Mathf.Atan2(toAim.x, -toAim.y) * Mathf.Rad2Deg, -120, 120);
            var angle = _digging ? Mathf.LerpAngle(_digSide * 50, aimed, full * full) : Hands > 0 ? aimed : Facing * 50;
            if (_digging && Hands > 0 && full < .99f) angle += _digSide * Mathf.Sin(Time.time * 16) * 9 * (1 - full);
            if (throwing < .4f)
            {
                // Across and up in .12 s, back down over the rest.
                var swing = throwing < .12f ? Mathf.SmoothStep(0, 1, throwing / .12f) : 1 - Mathf.SmoothStep(0, 1, (throwing - .12f) / .28f);
                angle = Mathf.Lerp(angle, Facing * 145, swing);
            }
            var toward = angle >= 0 ? 1 : -1;
            heldShovel.localPosition = new Vector3(Mathf.Lerp(heldShovel.localPosition.x, toward * .2f, Time.deltaTime * 20), -.08f, 0);
            heldShovel.localRotation = Quaternion.Euler(0, 0, angle);
            if (!shovelLoad) return;
            shovelLoad.gameObject.SetActive(full > .01f);
            // On the blade's upper face.
            shovelLoad.localPosition = new Vector3(toward * .06f * Mathf.Sqrt(full), -.86f, 0);
            shovelLoad.localScale = new Vector3(.18f + .16f * full, .12f + .14f * full, 1);
        }

        private bool _digging;
        private int _digSide = 1;
        private float _thrownAt = -1;

        // The launch velocity that reaches `to` after a flight time that grows with distance, so near
        // throws are quick tosses and far ones lob. Capped at `max`, in which case it falls short.
        private static Vector2 ThrowVelocity(Vector2 from, Vector2 to, float max)
        {
            var d = to - from;
            var time = Mathf.Clamp(d.magnitude / 7, .25f, .7f);
            var v = d / time - .5f * Physics2D.gravity * time;
            return Vector2.ClampMagnitude(v, max);
        }

        // `cause` is what the death message says if this is what kills them. A steady hurt (being crushed)
        // doesn't restart the stagger every step.
        public void Hurt(float amount, string cause = "Crushed by falling coal", bool stagger = true)
        {
            Injury += amount;
            _hurtBy = cause;
            Stagger = stagger ? .35f : Mathf.Max(Stagger, .2f);
            _game.Sound("hurt", .4f);
        }

        private string _hurtBy = "Crushed by falling coal";

        // Coal over the operator's head for five seconds kills; so does enough coal falling on them.
        private void CheckBurial(float dt)
        {
            if (Lane != 0) return;
            var p = Body.position;
            var depth = _game.Coal.GroundAt(p.x, p.y) - (p.y - .66f);
            if (Holding && depth > .35f)
            {
                _gripBlocked = true;
                ReleaseGrip();
                _box.excludeLayers = GameLayers.Mask(GameLayers.Cart);
                _game.Say("Too much coal underfoot to hold the cart. Shovel a path.", 3);
            }
            Burial = depth > 1.5f ? Burial + dt : Mathf.Max(0, Burial - dt * 2);
            if (Injury > 2.5f) Stagger = .8f;
            if (Burial > 5 || Injury > 7) _game.Die(Burial > 5 ? "Buried under coal" : _hurtBy);
        }

        // Drops whatever was in hand, leaves the body where it stood (see Corpse), and takes the operator out of the
        // world until the replacement arrives (see Respawn). Returns the body.
        public Rigidbody2D Kill()
        {
            ThrowHands();
            ReleaseGrip();
            machines.Release();
            StopAllCoroutines();
            _flying = Climbing = _striking = false;
            _fit = 0;
            Body.bodyType = RigidbodyType2D.Dynamic;
            if (CarryingWheel) ReleaseWheel(Vector2.zero);
            var body = Corpse.Topple(Instantiate(visual.gameObject), "Dead operator", Body.position, Body.linearVelocity);
            Hide();
            return body;
        }

        // Out of the world until Respawn: dead, or still riding down the staff lift.
        public void Hide()
        {
            Body.simulated = false;
            visual.gameObject.SetActive(false);
        }

        // Riding down the staff lift: standing in the car behind the closed door, so they're already there
        // when it opens, but out of the world until Respawn.
        public void WaitInLift(Vector2 at)
        {
            Respawn(at);
            Body.simulated = false;
            if (visual) visual.sortingOrder = liftOrder;
        }

        public void Respawn(Vector2 at)
        {
            Body.simulated = true;
            visual.gameObject.SetActive(true);
            Facing = -1;
            ReleaseGrip();
            machines.Release();
            StopAllCoroutines();
            _flying = Climbing = false;
            Body.bodyType = RigidbodyType2D.Dynamic;
            if (CarryingWheel) ReleaseWheel(Vector2.zero);
            Hands = 0;
            Injury = Burial = Stagger = 0;
            Lane = 0;
            if (visual) visual.sortingOrder = frontOrder;
            Body.position = at;
            transform.position = at;
            Body.linearVelocity = Vector2.zero;
        }

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
            if (CarryingWheel) { _game.Say("Put the wheel down first.", 2); return; }
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

        // In front of the staff lift, past the control room glass, with the shift over.
        private bool CanClockOut => _game.Phase == Phase.Leaving && Lane == 0 && Body.position is { y: < 4.3f, x: > Room.EntranceStart + .2f };

        // At the foot of the stairs on the work floor, or at the control room door up top.
        private bool AtDoor => Lane == 0
            ? Body.position is { y: < 4.3f } p && Mathf.Abs(p.x - Room.StairsFoot) < .8f
            : Body.position.x < Room.BoothStart + 1.2f;

        // Coal in the scoop box on either side: they turn to face the cursor when they dig.
        private bool PileInReach() => ScoopTarget(1) != null || ScoopTarget(-1) != null;

        // The cart is taken hold of from either end, not from inside its footprint. 1.24 is the distance
        // between centres when the operator's body touches the end of the bin.
        private bool CartInGrip()
        {
            Vector2 p = Body.position, cp = _game.Cart.Body.position;
            var dx = Mathf.Abs(p.x - cp.x);
            return dx > 1.05f && dx < 1.24f + _game.Tune.gripReach && Mathf.Abs(p.y - cp.y) < 1.4f;
        }

        // Up to two key prompts for whatever is within reach, most urgent first.
        public List<(string key, string label)> Prompts()
        {
            var list = new List<(string, string)>();
            if (!_game.CanAct || Machine || Climbing) return list;
            if (Lane == 1)
            {
                if (AtDoor) list.Add(("S", "Down the stairs"));
                MachinePrompt(list);
                return list;
            }
            if (_game.Tutorial.ShovelInReach(this)) list.Add(("E", "Pick up the shovel"));
            if (_game.Tutorial.Restricts)
            {
                if (AtDoor) list.Add(("W", "Up to the control room"));
                return list;
            }
            if (_game.Phone.CanAnswer(this)) list.Add(("E", "Answer the phone"));
            if (CarryingWheel)
            {
                list.Add(CartNear() && _game.Cart.Wheels < 2 ? ("Hold E", "Fit wheel") : ("E", "Put wheel down"));
                list.Add(("Click", "Throw wheel"));
                return list;
            }
            if (WheelInReach()) list.Add(("E", "Pick up wheel"));
            if (CanClockOut) list.Add(("E", "Clock out"));
            if (AtDoor) list.Add(("W", "Up to the control room"));
            if (_game.Furnace.CanLight(this)) list.Add(("Hold E", "Light the fire"));
            if (CartNear() && _game.Cradle.Prompt != null) list.Add(("E", _game.Cradle.Prompt));
            if (CartOverturnedInReach()) list.Add(("E", "Right cart"));
            if (PileInReach() && _game.Tutorial.CanDig) list.Add(("F", "Shovel"));
            if (!Holding && CartInGrip() && Mathf.Cos(_game.Cart.Angle) > .3f) list.Add(("Space", "Grip cart"));
            MachinePrompt(list);
            if (list.Count > 2) list.RemoveRange(2, list.Count - 2);
            return list;
        }

        private void MachinePrompt(List<(string, string)> list)
        {
            foreach (var m in Machine.All)
            {
                if (!_game.Tutorial.Allows(m)) continue;
                if (m is LiftCrank && m.CanOperate(this)) { list.Add(("W S", "Crank")); return; }
                if (m is not FloorLever lever || !m.CanOperate(this)) continue;
                list.Add(("Mouse", lever.Prompt));
                return;
            }
        }
    }
}

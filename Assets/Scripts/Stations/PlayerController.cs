using System.Collections.Generic;
using CoalRun.Core;
using UnityEngine.Rendering;
using UnityEngine;

namespace CoalRun.Stations
{
    // The operator. Walks with forces rather than by setting velocity, so the body still gets shoved
    // around by the cart and the lift. Gripping the cart is a soft spring between the two bodies.
    // Lane 0 is the work floor; lane 1 is behind the control room glass, where only terrain collides.
    [RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
    public class PlayerController : MonoBehaviour
    {
        [SerializeField] private InputReader input;
        [SerializeField] private MachineInput machines;
        [Tooltip("Sorting group on the body art, pushed behind the glass in the control room.")]
        [SerializeField]
        private SortingGroup visual;
        [SerializeField] private int frontOrder = 20, backOrder = 4;

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

        private BoxCollider2D _box;
        // How far the operator may be from touching the cart; closes to zero right after taking hold.
        private float _slack;
        // Centre to centre when the operator's hands are on the bin's rim (cart half width 1 + operator .24).
        private const float Touching = 1.26f;

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
            if (!_game.CanAct) { ReleaseGrip(); return; }
            var c = input.Current;
            if (c.Lane != 0) ChangeLane(c.Lane > 0 ? 1 : 0);
            // At the firebox, E is held to light the fire rather than pressed.
            var atFirebox = _game.Furnace.CanLight(this);
            _game.Furnace.Kindle(atFirebox && c.InteractHeld, dt);
            if (c.Interact && !atFirebox) Interact();
            Move(c, dt);
            Shovel(c, dt);
            if (Lane == 1) Body.position = new Vector2(Mathf.Clamp(Body.position.x, 23.1f, 30.6f), Body.position.y);
            CheckBurial(dt);
        }

        private void Move(Controls c, float dt)
        {
            var cart = _game.Cart;
            var t = _game.Tune;
            if (!c.Grip) _gripBlocked = false;
            if (c.Move != 0) Facing = (int)Mathf.Sign(c.Move);
            var digging = Digging(c);
            Vector2 p = Body.position, cp = cart.Body.position;

            if (Holding && (!c.Grip || digging || Machine || Lane != 0 || cart.InCradle)) ReleaseGrip();
            if (!_gripBlocked && !Holding && c.Grip && !digging && !Machine && Lane == 0 && CartInGrip() && !cart.InCradle && Mathf.Cos(cart.Angle) > .3f)
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

            const float cold = 1; // Slows the operator when the building freezes; arrives with the furnace.
            var target = digging ? 0 : c.Move * t.walkSpeed * (Machine ? t.machineWalk : 1) * cold * (Stagger > 0 ? .25f : 1);
            // The dash speeds up as it goes (a run-up), set directly so it follows the ramp exactly.
            if (_dashFor > 0)
            {
                var ramp = Mathf.Lerp(_dashStart, t.dashSpeed, 1 - _dashFor / t.dashTime);
                Body.linearVelocity = new Vector2(_dashDir * Mathf.Max(ramp, Body.linearVelocity.x * _dashDir), Body.linearVelocity.y);
                target = Body.linearVelocity.x;
            }
            var v = Body.linearVelocity;
            // Walking into a low pile steps up onto it instead of stopping dead against it.
            if (c.Move != 0 && !Machine && !digging && Mathf.Abs(v.y) < 1 && Mathf.Abs(v.x) < 2.5f)
            {
                var ahead = p.x + c.Move * .36f;
                var step = _game.Coal.GroundAt(ahead, p.y) - (p.y - .66f);
                if (step is > .005f and < .48f) Body.position = new Vector2(p.x, p.y + step + .035f);
            }
            // Coal around the cart slows the haul. Past 1.2 deep it doesn't move at all.
            var bury = Mathf.Max(0, 1 - cart.Burial / 1.2f);
            // A dragging skid makes it slow work: the operator can't haul it at walking pace however hard they
            // push. Heaved off the floor, it nearly rolls like a two-wheel cart.
            var wheels = cart.Wheels >= 2 ? 1 : cart.Wheels == 1 ? cart.SkidClear ? t.heaveHaul : t.oneWheelHaul : t.noWheelHaul;
            var effective = holding ? c.Move * Mathf.Min(t.walkSpeed, 4.8f / (1 + cart.Coal * .038f)) * wheels * bury : target;
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

        // E: one press, one action, picked by what the operator is standing next to: hooking the cart onto
        // the tip cradle or lowering it, righting it, or clocking out at the staff lift. The phone and wheels come in M4.
        private void Interact()
        {
            if (Machine || Lane != 0) return;
            var cart = _game.Cart;
            if (CartNear() && _game.Cradle.Interact()) return;
            if (CanClockOut) { _game.ClockOut(); return; }
            if (!CartOverturnedInReach()) return;
            var cp = cart.Body.position;
            var floor = _game.Coal.SurfaceAt(cp.x, cp.y).Floor();
            // A cart at rest has landed, even when it came to rest hung up on something.
            if (cart.Body.linearVelocity.magnitude > (cp.y - floor > 1.8f ? .3f : 1)) { _game.Say("The cart has to land first.", 2); return; }
            if (cart.Burial > .8f) { _game.Say("The cart is buried. Dig it out first.", 3); return; }
            ReleaseGrip();
            cart.Right();
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

        private bool Digging(Controls c) => c.Shovel && Lane == 0 && !Machine;

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
            var coal = _game.Coal;
            Vector2 p = Body.position, cp = _game.Cart.Body.position;
            _spots.Clear();
            foreach (var dx in new[] { Facing * .65f, 0, -Facing * .65f }) Reach(p.x + dx, p.y);
            if (Mathf.Abs(cp.x - p.x) < 2.4f && Mathf.Abs(cp.y - p.y) < 2)
            {
                foreach (var x in new[] { cp.x - .6f, cp.x, cp.x + .6f }) if (Mathf.Abs(x - p.x) < 2.6f) Reach(x, cp.y);
                _spots.Sort((a, b) => b.s.Heights[b.i].CompareTo(a.s.Heights[a.i]));
            }
            foreach (var (s, i) in _spots)
            {
                if (s.Heights[i] <= .001f) continue;
                Hands += coal.TakeFromPile(s, i, Mathf.Min(t.shovelRate * dt, t.handCapacity - Hands));
                _game.Sound("shovel", .22f);
                return;
            }
            ThrowHands();
            return;

            void Reach(float x, float y) { var s = coal.SurfaceAt(x, y); _spots.Add((s, coal.Index(s, x))); }
        }

        private readonly List<(CoalField.Surface s, int i)> _spots = new();

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
            _game.Sound("coal");
        }

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
            // Stepping out of the control room into coal deep enough to bury the operator is refused.
            if (lane == 0 && DoorBlocked) { _game.Say("Coal is piled against this door.", 3); return; }
            ThrowHands();
            ReleaseGrip();
            machines.Release();
            Lane = lane;
            if (visual) visual.sortingOrder = lane == 1 ? backOrder : frontOrder;
            _game.Sound("step");
        }

        // In front of the staff lift, past the control room glass, with the shift over.
        private bool CanClockOut => _game.Phase == Phase.Leaving && Lane == 0 && Body.position is { y: < 4.3f, x: > Room.EntranceStart + .2f };

        private bool AtDoor => Body.position is { y: < 4.3f, x: > 29.4f or > 22.7f and < 24.2f };

        private bool DoorBlocked
        {
            get { var c = _game.Coal; return c.PileHeight(c.Surfaces[0], Body.position.x) > 1; }
        }

        private bool PileInReach()
        {
            var c = _game.Coal;
            var p = Body.position;
            foreach (var dx in new[] { 0, .65f, -.65f })
                if (c.PileHeight(c.SurfaceAt(p.x + dx, p.y), p.x + dx) > .04f) return true;
            return false;
        }

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
            if (!_game.CanAct || Machine) return list;
            if (Lane == 1)
            {
                if (AtDoor && !DoorBlocked) list.Add(("S", "Leave"));
                return list;
            }
            if (CanClockOut) list.Add(("E", "Clock out"));
            if (AtDoor) list.Add(("W", "Enter"));
            if (_game.Furnace.CanLight(this)) list.Add(("Hold E", "Light the fire"));
            if (CartNear() && _game.Cradle.Prompt != null) list.Add(("E", _game.Cradle.Prompt));
            if (CartOverturnedInReach()) list.Add(("E", "Right cart"));
            if (PileInReach()) list.Add(("F", "Shovel"));
            if (!Holding && CartInGrip() && Mathf.Cos(_game.Cart.Angle) > .3f) list.Add(("Space", "Grip cart"));
            foreach (var m in Machine.All)
            {
                if (m is LiftCrank && m.CanOperate(this)) { list.Add(("W S", "Crank")); break; }
                if (m is not FloorLever lever || !m.CanOperate(this)) continue;
                list.Add(("Mouse", lever.Prompt));
                break;
            }
            if (list.Count > 2) list.RemoveRange(2, list.Count - 2);
            return list;
        }
    }
}

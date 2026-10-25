using CoalRun.Core;
using UnityEngine;

namespace CoalRun.Stations
{
    // The tip cradle on the upper floor, beside the hopper grate. Park the cart in it and press E: two
    // chains hook onto the bin and hoist it clear of the floor. Then the tip lever turns it about the
    // bottom corner on the side it's pushed toward, like a pitcher over a cup: pushed right, the rim swings
    // out over the grate and the coal pours out onto it (the cart's ordinary spill) and down into the
    // hopper; pulled left, it goes on the floor. Let go and the lever rolls back upright. E again with the
    // cart level lowers it and unhooks it.
    public class TipCradle : MonoBehaviour
    {
        [SerializeField] private FloorLever lever;
        [Tooltip("Hung from the hoist beam over either end of the cart, left then right. Stretched to the bin's top corners while it's hooked on.")]
        [SerializeField]
        private Transform[] chains = new Transform[2];
        [Tooltip("How high the beam the chains hang from is, above the floor.")]
        [SerializeField]
        private float beamHeight = 4;

        private enum State { Free, Raising, Up, Lowering }

        public bool Hooked => _state != State.Free;
        // What E would do, for the key prompt; null when it would do nothing here.
        public string Prompt => _state switch
        {
            State.Free when Parked => "Hook cart",
            State.Up => "Lower cart",
            _ => null
        };

        private ShiftManager _game;
        private HingeJoint2D _joint;
        private State _state;
        // Which bottom corner of the bin the cart turns about: 1 right (over the grate), -1 left.
        private float _side = 1;

        private void Start()
        {
            _game = ShiftManager.Instance;
            lever.Interlock = () => _state == State.Up;
        }

        // Upright and all but stopped, on the upper floor over the cradle.
        private bool Parked
        {
            get
            {
                var cart = _game.Cart;
                var p = cart.Body.position;
                return Mathf.Abs(p.x - transform.position.x) < _game.Tune.cradleReach && Mathf.Abs(p.y - (Room.Upper + .7f)) < .8f &&
                       Mathf.Abs(cart.Body.linearVelocity.x) < 2 && !cart.Overturned;
            }
        }

        // The bin's bottom corner on the given side, in the cart's own space.
        private static Vector2 Corner(float side) => new(side * .83f, -.12f);

        // Where that corner is with the cart standing level in the cradle, hoisted or set down.
        private Vector2 CornerAt(float side, bool hoisted) =>
            new Vector2(transform.position.x, Room.Upper + .7f + (hoisted ? _game.Tune.hoistHeight : 0)) + Corner(side);

        // E next to the cart. Returns whether the press was for the cradle.
        public bool Interact()
        {
            var cart = _game.Cart;
            switch (_state)
            {
                case State.Free when Parked:
                    _game.Player.ReleaseGrip();
                    _side = 1;
                    // Hooked on where it stands; the hoist then lifts it and centres it in the cradle.
                    _joint = cart.gameObject.AddComponent<HingeJoint2D>();
                    _joint.autoConfigureConnectedAnchor = false;
                    _joint.anchor = Corner(_side);
                    _joint.connectedAnchor = cart.Body.GetRelativePoint(Corner(_side));
                    _joint.useMotor = true;
                    cart.Cradle = this;
                    _state = State.Raising;
                    _game.Sound("hook");
                    return true;
                case State.Up when lever.InUse || Mathf.Abs(cart.Body.rotation) > 3:
                    _game.Say("Let the cart come level first.", 2);
                    return true;
                case State.Up:
                    _state = State.Lowering;
                    _game.Sound("hoist");
                    return true;
                case State.Raising or State.Lowering:
                    return true;
                default:
                    return false;
            }
        }

        private void FixedUpdate()
        {
            lever.InterlockMessage = Parked ? "Hook the cart on first (E)." : "Stop the cart at the tip cradle.";
            if (!_joint) return;
            var t = _game.Tune;
            var cart = _game.Cart;
            var rotation = cart.Body.rotation;

            // Level, it changes over to the corner on the side the lever is pushed toward.
            var push = lever.Value - lever.RestValue;
            var want = push > .01f ? 1 : push < -.01f ? -1 : _side;
            if (_state == State.Up && !Mathf.Approximately(want, _side) && Mathf.Abs(rotation) < 1.5f)
            {
                _side = want;
                _joint.anchor = Corner(_side);
                _joint.connectedAnchor = CornerAt(_side, true);
            }

            // The hoist moves the hooked corner up into place, or back down.
            var goal = CornerAt(_side, _state != State.Lowering);
            _joint.connectedAnchor = Vector2.MoveTowards(_joint.connectedAnchor, goal, t.hoistSpeed * Time.fixedDeltaTime);
            var arrived = (_joint.connectedAnchor - goal).sqrMagnitude < 1e-6f;
            if (_state == State.Raising && arrived) { _state = State.Up; _game.Sound("clank"); }
            if (_state == State.Lowering && arrived)
            {
                Destroy(_joint);
                _joint = null;
                cart.Cradle = null;
                _state = State.Free;
                _game.Sound("clank");
                return;
            }

            // Rotating clockwise (negative) lowers the cart's right end. Only the lever turns it, and only
            // once it's up; till then the motor holds it level.
            var target = _state == State.Up ? -push * 2 * t.tipMaxAngle : 0;
            // A loaded bin turns slower.
            var speed = Mathf.Clamp((target - rotation) * 7, -t.tipSpeed, t.tipSpeed) / (1 + cart.Coal * .07f);
            // The joint turns the world relative to the cart, so its motor runs the opposite way.
            _joint.motor = new JointMotor2D { motorSpeed = -speed, maxMotorTorque = t.tipTorque };
        }

        private void Update()
        {
            if (!_game) return;
            var cart = _game.Cart;
            for (var i = 0; i < chains.Length; i++)
            {
                if (!chains[i]) continue;
                float end = i == 0 ? -1 : 1;
                var top = new Vector2(transform.position.x + end, transform.position.y + beamHeight);
                var bottom = Hooked ? cart.Body.GetRelativePoint(new Vector2(end, .64f)) : top + Vector2.down * 1.2f;
                var d = bottom - top;
                chains[i].position = (top + bottom) / 2;
                chains[i].rotation = Quaternion.FromToRotation(Vector3.up, d);
                chains[i].localScale = new Vector3(chains[i].localScale.x, d.magnitude, 1);
            }
        }
    }
}

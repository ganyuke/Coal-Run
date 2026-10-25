using CoalRun.Core;
using UnityEngine;

namespace CoalRun.Stations
{
    // The tip cradle on the upper floor, by the hopper grate. Park the cart in it and press E: two chains
    // from the hoist beam hook onto the bin's top corners and wind it up off the floor. The tip lever then
    // pays out the chain on the side it's pushed toward: that end drops and swings in under the other
    // chain, which holds, and the coal pours out over the low rim (the cart's ordinary spill). Pushed right
    // it lands on the grate and drops into the hopper; pulled left it goes on the floor. Let go and the
    // lever rolls back upright, winding the chain back in. E again with the cart level lowers it and
    // unhooks it. The chains are real ropes: only they and gravity hold and turn the cart.
    public class TipCradle : MonoBehaviour
    {
        [SerializeField] private FloorLever lever;
        [Tooltip("Hung from the hoist beam over either end of the cart, left then right. Drawn to the bin's top corners while it's hooked on.")]
        [SerializeField]
        private Transform[] chains = new Transform[2];
        [Tooltip("How high the beam the chains hang from is, above the floor.")]
        [SerializeField]
        private float beamHeight = 4;
        [Tooltip("Damping on the hanging cart, so it doesn't swing for ages.")]
        [SerializeField]
        private float swingDamping = 2;

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
        private State _state;
        // Left then right, like the chains.
        private readonly DistanceJoint2D[] _ropes = new DistanceJoint2D[2];
        private float _linearDamping, _angularDamping;

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
                       Mathf.Abs(cart.Body.linearVelocity.x) < 2 && !cart.Overturned && !cart.Righting;
            }
        }

        // The bin's top corner at that end (-1 left, 1 right), in the cart's own space, and where its chain hangs from.
        private static Vector2 Hook(float end) => new(end, .64f);
        private Vector2 BeamPoint(float end) => new(transform.position.x + end, transform.position.y + beamHeight);

        // E next to the cart. Returns whether the press was for the cradle.
        public bool Interact()
        {
            var cart = _game.Cart;
            switch (_state)
            {
                case State.Free when Parked:
                    _game.Player.ReleaseGrip();
                    for (var i = 0; i < 2; i++)
                    {
                        float end = i == 0 ? -1 : 1;
                        var rope = cart.gameObject.AddComponent<DistanceJoint2D>();
                        rope.autoConfigureConnectedAnchor = false;
                        rope.autoConfigureDistance = false;
                        rope.anchor = Hook(end);
                        rope.connectedAnchor = BeamPoint(end);
                        rope.maxDistanceOnly = true;
                        rope.distance = Vector2.Distance(cart.Body.GetRelativePoint(Hook(end)), BeamPoint(end));
                        _ropes[i] = rope;
                    }
                    _linearDamping = cart.Body.linearDamping;
                    _angularDamping = cart.Body.angularDamping;
                    cart.Body.linearDamping = swingDamping;
                    cart.Body.angularDamping = swingDamping;
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
            if (_state == State.Free) return;
            var t = _game.Tune;
            var cart = _game.Cart;
            var dt = Time.fixedDeltaTime;

            // Wound up, the chains hold the cart level `hoistHeight` off the floor. Lowering pays them out a
            // little past where it stands, so it's resting on its wheels before they come off.
            var standing = Room.Upper + .7f + Hook(1).y;
            var held = transform.position.y + beamHeight - standing - (_state == State.Lowering ? -.1f : t.hoistHeight);
            var push = (lever.Value - lever.RestValue) * 2;
            var arrived = true;
            for (var i = 0; i < 2; i++)
            {
                // The lever pays out the chain on the side it's pushed toward.
                var payout = _state == State.Up ? Mathf.Max(0, i == 0 ? -push : push) * t.tipPayout : 0;
                var speed = _state == State.Up ? t.payoutSpeed : t.hoistSpeed;
                var length = Mathf.MoveTowards(_ropes[i].distance, held + payout, speed * dt);
                arrived &= Mathf.Approximately(length, held + payout);
                if (Mathf.Approximately(length, _ropes[i].distance)) continue;
                _ropes[i].distance = length;
                cart.Body.WakeUp(); // Paying out chain doesn't wake a hanging cart that's gone to sleep.
            }

            switch (_state)
            {
                case State.Raising when arrived:
                    _state = State.Up;
                    _game.Sound("clank");
                    break;
                case State.Lowering when arrived:
                    for (var i = 0; i < 2; i++)
                    {
                        Destroy(_ropes[i]);
                        _ropes[i] = null;
                    }
                    cart.Body.linearDamping = _linearDamping;
                    cart.Body.angularDamping = _angularDamping;
                    cart.Cradle = null;
                    _state = State.Free;
                    _game.Sound("clank");
                    break;
            }
        }

        private void Update()
        {
            if (!_game) return;
            var cart = _game.Cart;
            for (var i = 0; i < chains.Length; i++)
            {
                if (!chains[i]) continue;
                float end = i == 0 ? -1 : 1;
                var top = BeamPoint(end);
                var bottom = Hooked ? cart.Body.GetRelativePoint(Hook(end)) : top + Vector2.down * 1.2f;
                var d = bottom - top;
                chains[i].position = (top + bottom) / 2;
                chains[i].rotation = Quaternion.FromToRotation(Vector3.up, d);
                chains[i].localScale = new Vector3(chains[i].localScale.x, d.magnitude, 1);
            }
        }
    }
}

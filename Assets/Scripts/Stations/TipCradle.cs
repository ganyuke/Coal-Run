using CoalRun.Core;
using UnityEngine;

namespace CoalRun.Stations
{
    // The tip cradle on the upper floor, over the furnace hopper. Park the cart on it and take the tip
    // lever: the cradle lifts the bin clear of the floor on a pivot and a motor turns it after the lever.
    // Push the lever right and the cart tips toward the hopper and pours in; pull it left and the coal
    // goes on the floor. Let go and the lever rolls back upright, and once the cart is level it's set down.
    public class TipCradle : MonoBehaviour
    {
        [SerializeField] private FloorLever lever;
        [Tooltip("Which way the cart tips to pour into the hopper, in world x. The lever pushed right tips it this way.")]
        [SerializeField]
        private float hopperSide = 1;

        private ShiftManager _game;
        private HingeJoint2D _joint;

        private void Start()
        {
            _game = ShiftManager.Instance;
            lever.Interlock = () => Parked;
            lever.InterlockMessage = "Stop the cart at the tip cradle.";
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

        private void FixedUpdate()
        {
            var cart = _game.Cart;
            if (!_joint)
            {
                if (lever.InUse && Parked) Seat(cart);
                return;
            }
            var t = _game.Tune;
            // Rotating clockwise (negative) lowers the cart's +x end.
            var target = -(lever.Value - lever.RestValue) * 2 * t.tipMaxAngle * hopperSide;
            // A loaded bin turns slower.
            var speed = Mathf.Clamp((target - cart.Body.rotation) * 7, -t.tipSpeed, t.tipSpeed) / (1 + cart.Coal * .07f);
            // The joint turns the world relative to the cart, so its motor runs the opposite way.
            _joint.motor = new JointMotor2D { motorSpeed = -speed, maxMotorTorque = t.tipTorque };
            if (!lever.InUse && Mathf.Abs(cart.Body.rotation) < 3.4f) SetDown(cart);
        }

        private void Seat(Cart cart)
        {
            _game.Player.ReleaseGrip();
            var b = cart.Body;
            // Lifted clear of the floor, so the floor can't block the bin as it turns.
            b.position = new Vector2(b.position.x, transform.position.y + 1.5f);
            b.rotation = 0;
            b.linearVelocity = Vector2.zero;
            b.angularVelocity = 0;
            cart.transform.SetPositionAndRotation(b.position, Quaternion.identity);
            _joint = cart.gameObject.AddComponent<HingeJoint2D>();
            _joint.autoConfigureConnectedAnchor = false;
            _joint.anchor = Vector2.zero;
            _joint.connectedAnchor = b.position;
            _joint.useLimits = true;
            _joint.limits = new JointAngleLimits2D { min = -92, max = 92 };
            _joint.useMotor = true;
            cart.Cradle = this;
            _game.Sound("clank");
        }

        private void SetDown(Cart cart)
        {
            Destroy(_joint);
            _joint = null;
            cart.Cradle = null;
            _game.Sound("clank");
        }

        // Coal tipped out over the hopper side goes in until the hopper is full. Returns how much went in.
        public float Pour(float amount, float side) => side * hopperSide > 0 ? _game.Furnace.Feed(amount) : 0;
    }
}

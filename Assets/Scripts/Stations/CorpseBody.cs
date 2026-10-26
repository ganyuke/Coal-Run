using CoalRun.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoalRun.Stations
{
    // What a body does besides lie there (see Corpse): it can be picked up and thrown like a wheel, knocks the cart and
    // loose wheels about when it's thrown into them, and burns up if it ends in the furnace's mouth.
    public class CorpseBody : MonoBehaviour
    {
        [Tooltip("How hard a thrown body hits the cart and the wheels, as a share of the momentum it carries.")]
        [SerializeField]
        private float knock = .8f;

        [Tooltip("What a person weighs when it comes to hitting something: the body's own physics mass is only for how it falls.")]
        [SerializeField]
        private float impactMass = 40;

        public bool Carried { get; set; }
        // Seconds left in which a body that's been thrown still knocks things.
        public float Thrown { get; set; }

        private Rigidbody2D _body;
        private ShiftManager _game;
        private SortingGroup _group;
        private bool _hit;

        private void Awake()
        {
            _body = GetComponent<Rigidbody2D>();
            _group = GetComponentInChildren<SortingGroup>();
        }

        private void Start() => _game = ShiftManager.Instance;

        // Over the operator while carried, back among the floor's clutter once it's down.
        public void Lift(bool lifted)
        {
            if (_group) _group.sortingOrder = lifted ? 20 : 3;
        }

        public void Throw(float seconds)
        {
            Thrown = seconds;
            _hit = false;
        }

        private void FixedUpdate()
        {
            if (!_game || Carried) return;
            if (_game.Furnace.InMouth(_body.position))
            {
                _game.Furnace.Burn(_body.position);
                _game.Bodies.Remove(_body);
                gameObject.SetActive(false);
                return;
            }
            if (Thrown <= 0) return;
            Thrown -= Time.fixedDeltaTime;
            if (_hit || _body.linearVelocity.magnitude < 2.5f) return;
            var cart = _game.Cart;
            var p = _body.position;
            var v = _body.linearVelocity;
            if (Mathf.Abs(cart.Body.position.x - p.x) < cart.HalfWidth + .25f && Mathf.Abs(cart.Body.position.y - p.y) < 1.1f && !cart.Parked)
            {
                // An inelastic shove through the cart's middle: its share of the momentum, and the body bounces back a little.
                cart.Body.linearVelocity += new Vector2(v.x * knock * impactMass / (impactMass + cart.Body.mass), 0);
                _body.linearVelocity = new Vector2(-v.x * .3f, Mathf.Max(v.y, 1.5f));
                _game.Sound("ram", .2f, cart.Body.mass);
                _hit = true;
                return;
            }
            foreach (var wheel in cart.LooseWheels)
            {
                if (Vector2.Distance(wheel.position, p) > .8f) continue;
                wheel.linearVelocity += v * knock * .6f;
                _body.linearVelocity = v * .6f;
                _game.Sound("clank", .2f);
                _hit = true;
                return;
            }
        }
    }
}

using System.Collections;
using CoalRun.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Scripting.APIUpdating;

namespace CoalRun.Gameplay.Characters
{
    // What a body does besides lie there (see Corpse): it can be picked up and thrown like a wheel, knocks the cart and
    // loose wheels about when it's thrown into them, and burns up if it ends in the furnace's mouth.
    [MovedFrom("CoalRun.Stations")]
    public class CorpseBody : MonoBehaviour
    {
        [Tooltip("How hard a thrown body hits the cart and the wheels, as a share of the momentum it carries.")]
        [SerializeField]
        private float knock = .8f;

        [Tooltip(
            "What a person weighs when it comes to hitting something: the body's own physics mass is only for how it falls.")]
        [SerializeField]
        private float impactMass = 40;

        [Tooltip(
            "Seconds a body takes to burn up in the furnace: it settles on the hearth, chars, glows and crumbles away.")]
        [SerializeField]
        private float burnTime = 2.2f;

        [Tooltip("The body's sorting order while it burns.")] [SerializeField]
        private int burnOrder = 3;

        private Rigidbody2D _body;
        private ShiftManager _game;
        private SortingGroup _group;
        private bool _hit;

        public bool Carried { get; set; }

        // Seconds left in which a body that's been thrown still knocks things.
        public float Thrown { get; set; }

        private void Awake()
        {
            _body = GetComponent<Rigidbody2D>();
            _group = GetComponentInChildren<SortingGroup>();
        }

        private void Start()
        {
            _game = ShiftManager.Instance;
        }

        private void FixedUpdate()
        {
            if (!_game || Carried) return;
            if (_game.Furnace.Lit && _game.Furnace.InMouth(_body.position))
            {
                _game.Furnace.Burn(_body.position);
                _game.Bodies.Remove(_body);
                StartCoroutine(Burn());
                return;
            }

            if (Thrown <= 0) return;
            Thrown -= Time.fixedDeltaTime;
            if (_hit || _body.linearVelocity.magnitude < 2.5f) return;
            var cart = _game.Cart;
            var p = _body.position;
            var v = _body.linearVelocity;
            if (Mathf.Abs(cart.Body.position.x - p.x) < cart.HalfWidth + .25f &&
                Mathf.Abs(cart.Body.position.y - p.y) < 1.1f && !cart.Parked)
            {
                // An inelastic shove through the cart's middle: its share of the momentum, and the body bounces back a little.
                cart.Body.linearVelocity += new Vector2(v.x * knock * impactMass / (impactMass + cart.Body.mass), 0);
                _body.linearVelocity = new Vector2(-v.x * .3f, Mathf.Max(v.y, 1.5f));
                _game.PlayCue("ram", .2f, cart.Body.mass);
                _hit = true;
                return;
            }

            foreach (var wheel in cart.LooseWheels)
            {
                if (Vector2.Distance(wheel.position, p) > .8f) continue;
                wheel.linearVelocity += v * knock * .6f;
                _body.linearVelocity = v * .6f;
                _game.PlayCue("clank", .2f);
                _hit = true;
                return;
            }
        }

        // Over the operator while carried, back among the floor's clutter once it's down.
        public void Lift(bool lifted)
        {
            if (_group) _group.sortingOrder = lifted ? 20 : 3;
        }

        // Settles on the hearth, reddens, chars to black and fades out in a rain of sparks, then it's gone.
        private IEnumerator Burn()
        {
            Carried = true; // From here on it's the fire's: no throwing, no knocks.
            var furnace = _game.Furnace;
            _body.bodyType = RigidbodyType2D.Kinematic;
            _body.linearVelocity = Vector2.zero;
            _body.angularVelocity = 0;
            foreach (var c in GetComponents<Collider2D>()) c.enabled = false;
            var renderers = GetComponentsInChildren<SpriteRenderer>();
            var from = new Color[renderers.Length];
            for (var i = 0; i < from.Length; i++) from[i] = renderers[i].color;
            var start = _body.position;
            var hearth = new Vector2(Mathf.Clamp(start.x, furnace.Mouth.min.x + .6f, furnace.Mouth.max.x - .6f),
                furnace.Mouth.min.y + .45f);
            // Over the coal bed's glow, but behind the firebox's front.
            if (_group) _group.sortingOrder = burnOrder;
            var char_ = new Color(.07f, .05f, .04f);
            var hot = new Color(1, .45f, .15f);
            for (var t = 0f; t < burnTime; t += Time.deltaTime)
            {
                var k = t / burnTime;
                transform.position = Vector2.Lerp(start, hearth, Mathf.SmoothStep(0, 1, Mathf.Min(1, k * 2.5f)));
                transform.rotation = Quaternion.Euler(0, 0,
                    Mathf.LerpAngle(transform.eulerAngles.z, 80 * Mathf.Sign(Mathf.Sin(start.x * 7)),
                        Time.deltaTime * 2));
                // Lit up red-hot first, then burnt black, then thinning to nothing.
                var heat = Mathf.Clamp01(1 - Mathf.Abs(k - .25f) * 4);
                var fade = Mathf.Clamp01((k - .6f) / .4f);
                for (var i = 0; i < renderers.Length; i++)
                {
                    var tint = Color.Lerp(Color.Lerp(from[i], char_, Mathf.Clamp01(k * 2)), hot, heat * .8f);
                    tint.a = from[i].a * (1 - fade);
                    renderers[i].color = tint;
                }

                furnace.Cook(transform.position, k);
                yield return null;
            }

            gameObject.SetActive(false);
        }

        public void Throw(float seconds)
        {
            Thrown = seconds;
            _hit = false;
        }
    }
}
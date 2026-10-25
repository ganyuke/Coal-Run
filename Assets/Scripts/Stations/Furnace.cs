using CoalRun.Core;
using UnityEngine;

namespace CoalRun.Stations
{
    // The furnace, fed from the hopper under the tip cradle. It burns coal from the hopper at the rate set
    // by the burn lever's notch, and the burning makes the building's heat (ShiftManager) and electricity.
    public class Furnace : MonoBehaviour
    {
        [Tooltip("Scaled vertically with the hopper's coal, from its bottom edge.")]
        [SerializeField]
        private Transform fill;
        [SerializeField] private float fillHeight = 2.6f;
        [Tooltip("Its notches are Tuning.burnRates, slowest on the left.")]
        [SerializeField]
        private FloorLever burnLever;

        [Header("Fire and meter")]
        [Tooltip("Strips standing in the firebox mouth, scaled up from their bottom edge. Each flickers on its own.")]
        [SerializeField]
        private SpriteRenderer[] flames;
        [Tooltip("Tallest the middle flame gets at the top notch, before flicker. The outer ones are shorter.")]
        [SerializeField]
        private float flameHeight = 1.15f;
        [SerializeField] private Color emberColor = new(.45f, .08f, .03f);
        [SerializeField] private Color lowFlame = new(.85f, .25f, .06f);
        [SerializeField] private Color highFlame = new(1, .9f, .6f);
        [Tooltip("The electricity meter's disc. Spins with the coal actually burning, like a house meter.")]
        [SerializeField]
        private Transform meterDisc;
        [Tooltip("Degrees a second per coal a second: 1000 turns it about once a second at the top notch.")]
        [SerializeField]
        private float meterSpin = 1000;

        public float Hopper { get; private set; }
        // Everything poured into the hopper this shift.
        public float Delivered { get; private set; }
        // Everything burned, which leaves the coal total; and the electricity it made, in kWh.
        public float Burned { get; private set; }
        public float Output { get; private set; }
        // Coal actually burning per second: the notch's rate, or less once the hopper runs dry.
        public float BurnRate { get; private set; }
        public int Notch => burnLever ? burnLever.Notch : 0;

        private ShiftManager _game;
        // Each flame's bottom edge, which stays put as it grows.
        private float[] _flameBottoms;
        // How hard the fire looks to be burning, 0 to 1 (see Update).
        private float _heat;

        // In Awake, so ShiftManager counts the starting hopper in its Start.
        private void Awake()
        {
            _game = ShiftManager.Instance;
            Hopper = _game.Day.startingHopper;
            _flameBottoms = new float[flames.Length];
            for (var i = 0; i < flames.Length; i++)
                _flameBottoms[i] = flames[i].transform.localPosition.y - flames[i].transform.localScale.y / 2;
        }

        private void FixedUpdate()
        {
            if (_game.Phase != Phase.Shift) { BurnRate = 0; return; }
            var dt = Time.fixedDeltaTime;
            var used = Mathf.Min(Hopper, _game.Tune.burnRates[Notch] * dt);
            Hopper -= used;
            Burned += used;
            Output += used * 1.5f;
            BurnRate = used / dt;
        }

        private void Update()
        {
            if (fill)
            {
                var f = Mathf.Clamp01(Hopper / _game.Tune.hopperCapacity);
                fill.localScale = new Vector3(fill.localScale.x, f * fillHeight, 1);
                fill.gameObject.SetActive(f > .01f);
            }

            // 0 out, up to 1 at the top notch's rate. Smoothed, so a fire starved of coal dies down over a
            // second or two instead of going out at once.
            var rates = _game.Tune.burnRates;
            _heat = Mathf.MoveTowards(_heat, BurnRate / rates[^1], Time.deltaTime * .6f);
            for (var i = 0; i < flames.Length; i++)
            {
                // Perlin noise wanders smoothly, so the flames lick up and down instead of strobing.
                var flicker = Mathf.PerlinNoise(Time.time * 3 + i * 7.3f, i * 1.7f);
                var lit = _heat > .01f;
                // Tallest in the middle, so the row reads as one fire rather than a bar chart.
                var middle = (flames.Length - 1) / 2f;
                var shape = middle > 0 ? 1 - .45f * Mathf.Abs(i - middle) / middle : 1;
                var h = lit ? Mathf.Lerp(.15f, flameHeight, _heat) * shape * (.7f + .6f * flicker) : .06f + .04f * flicker;
                var t = flames[i].transform;
                t.localScale = new Vector3(t.localScale.x, h, 1);
                t.localPosition = new Vector3(t.localPosition.x, _flameBottoms[i] + h / 2, t.localPosition.z);
                flames[i].color = lit ? Color.Lerp(lowFlame, highFlame, _heat * (.6f + .4f * flicker)) : emberColor;
            }

            if (meterDisc) meterDisc.Rotate(0, 0, -BurnRate * meterSpin * Time.deltaTime);
        }

        // Takes what fits of `amount`. Returns how much went in.
        public float Feed(float amount)
        {
            var q = Mathf.Min(amount, _game.Tune.hopperCapacity - Hopper);
            if (q <= 0) return 0;
            Hopper += q;
            Delivered += q;
            _game.Sound("pour");
            return q;
        }
    }
}

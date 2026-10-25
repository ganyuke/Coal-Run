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
        [Tooltip("Flames rising off the coal bed, masked to the firebox mouth. Its emission, size and rise speed follow the burn; its noise module makes them lick.")]
        [SerializeField]
        private ParticleSystem fire;
        [Tooltip("A few sparks drifting up out of the mouth, more the harder it burns.")]
        [SerializeField]
        private ParticleSystem sparks;
        [Tooltip("The coal bed along the bottom of the firebox: embers when the fire is starved, bright when it roars.")]
        [SerializeField]
        private SpriteRenderer bed;
        [Tooltip("The inside of the firebox mouth, lit up by the fire.")]
        [SerializeField]
        private SpriteRenderer glow;
        [Tooltip("Flames and sparks a second at the top notch.")]
        [SerializeField]
        private float flameRate = 60;
        [SerializeField] private float sparkRate = 6;
        [SerializeField] private Color coldBed = new(.12f, .1f, .1f);
        [SerializeField] private Color emberColor = new(.7f, .16f, .04f);
        [SerializeField] private Color hotBedColor = new(1, .55f, .15f);
        [SerializeField] private Color darkMouth = new(.1f, .05f, .04f);
        [SerializeField] private Color litMouth = new(.4f, .12f, .05f);
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
        // The shift starts with the fire out. It's lit by holding E at the firebox, and goes out again once
        // the hopper has been empty for Tuning.emberTime.
        public bool Lit { get; private set; }

        private ShiftManager _game;
        // How hard the fire looks to be burning, 0 to 1 (see Update).
        private float _heat;
        // Seconds E has been held at the firebox, up to Tuning.lightTime; how far a fresh fire has come up
        // to full burn, 0 to 1; seconds the hopper has been empty while lit.
        private float _kindle, _catch, _starved;

        // In Awake, so ShiftManager counts the starting hopper in its Start.
        private void Awake()
        {
            _game = ShiftManager.Instance;
            Hopper = _game.Day.startingHopper;
        }

        private void FixedUpdate()
        {
            if (_game.Phase is not (Phase.Shift or Phase.Leaving)) { BurnRate = 0; return; }
            var dt = Time.fixedDeltaTime;
            var t = _game.Tune;
            if (!Lit) { BurnRate = 0; return; }
            _catch = Mathf.Min(1, _catch + dt / t.catchTime);
            var used = Mathf.Min(Hopper, t.burnRates[Notch] * _catch * dt);
            _starved = Hopper > 0 ? 0 : _starved + dt;
            if (_starved >= t.emberTime)
            {
                Lit = false;
                _game.Say("The fire has gone out. Light it again at the firebox.", 4);
            }

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
            var burning = _heat > .01f;
            if (fire)
            {
                var emission = fire.emission;
                emission.rateOverTimeMultiplier = burning ? Mathf.Lerp(.15f, 1, _heat) * flameRate : 0;
                var main = fire.main;
                main.startSizeMultiplier = Mathf.Lerp(.5f, 1, _heat);
                var rise = fire.velocityOverLifetime;
                rise.yMultiplier = Mathf.Lerp(.5f, 1.6f, _heat);
            }

            if (sparks)
            {
                var emission = sparks.emission;
                emission.rateOverTimeMultiplier = _heat * _heat * sparkRate;
            }

            // Perlin noise wanders smoothly, so the glow breathes instead of strobing.
            var flicker = Mathf.PerlinNoise(Time.time * 2, .5f);
            // The bed warms up as it's kindled, glows with the burn, and cools while the embers die.
            var ember = Lit ? 1 - _starved / _game.Tune.emberTime : _kindle / _game.Tune.lightTime;
            var bedColor = Color.Lerp(coldBed, emberColor, ember * (.8f + .2f * flicker));
            if (bed) bed.color = Color.Lerp(bedColor, hotBedColor, _heat * (.75f + .25f * flicker));
            if (glow) glow.color = Color.Lerp(darkMouth, litMouth, _heat * (.7f + .3f * flicker));

            if (meterDisc) meterDisc.Rotate(0, 0, -BurnRate * meterSpin * Time.deltaTime);
        }

        public bool CanLight(PlayerController player) =>
            !Lit && player.Lane == 0 && !player.Holding && !player.CarryingWheel && Mathf.Abs(player.Position.x - transform.position.x) < 1.3f &&
            Mathf.Abs(player.Position.y - transform.position.y) < 1.6f;

        // Called every step with whether the operator is holding E at the firebox. Letting go starts over.
        // Returns whether they're striking at it, for their animation.
        public bool Kindle(bool held, float dt)
        {
            if (!held) { _kindle = 0; return false; }
            if (Hopper <= 0) { _game.Say("There's no coal in the hopper to light.", 2); return false; }
            _kindle += dt;
            _game.Sound("kindle", .4f);
            if (_kindle < _game.Tune.lightTime) return true;
            Lit = true;
            _kindle = _catch = _starved = 0;
            _game.Sound("ignite");
            return false;
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

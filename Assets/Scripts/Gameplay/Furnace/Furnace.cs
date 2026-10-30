using CoalRun.Core;
using CoalRun.Gameplay.Machinery;
using CoalRun.Gameplay.Player;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Scripting.APIUpdating;

namespace CoalRun.Gameplay.Furnace
{
    // The furnace, fed from the hopper under the tip cradle. It burns coal from the hopper at the rate set
    // by the burn lever's notch, and the burning makes the building's heat (ShiftManager) and electricity.
    [MovedFrom("CoalRun.Stations")]
    public class Furnace : MonoBehaviour
    {
        [Tooltip("Scaled vertically with the hopper's coal, from its bottom edge.")] [SerializeField]
        private Transform fill;

        [SerializeField] private float fillHeight = 2.6f;

        [Tooltip("Its notches are Tuning.burnRates, slowest on the left.")] [SerializeField]
        private FloorLever burnLever;

        [Tooltip(
            "The firebox mouth in world space, down to the hearth floor: a body that ends up inside is burned up.")]
        [SerializeField]
        private Bounds mouth = new(new Vector3(17, 3.2f, 0), new Vector3(2.8f, 2.4f, 20));

        [Header("Fire and meter")]
        [Tooltip(
            "Flames rising off the coal bed, masked to the firebox mouth. Its emission, size and rise speed follow the burn; its noise module makes them lick.")]
        [SerializeField]
        private ParticleSystem fire;

        [Tooltip("A few sparks drifting up out of the mouth, more the harder it burns.")] [SerializeField]
        private ParticleSystem sparks;

        [Tooltip(
            "The coal bed along the bottom of the firebox: embers when the fire is starved, bright when it roars.")]
        [SerializeField]
        private SpriteRenderer bed;

        [Tooltip(
            "Soft additive blobs breathing over the coal bed: the red glow of embers, strongest while the fire is dying or being kindled, when the flames don't hide it.")]
        [SerializeField]
        private ParticleSystem emberGlow;

        [Tooltip("Particles a second at full glow.")] [SerializeField]
        private float glowRate = 7;

        [SerializeField] private Color glowColor = new(1, .3f, .08f);

        [Tooltip("The warm light the fire throws on the room: it breathes with the flames and swells with the burn.")]
        [SerializeField]
        private Light2D fireLight;

        [SerializeField] private Color embers = new(1, .32f, .1f), flames = new(1, .66f, .3f);

        [Tooltip("Its intensity with only embers, and at the top notch.")] [SerializeField]
        private Vector2 lightRange = new(.25f, 1.7f);

        [Tooltip("The inside of the firebox mouth, lit up by the fire.")] [SerializeField]
        private SpriteRenderer glow;

        [Tooltip("Flames and sparks a second at the top notch.")] [SerializeField]
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

        // A body going up in flames lights the room up for a moment; dies away to 0.
        private float _flare;

        private ShiftManager _game;

        // How hard the fire looks to be burning, 0 to 1 (see Update).
        private float _heat;

        // Seconds E has been held at the firebox, up to Tuning.lightTime; how far a fresh fire has come up
        // to full burn, 0 to 1; seconds the hopper has been empty while lit.
        private float _kindle, _catch, _starved;

        public float Hopper { get; private set; }

        // Everything poured into the hopper this shift.
        public float Delivered { get; private set; }

        // Everything burned, which leaves the coal total; and the electricity it made, in kWh.
        public float Burned { get; private set; }

        // Bodies thrown into the fire this shift.
        public int BodiesBurned { get; private set; }

        public float Output { get; private set; }

        // Coal actually burning per second: the notch's rate, or less once the hopper runs dry.
        public float BurnRate { get; private set; }

        public int Notch => burnLever ? burnLever.Notch : 0;

        // The shift starts with the fire out. It's lit by holding E at the firebox, and goes out again once
        // the hopper has been empty for Tuning.emberTime.
        public bool Lit { get; private set; }

        public Bounds Mouth => mouth;

        private void Awake()
        {
            _game = ShiftManager.Instance;
            Hopper = _game.Day.startingHopper;
        }

        // In Awake, so ShiftManager counts the starting hopper in its Start.
        private void Start()
        {
            burnLever.Text = _game.Lines.levers.burn;
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

            if (emberGlow)
            {
                // Embers glow reddest when the flames have died down; a roaring fire drowns them out.
                var strength = ember * (1 - .6f * _heat) * (.75f + .25f * flicker);
                var emission = emberGlow.emission;
                emission.rateOverTimeMultiplier = strength > .02f ? glowRate * strength : 0;
                var main = emberGlow.main;
                main.startColor = new Color(glowColor.r, glowColor.g, glowColor.b, .8f * strength);
            }

            _flare = Mathf.MoveTowards(_flare, 0, Time.deltaTime * .8f);
            if (fireLight)
            {
                // Embers cast a dim red light, flames a bright orange one; it wavers with the same noise as the glow.
                var strength = Mathf.Lerp(lightRange.x * ember, lightRange.y, _heat) * (.85f + .3f * flicker);
                fireLight.intensity = strength + _flare * 1.6f;
                fireLight.color = Color.Lerp(embers, flames, _heat);
                fireLight.pointLightOuterRadius = Mathf.Lerp(5.5f, 11f, _heat);
            }

            if (meterDisc) meterDisc.Rotate(0, 0, -BurnRate * meterSpin * Time.deltaTime);
        }

        private void FixedUpdate()
        {
            if (!_game.InShift)
            {
                BurnRate = 0;
                return;
            }

            var dt = Time.fixedDeltaTime;
            var t = _game.Tune;
            if (!Lit)
            {
                BurnRate = 0;
                return;
            }

            _catch = Mathf.Min(1, _catch + dt / t.catchTime);
            var used = Mathf.Min(Hopper, t.burnRates[Notch] * _catch * dt);
            _starved = Hopper > 0 ? 0 : _starved + dt;
            if (_starved >= t.emberTime)
            {
                Lit = false;
                _game.Say(_game.Lines.speech.fireOut);
            }

            Hopper -= used;
            Burned += used;
            Output += used * t.kWhPerCoal;
            BurnRate = used / dt;
        }

        public bool InMouth(Vector2 p)
        {
            return mouth.Contains(new Vector3(p.x, p.y, 0));
        }

        // Something thrown into the lit furnace (a cold one burns nothing, see CorpseBody): a shower of sparks and a flare of glow, and nothing left of it.
        public void Burn(Vector2 at)
        {
            if (sparks) sparks.Emit(50);
            if (emberGlow) emberGlow.Emit(6);
            _game.PlayCue("ignite", .5f);
            // Only the shift pays (the training's helper, say, is just a body).
            if (!_game.InShift) return;
            Output += _game.Tune.bodyOutput;
            _game.Warm(_game.Tune.bodyHeat);
            if (++BodiesBurned == 1) _game.Phone.Announce(_game.Lines.phone.burned);
        }

        // Called every frame while a body burns, `k` from 0 (just in) to 1 (nearly gone): sparks and glow thrown up from `at`,
        // and the firelight swelling.
        public void Cook(Vector2 at, float k)
        {
            if (sparks) sparks.Emit(Random.Range(1, 4));
            if (emberGlow && Random.value < .5f) emberGlow.Emit(1);
            _flare = Mathf.Max(_flare, 1 - k * .5f);
            _game.PlayCue("kindle", .35f);
        }

        public bool CanLight(PlayerController player)
        {
            return !Lit && !_game.Tutorial.Restricts && player.Lane == 0 && !player.Holding && !player.HandsFull &&
                   Mathf.Abs(player.Position.x - transform.position.x) < 1.3f &&
                   Mathf.Abs(player.Position.y - transform.position.y) < 1.6f;
        }

        // Called every step with whether the operator is holding E at the firebox. Letting go starts over.
        // Returns whether they're striking at it, for their animation.
        public bool Kindle(bool held, float dt)
        {
            if (!held)
            {
                _kindle = 0;
                return false;
            }

            if (Hopper <= 0)
            {
                _game.Say(_game.Lines.speech.nothingToLight);
                return false;
            }

            _kindle += dt;
            _game.PlayCue("kindle", .4f);
            if (_kindle < _game.Tune.lightTime) return true;
            Lit = true;
            _kindle = _catch = _starved = 0;
            _game.PlayCue("ignite");
            return false;
        }

        // Takes what fits of `amount`. Returns how much went in. `audible` is off for the grate's slow drip, which
        // would otherwise pour all shift long.
        public float Feed(float amount, bool audible = true)
        {
            var q = Mathf.Min(amount, _game.Tune.hopperCapacity - Hopper);
            if (q <= 0) return 0;
            Hopper += q;
            Delivered += q;
            if (audible) _game.PlayCue("pour");
            return q;
        }
    }
}
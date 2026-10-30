using CoalRun.Core;
using UnityEngine.Rendering.Universal;
using UnityEngine;

namespace CoalRun.Environment
{
    // The blizzard outside, as bad as the forecast says: the colder it gets, the harder it blows. One strength, 0 on the
    // shift's mildest stretch and 1 at its worst, with gusts on top, drives the snow past the window (a slow drift of
    // flakes when it's calm, sideways streaks at its worst), how much grey daylight gets in, how blue the room goes,
    // and the wind, rattles and knocks you hear.
    [UnityEngine.Scripting.APIUpdating.MovedFrom("CoalRun.Stations")]
    public class Storm : MonoBehaviour
    {
        [SerializeField] private ParticleSystem snow;

        [Tooltip("The outside temperatures at strength 0 and 1.")] [SerializeField]
        private Vector2 temperatureRange = new(-4, -38);

        [Tooltip("How fast the gusts come and go, calm and at worst.")] [SerializeField]
        private Vector2 gustSpeed = new(.12f, .45f);

        [Header("Snow")] [Tooltip("Flakes a second, calm and at worst.")] [SerializeField]
        private Vector2 rate = new(25, 240);

        [Tooltip("The wind across the window, m/s blowing left, calm and at worst.")] [SerializeField]
        private Vector2 windSpeed = new(.5f, 13);

        [SerializeField] private Vector2 fallSpeed = new(.9f, 3.2f);
        [SerializeField] private Vector2 flakeSize = new(.14f, .09f);

        [Tooltip("How far a flake stretches with its speed: round flakes when calm, streaks at worst.")]
        [SerializeField]
        private Vector2 streak = new(0, .05f);

        [Tooltip("How much the flakes flutter, calm and at worst.")] [SerializeField]
        private Vector2 flutter = new(.8f, .2f);

        [SerializeField] private Vector2 lifetime = new(3.2f, .7f);

        [Header("Light")] [SerializeField] private SpriteRenderer window;

        [SerializeField] private Light2D windowLight, ambient;
        [SerializeField] private Color windowCalm = Color.white, windowStorm = new(.52f, .56f, .66f);
        [SerializeField] private Color windowLightCalm = new(.82f, .9f, 1), windowLightStorm = new(.55f, .64f, .9f);
        [SerializeField] private Vector2 windowIntensity = new(1.4f, .45f);
        [SerializeField] private Color ambientCalm = new(.55f, .6f, .74f), ambientStorm = new(.34f, .48f, .95f);
        [SerializeField] private Vector2 ambientIntensity = new(.38f, .26f);

        [Header("Sounds")] [Tooltip("Rattles and knocks a second at full strength in a strong gust.")] [SerializeField]
        private float rattleRate = .6f;

        [SerializeField] private float knockRate = .25f;

        private ShiftManager _game;
        private float _gustTime;
        private ParticleSystemRenderer _snowRenderer;

        public float Strength { get; private set; }

        // How hard it's gusting, 0 to 1: slow swells, coming quicker the worse it is.
        public float Gust { get; private set; }

        // The strength with the gust on it, what the snow and the wind sound follow: up to 1.25 in a gust at its worst.
        public float Blow => Strength * (.75f + .5f * Gust);

        private void Start()
        {
            _game = ShiftManager.Instance;
            _snowRenderer = snow.GetComponent<ParticleSystemRenderer>();
        }

        private void Update()
        {
            var dt = Time.deltaTime;
            Strength = Mathf.InverseLerp(temperatureRange.x, temperatureRange.y, _game.Outside);
            _gustTime += dt * Mathf.Lerp(gustSpeed.x, gustSpeed.y, Strength);
            Gust = Mathf.SmoothStep(0, 1, Mathf.PerlinNoise(_gustTime, 7.3f));
            var k = Mathf.Clamp01(Blow);

            var emission = snow.emission;
            emission.rateOverTime = Mathf.Lerp(rate.x, rate.y, Strength) * (.7f + .6f * Gust);
            var wind = snow.velocityOverLifetime;
            wind.x = -Mathf.Lerp(windSpeed.x, windSpeed.y, k);
            wind.y = -Mathf.Lerp(fallSpeed.x, fallSpeed.y, k);
            var main = snow.main;
            var size = Mathf.Lerp(flakeSize.x, flakeSize.y, k);
            main.startSize = new ParticleSystem.MinMaxCurve(size * .6f, size);
            main.startLifetime = Mathf.Lerp(lifetime.x, lifetime.y, k);
            var noise = snow.noise;
            noise.strength = Mathf.Lerp(flutter.x, flutter.y, k);
            _snowRenderer.velocityScale = Mathf.Lerp(streak.x, streak.y, k);

            // The daylight greys over as it thickens, and dims a little more in each gust.
            var dark = Mathf.Clamp01(Strength + .2f * (Gust - .5f) * Strength);
            if (window) window.color = Color.Lerp(windowCalm, windowStorm, dark);
            if (windowLight)
            {
                windowLight.intensity = Mathf.Lerp(windowIntensity.x, windowIntensity.y, dark);
                windowLight.color = Color.Lerp(windowLightCalm, windowLightStorm, dark);
            }

            if (ambient)
            {
                ambient.intensity = Mathf.Lerp(ambientIntensity.x, ambientIntensity.y, Strength);
                ambient.color = Color.Lerp(ambientCalm, ambientStorm, Strength);
            }

            // The building takes it: the window rattles in the gusts, and something loose knocks once it's bad.
            var loud = .35f + .65f * Strength;
            if (Random.value < rattleRate * Mathf.Max(.08f, k * Gust) * dt) _game.PlayCue("rattle", .4f, loud);
            if (Random.value < knockRate * Strength * Strength * Gust * dt) _game.PlayCue("knock", .6f, loud);
        }
    }
}
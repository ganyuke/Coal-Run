using CoalRun.Core;
using UnityEngine;

namespace CoalRun.Stations
{
    // The freight lift's shaft, dressed to show the lift working: its guide shoes spark on the rails, more
    // the faster it goes and in a burst when it jolts to a stop; a counterweight on the other end of its
    // cable goes the opposite way over a turning pulley; a caged work lamp swings when it jolts; and coal
    // dust trickles off the platform's edges, puffing up when it comes down on coal. Only looks: nothing
    // here touches the lift.
    public class LiftShaft : MonoBehaviour
    {
        [SerializeField] private Lift lift;

        [Header("Sparks")]
        [Tooltip("Where the guide shoes run on the rails, relative to the platform's centre and top.")]
        [SerializeField]
        private Vector2[] shoes = { new(-1.6f, -.15f), new(1.6f, -.15f) };
        [Tooltip("Sparks a second per shoe per m/s, above a crawl.")]
        [SerializeField]
        private float sparksPerMetre = 5;
        [SerializeField] private ParticleSystem sparks;
        [Tooltip("A change of speed in one step, in m/s, above what the lift's own braking does, that counts as a jolt.")]
        [SerializeField]
        private float joltAt = .3f;

        [Tooltip("Sparks per shoe when the platform arrives at the top or the bottom, at a crawl, and at its top speed.")]
        [SerializeField]
        private Vector2 endSparks = new(4, 10);
        [Tooltip("The size of those sparks, about the same as the running ones.")]
        [SerializeField]
        private Vector2 endSparkSize = new(.06f, .12f);
        [Tooltip("Slower than this over the trip and it arrives quietly, with no burst.")]
        [SerializeField]
        private float endSparkSpeed = .8f;

        [Header("Counterweight")]
        [SerializeField] private Transform pulley;
        [SerializeField] private float pulleyRadius = .4f;
        [Tooltip("Its centre's height with the platform at the bottom; it drops as the platform rises.")]
        [SerializeField]
        private Transform counterweight;
        [SerializeField] private float weightTop = 11.5f;
        [Tooltip("Cables hung from the pulley's rim, scaled down to the platform frame and to the counterweight.")]
        [SerializeField]
        private Transform platformCable;
        [SerializeField] private Transform weightCable;
        [Tooltip("The platform frame's top, above the platform's top, where its cable is made fast.")]
        [SerializeField]
        private float frameHeight = 2.1f;

        [Header("Lamp")]
        [Tooltip("The caged work lamp's pivot. It swings as a pendulum.")]
        [SerializeField]
        private Transform lamp;
        [SerializeField] private float lampLength = .7f;
        [Tooltip("Degrees a second of swing per m/s of jolt.")]
        [SerializeField]
        private float lampKick = 120;
        [Tooltip("The lamp's glow, flickering, and dimmed while it swings hard.")]
        [SerializeField]
        private SpriteRenderer glow;

        [Header("Dust")]
        [SerializeField] private ParticleSystem dust;
        [Tooltip("Specks a second per m/s off each edge, more with coal on the platform.")]
        [SerializeField]
        private float dustPerMetre = 2;

        private ShiftManager _game;
        private float _lastSpeed, _sparkDebt, _dustDebt;
        private float _swing, _swingSpeed;
        // The fastest it went since it left an end of the shaft, and whether it was at one last step.
        private float _peak;
        private bool _wasAtEnd = true;
        private Color _glowColor;

        private void Start()
        {
            _game = ShiftManager.Instance;
            if (glow) _glowColor = glow.color;
        }

        private void FixedUpdate()
        {
            var dt = Time.fixedDeltaTime;
            var speed = lift.Speed;
            var y = lift.Y;
            // More than braking or speeding up can do in a step: it hit something or slammed into the end.
            var jolt = Mathf.Abs(speed - _lastSpeed) - _game.Tune.liftAcceleration * dt;
            // Coming down on coal in the shaft, it squashes it flat: dust puffs out from under.
            var peak = _game.Coal.ShaftPeak();
            var squashing = _lastSpeed < -.05f && peak > .05f && y < Room.Lower + peak + .05f;
            if (jolt > joltAt)
            {
                Sparks(y, 4 + jolt * 4, speed);
                // Always the same way for the same jolt, so a stop spread over a few steps adds up instead of cancelling.
                _swingSpeed += lampKick * jolt * Mathf.Sign(speed - _lastSpeed);
                if (squashing) Puff(y, 10 + jolt * 8);
            }
            else if (squashing && Random.value < .3f) Puff(y, 1);
            _lastSpeed = speed;

            // Arriving at the top or the bottom throws a big burst off the shoes, more the faster the trip was.
            var atTop = y >= Room.Upper - .03f;
            var atEnd = atTop || y <= Room.Lower + .03f;
            if (!atEnd) _peak = Mathf.Max(_peak, Mathf.Abs(speed));
            else if (!_wasAtEnd)
            {
                if (_peak > endSparkSpeed) EndSparks(y, _peak, atTop);
                _peak = 0;
            }
            _wasAtEnd = atEnd;

            var crawl = Mathf.Abs(speed) - .3f;
            if (crawl <= 0) return;
            _sparkDebt += crawl * sparksPerMetre * dt;
            if (_sparkDebt >= 1)
            {
                Sparks(y, Mathf.Floor(_sparkDebt), speed);
                _sparkDebt -= Mathf.Floor(_sparkDebt);
            }
            _dustDebt += crawl * dustPerMetre * (1 + _game.Coal.LiftCoal) * dt;
            while (_dustDebt >= 1)
            {
                _dustDebt--;
                var side = Random.value < .5f ? -1 : 1;
                Emit(dust, new Vector2(Room.LiftX + side * (Room.LiftWidth / 2 - .05f), y - .1f), new Vector2(side * Random.Range(0, .3f), speed * .5f), Random.Range(.03f, .06f), 1.2f);
            }
        }

        // A burst off both shoes like the running sparks, but thrown the other way: up and out off the floor of the shaft, or down and out off the top.
        private void EndSparks(float y, float peak, bool top)
        {
            if (!sparks) return;
            var count = Mathf.RoundToInt(Mathf.Lerp(endSparks.x, endSparks.y, Mathf.InverseLerp(endSparkSpeed, _game.Tune.liftMaxSpeed, peak)));
            foreach (var shoe in shoes)
            {
                var at = new Vector2(Room.LiftX + shoe.x, y + shoe.y);
                var inward = -Mathf.Sign(shoe.x);
                for (var i = 0; i < count; i++)
                    Emit(sparks, at, new Vector2(inward * Random.Range(.5f, 3.5f), top ? Random.Range(-3f, 1f) : Random.Range(.5f, 4f)), Random.Range(endSparkSize.x, endSparkSize.y), Random.Range(.25f, .5f));
            }
        }

        // Each shoe throws them out off the rail; the ones left behind as it moves trail off the other way.
        private void Sparks(float y, float count, float speed)
        {
            if (!sparks) return;
            foreach (var shoe in shoes)
            {
                var at = new Vector2(Room.LiftX + shoe.x, y + shoe.y);
                var inward = -Mathf.Sign(shoe.x);
                for (var i = 0; i < count; i++)
                    Emit(sparks, at, new Vector2(inward * Random.Range(.5f, 3f), -speed * Random.Range(.2f, .6f) + Random.Range(-.5f, 2f)), Random.Range(.06f, .11f), Random.Range(.2f, .45f));
            }
        }

        private void Puff(float y, float count)
        {
            if (!dust) return;
            for (var i = 0; i < count; i++)
            {
                var side = Random.value < .5f ? -1 : 1;
                Emit(dust, new Vector2(Room.LiftX + side * Random.Range(.8f, Room.LiftWidth / 2), y), new Vector2(side * Random.Range(.4f, 1.6f), Random.Range(.2f, 1)), Random.Range(.12f, .25f), Random.Range(.8f, 1.4f));
            }
        }

        // For the rest of the room's coal, so it kicks up the same dust as coal squashed in the shaft.
        public void Dust(Vector2 at, Vector2 velocity, float size, float life) => Emit(dust, at, velocity, size, life);

        private static void Emit(ParticleSystem system, Vector2 at, Vector2 velocity, float size, float life) =>
            system.Emit(new ParticleSystem.EmitParams { position = at, velocity = velocity, startSize = size, startLifetime = life }, 1);

        private void Update()
        {
            var y = lift.Y;
            var top = pulley ? pulley.position.y : 0;
            // One cable over the pulley: the platform end shortens as it rises, the counterweight's lengthens.
            if (counterweight) counterweight.position = new Vector3(counterweight.position.x, weightTop - (y - Room.Lower), counterweight.position.z);
            if (pulley) pulley.localRotation = Quaternion.Euler(0, 0, -y / pulleyRadius * Mathf.Rad2Deg);
            if (platformCable) Hang(platformCable, top, y + frameHeight);
            if (weightCable && counterweight) Hang(weightCable, top, counterweight.position.y + counterweight.lossyScale.y / 2);

            // A damped pendulum, kept shivering a little while the lift runs.
            var dt = Time.deltaTime;
            _swingSpeed += (-Physics2D.gravity.magnitude / lampLength * Mathf.Sin(_swing * Mathf.Deg2Rad) * Mathf.Rad2Deg - _swingSpeed * .8f) * dt;
            _swing = Mathf.Clamp(_swing + _swingSpeed * dt, -60, 60);
            var shiver = Mathf.Min(Mathf.Abs(lift.Speed), 4) * .6f * Mathf.Sin(Time.time * 47);
            if (lamp) lamp.localRotation = Quaternion.Euler(0, 0, _swing + shiver);
            if (!glow) return;
            var flicker = .85f + .15f * Mathf.PerlinNoise(Time.time * 6, 0) - Mathf.Min(.3f, Mathf.Abs(_swingSpeed) / 400);
            glow.color = new Color(_glowColor.r, _glowColor.g, _glowColor.b, _glowColor.a * flicker);
        }

        // Stretches a cable sprite (pivot at its centre, 1 unit long) between two heights.
        private static void Hang(Transform cable, float from, float to)
        {
            cable.position = new Vector3(cable.position.x, (from + to) / 2, cable.position.z);
            cable.localScale = new Vector3(cable.localScale.x, Mathf.Max(0, from - to), 1);
        }
    }
}

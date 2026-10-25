using System.Collections.Generic;
using System;
using CoalRun.Stations;
using UnityEngine.SceneManagement;
using UnityEngine;

namespace CoalRun.Core
{
    // Leaving: the clock has run out and the operator is on their way to the staff lift to clock out.
    public enum Phase { Tutorial, Briefing, Shift, Leaving, Replacement, Complete, Frozen }

    // The hub. Holds the state every station shares (phase, clock, the day's settings) and the
    // references stations use to find each other. UI and audio listen to its events instead of
    // stations calling them directly.
    [DefaultExecutionOrder(-50)]
    public class ShiftManager : MonoBehaviour
    {
        public static ShiftManager Instance { get; private set; }

        [SerializeField] private Tuning tuning;
        [SerializeField] private DayConfig day;
        [SerializeField] private InputReader input;
        [SerializeField] private PlayerController player;
        [SerializeField] private Cart cart;
        [SerializeField] private Lift lift;
        [SerializeField] private CoalField coal;
        [SerializeField] private Chute chute;
        [SerializeField] private Furnace furnace;
        [SerializeField] private TipCradle cradle;
        [SerializeField] private StaffLift staffLift;
        [SerializeField] private RedPhone phone;

        public Tuning Tune => tuning;
        public DayConfig Day => day;
        public PlayerController Player => player;
        public Cart Cart => cart;
        public Lift Lift => lift;
        public CoalField Coal => coal;
        public Chute Chute => chute;
        public Furnace Furnace => furnace;
        public TipCradle Cradle => cradle;
        public RedPhone Phone => phone;

        public Phase Phase { get; private set; } = Phase.Shift;
        public bool Ended => Phase is Phase.Complete or Phase.Frozen;
        public bool CanAct => Phase is Phase.Tutorial or Phase.Shift or Phase.Leaving;
        // The shift's clock and the building's heat run on while a replacement is on the way.
        public bool ShiftRunning => Phase == Phase.Shift || Phase == Phase.Replacement && _resume == Phase.Shift;
        // Operators lost this shift, and the seconds until the next one steps out of the staff lift.
        public int Deaths { get; private set; }
        public float ReplacementIn { get; private set; }
        // Dead operators, left where they fell for the rest of the shift.
        public List<Rigidbody2D> Bodies { get; } = new();
        public float Elapsed { get; private set; }
        // Seconds into the shift, the temperature outside from the day's forecast, and inside the building.
        public float Clock { get; private set; }
        public float Outside { get; private set; }
        public float Temperature { get; private set; }
        // How far through the freeze grace the operator is, 0 to 1 (1 is frozen). Kept hidden: they only
        // feel it in their legs, and the lamp over the staff lift is lit while it's freezing.
        public float Chill { get; private set; }
        public bool Freezing => Temperature <= tuning.freeze;
        public string Message { get; private set; } = "";
        public float MessageFor { get; private set; }
        public float InitialCoal { get; private set; }
        // Every unit of coal is always in exactly one place, so this should never change.
        public float TotalCoal => chute.Bunker + cart.Coal + player.Hands + coal.Loose + furnace.Hopper + furnace.Burned;

        // name, loudness
        public event Action<string, float> OnSound;
        public event Action<Phase> OnPhase;

        private readonly Dictionary<string, float> _soundTimers = new();
        // The phase to go back to once the replacement arrives.
        private Phase _resume;
        private bool _doorOpened;

        private void Awake()
        {
            Instance = this;
            Time.fixedDeltaTime = tuning.step;
            Physics2D.gravity = new Vector2(0, tuning.gravity);
            GameLayers.ConfigureCollisions();
        }

        private void Start()
        {
            InitialCoal = TotalCoal;
            Temperature = tuning.comfortable;
            Outside = day.OutsideAt(0);
        }

        private void FixedUpdate()
        {
            var dt = Time.fixedDeltaTime;
            Elapsed += dt;
            MessageFor -= dt;
            if (input.Current.DebugKey != 0) DebugCommand(input.Current.DebugKey);
            if (ShiftRunning) StepHeat(dt);
            if (Phase == Phase.Replacement) StepReplacement(dt);
            if (Ended && input.Current.Restart) Restart();
            RecoverLost();
        }

        // The building warms by what the furnace burns and loses heat to the outside in proportion to the
        // difference, so it changes slowly: run under demand and it cools gradually. Burning a notch's rate
        // holds it at `comfortable` against that notch's held temperature. The furnace runs after the hub,
        // so this is the last step's burn.
        private void StepHeat(float dt)
        {
            Clock += dt;
            Outside = day.OutsideAt(Clock);
            Temperature += (furnace.BurnRate - tuning.heatLoss * (Temperature - Outside)) / tuning.heatCapacity * dt;
            if (Freezing && Chill == 0) Say("It's freezing in here. Get the fire up.", 4);
            Chill = Mathf.Clamp01(Chill + (Freezing ? dt : -dt) / tuning.freezeGrace);
            if (Chill >= 1) SetPhase(Phase.Frozen);
            else if (Clock >= tuning.shiftLength)
            {
                // A replacement still on the way arrives to a finished shift.
                if (Phase == Phase.Replacement) _resume = Phase.Leaving;
                else SetPhase(Phase.Leaving);
                Say("The shift is over. Take the staff lift up.", 6);
            }
        }

        // At the staff lift once the shift is over.
        public void ClockOut()
        {
            if (Phase != Phase.Leaving) return;
            if (staffLift) staffLift.Cycle();
            SetPhase(Phase.Complete);
        }

        // Once the shift has ended the world keeps running, but nobody can act, the clock stops and the
        // furnace stops burning. While leaving, the clock is stopped and the building can't freeze any more.
        private void SetPhase(Phase phase)
        {
            Phase = phase;
            OnPhase?.Invoke(phase);
            Sound(phase.ToString().ToLowerInvariant());
        }

        // Reloading the scene puts every station back as it was.
        public void Restart() => SceneManager.LoadScene(gameObject.scene.path);

        // Testing shortcuts, by F key. Public so the editor CLI can call them too. Coal is moved to and from
        // the bunker, so the total stays intact.
        public void DebugCommand(int key)
        {
            switch (key)
            {
                case 2: // A wheel knocked off as in a crash; with none left, both back on and the loose ones gone.
                    if (cart.Wheels > 0)
                    {
                        cart.PopWheel();
                        break;
                    }
                    while (cart.LooseWheels.Count > 0) cart.RemoveWheel(cart.LooseWheels[0]);
                    cart.SetWheels(2);
                    Say("Debug: both wheels back on.", 2);
                    break;
                case 4: // Load empty → 6 → full → overfilled → empty.
                    var load = cart.Coal < 5.9f ? 6 : cart.Coal < tuning.cartCapacity - .1f ? tuning.cartCapacity : cart.Coal < tuning.overfillCapacity - .1f ? tuning.overfillCapacity : 0;
                    chute.Bunker += cart.Coal - load;
                    cart.Coal = load;
                    Say($"Debug: cart holds {load:0}.", 2);
                    break;
                case 5: // Loose coal back to the bunker.
                    Coal.ReturnAll();
                    Say("Debug: loose coal cleaned up.", 2);
                    break;
                case 6: // The cart, upright and still, beside the operator on their floor.
                    var p = player.Position;
                    float side = player.Facing, floor = Coal.SurfaceAt(p.x, p.y).Y;
                    cart.Body.position = new Vector2(Mathf.Clamp(p.x + side * 1.6f, 1.45f, 30.55f), floor + .7f);
                    cart.Body.rotation = 0;
                    cart.Body.linearVelocity = Vector2.zero;
                    cart.Body.angularVelocity = 0;
                    cart.transform.SetPositionAndRotation(cart.Body.position, Quaternion.identity);
                    Say("Debug: cart brought over.", 2);
                    break;
            }
        }

        public void Say(string message, float duration = 5)
        {
            Message = message;
            MessageFor = duration;
        }

        // Repeats of the same sound inside the cooldown are dropped, so stations can call this every step.
        public void Sound(string sound, float cooldown = .12f, float amount = 1)
        {
            if (_soundTimers.TryGetValue(sound, out var until) && until > Elapsed) return;
            _soundTimers[sound] = Elapsed + cooldown;
            OnSound?.Invoke(sound, amount);
        }

        // Accidents kill only the operator. Brass sends a replacement down the staff lift, and the furnace
        // keeps burning while they ride down: a death costs time and nothing else.
        public void Die(string reason)
        {
            if (!CanAct) return;
            _resume = Phase;
            Deaths++;
            ReplacementIn = tuning.replacementDelay;
            _doorOpened = false;
            Bodies.Add(player.Kill());
            SetPhase(Phase.Replacement);
            Say($"{reason}. Another worker is on the way.", 7);
            Sound("hurt");
        }

        private void StepReplacement(float dt)
        {
            ReplacementIn -= dt;
            // The door opens just before they step out.
            if (!_doorOpened && ReplacementIn <= .5f)
            {
                _doorOpened = true;
                if (staffLift) staffLift.Cycle();
            }
            if (ReplacementIn > 0) return;
            player.Respawn(new Vector2(30.5f, Room.Lower + .72f));
            SetPhase(_resume);
            Say("Management sent another operator.", 4);
        }

        // Nothing can leave the room. Anything physics squeezes through a wall or floor is put back on the lower floor.
        private void RecoverLost()
        {
            if (Lost(cart.Body.position))
            {
                player.ReleaseGrip();
                Put(cart.Body, Room.Lower + .72f);
            }
            foreach (var wheel in cart.LooseWheels)
                if (Lost(wheel.position))
                    Put(wheel, Room.Lower + .35f);
            foreach (var body in Bodies)
                if (Lost(body.position))
                    Put(body, Room.Lower + .3f);
            if (!player.Body.simulated || !Lost(player.Body.position)) return;
            player.ReleaseGrip();
            Put(player.Body, Room.Lower + .72f);
            return;

            static bool Lost(Vector2 p) => p is not { x: > 0 and < Room.Width, y: > Room.Lower - 1.5f and < Room.Height + 4 };
            static void Put(Rigidbody2D b, float y)
            {
                var x = float.IsFinite(b.position.x) ? Mathf.Clamp(b.position.x, 5.5f, 28.5f) : 14;
                b.position = new Vector2(x, y);
                b.rotation = 0;
                b.linearVelocity = Vector2.zero;
                b.angularVelocity = 0;
            }
        }
    }
}

using System.Collections.Generic;
using System.Collections;
using System;
using CoalRun.Data;
using CoalRun.Dev;
using CoalRun.Dialogue;
using CoalRun.Environment;
using CoalRun.Gameplay.Cart;
using CoalRun.Gameplay.Coal;
using CoalRun.Gameplay.Furnace;
using CoalRun.Gameplay.Lift;
using CoalRun.Gameplay.Player;
using CoalRun.Gameplay.Tutorial;
using CoalRun.UI;
using UnityEngine.SceneManagement;
using UnityEngine;

namespace CoalRun.Core
{
    // Leaving: the clock has run out and the operator is on their way to the staff lift to clock out.
    public enum Phase
    {
        Tutorial,
        Briefing,
        Shift,
        Leaving,
        Replacement,
        Complete,
        Frozen
    }

    // The hub. Holds the state every station shares (phase, clock, the day's settings) and the
    // references stations use to find each other. UI and audio listen to its events instead of
    // stations calling them directly.
    [DefaultExecutionOrder(-50)]
    public class ShiftManager : MonoBehaviour
    {
        // The training plays once a session: a restart goes straight to the briefing. Static, so it
        // survives the scene reload; cleared on entering Play mode, since domain reload may be off.
        private static bool _trained;

        // Which shift we are on. Static like _trained, for the same reason.
        private static int _dayIndex;

        // Out of the staff lift.
        private Vector2 ArrivalSpot => room.Arrival;

        [Header("Configuration")]
        [SerializeField] private Tuning tuning;
        [SerializeField] private DayConfig day;

        [Tooltip("Every line of text the player reads.")] [SerializeField]
        private Lines lines;

        [Tooltip("The shifts in order. A survived shift moves on to the next; empty: just the one above.")]
        [SerializeField]
        private DayConfig[] days;

        [Header("Player and stations")]
        [SerializeField] private Room room;
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
        [SerializeField] private Storm storm;

        [Tooltip("Owns the coal dust particles, which the rest of the room's coal borrows (CoalDust).")]
        [SerializeField]
        private LiftShaft liftShaft;

        [Header("Progress and presentation")]
        [SerializeField] private Tutorial tutorial;
        [SerializeField] private Notebook notebook;
        [SerializeField] private CameraRig cameraRig;

        [Header("Arrival")]
        [Tooltip("Seconds from Play until the first operator steps out of the staff lift.")] [SerializeField]
        private float firstArrival = 1.2f;

        // Audio cooldowns.
        private readonly Dictionary<string, float> _soundTimers = new();

        // Clocking out.
        private float _inLiftFor;

        // Worker arrival: the door and the phase to return to afterward.
        private bool _doorOpened;
        private Phase _resume;

        public static ShiftManager Instance { get; private set; }

        public Room Room => room;
        public Tuning Tune => tuning;
        public Lines Lines => lines;
        public DayConfig Day => days is { Length: > 0 } ? days[Mathf.Clamp(_dayIndex, 0, days.Length - 1)] : day;
        public bool HasNextDay => days != null && _dayIndex < days.Length - 1;
        public PlayerController Player => player;
        public Cart Cart => cart;
        public Lift Lift => lift;
        public CoalField Coal => coal;
        public Chute Chute => chute;
        public Furnace Furnace => furnace;
        public TipCradle Cradle => cradle;
        public RedPhone Phone => phone;
        public Storm Storm => storm;
        public Tutorial Tutorial => tutorial;
        public Notebook Notebook => notebook;

        public Phase Phase { get; private set; } = Phase.Shift;

        // Training counts as going on while a replacement is on the way in the middle of it.
        public bool InTraining => Phase == Phase.Tutorial || (Phase == Phase.Replacement && _resume == Phase.Tutorial);

        public bool Ended => Phase is Phase.Complete or Phase.Frozen;

        // The room is live: the training or the shift, including the leaving and a replacement on the way.
        public bool Playing => Phase is Phase.Tutorial or Phase.Shift or Phase.Leaving or Phase.Replacement;

        // The same, minus the training: the furnace burns, the building cools, and what happens counts.
        public bool InShift => Phase is Phase.Shift or Phase.Leaving or Phase.Replacement;

        public bool CanAct => !Departing && Phase is Phase.Tutorial or Phase.Shift or Phase.Leaving;

        // In the staff lift with the door closing: the operator is locked in place, and the screen fades out.
        public bool Departing { get; private set; }

        public float Fade { get; private set; }

        // The shift is over and whatever leads up to the report (the fade out of the staff lift, or the operator
        // freezing to death) has played: the report can come up.
        public bool ReportReady { get; private set; }

        // The shift's clock and the building's heat run on while a replacement is on the way.
        public bool ShiftRunning => Phase == Phase.Shift || (Phase == Phase.Replacement && _resume == Phase.Shift);

        // Operators lost this shift, and the seconds until the next one steps out of the staff lift.
        public int Deaths { get; private set; }

        // How the last one died, for the report.
        public string LastDeath { get; private set; } = "";

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

        public float InitialCoal { get; private set; }

        // Every unit of coal is always in exactly one place, so this should never change.
        public float TotalCoal =>
            chute.Bunker + cart.Coal + player.Hands + coal.Loose + furnace.Hopper + furnace.Burned;

        // name, loudness
        public event Action<string, float> OnSound;
        public event Action<Phase> OnPhase;

        // Unity lifecycle

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetTraining()
        {
            _trained = false;
            _dayIndex = 0;
        }

        private void Awake()
        {
            Instance = this;
            if (!room) room = FindFirstObjectByType<Room>();
            room?.Bind();
            Phase = Day.training && !_trained ? Phase.Tutorial : Phase.Briefing;
            Time.fixedDeltaTime = tuning.step;
            Physics2D.gravity = new Vector2(0, tuning.gravity);
            GameLayers.ConfigureCollisions();
        }

        private void Start()
        {
            InitialCoal = TotalCoal;
            Temperature = tuning.comfortable;
            Outside = Day.OutsideAt(0);
            // The training opens with the new operator (and the helper, see Tutorial) riding down the staff
            // lift, the same way a replacement arrives.
            if (Phase == Phase.Tutorial) SendOperator(firstArrival);
        }

        private void FixedUpdate()
        {
            var dt = Time.fixedDeltaTime;
            Elapsed += dt;
            if (input.Current.DebugKey != 0) DebugCommand(input.Current.DebugKey);
            if (ShiftRunning) StepHeat(dt);
            if (Phase == Phase.Replacement) StepReplacement(dt);
            if (Phase == Phase.Leaving && !Departing) StepLeaving(dt);
            if (Ended && input.Current.Restart) Continue();
            if (Phase == Phase.Briefing && input.Current.Begin) BeginShift();
            RecoverLost();
        }

        // Shift phases and day progression

        public void FinishTraining()
        {
            _trained = true;
            // Skipped (F8) before they've even stepped out of the lift.
            if (Phase == Phase.Replacement) Arrive();
            SetPhase(Phase.Briefing);
        }

        // The clock starts from the briefing, and the training's deaths don't go on the report.
        public void BeginShift()
        {
            if (Phase != Phase.Briefing) return;
            Clock = 0;
            Deaths = 0;
            Temperature = tuning.comfortable;
            SetPhase(Phase.Shift);
        }

        // Once the shift has ended the world keeps running, but nobody can act, the clock stops and the
        // furnace stops burning. While leaving, the clock is stopped and the building can't freeze any more.
        private void SetPhase(Phase phase)
        {
            Phase = phase;
            // The door stands open for the operator to leave by.
            if (phase == Phase.Leaving && staffLift) staffLift.Open();
            OnPhase?.Invoke(phase);
            PlayCue(phase.ToString().ToLowerInvariant());
        }

        // Reloading the scene puts every station back as it was.
        public void Restart()
        {
            SceneManager.LoadScene(gameObject.scene.path);
        }

        // What the report's button does: a survived shift goes on to the next day (or back to the first after the last);
        // a lost one is run again.
        public void Continue()
        {
            if (Phase == Phase.Complete) _dayIndex = HasNextDay ? _dayIndex + 1 : 0;
            Restart();
        }

        // Temperature and shift clock

        // A flare of heat from something thrown into the fire.
        public void Warm(float degrees)
        {
            Temperature += degrees;
        }

        // The building warms by what the furnace burns and loses heat to the outside in proportion to the
        // difference, so it changes slowly: run under demand and it cools gradually. Burning a notch's rate
        // holds it at `comfortable` against that notch's held temperature. The furnace runs after the hub,
        // so this is the last step's burn.
        private void StepHeat(float dt)
        {
            Clock += dt;
            Outside = Day.OutsideAt(Clock);
            Temperature += (furnace.BurnRate - tuning.heatLoss * (Temperature - Outside)) / tuning.heatCapacity * dt;
            if (Freezing && Chill == 0) Say(lines.speech.freezing);
            Chill = Mathf.Clamp01(Chill + (Freezing ? dt : -dt) / tuning.freezeGrace);
            if (Chill >= 1)
            {
                SetPhase(Phase.Frozen);
                StartCoroutine(FreezeEnd());
            }
            else if (Clock >= tuning.shiftLength)
            {
                // A replacement still on the way arrives to a finished shift.
                if (Phase == Phase.Replacement) _resume = Phase.Leaving;
                else SetPhase(Phase.Leaving);
                Say(lines.speech.shiftOver);
            }
        }

        // Death and replacement workers

        // Accidents kill only the operator. Brass sends a replacement down the staff lift, and the furnace
        // keeps burning while they ride down: a death costs time and nothing else.
        public void Die(string reason)
        {
            if (!CanAct) return;
            Deaths++;
            LastDeath = reason;
            Bodies.Add(player.Kill());
            SendOperator(tuning.replacementDelay);
            // Brass rings down to say another man is on his way, rather than anyone saying why this one died.
            var deaths = lines.phone.deaths;
            phone.Announce(deaths[Mathf.Min(Deaths, deaths.Length) - 1], 2);
            PlayCue("hurt");
        }

        // An operator on the way down the staff lift, out in `delay` seconds. Only a replacement once
        // someone has died (Deaths); the first one down is just arriving.
        private void SendOperator(float delay)
        {
            _resume = Phase;
            ReplacementIn = delay;
            _doorOpened = false;
            player.WaitInLift(ArrivalSpot);
            SetPhase(Phase.Replacement);
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
            Arrive();
            SetPhase(_resume);
        }

        private void Arrive()
        {
            player.Respawn(ArrivalSpot);
        }

        // Clocking out and end-of-shift presentation

        // The shift is over and the staff lift's door stands open. Standing still inside it for a moment ends the shift.
        private void StepLeaving(float dt)
        {
            var p = player.Position;
            // Standing in the car, not just running through it.
            var inside = player.Lane == 0 && p.y < room.Lower + 2.3f && p.x > room.StaffLiftInside &&
                         Mathf.Abs(player.Body.linearVelocity.x) < .5f;
            _inLiftFor = inside ? _inLiftFor + dt : 0;
            if (_inLiftFor >= tuning.liftStandTime) StartCoroutine(Depart());
        }

        // Locked in place, the door closes, and the screen goes black.
        private IEnumerator Depart()
        {
            Departing = true;
            player.Stand(room.StaffLiftCenter);
            if (staffLift) yield return staffLift.CloseDoor();
            for (var k = 0f; k < 1; k += Time.deltaTime / tuning.fadeTime)
            {
                Fade = k;
                yield return null;
            }

            Fade = 1;
            SetPhase(Phase.Complete);
            ReportReady = true;
            // The door has closed and the shift is over: nobody acts in Complete anyway, and while this stayed set the
            // report page was hidden (NotebookView) over a black screen.
            Departing = false;
        }

        // The cold gets them: the camera comes in on the operator, they keel over, it pulls back out, the screen goes
        // black, and then the report comes up.
        private IEnumerator FreezeEnd()
        {
            var at = player.Position + Vector2.up * .5f;
            if (cameraRig) yield return cameraRig.FocusOn(at, tuning.freezeZoom, tuning.freezeZoomIn);
            yield return new WaitForSeconds(tuning.freezeHold);
            // Nobody to keel over if they were already dead and the next one hadn't come down yet.
            if (player.Visible) Bodies.Add(player.Kill());
            PlayCue("hurt");
            yield return new WaitForSeconds(tuning.freezeKeel);
            if (cameraRig) yield return cameraRig.Home(tuning.freezeZoomOut);
            for (var k = 0f; k < 1; k += Time.deltaTime / tuning.fadeTime)
            {
                Fade = k;
                yield return null;
            }

            Fade = 1;
            ReportReady = true;
        }

        // Dialogue, audio and shared particles

        // Said by `speaker`, as little text above them. By default it's the operator's, and dropped while there's no
        // operator on the floor (dead, or the replacement still riding down in the staff lift): nobody saw it happen.
        public void Say(string message, Speaker speaker = null)
        {
            if (!speaker && (!player.Visible || Phase == Phase.Replacement)) return;
            speaker ??= player.Voice;
            if (speaker) speaker.Speak(message);
        }

        // Repeats of the same sound inside the cooldown are dropped, so stations can call this every step.
        public void PlayCue(string sound, float cooldown = .12f, float amount = 1)
        {
            if (_soundTimers.TryGetValue(sound, out var until) && until > Elapsed) return;
            _soundTimers[sound] = Elapsed + cooldown;
            OnSound?.Invoke(sound, amount);
        }

        // A speck of the lift shaft's coal dust, for coal being trampled or heaved about elsewhere.
        public void CoalDust(Vector2 at, Vector2 velocity, float size, float life)
        {
            if (liftShaft) liftShaft.Dust(at, velocity, size, life);
        }

        // Recovery and development shortcuts

        // Nothing can leave the room. Anything physics squeezes through a wall or floor is put back on the lower floor.
        private void RecoverLost()
        {
            if (Lost(cart.Body.position))
            {
                player.ReleaseGrip();
                Put(cart.Body, room.Lower + .72f);
            }

            foreach (var wheel in cart.LooseWheels)
                if (Lost(wheel.position))
                    Put(wheel, room.Lower + .35f);
            foreach (var body in Bodies)
                if (Lost(body.position))
                    Put(body, room.Lower + .3f);
            if (!player.Body.simulated || !Lost(player.Body.position)) return;
            player.ReleaseGrip();
            Put(player.Body, room.Lower + .72f);
            return;

            bool Lost(Vector2 p)
            {
                return p.x <= room.Left || p.x >= room.Right || p.y <= room.Lower - 1.5f || p.y >= room.Top + 4;
            }

            static void Put(Rigidbody2D b, float y)
            {
                var x = float.IsFinite(b.position.x) ? Mathf.Clamp(b.position.x, 5.5f, 28.5f) : 14;
                b.position = new Vector2(x, y);
                b.rotation = 0;
                b.linearVelocity = Vector2.zero;
                b.angularVelocity = 0;
            }
        }

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
                    Say("Debug: both wheels back on.");
                    break;
                case 4: // Load empty → 6 → full → overfilled → empty.
                    var load = cart.Coal < 5.9f ? 6 :
                        cart.Coal < tuning.cartCapacity - .1f ? tuning.cartCapacity :
                        cart.Coal < tuning.overfillCapacity - .1f ? tuning.overfillCapacity : 0;
                    chute.Bunker += cart.Coal - load;
                    cart.Coal = load;
                    Say($"Debug: cart holds {load:0}.");
                    break;
                case 5: // Loose coal back to the bunker.
                    Coal.ReturnAll();
                    Say("Debug: loose coal cleaned up.");
                    break;
                case 7: // The training again, from the top.
                    _trained = false;
                    Restart();
                    break;
                case 8: // Past the training and the briefing, into the shift.
                    tutorial.Skip();
                    BeginShift();
                    break;
                case 10: // The clock jumps to the last 30 seconds of the shift.
                    if (ShiftRunning) Clock = Mathf.Max(Clock, tuning.shiftLength - 30);
                    break;
                case 9: // The bot plays the shift (balance testing); F9 again stops it.
                    ShiftBot.Run(this);
                    break;
                case 6: // The cart, upright and still, beside the operator on their floor.
                    var p = player.Position;
                    float side = player.Facing, floor = Coal.SurfaceAt(p.x, p.y).Y;
                    cart.Body.position = new Vector2(Mathf.Clamp(p.x + side * 1.6f, 1.45f, 30.55f), floor + .7f);
                    cart.Body.rotation = 0;
                    cart.Body.linearVelocity = Vector2.zero;
                    cart.Body.angularVelocity = 0;
                    cart.transform.SetPositionAndRotation(cart.Body.position, Quaternion.identity);
                    Say("Debug: cart brought over.");
                    break;
            }
        }
    }
}
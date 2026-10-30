using System;
using System.Collections;
using System.Collections.Generic;
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
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoalRun.Core
{
    /**
     * This file is kinda the nightmare file (alongside PlayerController) because it manages so much of the game.
     * Maybe not the best split of responsibilities here but it gets the job done.
     */

    public enum Phase
    {
        Tutorial, // self explanatory
        Briefing, // notebook showing daily briefing
        Shift, // normal gameplay
        Leaving,  // period between shift end and elevator opens
        Replacement, // period between respawning
        Complete, // day finished screen
        Frozen // death cutscene
    }

    // the god class holding the shared state for every station (e.g. game's current phase, game clock,
    // tuning specific to that day e.g. cold). UI and audio are hooked into this with C# events.
    // That way we get less coupling. Woo-hoo! No couples!
    [DefaultExecutionOrder(-50)] // crank this back so it can initialize the state object before the rest of the components
    public class ShiftManager : MonoBehaviour
    {
        // both vars here are static to persist it between scenes
        private static bool _trained; // tracks tutorial completion so it can be skipped for re-attempts.
        private static int _dayIndex; // track which day we are currently on

        // set the point that the player should be standing when they get out of the elevator
        private static readonly Vector2 ArrivalSpot = new(30.5f, Room.Lower + .72f);

        [Header("Configuration")] [SerializeField]
        private Tuning tuning;

        [SerializeField] private DayConfig day;

        [Tooltip("Every line of text the player reads.")] [SerializeField]
        private Lines lines;

        [Tooltip("The shifts in order. A survived shift moves on to the next; empty: just the one above.")]
        [SerializeField]
        private DayConfig[] days;

        [Header("Player and stations")] [SerializeField]
        private InputReader input;

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

        [Header("Progress and presentation")] [SerializeField]
        private Tutorial tutorial;

        [SerializeField] private Notebook notebook;
        [SerializeField] private CameraRig cameraRig;

        [Header("Arrival")]
        [Tooltip("Seconds from Play until the first operator steps out of the staff lift.")]
        [SerializeField]
        private float firstArrival = 1.2f;

        // Audio cooldowns.
        private readonly Dictionary<string, float> _soundTimers = new();

        // Whether the arriving operator's lift door has opened.
        private bool _doorOpened;

        // Clocking out.
        private float _inLiftFor;

        // Phase to restore after an operator arrives.
        private Phase _resume;

        public static ShiftManager Instance { get; private set; }

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

        // Enables furnace consumption and scoring during the shift, leaving, and replacement phases.
        // Building temperature and the clock advance only while ShiftRunning is true.
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

        // Operators lost this shift.
        public int Deaths { get; private set; }

        // How the last one died, for the report.
        public string LastDeath { get; private set; } = "";

        // Seconds until the arriving operator steps out of the staff lift.
        public float ReplacementIn { get; private set; }

        // Tracks corpses for recovery if physics pushes them outside the room.
        public List<Rigidbody2D> Bodies { get; } = new();

        // Total physics time since scene startup, including training and briefing.
        public float Elapsed { get; private set; }

        // Seconds elapsed on the timed shift.
        public float Clock { get; private set; }

        // Outside temperature sampled from the day's forecast.
        public float Outside { get; private set; }

        // Current temperature inside the building.
        public float Temperature { get; private set; }

        // Freeze-grace progress, from 0 to 1. Reaching 1 ends the shift; warming up reduces it.
        public float Chill { get; private set; }
        public bool Freezing => Temperature <= tuning.freeze;

        public float InitialCoal { get; private set; }

        // Includes burned coal so consumption preserves the total used for the conservation check.
        public float TotalCoal =>
            chute.Bunker + cart.Coal + player.Hands + coal.Loose + furnace.Hopper + furnace.Burned;

        // Unity lifecycle

        private void Awake()
        {
            Instance = this;
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

        // Sound cue name and intensity.
        public event Action<string, float> OnSound;
        public event Action<Phase> OnPhase;

        // Reset static session progress when entering Play mode, even with domain reload disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetTraining()
        {
            _trained = false;
            _dayIndex = 0;
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

        // Notify listeners of a phase change and play its sound cue.
        // Clock, heat, fuel consumption, and player actions use their own phase guards.
        private void SetPhase(Phase phase)
        {
            Phase = phase;

            // for the leaving phase specifically, need the elevator to open for the player to enter
            if (phase == Phase.Leaving && staffLift) staffLift.Open();

            OnPhase?.Invoke(phase);
            PlayCue(phase.ToString().ToLowerInvariant());
        }

        // thanks to this, we don't need to manually reset every station
        private void Restart()
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

        // building warms by furnace burn rate and cools (due to outside temp) in proportion to difference in
        // temperature. latter means that running under demanded temp will cool room gradually.
        // burning a notch's rate holds it at `comfortable` against that notch's held temperature.
        // tehnically the furnace runs after this script, so this is the previous step's burn.
        private void StepHeat(float dt)
        {
            Clock += dt;
            Outside = Day.OutsideAt(Clock);
            Temperature += (furnace.BurnRate - tuning.heatLoss * (Temperature - Outside)) / tuning.heatCapacity * dt;
            
            // trigger red phone dialogue when freezing has started
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

        /**
         * DEATH AND REPLACEMENT HANDLING
         */

        // Replace the operator without resetting the room, equipment, or fuel.
        // During a timed shift, the clock and building heat continue while the replacement arrives.
        public void Die(string reason)
        {
            if (!CanAct) return;

            Deaths++;
            LastDeath = reason;
            Bodies.Add(player.Kill());
            SendOperator(tuning.replacementDelay);

            // red phone complains to you about you constantly dying
            // number of deaths change which line you get. very fun!
            var deaths = lines.phone.deaths;
            var deathLineIdx = Mathf.Min(Deaths, deaths.Length) - 1;
            phone.Announce(deaths[deathLineIdx], 2);
            
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
        
        /**
         * LEAVING PHASE - for clocking out when the day's over!
         */
        
        // The shift is over and the staff lift's door stands open. Standing still inside it for a moment ends the shift.
        private void StepLeaving(float dt)
        {
            var p = player.Position;

            // check that the player is actually (1) in the elevator and (2) ain't running around
            // like they gotta be IN the elevator, stopped, waiting for the door to close
            var onWorkFloor = player.Lane == 0 && p.y < Room.Lower + 2.3f;
            var inLift = p.x > Room.StaffLiftInside;
            var nearlyStopped = Mathf.Abs(player.Body.linearVelocity.x) < .5f;

            var inside = onWorkFloor && inLift && nearlyStopped;
            
            _inLiftFor = inside ? _inLiftFor + dt : 0;
            if (_inLiftFor >= tuning.liftStandTime) StartCoroutine(Depart());
        }

        // takes away the player's control so we can play the depart cutscene without them just walking out
        // as soon as the elevator closes
        private IEnumerator Depart()
        {
            Departing = true;
            player.Stand(Room.StaffLiftCenter);
            if (staffLift) yield return staffLift.CloseDoor();

            for (var k = 0f; k < 1; k += Time.deltaTime / tuning.fadeTime)
            {
                Fade = k;
                yield return null;
            }

            Fade = 1;
            SetPhase(Phase.Complete);
            ReportReady = true;

            // notebook doesn't show if departing is true so we need to mark this as false
            Departing = false;
        }

        // cutscnee that plays when the player dies from the cold:
        // zoom in on player, kill em and knock em over, pull back, fade to black and report thrown up
        // very cool!
        private IEnumerator FreezeEnd()
        {
            var at = player.Position + Vector2.up * .5f;
            if (cameraRig) yield return cameraRig.FocusOn(at, tuning.freezeZoom, tuning.freezeZoomIn);
            yield return new WaitForSeconds(tuning.freezeHold);

            // kill the player if they are still in the map, alive
            // because you can't really kill someone whose dead
            // unless you are a zombie maybe?
            if (player.Visible)
            {
                var body = player.Kill();
                Bodies.Add(body);
            }
            
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

        /**
         * DIALOGUE, AUDIO, SHARED PARTICLES
         */

        // messenger for making speakers say dialogue out loud (the little sppech bubble things)
        public void Say(string message, Speaker speaker = null)
        {
            // sometimes a replacement will come down and say stuff like "the wheel came off" line
            // even though they never saw it so if that happens, just drop the line to fix that bug
            if (!speaker && (!player.Visible || Phase == Phase.Replacement)) return;

            // assume the speaker is the player operator if speaker is not specified
            speaker ??= player.Voice;
            
            if (speaker) speaker.Speak(message);
        }

        public void PlayCue(string sound, float cooldown = .12f, float amount = 1)
        {
            // drop repeated calls of the same sound so station callers can be dumb and call it on every step
            if (_soundTimers.TryGetValue(sound, out var until) && until > Elapsed) return;

            _soundTimers[sound] = Elapsed + cooldown;
            
            // it's kinda annoying that Invoke is expensive because PlayCue is used in a lot of places
            // and the expensive marking bubles up :(
            OnSound?.Invoke(sound, amount);
        }

        // A speck of the lift shaft's coal dust, for coal being trampled or heaved about elsewhere.
        public void CoalDust(Vector2 at, Vector2 velocity, float size, float life)
        {
            if (liftShaft) liftShaft.Dust(at, velocity, size, life);
        }

        /**
         * DEBUG & RECOVERY STUFF FOR CLAUDE TO STOP DOING THINGS THE HARD WAY
         */

        // teleport things that somehow fall out of the map's boundaries back into the map
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

            static bool Lost(Vector2 p)
            {
                return p is not { x: > 0 and < Room.Width, y: > Room.Lower - 1.5f and < Room.Height + 4 };
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

        // debug commands controlled by function keys. claude should be able to use these through the unity CLI
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
                    
                    // keep the coal bunker capacity intact, unlike what the treasury does with the USD.
                    // idk why i don't make bunker capacity infinite. original design was finite,
                    // but it's not really relevant anymore. maybe i should fix that later.
                    chute.Bunker += cart.Coal - load;
                    
                    cart.Coal = load;
                    Say($"Debug: cart holds {load:0}.");
                    break;

                case 5: // clean up all the loose coal currently present on the map and put it back into the bunker.
                    Coal.ReturnAll();
                    Say("Debug: loose coal cleaned up.");
                    break;
                
                case 6: // teleport the cart to the player.
                    var p = player.Position;

                    float side = player.Facing, floor = Coal.SurfaceAt(p.x, p.y).Y;
                    cart.Body.position = new Vector2(Mathf.Clamp(p.x + side * 1.6f, 1.45f, 30.55f), floor + .7f);
                    cart.Body.rotation = 0;
                    cart.Body.linearVelocity = Vector2.zero;
                    cart.Body.angularVelocity = 0;
                    cart.transform.SetPositionAndRotation(cart.Body.position, Quaternion.identity);
                    Say("Debug: cart brought over.");
                    break;
                
                case 7: // restart training from the top.
                    _trained = false;
                    Restart();
                    break;

                case 8: // skip training and briefly directly into that day's shift.
                    tutorial.Skip();
                    BeginShift();
                    break;

                case 10: // jump the clock to last 30 seconds of the shift.
                    if (ShiftRunning) Clock = Mathf.Max(Clock, tuning.shiftLength - 30);
                    break;

                case 9: // toggles the Shift Bot on and off so i can watch the game instead of playing it :sunglasses:
                    ShiftBot.Run(this);
                    break;
            }
        }
    }
}

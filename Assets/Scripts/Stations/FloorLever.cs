using CoalRun.Core;
using UnityEngine;

namespace CoalRun.Stations
{
    // A lever standing up out of the floor, pivoting at its base. Drag the knob from left to right:
    // Value goes 0 (tilted left) to 1 (tilted right). A spring-return lever rolls back to its rest
    // value (the left end, or upright) on its own once let go; one without a spring stays where it was left,
    // or with notches, rolls into the nearest one.
    public class FloorLever : Machine
    {
        [Tooltip("Rotated about its own position, the base of the lever.")]
        [SerializeField]
        private Transform stick;
        [SerializeField] private float length = 1.3f;
        [Tooltip("Held, the operator can walk the lever to either end but no further than this beyond the knob, so they stay standing beside it.")]
        [SerializeField]
        private float standOff = .3f;
        [SerializeField] private int lane = 1;
        [SerializeField] private bool springReturn = true;
        [Tooltip("More than one: let go, it rolls into the nearest of this many evenly spaced notches, ends included, and clunks at each one it passes. Without a spring only.")]
        [SerializeField]
        private int notches;
        [Tooltip("Where it starts and a spring-return lever rolls back to: 0 left, .5 upright, 1 right.")]
        [Range(0, 1)]
        [SerializeField]
        private float restValue;
        [Tooltip("What the key prompt calls it.")]
        [SerializeField]
        private string prompt = "Lever";
        [SerializeField] private string farMessage = "Walk up to the lever.";
        [SerializeField] private string wrongLaneMessage = "The lever is up in the control room.";

        [Header("Audio (all optional)")]
        [SerializeField]
        private AudioSource grab;
        [Tooltip("Looped while it's pushed by hand; louder the faster it moves.")]
        [SerializeField]
        private AudioSource drag;
        [Tooltip("Looped while it rolls back on its own.")]
        [SerializeField]
        private AudioSource rollBack;
        [Tooltip("Played when it hits either end.")]
        [SerializeField]
        private AudioSource stop;
        [Tooltip("Played as it passes a notch.")]
        [SerializeField]
        private AudioSource clunk;

        public float Value { get; private set; }
        public float RestValue => restValue;
        // How far over it goes by hand: less than 1 while it's stuck (see Tutorial).
        public float Limit { get; set; } = 1;
        public string Prompt => prompt;
        // The nearest notch, 0 at the left end; 0 on a lever without notches.
        public int Notch => notches > 1 ? Mathf.RoundToInt(Value * (notches - 1)) : 0;
        // Set by the station the lever works: while it returns false, the lever can't be taken hold of.
        public System.Func<bool> Interlock { get; set; }
        public string InterlockMessage { get; set; } = "";
        public override string OutOfReach => ShiftManager.Instance.Player.Lane != lane ? wrongLaneMessage : InReach(ShiftManager.Instance.Player) ? InterlockMessage : farMessage;
        public override Vector2 HandlePosition => _game ? Knob : base.HandlePosition;
        // Held, the operator can walk as far as the knob goes at either end, staying on the side of it
        // they took hold from.
        public override Vector2 WalkRange(float grabbedAt) => new Vector2(XAt(0), XAt(1)) + Vector2.one * _side * standOff;

        private ShiftManager _game;
        // Where the cursor's value sat relative to the lever when it was grabbed, so taking hold of a
        // lever partway through its roll-back doesn't make it jump.
        private float _grabOffset;
        private float _lastValue;
        private int _lastNotch;

        private Vector2 _lastMouse;
        // Which side of the knob the operator took hold from: 1 right, -1 left.
        private float _side = 1;

        private void Start()
        {
            _game = ShiftManager.Instance;
            if (!handle) handle = stick;
            Value = _lastValue = restValue;
            _lastNotch = Notch;
        }

        private Vector2 Pivot => stick ? stick.position : transform.position;
        private float MaxAngle => _game.Tune.leverAngle;
        private Vector2 Knob => Pivot + (Vector2)(Quaternion.Euler(0, 0, Angle(Value)) * Vector2.up) * length;

        private float XAt(float value) => Pivot.x - Mathf.Sin(Angle(value) * Mathf.Deg2Rad) * length;
        // The value that puts the knob at world x.
        private float AtX(float x) => Mathf.InverseLerp(MaxAngle, -MaxAngle, -Mathf.Asin(Mathf.Clamp((x - Pivot.x) / length, -1, 1)) * Mathf.Rad2Deg);
        // Positive tilts left, so 0 leans left and 1 leans right.
        private float Angle(float value) => Mathf.Lerp(MaxAngle, -MaxAngle, value);

        // Anywhere along the stick, with a bit extra around the knob.
        public override bool Hit(Vector2 mouse)
        {
            Vector2 a = Pivot, b = Knob;
            var t = Mathf.Clamp01(Vector2.Dot(mouse - a, b - a) / (b - a).sqrMagnitude);
            return Vector2.Distance(mouse, a + (b - a) * t) < .25f + .2f * t;
        }

        public override bool CanOperate(PlayerController player) =>
            _game.CanAct && player.Lane == lane && InReach(player) && (InUse || Interlock == null || Interlock());

        private bool InReach(PlayerController player)
        {
            Vector2 p = player.Position, reach = _game.Tune.machineReach;
            // The operator's middle is about .7 above the floor the lever stands on. Once held, WalkRange
            // keeps them beside it, so only the floor is checked.
            return (InUse || Mathf.Abs(p.x - Pivot.x) < reach.x) && Mathf.Abs(p.y - Pivot.y - .7f) < reach.y;
        }

        private float FromMouse(Vector2 mouse)
        {
            var d = mouse - Pivot;
            var tilt = Mathf.Atan2(-d.x, Mathf.Max(.2f, d.y)) * Mathf.Rad2Deg;
            return Mathf.InverseLerp(MaxAngle, -MaxAngle, tilt);
        }

        protected override void OnBegin(Vector2 mouse)
        {
            _side = _game.Player.Position.x >= Knob.x ? 1 : -1;
            _grabOffset = Value - FromMouse(mouse);
            _lastMouse = mouse;
            if (grab) grab.Play();
        }

        // Only a moving cursor works the lever, so a still mouse doesn't fight the operator walking it.
        public override void Drag(Vector2 mouse, float dt)
        {
            if ((mouse - _lastMouse).sqrMagnitude < 1e-6f) return;
            _lastMouse = mouse;
            Value = Mathf.Clamp(FromMouse(mouse) + _grabOffset, 0, Limit);
        }

        // Walking while holding it pulls the knob along by as much as the operator moved.
        public override void Walk(float dx)
        {
            Value = Mathf.Min(Limit, AtX(Knob.x + dx));
            _grabOffset = Value - FromMouse(_lastMouse);
        }

        private void FixedUpdate()
        {
            var dt = Time.fixedDeltaTime;
            if (!InUse && springReturn) Value = Mathf.MoveTowards(Value, restValue, _game.Tune.leverReturn * dt);
            else if (!InUse && notches > 1) Value = Mathf.MoveTowards(Value, (float)Notch / (notches - 1), _game.Tune.leverReturn * dt);
            if (Notch != _lastNotch)
            {
                _lastNotch = Notch;
                if (clunk) clunk.Play();
                _game.Sound("notch");
            }

            var speed = Mathf.Abs(Value - _lastValue) / dt;
            var hitEnd = Value is <= 0 or >= 1 && _lastValue is > 0 and < 1;
            _lastValue = Value;

            if (stick) stick.localRotation = Quaternion.Euler(0, 0, Angle(Value));
            Loop(drag, InUse ? speed : 0);
            Loop(rollBack, InUse ? 0 : speed);
            if (hitEnd && stop) stop.Play();
        }

        private static void Loop(AudioSource source, float speed)
        {
            if (!source) return;
            source.volume = Mathf.Clamp01(speed * .8f);
            switch (speed)
            {
                case > .02f when !source.isPlaying: source.Play(); break;
                case <= .02f when source.isPlaying: source.Stop(); break;
            }
        }
    }
}

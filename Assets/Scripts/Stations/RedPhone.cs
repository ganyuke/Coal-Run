using CoalRun.Core;
using UnityEngine;

namespace CoalRun.Stations
{
    // The red phone by the staff lift: brass upstairs ringing down for heat. It starts once the building
    // drops below `ringBelow` and rings more often the colder it gets, until answered with E. Answering
    // only puts brass on the line; ignoring it is allowed. It's how the player hears that the building is
    // cooling without a thermometer on screen. Clicking it answers too, like E.
    public class RedPhone : Machine
    {
        [SerializeField] private float ringBelow = 13;
        [Tooltip("Quiet this long after it's answered.")]
        [SerializeField]
        private float cooldown = 10;
        [Tooltip("How far it rocks on its hook while ringing, in degrees.")]
        [SerializeField]
        private float shake = 9;

        public bool Ringing { get; private set; }

        private ShiftManager _game;
        // Seconds to the next ring and to the end of the quiet after an answer.
        private float _timer, _quiet;
        private Quaternion _rest;

        private void Start()
        {
            _game = ShiftManager.Instance;
            _rest = transform.localRotation;
        }

        private void FixedUpdate()
        {
            if (!_game.ShiftRunning) { Ringing = false; return; }
            var dt = Time.fixedDeltaTime;
            _timer -= dt;
            _quiet -= dt;
            var t = _game.Temperature;
            // Warm again: whatever it was ringing about is fixed.
            if (t >= ringBelow) { Ringing = false; return; }
            if (_timer > 0 || _quiet > 0) return;
            Ringing = true;
            _timer = t < 6 ? 4 : t < 9 ? 8 : 15;
            _game.Sound("ring", 1);
        }

        // Rings in bursts: a quick rattle, then a pause.
        private void Update()
        {
            var burst = Ringing && Time.time % .9f < .45f;
            transform.localRotation = _rest * Quaternion.Euler(0, 0, burst ? Mathf.Sin(Time.time * 60) * shake : 0);
        }

        public override string OutOfReach => Ringing ? "Walk up to the phone." : "It isn't ringing.";
        public override bool CanOperate(PlayerController player) => CanAnswer(player);
        protected override void OnBegin(Vector2 mouse) => Answer();

        public bool CanAnswer(PlayerController player) =>
            Ringing && player.Lane == 0 && Mathf.Abs(player.Position.x - transform.position.x) < 1 && player.Position.y < 4.3f;

        public void Answer()
        {
            Ringing = false;
            _quiet = cooldown;
            _game.Say(_game.Temperature < 7 ? "“We can’t feel our hands. Forget the target. Just keep us alive.”" : "“It’s getting cold upstairs. Could you turn the furnace up?”", 6);
            _game.Sound("phone");
        }
    }
}

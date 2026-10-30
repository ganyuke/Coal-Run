using CoalRun.Core;
using CoalRun.Gameplay.Machinery;
using UnityEngine;

namespace CoalRun.Gameplay.Coal
{
    // The coal chute, worked by a floor lever in the control room. Push the lever right with the
    // mouse: the further over, the faster coal pours. Let go and it rolls back shut, still pouring
    // on the way. The bunker above holds the coal that hasn't been dispensed yet.
    [UnityEngine.Scripting.APIUpdating.MovedFrom("CoalRun.Stations")]
    public class Chute : MonoBehaviour
    {
        [SerializeField] private FloorLever lever;
        [SerializeField] private float bunker = 1000;
        private float _accumulator;

        private ShiftManager _game;

        public float Bunker
        {
            get => bunker;
            set => bunker = value;
        }

        // 0 shut, 1 fully open.
        public float Pull => lever ? lever.Value : 0;
        public FloorLever Lever => lever;

        private void Start()
        {
            _game = ShiftManager.Instance;
            lever.Text = _game.Lines.levers.chute;
        }

        private void FixedUpdate()
        {
            if (Pull <= 0 || _game.Tutorial.HoldsChute) return;
            _accumulator += Pull * _game.Tune.chuteRate * Time.fixedDeltaTime;
            while (_accumulator >= .5f)
            {
                Dispense(.5f);
                _accumulator -= .5f;
            }

            _game.PlayCue("coal", .13f);
        }

        // Drops up to `amount` from the bunker out of the chute mouth, thrown sideways at `vx`, scattered
        // by `spread` (see CoalField.Emit).
        public void Dispense(float amount, float vx = 0, float spread = 1)
        {
            var q = Mathf.Min(bunker, amount);
            bunker -= q;
            _game.Coal.Emit(Room.Chute, 6.7f, q, vx, 0, spread);
        }
    }
}
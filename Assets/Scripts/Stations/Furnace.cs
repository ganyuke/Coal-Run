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

        public float Hopper { get; private set; }
        // Everything poured into the hopper this shift.
        public float Delivered { get; private set; }
        // Everything burned, which leaves the coal total; and the electricity it made, in kWh.
        public float Burned { get; private set; }
        public float Output { get; private set; }
        // Coal actually burning per second: the notch's rate, or less once the hopper runs dry.
        public float BurnRate { get; private set; }
        public int Notch => burnLever ? burnLever.Notch : 0;

        private ShiftManager _game;

        // In Awake, so ShiftManager counts the starting hopper in its Start.
        private void Awake()
        {
            _game = ShiftManager.Instance;
            Hopper = _game.Day.startingHopper;
        }

        private void FixedUpdate()
        {
            if (_game.Phase != Phase.Shift) { BurnRate = 0; return; }
            var dt = Time.fixedDeltaTime;
            var used = Mathf.Min(Hopper, _game.Tune.burnRates[Notch] * dt);
            Hopper -= used;
            Burned += used;
            Output += used * 1.5f;
            BurnRate = used / dt;
        }

        private void Update()
        {
            if (!fill) return;
            var f = Mathf.Clamp01(Hopper / _game.Tune.hopperCapacity);
            fill.localScale = new Vector3(fill.localScale.x, f * fillHeight, 1);
            fill.gameObject.SetActive(f > .01f);
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

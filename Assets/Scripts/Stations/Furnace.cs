using CoalRun.Core;
using UnityEngine;

namespace CoalRun.Stations
{
    // The furnace, fed from the hopper under the tip cradle. So far it only holds the hopper; the burn
    // lever, heat and output come with the rest of M3.
    public class Furnace : MonoBehaviour
    {
        [Tooltip("Scaled vertically with the hopper's coal, from its bottom edge.")]
        [SerializeField]
        private Transform fill;
        [SerializeField] private float fillHeight = 2.6f;

        public float Hopper { get; private set; }
        // Everything poured into the hopper this shift.
        public float Delivered { get; private set; }

        private ShiftManager _game;

        // In Awake, so ShiftManager counts the starting hopper in its Start.
        private void Awake()
        {
            _game = ShiftManager.Instance;
            Hopper = _game.Day.startingHopper;
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

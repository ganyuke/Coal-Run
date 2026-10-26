using UnityEngine;

namespace CoalRun.Stations
{
    // Coal falling from the hopper down its chute and into the back of the firebox, for as long as there is coal in the
    // hopper: a trickle while the fire is out or only just fed, a steady stream at a hard burn. Only a picture (see Furnace).
    public class HopperFeed : MonoBehaviour
    {
        [SerializeField] private Furnace furnace;
        [Tooltip("Lumps dropping through the funnel and pipe.")]
        [SerializeField]
        private ParticleSystem chute;
        [Tooltip("Lumps dropping off the back wall of the firebox onto the bed. Clipped to the mouth by a mask.")]
        [SerializeField]
        private ParticleSystem pour;
        [Tooltip("Lumps a second at the lowest flow and at the hardest burn.")]
        [SerializeField]
        private Vector2 rate = new(3, 22);

        private void Update()
        {
            var has = furnace.Hopper > .05f;
            var burn = Mathf.Clamp01(furnace.BurnRate / Mathf.Max(.01f, ShiftManager().Tune.burnRates[^1]));
            var perSecond = has ? Mathf.Lerp(rate.x, rate.y, burn) : 0;
            Set(chute, perSecond);
            Set(pour, furnace.Lit ? perSecond : 0);
        }

        private static Core.ShiftManager ShiftManager() => Core.ShiftManager.Instance;

        private static void Set(ParticleSystem system, float perSecond)
        {
            if (!system) return;
            var emission = system.emission;
            emission.rateOverTime = perSecond;
        }
    }
}

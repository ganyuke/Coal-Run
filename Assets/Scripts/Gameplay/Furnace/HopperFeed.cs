using CoalRun.Core;
using UnityEngine;

namespace CoalRun.Gameplay.Furnace
{
    // Coal falling from the hopper down its chute and into the back of the firebox while the fire is burning it: a
    // trickle at the lowest notch, a steady stream at a hard burn, nothing while the fire is out. Only a picture (see
    // Furnace).
    [UnityEngine.Scripting.APIUpdating.MovedFrom("CoalRun.Stations")]
    public class HopperFeed : MonoBehaviour
    {
        [SerializeField] private Furnace furnace;

        [Tooltip("Lumps dropping through the funnel and pipe. Clipped by a mask to the dark opening under the funnel.")]
        [SerializeField]
        private ParticleSystem chute;

        [Tooltip("Lumps dropping off the back wall of the firebox onto the bed. Clipped to the mouth by a mask.")]
        [SerializeField]
        private ParticleSystem pour;

        [Tooltip("Lumps a second at the lowest flow and at the hardest burn.")] [SerializeField]
        private Vector2 rate = new(3, 22);

        private void Update()
        {
            var has = furnace.Lit && furnace.Hopper > .05f;
            var burn = Mathf.Clamp01(furnace.BurnRate / Mathf.Max(.01f, ShiftManager().Tune.burnRates[^1]));
            var perSecond = has ? Mathf.Lerp(rate.x, rate.y, burn) : 0;
            Set(chute, perSecond);
            Set(pour, perSecond);
        }

        private static ShiftManager ShiftManager()
        {
            return Core.ShiftManager.Instance;
        }

        private static void Set(ParticleSystem system, float perSecond)
        {
            if (!system) return;
            var emission = system.emission;
            emission.rateOverTime = perSecond;
        }
    }
}
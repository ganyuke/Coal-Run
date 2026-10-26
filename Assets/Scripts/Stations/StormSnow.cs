using CoalRun.Core;
using UnityEngine;

namespace CoalRun.Stations
{
    // Snow blowing past the window, thicker and faster the colder it is outside.
    public class StormSnow : MonoBehaviour
    {
        [SerializeField] private ParticleSystem snow;
        [Tooltip("Emission per second at the shift's mildest outside temperature and at its worst.")]
        [SerializeField]
        private Vector2 rate = new(25, 140);
        [SerializeField] private Vector2 temperatureRange = new(-4, -38);

        private void Update()
        {
            var g = ShiftManager.Instance;
            if (!g) return;
            var k = Mathf.InverseLerp(temperatureRange.x, temperatureRange.y, g.Outside);
            var emission = snow.emission;
            emission.rateOverTime = Mathf.Lerp(rate.x, rate.y, k);
            var wind = snow.velocityOverLifetime;
            var speed = Mathf.Lerp(4, 9, k);
            wind.x = -speed;
            wind.y = -speed * .25f;
        }
    }
}

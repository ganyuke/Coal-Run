using System;
using UnityEngine;

namespace CoalRun.Core
{
    [Serializable]
    public struct ForecastPoint
    {
        public float time, temperature;
        public ForecastPoint(float time, float temperature) { this.time = time; this.temperature = temperature; }
    }

    // What changes from one shift to the next. One asset per day in Assets/Data.
    [CreateAssetMenu(menuName = "Coal Run/Day")]
    public class DayConfig : ScriptableObject
    {
        public int day = 1;
        [Tooltip("Run the helper's demonstration before the shift.")]
        public bool training = true;
        [Tooltip("The morning memo on the briefing page, above the target. Empty on a day with nothing to say.")]
        [TextArea]
        public string memo;
        public int startingWheels = 1;
        public float startingHopper = 10;
        [Tooltip("Brass's electrical output target, kWh.")]
        public float target = 210;
        [Tooltip("Outside temperature over the shift. Linear between points.")]
        public ForecastPoint[] forecast =
        {
            new(0, -4), new(140, -4), new(220, -14), new(290, -26), new(335, -38),
            new(400, -38), new(455, -14), new(520, -4), new(600, -4)
        };

        public float OutsideAt(float time)
        {
            for (var i = 1; i < forecast.Length; i++)
            {
                var a = forecast[i - 1];
                var b = forecast[i];
                if (time <= b.time) return Mathf.Lerp(a.temperature, b.temperature, (time - a.time) / (b.time - a.time));
            }
            return forecast[^1].temperature;
        }
    }
}

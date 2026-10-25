using CoalRun.Core;
using TMPro;
using UnityEngine;

namespace CoalRun.UI
{
    // The television on the wall between the freight lift and the furnace: the shift clock, and the day's
    // forecast as a graph of the outside temperature over the whole shift, with where the shift is now and
    // the temperature the fire's current notch holds against. Read the graph against the holds line to see
    // when the burn lever has to go up (the design doc's forecast graph, "nothing about the weather is a
    // surprise").
    public class ForecastTv : MonoBehaviour
    {
        [SerializeField] private TMP_Text clock, outside, inside, holds;
        [Tooltip("The whole forecast, dim, drawn once. Positions are local to the graph area.")]
        [SerializeField]
        private LineRenderer forecast;
        [Tooltip("The forecast up to now, bright.")]
        [SerializeField]
        private LineRenderer past;
        [SerializeField] private LineRenderer holdsLine, nowLine;
        [Tooltip("The graph area in local units, the time axis across and temperature up.")]
        [SerializeField]
        private Rect graph = new(-1.65f, -.85f, 3.3f, 1.4f);
        [SerializeField] private int samples = 60;
        [SerializeField] private Color normal = new(.55f, 1, .6f), warning = new(1, .35f, .3f);

        private ShiftManager _game;
        private float _low, _high;
        private Vector3[] _points;

        private void Start()
        {
            _game = ShiftManager.Instance;
            var day = _game.Day;
            // Room for the coldest forecast point and every notch's holds line.
            _low = _high = day.forecast[0].temperature;
            foreach (var p in day.forecast) { _low = Mathf.Min(_low, p.temperature); _high = Mathf.Max(_high, p.temperature); }
            foreach (var t in _game.Tune.heldTemperatures) { _low = Mathf.Min(_low, t); _high = Mathf.Max(_high, t); }
            _low -= 2;
            _high += 2;

            _points = new Vector3[samples + 1];
            for (var i = 0; i <= samples; i++)
            {
                var time = _game.Tune.shiftLength * i / samples;
                _points[i] = Point(time, day.OutsideAt(time));
            }
            forecast.positionCount = _points.Length;
            forecast.SetPositions(_points);
        }

        private void Update()
        {
            var length = _game.Tune.shiftLength;
            var time = Mathf.Min(_game.Clock, length);
            clock.text = _game.Ended ? "SHIFT OVER" : $"{(int)time / 60}:{(int)time % 60:00}";
            outside.text = $"OUTSIDE {_game.Outside:0}°";
            inside.text = $"INSIDE {_game.Temperature:0}°";
            inside.color = _game.Freezing ? warning : normal;
            var held = _game.Tune.heldTemperatures[_game.Furnace.Notch];
            holds.text = _game.Furnace.Lit ? $"FIRE HOLDS {held:0}°" : "FIRE OUT";
            holds.color = !_game.Furnace.Lit || held > _game.Outside + .5f ? warning : normal;

            // The bright part runs up to now, ending exactly on the current temperature.
            var done = Mathf.Clamp(Mathf.FloorToInt(time / length * samples), 0, samples);
            past.positionCount = done + 2;
            for (var i = 0; i <= done; i++) past.SetPosition(i, _points[i]);
            past.SetPosition(done + 1, Point(time, _game.Day.OutsideAt(time)));

            var x = graph.xMin + time / length * graph.width;
            nowLine.SetPosition(0, new Vector3(x, graph.yMin));
            nowLine.SetPosition(1, new Vector3(x, graph.yMax));
            holdsLine.enabled = _game.Furnace.Lit;
            var y = Point(0, held).y;
            holdsLine.SetPosition(0, new Vector3(graph.xMin, y));
            holdsLine.SetPosition(1, new Vector3(graph.xMax, y));
        }

        private Vector3 Point(float time, float temperature) =>
            new(graph.xMin + time / _game.Tune.shiftLength * graph.width, graph.yMin + Mathf.InverseLerp(_low, _high, temperature) * graph.height);
    }
}

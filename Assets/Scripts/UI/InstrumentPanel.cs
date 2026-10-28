using System.Collections.Generic;
using CoalRun.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CoalRun.UI
{
    // The instrument board on the wall between the freight lift and the furnace, where the television used to be. One
    // temperature scale runs through all of it: a glass thermometer on the left whose mercury is the temperature inside,
    // and, on the same gridlines, a chart of the whole shift. The chart carries the outside temperature as the day's
    // forecast (bright up to now), what the building has actually done so far, and the temperature the burn lever's notch
    // holds against (dotted while the fire is out). Read the forecast against the orange line to see when the lever has to
    // go up. A red cage light on top of the thermometer comes on while it's colder outside than the fire holds against,
    // so nobody has to read the lines to know. "Nothing about the weather is a surprise."
    public class InstrumentPanel : MonoBehaviour
    {
        [Header("Scale")]
        [Tooltip("The temperatures at the bottom and top of the shared axis, and the local heights they sit at.")]
        [SerializeField]
        private Vector2 temperatureRange = new(-40, 25);
        [SerializeField] private Vector2 heightRange = new(-1.05f, 1.05f);
        [Tooltip("The chart's area, local: x, y of its lower left, then width and height. Time runs across, the whole shift.")]
        [SerializeField]
        private Rect chart = new(-1.55f, -1.1f, 3.8f, 2.2f);
        [Header("Thermometer")]
        [SerializeField] private float tubeX = -2.28f;
        [SerializeField] private float bulbY = -1.18f;
        [SerializeField] private float mercuryWidth = .12f;
        [SerializeField] private SpriteRenderer mercury, bulb;
        [Tooltip("Little triangles at the tube: the outside temperature (right) and what the fire holds (left).")]
        [SerializeField]
        private SpriteRenderer outsideMarker, holdMarker;
        [Header("Chart")]
        [SerializeField] private LineRenderer forecast, past, nowLine, holdsLine, insideLine;
        [SerializeField] private int samples = 60;
        [Tooltip("Seconds between the points of the line that records the building's own temperature.")]
        [SerializeField]
        private float recordEvery = 3;
        [Tooltip("Dots across the whole holds line while the fire is out.")]
        [SerializeField]
        private int dots = 26;
        [Header("Readout")]
        [SerializeField] private TMP_Text clockText;
        [Header("Cold lamp")]
        [Tooltip("The red cage light's bulb, white in the art and tinted here, and the light it throws.")]
        [SerializeField]
        private SpriteRenderer coldBulb;
        [SerializeField] private Light2D coldGlow;
        [SerializeField] private Color bulbOff = new(.3f, .1f, .1f), bulbOn = new(1, .15f, .1f);
        [SerializeField] private float glowIntensity = 1.2f;
        [Tooltip("Seconds for the bulb to come up or die down.")]
        [SerializeField]
        private float warmUp = .15f;
        [Header("Colours")]
        [SerializeField] private Color icy = new(.55f, .82f, 1), cool = new(.82f, .96f, 1), mild = new(1, .9f, .45f), warm = new(1, .55f, .25f);
        [SerializeField] private Color outsideColor = new(.62f, .83f, 1), holdColor = new(1, .54f, .22f), dimOutside = new(.3f, .42f, .52f);

        private ShiftManager _game;
        private Vector3[] _points;
        private readonly List<Vector3> _record = new();
        private float _nextRecord;
        private Material _holdsMaterial;
        private float _cold;

        private void Start()
        {
            _game = ShiftManager.Instance;
            var day = _game.Day;
            _points = new Vector3[samples + 1];
            for (var i = 0; i <= samples; i++)
            {
                var time = _game.Tune.shiftLength * i / samples;
                _points[i] = Point(time, day.OutsideAt(time));
            }
            forecast.positionCount = _points.Length;
            forecast.SetPositions(_points);
            // The holds line is solid while the fire's lit, dotted when it's out: one dot texture, tiled along the line.
            var dash = new Texture2D(2, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            dash.SetPixels(new[] { Color.white, Color.clear });
            dash.Apply();
            _holdsMaterial = holdsLine.material;
            _holdsMaterial.mainTexture = dash;
            holdsLine.textureMode = LineTextureMode.Tile;
        }

        private float HeightOf(float temperature) =>
            Mathf.Lerp(heightRange.x, heightRange.y, Mathf.InverseLerp(temperatureRange.x, temperatureRange.y, temperature));

        private Vector3 Point(float time, float temperature) =>
            new(chart.xMin + Mathf.Clamp01(time / _game.Tune.shiftLength) * chart.width, Mathf.Clamp(HeightOf(temperature), chart.yMin + .02f, chart.yMax - .02f), 0);

        // Blue and icy at and below freezing, pale up to comfortable, then yellow and orange: how warm it feels.
        private Color Heat(float temperature)
        {
            if (temperature <= 0) return icy;
            if (temperature < 8) return Color.Lerp(icy, cool, temperature / 8);
            if (temperature < 14) return Color.Lerp(cool, mild, (temperature - 8) / 6);
            return Color.Lerp(mild, warm, Mathf.Clamp01((temperature - 14) / 8));
        }

        private void Update()
        {
            var length = _game.Tune.shiftLength;
            var time = Mathf.Min(_game.Clock, length);
            var inside = _game.Temperature;
            var outside = _game.Outside;
            var held = _game.Tune.heldTemperatures[_game.Furnace.Notch];
            var lit = _game.Furnace.Lit;

            // The mercury, from the bulb up to the temperature inside.
            var top = Mathf.Max(bulbY, HeightOf(inside));
            var height = top - bulbY;
            mercury.transform.localScale = new Vector3(mercuryWidth, Mathf.Max(.01f, height), 1);
            mercury.transform.localPosition = new Vector3(tubeX, bulbY + height / 2, 0);
            var heat = Heat(inside);
            mercury.color = bulb.color = heat;
            clockText.text = _game.Ended || time >= length ? "OVER" : $"{(int)time / 60}:{(int)time % 60:00}";

            // Markers beside the tube: outside on the right, what the fire holds on the left (faint when it's out).
            outsideMarker.transform.localPosition = new Vector3(tubeX + .2f, HeightOf(outside), 0);
            holdMarker.transform.localPosition = new Vector3(tubeX - .2f, HeightOf(held), 0);
            var holdTint = lit ? holdColor : new Color(holdColor.r, holdColor.g, holdColor.b, .35f);
            holdMarker.color = holdTint;
            outsideMarker.color = outsideColor;

            // The chart: forecast bright up to now, the building's own record, the holds line, and where now is.
            var done = Mathf.Clamp(Mathf.FloorToInt(time / length * samples), 0, samples);
            past.positionCount = done + 2;
            for (var i = 0; i <= done; i++) past.SetPosition(i, _points[i]);
            past.SetPosition(done + 1, Point(time, _game.Day.OutsideAt(time)));
            var x = chart.xMin + time / length * chart.width;
            nowLine.SetPosition(0, new Vector3(x, chart.yMin));
            nowLine.SetPosition(1, new Vector3(x, chart.yMax));
            var y = Mathf.Clamp(HeightOf(held), chart.yMin + .02f, chart.yMax - .02f);
            holdsLine.SetPosition(0, new Vector3(chart.xMin, y));
            holdsLine.SetPosition(1, new Vector3(chart.xMax, y));
            // Scaled to a sliver the texture stays on its solid first pixel: solid when lit, dotted when out.
            holdsLine.textureScale = new Vector2(lit ? .01f : dots / chart.width, 1);
            holdsLine.startColor = holdsLine.endColor = holdColor;

            // The cold lamp: on while the outside is colder than the notch holds against, or there's no fire to hold it.
            var cold = !_game.Ended && (!lit || outside < held);
            _cold = Mathf.MoveTowards(_cold, cold ? 1 : 0, Time.deltaTime / warmUp);
            coldBulb.color = Color.Lerp(bulbOff, bulbOn, _cold);
            if (coldGlow) coldGlow.intensity = glowIntensity * _cold;

            if (_game.ShiftRunning && time >= _nextRecord)
            {
                _nextRecord = time + recordEvery;
                _record.Add(Point(time, inside));
            }
            insideLine.positionCount = _record.Count + 1;
            for (var i = 0; i < _record.Count; i++) insideLine.SetPosition(i, _record[i]);
            insideLine.SetPosition(_record.Count, Point(time, inside));
            insideLine.startColor = insideLine.endColor = heat;
        }
    }
}

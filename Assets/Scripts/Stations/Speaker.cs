using System.Collections.Generic;
using CoalRun.Core;
using TMPro;
using UnityEngine;

namespace CoalRun.Stations
{
    // Something that talks: what it says appears as little text above it, typed out at its own pace, its head
    // bobs while it's talking, and a few specks puff up around it. Lines wait their turn (a speaker says one at
    // a time, with a pause between), a line repeated straight away is dropped, and speakers standing near each
    // other stack their text instead of writing over each other. ShiftManager.Say picks who's talking.
    public class Speaker : MonoBehaviour
    {
        [Tooltip("Bobbed up and down while talking: a character's head, or the phone itself.")]
        [SerializeField]
        private Transform head;
        [SerializeField] private float bob = .07f, bobRate = 13;
        [Tooltip("Where the text stands, centred; it grows upward from here.")]
        [SerializeField]
        private Transform anchor;
        [Tooltip("Little specks puffed up while talking, in world space. Optional.")]
        [SerializeField]
        private ParticleSystem puffs;
        [SerializeField] private float puffRate = 10;
        [SerializeField] private float textSize = 3.4f, width = 6, outline = .3f;
        [SerializeField] private Color ink = new(1, .96f, .85f);
        [Tooltip("Characters a second it says a line at: how fast the text is typed out, and how long the bobbing and puffing go on. Lower is slower.")]
        [SerializeField]
        private float charsPerSecond = 16;
        [Tooltip("Seconds a line stays up after it's been said, at least (the line's own duration can be longer).")]
        [SerializeField]
        private float holdAfter = 1.8f;
        [Tooltip("Quiet between one line and the next from the same speaker.")]
        [SerializeField]
        private float gap = .6f;
        [Tooltip("Lines waiting their turn; past this the oldest is dropped.")]
        [SerializeField]
        private int maxQueued = 2;
        [Tooltip("The same line again within this many seconds is dropped, so a hint that fires every step is said once.")]
        [SerializeField]
        private float repeatAfter = 6;
        [Tooltip("Space between stacked texts, in world units.")]
        [SerializeField]
        private float stackGap = .12f;
        [SerializeField] private float fadeTime = .5f;
        [SerializeField] private int sortingOrder = 60;
        [Tooltip("The pitch of the little blip that goes with each couple of letters: higher for a lighter voice.")]
        [SerializeField]
        private float voicePitch = 1;
        [SerializeField] private TMP_FontAsset font;

        public bool Talking => Time.time < _talkUntil;
        // The line is up (being typed or held), not yet faded out.
        public bool Showing => Time.time < _lineEnd;

        private static readonly List<Speaker> All = new();
        private static int _laidOutAt = -1;

        private readonly Queue<(string text, float seconds)> _queue = new();
        private TextMeshPro _text;
        private Vector3 _headRest, _wanted;
        private string _lastText = "";
        private float _lineStart, _lineEnd, _talkUntil, _lastEnd, _bob, _puffDebt;
        private int _typed;

        private void Awake()
        {
            // On its own at the top of the scene, so the speaker's scale (the phone is small) doesn't shrink it;
            // it just follows the anchor.
            var go = new GameObject("Speech: " + name);
            _text = go.AddComponent<TextMeshPro>();
            _text.fontSize = textSize;
            if (font) _text.font = font;
            _text.alignment = TextAlignmentOptions.Bottom;
            _text.textWrappingMode = TextWrappingModes.Normal;
            _text.rectTransform.sizeDelta = new Vector2(width, 3);
            _text.rectTransform.pivot = new Vector2(.5f, 0);
            _text.outlineWidth = outline;
            _text.outlineColor = new Color32(20, 14, 10, 255);
            _text.sortingOrder = sortingOrder;
            _text.gameObject.SetActive(false);
            if (head) _headRest = head.localPosition;
        }

        private void OnEnable() => All.Add(this);

        private void OnDisable()
        {
            All.Remove(this);
            if (_text) _text.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_text) Destroy(_text.gameObject);
        }

        // Said when it's this speaker's turn, for at least `seconds`.
        public void Speak(string text, float seconds)
        {
            if (text == _lastText && Time.time < _lastEnd + repeatAfter) return;
            foreach (var queued in _queue) if (queued.text == text) return;
            _queue.Enqueue((text, seconds));
            while (_queue.Count > maxQueued) _queue.Dequeue();
        }

        private void Begin(string text, float seconds)
        {
            var say = text.Length / charsPerSecond;
            _text.text = text;
            _text.maxVisibleCharacters = 0;
            _text.gameObject.SetActive(true);
            _lineStart = Time.time;
            _talkUntil = _lineStart + say;
            _typed = 0;
            _lineEnd = _lineStart + Mathf.Max(seconds, say + holdAfter);
            _lastText = text;
            _lastEnd = _lineEnd;
        }

        private void Update()
        {
            if (_queue.Count > 0 && Time.time >= _lineEnd + gap)
            {
                var (text, seconds) = _queue.Dequeue();
                Begin(text, seconds);
            }

            var left = _lineEnd - Time.time;
            if (left <= 0)
            {
                if (_text.gameObject.activeSelf) _text.gameObject.SetActive(false);
            }
            else
            {
                var shown = Mathf.FloorToInt((Time.time - _lineStart) * charsPerSecond) + 1;
                _text.maxVisibleCharacters = shown;
                // A blip every couple of letters while it's being typed out.
                if (shown >= _typed + 2 && shown <= _text.textInfo.characterCount + 1)
                {
                    _typed = shown;
                    ShiftManager.Instance.Sound("blip", 0, voicePitch * UnityEngine.Random.Range(.9f, 1.12f));
                }
                _text.color = new Color(ink.r, ink.g, ink.b, Mathf.Clamp01(left / fadeTime));
                _wanted = Wanted();
            }

            _bob = Mathf.MoveTowards(_bob, Talking ? 1 : 0, Time.deltaTime * 8);
            if (head) head.localPosition = _headRest + Vector3.up * (Mathf.Abs(Mathf.Sin(Time.time * bobRate)) * bob * _bob);

            if (!puffs || !Talking) return;
            _puffDebt += puffRate * Time.deltaTime;
            var from = (head ? head.position : transform.position) + Vector3.up * .3f;
            for (; _puffDebt >= 1; _puffDebt--)
                puffs.Emit(new ParticleSystem.EmitParams
                {
                    position = from + (Vector3)(Random.insideUnitCircle * .15f),
                    velocity = new Vector2(Random.Range(-.7f, .7f), Random.Range(.9f, 1.8f)),
                    startSize = Random.Range(.05f, .1f),
                    startLifetime = Random.Range(.5f, .8f)
                }, 1);
        }

        // Above the anchor, kept on screen: a line near the edge of the room shifts in rather than running off it.
        private Vector3 Wanted()
        {
            var at = (anchor ? anchor : transform).position;
            var cam = Camera.main;
            if (!cam) return at;
            var half = Mathf.Min(width, _text.preferredWidth) / 2 + .2f;
            var edge = cam.ViewportToWorldPoint(new Vector3(0, 0, -cam.transform.position.z)).x;
            var other = cam.ViewportToWorldPoint(new Vector3(1, 0, -cam.transform.position.z)).x;
            at.x = Mathf.Clamp(at.x, edge + half, Mathf.Max(edge + half, other - half));
            return at;
        }

        // Once a frame, after everyone has said their piece: the one who started first keeps its place, and each
        // later one that would overlap an earlier one is lifted above it.
        private void LateUpdate()
        {
            if (_laidOutAt == Time.frameCount) return;
            _laidOutAt = Time.frameCount;
            var up = new List<Speaker>();
            foreach (var s in All) if (s.Showing) up.Add(s);
            up.Sort((a, b) => a._lineStart.CompareTo(b._lineStart));
            for (var i = 0; i < up.Count; i++)
            {
                var me = up[i];
                var at = me._wanted;
                var w = Mathf.Min(me.width, me._text.preferredWidth);
                var h = me._text.preferredHeight;
                for (var pass = 0; pass < up.Count; pass++)
                {
                    var moved = false;
                    for (var j = 0; j < i; j++)
                    {
                        var other = up[j];
                        var ow = Mathf.Min(other.width, other._text.preferredWidth);
                        var otherAt = other._text.transform.position;
                        var oh = other._text.preferredHeight;
                        var apart = Mathf.Abs(at.x - otherAt.x) < (w + ow) / 2 + .1f;
                        var overlaps = at.y < otherAt.y + oh + stackGap && at.y + h + stackGap > otherAt.y;
                        if (!apart || !overlaps) continue;
                        at.y = otherAt.y + oh + stackGap;
                        moved = true;
                    }
                    if (!moved) break;
                }
                me._text.transform.position = at;
            }
        }
    }
}

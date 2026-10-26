using TMPro;
using UnityEngine;

namespace CoalRun.Stations
{
    // Something that talks: what it says appears as little text above it, its head bobs while it's talking,
    // and a few specks puff up around it. The text stays up for the line's duration and fades out; the bobbing
    // and puffing last about as long as it takes to say it. ShiftManager.Say picks who's talking.
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
        [Tooltip("Characters a second it takes to say a line: how long the bobbing and puffing go on.")]
        [SerializeField]
        private float charsPerSecond = 16;
        [SerializeField] private float fadeTime = .5f;
        [SerializeField] private int sortingOrder = 60;

        public bool Talking => Time.time < _talkUntil;

        private TextMeshPro _text;
        private Vector3 _headRest;
        private float _showUntil, _talkUntil, _bob, _puffDebt;

        private void Awake()
        {
            // On its own at the top of the scene, so the speaker's scale (the phone is small) doesn't shrink it;
            // it just follows the anchor.
            var go = new GameObject("Speech: " + name);
            _text = go.AddComponent<TextMeshPro>();
            _text.fontSize = textSize;
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

        public void Speak(string text, float seconds)
        {
            _text.text = text;
            _text.gameObject.SetActive(true);
            _showUntil = Time.time + seconds;
            _talkUntil = Time.time + Mathf.Min(seconds, .4f + text.Length / charsPerSecond);
        }

        private void OnDestroy()
        {
            if (_text) Destroy(_text.gameObject);
        }

        private void OnDisable()
        {
            if (_text) _text.gameObject.SetActive(false);
        }

        private void Update()
        {
            var at = (anchor ? anchor : transform).position;
            // Kept on screen: a line near the edge of the room shifts in rather than running off it.
            var cam = Camera.main;
            if (cam)
            {
                var half = Mathf.Min(width, _text.preferredWidth) / 2 + .2f;
                var edge = cam.ViewportToWorldPoint(new Vector3(0, 0, -cam.transform.position.z)).x;
                var other = cam.ViewportToWorldPoint(new Vector3(1, 0, -cam.transform.position.z)).x;
                at.x = Mathf.Clamp(at.x, edge + half, Mathf.Max(edge + half, other - half));
            }
            _text.transform.position = at;
            var left = _showUntil - Time.time;
            if (left <= 0)
            {
                if (_text.gameObject.activeSelf) _text.gameObject.SetActive(false);
            }
            else _text.color = new Color(ink.r, ink.g, ink.b, Mathf.Clamp01(left / fadeTime));

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
    }
}

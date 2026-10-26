using CoalRun.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CoalRun.UI
{
    // A light blue vignette closing in around the screen while the building is freezing. It starts faint and gets
    // harder as the freeze grace runs out, and past halfway a second, tighter layer fades in under it so the frost
    // visibly closes toward the middle. Eased in and out, so it creeps in rather than snapping on. It holds at its
    // worst while the operator freezes to death, and goes once the report is up.
    public class FrostVignette : MonoBehaviour
    {
        [SerializeField] private Image image;
        [Tooltip("How strong the outer layer is the moment the building starts freezing, and when the grace is nearly out.")]
        [SerializeField]
        private float least = .2f, most = 1;
        [SerializeField] private float opacity = .85f, ease = .5f;
        [Tooltip("Where the frosting starts and ends, from the centre (1 is the edge of the screen): the outer layer, and the tighter one that comes in past halfway.")]
        [SerializeField]
        private Vector2 fromTo = new(.55f, 1.25f), deepFromTo = new(.2f, .85f);
        [SerializeField] private float deepOpacity = .9f;
        [SerializeField] private Color frost = new(.65f, .88f, 1), deepFrost = new(.82f, .94f, 1);

        private Image _deep;
        private float _amount;

        private void Awake()
        {
            image.sprite = Gradient(frost, fromTo);
            image.raycastTarget = false;
            var go = new GameObject("Deep", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            _deep = go.AddComponent<Image>();
            _deep.sprite = Gradient(deepFrost, deepFromTo);
            _deep.raycastTarget = false;
            _deep.color = Color.clear;
        }

        // Clear in the middle, frosting over toward the edges and corners.
        private static Sprite Gradient(Color color, Vector2 range)
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var d = new Vector2(x - size / 2f + .5f, y - size / 2f + .5f).magnitude / (size / 2f);
                    texture.SetPixel(x, y, new Color(color.r, color.g, color.b, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(range.x, range.y, d))));
                }
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f));
        }

        private void Update()
        {
            var game = ShiftManager.Instance;
            // Worst while they freeze to death; gone once the report is up (and for a shift that ended another way).
            var dying = game.Phase == Phase.Frozen && !game.ReportReady;
            var chill = dying ? 1 : game.Chill;
            var target = (game.Freezing && !game.Ended || dying) ? Mathf.Lerp(least, most, Mathf.Pow(chill, .8f)) : 0;
            _amount = Mathf.MoveTowards(_amount, target, Time.deltaTime * ease);
            image.color = new Color(1, 1, 1, _amount * opacity);
            // The tight layer comes in over the second half of the grace.
            var deep = _amount > 0 ? Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.45f, 1, chill)) : 0;
            _deep.color = new Color(1, 1, 1, deep * _amount * deepOpacity);
        }
    }
}

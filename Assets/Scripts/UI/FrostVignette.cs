using CoalRun.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CoalRun.UI
{
    // A light blue vignette closing in around the screen while the building is freezing, harder as the
    // freeze grace runs out. Eased in and out, so it creeps in rather than snapping on.
    public class FrostVignette : MonoBehaviour
    {
        [SerializeField] private Image image;
        [Tooltip("How strong it is the moment the building starts freezing, and when the grace is nearly out.")]
        [SerializeField]
        private float least = .55f, most = 1;
        [SerializeField] private float opacity = .7f, ease = .5f;
        [Tooltip("Where the frosting starts and ends, from the centre (1 is the edge of the screen).")]
        [SerializeField]
        private Vector2 fromTo = new(.55f, 1.25f);
        [SerializeField] private Color frost = new(.65f, .88f, 1);

        private float _amount;

        private void Awake()
        {
            // Clear in the middle, frosting over toward the edges and corners.
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var d = new Vector2(x - size / 2f + .5f, y - size / 2f + .5f).magnitude / (size / 2f);
                    texture.SetPixel(x, y, new Color(frost.r, frost.g, frost.b, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(fromTo.x, fromTo.y, d))));
                }
            texture.Apply();
            image.sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f));
            image.raycastTarget = false;
        }

        private void Update()
        {
            var game = ShiftManager.Instance;
            var target = game.Freezing && !game.Ended ? Mathf.Lerp(least, most, game.Chill) : 0;
            _amount = Mathf.MoveTowards(_amount, target, Time.deltaTime * ease);
            image.color = new Color(1, 1, 1, _amount * opacity);
        }
    }
}

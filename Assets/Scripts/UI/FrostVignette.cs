using CoalRun.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CoalRun.UI
{
    // A light blue vignette closing in around the screen while the building is freezing. It starts barely there and
    // gets harder as the freeze grace runs out, and near the end a second, tighter layer fades in under it so the
    // frost visibly closes toward the middle. Eased in and out, so it creeps in rather than snapping on. When the
    // operator finally goes it isn't a wash that stays: it's a shock, a flash of frost over the whole screen that
    // dies away while the camera is still on them. The vignette holds where it was until the report is up.
    public class FrostVignette : MonoBehaviour
    {
        [SerializeField] private Image image;
        [SerializeField] private Image deep;
        [SerializeField] private Image flash;

        [Tooltip(
            "How strong the outer layer is the moment the building starts freezing, and when the grace is nearly out.")]
        [SerializeField]
        private float least = .05f, most = .75f;

        [Tooltip("How sharply it builds: above 1 it stays faint longer and then comes on late.")] [SerializeField]
        private float curve = 1.5f;

        [SerializeField] private float opacity = .8f, ease = .5f;

        [Tooltip("The shock when they go: its colour, and how long it takes to die away.")] [SerializeField]
        private Color flashColor = new(.88f, .96f, 1);

        [SerializeField] private float flashTime = .9f;

        [SerializeField] private float deepOpacity = .8f;
        private float _amount, _flashLeft;

        private void Start()
        {
            ShiftManager.Instance.OnPhase += Went;
        }

        private void Update()
        {
            var game = ShiftManager.Instance;
            // Worst while they freeze to death; gone once the report is up (and for a shift that ended another way).
            var dying = game.Phase == Phase.Frozen && !game.ReportReady;
            // The worst is the flash's, not the vignette's: when they go it eases back to a modest level under the zoom.
            var chill = dying ? .6f : game.Chill;
            var target = (game.Freezing && !game.Ended) || dying ? Mathf.Lerp(least, most, Mathf.Pow(chill, curve)) : 0;
            _amount = Mathf.MoveTowards(_amount, target, Time.deltaTime * ease);
            image.color = new Color(1, 1, 1, _amount * opacity);
            // The tight layer comes in over the second half of the grace.
            var deepStrength = _amount > 0 ? Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.6f, 1, chill)) : 0;
            deep.color = new Color(1, 1, 1, deepStrength * _amount * deepOpacity);

            // Sudden, then dying away fast and then slowly.
            _flashLeft = Mathf.Max(0, _flashLeft - Time.deltaTime);
            var f = _flashLeft / flashTime;
            flash.color = new Color(flashColor.r, flashColor.g, flashColor.b, f * f);
        }

        private void OnDestroy()
        {
            if (ShiftManager.Instance) ShiftManager.Instance.OnPhase -= Went;
        }

        // The cold has them: the whole screen flashes.
        private void Went(Phase phase)
        {
            if (phase == Phase.Frozen) _flashLeft = flashTime;
        }
    }
}
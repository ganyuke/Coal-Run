using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CoalRun.UI
{
    // One line of the notebook: a box with a tick in it, and what it says.
    public class NotebookRow : MonoBehaviour
    {
        [SerializeField] private RectTransform rect;
        [SerializeField] private TMP_Text label;
        [Tooltip("Turned on when the line is ticked.")]
        [SerializeField]
        private GameObject tick;
        [SerializeField] private Image box;

        public RectTransform Rect => rect;

        public void Set(string text, bool done, float size, Color ink)
        {
            label.text = done ? $"<s>{text}</s>" : text;
            label.fontSize = size;
            // Ticked lines shrink to a single line so a long one doesn't run into the next.
            label.textWrappingMode = done ? TextWrappingModes.NoWrap : TextWrappingModes.Normal;
            label.overflowMode = done ? TextOverflowModes.Ellipsis : TextOverflowModes.Overflow;
            label.color = ink;
            tick.SetActive(done);
            box.color = ink;
        }
    }
}

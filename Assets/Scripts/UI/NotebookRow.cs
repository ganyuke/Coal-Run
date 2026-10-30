using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CoalRun.UI
{
    // The notebook's task: a box with a tick in it, and what it says.
    public class NotebookRow : MonoBehaviour
    {
        [SerializeField] private RectTransform rect;
        [SerializeField] private TMP_Text label;

        [Tooltip("Turned on when the line is ticked.")] [SerializeField]
        private GameObject tick;

        [SerializeField] private Image box;

        public RectTransform Rect => rect;

        // How tall what it says wants to be at the width it has.
        public float TextHeight => label.GetPreferredValues(label.text, label.rectTransform.rect.width, 0).y;

        public void Set(string text, bool done, float size, Color ink)
        {
            label.text = done ? $"<s>{text}</s>" : text;
            label.fontSize = size;
            label.color = ink;
            tick.SetActive(done);
            box.color = ink;
        }
    }
}
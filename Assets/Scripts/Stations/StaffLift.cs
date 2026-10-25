using System.Collections;
using CoalRun.Core;
using UnityEngine;

namespace CoalRun.Stations
{
    // The staff lift at the right end of the lower floor, the only way in or out. Replacement operators
    // step out of it, and the operator clocks out into it. Its door slides aside into the frame.
    public class StaffLift : MonoBehaviour
    {
        [Tooltip("Scaled down toward its right edge to open.")]
        [SerializeField]
        private Transform door;
        [SerializeField] private float slideTime = .4f;
        [Tooltip("Seconds the door stays open before it closes again.")]
        [SerializeField]
        private float holdOpen = 1.6f;

        private ShiftManager _game;
        private float _width, _right;

        private void Start()
        {
            _game = ShiftManager.Instance;
            if (!door) return;
            _width = door.localScale.x;
            _right = door.localPosition.x + _width / 2;
        }

        // Opens, waits, and closes again.
        public void Cycle()
        {
            if (!door) return;
            StopAllCoroutines();
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            _game.Sound("ding");
            yield return Slide(1);
            yield return new WaitForSeconds(holdOpen);
            yield return Slide(0);
        }

        private IEnumerator Slide(float to)
        {
            var from = 1 - door.localScale.x / _width;
            for (float k = 0; k < 1;)
            {
                k = Mathf.Min(1, k + Time.deltaTime / slideTime);
                var open = Mathf.Lerp(from, to, k * k * (3 - 2 * k));
                var w = _width * Mathf.Max(.04f, 1 - open);
                door.localScale = new Vector3(w, door.localScale.y, 1);
                door.localPosition = new Vector3(_right - w / 2, door.localPosition.y, 0);
                yield return null;
            }
        }
    }
}

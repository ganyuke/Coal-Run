using System.Collections;
using UnityEngine;

namespace CoalRun.Core
{
    // The camera, which otherwise sits still showing the whole room: it can ease in on a point and back out again,
    // for the moments that want a close look (the operator freezing).
    public class CameraRig : MonoBehaviour
    {
        [SerializeField] private Camera cam;

        private Vector3 _home;
        private float _homeSize;

        private void Awake()
        {
            _home = transform.position;
            _homeSize = cam.orthographicSize;
        }

        // Eases in on `at`, showing `size` world units from the middle to the top edge, and returns once it's there.
        public IEnumerator FocusOn(Vector2 at, float size, float time)
        {
            // Never showing past the room: the near edge of the view stays inside every wall, floor and ceiling,
            // so a zoom on someone by a wall sits them off-centre rather than showing the dark outside.
            var half = size * cam.aspect;
            var x = Mathf.Clamp(at.x, Mathf.Min(half, Room.Width / 2), Mathf.Max(Room.Width - half, Room.Width / 2));
            var y = Mathf.Clamp(at.y, Mathf.Min(size, Room.Height / 2), Mathf.Max(Room.Height - size, Room.Height / 2));
            yield return Move(new Vector3(x, y, _home.z), size, time);
        }

        // Back to how it started.
        public IEnumerator Home(float time)
        {
            return Move(_home, _homeSize, time);
        }

        private IEnumerator Move(Vector3 position, float size, float time)
        {
            var from = transform.position;
            var fromSize = cam.orthographicSize;
            for (float k = 0; k < 1; k += Time.deltaTime / time)
            {
                var e = Mathf.SmoothStep(0, 1, k);
                transform.position = Vector3.Lerp(from, position, e);
                cam.orthographicSize = Mathf.Lerp(fromSize, size, e);
                yield return null;
            }

            transform.position = position;
            cam.orthographicSize = size;
        }
    }
}
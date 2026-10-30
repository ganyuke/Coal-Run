using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace CoalRun.Gameplay.Machinery
{
    // The fan behind a notched lever: a coloured tile per notch, with snowflakes beyond it for how much
    // cold that notch holds off. The lever's current notch is lit; the rest are dimmed, so the dial reads
    // without numbers.
    [MovedFrom("CoalRun.Stations")]
    public class NotchDial : MonoBehaviour
    {
        [SerializeField] private FloorLever lever;

        [Tooltip("One per notch, left to right: a ray holding the notch's tile and snowflakes, which dim together.")]
        [SerializeField]
        private Transform[] notches;

        [Tooltip("How bright the notches the lever isn't in are drawn.")] [Range(0, 1)] [SerializeField]
        private float dim = .25f;

        private int _lit = -1;

        private void Update()
        {
            if (!lever || lever.Notch == _lit) return;
            _lit = lever.Notch;
            for (var i = 0; i < notches.Length; i++)
            {
                var k = i == _lit ? 1 : dim;
                foreach (var r in notches[i].GetComponentsInChildren<SpriteRenderer>())
                    r.color = new Color(r.color.r, r.color.g, r.color.b, k);
            }
        }
    }
}
using System.Collections.Generic;
using UnityEngine;

namespace CoalRun.Dialogue
{
    // centralized system to lay out overlapping dialogue
    [UnityEngine.Scripting.APIUpdating.MovedFrom("CoalRun.Core")]
    public class SpeechLayout : MonoBehaviour
    {
        [Tooltip("Space between stacked speech texts, in world units.")] [SerializeField]
        private float stackGap = .12f;

        private readonly List<Speaker> _visibleSpeakers = new();

        private Camera _camera;

        private void Awake()
        {
            _camera = Camera.main;
        }

        // use lateupdate so that all speaker update() have already run so we can now do layout
        private void LateUpdate()
        {
            _visibleSpeakers.Sort(CompareSpeechStartTimes);
            for (var speakerIndex = 0; speakerIndex < _visibleSpeakers.Count; speakerIndex++)
            {
                var speaker = _visibleSpeakers[speakerIndex];
                
                // apply transformation to stack and readjust position of dialogue
                var position = CalculatePositionOnScreen(speaker, _camera);
                speaker.Text.transform.position = CalculateStackedPosition(position, speakerIndex);
            }
        }

        // api to register as a speaker
        public void Register(Speaker speaker)
        {
            if (!_visibleSpeakers.Contains(speaker)) _visibleSpeakers.Add(speaker);
        }

        // api to deregister as a speaker
        public void Unregister(Speaker speaker)
        {
            _visibleSpeakers.Remove(speaker);
        }

        // method for sorting lines by start order
        private static int CompareSpeechStartTimes(Speaker firstSpeaker, Speaker secondSpeaker)
        {
            return firstSpeaker.LineStartedAt.CompareTo(secondSpeaker.LineStartedAt);
        }

        private static Vector3 CalculatePositionOnScreen(Speaker speaker, Camera camera)
        {
            var position = speaker.AnchorPosition;
            if (!camera) return position;
            var halfWidth = speaker.TextWidth / 2 + .2f;

            var screenLeft = camera.ViewportToWorldPoint(new Vector3(0, 0, -camera.transform.position.z)).x;
            var screenRight = camera.ViewportToWorldPoint(new Vector3(1, 0, -camera.transform.position.z)).x;

            // clamp text so it doesn't spill off the side of the screen
            position.x = Mathf.Clamp(position.x, screenLeft + halfWidth, Mathf.Max(screenLeft + halfWidth, screenRight - halfWidth));
            return position;
        }

        // stack newer lines on top of the bottommost (earliest started) playing line
        // kinda first in, first out though lines still play regardless of position
        private Vector3 CalculateStackedPosition(Vector3 position, int speakerIndex)
        {
            var speaker = _visibleSpeakers[speakerIndex];
            var textWidth = speaker.TextWidth;
            var textHeight = speaker.Text.preferredHeight;

            // loop over currently 
            for (var attempt = 0; attempt < _visibleSpeakers.Count; attempt++)
            {
                var movedUp = false;

                for (var earlierSpeakerIndex = 0; earlierSpeakerIndex < speakerIndex; earlierSpeakerIndex++)
                {
                    var earlierSpeaker = _visibleSpeakers[earlierSpeakerIndex];
                    var earlierTextPosition = earlierSpeaker.Text.transform.position;
                    var earlierTextHeight = earlierSpeaker.Text.preferredHeight;

                    // math to check if boxes are touching or overlap in both the horizontal and vertical direction
                    var overlapsHorizontally =
                        Mathf.Abs(position.x - earlierTextPosition.x) < (textWidth + earlierSpeaker.TextWidth) / 2 + .1f;
                    var overlapsVertically = position.y < earlierTextPosition.y + earlierTextHeight + stackGap &&
                                             position.y + textHeight + stackGap > earlierTextPosition.y;
                    if (!overlapsHorizontally || !overlapsVertically) continue;

                    // if overlap occured, move the line's bottom point just above the overlapped line
                    position.y = earlierTextPosition.y + earlierTextHeight + stackGap;
                    movedUp = true;
                }

                // if nothing moves on the pass then all dialogue is aligned
                if (!movedUp) break;
            }

            return position;
        }
    }
}

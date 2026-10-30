using CoalRun.Core;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Scripting.APIUpdating;

namespace CoalRun.Environment
{
    // The warning lamp over the staff lift: dark normally, pulsing red while the building is at or below
    // freezing. The only sign of the freeze grace running out besides the operator slowing down.
    [MovedFrom("CoalRun.Stations")]
    public class FreezeLamp : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer lamp;
        [SerializeField] private Color off = new(.25f, .1f, .1f);
        [SerializeField] private Color on = new(1, .12f, .08f);

        [Tooltip("The red light it throws while it pulses.")] [SerializeField]
        private Light2D glow;

        [SerializeField] private float glowIntensity = 1.4f;

        [Tooltip("Pulses a second.")] [SerializeField]
        private float pulse = 1.2f;

        [Tooltip(
            "Times the buzzer sounds when the building starts to freeze; the lamp keeps pulsing on its own after.")]
        [SerializeField]
        private int alarmRings = 4;

        private int _beat;

        private ShiftManager _game;
        private int _rung;

        private void Start()
        {
            _game = ShiftManager.Instance;
        }

        private void Update()
        {
            var lit = _game.Freezing && !_game.Ended;
            var k = lit ? .6f + .4f * Mathf.Cos(Time.time * pulse * 2 * Mathf.PI) : 0;
            // The buzzer sounds on the first few pulses of a freeze and then leaves the lamp to it, until the next freeze.
            var beat = Mathf.FloorToInt(Time.time * pulse);
            if (!lit)
            {
                _rung = 0;
            }
            else if (beat != _beat && _rung < alarmRings)
            {
                _rung++;
                _game.PlayCue("alarm", .5f);
            }

            _beat = beat;
            lamp.color = Color.Lerp(off, on, k);
            if (glow) glow.intensity = glowIntensity * k;
        }
    }
}
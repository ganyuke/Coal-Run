using System.Collections;
using CoalRun.Core;
using UnityEngine.Rendering.Universal;
using UnityEngine;

namespace CoalRun.Dialogue
{
    // controls the dialogue for the red phone by the elevator
    // one ring for standard dialogue, two rings for respawns
    [UnityEngine.Scripting.APIUpdating.MovedFrom("CoalRun.Stations")]
    public class RedPhone : MonoBehaviour
    {
        [SerializeField] private float ringBelow = 13;
        [SerializeField] private float desperateBelow = 7;

        [Tooltip("Red light that flashes in time with the ringing.")] [SerializeField]
        private Light2D ringLight;

        [SerializeField] private float ringLightIntensity = 1.6f;

        [Tooltip("Speaker script for the dialogue voice.")] [SerializeField]
        private Speaker voice;

        [Tooltip("How far it rocks on its hook while ringing, in degrees.")] [SerializeField]
        private float shake = 9;

        private bool _calling;

        private ShiftManager _game;
        private bool _owesThanks;

        private Quaternion _rest;

        // How far down the three calls it has been this shift (0 none, 3 all), and whether a thank-you is owed.
        private int _said;

        private void Start()
        {
            _game = ShiftManager.Instance;
            _rest = transform.localRotation;
        }

        // Rings in bursts: a quick rattle, then a pause.
        private void Update()
        {
            var burst = _calling && Time.time % .9f < .45f;
            transform.localRotation = _rest * Quaternion.Euler(0, 0, burst ? Mathf.Sin(Time.time * 60) * shake : 0);
            if (ringLight)
                ringLight.intensity = burst ? ringLightIntensity * (.6f + .4f * Mathf.Sin(Time.time * 40)) : 0;
        }

        private void FixedUpdate()
        {
            if (_game.Phase == Phase.Briefing)
            {
                _said = 0;
                _owesThanks = false;
                return;
            }

            // Not while it's still talking: the voice's bubble hangs off the phone, so ringing would shake it.
            if (!_game.ShiftRunning || _calling || (voice && voice.Showing)) return;
            var t = _game.Temperature;
            var level = _game.Freezing ? 3 : t < desperateBelow ? 2 : t < ringBelow ? 1 : 0;
            // Warm again after a call: thanks, once.
            if (level == 0 && _owesThanks)
            {
                _owesThanks = false;
                Announce(_game.Lines.phone.warmed);
                return;
            }

            // Only the deepest call it hasn't made yet: dropping fast skips the ones in between.
            if (level <= _said) return;
            _said = level;
            _owesThanks = true;
            var phone = _game.Lines.phone;
            Announce(level == 3 ? phone.frozen : level == 2 ? phone.freezing : phone.cold);
        }

        // Rings `rings` times, then says its piece. A death's news cuts in over a call about the cold, hanging up on
        // whatever it was saying first.
        public void Announce(string line, int rings = 1)
        {
            StopAllCoroutines();
            if (voice) voice.Forget();
            StartCoroutine(Call(line, rings));
        }

        private IEnumerator Call(string line, int rings)
        {
            _calling = true;
            for (var i = 0; i < rings; i++)
            {
                _game.PlayCue("ring", 1);
                yield return new WaitForSeconds(1.3f);
            }

            _calling = false;
            _game.PlayCue("phone");
            _game.Say(line, voice);
        }
    }
}

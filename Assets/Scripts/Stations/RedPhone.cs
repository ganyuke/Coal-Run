using System.Collections;
using CoalRun.Core;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CoalRun.Stations
{
    // The red phone by the staff lift: brass upstairs ringing down about the heat. Nobody answers it: it rings once (twice
    // for a death) and then the voice comes on the line by itself. It has three things to say as the building cools, each
    // said once a shift: that it's getting cold (below `ringBelow`), that it's bad (below `desperateBelow`) and that it's
    // freezing (0°, where the chill starts). Once it's warm again it thanks the operator, if it called at all. It's how the
    // player hears that the building is cooling without looking at the thermometer.
    public class RedPhone : MonoBehaviour
    {
        [SerializeField] private float ringBelow = 13;
        [SerializeField] private float desperateBelow = 7;
        [Tooltip("A red light that flashes in time with the ringing.")]
        [SerializeField]
        private Light2D ringLight;
        [SerializeField] private float ringLightIntensity = 1.6f;
        [Tooltip("Brass's voice on the line, above the phone.")]
        [SerializeField]
        private Speaker voice;
        [Tooltip("How far it rocks on its hook while ringing, in degrees.")]
        [SerializeField]
        private float shake = 9;

        private ShiftManager _game;
        private Quaternion _rest;
        private bool _calling;
        // How far down the three calls it has been this shift (0 none, 3 all), and whether a thank-you is owed.
        private int _said;
        private bool _owesThanks;

        private void Start()
        {
            _game = ShiftManager.Instance;
            _rest = transform.localRotation;
        }

        private void FixedUpdate()
        {
            if (_game.Phase == Phase.Briefing) { _said = 0; _owesThanks = false; return; }
            // Not while it's still talking: the voice's bubble hangs off the phone, so ringing would shake it.
            if (!_game.ShiftRunning || _calling || voice && voice.Showing) return;
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

        // Rings in bursts: a quick rattle, then a pause.
        private void Update()
        {
            var burst = _calling && Time.time % .9f < .45f;
            transform.localRotation = _rest * Quaternion.Euler(0, 0, burst ? Mathf.Sin(Time.time * 60) * shake : 0);
            if (ringLight) ringLight.intensity = burst ? ringLightIntensity * (.6f + .4f * Mathf.Sin(Time.time * 40)) : 0;
        }

        // Rings `rings` times, then says its piece. A death's news cuts in over a call about the cold, hanging up on
        // whatever it was saying first.
        public void Announce(string line, int rings = 1)
        {
            StopAllCoroutines();
            if (voice) voice.Hush();
            StartCoroutine(Call(line, rings));
        }

        private IEnumerator Call(string line, int rings)
        {
            _calling = true;
            for (var i = 0; i < rings; i++)
            {
                _game.Sound("ring", 1);
                yield return new WaitForSeconds(1.3f);
            }
            _calling = false;
            _game.Sound("phone");
            _game.Say(line, 6, voice);
        }
    }
}

using System.Collections;
using CoalRun.Core;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CoalRun.Stations
{
    // The red phone by the staff lift: brass upstairs ringing down for heat. Nobody answers it: it rings three
    // times and then the voice comes on the line by itself. It calls once the building drops below `ringBelow`,
    // more often the colder it gets, and rings down too when someone dies. It's how the player hears that the
    // building is cooling without looking at the thermometer.
    public class RedPhone : MonoBehaviour
    {
        [SerializeField] private float ringBelow = 13;
        [Tooltip("A red light that flashes in time with the ringing.")]
        [SerializeField]
        private Light2D ringLight;
        [SerializeField] private float ringLightIntensity = 1.6f;
        [Tooltip("Brass's voice on the line, above the phone.")]
        [SerializeField]
        private Speaker voice;
        [Tooltip("Quiet this long after a call, on top of the wait for the next (shorter the colder it is).")]
        [SerializeField]
        private float cooldown = 10;
        [Tooltip("How far it rocks on its hook while ringing, in degrees.")]
        [SerializeField]
        private float shake = 9;

        private ShiftManager _game;
        // Seconds to the next call.
        private float _timer;
        private Quaternion _rest;
        private bool _calling;

        private void Start()
        {
            _game = ShiftManager.Instance;
            _rest = transform.localRotation;
        }

        private void FixedUpdate()
        {
            if (!_game.ShiftRunning || _calling) return;
            var t = _game.Temperature;
            // Warm again: whatever it was calling about is fixed.
            if (t >= ringBelow) { _timer = 0; return; }
            _timer -= Time.fixedDeltaTime;
            if (_timer > 0) return;
            _timer = cooldown + (t < 6 ? 4 : t < 9 ? 8 : 15);
            Announce(t < 7 ? "“We can’t feel our hands. Forget the target. Just keep us alive.”" : "“It’s getting cold upstairs. Could you turn the furnace up?”");
        }

        // Rings in bursts: a quick rattle, then a pause.
        private void Update()
        {
            var burst = _calling && Time.time % .9f < .45f;
            transform.localRotation = _rest * Quaternion.Euler(0, 0, burst ? Mathf.Sin(Time.time * 60) * shake : 0);
            if (ringLight) ringLight.intensity = burst ? ringLightIntensity * (.6f + .4f * Mathf.Sin(Time.time * 40)) : 0;
        }

        // Rings three times, then says its piece. A death's news cuts in over a call about the cold.
        public void Announce(string line)
        {
            StopAllCoroutines();
            StartCoroutine(Call(line));
        }

        private IEnumerator Call(string line)
        {
            _calling = true;
            for (var i = 0; i < 3; i++)
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

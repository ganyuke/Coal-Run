using System;
using System.Collections.Generic;
using CoalRun.Core;
using UnityEngine;
using Random = UnityEngine.Random;

namespace CoalRun.Audio
{
    // Everything you hear. Stations and the hub raise named sounds (ShiftManager.Sound); the director plays the cue
    // with that name: a random one of its clips at a little random pitch, or, for a sound that goes on (a crank turning,
    // a cart dragging), a loop that fades in while the event keeps arriving and out when it stops. Underneath are the beds:
    // the wind and the storm's howl, the fire, the freight lift's hum, a low drone, and, as the cold gets to the operator,
    // a heartbeat and the chattering of teeth. Nothing here changes the game.
    public class AudioDirector : MonoBehaviour
    {
        [SerializeField] private Cue[] cues;
        [SerializeField] private Bed[] beds;
        [Range(0, 1)] [SerializeField] private float master = .8f;
        [SerializeField] private int voices = 14;
        private readonly Dictionary<string, AudioSource> _beds = new();

        private readonly Dictionary<string, Cue> _cues = new();
        private readonly Dictionary<string, Sustained> _sustained = new();
        private ShiftManager _game;
        private int _next;
        private AudioSource[] _pool;

        private void Start()
        {
            _game = ShiftManager.Instance;
            AudioListener.volume = master;
            foreach (var cue in cues) _cues[cue.name] = cue;
            _pool = new AudioSource[voices];
            for (var i = 0; i < voices; i++) _pool[i] = NewSource("Voice " + i, false);
            foreach (var bed in beds)
            {
                if (!bed.clip) continue;
                var source = NewSource("Bed: " + bed.name, true);
                source.clip = bed.clip;
                source.volume = 0;
                source.Play();
                _beds[bed.name] = source;
            }

            _game.OnSound += Heard;
        }

        private void Update()
        {
            foreach (var s in _sustained.Values)
            {
                var want = Time.unscaledTime - s.Heard < .25f ? s.Target : 0;
                s.Source.volume = Mathf.MoveTowards(s.Source.volume, want, Time.unscaledDeltaTime * 4);
                if (want == 0 && s.Source.volume <= 0 && s.Source.isPlaying) s.Source.Pause();
            }

            Beds();
        }

        private void OnDestroy()
        {
            if (_game) _game.OnSound -= Heard;
        }

        private AudioSource NewSource(string sourceName, bool loop)
        {
            var go = new GameObject(sourceName);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = loop;
            s.spatialBlend = 0;
            return s;
        }

        private void Heard(string sound, float amount)
        {
            if (!_cues.TryGetValue(sound, out var cue) || cue.clips == null || cue.clips.Length == 0) return;
            if (cue.sustain)
            {
                if (!_sustained.TryGetValue(sound, out var s))
                {
                    var source = NewSource("Loop: " + sound, true);
                    source.clip = cue.clips[0];
                    source.volume = 0;
                    s = new Sustained { Source = source };
                    _sustained[sound] = s;
                }

                s.Target = cue.volume * (cue.amountScale > 0 ? Mathf.Clamp01(amount * cue.amountScale) : 1);
                s.Heard = Time.unscaledTime;
                if (!s.Source.isPlaying) s.Source.Play();
                return;
            }

            var voice = _pool[_next++ % _pool.Length];
            voice.clip = cue.clips[Random.Range(0, cue.clips.Length)];
            voice.pitch = cue.pitchFromAmount ? amount : Random.Range(cue.pitch.x, cue.pitch.y);
            voice.volume = cue.volume * (cue.amountScale > 0 ? Mathf.Clamp01(amount * cue.amountScale) : 1);
            voice.Play();
        }

        private void SetBed(string bedName, float level, float pitch = 1)
        {
            if (!_beds.TryGetValue(bedName, out var source)) return;
            var cap = 1f;
            foreach (var b in beds)
                if (b.name == bedName)
                    cap = b.volume;
            source.volume = Mathf.MoveTowards(source.volume, level * cap, Time.unscaledDeltaTime * .8f);
            source.pitch = Mathf.Lerp(source.pitch, pitch, Time.unscaledDeltaTime * 3);
        }

        // The beds follow the game: louder fire for a harder burn, the lift humming faster with its speed, the cold getting in.
        private void Beds()
        {
            var playing = _game.Playing;
            // The wind follows the storm: always there, rising and gusting with it, and a howl on top in the worst of it.
            var storm = _game.Storm;
            var blow = storm ? storm.Blow : .4f;
            SetBed("wind",
                (.25f + .5f * Mathf.Min(blow, 1)) * (playing || _game.Phase == Phase.Briefing ? 1 : .6f) +
                _game.Chill * .25f, .9f + .2f * blow);
            SetBed("howl", storm ? Mathf.Clamp01(blow * blow) : 0, .92f + .16f * (storm ? storm.Gust : 0));
            SetBed("drone", _game.Phase == Phase.Frozen ? 0 : playing ? .5f : .35f);
            var f = _game.Furnace;
            var top = _game.Tune.burnRates[^1];
            SetBed("fire", f.Lit && playing ? .22f + .78f * Mathf.Clamp01(f.BurnRate / top) : 0,
                1 + Mathf.Clamp01(f.BurnRate / top) * .1f);
            var speed = Mathf.Abs(_game.Lift.Speed);
            SetBed("lift", Mathf.Clamp01(speed / _game.Tune.liftMaxSpeed) * .9f,
                .75f + speed / _game.Tune.liftMaxSpeed * .7f);
            var cold = _game.Freezing && playing ? _game.Chill : 0;
            SetBed("heartbeat", cold * .9f, .9f + cold * .5f);
            SetBed("shiver", cold * .6f);
        }

        [Serializable]
        public class Cue
        {
            public string name;
            public AudioClip[] clips;
            [Range(0, 2)] public float volume = 1;
            public Vector2 pitch = new(.95f, 1.05f);

            [Tooltip(
                "When the event carries an amount (speed, load), the sound is that much louder: volume * the amount times this, up to 1. 0 means the amount is ignored.")]
            public float amountScale;

            [Tooltip("A loop kept alive by the event repeating, instead of a one-shot each time.")]
            public bool sustain;

            [Tooltip("The event's amount is the pitch (the speech blips).")]
            public bool pitchFromAmount;
        }

        [Serializable]
        public class Bed
        {
            public string name;
            public AudioClip clip;
            [Range(0, 1)] public float volume = 1;
        }

        private class Sustained
        {
            public AudioSource Source;
            public float Target, Heard;
        }
    }
}
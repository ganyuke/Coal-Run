using System.Collections.Generic;
using CoalRun.Core;
using CoalRun.Gameplay.Characters;
using TMPro;
using UnityEngine;
using Random = UnityEngine.Random;

namespace CoalRun.Dialogue
{
    // handle a character talking with a nifty little bob animation and speech puffs
    [UnityEngine.Scripting.APIUpdating.MovedFrom("CoalRun.Stations")]
    public class Speaker : MonoBehaviour
    {
        [Header("Owner")]
        [Tooltip("Optional human this voice belongs to. For interrupting dialogue on death.")]
        [SerializeField]
        private Human human;

        [Header("Dialogue queue")]
        [Tooltip("Maximum waiting lines. Excess lines dropped oldest-first.")]
        [SerializeField]
        private int maxQueued = 2;

        [Tooltip("Cooldown for repeat lines after line ends.")] [SerializeField]
        private float repeatAfter = 6;

        private readonly Queue<string> _pendingLines = new();
        private string _previousDialogue = "";
        private float _previousLineEndsAt;

        [Header("Text and layout")] [SerializeField]
        private TextMeshPro text;

        [Tooltip("Fixed starting position for the text; the text child moves independently.")] [SerializeField]
        private Transform anchor;

        [SerializeField] private SpeechLayout speechLayout;
        [SerializeField] private float fadeTime = .5f;

        private Color _originalTextColor;

        [Header("Typing and hold")] [Tooltip("Characters revealed each second. Lower is slower.")] [SerializeField]
        private float charsPerSecond = 16;

        [Tooltip("Seconds the completed line stays visible after typing.")] [SerializeField]
        private float holdAfter = 1.8f;

        private float _typingEndsAt;
        private float _lineEndsAt;

        [Header("Voice blips")] [Tooltip("Pitch of the sound played as letters appear.")] [SerializeField]
        private float voicePitch = 1;

        private int _lastBlipCharacter;

        [Header("Head bob")] [Tooltip("Part animated up and down while typing.")] [SerializeField]
        private Transform head;

        [SerializeField] private float bob = .07f, bobRate = 13;

        private Vector3 _headRestPosition;
        private float _bobStrength;

        [Header("Speech particles")]
        [Tooltip("World-space speech particles. Appearance is configured on the particle system.")]
        [UnityEngine.Serialization.FormerlySerializedAs("puffs")]
        [SerializeField]
        private ParticleSystem speechPuffs;

        [SerializeField] private float puffRate = 10;

        private float _pendingPuffs;

        [Header("Cold breath")]
        [SerializeField] private ParticleSystem coldBreath;

        [Tooltip("Show breath below this temperature in Celsius. Set to 0 to disable.")]
        [SerializeField]
        private float breathBelow;

        private float _nextBreathAt;

        // i want to still use the inspector and you can't really do that with private setter
        public TextMeshPro Text => text;
        
        public Vector3 AnchorPosition => (anchor ? anchor : transform).position;
        public float TextWidth => Mathf.Min(text.rectTransform.rect.width, text.preferredWidth);

        public float LineStartedAt { get; private set; }

        // check where character is currently talking
        private bool Talking => Time.time < _typingEndsAt;

        // check where line is visible after talking
        public bool Showing => Time.time < _lineEndsAt;
        public bool IsBusy => Showing || _pendingLines.Count > 0;

        // Unity lifecycle

        private void Awake()
        {
            _originalTextColor = text.color;
            text.gameObject.SetActive(false);
            if (head) _headRestPosition = head.localPosition;
        }

        private void Update()
        {
            if (Time.time >= _lineEndsAt)
            {
                if (text.gameObject.activeSelf) StopTalking();
                if (_pendingLines.Count > 0) StartTalking(_pendingLines.Dequeue());
            }

            var secondsRemaining = _lineEndsAt - Time.time;
            if (secondsRemaining > 0)
            {
                UpdateTypewriter();
                text.color = GetTextColor(secondsRemaining);
            }

            // yummy yummy animations
            UpdateHeadBob();
            UpdateColdBreath();
            UpdateSpeechPuffs();
        }

        private void OnEnable()
        {
            // subscribe to death event if human
            if (human) human.Died += Forget;
        }

        private void OnDisable()
        {
            if (human) human.Died -= Forget;
            Forget();
        }

        // Dialogue queue and line lifecycle

        // start character speaking
        public void Speak(string dialogue)
        {
            if (human && human.IsDead) return;

            // reject repeat dialogue too closely/concurrently with each other
            if (dialogue == _previousDialogue && Time.time < _previousLineEndsAt + repeatAfter) return;
            if (_pendingLines.Contains(dialogue)) return;

            _pendingLines.Enqueue(dialogue);

            // forget the oldest waiting line if there are too many in the queue
            while (_pendingLines.Count > maxQueued) _pendingLines.Dequeue();
        }

        // stops currently running dialogue and clears all queued dialogue
        // for when a character dies mid-dialogue
        public void Forget()
        {
            _pendingLines.Clear();
            StopTalking();
        }

        private void StartTalking(string line)
        {
            text.text = line;
            text.maxVisibleCharacters = 0;
            text.gameObject.SetActive(true);
            text.ForceMeshUpdate();
            var typingDuration = GetTypingDuration();
            LineStartedAt = Time.time;
            _typingEndsAt = LineStartedAt + typingDuration;
            _lastBlipCharacter = 0;
            _lineEndsAt = _typingEndsAt + Mathf.Max(0, holdAfter);
            _previousDialogue = line;
            _previousLineEndsAt = _lineEndsAt;
            if (speechLayout) speechLayout.Register(this);
        }

        private void StopTalking()
        {
            if (speechLayout) speechLayout.Unregister(this);
            _lineEndsAt = _typingEndsAt = Time.time;
            _bobStrength = _pendingPuffs = 0;
            if (head) head.localPosition = _headRestPosition;
            if (text) text.gameObject.SetActive(false);
        }

        // Typing and text fade

        private float GetTypingDuration()
        {
            return text.textInfo.characterCount / Mathf.Max(1, charsPerSecond);
        }

        private int GetVisibleCharacterCount()
        {
            return Mathf.Min(text.textInfo.characterCount,
                Mathf.FloorToInt((Time.time - LineStartedAt) * Mathf.Max(1, charsPerSecond)));
        }

        private Color GetTextColor(float secondsLeft)
        {
            var color = _originalTextColor;
            color.a *= fadeTime > 0 ? Mathf.Clamp01(secondsLeft / fadeTime) : 1;
            return color;
        }
        
        private void UpdateTypewriter()
        {
            var visibleCharacters = GetVisibleCharacterCount();
            text.maxVisibleCharacters = visibleCharacters;

            // skip blip sound if insignificant/no amount of chars is being spoken
            if (visibleCharacters < _lastBlipCharacter + 2) return;

            _lastBlipCharacter = visibleCharacters;
            ShiftManager.Instance.PlayCue("blip", 0, voicePitch * Random.Range(.9f, 1.12f));
        }

        // Speech effects and cold breath

        private void UpdateHeadBob()
        {
            _bobStrength = Mathf.MoveTowards(_bobStrength, Talking ? 1 : 0, Time.deltaTime * 8);
            if (head)
                head.localPosition = _headRestPosition +
                                     Vector3.up * (Mathf.Abs(Mathf.Sin(Time.time * bobRate)) * bob * _bobStrength);
        }

        private void UpdateColdBreath()
        {
            if (human && human.IsDead) return;
            if (coldBreath && breathBelow > 0 && ShiftManager.Instance.Temperature < breathBelow &&
                Time.time >= _nextBreathAt)
            {
                _nextBreathAt = Time.time + Mathf.Lerp(1.4f, 3f,
                    Mathf.InverseLerp(-10, breathBelow, ShiftManager.Instance.Temperature));
                coldBreath.Emit(1);
            }
        }

        private void UpdateSpeechPuffs()
        {
            if (!speechPuffs || !Talking) return;
            _pendingPuffs += puffRate * Time.deltaTime;
            var count = Mathf.FloorToInt(_pendingPuffs);
            if (count == 0) return;
            _pendingPuffs -= count;
            speechPuffs.Emit(count);
        }
    }
}

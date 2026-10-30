using System.Collections.Generic;
using CoalRun.Core;
using CoalRun.Gameplay.Characters;
using TMPro;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using Random = UnityEngine.Random;

namespace CoalRun.Dialogue
{
    // handle a character talking with a nifty little bob animation and speech puffs
    [MovedFrom("CoalRun.Stations")]
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

        [Header("Text and layout")] [SerializeField]
        private TextMeshPro text;

        [Tooltip("Fixed starting position for the text; the text child moves independently.")] [SerializeField]
        private Transform anchor;

        [SerializeField] private SpeechLayout speechLayout;
        [SerializeField] private float width = 6;
        [SerializeField] private float fadeTime = .5f;

        [Header("Typing and hold")] [Tooltip("Characters revealed each second. Lower is slower.")] [SerializeField]
        private float charsPerSecond = 16;

        [Tooltip("Seconds the completed line stays visible after typing.")] [SerializeField]
        private float holdAfter = 1.8f;

        [Header("Voice blips")] [Tooltip("Pitch of the sound played as letters appear.")] [SerializeField]
        private float voicePitch = 1;

        [Header("Head bob")] [Tooltip("Part animated up and down while typing.")] [SerializeField]
        private Transform head;

        [SerializeField] private float bob = .07f, bobRate = 13;

        [Header("Speech particles")]
        [Tooltip("World-space particles shared by speech puffs and cold breath. Optional.")]
        [SerializeField]
        private ParticleSystem puffs;

        [SerializeField] private float puffRate = 10;

        [Header("Cold breath")]
        [Tooltip("Show breath below this temperature in Celsius. Set to 0 to disable.")]
        [SerializeField]
        private float breathBelow;

        private readonly Queue<string> _pendingLines = new();
        private float _bobStrength;

        private Vector3 _headRestPosition;

        private int _lastBlipCharacter;
        private float _lineEndsAt;

        private float _nextBreathAt;

        private Color _originalTextColor;

        private float _pendingPuffs;
        private string _previousDialogue = "";
        private float _previousLineEndsAt;

        private float _typingEndsAt;

        // i want to still use the inspector and you can't really do that with private setter
        public TextMeshPro Text => text;

        public Vector3 AnchorPosition => (anchor ? anchor : transform).position;
        public float TextWidth => Mathf.Min(width, text.preferredWidth);

        public float LineStartedAt { get; private set; }

        // check where character is currently talking
        private bool Talking => Time.time < _typingEndsAt;

        // check where line is visible after talking
        public bool Showing => Time.time < _lineEndsAt;

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
            if (puffs && breathBelow > 0 && ShiftManager.Instance.Temperature < breathBelow &&
                Time.time >= _nextBreathAt)
            {
                _nextBreathAt = Time.time + Mathf.Lerp(1.4f, 3f,
                    Mathf.InverseLerp(-10, breathBelow, ShiftManager.Instance.Temperature));
                var mouthPosition = (head ? head.position : transform.position) + Vector3.up * .2f;
                puffs.Emit(new ParticleSystem.EmitParams
                {
                    position = mouthPosition,
                    velocity = new Vector2(Random.Range(-.25f, .25f), Random.Range(.35f, .6f)),
                    startSize = Random.Range(.2f, .3f),
                    startColor = new Color(.88f, .94f, 1, .8f),
                    rotation = Random.Range(0f, 90f),
                    startLifetime = Random.Range(1f, 1.4f)
                }, 1);
            }
        }

        private void UpdateSpeechPuffs()
        {
            if (!puffs || !Talking) return;
            _pendingPuffs += puffRate * Time.deltaTime;
            var mouthPosition = (head ? head.position : transform.position) + Vector3.up * .3f;
            for (; _pendingPuffs >= 1; _pendingPuffs--)
                puffs.Emit(new ParticleSystem.EmitParams
                {
                    position = mouthPosition + (Vector3)(Random.insideUnitCircle * .15f),
                    velocity = new Vector2(Random.Range(-.7f, .7f), Random.Range(.9f, 1.8f)),
                    startSize = Random.Range(.05f, .1f),
                    startLifetime = Random.Range(.5f, .8f)
                }, 1);
        }
    }
}
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Scripting.APIUpdating;

namespace CoalRun.Environment
{
    // A light that wavers: a lamp on a loose cord, a bulb on a bad circuit. Perlin noise, so it breathes rather than strobes.
    [MovedFrom("CoalRun.Stations")]
    public class LightFlicker : MonoBehaviour
    {
        [SerializeField] private Light2D lamp;
        [SerializeField] private float baseIntensity = 1;

        [Tooltip("How far it wanders either side of the base, as a fraction of it.")] [SerializeField]
        private float wander = .12f;

        [SerializeField] private float speed = 1.4f;

        [Tooltip("Now and then it drops out for a blink: how often (a second) and how deep.")] [SerializeField]
        private float blinkRate = .03f, blinkDepth = .5f;

        private float _seed, _blink;

        private void Awake()
        {
            _seed = Random.value * 100;
        }

        private void Update()
        {
            if (!lamp) return;
            var n = Mathf.PerlinNoise(_seed, Time.time * speed) * 2 - 1;
            _blink = Mathf.MoveTowards(_blink, 0, Time.deltaTime * 6);
            if (Random.value < blinkRate * Time.deltaTime) _blink = 1;
            lamp.intensity = baseIntensity * (1 + n * wander) * (1 - _blink * blinkDepth);
        }
    }
}
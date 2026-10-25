using System;
using System.Collections.Generic;
using UnityEngine;

public enum Phase { Tutorial, Briefing, Shift, Replacement, Complete, Frozen }

// The hub. Holds the state every station shares (phase, clock, the day's settings) and the
// references stations use to find each other. UI and audio listen to its events instead of
// stations calling them directly.
[DefaultExecutionOrder(-50)]
public class ShiftManager : MonoBehaviour
{
    public static ShiftManager Instance { get; private set; }

    [SerializeField] Tuning tuning;
    [SerializeField] DayConfig day;
    [SerializeField] InputReader input;
    [SerializeField] PlayerController player;
    [SerializeField] Cart cart;
    [SerializeField] Lift lift;
    [SerializeField] CoalField coal;
    [SerializeField] Chute chute;

    public Tuning Tune => tuning;
    public DayConfig Day => day;
    public PlayerController Player => player;
    public Cart Cart => cart;
    public Lift Lift => lift;
    public CoalField Coal => coal;
    public Chute Chute => chute;

    public Phase Phase { get; private set; } = Phase.Shift;
    public bool CanAct => Phase is Phase.Tutorial or Phase.Shift;
    public float Elapsed { get; private set; }
    public string Message { get; private set; } = "";
    public float MessageFor { get; private set; }
    public float InitialCoal { get; private set; }
    // Every unit of coal is always in exactly one place, so this should never change.
    public float TotalCoal => chute.Bunker + cart.Coal + player.Hands + coal.Loose;

    // name, loudness
    public event Action<string, float> OnSound;

    readonly Dictionary<string, float> soundTimers = new();

    void Awake()
    {
        Instance = this;
        Time.fixedDeltaTime = tuning.step;
        Physics2D.gravity = new Vector2(0, tuning.gravity);
        GameLayers.ConfigureCollisions();
    }

    void Start() => InitialCoal = TotalCoal;

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        Elapsed += dt;
        MessageFor -= dt;
        if (input.Current.debugWheel) cart.SetWheels(cart.Wheels < 2 ? 2 : 1);
        RecoverLost();
    }

    public void Say(string message, float duration = 5)
    {
        Message = message;
        MessageFor = duration;
    }

    // Repeats of the same sound inside the cooldown are dropped, so stations can call this every step.
    public void Sound(string name, float cooldown = .12f, float amount = 1)
    {
        if (soundTimers.TryGetValue(name, out var until) && until > Elapsed) return;
        soundTimers[name] = Elapsed + cooldown;
        OnSound?.Invoke(name, amount);
    }

    // Accidents kill only the operator. Placeholder until M4's replacement worker: the next one
    // walks in from the staff lift straight away.
    public void Die(string reason)
    {
        Say($"{reason}. Another worker is on the way.", 5);
        Sound("hurt");
        player.Respawn(new Vector2(30.5f, Room.Lower + .75f));
    }

    // Nothing can leave the room. Anything physics squeezes through a wall or floor is put back on the lower floor.
    void RecoverLost()
    {
        static bool Lost(Vector2 p) => !(p.x > 0 && p.x < Room.Width && p.y > Room.Lower - 1.5f && p.y < Room.Height + 4);
        static void Put(Rigidbody2D b, float y)
        {
            float x = float.IsFinite(b.position.x) ? Mathf.Clamp(b.position.x, 5.5f, 28.5f) : 14;
            b.position = new Vector2(x, y);
            b.rotation = 0;
            b.linearVelocity = Vector2.zero;
            b.angularVelocity = 0;
        }
        if (Lost(cart.Body.position))
        {
            player.ReleaseGrip();
            Put(cart.Body, Room.Lower + .72f);
        }
        if (player.Body.simulated && Lost(player.Body.position))
        {
            player.ReleaseGrip();
            Put(player.Body, Room.Lower + .72f);
        }
    }
}

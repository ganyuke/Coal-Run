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

    [SerializeField] private Tuning tuning;
    [SerializeField] private DayConfig day;
    [SerializeField] private InputReader input;
    [SerializeField] private PlayerController player;
    [SerializeField] private Cart cart;
    [SerializeField] private Lift lift;
    [SerializeField] private CoalField coal;
    [SerializeField] private Chute chute;

    public Tuning Tune => tuning;
    public DayConfig Day => day;
    public PlayerController Player => player;
    public Cart Cart => cart;
    public Lift Lift => lift;
    public CoalField Coal => coal;
    public Chute Chute => chute;

    public Phase Phase { get; } = Phase.Shift;
    public bool CanAct => Phase is Phase.Tutorial or Phase.Shift;
    public float Elapsed { get; private set; }
    public string Message { get; private set; } = "";
    public float MessageFor { get; private set; }
    public float InitialCoal { get; private set; }
    // Every unit of coal is always in exactly one place, so this should never change.
    public float TotalCoal => chute.Bunker + cart.Coal + player.Hands + coal.Loose;

    // name, loudness
    public event Action<string, float> OnSound;

    private readonly Dictionary<string, float> _soundTimers = new();

    private void Awake()
    {
        Instance = this;
        Time.fixedDeltaTime = tuning.step;
        Physics2D.gravity = new Vector2(0, tuning.gravity);
        GameLayers.ConfigureCollisions();
    }

    private void Start() => InitialCoal = TotalCoal;

    private void FixedUpdate()
    {
        var dt = Time.fixedDeltaTime;
        Elapsed += dt;
        MessageFor -= dt;
        if (input.Current.DebugKey != 0) DebugCommand(input.Current.DebugKey);
        RecoverLost();
    }

    // Testing shortcuts, by F key. Public so the editor CLI can call them too. Coal is moved to and from
    // the bunker, so the total stays intact.
    public void DebugCommand(int key)
    {
        switch (key)
        {
            case 2: // Wheels 1 → 2 → 0 → 1.
                cart.SetWheels((cart.Wheels + 1) % 3);
                Say($"Debug: {cart.Wheels} wheel{(cart.Wheels == 1 ? "" : "s")}.", 2);
                break;
            case 4: // Load empty → 6 → full → overfilled → empty.
                var load = cart.Coal < 5.9f ? 6 : cart.Coal < tuning.cartCapacity - .1f ? tuning.cartCapacity : cart.Coal < tuning.overfillCapacity - .1f ? tuning.overfillCapacity : 0;
                chute.Bunker += cart.Coal - load;
                cart.Coal = load;
                Say($"Debug: cart holds {load:0}.", 2);
                break;
            case 5: // Loose coal back to the bunker.
                Coal.ReturnAll();
                Say("Debug: loose coal cleaned up.", 2);
                break;
            case 6: // The cart, upright and still, beside the operator on their floor.
                var p = player.Position;
                float side = player.Facing, floor = Coal.SurfaceAt(p.x, p.y).Y;
                cart.Body.position = new Vector2(Mathf.Clamp(p.x + side * 1.6f, 1.45f, 30.55f), floor + .7f);
                cart.Body.rotation = 0;
                cart.Body.linearVelocity = Vector2.zero;
                cart.Body.angularVelocity = 0;
                cart.transform.SetPositionAndRotation(cart.Body.position, Quaternion.identity);
                Say("Debug: cart brought over.", 2);
                break;
        }
    }

    public void Say(string message, float duration = 5)
    {
        Message = message;
        MessageFor = duration;
    }

    // Repeats of the same sound inside the cooldown are dropped, so stations can call this every step.
    public void Sound(string sound, float cooldown = .12f, float amount = 1)
    {
        if (_soundTimers.TryGetValue(sound, out var until) && until > Elapsed) return;
        _soundTimers[sound] = Elapsed + cooldown;
        OnSound?.Invoke(sound, amount);
    }

    // Accidents kill only the operator. Placeholder until M4's replacement worker: the next one
    // walks in from the staff lift straight away.
    public void Die(string reason)
    {
        Say($"{reason}. Another worker is on the way.");
        Sound("hurt");
        player.Respawn(new Vector2(30.5f, Room.Lower + .75f));
    }

    // Nothing can leave the room. Anything physics squeezes through a wall or floor is put back on the lower floor.
    private void RecoverLost()
    {
        if (Lost(cart.Body.position))
        {
            player.ReleaseGrip();
            Put(cart.Body, Room.Lower + .72f);
        }
        if (!player.Body.simulated || !Lost(player.Body.position)) return;
        player.ReleaseGrip();
        Put(player.Body, Room.Lower + .72f);
        return;

        static bool Lost(Vector2 p) => p is not { x: > 0 and < Room.Width, y: > Room.Lower - 1.5f and < Room.Height + 4 };
        static void Put(Rigidbody2D b, float y)
        {
            var x = float.IsFinite(b.position.x) ? Mathf.Clamp(b.position.x, 5.5f, 28.5f) : 14;
            b.position = new Vector2(x, y);
            b.rotation = 0;
            b.linearVelocity = Vector2.zero;
            b.angularVelocity = 0;
        }
    }
}

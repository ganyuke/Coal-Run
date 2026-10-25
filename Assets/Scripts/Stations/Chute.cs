using UnityEngine;

// The coal chute and its analog lever in the control room. Pull the lever down with the mouse:
// the further down, the faster coal pours. Letting go springs it back shut. The bunker above holds
// the coal that hasn't been dispensed yet.
public class Chute : Machine
{
    [Tooltip("Moves down the lever's travel as it's pulled.")]
    [SerializeField] Transform knob;
    [SerializeField] float bunker = 1000;

    public float Bunker { get => bunker; set => bunker = value; }
    // 0 shut, 1 pulled all the way down.
    public float Pull { get; private set; }
    public override string OutOfReach => ShiftManager.Instance.Player.Lane == 1 ? "Walk up to the lever." : "The chute lever is inside the control room.";

    ShiftManager game;
    float accumulator;
    Vector3 knobTop;

    void Start()
    {
        game = ShiftManager.Instance;
        if (knob) knobTop = knob.localPosition;
    }

    float Travel => game.Tune.leverTravel;
    float Top => HandlePosition.y + Travel / 2;

    public override bool Hit(Vector2 mouse)
    {
        var m = HandlePosition;
        return Mathf.Abs(mouse.x - m.x) < .7f && Mathf.Abs(mouse.y - m.y) < Travel / 2 + .35f;
    }

    public override bool CanOperate(PlayerController player)
    {
        Vector2 p = player.Position;
        return game.CanAct && player.Lane == 1 && Mathf.Abs(p.x - HandlePosition.x) < game.Tune.machineReach.x && p.y < 4.2f;
    }

    public override void Drag(Vector2 mouse, float dt) => Pull = Mathf.Clamp01((Top - mouse.y) / Travel);

    protected override void OnEnd() => Pull = 0;

    void FixedUpdate()
    {
        if (knob) knob.localPosition = knobTop + Vector3.down * Pull * Travel;
        if (Pull <= 0) return;
        accumulator += Pull * game.Tune.chuteRate * Time.fixedDeltaTime;
        while (accumulator >= .5f)
        {
            Dispense(.5f);
            accumulator -= .5f;
        }
        game.Sound("coal", .13f);
    }

    // Drops up to `amount` from the bunker out of the chute mouth. Returns what actually fell.
    public float Dispense(float amount)
    {
        float q = Mathf.Min(bunker, amount);
        bunker -= q;
        game.Coal.Emit(Room.Chute, 6.7f, q);
        return q;
    }
}

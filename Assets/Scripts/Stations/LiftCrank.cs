using UnityEngine;

// A lift wheel, worked like an osu! spinner. Spin is read from how the cursor's path turns, so
// circles count wherever and however big they are drawn, and the wheel can ride away on the
// platform without breaking the gesture. Clockwise is up.
public class LiftCrank : Machine
{
    public enum Spot { Platform, LowerLanding, UpperLanding }

    [SerializeField] Spot spot;
    [SerializeField] Lift lift;

    // Smoothed spin in rad/s, positive clockwise.
    public float Rate { get; private set; }

    public override string OutOfReach => "Walk up to the wheel.";

    float pending;
    Vector2 last;
    float? heading;

    public override bool CanOperate(PlayerController player)
    {
        if (!ShiftManager.Instance.CanAct || player.Lane != 0) return false;
        Vector2 p = player.Position, m = HandlePosition, reach = ShiftManager.Instance.Tune.machineReach;
        bool onPlatform = p.x > .5f && p.x < 4 && Mathf.Abs(p.y - lift.Y - .72f) < .6f;
        if (spot == Spot.Platform && ShiftManager.Instance.Tune.platformWheelAnywhere && onPlatform) return true;
        return Mathf.Abs(p.x - m.x) <= reach.x && Mathf.Abs(p.y - m.y) <= reach.y;
    }

    protected override void OnBegin(Vector2 mouse)
    {
        last = mouse;
        heading = null;
        pending = 0;
        Rate = 0;
    }

    public override void Drag(Vector2 mouse, float dt)
    {
        var d = mouse - last;
        if (d.magnitude >= .12f)
        {
            float h = Mathf.Atan2(d.y, d.x);
            if (heading.HasValue)
            {
                float turn = Mathf.DeltaAngle(heading.Value * Mathf.Rad2Deg, h * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                if (Mathf.Abs(turn) < 1.6f) pending -= turn; // A sharper turn is a reversal or jitter, not part of a circle.
            }
            heading = h;
            last = mouse;
        }
        // Mouse movement arrives unevenly, so the rate is smoothed over about a tenth of a second.
        Rate += (pending / Mathf.Max(dt, 1 / 240f) - Rate) * Mathf.Min(1, dt / .1f);
        pending = 0;
    }

    protected override void OnEnd() => Rate = 0;
}

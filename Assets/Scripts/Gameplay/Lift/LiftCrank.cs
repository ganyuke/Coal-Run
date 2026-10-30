using CoalRun.Core;
using CoalRun.Gameplay.Machinery;
using CoalRun.Gameplay.Player;
using UnityEngine;

namespace CoalRun.Gameplay.Lift
{
    // A lift wheel, worked like an osu! spinner. Spin is read from how the cursor's path turns, so
    // circles count wherever and however big they are drawn, and the wheel can ride away on the
    // platform without breaking the gesture. Clockwise is up.
    [UnityEngine.Scripting.APIUpdating.MovedFrom("CoalRun.Stations")]
    public class LiftCrank : Machine
    {
        [Tooltip("Turned by however far the lift moves while this crank drives it.")] [SerializeField]
        private Transform wheel;

        private float? _heading;
        private Vector2 _last;

        private float _pending;

        // Smoothed spin in rad/s, positive clockwise.
        public float Rate { get; private set; }

        public override string OutOfReach => ShiftManager.Instance.Lines.speech.crankFar;

        public override bool CanOperate(PlayerController player)
        {
            if (!ShiftManager.Instance.CanAct || player.Lane != 0) return false;
            Vector2 p = player.Position, m = HandlePosition, reach = ShiftManager.Instance.Tune.machineReach;
            return Mathf.Abs(p.x - m.x) <= reach.x && Mathf.Abs(p.y - m.y) <= reach.y;
        }

        protected override void OnBegin(Vector2 mouse)
        {
            _last = mouse;
            _heading = null;
            _pending = 0;
            Rate = 0;
        }

        public override void Drag(Vector2 mouse, float dt)
        {
            var d = mouse - _last;
            if (d.magnitude >= .12f)
            {
                var h = Mathf.Atan2(d.y, d.x);
                if (_heading.HasValue)
                {
                    var turn = Mathf.DeltaAngle(_heading.Value * Mathf.Rad2Deg, h * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                    if (Mathf.Abs(turn) < 1.6f)
                        _pending -= turn; // A sharper turn is a reversal or jitter, not part of a circle.
                }

                _heading = h;
                _last = mouse;
            }

            // Mouse movement arrives unevenly, so the rate is smoothed over about a tenth of a second.
            Rate += (_pending / Mathf.Max(dt, 1 / 240f) - Rate) * Mathf.Min(1, dt / .1f);
            _pending = 0;
        }

        protected override void OnEnd()
        {
            Rate = 0;
        }

        public void Turn(float degrees)
        {
            if (wheel) wheel.Rotate(0, 0, degrees);
        }
    }
}
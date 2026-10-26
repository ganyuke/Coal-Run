using CoalRun.Core;
using UnityEngine;

namespace CoalRun.Stations
{
    // The shovel leaning on the furnace in the training: click it to take it, like E. It only exists as a
    // machine while it's lying there (inactive once taken), so MachineInput never sees it otherwise.
    public class ShovelPickup : Machine
    {
        public override string OutOfReach => "Gotta get closer to the shovel.";
        public override bool CanOperate(PlayerController player) => ShiftManager.Instance.Tutorial.ShovelInReach(player);
        protected override void OnBegin(Vector2 mouse) => ShiftManager.Instance.Tutorial.TakeShovel();
    }
}

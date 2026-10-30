using CoalRun.Core;
using CoalRun.Gameplay.Machinery;
using CoalRun.Gameplay.Player;
using UnityEngine;

namespace CoalRun.Gameplay.Tutorial
{
    // The shovel leaning on the furnace in the training: click it to take it, like E. It only exists as a
    // machine while it's lying there (inactive once taken), so MachineInput never sees it otherwise.
    [UnityEngine.Scripting.APIUpdating.MovedFrom("CoalRun.Stations")]
    public class ShovelPickup : Machine
    {
        public override string OutOfReach => ShiftManager.Instance.Lines.speech.shovelFar;

        public override bool CanOperate(PlayerController player)
        {
            return ShiftManager.Instance.Tutorial.ShovelInReach(player);
        }

        protected override void OnBegin(Vector2 mouse)
        {
            ShiftManager.Instance.Tutorial.TakeShovel();
        }
    }
}

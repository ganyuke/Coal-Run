using CoalRun.Core;
using UnityEngine.InputSystem;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CoalRun.Stations
{
    // The mouse side of the machinery: on press, takes the machine under the cursor if the operator
    // can reach it; while held, feeds it the cursor; on release or when it goes out of reach, lets go.
    public class MachineInput : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private Camera cam;
        [Header("Glow")]
        [Tooltip("The faint light each machine's handle carries, copied onto it. Kept inactive here.")]
        [SerializeField]
        private Light2D glowLight;
        [Tooltip("Its intensity across the room, and with the operator in reach.")]
        [SerializeField]
        private Vector2 glowLevel = new(.7f, 1.8f);

        public Machine Active { get; private set; }
        public Machine Hover { get; private set; }

        private void Update()
        {
            var mouse = Mouse.current;
            var game = ShiftManager.Instance;
            if (mouse == null) return;
            Vector2 p = cam.ScreenToWorldPoint(mouse.position.ReadValue());
            // Hands full of wheel: a click throws it instead (see PlayerController).
            Hover = game.CanAct && !player.CarryingWheel && !UI.NotebookView.PointerOver ? Pick(p, game.Tutorial) : null;
            if (Active && (!mouse.leftButton.isPressed || !game.CanAct || !Active.CanOperate(player))) Release();
            if (!Active && mouse.leftButton.wasPressedThisFrame && Hover)
            {
                if (Hover.CanOperate(player))
                {
                    player.ReleaseGrip();
                    player.ThrowHands();
                    Active = Hover;
                    Active.Begin(p);
                }
                else game.Say(Hover.OutOfReach, 3);
            }
            if (Active) Active.Drag(p, Time.deltaTime);
            foreach (var m in Machine.All)
                m.Glow(game.Tutorial.Allows(m) ? m.CanOperate(player) && game.CanAct ? glowLevel.y : glowLevel.x : 0, glowLight, Time.deltaTime);
        }

        public void Release()
        {
            if (Active) Active.End();
            Active = null;
        }

        private static Machine Pick(Vector2 p, Tutorial tutorial)
        {
            Machine best = null;
            var bestDistance = float.PositiveInfinity;
            foreach (var m in Machine.All)
            {
                if (!tutorial.Allows(m)) continue;
                var d = Vector2.Distance(p, m.HandlePosition);
                if (!m.Hit(p) || d >= bestDistance) continue;
                best = m;
                bestDistance = d;
            }
            return best;
        }
    }
}

using CoalRun.Core;
using UnityEngine.InputSystem;
using UnityEngine;

namespace CoalRun.Stations
{
    // The mouse side of the machinery: on press, takes the machine under the cursor if the operator
    // can reach it; while held, feeds it the cursor; on release or when it goes out of reach, lets go.
    public class MachineInput : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private Camera cam;
        [Tooltip("A handle's light times this with the operator right by it, where the hat lamp already lights it and the two would bloom.")]
        [SerializeField]
        private float glowNear = .4f;
        [Tooltip("Closer than this (metres), a handle's light is at its dimmest.")]
        [SerializeField]
        private float dimWithin = 1;
        [Tooltip("Further than this (metres), a handle's light is at full. Set it to the same as Dim Within for a sharp switch.")]
        [SerializeField]
        private float fullBeyond = 3;

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
                m.Glow(game.Tutorial.Allows(m) ? Mathf.Lerp(glowNear, 1, Fade(Vector2.Distance(player.Position, m.HandlePosition))) : 0, Time.deltaTime);
        }

        // 0 within dimWithin, 1 beyond fullBeyond, a ramp between. (InverseLerp gives 0 everywhere if they're equal.)
        private float Fade(float distance) =>
            fullBeyond > dimWithin ? Mathf.InverseLerp(dimWithin, fullBeyond, distance) : distance > dimWithin ? 1 : 0;

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

using CoalRun.Core;
using CoalRun.Stations;
using UnityEngine.InputSystem;
using UnityEngine;

namespace CoalRun.UI
{
    // Placeholder HUD drawn with IMGUI: dialogue, key prompts, and an F3 readout. Gets replaced by
    // the real UI (notepad, objective, meters) later; delete this component when that exists.
    public class DebugHud : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private MachineInput machines;

        private bool _readout = true;
        private GUIStyle _box, _small;

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame) _readout = !_readout;
        }

        private void OnGUI()
        {
            var game = ShiftManager.Instance;
            if (!game) return;
            _box ??= new GUIStyle(GUI.skin.box) { fontSize = 20, wordWrap = true, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(16, 16, 10, 10) };
            _small ??= new GUIStyle(GUI.skin.label) { fontSize = 14 };
            // Lay out in a 1280x720 space, scaled to the window.
            var scale = Screen.height / 720f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            var width = Screen.width / scale;

            if (game.MessageFor > 0) GUI.Box(new Rect(width / 2 - 360, 600, 720, 80), game.Message, _box);

            float y = 16;
            foreach (var (key, label) in player.Prompts())
            {
                GUI.Box(new Rect(16, y, 240, 34), $"[{key}]  {label}", new GUIStyle(_box) { fontSize = 16, alignment = TextAnchor.MiddleLeft });
                y += 40;
            }

            if (!_readout) return;
            var cart = game.Cart;
            var text =
                "F3 hides this · F2 wheels 1/2/0 · F4 cart load 0/6/10/12 · F5 clean up coal · F6 cart to me\n" +
                $"Operator {player.Position.x:0.0}, {player.Position.y:0.0}  lane {player.Lane}  holding {player.Holding}\n" +
                $"Cart {cart.Body.position.x:0.0}, {cart.Body.position.y:0.0}  speed {cart.Body.linearVelocity.magnitude:0.0}  wheels {cart.Wheels}  coal {cart.Coal:0.0}\n" +
                $"Lift {game.Lift.Y:0.00}  speed {game.Lift.Speed:0.00}\n" +
                $"Bunker {game.Chute.Bunker:0.0}  chute {game.Chute.Pull:0.00}  hands {player.Hands:0.0}  lumps {game.Coal.ActiveLumps}\n" +
                $"Hopper {game.Furnace.Hopper:0.0}  delivered {game.Furnace.Delivered:0.0}  in cradle {cart.InCradle}  tilt {cart.Body.rotation:0}°\n" +
                $"Cart burial {cart.Burial:0.00}  operator burial {player.Burial:0.0}s  injury {player.Injury:0.0}\n" +
                $"Coal error {game.TotalCoal - game.InitialCoal:0.0000}\n" +
                $"Machine {(machines.Active ? machines.Active.name : "-")}  hover {(machines.Hover ? machines.Hover.name : "-")}\n" +
                $"{1f / Mathf.Max(Time.smoothDeltaTime, 1e-4f):0} fps";
            GUI.Label(new Rect(width - 620, 12, 610, 200), text, _small);
        }
    }
}

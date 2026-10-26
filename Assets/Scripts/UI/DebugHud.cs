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
        // The report card's solid backing; the default box skin shows the room through it.
        private Texture2D _vignette;
        private float _frost;

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

            DrawFrost(game, width);
            // The briefing, report and fade are the notebook's (NotebookView).
            if (game.Ended || game.Departing || game.Phase == Phase.Briefing) return;
            if (game.MessageFor > 0) GUI.Box(new Rect(width / 2 - 360, 600, 720, 80), game.Message, _box);
            // The objective and the key prompts are the notebook's (NotebookView).
            if (game.Phase == Phase.Replacement && game.Deaths > 0)
                GUI.Box(new Rect(width / 2 - 380, 24, 760, 44), $"Replacement arriving in {Mathf.CeilToInt(game.ReplacementIn)} s", new GUIStyle(_box) { fontSize = 17 });

            if (!_readout) return;
            var cart = game.Cart;
            var text =
                "F3 hides this · F2 knock a wheel off · F4 cart load 0/6/10/12 · F5 clean up coal · F6 cart to me · F7 replay the training · F8 skip to the shift · F9 bot plays\n" +
                $"Phase {game.Phase}  training {game.Tutorial.Current}\n" +
                $"Operator {player.Position.x:0.0}, {player.Position.y:0.0}  lane {player.Lane}  holding {player.Holding}\n" +
                $"Cart {cart.Body.position.x:0.0}, {cart.Body.position.y:0.0}  speed {cart.Body.linearVelocity.magnitude:0.0}  wheels {cart.Wheels}  coal {cart.Coal:0.0}\n" +
                $"Lift {game.Lift.Y:0.00}  speed {game.Lift.Speed:0.00}\n" +
                $"Bunker {game.Chute.Bunker:0.0}  chute {game.Chute.Pull:0.00}  hands {player.Hands:0.0}  lumps {game.Coal.ActiveLumps}\n" +
                $"Hopper {game.Furnace.Hopper:0.0}  delivered {game.Furnace.Delivered:0.0}  in cradle {cart.InCradle}  tilt {cart.Body.rotation:0}°\n" +
                $"Clock {(int)game.Clock / 60}:{(int)game.Clock % 60:00}  outside {game.Outside:0.0}°  inside {game.Temperature:0.0}° (chill {game.Chill:0.00})  fire {(game.Furnace.Lit ? "lit" : "out")}, notch {game.Furnace.Notch} ({game.Furnace.BurnRate:0.000}/s, holds {game.Tune.heldTemperatures[game.Furnace.Notch]:0}°)  output {game.Furnace.Output:0.0} kWh\n" +
                $"Cart burial {cart.Burial:0.00}  operator burial {player.Burial:0.0}s  injury {player.Injury:0.0}\n" +
                $"Coal error {game.TotalCoal - game.InitialCoal:0.0000}\n" +
                $"Machine {(machines.Active ? machines.Active.name : "-")}  hover {(machines.Hover ? machines.Hover.name : "-")}\n" +
                $"{1f / Mathf.Max(Time.smoothDeltaTime, 1e-4f):0} fps";
            GUI.Label(new Rect(width - 620, 12, 610, 200), text, _small);
        }

        // A light blue vignette closing in around the screen while the building is freezing, harder as the
        // freeze grace runs out. Eased in and out, so it creeps in rather than snapping on.
        private void DrawFrost(ShiftManager game, float width)
        {
            var target = game.Freezing && !game.Ended ? Mathf.Lerp(.55f, 1, game.Chill) : 0;
            _frost = Mathf.MoveTowards(_frost, target, Time.deltaTime * .5f);
            if (_frost <= .01f) return;
            if (!_vignette)
            {
                // Clear in the middle, frosting over toward the edges and corners.
                const int size = 128;
                _vignette = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (var y = 0; y < size; y++)
                    for (var x = 0; x < size; x++)
                    {
                        var d = new Vector2(x - size / 2f + .5f, y - size / 2f + .5f).magnitude / (size / 2f);
                        _vignette.SetPixel(x, y, new Color(.65f, .88f, 1, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.55f, 1.25f, d))));
                    }
                _vignette.Apply();
            }

            var color = GUI.color;
            GUI.color = new Color(1, 1, 1, _frost * .7f);
            GUI.DrawTexture(new Rect(0, 0, width, 720), _vignette);
            GUI.color = color;
        }
    }
}

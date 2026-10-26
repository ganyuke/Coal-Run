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
        private Texture2D _cardBack, _black;

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

            DrawFade(game, width);
            if (game.Ended) { Report(game, width); return; }
            if (game.Departing) return;
            if (game.Phase == Phase.Briefing) { Briefing(game, width); return; }
            if (game.MessageFor > 0) GUI.Box(new Rect(width / 2 - 360, 600, 720, 80), game.Message, _box);
            var objective = game.Phase == Phase.Replacement && game.Deaths > 0 ? $"Replacement arriving in {Mathf.CeilToInt(game.ReplacementIn)} s" : game.Tutorial.Objective;
            if (objective != "") GUI.Box(new Rect(width / 2 - 380, 24, 760, 44), objective, new GUIStyle(_box) { fontSize = 17 });

            float y = 16;
            foreach (var (key, label) in player.Prompts())
            {
                GUI.Box(new Rect(16, y, 240, 34), $"[{key}]  {label}", new GUIStyle(_box) { fontSize = 16, alignment = TextAnchor.MiddleLeft });
                y += 40;
            }

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

        // Black over everything while the operator rides out of the staff lift.
        private void DrawFade(ShiftManager game, float width)
        {
            if (game.Fade <= 0) return;
            _black ??= Texture2D.blackTexture;
            var color = GUI.color;
            GUI.color = new Color(1, 1, 1, game.Fade);
            GUI.DrawTexture(new Rect(0, 0, width, 720), _black);
            GUI.color = color;
        }

        // Between the training and the shift. The real one is the M5 notepad.
        private void Briefing(ShiftManager game, float width)
        {
            var text =
                $"DAY {game.Day.day:00} · BRIEFING\n\n<b>Keep the building warm until the blizzard passes.</b>\n" +
                "Haul coal from the chute to the furnace. Feed it, light it, and set the burn lever: the colder it gets outside, the harder it has to burn." +
                $"\n\nElectrical output target  {game.Day.target:0} kWh\nThe cart has {game.Cart.Wheels} wheel{(game.Cart.Wheels == 1 ? "" : "s")}.";
            var card = Card(width, text, 380);
            if (GUI.Button(new Rect(width / 2 - 140, card.yMax - 64, 280, 44), "[Enter]  Begin the shift", new GUIStyle(GUI.skin.button) { fontSize = 18 }))
                game.BeginShift();
        }

        // The end-of-shift card, worded as in the JS. The real one comes with the M5 UI.
        private void Report(ShiftManager game, float width)
        {
            var complete = game.Phase == Phase.Complete;
            var text =
                $"DAY 01 · SHIFT REPORT\n\n<b>{(complete ? "You kept them alive." : "No one is coming down.")}</b>\n" +
                (complete ? "The shift is over. The furnace is still your problem tomorrow." : "The offices froze first. There is nobody left to send a replacement.") +
                $"\n\nElectrical output  {game.Furnace.Output:0.0} / {game.Day.target:0} kWh\nCoal delivered  {game.Furnace.Delivered:0.0}\nOperators lost  {game.Deaths}";
            var card = Card(width, text);
            if (GUI.Button(new Rect(width / 2 - 140, card.yMax - 64, 280, 44), "[R]  Run the shift again", new GUIStyle(GUI.skin.button) { fontSize = 18 }))
                game.Restart();
        }

        private Rect Card(float width, string text, float height = 330)
        {
            var card = new Rect(width / 2 - 300, 160, 600, height);
            if (!_cardBack)
            {
                _cardBack = new Texture2D(1, 1);
                _cardBack.SetPixel(0, 0, new Color(.08f, .08f, .09f, .96f));
                _cardBack.Apply();
            }

            var style = new GUIStyle(_box) { richText = true, alignment = TextAnchor.UpperCenter, padding = new RectOffset(24, 24, 28, 16) };
            style.normal.background = _cardBack;
            GUI.Box(card, text, style);
            return card;
        }
    }
}

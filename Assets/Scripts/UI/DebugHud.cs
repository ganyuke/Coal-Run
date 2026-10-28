using CoalRun.Core;
using CoalRun.Stations;
using TMPro;
using UnityEngine.InputSystem;
using UnityEngine;

namespace CoalRun.UI
{
    // The F3 developer readout: a uGUI text box in the top-left corner with the F-key shortcuts and the numbers behind
    // the game (positions, coal totals, heat). Not part of the game: F3 hides it, and a build starts with it hidden.
    public class DebugHud : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private MachineInput machines;
        [SerializeField] private TMP_Text readout;
        [Tooltip("Seconds between redraws of the text, so building it doesn't cost a string every frame.")]
        [SerializeField]
        private float refresh = .2f;

        private bool _visible;
        private float _next;

        private void Start()
        {
            _visible = Application.isEditor;
            readout.gameObject.SetActive(_visible);
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame)
            {
                _visible = !_visible;
                readout.gameObject.SetActive(_visible);
            }
            if (!_visible || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + refresh;
            var game = ShiftManager.Instance;
            if (!game) return;
            var cart = game.Cart;
            readout.text =
                "F3 hides this · F2 knock a wheel off · F4 cart load 0/6/10/12 · F5 clean up coal · F6 cart to me · F7 replay the training · F8 skip to the shift · F9 bot plays · F10 last 30 s of the shift\n" +
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
        }
    }
}

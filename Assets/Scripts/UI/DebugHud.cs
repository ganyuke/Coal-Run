using UnityEngine;
using UnityEngine.InputSystem;

// Placeholder HUD drawn with IMGUI: dialogue, key prompts, and an F3 readout. Gets replaced by
// the real UI (notepad, objective, meters) later; delete this component when that exists.
public class DebugHud : MonoBehaviour
{
    [SerializeField] PlayerController player;
    [SerializeField] MachineInput machines;

    bool readout = true;
    GUIStyle box, small;

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame) readout = !readout;
    }

    void OnGUI()
    {
        var game = ShiftManager.Instance;
        if (!game) return;
        box ??= new GUIStyle(GUI.skin.box) { fontSize = 20, wordWrap = true, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(16, 16, 10, 10) };
        small ??= new GUIStyle(GUI.skin.label) { fontSize = 14 };
        // Lay out in a 1280x720 space, scaled to the window.
        float scale = Screen.height / 720f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
        float width = Screen.width / scale;

        if (game.MessageFor > 0) GUI.Box(new Rect(width / 2 - 360, 600, 720, 80), game.Message, box);

        float y = 16;
        foreach (var (key, label) in player.Prompts())
        {
            GUI.Box(new Rect(16, y, 240, 34), $"[{key}]  {label}", new GUIStyle(box) { fontSize = 16, alignment = TextAnchor.MiddleLeft });
            y += 40;
        }

        if (!readout) return;
        var cart = game.Cart;
        string text =
            $"F3 hides this · F2 toggles the second wheel\n" +
            $"Operator {player.Position.x:0.0}, {player.Position.y:0.0}  lane {player.Lane}  holding {player.Holding}\n" +
            $"Cart {cart.Body.position.x:0.0}, {cart.Body.position.y:0.0}  speed {cart.Body.linearVelocity.magnitude:0.0}  wheels {cart.Wheels}  coal {cart.Coal:0.0}\n" +
            $"Lift {game.Lift.Y:0.00}  speed {game.Lift.Speed:0.00}\n" +
            $"Machine {(machines.Active ? machines.Active.name : "-")}  hover {(machines.Hover ? machines.Hover.name : "-")}\n" +
            $"{1f / Mathf.Max(Time.smoothDeltaTime, 1e-4f):0} fps";
        GUI.Label(new Rect(width - 520, 12, 510, 140), text, small);
    }
}

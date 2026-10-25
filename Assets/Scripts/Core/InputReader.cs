using UnityEngine;
using UnityEngine.InputSystem;

// One physics step's worth of keyboard input. Same shape as the prototype's Controls.
public struct Controls
{
    public float move;      // -1 left, 1 right
    public bool grip;       // Space held
    public bool shovel;     // F held
    public bool interact;   // E pressed this step
    public int crankKey;    // W/S held: 1 up, -1 down
    public int lane;        // W/S pressed this step: 1 into the control room, -1 out
    public bool debugWheel; // F2 pressed this step
}

// Every key binding lives here. Presses are caught in Update and handed out once in the next
// FixedUpdate, so a tap is never lost or counted twice however frame rate and physics rate line up.
[DefaultExecutionOrder(-100)]
public class InputReader : MonoBehaviour
{
    public Controls Current { get; private set; }

    bool interactPressed, wheelPressed;
    int lanePressed;

    void Update()
    {
        var k = Keyboard.current;
        if (k == null) return;
        if (k.eKey.wasPressedThisFrame) interactPressed = true;
        if (k.f2Key.wasPressedThisFrame) wheelPressed = true;
        if (k.wKey.wasPressedThisFrame || k.upArrowKey.wasPressedThisFrame) lanePressed = 1;
        if (k.sKey.wasPressedThisFrame || k.downArrowKey.wasPressedThisFrame) lanePressed = -1;
    }

    void FixedUpdate()
    {
        var k = Keyboard.current;
        if (k == null) { Current = default; return; }
        bool left = k.aKey.isPressed || k.leftArrowKey.isPressed, right = k.dKey.isPressed || k.rightArrowKey.isPressed;
        bool up = k.wKey.isPressed || k.upArrowKey.isPressed, down = k.sKey.isPressed || k.downArrowKey.isPressed;
        Current = new Controls
        {
            move = (right ? 1 : 0) - (left ? 1 : 0),
            grip = k.spaceKey.isPressed,
            shovel = k.fKey.isPressed,
            interact = interactPressed,
            crankKey = (up ? 1 : 0) - (down ? 1 : 0),
            lane = lanePressed,
            debugWheel = wheelPressed,
        };
        interactPressed = wheelPressed = false;
        lanePressed = 0;
    }
}

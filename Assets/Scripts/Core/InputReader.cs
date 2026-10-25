using UnityEngine.InputSystem;
using UnityEngine;

namespace CoalRun.Core
{
    // One physics step's worth of keyboard input. Same shape as the prototype's Controls.
    public struct Controls
    {
        public float Move;      // -1 left, 1 right
        public bool Grip;       // Space held
        public bool Shovel;     // F held
        public bool Interact;   // E pressed this step
        public bool InteractHeld; // E held
        public int CrankKey;    // W/S held: 1 up, -1 down
        public int Lane;        // W/S pressed this step: 1 into the control room, -1 out
        public bool Heave;      // Shift held
        public int Dash;        // A/D double-tapped this step: 1 right, -1 left
        public int Tap;         // A/D pressed this step, double or not
        public bool Restart;    // R pressed this step
        public bool Click;      // Left mouse button pressed this step
        public int DebugKey;    // F2, F4, F5 or F6 pressed this step (2, 4, 5, 6), else 0. See ShiftManager.DebugCommand.
        public Vector2 Aim;     // The cursor, in world space
    }

    // Every key binding lives here. Presses are caught in Update and handed out once in the next
    // FixedUpdate, so a tap is never lost or counted twice however frame rate and physics rate line up.
    [DefaultExecutionOrder(-100)]
    public class InputReader : MonoBehaviour
    {
        [SerializeField] private Camera cam;
        [Tooltip("Longest gap between the two taps of a double tap, in seconds.")]
        [SerializeField]
        private float doubleTap = .25f;

        public Controls Current { get; private set; }

        private bool _interactPressed, _restartPressed, _clickPressed;
        private int _debugPressed;
        private int _lanePressed, _dashPressed, _tapPressed;
        private bool _wasLeft, _wasRight;
        private float _lastLeft = -1, _lastRight = -1;

        private void Update()
        {
            var k = Keyboard.current;
            if (k == null) return;
            // Taps are found from isPressed rather than wasPressedThisFrame, which never fires while the
            // editor window is unfocused (see CLAUDE.md), so the dash can be tested from the CLI.
            bool left = k.aKey.isPressed || k.leftArrowKey.isPressed, right = k.dKey.isPressed || k.rightArrowKey.isPressed;
            if (left && !_wasLeft) { _tapPressed = -1; _dashPressed = Tap(ref _lastLeft) ? -1 : _dashPressed; }
            if (right && !_wasRight) { _tapPressed = 1; _dashPressed = Tap(ref _lastRight) ? 1 : _dashPressed; }
            _wasLeft = left;
            _wasRight = right;
            if (k.eKey.wasPressedThisFrame) _interactPressed = true;
            if (k.rKey.wasPressedThisFrame) _restartPressed = true;
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) _clickPressed = true;
            if (k.f2Key.wasPressedThisFrame) _debugPressed = 2;
            if (k.f4Key.wasPressedThisFrame) _debugPressed = 4;
            if (k.f5Key.wasPressedThisFrame) _debugPressed = 5;
            if (k.f6Key.wasPressedThisFrame) _debugPressed = 6;
            if (k.wKey.wasPressedThisFrame || k.upArrowKey.wasPressedThisFrame) _lanePressed = 1;
            if (k.sKey.wasPressedThisFrame || k.downArrowKey.wasPressedThisFrame) _lanePressed = -1;
        }

        private void FixedUpdate()
        {
            var k = Keyboard.current;
            if (k == null) { Current = default; return; }
            bool left = k.aKey.isPressed || k.leftArrowKey.isPressed, right = k.dKey.isPressed || k.rightArrowKey.isPressed;
            bool up = k.wKey.isPressed || k.upArrowKey.isPressed, down = k.sKey.isPressed || k.downArrowKey.isPressed;
            Current = new Controls
            {
                Move = (right ? 1 : 0) - (left ? 1 : 0),
                Grip = k.spaceKey.isPressed,
                Shovel = k.fKey.isPressed,
                Interact = _interactPressed,
                InteractHeld = k.eKey.isPressed,
                CrankKey = (up ? 1 : 0) - (down ? 1 : 0),
                Lane = _lanePressed,
                Heave = k.leftShiftKey.isPressed || k.rightShiftKey.isPressed,
                Dash = _dashPressed,
                Tap = _tapPressed,
                Restart = _restartPressed,
                Click = _clickPressed,
                DebugKey = _debugPressed,
                Aim = Aim()
            };
            _interactPressed = _restartPressed = _clickPressed = false;
            _debugPressed = 0;
            _lanePressed = _dashPressed = _tapPressed = 0;
        }

        // True on the second tap of a double tap.
        private bool Tap(ref float last)
        {
            var twice = Time.unscaledTime - last < doubleTap;
            last = twice ? -1 : Time.unscaledTime;
            return twice;
        }

        private Vector2 Aim()
        {
            var mouse = Mouse.current;
            var c = cam ? cam : Camera.main;
            return mouse != null && c ? c.ScreenToWorldPoint(mouse.position.ReadValue()) : Current.Aim;
        }
    }
}

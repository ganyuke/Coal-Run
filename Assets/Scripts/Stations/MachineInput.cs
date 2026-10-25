using UnityEngine;
using UnityEngine.InputSystem;

// The mouse side of the machinery: on press, takes the machine under the cursor if the operator
// can reach it; while held, feeds it the cursor; on release or when it goes out of reach, lets go.
public class MachineInput : MonoBehaviour
{
    [SerializeField] PlayerController player;
    [SerializeField] Camera cam;

    public Machine Active { get; private set; }
    public Machine Hover { get; private set; }

    void Update()
    {
        var mouse = Mouse.current;
        var game = ShiftManager.Instance;
        if (mouse == null) return;
        Vector2 p = cam.ScreenToWorldPoint(mouse.position.ReadValue());
        Hover = game.CanAct ? Pick(p) : null;
        if (Active && (!mouse.leftButton.isPressed || !game.CanAct || !Active.CanOperate(player))) Release();
        if (!Active && mouse.leftButton.wasPressedThisFrame && Hover)
        {
            if (Hover.CanOperate(player))
            {
                player.ReleaseGrip();
                Active = Hover;
                Active.Begin(p);
            }
            else game.Say(Hover.OutOfReach, 3);
        }
        if (Active) Active.Drag(p, Time.deltaTime);
    }

    public void Release()
    {
        if (Active) Active.End();
        Active = null;
    }

    static Machine Pick(Vector2 p)
    {
        Machine best = null;
        float bestDistance = float.PositiveInfinity;
        foreach (var m in Machine.All)
        {
            float d = Vector2.Distance(p, m.HandlePosition);
            if (m.Hit(p) && d < bestDistance) { best = m; bestDistance = d; }
        }
        return best;
    }
}

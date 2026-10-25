using System.Collections.Generic;
using UnityEngine;

// Anything worked with the mouse: hold the button on its handle and drag. MachineInput picks
// which one the cursor is on; subclasses turn the drag into lever travel, spin, or tilt.
public abstract class Machine : MonoBehaviour
{
    // Every enabled machine, so MachineInput doesn't have to search the scene.
    public static readonly List<Machine> All = new();

    [SerializeField] protected Transform handle;
    [SerializeField] float grabRadius = 1;

    public bool InUse { get; private set; }
    public Vector2 HandlePosition => handle ? handle.position : transform.position;
    public virtual string OutOfReach => "Walk up to it.";

    void OnEnable() => All.Add(this);
    void OnDisable() { All.Remove(this); End(); }

    public virtual bool Hit(Vector2 mouse) => Vector2.Distance(mouse, HandlePosition) < grabRadius;
    public abstract bool CanOperate(PlayerController player);

    public void Begin(Vector2 mouse) { InUse = true; OnBegin(mouse); }
    public void End() { if (!InUse) return; InUse = false; OnEnd(); }
    // Called every frame while held, with the cursor in world space.
    public virtual void Drag(Vector2 mouse, float dt) { }
    protected virtual void OnBegin(Vector2 mouse) { }
    protected virtual void OnEnd() { }
}

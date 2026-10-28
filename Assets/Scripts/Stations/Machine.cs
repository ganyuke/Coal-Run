using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CoalRun.Stations
{
    // Anything worked with the mouse: hold the button on its handle and drag. MachineInput picks
    // which one the cursor is on; subclasses turn the drag into lever travel, spin, or tilt.
    public abstract class Machine : MonoBehaviour
    {
        // Every enabled machine, so MachineInput doesn't have to search the scene.
        public static readonly List<Machine> All = new();

        [SerializeField] protected Transform handle;
        [SerializeField] private float grabRadius = 1;
        [Tooltip("A faint light over the handle, so it shows in the dark. Its intensity in the scene is how bright it is across the room; in reach it's brighter.")]
        [SerializeField]
        private Light2D glow;

        private float _glowRest = -1;

        public bool InUse { get; private set; }
        public virtual Vector2 HandlePosition => handle ? handle.position : transform.position;
        public virtual string OutOfReach => "Gotta get closer.";
        // While it's held, the operator can walk between these world x positions and no further. By
        // default, a little either way of where they took hold.
        public virtual Vector2 WalkRange(float grabbedAt) => new(grabbedAt - .6f, grabbedAt + .6f);

        private void OnEnable() => All.Add(this);
        private void OnDisable() { All.Remove(this); End(); }

        public virtual bool Hit(Vector2 mouse) => Vector2.Distance(mouse, HandlePosition) < grabRadius;
        public abstract bool CanOperate(PlayerController player);

        public void Begin(Vector2 mouse) { InUse = true; OnBegin(mouse); }
        public void End() { if (!InUse) return; InUse = false; OnEnd(); }
        // Called every frame while held, with the cursor in world space.
        public virtual void Drag(Vector2 mouse, float dt) { }
        // The operator walked dx while holding it. Most machines ignore it.
        public virtual void Walk(float dx) { }
        protected virtual void OnBegin(Vector2 mouse) { }

        // Eases the light over its handle towards its scene intensity times `scale` (0 for off).
        public void Glow(float scale, float dt)
        {
            if (!glow) return;
            if (_glowRest < 0) _glowRest = glow.intensity;
            glow.intensity = Mathf.MoveTowards(glow.intensity, _glowRest * scale, _glowRest * dt * 3);
        }
        protected virtual void OnEnd() { }
    }
}

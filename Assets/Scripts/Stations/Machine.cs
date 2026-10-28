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
        [Tooltip("The parts you take hold of. A faint light over them shows them in the dark, brighter in reach.")]
        [SerializeField]
        private SpriteRenderer[] accents;

        private Light2D _glow;

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

        // Eases a faint light over the handle towards level (its intensity), so it shows however dark the room
        // is. A copy of `template`, made on first use and carried on the first accent so it moves with it.
        public void Glow(float level, Light2D template, float dt)
        {
            if (accents == null || accents.Length == 0 || !template) return;
            if (!_glow)
            {
                _glow = Instantiate(template, accents[0].transform);
                _glow.name = "Glow";
                _glow.transform.localPosition = Vector3.zero;
                _glow.gameObject.SetActive(true);
            }
            _glow.intensity = Mathf.MoveTowards(_glow.intensity, level, dt * 2);
        }
        protected virtual void OnEnd() { }
    }
}

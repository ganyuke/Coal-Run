using System.Collections.Generic;
using UnityEngine;

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
        [Tooltip("The parts you take hold of. They keep a faint glow so they show in the dark, brighter in reach.")]
        [SerializeField]
        private SpriteRenderer[] accents;

        private SpriteRenderer[] _glows;
        private float _glow;

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

        // Eases the glow towards level (its alpha). The glow is an unlit copy of each accent laid over it, so
        // the room's light can't put it out; made on first use.
        public void Glow(float level, Material material, Color color, float dt)
        {
            if (accents == null || accents.Length == 0) return;
            if (_glows == null)
            {
                _glows = new SpriteRenderer[accents.Length];
                for (var i = 0; i < accents.Length; i++)
                {
                    var a = accents[i];
                    var copy = new GameObject("Glow").AddComponent<SpriteRenderer>();
                    copy.transform.SetParent(a.transform, false);
                    copy.sprite = a.sprite;
                    copy.drawMode = a.drawMode;
                    copy.size = a.size;
                    copy.flipX = a.flipX;
                    copy.sharedMaterial = material;
                    copy.sortingLayerID = a.sortingLayerID;
                    copy.sortingOrder = a.sortingOrder + 1;
                    _glows[i] = copy;
                }
            }
            _glow = Mathf.MoveTowards(_glow, level, dt * 2);
            color.a = _glow;
            foreach (var g in _glows) g.color = color;
        }
        protected virtual void OnEnd() { }
    }
}

using System;
using UnityEngine;

namespace CoalRun.Gameplay.Characters
{
    // template class to hold the death event for entities
    [UnityEngine.Scripting.APIUpdating.MovedFrom("CoalRun.Stations")]
    public abstract class Human : MonoBehaviour
    {
        [SerializeField] protected Rigidbody2D corpsePrefab;

        private Rigidbody2D _corpse;
        public bool IsDead { get; private set; }
        public event Action Died;

        public Rigidbody2D Kill()
        {
            if (IsDead) return _corpse;
            IsDead = true;
            // Notify before moving the art or disabling the character.
            Died?.Invoke();
            _corpse = OnDeath();
            return _corpse;
        }

        protected abstract Rigidbody2D OnDeath();

        protected void ResetLife()
        {
            IsDead = false;
            _corpse = null;
        }
    }
}
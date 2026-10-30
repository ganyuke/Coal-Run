using CoalRun.Core;
using UnityEngine;

namespace CoalRun.Gameplay.Characters
{
    // specialized helper for killing the tutorial helper
    [UnityEngine.Scripting.APIUpdating.MovedFrom("CoalRun.Stations")]
    public class TutorialHelper : Human
    {
        [SerializeField] private GameObject art;

        protected override Rigidbody2D OnDeath()
        {
            var body = Corpse.Topple(corpsePrefab, art, "Dead helper", transform.position, Vector2.zero, true);
            gameObject.SetActive(false);
            ShiftManager.Instance.PlayCue("hurt");
            return body;
        }
    }
}
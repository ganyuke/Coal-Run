using CoalRun.Gameplay.Coal;
using CoalRun.Gameplay.Lift;
using UnityEngine;

namespace CoalRun.Core
{
    // Scene geometry used by movement, coal, the lift and the camera. Wire references on the Room object in
    // Boiler.unity; Bind() fills any that are still empty from the colliders and art already in the scene.
    [DefaultExecutionOrder(-55)]
    public class Room : MonoBehaviour
    {
        [Header("Room and floors")]
        [SerializeField] private SpriteRenderer backdrop;
        [SerializeField] private BoxCollider2D lowerFloor, upperFloor, controlRoomFloor;
        [SerializeField] private Collider2D liftPlatform;

        [Header("Station geometry")]
        [SerializeField] private SpriteRenderer hopperGrate;
        [SerializeField] private Transform chuteMouth;
        [SerializeField] private Transform returnBoundary;

        [Header("Movement destinations")]
        [SerializeField] private Transform arrival;
        [SerializeField] private Transform staffLiftInside, staffLiftCenter, stairsFoot;

        public Bounds Bounds
        {
            get
            {
                if (backdrop) return backdrop.bounds;
                var b = lowerFloor.bounds;
                b.Encapsulate(upperFloor.bounds);
                b.Encapsulate(controlRoomFloor.bounds);
                return b;
            }
        }

        public float Left => Bounds.min.x;
        public float Right => Bounds.max.x;
        public float Top => Bounds.max.y;
        public float Lower => lowerFloor.bounds.max.y;
        public float Upper => upperFloor.bounds.max.y;
        public float LowerLeft => lowerFloor.bounds.min.x;
        public float LowerRight => lowerFloor.bounds.max.x;
        public float UpperLeft => upperFloor.bounds.min.x;
        public float Edge => upperFloor.bounds.max.x;
        public float LiftX => liftPlatform.bounds.center.x;
        public float LiftWidth => liftPlatform.bounds.size.x;
        public float LiftLeft => liftPlatform.bounds.min.x;
        public float LiftRight => liftPlatform.bounds.max.x;
        public float HopperStart => hopperGrate.bounds.min.x;
        public float HopperEnd => hopperGrate.bounds.max.x;
        public Vector2 ChuteMouth => chuteMouth.position;
        public float Chute => chuteMouth.position.x;
        public float EntranceStart => returnBoundary.position.x;
        public Vector2 Arrival => new(arrival.position.x, Lower + .72f);
        public float StaffLiftInside => staffLiftInside.position.x;
        public float StaffLiftCenter => staffLiftCenter.position.x;
        public float StairsFoot => stairsFoot.position.x;
        public float BoothStart => controlRoomFloor.bounds.min.x;
        public float BoothFloor => controlRoomFloor.bounds.max.y;

        private void Awake() => Bind();

        public void Bind()
        {
            var colliders = transform.Find("Colliders");
            if (colliders)
            {
                foreach (var box in colliders.GetComponents<BoxCollider2D>())
                {
                    if (box.size.y > 2f) continue;
                    if (box.offset.y < 5f) lowerFloor ??= box;
                    else if (box.size.x >= 10f) upperFloor ??= box;
                }

                var control = colliders.Find("Control room floor");
                if (control) controlRoomFloor ??= control.GetComponent<BoxCollider2D>();
            }

            if (!liftPlatform)
            {
                var lift = FindFirstObjectByType<Lift>();
                if (lift) liftPlatform = lift.GetComponent<Collider2D>();
            }

            if (!hopperGrate)
            {
                foreach (var sprite in GetComponentsInChildren<SpriteRenderer>(true))
                    if (sprite.name.StartsWith("Hopper grate")) hopperGrate = sprite;
            }

            if (!backdrop)
            {
                var walls = transform.Find("Art/Walls");
                if (walls)
                {
                    SpriteRenderer best = null;
                    var bestWidth = 0f;
                    foreach (var sprite in walls.GetComponentsInChildren<SpriteRenderer>(true))
                    {
                        var w = sprite.bounds.size.x;
                        if (w > bestWidth) { bestWidth = w; best = sprite; }
                    }

                    backdrop = best;
                }
            }

            var chute = FindFirstObjectByType<Chute>();
            if (chute)
            {
                var mouth = chute.transform.Find("Mouth");
                chuteMouth = mouth ? mouth : chuteMouth ? chuteMouth : chute.transform;
            }

            var art = transform.Find("Art");
            if (art)
            {
                returnBoundary ??= art.Find("Return grate/Grate slot") ?? art.Find("Return grate");
                stairsFoot ??= art.Find("Stairs");
            }

            if (!arrival || !staffLiftInside || !staffLiftCenter)
            {
                foreach (var staff in GetComponentsInChildren<Transform>(true))
                {
                    if (staff.name != "Staff lift") continue;
                    staffLiftCenter ??= staff;
                    staffLiftInside ??= staff;
                    break;
                }

                foreach (var door in GetComponentsInChildren<Transform>(true))
                {
                    if (door.name != "Staff lift door") continue;
                    arrival ??= door;
                    break;
                }
            }
        }
    }
}

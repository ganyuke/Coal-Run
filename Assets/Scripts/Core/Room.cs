using UnityEngine;
namespace CoalRun.Core
{
    // Scene geometry used by movement, coal, the lift and the camera.
    public class Room : MonoBehaviour
    {
        [Header("Room and floors")]
        [SerializeField] private SpriteRenderer backdrop;
        [SerializeField] private BoxCollider2D lowerFloor, upperFloor, controlRoomFloor;
        [SerializeField] private BoxCollider2D liftPlatform;

        [Header("Station geometry")]
        [SerializeField] private SpriteRenderer hopperGrate;
        [SerializeField] private Transform chuteMouth;
        [SerializeField] private Transform returnBoundary;

        [Header("Movement destinations")]
        [SerializeField] private Transform arrival;
        [SerializeField] private Transform staffLiftInside, staffLiftCenter, stairsFoot;

        public Bounds Bounds => backdrop.bounds;
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
    }
}

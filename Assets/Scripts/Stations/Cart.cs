using System.Collections.Generic;
using UnityEngine;

// The cart. A dynamic body whose colliders are rebuilt whenever a wheel comes or goes, and whose
// mass follows the coal in it. With one wheel the back end drags on a high-friction skid.
[RequireComponent(typeof(Rigidbody2D))]
public class Cart : MonoBehaviour
{
    [SerializeField] GameObject frontWheel, backWheel, skid;
    [Tooltip("Scaled vertically with the load.")]
    [SerializeField] Transform fill;

    public Rigidbody2D Body { get; private set; }
    public int Wheels { get; private set; }
    public float Coal { get; set; }
    public float Angle => Body.rotation * Mathf.Deg2Rad;
    public float AngularVelocity => Body.angularVelocity * Mathf.Deg2Rad;
    // Filled in when the tip cradle arrives.
    public bool InCradle => false;
    // How deep the bin sits in coal.
    public float Burial => game.Coal.CartBurial(this);

    ShiftManager game;
    readonly List<Collider2D> colliders = new();
    PhysicsMaterial2D binMaterial, wheelMaterial, skidMaterial, dragMaterial;

    void Awake()
    {
        Body = GetComponent<Rigidbody2D>();
        Body.useAutoMass = false;
        binMaterial = new PhysicsMaterial2D("Cart bin") { friction = .55f };
        wheelMaterial = new PhysicsMaterial2D("Cart wheel") { friction = .025f };
        skidMaterial = new PhysicsMaterial2D("Cart skid") { friction = .75f };
        dragMaterial = new PhysicsMaterial2D("Cart no wheels") { friction = 1.05f };
    }

    void Start()
    {
        game = ShiftManager.Instance;
        Wheels = game.Day.startingWheels;
        Rebuild(false);
    }

    void FixedUpdate() => UpdateMass();

    void Update()
    {
        if (fill)
        {
            float f = Mathf.Clamp01(Coal / game.Tune.overfillCapacity);
            fill.localScale = new Vector3(fill.localScale.x, f * .7f, 1);
            fill.gameObject.SetActive(f > .01f);
        }
    }

    public void SetWheels(int wheels)
    {
        Wheels = Mathf.Clamp(wheels, 0, 2);
        Rebuild(true);
        game.Sound("repair");
    }

    void Rebuild(bool clearWheelOverlap)
    {
        // Disabled first: Destroy only happens at the end of the frame, after this physics step.
        foreach (var c in colliders) { c.enabled = false; Destroy(c); }
        colliders.Clear();
        int noParts = GameLayers.Mask(GameLayers.Part);

        var bin = gameObject.AddComponent<PolygonCollider2D>();
        bin.points = new Vector2[] { new(-.83f, -.12f), new(.83f, -.12f), new(1, .64f), new(-1, .64f) };
        bin.sharedMaterial = binMaterial;
        colliders.Add(bin);
        if (Wheels >= 1) colliders.Add(Wheel(.58f));
        if (Wheels >= 2) colliders.Add(Wheel(-.58f));
        else
        {
            var box = gameObject.AddComponent<BoxCollider2D>();
            box.offset = new Vector2(-.58f, -.3f);
            box.size = new Vector2(.32f, .46f);
            box.sharedMaterial = Wheels > 0 ? skidMaterial : dragMaterial;
            box.excludeLayers = noParts;
            colliders.Add(box);
        }

        CircleCollider2D Wheel(float x)
        {
            var wheel = gameObject.AddComponent<CircleCollider2D>();
            wheel.offset = new Vector2(x, -.34f);
            wheel.radius = .3f;
            wheel.sharedMaterial = wheelMaterial;
            wheel.excludeLayers = noParts;
            return wheel;
        }

        if (frontWheel) frontWheel.SetActive(Wheels >= 1);
        if (backWheel) backWheel.SetActive(Wheels >= 2);
        if (skid) skid.SetActive(Wheels < 2);
        UpdateMass();

        if (!clearWheelOverlap) return;
        // A newly fitted wheel can reach below the skid that held the bin up, so lift the cart just
        // enough to clear the floor.
        float rise = 0;
        foreach (var c in colliders)
            if (c is CircleCollider2D)
            {
                Vector2 center = Body.GetRelativePoint(c.offset);
                rise = Mathf.Max(rise, FloorBelow(center) + .325f - center.y);
            }
        if (rise > 0)
        {
            Body.position += new Vector2(0, rise);
            Body.linearVelocity = new Vector2(Body.linearVelocity.x, Mathf.Max(0, Body.linearVelocity.y));
        }
        Body.WakeUp();
    }

    float FloorBelow(Vector2 p)
    {
        if (p.x >= .55f && p.x <= 3.95f && p.y >= game.Lift.Y - .3f) return game.Lift.Y;
        if (p.x >= 4 && p.x <= Room.Edge && p.y >= Room.Upper - .3f) return Room.Upper;
        return Room.Lower;
    }

    public void UpdateMass()
    {
        var t = game.Tune;
        float mass = t.cartMass + Coal * t.coalMass;
        Body.mass = mass;
        Body.centerOfMass = new Vector2(0, Coal * .012f);
        Body.inertia = mass * .62f;
    }
}

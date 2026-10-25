using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Coal Run > Build Greybox Scene. Lays out the boiler room from Room's numbers with placeholder
// sprites and wires every reference. Run it once; after that Boiler.unity is an ordinary scene
// to edit by hand. Running it again replaces the scene (it asks first).
public static class GreyboxBuilder
{
    const string ScenePath = "Assets/Scenes/Boiler.unity";
    const string ArtDir = "Assets/Art/Greybox";
    const string DataDir = "Assets/Data";
    static readonly string[] Layers = { "Terrain", "Player", "Cart", "Coal", "Part", "Pile" };

    static Sprite square, circle;
    static Material unlit;

    static readonly Color Concrete = new(.16f, .16f, .17f), Slab = new(.32f, .31f, .29f), Steel = new(.45f, .45f, .47f);
    static readonly Color Rust = new(.55f, .3f, .18f), Ember = new(.75f, .28f, .12f), Glass = new(.45f, .7f, .85f, .22f);

    [MenuItem("Coal Run/Build Greybox Scene")]
    static void BuildFromMenu()
    {
        if (File.Exists(ScenePath) && !EditorUtility.DisplayDialog("Rebuild the Boiler scene?",
                "This replaces Assets/Scenes/Boiler.unity, including anything you changed in it by hand.", "Rebuild", "Cancel")) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Build();
    }

    // No dialogs, for driving the editor from outside (unity command eval). Unsaved scene changes are discarded.
    public static void Build()
    {
        // Opening a new scene unloads assets nothing references yet, so the scene comes before any asset is loaded.
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EnsureLayers();
        square = MakeSprite("square", 4, (x, y) => true);
        circle = MakeSprite("circle", 64, (x, y) => (x - 31.5f) * (x - 31.5f) + (y - 31.5f) * (y - 31.5f) <= 32 * 32);
        unlit = AssetDatabase.LoadAssetAtPath<Material>("Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Unlit-Default.mat");

        var tuning = DataAsset<Tuning>("Tuning", _ => { });
        var day1 = DataAsset<DayConfig>("Day1", _ => { });
        DataAsset<DayConfig>("Day2", d =>
        {
            // Placeholder: Day 1's storm, six degrees colder. Tune once Day 1 plays well.
            d.day = 2; d.training = false; d.startingWheels = 2; d.startingHopper = 15; d.target = 240;
            for (int i = 0; i < d.forecast.Length; i++) d.forecast[i].temperature -= 6;
        });

        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = Room.Height / 2;
        cam.transform.position = new Vector3(Room.Width / 2, Room.Height / 2, -10);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(.06f, .06f, .07f);

        BuildRoom();
        var lift = BuildLift(out var platformCrank, out var wheelVisual);
        var lowerCrank = Crank("Lower landing crank", new Vector2(4.75f, Room.Lower + 1.65f), null, LiftCrank.Spot.LowerLanding, lift);
        var upperCrank = Crank("Upper landing crank", new Vector2(4.75f, Room.Upper + 1.65f), null, LiftCrank.Spot.UpperLanding, lift);
        var player = BuildPlayer(out var playerVisual);
        var cart = BuildCart();
        var chute = BuildChute(tuning);

        var coalGo = new GameObject("Coal field");
        var coal = coalGo.AddComponent<CoalField>();
        Wire(coal, ("liftPlatform", lift.transform), ("lumpSprite", circle), ("columnSprite", square), ("spriteMaterial", unlit));

        var game = new GameObject("Game");
        var shift = game.AddComponent<ShiftManager>();
        var input = game.AddComponent<InputReader>();
        var machines = game.AddComponent<MachineInput>();
        var hud = game.AddComponent<DebugHud>();

        Wire(shift, ("tuning", tuning), ("day", day1), ("input", input), ("player", player), ("cart", cart), ("lift", lift), ("coal", coal), ("chute", chute));
        Wire(machines, ("player", player), ("cam", cam));
        Wire(hud, ("player", player), ("machines", machines));
        Wire(player, ("input", input), ("machines", machines), ("visual", playerVisual));
        Wire(lift, ("input", input), ("wheelVisual", wheelVisual));
        WireArray(lift, "cranks", platformCrank, lowerCrank, upperCrank);

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        Debug.Log("Coal Run: built " + ScenePath + ". Press Play.");
    }

    // ---- Room -------------------------------------------------------------------------------

    static void BuildRoom()
    {
        var room = new GameObject("Room").transform;
        var art = new GameObject("Art").transform;
        art.SetParent(room);

        // Back wall and structure, back to front.
        Rect(art, "Back wall", 0, 0, Room.Width, Room.Height, Concrete, -20);
        Rect(art, "Lift shaft", .35f, Room.Lower, 4, Room.Height - 2, new Color(.1f, .1f, .11f), -19);
        Rect(art, "Coal bunker", 24.5f, 14, 27.5f, Room.Height, new Color(.12f, .11f, .1f), -18);
        Rect(art, "Chute", 25.5f, 6.8f, 26.5f, 14, Steel * .7f, -18);
        Rect(art, "Firebox (back lane)", 15.4f, Room.Lower, 18.6f, 5.6f, Ember * .6f, -17);
        Rect(art, "Firebox mouth", 16, 2.6f, 18, 4.4f, Ember, -16);
        Rect(art, "Hopper", 15.9f, 6.2f, 18.1f, Room.Upper - .4f, Rust * .8f, -17);
        Rect(art, "Staff lift", Room.EntranceStart, Room.Lower, 31.7f, Room.EntranceRoof, new Color(.2f, .21f, .2f), -18);
        Rect(art, "Red phone", 28.75f, 3.3f, 29.05f, 3.75f, new Color(.85f, .2f, .2f), -15);
        Rect(art, "Window (blizzard)", 8, 12, 14, 15.5f, new Color(.55f, .6f, .66f), -18);

        // Floors and walls.
        Rect(art, "Lower floor", 0, 0, Room.Width, Room.Lower, Slab, 0);
        Rect(art, "Upper floor", 4, Room.Upper - .4f, Room.Edge, Room.Upper, Slab, 0);
        Rect(art, "Open edge stripe", Room.Edge - .4f, Room.Upper - .1f, Room.Edge, Room.Upper + .05f, new Color(.9f, .7f, .1f), 1);
        Rect(art, "Outer wall", 0, 0, .35f, Room.Height, Slab * .8f, 0);
        Rect(art, "Right wall", 31.7f, 0, Room.Width, Room.Height, Slab * .8f, 0);

        // Stations that don't do anything yet, so the room reads.
        Rect(art, "Tip cradle", Room.Tip - .6f, Room.Upper, Room.Tip + .6f, Room.Upper + .25f, Steel, 2);
        Rect(art, "Burn lever (todo)", 14.15f, Room.Upper, 14.45f, Room.Upper + 1.8f, Steel, 2);
        Rect(art, "Weigh plate", 24.6f, Room.Lower - .05f, 27.4f, Room.Lower + .05f, new Color(.6f, .5f, .2f), 1);
        // Control room: the operator stands behind this glass in lane 1.
        Rect(art, "Control room glass", 23, Room.Lower, 30.7f, 5.6f, Glass, 10);

        var floors = new GameObject("Colliders") { layer = LayerMask.NameToLayer("Terrain") };
        floors.transform.SetParent(room);
        var floor = PhysicsMaterial("Floor", .65f);
        var wall = PhysicsMaterial("Wall", .4f);
        Edge(floors, floor, new(.35f, Room.Lower), new(31.7f, Room.Lower));
        Edge(floors, floor, new(4, Room.Upper), new(Room.Edge, Room.Upper));
        Edge(floors, wall, new(.35f, Room.Lower), new(.35f, 16));
        Edge(floors, wall, new(31.7f, Room.Lower), new(31.7f, 16));
    }

    static void Edge(GameObject go, PhysicsMaterial2D material, Vector2 a, Vector2 b)
    {
        var e = go.AddComponent<EdgeCollider2D>();
        e.points = new[] { a, b };
        e.sharedMaterial = material;
    }

    // ---- Lift -------------------------------------------------------------------------------

    static Lift BuildLift(out LiftCrank crank, out Transform wheelVisual)
    {
        var go = new GameObject("Lift") { layer = LayerMask.NameToLayer("Terrain") };
        go.transform.position = new Vector2(Room.LiftX, Room.Lower - .15f);
        var body = go.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
        var shape = go.AddComponent<PolygonCollider2D>();
        shape.points = new Vector2[] { new(-1.75f, -.15f), new(1.75f, -.15f), new(1.75f, .04f), new(1.6f, .15f), new(-1.6f, .15f), new(-1.75f, .04f) };
        // Same grip as the floors, so a cart with no wheels can still be dragged off.
        shape.sharedMaterial = PhysicsMaterial("Lift", .65f);
        var lift = go.AddComponent<Lift>();

        Vector2 at = go.transform.position;
        Box(go.transform, "Platform", at, new Vector2(3.5f, .3f), Steel, 3);
        Box(go.transform, "Post", at + new Vector2(-1.55f, 1), new Vector2(.12f, 2), Rust, 2);
        Box(go.transform, "Rail", at + new Vector2(0, 2.05f), new Vector2(3.2f, .1f), Rust, 2);

        crank = Crank("Platform wheel", new Vector2(Room.LiftX - .85f, Room.Lower + 1.65f), go.transform, LiftCrank.Spot.Platform, lift);
        wheelVisual = crank.transform.Find("Wheel");
        return lift;
    }

    static LiftCrank Crank(string name, Vector2 at, Transform parent, LiftCrank.Spot spot, Lift lift)
    {
        var go = new GameObject(name);
        if (parent) go.transform.SetParent(parent);
        go.transform.position = at;
        var wheel = Box(go.transform, "Wheel", at, Vector2.one * 1.2f, Steel * .8f, 4, circle).transform;
        Box(wheel, "Spoke", at, new Vector2(1.1f, .12f), Rust, 5);
        Box(wheel, "Spoke", at, new Vector2(.12f, 1.1f), Rust, 5);
        // The spokes live under the scaled wheel, so undo its scale on them.
        foreach (Transform spoke in wheel) spoke.localScale = new Vector3(spoke.localScale.x / 1.2f, spoke.localScale.y / 1.2f, 1);
        var crank = go.AddComponent<LiftCrank>();
        var so = new SerializedObject(crank);
        so.FindProperty("handle").objectReferenceValue = go.transform;
        so.FindProperty("spot").enumValueIndex = (int)spot;
        so.FindProperty("lift").objectReferenceValue = lift;
        so.ApplyModifiedPropertiesWithoutUndo();
        return crank;
    }

    // The lever sits in the control room, behind the glass. Its knob starts at the top of its travel.
    static Chute BuildChute(Tuning tuning)
    {
        var at = new Vector2(28.35f, 3.7f);
        var go = new GameObject("Chute lever");
        go.transform.position = at;
        Box(go.transform, "Slot", at, new Vector2(.1f, tuning.leverTravel + .2f), Steel * .5f, -6);
        var knob = Box(go.transform, "Knob", at + new Vector2(0, tuning.leverTravel / 2), new Vector2(.4f, .25f), new Color(.8f, .2f, .15f), -5).transform;
        var chute = go.AddComponent<Chute>();
        Wire(chute, ("handle", go.transform), ("knob", knob));
        return chute;
    }

    // ---- Operator and cart ------------------------------------------------------------------

    static PlayerController BuildPlayer(out SortingGroup visual)
    {
        var go = new GameObject("Operator") { layer = LayerMask.NameToLayer("Player") };
        go.transform.position = new Vector2(30.3f, Room.Lower + .72f);
        var body = go.AddComponent<Rigidbody2D>();
        body.useAutoMass = false;
        body.mass = 14;
        body.freezeRotation = true;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        body.sleepMode = RigidbodySleepMode2D.NeverSleep;
        var box = go.AddComponent<BoxCollider2D>();
        box.size = new Vector2(.48f, 1.32f);
        box.sharedMaterial = PhysicsMaterial("Operator", .15f);
        var player = go.AddComponent<PlayerController>();

        var art = new GameObject("Art");
        art.transform.SetParent(go.transform, false);
        visual = art.AddComponent<SortingGroup>();
        visual.sortingOrder = 20;
        Box(art.transform, "Body", (Vector2)go.transform.position + new Vector2(0, -.12f), new Vector2(.48f, 1.08f), new Color(.85f, .55f, .2f), 0);
        Box(art.transform, "Head", (Vector2)go.transform.position + new Vector2(0, .5f), new Vector2(.34f, .34f), new Color(.9f, .75f, .6f), 1, circle);
        return player;
    }

    static Cart BuildCart()
    {
        var go = new GameObject("Cart") { layer = LayerMask.NameToLayer("Cart") };
        go.transform.position = new Vector2(Room.Chute, Room.Lower + .68f);
        var body = go.AddComponent<Rigidbody2D>();
        body.useAutoMass = false;
        body.linearDamping = .08f;
        body.angularDamping = .8f;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        var cart = go.AddComponent<Cart>();

        Vector2 at = go.transform.position;
        var art = new GameObject("Art").transform;
        art.SetParent(go.transform, false);
        art.gameObject.AddComponent<SortingGroup>().sortingOrder = 15;
        Box(art, "Bin", at + new Vector2(0, .26f), new Vector2(1.9f, .76f), Rust, 1);
        var fill = Box(art, "Fill", at + new Vector2(0, .3f), new Vector2(1.7f, .7f), new Color(.08f, .08f, .08f), 2).transform;
        var front = Box(art, "Front wheel", at + new Vector2(.58f, -.34f), Vector2.one * .6f, Steel * .6f, 0, circle);
        var back = Box(art, "Back wheel", at + new Vector2(-.58f, -.34f), Vector2.one * .6f, Steel * .6f, 0, circle);
        var skid = Box(art, "Skid", at + new Vector2(-.58f, -.3f), new Vector2(.32f, .46f), Steel * .5f, 0);

        Wire(cart, ("frontWheel", front), ("backWheel", back), ("skid", skid), ("fill", fill));
        return cart;
    }

    // ---- Helpers ----------------------------------------------------------------------------

    static GameObject Rect(Transform parent, string name, float x0, float y0, float x1, float y1, Color color, int order) =>
        Box(parent, name, new Vector2((x0 + x1) / 2, (y0 + y1) / 2), new Vector2(x1 - x0, y1 - y0), color, order);

    // A sprite of the given world size centred at a world position. Art sits on its own child
    // objects, so scaling it never scales a collider.
    static GameObject Box(Transform parent, string name, Vector2 center, Vector2 size, Color color, int order, Sprite sprite = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = center;
        go.transform.localScale = new Vector3(size.x, size.y, 1);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite ? sprite : square;
        sr.color = color;
        sr.sortingOrder = order;
        if (unlit) sr.sharedMaterial = unlit;
        return go;
    }

    static Sprite MakeSprite(string name, int size, System.Func<int, int, bool> inside)
    {
        Directory.CreateDirectory(ArtDir);
        string path = $"{ArtDir}/{name}.png";
        if (!File.Exists(path))
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++) tex.SetPixel(x, y, inside(x, y) ? Color.white : Color.clear);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = size; // One unit across.
            importer.filterMode = size <= 4 ? FilterMode.Point : FilterMode.Bilinear;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static T DataAsset<T>(string name, System.Action<T> setup) where T : Object, new()
    {
        string path = $"{DataDir}/{name}" + (typeof(T) == typeof(PhysicsMaterial2D) ? ".physicsMaterial2D" : ".asset");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset) return asset; // Never overwrite tuning that's already been edited.
        asset = typeof(ScriptableObject).IsAssignableFrom(typeof(T)) ? (T)(Object)ScriptableObject.CreateInstance(typeof(T)) : new T();
        setup(asset);
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        return asset;
    }

    static PhysicsMaterial2D PhysicsMaterial(string name, float friction) => DataAsset<PhysicsMaterial2D>($"Physics/{name}", m => m.friction = friction);

    static void EnsureLayers()
    {
        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tagManager.FindProperty("layers");
        for (int i = 0; i < Layers.Length; i++)
        {
            if (LayerMask.NameToLayer(Layers[i]) >= 0) continue;
            var slot = layers.GetArrayElementAtIndex(6 + i);
            if (!string.IsNullOrEmpty(slot.stringValue))
                throw new System.Exception($"Layer slot {6 + i} is already '{slot.stringValue}'. Free layers 6-11 or add '{Layers[i]}' by hand.");
            slot.stringValue = Layers[i];
        }
        tagManager.ApplyModifiedPropertiesWithoutUndo();
    }

    static void Wire(Object target, params (string field, Object value)[] fields)
    {
        var so = new SerializedObject(target);
        foreach (var (field, value) in fields)
        {
            var p = so.FindProperty(field) ?? throw new System.Exception($"{target.GetType().Name} has no field '{field}'.");
            p.objectReferenceValue = value;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void WireArray(Object target, string field, params Object[] values)
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(field);
        p.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}

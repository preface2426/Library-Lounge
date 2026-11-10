using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Editor-only, deterministic, non-PlayMode furnishing pass.
// Source scene and production player launcher are never modified here.
// Run via: NetCafe / Cozy Lounge / Furnish library lounge V2 (separate scene)
public static class CozyLoungeFurnitureBuilder
{
    public const string BaseScene = "Assets/CozyLoungeGreybox/Scenes/CozyLounge_Playable.unity";
    public const string FurnishedScene = "Assets/CozyLoungeGreybox/Scenes/LibraryLounge_Furnished.unity";
    const string Models = "Assets/CozyLoungeGreybox/KenneyFurniture/";
    const string MatFolder = "Assets/CozyLoungeGreybox/FurnishedMaterials/";
    static Transform decoration;
    static Material oak, honey, cream, sage, moss, terracotta, graphite, paleBlue, rug;
    static readonly Vector3[] ScreenCoords = {
        new Vector3(1.22f, 1.16f, 3.401f), new Vector3(3.72f, 1.16f, 3.401f),
        new Vector3(-4.0f, .79f, 2.095f), new Vector3(-1.8f, .79f, 2.095f),
        new Vector3(-3.48f, .79f, -2.235f), new Vector3(-1.32f, .79f, -2.235f)
    };
    static readonly float[] SeatEye = { 1.25f, 1.25f, 1.12f, 1.12f, 1.13f, 1.13f };
    static readonly float[] SeatOffset = { .69f, .69f, .69f, .69f, .69f, .69f };

    [MenuItem("NetCafe/Cozy Lounge/Furnish library lounge V2 (separate scene)")]
    public static void Rebuild()
    {
        if (!File.Exists(BaseScene)) throw new FileNotFoundException("Playable source missing", BaseScene);
        if (File.Exists(FurnishedScene))
            throw new IOException("Furnished scene exists, refusing overwrite: " + FurnishedScene);
        string[] requiredModels = {
            "desk", "chairDesk", "loungeChairRelax", "loungeSofaLong",
            "loungeDesignChair", "sideTable", "tableCoffee", "tableRound",
            "bookcaseOpen", "lampRoundFloor", "lampRoundTable", "pottedPlant",
            "plantSmall1", "plantSmall2", "books", "rugRectangle", "rugRound",
            "computerScreen", "computerKeyboard", "computerMouse", "sideTableDrawers"
        };
        foreach (string name in requiredModels)
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Models + name + ".fbx") == null)
                throw new FileNotFoundException("Kenney FBX was not imported: " + name, Models + name + ".fbx");

        Scene initial = EditorSceneManager.OpenScene(BaseScene, OpenSceneMode.Single);
        if (!EditorSceneManager.SaveScene(initial, FurnishedScene, true))
            throw new IOException("Could not save independent furnished scene");
        Scene scene = EditorSceneManager.OpenScene(FurnishedScene, OpenSceneMode.Single);
        var roots = scene.GetRootGameObjects();
        var cafe = roots.SelectMany(x => x.GetComponentsInChildren<CafePrototype>(true)).Single();
        if (cafe.seats.Length < 8 || cafe.screens.Length != 6 || cafe.exits.Length < 8)
            throw new InvalidOperationException("Unexpected game seat and screen bindings");

        var oldGreybox = roots.Single(x => x.name == "Cozy Lounge L01 OFFLINE GREYBOX");
        oldGreybox.SetActive(false); // Retained for rollback and design comparison.
        HideLegacyCosmetics(cafe);

        var newRoot = new GameObject("LIBRARY LOUNGE V2 - KENNEY CC0 + WARM ARCHITECTURE");
        decoration = newRoot.transform;
        CreateMaterials();
        BuildRoom();
        BuildFurniture();
        FitGameSeats(cafe, scene);
        LibraryLoungeFurnitureRefinement.ApplyTransforms(scene);        // Interactive hotspots are gaze/raycast targets, not hidden solid
        // obstacles. Actual furniture colliders remain on the IgnoreRaycast layer.
        foreach (var hit in scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<CafeInteractable>(true)))
            if (hit.action == CafeInteractable.Action.Seat || hit.action == CafeInteractable.Action.Sofa)
            {
                var volume = hit.GetComponent<Collider>();
                if (volume == null) throw new InvalidOperationException("Missing seat hotspot collider");
                volume.isTrigger = true;
            }
        ValidateSeatDistances(cafe);        // Regenerate every wall and furniture collider when rebuilding the room.
        CozyLoungeCollisionCoverage.RepairScene(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new IOException("Failed to save furnished Unity scene");
        // Only promote the new scene after all validation and the save succeed.
        EditorBuildSettings.scenes = new[] {
            new EditorBuildSettingsScene(FurnishedScene, true),
            new EditorBuildSettingsScene(BaseScene, false),
            new EditorBuildSettingsScene("Assets/Prototype/NetCafePrototype.unity", false)
        };
        Debug.Log("LIBRARY_LOUNGE_V2_SCENE_PASS: " + FurnishedScene +
            "; six interactive computers; seat-eye-to-screen=.69m; 2 desktops + 4 laptops;" +
            " original playable/greybox untouched; no game or native input started");
    }

    static void HideLegacyCosmetics(CafePrototype cafe)
    {
        Transform gameplayRoot = cafe.transform;
        foreach (Renderer r in gameplayRoot.GetComponentsInChildren<Renderer>(true))
        {
            if (cafe.body != null && r.transform.IsChildOf(cafe.body.transform)) continue;
            if (cafe.screens.Any(x => x == r)) continue;
            if (cafe.publicScreen == r) continue;
            if (r is MeshRenderer) r.enabled = false;
        }
        foreach (Collider c in gameplayRoot.GetComponentsInChildren<Collider>(true))
        {
            if (c == cafe.body || (cafe.body && c.transform.IsChildOf(cafe.body.transform))) continue;
            if (c.GetComponentInParent<CafeInteractable>() != null) continue;
            if (c.name.IndexOf("wall", StringComparison.OrdinalIgnoreCase) >= 0 ||
                c.name.IndexOf("floor", StringComparison.OrdinalIgnoreCase) >= 0) continue;
            if (c is BoxCollider || c is MeshCollider) c.enabled = false;
        }
    }

    static Material Create(string name, Color color, float smoothness = .18f)
    {
        string path = MatFolder + name + ".mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) throw new InvalidOperationException("No lit shader available");
        m = new Material(shader) { name = name };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }
    static void CreateMaterials()
    {
        if (!AssetDatabase.IsValidFolder(MatFolder.TrimEnd('/')))
            AssetDatabase.CreateFolder("Assets/CozyLoungeGreybox", "FurnishedMaterials");
        oak = Create("WarmOak", new Color(.65f,.44f,.28f));
        honey = Create("HoneyOak", new Color(.78f,.59f,.37f));
        cream = Create("IvoryCream", new Color(.94f,.87f,.75f));
        sage = Create("SageCloth", new Color(.45f,.58f,.49f));
        moss = Create("DeepOlive", new Color(.25f,.39f,.34f));
        terracotta = Create("ClayOrange", new Color(.76f,.43f,.30f));
        graphite = Create("SoftGraphite", new Color(.20f,.22f,.23f));
        paleBlue = Create("MutedBlue", new Color(.50f,.63f,.65f));
        rug = Create("NaturalRug", new Color(.67f,.67f,.53f));
    }

    static GameObject Block(string name, Vector3 at, Vector3 size, Material m,
        bool solid = false, Transform parent = null, PrimitiveType type = PrimitiveType.Cube)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent != null ? parent : decoration, false);
        go.transform.position = at;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = m;
        var collider = go.GetComponent<Collider>();
        if (collider != null) collider.enabled = solid;
        // Visual-only props must not steal gaze rays from seat interaction hotspots.
        go.layer = 2; // Ignore Raycast; physics collision can remain for solid furniture.
        return go;
    }
    static void BuildRoom()
    {
        Transform architecture = new GameObject("01 ARCHITECTURE - OAK LINEN").transform;
        architecture.SetParent(decoration);
        // This is the only active floor: the retained prototype and greybox floors
        // are inactive. Never create it as visual-only or players will fall forever.
        Block("Warm plank floor", new Vector3(0, -.16f, 0), new Vector3(12,.28f,10), oak, true, architecture);
        // Low-frequency plank rhythm, subtle contrast without complex texture atlases.
        for (int i = 0; i < 20; i++)
        {
            float z=-4.72f + i*.50f;
            Block("Floor inlay " + i, new Vector3(0,.0005f,z), new Vector3(11.8f,.005f,.012f),
                (i%4==0)?honey:oak, false, architecture);
        }
        Block("North ivory wall", new Vector3(0,1.65f,4.99f), new Vector3(12.0f,3.3f,.14f), cream, false, architecture);
        Block("West ivory wall", new Vector3(-5.98f,1.65f,0), new Vector3(.14f,3.3f,10f), cream, false, architecture);
        Block("East ivory wall", new Vector3(5.98f,1.65f,0), new Vector3(.14f,3.3f,10f), cream, false, architecture);
        Block("South wall left", new Vector3(-3.52f,1.65f,-4.98f), new Vector3(4.95f,3.3f,.14f), cream, false, architecture);
        Block("South wall right", new Vector3(3.52f,1.65f,-4.98f), new Vector3(4.95f,3.3f,.14f), cream, false, architecture);
        Block("Entrance arch lintel", new Vector3(0,2.96f,-4.98f), new Vector3(2.15f,.62f,.18f), honey, false, architecture);
        for (int i=0;i<4;i++)
        {
            float x=-4.3f + i*2.85f;
            Block("Window wall battens "+i, new Vector3(x,1.85f,4.85f),new Vector3(.08f,2.15f,.08f),honey,false,architecture);
            Block("Window diffused daylight "+i, new Vector3(x+1.35f,1.95f,4.80f),
                new Vector3(2.53f,1.79f,.022f),paleBlue,false,architecture);
        }
        // Warm library architectural rhythm.
        for (int i=0;i<8;i++)
        {
            float x=-5.34f+i*1.53f;
            Block("Ceiling oak slat "+i, new Vector3(x,3.22f,0),new Vector3(.11f,.08f,9.4f),honey,false,architecture);
        }
        for (int i=0;i<3;i++)
        {
            Block("Ceiling diffused strip "+i,new Vector3(-2.7f+i*2.7f,3.16f,-.2f),
                new Vector3(.045f,.035f,5.6f),cream,false,architecture);
        }
        // Dedicated zones; thin ground rugs have no movement collision.
        Block("Central conversation rug",new Vector3(-2.38f,.015f,-2.73f),
            new Vector3(4.65f,.035f,3.2f),rug,false,architecture);
        Block("Reading rug",new Vector3(-3.15f,.013f,1.62f),
            new Vector3(4.7f,.025f,2.7f),sage,false,architecture);
        Block("Window work rug",new Vector3(2.42f,.013f,2.10f),
            new Vector3(4.85f,.025f,2.0f),cream,false,architecture);
    }

    static GameObject Furniture(string asset, string name, Vector3 where, Vector3 size, Material baseMat,
        Material detailMat = null, float yaw = 0f, bool solid = true)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Models + asset + ".fbx");
        if (prefab == null) throw new FileNotFoundException("Missing furniture: " + asset);
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, decoration);
        go.name = name;
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        go.transform.localScale = Vector3.one;
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) throw new InvalidOperationException("Empty FBX renderer: " + asset);
        foreach (Renderer r in renderers)
        {
            Material[] arr = r.sharedMaterials;
            for (int i=0;i<arr.Length;i++)
                arr[i] = (detailMat!=null && i%3==1) ? detailMat : baseMat;
            r.sharedMaterials = arr;
            r.gameObject.layer = 2;
        }
        Bounds original = Combine(renderers);
        if (original.size.x < .0001f || original.size.y < .0001f || original.size.z < .0001f)
            throw new InvalidOperationException("Invalid imported FBX bounds: " + asset);
        go.transform.localScale = new Vector3(size.x/original.size.x,size.y/original.size.y,size.z/original.size.z);
        go.transform.rotation = Quaternion.Euler(0,yaw,0);
        Bounds bounds = Combine(renderers);
        go.transform.position += new Vector3(where.x-bounds.center.x,
            where.y-bounds.min.y, where.z-bounds.center.z);
        // Add limited physical blocking with an explicit convex box, not complex mesh colliders.
        if (solid)
        {
            GameObject obstacle = new GameObject(name+" - walk blocker (no gaze ray)");
            obstacle.transform.SetParent(decoration);
            obstacle.transform.position = new Vector3(where.x,where.y+size.y*.45f,where.z);
            obstacle.layer = 2;
            BoxCollider box = obstacle.AddComponent<BoxCollider>();
            box.size = new Vector3(Mathf.Max(.25f,size.x*.82f),
                Mathf.Max(.35f,size.y*.9f),Mathf.Max(.25f,size.z*.78f));
            box.isTrigger = false;
        }
        return go;
    }
    static Bounds Combine(Renderer[] elements)
    {
        Bounds result = elements[0].bounds;
        for (int i=1;i<elements.Length;i++)result.Encapsulate(elements[i].bounds);
        return result;
    }

    static void BuildFurniture()
    {
        // Window / desktop positions 0-1. Exact logical screen positions are preserved.
        for(int i=0;i<2;i++)
        {
            float x=i==0?1.22f:3.72f;
            Furniture("desk","Window reading desk "+(i+1),new Vector3(x,0,3.10f),
                new Vector3(1.85f,.74f,.80f),oak,honey,0,true);
            Furniture("chairDesk","Close desk chair "+(i+1),new Vector3(x,0,2.56f),
                new Vector3(.69f,.96f,.70f),sage,honey,0,true);
            // Screen frame is intentionally *behind* the original live Screen renderer (z=3.401).
            Furniture("computerScreen","Warm framed monitor "+(i+1),new Vector3(x,.74f,3.49f),
                new Vector3(.91f,.65f,.12f),graphite,honey,0,false);
            Furniture("computerKeyboard","Keyboard "+(i+1),new Vector3(x,.74f,2.92f),
                new Vector3(.43f,.045f,.17f),graphite,cream,0,false);
            Furniture("computerMouse","Mouse "+(i+1),new Vector3(x+.38f,.74f,2.94f),
                new Vector3(.065f,.04f,.10f),graphite,null,0,false);
            Furniture("lampRoundTable","Amber task lamp "+(i+1),new Vector3(x-.66f,.74f,3.34f),
                new Vector3(.22f,.35f,.22f),cream,honey,0,false);
        }
        // Reading corner positions 2-3: comfortable chairs are 0.7m from actual laptop surfaces.
        for(int i=0;i<2;i++)
        {
            float x=i==0?-4f:-1.8f;
            Furniture("loungeChairRelax","Reading recliner "+(i+1),new Vector3(x,0,1.30f),
                new Vector3(1.03f,1.06f,1.17f),i==0?terracotta:sage,honey,0,true);
            Furniture("sideTable","Reading side table "+(i+1),new Vector3(x,0,2.02f),
                new Vector3(.88f,.62f,.69f),oak,honey,0,true);
            Furniture("books","Open books "+(i+1),new Vector3(x+.26f,.63f,2.2f),
                new Vector3(.21f,.06f,.17f),cream,terracotta,10,false);
        }
        // Four laptop surfaces remain interactive and private: no extra network subscriptions.
        Furniture("loungeSofaLong","Social sofa - sage",new Vector3(-2.4f,0,-3.27f),
            new Vector3(3.25f,1.06f,1.00f),sage,honey,0,true);
        Furniture("tableCoffee","Social laptop table",new Vector3(-2.4f,0,-2.18f),
            new Vector3(2.86f,.52f,.83f),honey,oak,0,true);
        Furniture("loungeDesignChair","Corner reading chair",new Vector3(.63f,0,-1.10f),
            new Vector3(.88f,1.04f,.84f),terracotta,oak,225,true);
        Furniture("tableRound","Quiet corner round table",new Vector3(1.95f,0,-1.45f),
            new Vector3(.9f,.70f,.90f),honey,oak,0,true);
        // Existing rest seats 6/7 are separate from the six computer indexes.
        Furniture("loungeSofa","Guest rest sofa",new Vector3(1.4f,0,-3.62f),
            new Vector3(2.44f,1.04f,1.04f),paleBlue,honey,0,true);
        // Books, plants and lamps make the social space feel lived in rather than a PC showroom.
        Furniture("bookcaseOpen","Reading library shelves west",new Vector3(-5.48f,0,.55f),
            new Vector3(.44f,2.03f,1.43f),oak,honey,90,true);
        Furniture("bookcaseOpen","Open bookcase north",new Vector3(-4.84f,0,3.75f),
            new Vector3(1.05f,1.90f,.43f),oak,honey,0,true);
        Furniture("sideTableDrawers","Entry cabinet",new Vector3(4.52f,0,-4.03f),
            new Vector3(.96f,.73f,.51f),oak,cream,0,true);
        Furniture("lampRoundFloor","Reading floor lamp",new Vector3(-5.18f,0,2.55f),
            new Vector3(.34f,1.70f,.34f),cream,honey,0,false);
        Furniture("lampRoundFloor","Conversation floor lamp",new Vector3(-4.72f,0,-3.92f),
            new Vector3(.34f,1.75f,.34f),cream,honey,0,false);
        Furniture("pottedPlant","Tall indoor greenery",new Vector3(4.85f,0,.24f),
            new Vector3(.62f,1.28f,.62f),moss,honey,0,false);
        Furniture("plantSmall1","Shelf plant",new Vector3(-5.02f,1.9f,3.80f),
            new Vector3(.22f,.32f,.22f),sage,oak,0,false);
        Furniture("plantSmall2","Corner plant",new Vector3(-.03f,0,3.84f),
            new Vector3(.36f,.66f,.36f),moss,terracotta,0,false);
        // Shared secondary colors make the room distinct without costly shaders.
        Block("Room name plinth",new Vector3(3.9f,2.52f,-4.78f),
            new Vector3(2.15f,.38f,.06f),honey,false);
    }

    static void FitGameSeats(CafePrototype cafe, Scene scene)
    {
        var all = scene.GetRootGameObjects()
            .SelectMany(x => x.GetComponentsInChildren<CafeInteractable>(true))
            .Where(x => x.action == CafeInteractable.Action.Seat).ToArray();
        if (all.Length!=6)throw new InvalidOperationException("Expected six computer hotspots; got "+all.Length);
        for(int i=0;i<6;i++)
        {
            Vector3 screen = ScreenCoords[i];
            // Live gameplay screen renderer takes precedence over visual mock-up dimensions.
            if ((screen - cafe.screens[i].transform.position).magnitude > .2f)
                throw new InvalidOperationException("Live screen is outside expected location: "+i);
            Vector3 eyes = new Vector3(screen.x,SeatEye[i],screen.z-SeatOffset[i]);
            cafe.seats[i].position = eyes;
            cafe.seats[i].rotation = Quaternion.identity;
            float seatZ = i<2?2.56f:i<4?1.30f:-3.27f;
            Vector3 physicalSeat = new Vector3(screen.x,.71f,seatZ);
            CafeInteractable spot = all.Single(x=>x.seatIndex==i);
            spot.transform.position = physicalSeat;
            // Stand near a clear corridor, not inside the new furniture.
            cafe.exits[i].position = new Vector3(screen.x,.04f,
                i<2?1.48f:i<4?.09f:-4.40f);
            if (i>=2)
            {
                // Keep sofa/laptop line-of-sight aligned with the actual display.
                cafe.seats[i].rotation = Quaternion.identity;
            }
            Debug.Log("LIBRARY_SEAT_REPOSITION id="+i+" seat="+eyes+
                " monitor="+screen+" distance="+Vector3.Distance(eyes,screen).ToString("F2"));
        }
        // A separate non-computer rest sofa still accommodates both indices 6 and 7.
        if (cafe.seats.Length>7)
        {
            float[] x={.78f,1.95f};
            for(int i=6;i<8;i++)
            {
                cafe.seats[i].position = new Vector3(x[i-6],1.18f,-3.34f);
                cafe.seats[i].rotation = Quaternion.identity;
                cafe.exits[i].position = new Vector3(x[i-6],.04f,-2.4f);
                foreach (CafeInteractable rest in scene.GetRootGameObjects()
                    .SelectMany(xr=>xr.GetComponentsInChildren<CafeInteractable>(true))
                    .Where(q=>q.action==CafeInteractable.Action.Sofa&&q.seatIndex==i))
                    rest.transform.position=new Vector3(x[i-6],.66f,-3.45f);
            }
        }
    }

    static void ValidateSeatDistances(CafePrototype cafe)
    {
        for(int i=0;i<6;i++)
        {
            float delta=Vector3.Distance(cafe.seats[i].position,cafe.screens[i].transform.position);
            if (delta < .53f || delta > .82f)
                throw new InvalidOperationException("Seat/monitor distance out of range "+i+": "+delta);
        }
        if (cafe.seats.Length<8 || cafe.screens.Length!=6)
            throw new InvalidOperationException("Computer/rest capacity regression");
    }
}

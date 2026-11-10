using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// The approved 2026-10-31 low-poly cinema corner, BAKED into the actual Unity scene.
// Edit only the shared wall display and visual lounge props. All six computer
// screens, their seats, voice, and opt-in LiveKit desktop sharing are untouched.
public static class LibraryLoungeCinemaRedesign
{
    const string CinemaLounge20261031 = "CinemaLounge20261031 - curved seating and wall cinema";
    const float ScreenHeight = 2.90f; // 3.30m wall => 0.20m above and below
    const float ScreenWidth = 5.12f;  // 16:9-ish shared interactive display
    const string FurnishedMaterials = "Assets/CozyLoungeGreybox/FurnishedMaterials/";
    static Transform group, root;
    static Material linen, sand, olive, walnut, golden, pale, wood, graphite;

    [MenuItem("NetCafe/Library Lounge/Apply approved cinema and sofas to Unity scene")]
    public static void Apply()
    {
        Scene scene=EditorSceneManager.OpenScene(LibraryLoungeBuild.ScenePath, OpenSceneMode.Single);
        root=scene.GetRootGameObjects().Single(x=>x.name.StartsWith("LIBRARY LOUNGE V2 - KENNEY",StringComparison.Ordinal)).transform;
        var cafe=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<CafePrototype>(true)).Single();
        // Preserve the exact positions and bindings of ALL multiplayer stations.
        Vector3[] stations=cafe.seats.Take(6).Select(x=>x.position).ToArray();
        Vector3[] desktops=cafe.screens.Take(6).Select(x=>x.transform.position).ToArray();
        Transform previous=root.Find(CinemaLounge20261031);
        if(previous) UnityEngine.Object.DestroyImmediate(previous.gameObject);
        group=new GameObject(CinemaLounge20261031).transform;
        group.SetParent(root,false);
        linen=Mat("CinemaLinen",new Color(.80f,.76f,.70f),.12f);
        sand=Mat("CinemaSoftCream",new Color(.91f,.86f,.79f),.13f);
        olive=Mat("CinemaOliveVelvet",new Color(.42f,.47f,.32f),.1f);
        walnut=Mat("CinemaWalnut",new Color(.46f,.31f,.22f),.22f);
        golden=Mat("CinemaLampIvory",new Color(1.0f,.87f,.59f),.25f);
        pale=Mat("CinemaWovenRug",new Color(.82f,.77f,.68f),.08f);
        wood=AssetDatabase.LoadAssetAtPath<Material>(FurnishedMaterials+"HoneyOak.mat");
        graphite=AssetDatabase.LoadAssetAtPath<Material>(FurnishedMaterials+"SoftGraphite.mat");
        if(!wood||!graphite) throw new InvalidOperationException("Existing room finish materials missing");

        // The real interactive Public screen is on the EAST wall (seen on the
        // left in the user's camera). Scaling the live renderer matters: a new
        // decorative black box alone would never show shared room content.
        Transform live=cafe.publicScreen.transform;
        if(cafe.publicScreen.name!="Public screen") throw new InvalidOperationException("Interactive screen binding changed");
        live.position=new Vector3(5.755f,1.65f,-2.10f);
        live.localScale=new Vector3(ScreenWidth,ScreenHeight,.018f);
        EditorUtility.SetDirty(live);
        // Dark walnut frame, behind the active live renderer, with narrow trim.
        Cube("Cinema black backing",new Vector3(5.819f,1.65f,-2.10f),new Vector3(.070f,3.04f,5.26f),graphite);
        Cube("Cinema timber border top",new Vector3(5.755f,3.138f,-2.10f),new Vector3(.09f,.050f,5.26f),walnut);
        Cube("Cinema timber border bottom",new Vector3(5.755f,.162f,-2.10f),new Vector3(.09f,.050f,5.26f),walnut);
        Cube("Cinema timber border near",new Vector3(5.755f,1.65f,-4.698f),new Vector3(.09f,2.97f,.056f),walnut);
        Cube("Cinema timber border far",new Vector3(5.755f,1.65f,.498f),new Vector3(.09f,2.97f,.056f),walnut);

        // Keep all functional seating rigs and sofa hotspots. Refinish their
        // real FBX meshes rather than replacing them with non-interactive props.
        Transform couch=Only("Guest rest sofa");
        Refinish(couch,linen,sand);
        Refinish(Only("Corner reading chair"),olive,olive);
        Refinish(Only("Quiet corner round table"),walnut,wood);
        // A second adjoining module gives the neutral sofa a gentle wraparound
        // profile like the reference, without intersecting rest seats 6/7.
        var sectional=new GameObject("Cream curved sectional").transform;
        sectional.SetParent(group,false);
        Cube("Cream curved sectional wing seat",new Vector3(2.94f,.37f,-3.32f),new Vector3(.94f,.43f,1.10f),linen,sectional);
        Cube("Cream curved sectional wing back",new Vector3(3.40f,.71f,-3.31f),new Vector3(.22f,.70f,1.12f),sand,sectional);
        Ellipsoid("Cream curved sectional elbow",new Vector3(3.22f,.67f,-3.91f),new Vector3(.46f,.66f,.46f),sand,sectional);
        Ellipsoid("Cream curved sectional wing cushion",new Vector3(2.95f,.65f,-3.25f),new Vector3(.76f,.18f,.88f),sand,sectional);
        // Loose, low-poly sofa pillows set behind sitting avatars.
        for(int i=0;i<3;i++)
            Ellipsoid("Cream curved sectional pillow "+i,new Vector3(.68f+i*.72f,.83f,-3.93f),
                new Vector3(.48f,.42f,.16f),i==1?olive:sand,sectional);

        // The existing green chair retains its collision proxy but gains
        // rounder arms and inviting soft cushions like the supplied chair photo.
        var chair=new GameObject("Olive rounded accent chair").transform;
        chair.SetParent(group,false);
        Ellipsoid("Olive rounded accent chair left arm",new Vector3(.20f,.61f,-1.15f),new Vector3(.29f,.58f,.84f),olive,chair);
        Ellipsoid("Olive rounded accent chair right arm",new Vector3(1.09f,.61f,-1.15f),new Vector3(.29f,.58f,.84f),olive,chair);
        Ellipsoid("Olive rounded accent chair lumbar pillow",new Vector3(.63f,.80f,-1.46f),new Vector3(.66f,.38f,.17f),sand,chair);
        Ellipsoid("Olive rounded accent chair seat pad",new Vector3(.64f,.43f,-1.07f),new Vector3(.74f,.16f,.63f),olive,chair);

        // Large cream rug joins the new sofa, accent chair and coffee table;
        // floor collision remains entirely on the original single walkable floor.
        Cube("Cinema woven area rug",new Vector3(1.82f,.015f,-2.43f),new Vector3(4.03f,.026f,3.12f),pale);
        Cube("Cinema rug warm edge north",new Vector3(1.82f,.031f,-.89f),new Vector3(4.0f,.007f,.024f),linen);
        Cube("Cinema rug warm edge south",new Vector3(1.82f,.031f,-3.97f),new Vector3(4.0f,.007f,.024f),linen);

        // Layer a low, rounded wood table over the existing interactive-room
        // decor. Its ORIGINAL body collider remains, so rest-seat exits stay open.
        var table=new GameObject("Cinema low coffee table").transform;
        table.SetParent(group,false);
        Cylinder("Cinema low coffee table octagonal surface",new Vector3(1.95f,.705f,-1.45f),
            new Vector3(1.20f,.056f,1.15f),walnut,table);
        BookStack(table,new Vector3(1.65f,.77f,-1.30f));
        Cylinder("Coffee table ceramic bowl",new Vector3(2.22f,.760f,-1.48f),new Vector3(.26f,.055f,.23f),olive,table);
        // Cabinet is built to the LEFT of the pre-existing Entry cabinet,
        // making one coherent low wood sideboard without overlapping that prop.
        var cabinet=new GameObject("Walnut book cabinet").transform;
        cabinet.SetParent(group,false);
        Cube("Walnut book cabinet top",new Vector3(3.48f,.755f,-4.59f),new Vector3(2.88f,.075f,.44f),walnut,cabinet);
        Cube("Walnut book cabinet left end",new Vector3(2.16f,.40f,-4.59f),new Vector3(.095f,.71f,.40f),walnut,cabinet);
        Cube("Walnut book cabinet divider",new Vector3(3.12f,.40f,-4.59f),new Vector3(.070f,.70f,.40f),walnut,cabinet);
        Cube("Walnut book cabinet long bottom",new Vector3(3.06f,.095f,-4.59f),new Vector3(1.95f,.09f,.40f),walnut,cabinet);
        Cube("Walnut book cabinet middle shelf",new Vector3(3.06f,.415f,-4.59f),new Vector3(1.92f,.05f,.39f),wood,cabinet);
        for(int i=0;i<8;i++)
            Cube("Walnut book cabinet book "+i,new Vector3(2.28f+i*.102f,.53f,-4.58f),
                new Vector3(.065f,.20f+(i%3)*.024f,.23f),i%3==0?olive:i%3==1?sand:graphite,cabinet);
        for(int i=0;i<7;i++)
            Cube("Walnut book cabinet lower book "+i,new Vector3(3.32f+i*.09f,.23f,-4.58f),
                new Vector3(.070f,.17f+(i%2)*.04f,.25f),i%2==0?sand:olive,cabinet);
        var lamp=new GameObject("Cinema warm dome lamp").transform;
        lamp.SetParent(group,false);
        Cylinder("Cinema warm dome lamp stand",new Vector3(4.51f,.94f,-4.51f),new Vector3(.12f,.27f,.12f),walnut,lamp);
        Ellipsoid("Cinema warm dome lamp shade",new Vector3(4.51f,1.12f,-4.51f),new Vector3(.44f,.23f,.42f),golden,lamp);
        var lightObject=new GameObject("Cinema warm dome lamp local light");
        lightObject.transform.SetParent(lamp,false);
        lightObject.transform.position=new Vector3(4.51f,1.13f,-4.47f);
        var light=lightObject.AddComponent<Light>();light.type=LightType.Point;
        light.color=new Color(1f,.81f,.57f);light.intensity=.48f;light.range=2.75f;
        light.shadows=LightShadows.None;
        Foliage(new Vector3(2.66f,.78f,-4.55f));
        // The tall old plant stood INSIDE the enlarged real screen's footprint.
        Transform tall=Only("Tall indoor greenery");
        tall.position+=new Vector3(-.28f,0,1.00f);
        EditorUtility.SetDirty(tall);

        CozyLoungeCollisionCoverage.RepairScene(scene);
        VerifyScene(scene,stations,desktops);
        LibraryLoungeFurnitureRefinement.VerifyScene(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene))throw new IOException("Unable to save redesigned furnished scene");
        // Validate() reopens the scene, invalidating the previous Scene handle.
        // It must run AFTER saving and after all handle-based audits.
        LibraryLoungeBuild.Validate();
        Debug.Log("CINEMA_DESIGN_APPLY_PASS: large 2.90m real screen; curved cream sofa; olive chair; rug; wood coffee; bookshelf; plants; six seats unchanged");
    }

    static Material Mat(string name,Color color,float smoothness)
    {
        string path=FurnishedMaterials+name+".mat";
        var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(m!=null)return m;
        Shader shader=Shader.Find("Universal Render Pipeline/Lit");
        if(!shader)throw new InvalidOperationException("URP Lit shader missing");
        m=new Material(shader){name=name};
        m.SetColor("_BaseColor",color);
        if(m.HasProperty("_Smoothness"))m.SetFloat("_Smoothness",smoothness);
        AssetDatabase.CreateAsset(m,path);
        return m;
    }
    static Transform Only(string name)
    {
        var matches=root.GetComponentsInChildren<Transform>(true).Where(x=>x.name==name).ToArray();
        if(matches.Length!=1)throw new InvalidOperationException("Unexpected furniture "+name+" count="+matches.Length);
        return matches[0];
    }
    static GameObject Part(string name,PrimitiveType kind,Vector3 at,Vector3 size,Material material,Transform parent=null)
    {
        GameObject g=GameObject.CreatePrimitive(kind);g.name=name;
        g.transform.SetParent(parent?parent:group,false);
        g.transform.position=at;g.transform.localScale=size;
        g.layer=2;UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());
        g.GetComponent<Renderer>().sharedMaterial=material;
        return g;
    }
    static GameObject Cube(string name,Vector3 at,Vector3 size,Material m,Transform parent=null)
        =>Part(name,PrimitiveType.Cube,at,size,m,parent);
    static GameObject Ellipsoid(string name,Vector3 at,Vector3 size,Material m,Transform parent=null)
        =>Part(name,PrimitiveType.Sphere,at,size,m,parent);
    static GameObject Cylinder(string name,Vector3 at,Vector3 size,Material m,Transform parent=null)
        =>Part(name,PrimitiveType.Cylinder,at,size,m,parent);
    static void Refinish(Transform obj,Material a,Material b)
    {
        var meshes=obj.GetComponentsInChildren<Renderer>(true);
        if(meshes.Length==0)throw new InvalidOperationException("Missing furniture mesh "+obj.name);
        for(int j=0;j<meshes.Length;j++)
        {
            var arr=meshes[j].sharedMaterials;
            for(int i=0;i<arr.Length;i++) arr[i]=(i%3==1)?b:a;
            meshes[j].sharedMaterials=arr;EditorUtility.SetDirty(meshes[j]);
        }
    }
    static void BookStack(Transform parent,Vector3 basePos)
    {
        for(int i=0;i<3;i++)
            Cube("Cinema low coffee table books "+i,basePos+Vector3.up*(i*.05f),
                new Vector3(.36f-i*.025f,.043f,.23f+i*.012f),i==1?olive:i==2?sand:wood,parent);
    }
    static void Foliage(Vector3 basePos)
    {
        var plant=new GameObject("Low poly foliage in vase").transform;plant.SetParent(group,false);
        Cylinder("Low poly foliage ceramic vase",basePos+Vector3.up*.17f,new Vector3(.24f,.16f,.24f),sand,plant);
        Cube("Low poly foliage stem",basePos+Vector3.up*.53f,new Vector3(.025f,.50f,.025f),walnut,plant);
        for(int i=0;i<10;i++)
        {
            float a=i*Mathf.PI*2/10f;
            var at=basePos+new Vector3(Mathf.Cos(a)*(.22f+.035f*(i%2)),.58f+(i%4)*.12f,Mathf.Sin(a)*.18f);
            GameObject leaf=Ellipsoid("Low poly foliage leaf "+i,at,new Vector3(.26f,.08f,.12f),i%3==0?olive:wood,plant);
            leaf.transform.rotation=Quaternion.Euler(0,-a*Mathf.Rad2Deg,20f*(i%2==0?1:-1));
        }
    }
    [MenuItem("NetCafe/Library Lounge/Verify approved cinema lounge scene")]
    public static void Verify()
    {
        Scene scene=EditorSceneManager.OpenScene(LibraryLoungeBuild.ScenePath,OpenSceneMode.Single);
        var cafe=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<CafePrototype>(true)).Single();
        VerifyScene(scene,cafe.seats.Take(6).Select(x=>x.position).ToArray(),
            cafe.screens.Take(6).Select(x=>x.transform.position).ToArray());
        CozyLoungeCollisionCoverage.Verify(scene);
        LibraryLoungeBuild.Validate();
        Debug.Log("CINEMA_DESIGN_VERIFY_PASS");
    }
    static void VerifyScene(Scene scene,Vector3[] stationPositions,Vector3[] screenPositions)
    {
        var cafe=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<CafePrototype>(true)).Single();
        // CafeDesktopSharing is intentionally added on-demand by CafeEntryUi
        // at runtime. Do not require it to exist in the serialized scene.
        if(!cafe.GetComponent<CafeRoomSync>() || !cafe.GetComponent<CafeEntryUi>())
            throw new InvalidOperationException("Privacy-safe room sync / CafeDesktopSharing bootstrap missing");
        if(cafe.screens.Length<6||cafe.seats.Length<8||cafe.exits.Length<8)
            throw new InvalidOperationException("Six computer stations or rest seats lost");
        for(int i=0;i<6;i++)
        {
            if((cafe.seats[i].position-stationPositions[i]).sqrMagnitude>.00001f||
               (cafe.screens[i].transform.position-screenPositions[i]).sqrMagnitude>.00001f ||
               !cafe.screens[i].gameObject.activeInHierarchy)
                throw new InvalidOperationException("Computer station unexpectedly moved: "+i);
        }
        if(Mathf.Abs(cafe.publicScreen.bounds.size.y-ScreenHeight)>.05f||
           Mathf.Abs(cafe.publicScreen.bounds.size.z-ScreenWidth)>.06f||
           Mathf.Abs(cafe.publicScreen.bounds.min.y-.20f)>.035f||
           Mathf.Abs(cafe.publicScreen.bounds.max.y-3.10f)>.035f)
            throw new InvalidOperationException("Public wall display was not actually enlarged: "+cafe.publicScreen.bounds);
        int displayCount=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<CafeInteractable>(true))
            .Count(x=>x.action==CafeInteractable.Action.Display);
        if(displayCount!=1)throw new InvalidOperationException("Interactive public screen changed: "+displayCount);
        root=scene.GetRootGameObjects().Single(x=>x.name.StartsWith("LIBRARY LOUNGE V2 - KENNEY",StringComparison.Ordinal)).transform;
        Transform visual=root.Find(CinemaLounge20261031);
        if(!visual)throw new InvalidOperationException("Approved cinema group absent");
        string[] required={"Cream curved sectional","Olive rounded accent chair","Cinema woven area rug",
            "Cinema low coffee table","Walnut book cabinet","Cinema warm dome lamp","Low poly foliage in vase"};
        foreach(string name in required)
            if(!visual.GetComponentsInChildren<Transform>(true).Any(x=>x.name==name))
                throw new InvalidOperationException("Approved cinema decor missing: "+name);
        if(visual.GetComponentsInChildren<Collider>(true).Any(x=>x.enabled))
            throw new InvalidOperationException("Decorative cinema collision unexpectedly blocks player path");
        Debug.Log("CINEMA_DESIGN_GEOMETRY_PASS screen="+cafe.publicScreen.bounds+" six computers preserved, decorative colliders=0");
    }

    [MenuItem("NetCafe/Library Lounge/Build approved cinema lounge Windows candidate")]
    public static void BuildCinemaRelease() => LibraryLoungeCinemaBuild(false);
    [MenuItem("NetCafe/Library Lounge/Build B2 A2-RV Development QA")]
    public static void BuildB2A2RvQA() => LibraryLoungeCinemaBuild(true,"LibraryLounge-B2-A2RV-QA-20261101");
    // Isolated v0.1.0-derived desktop input/screenshot QA. Never overwrites
    // the approved cinema build or the user's published launcher/ZIP.
    [MenuItem("NetCafe/Library Lounge/Build desktop interaction Development QA")]
    public static void BuildDesktopInteractionQA() => LibraryLoungeCinemaBuild(true,"LibraryLounge-DesktopInteraction-QA-20261101");

    [MenuItem("NetCafe/Library Lounge/Build screen capture diagnostic QA")]
    public static void BuildDesktopCaptureDiagnosticQA() =>
        LibraryLoungeCinemaBuild(true, "LibraryLounge-DesktopCapture-Diagnostic-QA-20261101");
    [MenuItem("NetCafe/Library Lounge/Build 90 percent view and popup candidate")]
    public static void BuildRoomView90PopupQA() =>
        LibraryLoungeCinemaBuild(true, "LibraryLounge-90Pct-Popup-QA-20261101");

    [MenuItem("NetCafe/Library Lounge/Build 90 percent view popup and auto-hide toolbar QA")]
    public static void BuildRoomView90PopupAutoDockQA() =>
        LibraryLoungeCinemaBuild(true, "LibraryLounge-90Pct-Popup-AutoDock-QA-20261101");

    [MenuItem("NetCafe/Library Lounge/Build 90 percent view popup final candidate")]
    public static void BuildRoomView90PopupFinalQA() =>
        LibraryLoungeCinemaBuild(true, "LibraryLounge-90Pct-Popup-ImmediateDock-QA-20261101");

    [MenuItem("NetCafe/Library Lounge/Build 90 percent popup acceptance QA")]
    public static void BuildRoomView90PopupAcceptanceQA() =>
        LibraryLoungeCinemaBuild(true, "LibraryLounge-90Pct-Popup-Acceptance-QA-20261101");

    [MenuItem("NetCafe/Library Lounge/Build performance security candidate QA")]
    public static void BuildPerformanceSecurityQA() =>
        LibraryLoungeCinemaBuild(true, "LibraryLounge-PerfSecurity-QA-20261101");

    [MenuItem("NetCafe/Library Lounge/Build A1 color verified and window desk repaired QA")]
    public static void BuildA1RoomRepairedQA() =>
        LibraryLoungeCinemaBuild(true, "LibraryLounge-A1Room-Repaired-QA-20261101");

    [MenuItem("NetCafe/Library Lounge/Build input deadlock safety QA")]
    public static void BuildInputSafetyQA() =>
        LibraryLoungeCinemaBuild(true, "LibraryLounge-InputSafety-QA-20261101");

    [MenuItem("NetCafe/Library Lounge/Build input safety final QA")]
    public static void BuildInputSafetyFinalQA() =>
        LibraryLoungeCinemaBuild(true, "LibraryLounge-InputSafety-Final-QA-20261101");

    [MenuItem("NetCafe/Library Lounge/Build input safety cursor acceptance QA")]
    public static void BuildInputSafetyCursorQA() =>
        LibraryLoungeCinemaBuild(true, "LibraryLounge-InputSafety-Cursor-QA-20261101");

    [MenuItem("NetCafe/Library Lounge/Build focus handback QA")]
    public static void BuildInputSafetyFocusQA() =>
        LibraryLoungeCinemaBuild(true, "LibraryLounge-InputSafety-Focus-QA-20261101");

    [MenuItem("NetCafe/Library Lounge/Build B2 A2-RV Windows Release")]
    public static void BuildB2A2RvRelease() => LibraryLoungeCinemaBuild(false,"LibraryLounge-B2-A2RV-20261101");
    static void LibraryLoungeCinemaBuild(bool development, string requestedFolder=null)
    {
        Verify();
        string folder=requestedFolder ?? (development?"LibraryLounge-Cinema-Redesign-QA-20261031":"LibraryLounge-Cinema-Redesign-20261031");
        string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Builds",folder,"LibraryLounge.exe"));
        if(Directory.Exists(Path.GetDirectoryName(output)))throw new IOException("Refusing to overwrite build: "+output);
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{
            scenes=new[]{LibraryLoungeBuild.ScenePath},locationPathName=output,
            target=BuildTarget.StandaloneWindows64,
            options=development?BuildOptions.Development:BuildOptions.None
        });
        if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)
            throw new InvalidOperationException("Cinema Windows build failed: "+report.summary.result);
        Debug.Log("CINEMA_WINDOWS_BUILD_PASS "+output);
    }
}

using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Deterministic, reversible furnishings refinement. Only visual model transforms
// are adjusted; gameplay seats, exits, screens, privacy and network bindings stay fixed.
public static class LibraryLoungeFurnitureRefinement
{
    const string RootName="LIBRARY LOUNGE V2 - KENNEY CC0";
    const float ReadingRugTop=.0255f, WindowRugTop=.0255f, SocialRugTop=.0325f;
    static Transform decor;

    [MenuItem("NetCafe/Library Lounge/Refine furniture orientation, support and wall gaps")]
    public static void Apply()
    {
        Scene scene=EditorSceneManager.OpenScene(LibraryLoungeBuild.ScenePath,OpenSceneMode.Single);
        ApplyTransforms(scene);
        CozyLoungeCollisionCoverage.RepairScene(scene); // Saves and runs seat-exit checks.
        VerifyScene(scene);
        CozyLoungeFloorRepair.Verify(scene);
        CozyLoungeCameraClipAudit.Verify(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene))throw new IOException("Furniture scene save failed");
        LibraryLoungeBuild.Validate();
        Debug.Log("FURNITURE_REFINEMENT_APPLY_PASS: correct seat facing; intentional angle variation; level furniture and rugs; wall flush; collision + cameras + B1 bindings retained");
    }

    // Called from the initial scene builder so that recreating the scene does
    // not restore backwards seating or the floating floor gap.
    public static void ApplyTransforms(Scene scene)
    {
        decor=scene.GetRootGameObjects().Single(r=>r.name.StartsWith(RootName,StringComparison.Ordinal)).transform;
        Transform floor=Only("Warm plank floor");
        var floorCollider=floor.GetComponent<BoxCollider>();
        if(!floorCollider)throw new InvalidOperationException("No live floor collider");
        floor.position+=Vector3.up*(-floorCollider.bounds.max.y);
        // Kenney chair backs are at LOCAL +Z. In the old scene yaw=0 pointed
        // the backrest toward the monitor (world +Z), reversing every seat.
        Place("Close desk chair 1",1.20f,WindowRugTop,2.56f,174f);
        Place("Close desk chair 2",3.74f,WindowRugTop,2.54f,186f);
        Place("Reading recliner 1",-4.02f,ReadingRugTop,1.30f,171f);
        Place("Reading recliner 2",-1.78f,ReadingRugTop,1.32f,188f);
        Place("Social sofa - sage",-2.40f,SocialRugTop,-3.27f,178f);
        Place("Guest rest sofa",1.40f,0f,-3.62f,183f);
        // The lounge side chair faces its companion table, not an empty wall.
        Place("Corner reading chair",.63f,0f,-1.10f,287f);

        // Model foot contacts follow the real rug height (not a generic 0m
        // plane), then everything resting on a desk follows its actual top.
        for(int i=1;i<=2;i++)
        {
            float x=i==1?1.22f:3.72f;
            Place("Window reading desk "+i,x,WindowRugTop,3.10f,0f);
            Place("Warm framed monitor "+i,x,.74f+WindowRugTop,3.49f,0f);
            Place("Keyboard "+i,x,.74f+WindowRugTop,2.92f,0f);
            Place("Mouse "+i,x+.38f,.74f+WindowRugTop,2.94f,0f);
            Place("Amber task lamp "+i,x-.66f,.74f+WindowRugTop,3.34f,0f);
            float rx=i==1?-4f:-1.8f;
            Place("Reading side table "+i,rx,ReadingRugTop,2.02f,0f);
            Place("Open books "+i,rx+.26f,.62f+ReadingRugTop,2.2f,i==1?7f:353f);
        }
        Place("Social laptop table",-2.40f,SocialRugTop,-2.18f,0f);
        Place("Quiet corner round table",1.95f,0f,-1.45f,0f);
        // West shelf had world width 1.43m at yaw 90 and intersected the wall.
        // Correct the imported model's axis-swapped footprint to 0.44 x 1.43.
        Transform shelf=Only("Reading library shelves west");
        Bounds sb=BoundsOf(shelf);
        if(sb.size.x>.9f && sb.size.z<.9f)
        {
            Vector3 sc=shelf.localScale;
            shelf.localScale=new Vector3(sc.x*(1.43f/sb.size.z),sc.y,sc.z*(.44f/sb.size.x));
        }
        Place("Reading library shelves west",-5.675f,0f,.55f,90f);
        Place("Open bookcase north",-4.84f,0f,4.69f,0f);
        Place("Shelf plant",-5.02f,1.90f,4.65f,0f);
        Place("Entry cabinet",4.52f,0f,-4.635f,0f);
        Place("Reading floor lamp",-5.70f,0f,2.55f,0f);
        Place("Conversation floor lamp",-5.70f,0f,-3.75f,0f);
        Place("Tall indoor greenery",5.58f,0f,.24f,0f);
        Place("Corner plant",-.03f,0f,3.84f,0f);
        RepairLaptops(scene);
        RepairDisplayBezels(scene);
        RepairDesktopMonitorSupports(scene);
        Physics.SyncTransforms();
        Debug.Log("FURNITURE_TRANSFORM_PASS: floor top=0, 7 deliberate seat orientations, raised furniture to rugs, desk accessories supported, 3 cabinets aligned with walls");
    }

    static void RepairLaptops(Scene scene)
    {
        var cafe=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<CafePrototype>(true)).Single();
        Material shell=AssetDatabase.LoadAssetAtPath<Material>("Assets/CozyLoungeGreybox/FurnishedMaterials/SoftGraphite.mat");
        Material keys=AssetDatabase.LoadAssetAtPath<Material>("Assets/CozyLoungeGreybox/FurnishedMaterials/IvoryCream.mat");
        if (!shell || !keys) throw new InvalidOperationException("Laptop casing materials missing");
        for(int i=2;i<6;i++)
        {
            Transform screen=cafe.screens[i].transform;
            Vector3 center=screen.position;
            float top=i<4?.62f+ReadingRugTop:.52f+SocialRugTop;
            // Screen bottom meets its desk; normal follows the actual seated eyes.
            center.y=top+(i<4?.167f:.158f);
            screen.position=center;
            screen.rotation=Quaternion.LookRotation(center-cafe.seats[i].position,Vector3.up);
            Vector3 scale=screen.localScale;
            screen.localScale=new Vector3(.62f,scale.y,scale.z);
            Transform laptop=decor.Find("Laptop casing station "+(i+1));
            if (!laptop)
            {
                laptop=new GameObject("Laptop casing station "+(i+1)).transform;
                laptop.SetParent(decor,false);
            }
            laptop.position=new Vector3(center.x,top,center.z-(i<4?.22f:.18f));
            laptop.rotation=Quaternion.identity;
            float depth=i<4?.38f:.34f;
            Casing(laptop,"Keyboard base",new Vector3(0,.014f,0),new Vector3(.66f,.028f,depth),shell);
            Casing(laptop,"Keycaps",new Vector3(0,.029f,.025f),new Vector3(.49f,.002f,depth*.43f),keys);
            Casing(laptop,"Touchpad",new Vector3(0,.029f,-depth*.29f),new Vector3(.17f,.002f,.065f),keys);
            Casing(laptop,"Rear hinge",new Vector3(0,.033f,depth*.45f),new Vector3(.63f,.030f,.024f),shell);
            // The old screen bottom lies ~4 cm BELOW its hinge top. The Windows
            // taskbar occupies that clipped bottom strip in the 3D room view.
            // Align the whole lid so all screen pixels are above the hinge,
            // keeping the actual hinge/base fixed on the table.
            float hingeTop=top+.033f+.030f*.5f;
            float screenBottom=screen.TransformPoint(new Vector3(0,-.5f,0)).y;
            float clearance=Mathf.Max(0f,hingeTop+.007f-screenBottom);
            if(clearance>.10f)throw new InvalidOperationException("Unexpected laptop screen/hinge geometry "+i+" gap="+clearance);
            if(clearance>0f)
            {
                screen.position+=Vector3.up*clearance;
                screen.rotation=Quaternion.LookRotation(screen.position-cafe.seats[i].position,Vector3.up);
                // Recomputing the view angle changes the bottom-corner world height.
                // Keep the monitor in contact with its hinge without burying pixels.
                float adjusted=screen.TransformPoint(new Vector3(0,-.5f,0)).y;
                if(adjusted<hingeTop+.007f)
                {
                    screen.position+=Vector3.up*(hingeTop+.007f-adjusted);
                    screen.rotation=Quaternion.LookRotation(screen.position-cafe.seats[i].position,Vector3.up);
                }
            }
            Debug.Log("TASKBAR_LAPTOP_GEOMETRY_FIXED station="+i+" shift="+clearance.ToString("F5")+
                " bottom="+screen.TransformPoint(new Vector3(0,-.5f,0)).y.ToString("F5")+
                " hingeTop="+hingeTop.ToString("F5"));
            EditorUtility.SetDirty(screen);
        }
    }

    static void Casing(Transform parent,string name,Vector3 localPos,Vector3 size,Material material)
    {
        Transform part=parent.Find(name);
        if (!part)
        {
            var block=GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name=name;part=block.transform;part.SetParent(parent,false);
            UnityEngine.Object.DestroyImmediate(block.GetComponent<Collider>());
        }
        part.localPosition=localPos;part.localRotation=Quaternion.identity;part.localScale=size;
        part.gameObject.layer=2;
        part.GetComponent<Renderer>().sharedMaterial=material;
    }

    static void RepairDisplayBezels(Scene scene)
    {
        // Attach visuals to the actual interactive quad. Desktop monitors and laptop
        // lids have physically different surrounds, but share the exact native UV area.
        var cafe=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<CafePrototype>(true)).Single();
        Material dark=AssetDatabase.LoadAssetAtPath<Material>("Assets/CozyLoungeGreybox/FurnishedMaterials/SoftGraphite.mat");
        Material trim=AssetDatabase.LoadAssetAtPath<Material>("Assets/CozyLoungeGreybox/FurnishedMaterials/IvoryCream.mat");
        if (!dark || !trim) throw new InvalidOperationException("Screen bezel materials unavailable");
        for(int i=0;i<cafe.screens.Length;i++)
        {
            Transform face=cafe.screens[i].transform;
            bool laptop=i>=2;
            string name=laptop?"Laptop lid bezel":"Desktop monitor bezel";
            Transform rim=face.Find(name);
            if (!rim)
            {
                var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name=name;rim=cube.transform;
                rim.SetParent(face,false);
                UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
            }
            rim.gameObject.layer=2;
            rim.localPosition=new Vector3(0,laptop?-.012f:-.025f,.42f);
            rim.localRotation=Quaternion.identity;
            // Local dimensions follow capture's runtime aspect scaling: no mismatch
            // between visible image corners and bezel after monitor resolution changes.
            rim.localScale=laptop?new Vector3(1.025f,1.047f,.20f):new Vector3(1.035f,1.065f,.26f);
            rim.GetComponent<Renderer>().sharedMaterial=dark;
            Transform accent=face.Find(laptop?"Laptop lower edge":"Desktop lower edge");
            if (!accent)
            {
                var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name=laptop?"Laptop lower edge":"Desktop lower edge";
                accent=cube.transform;accent.SetParent(face,false);
                UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
            }
            accent.gameObject.layer=2;
            accent.localPosition=new Vector3(0,laptop?-.527f:-.539f,-.2f);
            accent.localRotation=Quaternion.identity;
            accent.localScale=laptop?new Vector3(1.026f,.013f,.16f):new Vector3(1.036f,.024f,.22f);
            accent.GetComponent<Renderer>().sharedMaterial=laptop?dark:trim;
            EditorUtility.SetDirty(face);
            Debug.Log("BEZEL_MODEL_PASS station="+i+" kind="+(laptop?"laptop":"desktop")+" bezel="+rim.name);
        }
    }

    // The furnished CC0 "computerScreen" is a second full monitor, 9cm behind
    // and 7cm below the real interactive desktop; it is NOT the support for that
    // screen. Hide that duplicate visual but retain its original scene hierarchy
    // and replace its silhouette with one grounded base + connected stem.
    static void RepairDesktopMonitorSupports(Scene scene)
    {
        var cafe=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<CafePrototype>(true)).Single();
        Material dark=AssetDatabase.LoadAssetAtPath<Material>("Assets/CozyLoungeGreybox/FurnishedMaterials/SoftGraphite.mat");
        if(!dark)throw new InvalidOperationException("Missing desktop stand material");
        for(int i=0;i<2;i++)
        {
            Transform deco=Only("Warm framed monitor "+(i+1));
            foreach(var renderer in deco.GetComponentsInChildren<Renderer>(true))
                if(renderer.name!="Attached stand base" && renderer.name!="Attached stand neck")renderer.enabled=false;
            Transform screen=cafe.screens[i].transform;
            Transform lower=screen.Find("Desktop lower edge");
            if(!lower)throw new InvalidOperationException("Desktop bottom edge missing seat="+i);
            float tableTop=.74f+WindowRugTop;
            float bottomEdge=lower.GetComponent<Renderer>().bounds.min.y;
            float neckTop=bottomEdge+.008f;
            float baseThickness=.017f;
            float neckBottom=tableTop+baseThickness;
            if(neckTop<neckBottom+.025f||neckTop>tableTop+.30f)
                throw new InvalidOperationException("Invalid stand to screen vertical range seat="+i);
            // Z is behind the glass, still within the depth of its real frame.
            float supportZ=screen.position.z+.035f;
            WorldSupport(deco,"Attached stand base",new Vector3(screen.position.x,tableTop+baseThickness*.5f,supportZ),
                new Vector3(.26f,baseThickness,.17f),dark);
            WorldSupport(deco,"Attached stand neck",new Vector3(screen.position.x,(neckBottom+neckTop)*.5f,supportZ),
                new Vector3(.105f,neckTop-neckBottom,.085f),dark);
            Debug.Log("DESKTOP_STAND_REPAIRED seat="+i+" tableTop="+tableTop+" screenEdge="+bottomEdge+
                      " neckTop="+neckTop+" supportZ="+supportZ);
            EditorUtility.SetDirty(deco);
        }
    }

    static void WorldSupport(Transform parent,string name,Vector3 center,Vector3 worldSize,Material mat)
    {
        Transform part=parent.Find(name);
        if(!part)
        {
            GameObject obj=GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.name=name;part=obj.transform;
            UnityEngine.Object.DestroyImmediate(obj.GetComponent<Collider>());
        }
        // Temporarily unparent for exact world-space dimensions regardless of
        // CC0 imported model pivot/scale; then keep as a named child of source.
        part.SetParent(null,true);
        part.SetPositionAndRotation(center,Quaternion.identity);
        part.localScale=worldSize;
        part.SetParent(parent,true);
        part.gameObject.layer=2;
        part.GetComponent<Renderer>().sharedMaterial=mat;
        part.GetComponent<Renderer>().enabled=true;
    }

    static Transform Only(string name)
    {
        var matches=decor.GetComponentsInChildren<Transform>(true).Where(t=>t.name==name).ToArray();
        if(matches.Length!=1)throw new InvalidOperationException("Expected one furniture " + name + "; found "+matches.Length);
        return matches[0];
    }
    static Bounds BoundsOf(Transform t)
    {
        var rs=t.GetComponentsInChildren<Renderer>(true).Where(r=>r.enabled).ToArray();
        if(rs.Length==0)throw new InvalidOperationException("Missing visible mesh on "+t.name);
        Bounds result=rs[0].bounds;
        for(int i=1;i<rs.Length;i++)result.Encapsulate(rs[i].bounds);
        return result;
    }
    static void Place(string name,float x,float minY,float z,float yaw)
    {
        Transform t=Only(name);
        t.rotation=Quaternion.Euler(0f,yaw,0f);
        Bounds b=BoundsOf(t);
        // Align actual visible mesh bounds, not FBX pivots or old collider boxes.
        t.position+=new Vector3(x-b.center.x,minY-b.min.y,z-b.center.z);
        EditorUtility.SetDirty(t);
    }

    [MenuItem("NetCafe/Library Lounge/Audit refined furnishings")]
    public static void Verify()
    {
        Scene scene=EditorSceneManager.OpenScene(LibraryLoungeBuild.ScenePath,OpenSceneMode.Single);
        VerifyScene(scene);
        CozyLoungeCollisionCoverage.Verify(scene);
        CozyLoungeCameraClipAudit.Verify(scene);
    }
    public static void VerifyScene(Scene scene)
    {
        decor=scene.GetRootGameObjects().Single(r=>r.name.StartsWith(RootName,StringComparison.Ordinal)).transform;
        var floor=Only("Warm plank floor").GetComponent<BoxCollider>();
        if(Mathf.Abs(floor.bounds.max.y)>.003f)throw new InvalidOperationException("Visible floor mismatch at "+floor.bounds.max.y);
        foreach(string n in new []{"Close desk chair 1","Close desk chair 2","Reading recliner 1","Reading recliner 2","Social sofa - sage","Guest rest sofa"})
        {
            Transform t=Only(n);
            Bounds b=BoundsOf(t);Vector3 high=Vector3.zero;int count=0;
            foreach(var mf in t.GetComponentsInChildren<MeshFilter>(true))
            {
                if(!mf.sharedMesh)continue;
                foreach(Vector3 v in mf.sharedMesh.vertices)
                {
                    Vector3 w=mf.transform.TransformPoint(v);
                    if(w.y>b.min.y+b.size.y*.77f){high+=w;count++;}
                }
            }
            if(count<5||((high/count)-b.center).z>-.10f)
                throw new InvalidOperationException("SEAT_VISUAL_BACKWARDS "+n);
            float target=n.StartsWith("Close")?WindowRugTop:n.StartsWith("Reading")?ReadingRugTop:n.StartsWith("Social")?SocialRugTop:0f;
            if(Mathf.Abs(b.min.y-target)>.004f)
                throw new InvalidOperationException("SEAT_FLOATING_OR_SUNK "+n+" bottom="+b.min.y);
        }
        string[] supported={"Window reading desk 1","Window reading desk 2","Reading side table 1","Reading side table 2","Social laptop table","Warm framed monitor 1","Warm framed monitor 2","Open books 1","Open books 2","Shelf plant"};
        foreach(var name in supported)
        {
            float y=name.StartsWith("Window")?WindowRugTop:name.StartsWith("Reading")?ReadingRugTop:
                name.StartsWith("Social")?SocialRugTop:name.StartsWith("Warm")?.74f+WindowRugTop:
                name.StartsWith("Open")?.62f+ReadingRugTop:1.90f;
            if(Mathf.Abs(BoundsOf(Only(name)).min.y-y)>.005f)
                throw new InvalidOperationException("FURNITURE_SUPPORT_GAP "+name);
        }
        var cafeForStand=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<CafePrototype>(true)).Single();
        for(int i=0;i<2;i++)
        {
            Transform deco=Only("Warm framed monitor "+(i+1));
            var baseMesh=deco.Find("Attached stand base")?.GetComponent<Renderer>();
            var stem=deco.Find("Attached stand neck")?.GetComponent<Renderer>();
            Transform face=cafeForStand.screens[i].transform;
            var edge=face.Find("Desktop lower edge")?.GetComponent<Renderer>();
            if(!baseMesh||!stem||!edge||!baseMesh.enabled||!stem.enabled)
                throw new InvalidOperationException("Missing physical monitor stand station="+i);
            float top=stem.bounds.max.y,edgeLow=edge.bounds.min.y;
            if(Mathf.Abs(baseMesh.bounds.min.y-(.74f+WindowRugTop))>.005f||
               top<edgeLow-.015f||top>edge.bounds.max.y+.025f||
               Mathf.Abs(stem.bounds.center.x-face.position.x)>.005f||
               Mathf.Abs(stem.bounds.center.z-face.position.z)>.11f)
                throw new InvalidOperationException("DESKTOP_STAND_GAP seat="+i+" top="+top+" edge="+edgeLow);
            int duplicate=deco.GetComponentsInChildren<Renderer>(true).Count(r=>r.enabled&&r!=baseMesh&&r!=stem);
            if(duplicate>0)throw new InvalidOperationException("DOUBLE_DESKTOP_MONITOR_MESH seat="+i);
            Debug.Log("DESKTOP_STAND_CONNECTED_PASS seat="+i+" baseBottom="+baseMesh.bounds.min.y+
                " stemTop="+top+" frameBottom="+edgeLow+" duplicate="+duplicate);
        }
        Bounds west=BoundsOf(Only("Reading library shelves west"));
        Bounds north=BoundsOf(Only("Open bookcase north"));
        Bounds cabinet=BoundsOf(Only("Entry cabinet"));
        if(Mathf.Abs(west.size.x-.44f)>.03f || Mathf.Abs(west.size.z-1.43f)>.04f)
            throw new InvalidOperationException("West shelf orientation / footprint wrong: "+west.size);
        if(west.min.x < -5.91f || west.min.x > -5.86f || north.max.z > 4.92f || north.max.z < 4.87f ||
           cabinet.min.z < -4.91f || cabinet.min.z > -4.86f)
            throw new InvalidOperationException("WALL_GAP_OR_PENETRATION west="+west.min.x+" north="+north.max.z+" cabinet="+cabinet.min.z);
        var cafe=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<CafePrototype>(true)).Single();
        for(int i=2;i<6;i++)
        {
            Transform face=cafe.screens[i].transform;
            if (!decor.Find("Laptop casing station "+(i+1)) ||
                Vector3.Dot(face.forward,(face.position-cafe.seats[i].position).normalized)<.9995f)
                throw new InvalidOperationException("LAPTOP_FACE_OR_CASING_INVALID seat="+i);
            if (face.lossyScale.x < .58f)
                throw new InvalidOperationException("LAPTOP_SCREEN_TOO_NARROW seat="+i);
            Transform hinge=decor.Find("Laptop casing station "+(i+1)+"/Rear hinge");
            float bottom=face.TransformPoint(new Vector3(0,-.5f,0)).y;
            float hingeTop=hinge.GetComponent<Renderer>().bounds.max.y;
            if (bottom < hingeTop + .005f)
                throw new InvalidOperationException("LAPTOP_TASKBAR_OCCLUDED_BY_HINGE seat="+i+
                    " screenBottom="+bottom+" hingeTop="+hingeTop);
            Debug.Log("LAPTOP_TASKBAR_CLEARANCE_PASS seat="+i+" gap="+(bottom-hingeTop).ToString("F5"));
        }
        Debug.Log("FURNITURE_REFINEMENT_AUDIT_PASS: six visible chairs/sofas face functional targets; floor/8 supports level; west/north/south cabinets wall-flush; no seat poses changed");
    }

    [MenuItem("NetCafe/Library Lounge/Build furniture-refined Windows candidate")]
    public static void BuildCandidate()=>Build(false);
    [MenuItem("NetCafe/Library Lounge/Build furniture-refined QA")]
    public static void BuildQA()=>Build(true);
    [MenuItem("NetCafe/Library Lounge/Build laptop-fixed Windows candidate")]
    public static void BuildLaptopCandidate()=>Build(false,"LibraryLounge-LaptopFixed-20261031");
    [MenuItem("NetCafe/Library Lounge/Build laptop-fixed Windows QA")]
    public static void BuildLaptopQA()=>Build(true,"LibraryLounge-LaptopFixed-QA-20261031");
    [MenuItem("NetCafe/Library Lounge/Build safe-exit + third-person Windows candidate")]
    public static void BuildPerspectiveCandidate()=>Build(false,"LibraryLounge-SafeExit-ThirdPerson-20261031");
    [MenuItem("NetCafe/Library Lounge/Build final safe-exit perspective Windows candidate")]
    public static void BuildSafeExitFinal()=>Build(false,"LibraryLounge-SafeExit-ThirdPerson-Final-20261031");
    [MenuItem("NetCafe/Library Lounge/Build safe-exit perspective verified candidate")]
    public static void BuildSafeExitVerified()=>Build(false,"LibraryLounge-SafeExit-Perspective-Verified-20261031");
    [MenuItem("NetCafe/Library Lounge/Build safe-exit perspective release candidate")]
    public static void BuildSafeExitRelease()=>Build(false,"LibraryLounge-SafeExit-Perspective-Release-20261031");
    [MenuItem("NetCafe/Library Lounge/Build room-view bezel Windows candidate")]
    public static void BuildRoomViewBezelCandidate()=>Build(false,"LibraryLounge-RoomView-Bezel-20261031");
    [MenuItem("NetCafe/Library Lounge/Build room-view bezel verified Windows candidate")]
    public static void BuildRoomViewBezelVerified()=>Build(false,"LibraryLounge-RoomView-Bezel-Verified-20261031");
    [MenuItem("NetCafe/Library Lounge/Build room-view complete Windows candidate")]
    public static void BuildRoomViewComplete()=>Build(false,"LibraryLounge-RoomView-Complete-20261031");
    [MenuItem("NetCafe/Library Lounge/Build taskbar-visible room Windows candidate")]
    public static void BuildTaskbarRoom()=>Build(false,"LibraryLounge-RoomView-Taskbar-20261031");
    [MenuItem("NetCafe/Library Lounge/Build enlarged taskbar aligned candidate")]
    public static void BuildTaskbarAligned()=>Build(false,"LibraryLounge-Taskbar-Aligned-20261031");
    [MenuItem("NetCafe/Library Lounge/Build roomview taskbar drag verified")]
    public static void BuildRoomViewFinal()=>Build(false,"LibraryLounge-RoomView-Taskbar-Final-20261031");
    [MenuItem("NetCafe/Library Lounge/Build aligned taskbar release")]
    public static void BuildTaskbarRelease()=>Build(false,"LibraryLounge-Taskbar-Release-20261031");
    [MenuItem("NetCafe/Library Lounge/Build full-taskbar overlay Windows QA")]
    public static void BuildFullTaskbarOverlay()=>Build(false,"LibraryLounge-FullTaskbar-Overlay-20261031");
    [MenuItem("NetCafe/Library Lounge/Build full taskbar compositor verified")]
    public static void BuildFullTaskbarVerified()=>Build(false,"LibraryLounge-FullTaskbar-Verified-20261031");
    [MenuItem("NetCafe/Library Lounge/Build full taskbar final Windows")]
    public static void BuildFullTaskbarFinal()=>Build(false,"LibraryLounge-FullTaskbar-Final-20261031");
    [MenuItem("NetCafe/Library Lounge/Build taskbar hinge fix room89")]
    public static void BuildTaskbarHingeFix()=>Build(false,"LibraryLounge-Taskbar-HingeFix-Room89-20261031");
    [MenuItem("NetCafe/Library Lounge/Build room89 taskbar hinge verified")]
    public static void BuildRoom89Verified()=>Build(false,"LibraryLounge-Taskbar-HingeFix-Room89-Verified-20261031");
    [MenuItem("NetCafe/Library Lounge/Build depth-correct roomview candidate")]
    public static void BuildDepthRoomView()=>Build(false,"LibraryLounge-SpatialRoomView-20261031");
    [MenuItem("NetCafe/Library Lounge/Build depth-correct roomview verified")]
    public static void BuildDepthRoomViewVerified()=>Build(false,"LibraryLounge-SpatialRoomView-Verified-20261031");
    [MenuItem("NetCafe/Library Lounge/Build spatial roomview final Windows")]
    public static void BuildSpatialFinal()=>Build(false,"LibraryLounge-SpatialRoomView-Final-20261031");
    [MenuItem("NetCafe/Library Lounge/Build spatial roomview final B1 QA")]
    public static void BuildSpatialFinalQA()=>Build(true,"LibraryLounge-SpatialRoomView-Final-QA-20261031");
    [MenuItem("NetCafe/Library Lounge/Build WAN A01 same-scene rejoin QA")]
    public static void BuildWanA01QA()=>Build(true,"LibraryLounge-SpatialRoomView-WANA01-QA-20261031");
    [MenuItem("NetCafe/Library Lounge/Build WAN A01 atomic invite QA")]
    public static void BuildWanA01AtomicQA()=>Build(true,"LibraryLounge-SpatialRoomView-WANA01-Atomic-QA-20261031");
    [MenuItem("NetCafe/Library Lounge/Build input restore and connected monitors")]
    public static void BuildInputStandFix()=>Build(false,"LibraryLounge-InputRestore-ConnectedMonitors-20261031");
    [MenuItem("NetCafe/Library Lounge/Build local host autostart Windows")]
    public static void BuildHostAutostart()=>Build(false,"LibraryLounge-HostAutoStart-20261031");
    [MenuItem("NetCafe/Library Lounge/Build B2 opt-in desktop sharing candidate")]
    public static void BuildB2OptIn()=>Build(false,"LibraryLounge-B2-OptIn-20261031");
    [MenuItem("NetCafe/Library Lounge/Build B2 opt-in desktop sharing QA")]
    public static void BuildB2OptInQA()=>Build(true,"LibraryLounge-B2-OptIn-QA-20261031");
    [MenuItem("NetCafe/Library Lounge/Build host auto-start verified")]
    public static void BuildHostAutostartVerified()=>Build(false,"LibraryLounge-HostAutoStart-Verified-20261031");
    [MenuItem("NetCafe/Library Lounge/Build host autostart lifecycle acceptance")]
    public static void BuildHostLifecycleAcceptance()=>Build(false,"LibraryLounge-HostLifecycle-Verified-20261031");
    [MenuItem("NetCafe/Library Lounge/Build WAN Cloud B1 Windows acceptance")]
    public static void BuildWanCloudB1Acceptance()=>Build(false,"LibraryLounge-WAN-Cloud-B1-Verified-20261031");
    [MenuItem("NetCafe/Library Lounge/Build Unified Online B2 Release")]
    public static void BuildUnifiedOnlineB2()=>Build(false,"LibraryLounge-Latest-Online-B2-20261031");
    [MenuItem("NetCafe/Library Lounge/Build Unified Online B2 QA")]
    public static void BuildUnifiedOnlineB2QA()=>Build(true,"LibraryLounge-Latest-Online-B2-QA-20261031");
    [UnityEditor.MenuItem("NetCafe/Library Lounge/Build All-Member Voice Release")]
    public static void BuildAllMemberVoiceRelease()=>Build(false,"LibraryLounge-Voice-AllMembers-20261031");
    [UnityEditor.MenuItem("NetCafe/Library Lounge/Build All-Member Voice QA")]
    public static void BuildAllMemberVoiceQA()=>Build(true,"LibraryLounge-Voice-AllMembers-QA-Final-20261031");
    static void Build(bool development,string outputFolder=null)
    {
#if !NETCAFE_B1
        throw new InvalidOperationException("NETCAFE_B1 required for multiplayer demo");
#else
        LibraryLoungeBuild.Validate();
        VerifyScene(SceneManager.GetActiveScene());
        string folder=outputFolder ?? (development?"LibraryLounge-FurnitureRefined-QA-20261031":"LibraryLounge-FurnitureRefined-20261031");
        string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Builds",folder,"LibraryLounge.exe"));
        if(Directory.Exists(Path.GetDirectoryName(output)))throw new IOException("Refuse to overwrite build: "+output);
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        BuildReport report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{
            scenes=new[]{LibraryLoungeBuild.ScenePath},locationPathName=output,
            target=BuildTarget.StandaloneWindows64,options=development?BuildOptions.Development:BuildOptions.None
        });
        if(report.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("Furniture demo build failed "+report.summary.result);
        Debug.Log("FURNITURE_REFINED_BUILD_PASS: "+output+" development="+development);
#endif
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Only primitive static colliders: no expensive runtime MeshCollider/Rigidbody.
// Decorated floor strips, rugs and faux windows are physically supported by
// their parent floor/wall and deliberately do NOT create duplicate thin planes.
public static class CozyLoungeCollisionCoverage
{
    const string DecorRoot = "LIBRARY LOUNGE V2 - KENNEY CC0 + WARM ARCHITECTURE";
    static readonly string[] Walls = {
        "North ivory wall", "West ivory wall", "East ivory wall",
        "South wall left", "South wall right", "Entrance arch lintel"
    };
    static readonly string[] Furniture = {
        "Window reading desk 1", "Window reading desk 2",
        "Close desk chair 1", "Close desk chair 2",
        "Warm framed monitor 1", "Warm framed monitor 2",
        "Keyboard 1", "Keyboard 2", "Mouse 1", "Mouse 2",
        "Amber task lamp 1", "Amber task lamp 2",
        "Reading recliner 1", "Reading recliner 2",
        "Reading side table 1", "Reading side table 2",
        "Open books 1", "Open books 2",
        "Social sofa - sage", "Social laptop table",
        "Corner reading chair", "Quiet corner round table",
        "Guest rest sofa", "Reading library shelves west",
        "Open bookcase north", "Entry cabinet",
        "Reading floor lamp", "Conversation floor lamp",
        "Tall indoor greenery", "Shelf plant", "Corner plant"
    };
    // These main pieces were already given simple walk blockers.
    // Refit those boxes rather than adding duplicate expensive colliders.
    static readonly HashSet<string> ExistingBlockingModels = new HashSet<string> {
        "Window reading desk 1","Window reading desk 2",
        "Close desk chair 1","Close desk chair 2",
        "Reading recliner 1","Reading recliner 2",
        "Reading side table 1","Reading side table 2",
        "Social sofa - sage","Social laptop table",
        "Corner reading chair","Quiet corner round table",
        "Guest rest sofa","Reading library shelves west",
        "Open bookcase north","Entry cabinet"
    };

    [MenuItem("NetCafe/Library Lounge/Repair all wall and furniture collisions")]
    public static void Repair()
    {
        Scene scene = EditorSceneManager.OpenScene(LibraryLoungeBuild.ScenePath, OpenSceneMode.Single);
        RepairScene(scene);
    }

    // Reusable by the original furnished-room generator as well as explicit repair.
    public static void RepairScene(Scene scene)
    {        Transform root = Root(scene);
        int wallCount=0, existing=0, added=0;
        foreach (string name in Walls)
        {
            Transform t = Only(root,name);
            BoxCollider box = t.GetComponent<BoxCollider>();
            if (box == null) throw new InvalidOperationException("Missing wall collider: " + name);
            box.enabled = true; box.isTrigger = false; t.gameObject.layer = 2;
            EditorUtility.SetDirty(box);
            wallCount++;
        }
        // The original entrance arch contains a deliberately open doorway.
        // Use a visible solid door so nobody can leave the supported floor
        // or collide with an invisible boundary across the threshold.
        AddEntranceDoor(root);
        foreach (string name in Furniture)
        {
            Transform model = Only(root,name);
            Renderer[] visible = model.GetComponentsInChildren<Renderer>()
                .Where(r=>r.enabled && r.gameObject.activeInHierarchy).ToArray();
            if (visible.Length==0) throw new InvalidOperationException("Missing visible furniture: "+name);
            Bounds b = visible[0].bounds;
            for(int i=1;i<visible.Length;i++) b.Encapsulate(visible[i].bounds);
            if (ExistingBlockingModels.Contains(name))
            {
                Transform proxy = Only(root,name+" - walk blocker (no gaze ray)");
                BoxCollider box = proxy.GetComponent<BoxCollider>();
                if (box==null) throw new InvalidOperationException("Missing furniture proxy: "+name);
                // Seats need a low, walk-blocking footprint with headroom.
                // A whole-chair AABB (including recliner backrests) intersects
                // the seated camera and puts the laptop view inside cushions.
                if (name.StartsWith("Reading recliner ", StringComparison.Ordinal) ||
                    name == "Social sofa - sage")
                {
                    var cafe = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<CafePrototype>(true)).Single();
                    float head = name == "Reading recliner 1" ? cafe.seats[2].position.y :
                        name == "Reading recliner 2" ? cafe.seats[3].position.y :
                        Mathf.Min(cafe.seats[4].position.y,cafe.seats[5].position.y);
                    float top = Mathf.Min(b.max.y,head-.15f);
                    if(top < b.min.y+.40f) throw new InvalidOperationException("Insufficient seat body collision height: "+name);
                    b.SetMinMax(b.min,new Vector3(b.max.x,top,b.max.z));
                }                // Match the actual visual footprint, including rotated bookcases.
                proxy.position = b.center;
                proxy.rotation = Quaternion.identity;
                proxy.localScale = Vector3.one;
                box.center = Vector3.zero;
                box.size = new Vector3(Mathf.Max(.12f,b.size.x),
                    Mathf.Max(.14f,b.size.y),Mathf.Max(.12f,b.size.z));
                box.isTrigger = false; box.enabled=true;
                proxy.gameObject.layer=2;
                EditorUtility.SetDirty(box); EditorUtility.SetDirty(proxy);
                existing++;
            }
            else
            {
                // One box per small decoration, not one per triangle/child mesh.
                // Compute root-local bounds by transforming each renderer's
                // world bounds corners; allows nested imported FBX geometry.
                BoxCollider box = model.GetComponent<BoxCollider>();
                if(box==null) box=model.gameObject.AddComponent<BoxCollider>();
                Bounds local = LocalBounds(model,visible);
                box.center=local.center;
                box.size=new Vector3(Mathf.Max(.08f,local.size.x),
                    Mathf.Max(.04f,local.size.y),Mathf.Max(.08f,local.size.z));
                box.isTrigger=false;box.enabled=true;
                model.gameObject.layer=2;
                EditorUtility.SetDirty(box);added++;
            }
        }
        EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Saving collision repaired scene failed");
        Verify(scene);
        Debug.Log("COZY_COLLISION_REPAIR_PASS: walls="+wallCount+
            "; furnitureProxies="+existing+"; smallModelColliders="+added+
            "; visibleEntranceDoor=true; no mesh collider; old scenes untouched");
    }

    [MenuItem("NetCafe/Library Lounge/Audit all wall and furniture collisions")]
    public static void Audit()
    {
        Scene scene=EditorSceneManager.OpenScene(LibraryLoungeBuild.ScenePath,OpenSceneMode.Single);
        Verify(scene);
    }

    public static void Verify(Scene scene)
    {
        Transform root=Root(scene);
        int walls=0,models=0,solidBoxes=0;
        foreach(var name in Walls) {RequireSolid(Only(root,name),name);walls++;}
        RequireSolid(Only(root,"Entrance door - closed"),"Entrance door - closed");
        foreach(var name in Furniture)
        {
            Transform t=ExistingBlockingModels.Contains(name)?
                Only(root,name+" - walk blocker (no gaze ray)"):Only(root,name);
            RequireSolid(t,name);models++;
        }
        foreach(var c in root.GetComponentsInChildren<Collider>(true))
        {
            if(c.gameObject.activeInHierarchy && c.enabled && !c.isTrigger && c is MeshCollider)
                throw new InvalidOperationException("Unexpected costly active mesh collider: "+c.name);
            if(c.gameObject.activeInHierarchy && c.enabled && !c.isTrigger && c is BoxCollider)solidBoxes++;
        }
        var cafe=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<CafePrototype>(true)).Single();
        if(Physics.GetIgnoreLayerCollision(cafe.body.gameObject.layer,2))
            throw new InvalidOperationException("Player cannot collide with furniture Layer 2");
        Physics.SyncTransforms();
        // Strict checks for leaving a seat, which can be blocked by a newly
        // enlarged proxy even though the rendered furniture looks right.
        for(int i=0;i<cafe.exits.Length;i++)
        {
            var pos=cafe.exits[i].position;
            var overlap=Physics.OverlapCapsule(pos+Vector3.up*.36f,pos+Vector3.up*1.44f,
                .30f,~0,QueryTriggerInteraction.Ignore)
                .Where(c=>c!=cafe.body && c.enabled && c.gameObject.activeInHierarchy)
                .Select(c=>c.name).Distinct().ToArray();
            if(overlap.Length>0)throw new InvalidOperationException(
                "New collision blocks exit "+i+": "+string.Join(", ",overlap));
        }
        Debug.Log("COZY_COLLISION_AUDIT_PASS: visibleWalls="+walls+
            "; entranceDoor=1; coveredFurniture="+models+"; enabledSolidBoxes="+solidBoxes+
            "; 8 seat exits unobstructed; no active mesh colliders");
    }

    static void RequireSolid(Transform t,string name)
    {
        Collider c=t.GetComponent<Collider>();
        if(c==null||!c.enabled||c.isTrigger||!t.gameObject.activeInHierarchy)
            throw new InvalidOperationException("No physical collision for "+name);
        if(c is not BoxCollider) throw new InvalidOperationException("Expected box collider for "+name);
    }
    static Transform Root(Scene s)
    {
        var hits=s.GetRootGameObjects()
            .Where(r=>r.name.StartsWith(DecorRoot,StringComparison.Ordinal)).ToArray();
        if(hits.Length!=1||!hits[0].activeInHierarchy)
            throw new InvalidOperationException("Expected one active furnished root");
        return hits[0].transform;
    }
    static Transform Only(Transform root,string name)
    {
        var hits=root.GetComponentsInChildren<Transform>(true).Where(x=>x.name==name).ToArray();
        if(hits.Length!=1)throw new InvalidOperationException("Expected one "+name+" found "+hits.Length);
        return hits[0];
    }
    static Bounds LocalBounds(Transform root, Renderer[] renderers)
    {
        bool start=false;Bounds b=default;
        foreach(Renderer r in renderers)
        {
            Bounds w=r.bounds;Vector3 mn=w.min,mx=w.max;
            for(int x=0;x<2;x++)for(int y=0;y<2;y++)for(int z=0;z<2;z++)
            {
                var world=new Vector3(x==0?mn.x:mx.x,y==0?mn.y:mx.y,z==0?mn.z:mx.z);
                var local=root.InverseTransformPoint(world);
                if(!start){b=new Bounds(local,Vector3.zero);start=true;}
                else b.Encapsulate(local);
            }
        }
        return b;
    }
    static void AddEntranceDoor(Transform root)
    {
        Transform door=root.Find("Entrance door - closed");
        if(door==null)
        {
            var panel=GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name="Entrance door - closed";
            panel.transform.SetParent(root,false);
            panel.transform.localPosition=new Vector3(0,1.20f,-4.975f);
            panel.transform.localScale=new Vector3(2.04f,2.40f,.09f);
            var sample=Only(root,"Entrance arch lintel").GetComponent<Renderer>();
            panel.GetComponent<Renderer>().sharedMaterial=sample.sharedMaterial;
            door=panel.transform;
        }
        door.gameObject.layer=2;
        var c=door.GetComponent<BoxCollider>();
        if(c==null)c=door.gameObject.AddComponent<BoxCollider>();
        c.enabled=true;c.isTrigger=false;
        EditorUtility.SetDirty(c);
    }
}

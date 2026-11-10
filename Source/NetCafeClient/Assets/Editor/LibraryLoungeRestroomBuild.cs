using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEditor.Build.Reporting;

public static class LibraryLoungeRestroomBuild
{
    public const string ArtScene="Assets/ArtV3/Scenes/Restroom-GridSkylight-20261105.unity";
    public const string ScenePath="Assets/ArtV3/Scenes/Restroom_Playable_v013.unity";
    static string Work=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../.."));
    [Serializable] class Node
    {
        public string path,name;public Vector3 position,rotation,scale,boundsCenter,boundsSize;
        public string[] materials,components;public bool active;
    }
    [Serializable] class Audit {public Node[] art,controllers;public string[] roots;}
    static string Hierarchy(Transform t)=>t.parent==null?t.name:Hierarchy(t.parent)+"/"+t.name;
    static Node Describe(Transform t)
    {
        var r=t.GetComponent<Renderer>();
        return new Node{path=Hierarchy(t),name=t.name,position=t.position,rotation=t.eulerAngles,scale=t.lossyScale,
            boundsCenter=r?r.bounds.center:Vector3.zero,boundsSize=r?r.bounds.size:Vector3.zero,
            materials=r?r.sharedMaterials.Select(m=>m?m.name:"MISSING").ToArray():Array.Empty<string>(),
            components=t.GetComponents<Component>().Select(c=>c?c.GetType().Name:"MISSING").ToArray(),active=t.gameObject.activeSelf};
    }
    public static void Inspect()
    {
        var art=EditorSceneManager.OpenScene(ArtScene,OpenSceneMode.Single);
        var artNodes=art.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).Select(Describe).ToArray();
        var old=EditorSceneManager.OpenScene(LibraryLoungeBuild.ScenePath,OpenSceneMode.Additive);
        var cafe=old.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<CafePrototype>(true)).Single();
        var data=new Audit{art=artNodes,roots=old.GetRootGameObjects().Select(r=>r.name).ToArray(),controllers=cafe.transform.root.GetComponentsInChildren<Transform>(true).Select(Describe).ToArray()};
        File.WriteAllText(Path.Combine(Work,"Docs/SCENE-AUDIT.json"),JsonUtility.ToJson(data,true));
        Debug.Log("RESTROOM_INSPECT_PASS artNodes="+artNodes.Length+" controller="+Hierarchy(cafe.transform));
    }
    static GameObject Find(Scene scene,string name)=>scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).Single(t=>t.name==name).gameObject;
    static Bounds WorldBounds(GameObject go)
    {
        var rs=go.GetComponentsInChildren<Renderer>();var b=rs[0].bounds;
        foreach(var r in rs)b.Encapsulate(r.bounds);return b;
    }
    static Transform NodeAt(string name,Transform parent,Vector3 position,Quaternion rotation)
    {
        var t=new GameObject(name).transform;t.SetParent(parent,false);t.SetPositionAndRotation(position,rotation);return t;
    }
    static Renderer Box(string name,Transform parent,Vector3 position,Vector3 size,Material material)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);
        go.transform.localPosition=position;go.transform.localScale=size;UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        var renderer=go.GetComponent<Renderer>();renderer.sharedMaterial=material;return renderer;
    }
    static (Vector3 center,Vector3 normal,float height,float minY) ScreenPlane(MeshFilter filter,Renderer renderer,Vector3 viewer)
    {
        var mesh=filter.sharedMesh;var vertices=mesh.vertices;var points=new List<Vector3>();Vector3 normal=Vector3.forward;float largest=0;
        for(int s=0;s<mesh.subMeshCount;s++)
        {
            if(!renderer.sharedMaterials[s].name.Contains("Screen"))continue;
            var triangles=mesh.GetTriangles(s);
            for(int i=0;i<triangles.Length;i+=3)
            {
                var a=filter.transform.TransformPoint(vertices[triangles[i]]);var b=filter.transform.TransformPoint(vertices[triangles[i+1]]);var c=filter.transform.TransformPoint(vertices[triangles[i+2]]);
                points.Add(a);points.Add(b);points.Add(c);var cross=Vector3.Cross(b-a,c-a);
                if(cross.sqrMagnitude>largest){largest=cross.sqrMagnitude;normal=cross.normalized;}
            }
        }
        if(points.Count==0)throw new Exception("No actual screen submesh: "+filter.name);
        var bounds=new Bounds(points[0],Vector3.zero);foreach(var p in points)bounds.Encapsulate(p);
        Vector3 center=bounds.center;if(Vector3.Dot(normal,center-viewer)<0)normal=-normal;
        var up=Quaternion.LookRotation(normal,Vector3.up)*Vector3.up;
        return(center,normal,points.Max(p=>Vector3.Dot(p,up))-points.Min(p=>Vector3.Dot(p,up)),bounds.min.y);
    }
    static void KeepComputerBase(MeshFilter filter,float cutoff,int station)
    {
        var original=filter.sharedMesh;var mesh=UnityEngine.Object.Instantiate(original);mesh.name="RestroomComputerBase_"+station;
        var vertices=mesh.vertices;
        for(int s=0;s<mesh.subMeshCount;s++)
        {
            var tris=mesh.GetTriangles(s);var keep=new List<int>();
            for(int i=0;i<tris.Length;i+=3)
                if(filter.transform.TransformPoint(vertices[tris[i]]).y<=cutoff&&filter.transform.TransformPoint(vertices[tris[i+1]]).y<=cutoff&&filter.transform.TransformPoint(vertices[tris[i+2]]).y<=cutoff)
                {keep.Add(tris[i]);keep.Add(tris[i+1]);keep.Add(tris[i+2]);}
            mesh.SetTriangles(keep,s);
        }
        var used=Enumerable.Range(0,mesh.subMeshCount).SelectMany(s=>mesh.GetTriangles(s)).Distinct().ToArray();
        if(used.Length==0)throw new Exception("Computer base extraction is empty: "+station);
        var bounds=new Bounds(vertices[used[0]],Vector3.zero);foreach(int v in used)bounds.Encapsulate(vertices[v]);mesh.bounds=bounds;
        AssetDatabase.CreateAsset(mesh,"Assets/ArtV3/Models/Playable013/ComputerBase_"+station+".asset");filter.sharedMesh=mesh;
        filter.gameObject.isStatic=false;filter.GetComponent<Renderer>().lightmapIndex=-1;
    }
    static Transform AddSeat(GameObject chair,int index,Transform parent,Vector3 eye,Vector3 facing,bool computer)
    {
        var rotation=Quaternion.LookRotation(Vector3.ProjectOnPlane(facing,Vector3.up),Vector3.up);
        var seat=NodeAt("Seat viewpoint "+index,parent,eye,rotation);
        var b=WorldBounds(chair);
        var interaction=NodeAt((computer?"ComputerSeatInteraction_":"SofaInteraction_")+index,parent,b.center,rotation);
        var action=interaction.gameObject.AddComponent<CafeInteractable>();action.action=computer?CafeInteractable.Action.Seat:CafeInteractable.Action.Sofa;action.seatIndex=index;action.label=computer?"坐下使用电脑":"坐下休息";
        var trigger=interaction.gameObject.AddComponent<BoxCollider>();trigger.isTrigger=true;trigger.size=new Vector3(.9f,Mathf.Min(1.2f,b.size.y),.9f);
        chair.transform.SetParent(interaction,true);
        return seat;
    }
    public static void Integrate()
    {
        string modelDir=Path.Combine(Application.dataPath,"ArtV3/Models/Playable013");
        if(Directory.Exists(modelDir)||File.Exists(ScenePath))throw new Exception("Refusing to overwrite the integrated scene; inspect the existing candidate first.");
        Directory.CreateDirectory(modelDir);AssetDatabase.Refresh();
        // Mesh edits remain in new assets; FBX source files and the art scene stay unchanged.
        foreach(string id in new[]{"monitor_27","laptop_13","screen_wall_5m"})
        {
            var importer=(ModelImporter)AssetImporter.GetAtPath("Assets/ArtV3/Models/"+id+".fbx");importer.isReadable=true;importer.SaveAndReimport();
        }
        var scene=EditorSceneManager.OpenScene(ArtScene,OpenSceneMode.Single);
        foreach(var camera in scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Camera>(true)))camera.gameObject.SetActive(false);
        var old=EditorSceneManager.OpenScene(LibraryLoungeBuild.ScenePath,OpenSceneMode.Additive);
        var cafe=old.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<CafePrototype>(true)).Single();
        var keep=cafe.transform.Cast<Transform>().Where(t=>cafe.body.transform.IsChildOf(t)||t.GetComponent<Canvas>()!=null||t.GetComponent<UnityEngine.EventSystems.EventSystem>()!=null).ToArray();
        foreach(var child in cafe.transform.Cast<Transform>().ToArray())if(!keep.Contains(child))UnityEngine.Object.DestroyImmediate(child.gameObject);
        SceneManager.MoveGameObjectToScene(cafe.gameObject,scene);EditorSceneManager.CloseScene(old,true);SceneManager.SetActiveScene(scene);cafe.name="Restroom Gameplay";
        var layout=cafe.gameObject.AddComponent<CafeRestroomLayout>();layout.cafe=cafe;layout.capture=cafe.GetComponent<CafeDesktopCapture>();layout.displays=new CafeMonitorFrame[6];
        cafe.walkableFloor=Find(scene,"Floor").GetComponent<Collider>();
        var sun=Find(scene,"GS_Sun").GetComponent<Light>();cafe.roomLight=sun;
        cafe.seats=new Transform[8];cafe.exits=new Transform[8];cafe.screens=new Renderer[6];cafe.seatedVisualRoots=new Transform[6];
        var bindings=NodeAt("Playable station bindings",cafe.transform,Vector3.zero,Quaternion.identity);
        string[] computers={"monitor_ne_0","monitor_ne_1","laptop_nw_2","laptop_nw_3","laptop_sw_4","laptop_sw_5"};
        string[] chairs={"chair_ne_0","chair_ne_1","reading_olive_chair_2","reading_olive_chair_3","social_cream_chair_4","social_cream_chair_5"};
        var bezel=new Material(Shader.Find("Universal Render Pipeline/Lit")){name="Restroom monitor narrow charcoal frame"};bezel.color=new Color(.055f,.052f,.045f);AssetDatabase.CreateAsset(bezel,"Assets/ArtV3/Models/Playable013/MonitorFrame.mat");
        float aspect=(float)Screen.currentResolution.width/Mathf.Max(1,Screen.currentResolution.height);
        for(int i=0;i<6;i++)
        {
            var chair=Find(scene,chairs[i]);var chairBounds=WorldBounds(chair);Vector3 eye=chairBounds.center;eye.y=chairBounds.min.y+chairBounds.size.y*.47f+.65f;
            var computer=Find(scene,computers[i]);var filter=computer.GetComponentInChildren<MeshFilter>();var renderer=filter.GetComponent<Renderer>();
            var plane=ScreenPlane(filter,renderer,eye);var rotation=Quaternion.LookRotation(plane.normal,Vector3.up);var up=rotation*Vector3.up;
            var originalVertices=filter.sharedMesh.vertices.Select(filter.transform.TransformPoint).ToArray();float bodyWidth=originalVertices.Max(p=>Vector3.Dot(p,rotation*Vector3.right))-originalVertices.Min(p=>Vector3.Dot(p,rotation*Vector3.right));
            float baseY=renderer.bounds.min.y;
            KeepComputerBase(filter,i<2?plane.minY-.035f:baseY+.032f,i);
            var center=i<2?plane.center:plane.center+up*((CafeMonitorFrame.ImageHeight-plane.height)*.5f);
            var rig=NodeAt("MonitorRig_"+i,bindings,center,rotation).gameObject.AddComponent<CafeMonitorFrame>();
            rig.frame=Box("Monitor frame",rig.transform,new Vector3(0,0,.006f),Vector3.one,bezel).transform;
            var image=Box("Screen_"+i,rig.transform,Vector3.zero,Vector3.one,cafe.idleScreen);rig.image=image.transform;
            if(i>=2){rig.laptopBody=filter.transform;rig.bodyScale=filter.transform.localScale;rig.bodyWidth=bodyWidth;}
            rig.Fit(aspect);layout.displays[i]=rig;cafe.screens[i]=image;
            cafe.seats[i]=AddSeat(chair,i,bindings,eye,center-eye,true);cafe.seatedVisualRoots[i]=chair.transform;
            cafe.exits[i]=NodeAt("Seat exit "+i,bindings,chairBounds.center+Vector3.ProjectOnPlane(center-eye,Vector3.up).normalized*1.5f+Vector3.up*.05f,Quaternion.identity);cafe.exits[i].position=new Vector3(cafe.exits[i].position.x,.05f,cafe.exits[i].position.z);
        }
        var sofa=Find(scene,"cinema_sofa");var cinemaChair=Find(scene,"cinema_chair_olive");
        cafe.seats[6]=AddSeat(sofa,6,bindings,new Vector3(2.1f,1.25f,-.6f),Vector3.right,false);
        cafe.seats[7]=AddSeat(cinemaChair,7,bindings,WorldBounds(cinemaChair).center+Vector3.up*.5f,Vector3.right,false);
        cafe.exits[6]=NodeAt("Seat exit 6",bindings,new Vector3(.0f,.05f,1.0f),Quaternion.identity);cafe.exits[7]=NodeAt("Seat exit 7",bindings,new Vector3(7.8f,.05f,-3.8f),Quaternion.identity);
        // Mesh collision follows actual furniture, leaving chair access and table legs open.
        foreach(var renderer in scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<MeshRenderer>(true)).ToArray())
        {
            if(!renderer.gameObject.activeInHierarchy||renderer.transform.IsChildOf(cafe.transform)&&!renderer.transform.IsChildOf(bindings))continue;
            if(!Hierarchy(renderer.transform).Contains("LoungeFurniture_20261103")&&!cafe.seatedVisualRoots.Any(t=>t!=null&&renderer.transform.IsChildOf(t))&&!renderer.transform.IsChildOf(sofa.transform)&&!renderer.transform.IsChildOf(cinemaChair.transform))continue;
            if(renderer.GetComponent<Collider>()==null){var filter=renderer.GetComponent<MeshFilter>();if(filter&&filter.sharedMesh)renderer.gameObject.AddComponent<MeshCollider>().sharedMesh=filter.sharedMesh;}
        }
        var publicModel=Find(scene,"public_screen").GetComponentInChildren<MeshFilter>();var publicPlane=ScreenPlane(publicModel,publicModel.GetComponent<Renderer>(),new Vector3(2,1.5f,0));
        var publicRig=NodeAt("Public display",bindings,publicPlane.center-publicPlane.normal*.01f,Quaternion.LookRotation(publicPlane.normal,Vector3.up));
        cafe.publicScreen=Box("Public screen",publicRig,Vector3.zero,new Vector3(publicPlane.height*16f/9f,publicPlane.height,.002f),cafe.idleScreen);
        var publicAction=cafe.publicScreen.gameObject.AddComponent<CafeInteractable>();publicAction.action=CafeInteractable.Action.Display;publicAction.label="公共大屏";var publicHit=cafe.publicScreen.gameObject.AddComponent<BoxCollider>();publicHit.isTrigger=true;
        cafe.body.enabled=false;cafe.body.transform.position=new Vector3(0,.06f,-5.5f);cafe.body.transform.rotation=Quaternion.identity;cafe.body.enabled=true;
        cafe.eyes.clearFlags=CameraClearFlags.Skybox;cafe.eyes.nearClipPlane=.03f;cafe.eyes.farClipPlane=150f;
        PlayerSettings.bundleVersion="0.1.3";EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true)};
        EditorUtility.SetDirty(cafe);EditorSceneManager.SaveScene(scene,ScenePath);AssetDatabase.SaveAssets();
        WritePlayableAudit(scene);Debug.Log("RESTROOM_INTEGRATION_PASS six computers; eight seats; original art preserved");
    }
    static void WritePlayableAudit(Scene scene)
    {
        var data=new Audit{art=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).Select(Describe).ToArray(),roots=scene.GetRootGameObjects().Select(r=>r.name).ToArray()};
        File.WriteAllText(Path.Combine(Work,"Docs/PLAYABLE-AUDIT.json"),JsonUtility.ToJson(data,true));
    }
    public static void Refine()
    {
        var scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
        var cafe=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<CafePrototype>(true)).Single();
        var sync=cafe.GetComponent<CafeRoomSync>();
        sync.roomMinimum=new Vector3(-9.15f,-.2f,-7.575f);
        sync.roomMaximum=new Vector3(9.15f,2.5f,7.575f);
        EditorUtility.SetDirty(sync);
        for(int i=0;i<2;i++)
        {
            var rig=Find(scene,"MonitorRig_"+i).GetComponent<CafeMonitorFrame>();
            if(rig.transform.Find("Monitor neck")!=null)continue;
            var baseRenderer=Find(scene,"monitor_ne_"+i+"_ImportedFBX").GetComponent<Renderer>();
            float bottom=rig.frame.GetComponent<Renderer>().bounds.min.y;
            float top=baseRenderer.bounds.max.y;
            var neck=Box("Monitor neck",rig.transform,Vector3.zero,new Vector3(.045f,bottom-top+.024f,.10f),rig.frame.GetComponent<Renderer>().sharedMaterial);
            neck.transform.position=new Vector3(baseRenderer.bounds.center.x,(bottom+top)*.5f,baseRenderer.bounds.center.z);
        }
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        WritePlayableAudit(scene);Debug.Log("RESTROOM_REFINE_PASS scene bounds and connected monitor necks");
    }
    public static void Build()
    {
        var scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
        var cafe=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<CafePrototype>(true)).Single();
        if(cafe.screens.Length!=6||cafe.seats.Length!=8||cafe.walkableFloor==null||cafe.screens.Any(s=>s==null))throw new Exception("Incomplete station bindings");
        if(scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<MonoBehaviour>(true)).Any(c=>c==null))throw new Exception("Missing scene scripts");
        PlayerSettings.bundleVersion="0.1.3";
        string output=Path.Combine(LibraryLoungeUiFixBuild.FindRoot(),"Builds/LibraryLounge-v0.1.3-Restroom-20261107");Directory.CreateDirectory(output);
        var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{ScenePath},target=BuildTarget.StandaloneWindows64,locationPathName=Path.Combine(output,"LibraryLounge.exe"),options=BuildOptions.Development});
        if(result.summary.result!=BuildResult.Succeeded)throw new Exception("RESTROOM_BUILD_FAIL "+result.summary.result);
        Debug.Log("RESTROOM_BUILD_PASS "+output);
    }
}

using System;
using System.Linq;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class LibraryLoungeRestroomR2
{
    static string Work=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../.."));
    static Transform Node(string name,Transform parent,Vector3 position,Quaternion rotation)
    {
        var t=new GameObject(name).transform;t.SetParent(parent,false);t.SetPositionAndRotation(position,rotation);return t;
    }
    public static void IntegrateAndBuild(){Integrate();ValidateLayout();Build();}
    public static void Integrate()
    {
        var scene=EditorSceneManager.OpenScene(LibraryLoungeRestroomBuild.ScenePath,OpenSceneMode.Single);
        var cafe=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<CafePrototype>(true)).Single();
        if(cafe.seats.Length!=8||cafe.GetComponent<CafeCinemaView>()!=null)throw new Exception("R2 scene already integrated; use Build/ValidateLayout without reapplying");
        var parent=cafe.seats[6].parent;
        var all=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).ToArray();
        var sofa=all.Single(t=>t.name=="cinema_sofa");
        var mesh=sofa.GetComponentInChildren<MeshFilter>();var renderer=mesh.GetComponent<MeshRenderer>();
        var oldHotspot=all.Single(t=>t.name=="SofaInteraction_6");
        sofa.SetParent(parent,true);UnityEngine.Object.DestroyImmediate(oldHotspot.gameObject);
        var seats=new Transform[13];var exits=new Transform[13];
        Array.Copy(cafe.seats,seats,6);Array.Copy(cafe.exits,exits,6);
        seats[12]=cafe.seats[7];seats[12].name="Seat viewpoint 12";
        exits[12]=cafe.exits[7];exits[12].name="Seat exit 12";
        var chair=all.Single(t=>t!=null&&t.name=="SofaInteraction_7");chair.name="SofaInteraction_12";chair.GetComponent<CafeInteractable>().seatIndex=12;
        UnityEngine.Object.DestroyImmediate(cafe.seats[6].gameObject);UnityEngine.Object.DestroyImmediate(cafe.exits[6].gameObject);
        // The preserved V4 cushion arc is 118 degrees with radius 1.844–2.575.
        // Calibrate its world axes from the actual repaired mesh, not a new sofa scale.
        var b=renderer.bounds;Vector3 center=mesh.transform.position;
        float sx=(center.x-b.min.x)/2.86f,sz=b.size.z/(2*2.86f*Mathf.Sin(59*Mathf.Deg2Rad));
        var collider=mesh.GetComponent<MeshCollider>();
        for(int i=0;i<6;i++)
        {
            int index=6+i;float angle=(-47.5f+i*19f)*Mathf.Deg2Rad;
            var cushion=new Vector3(center.x-2.10f*sx*Mathf.Cos(angle),b.max.y+.5f,center.z+2.10f*sz*Mathf.Sin(angle));
            if(!collider.Raycast(new Ray(cushion,Vector3.down),out var hit,3))throw new Exception("No physical cushion under sofa spot "+index);
            var eye=hit.point+Vector3.up*.67f;
            var facing=Vector3.ProjectOnPlane(cafe.publicScreen.transform.position-eye,Vector3.up).normalized;
            var rotation=Quaternion.LookRotation(facing,Vector3.up);
            seats[index]=Node("Seat viewpoint "+index,parent,eye,rotation);
            var interaction=Node("SofaInteraction_"+index,parent,hit.point+facing*.22f+Vector3.up*.22f,rotation);
            var action=interaction.gameObject.AddComponent<CafeInteractable>();action.action=CafeInteractable.Action.Sofa;action.seatIndex=index;action.label="坐下看大屏";
            var trigger=interaction.gameObject.AddComponent<BoxCollider>();trigger.isTrigger=true;trigger.size=new Vector3(.78f,.72f,.82f);
            var exit=eye+facing*1.2f;exit.y=.06f;exits[index]=Node("Seat exit "+index,parent,exit,rotation);
            Debug.Log("R2_SOFA_SPOT index="+index+" cushion="+hit.point+" eye="+eye+" angle="+angle*Mathf.Rad2Deg);
        }
        cafe.seats=seats;cafe.exits=exits;
        var cinema=cafe.gameObject.AddComponent<CafeCinemaView>();cinema.cafe=cafe;cinema.firstSeat=6;cinema.sofaCount=6;
        EditorUtility.SetDirty(cafe);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        Directory.CreateDirectory(Path.Combine(Work,"Docs/R2"));
        Debug.Log("R2_INTEGRATE_PASS sofa mesh, computer rigs, public media and room capacity preserved");
    }
    public static void Build()
    {
        string output=Path.Combine(LibraryLoungeUiFixBuild.FindRoot(),"Builds/LibraryLounge-v0.1.3-Restroom-R2-20261107");
        Directory.CreateDirectory(output);PlayerSettings.bundleVersion="0.1.3";
        var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{LibraryLoungeRestroomBuild.ScenePath},target=BuildTarget.StandaloneWindows64,locationPathName=Path.Combine(output,"LibraryLounge.exe"),options=BuildOptions.Development});
        if(result.summary.result!=BuildResult.Succeeded)throw new Exception("R2_BUILD_FAIL "+result.summary.result);
        Debug.Log("R2_BUILD_PASS "+output);
    }
    public static void RefineAndBuild()
    {
        var scene=EditorSceneManager.OpenScene(LibraryLoungeRestroomBuild.ScenePath,OpenSceneMode.Single);
        var cafe=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<CafePrototype>(true)).Single();
        cafe.seats[12].rotation=Quaternion.Euler(0,90,0);
        // Keep tabletop greenery below all six seated sightlines.
        var plant=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).Single(t=>t.name=="cinema_plant_table");
        var plantPosition=plant.position;plantPosition.x=4.0f;plant.position=plantPosition;plant.localScale=Vector3.one*.85f;
        Debug.Log("R2_TABLE_PLANT_CLEARANCE position="+plant.position+" scale="+plant.localScale);
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        ValidateLayout();Build();
    }
    public static void ValidateLayout()
    {
        var scene=EditorSceneManager.OpenScene(LibraryLoungeRestroomBuild.ScenePath,OpenSceneMode.Single);
        var cafe=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<CafePrototype>(true)).Single();
        if(cafe.seats.Length!=13||cafe.exits.Length!=13||cafe.screens.Length!=6)
            throw new Exception("R2_EXPECT_SIX_SOFA_SEATS: need 6 computer + 6 sofa + 1 chair anchors; found "+cafe.seats.Length);
        var spots=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<CafeInteractable>(true))
            .Where(a=>a.action==CafeInteractable.Action.Sofa&&a.seatIndex>=6&&a.seatIndex<=11).ToArray();
        if(spots.Length!=6||spots.Select(a=>a.seatIndex).Distinct().Count()!=6)
            throw new Exception("R2_EXPECT_SIX_INDEPENDENT_SOFA_HOTSPOTS");
        Debug.Log("R2_LAYOUT_PASS six sofa spots / thirteen anchors / six computers");
    }
}

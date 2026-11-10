using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class LibraryLoungeRestroomR1
{
    static string Work=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../.."));
    static string Output=>Path.Combine(LibraryLoungeUiFixBuild.FindRoot(),"Builds/LibraryLounge-v0.1.3-Restroom-R1-20261107");
    [Serializable] class MeshDump {public Vector3[] vertices;public int[] triangles;public string path;public Vector3 position,scale;}
    [Serializable] class CapPatch {public int triangle;public Vector3[] weights;}
    [Serializable] class CapPatches {public CapPatch[] patches;}
    static Transform Find(Scene scene,string name)=>scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).Single(t=>t.name==name);
    public static void DiagnoseAndBuild()
    {
        var scene=EditorSceneManager.OpenScene(LibraryLoungeRestroomBuild.ScenePath,OpenSceneMode.Single);
        var sofa=Find(scene,"cinema_sofa").GetComponentInChildren<MeshFilter>();
        string asset=AssetDatabase.GetAssetPath(sofa.sharedMesh);
        if(AssetImporter.GetAtPath(asset) is ModelImporter importer&&!importer.isReadable){importer.isReadable=true;importer.SaveAndReimport();}
        var data=new MeshDump{vertices=sofa.sharedMesh.vertices.Select(sofa.transform.TransformPoint).ToArray(),triangles=sofa.sharedMesh.triangles,path=asset,position=sofa.transform.position,scale=sofa.transform.lossyScale};
        Directory.CreateDirectory(Path.Combine(Work,"Docs/R1"));File.WriteAllText(Path.Combine(Work,"Docs/R1/sofa-mesh-before.json"),JsonUtility.ToJson(data));
        Debug.Log("R1_DIAGNOSE_SOFA mesh="+asset+" vertices="+data.vertices.Length+" triangles="+data.triangles.Length/3);
        Build();
    }
    public static void Build()
    {
        Directory.CreateDirectory(Output);PlayerSettings.bundleVersion="0.1.3";
        var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{LibraryLoungeRestroomBuild.ScenePath},target=BuildTarget.StandaloneWindows64,locationPathName=Path.Combine(Output,"LibraryLounge.exe"),options=BuildOptions.Development});
        if(result.summary.result!=BuildResult.Succeeded)throw new Exception("R1_BUILD_FAIL "+result.summary.result);
        Debug.Log("R1_BUILD_PASS "+Output);
    }
    public static void RestoreDesktops()
    {
        var scene=EditorSceneManager.OpenScene(LibraryLoungeRestroomBuild.ScenePath,OpenSceneMode.Single);
        var art=EditorSceneManager.OpenScene(LibraryLoungeRestroomBuild.ArtScene,OpenSceneMode.Additive);
        for(int i=0;i<2;i++)
        {
            var original=Find(art,"monitor_ne_"+i).GetComponentInChildren<MeshFilter>();
            var current=Find(scene,"monitor_ne_"+i).GetComponentInChildren<MeshFilter>();
            current.sharedMesh=original.sharedMesh;
            current.transform.localPosition=original.transform.localPosition;
            current.transform.localRotation=original.transform.localRotation;
            current.transform.localScale=original.transform.localScale;
            var renderer=current.GetComponent<MeshRenderer>();var originalRenderer=original.GetComponent<MeshRenderer>();
            renderer.sharedMaterials=originalRenderer.sharedMaterials;
            renderer.lightmapIndex=originalRenderer.lightmapIndex;renderer.lightmapScaleOffset=originalRenderer.lightmapScaleOffset;
            current.GetComponent<MeshCollider>().sharedMesh=original.sharedMesh;
            var mesh=original.sharedMesh;var verts=mesh.vertices;
            var points=Enumerable.Range(0,mesh.subMeshCount).Where(s=>originalRenderer.sharedMaterials[s].name.Contains("Screen")).SelectMany(s=>mesh.GetTriangles(s)).Select(v=>original.transform.TransformPoint(verts[v])).ToArray();
            var rig=Find(scene,"MonitorRig_"+i).GetComponent<CafeMonitorFrame>();
            var right=rig.transform.right;var up=rig.transform.up;
            var bounds=new Bounds(points[0],Vector3.zero);foreach(var p in points)bounds.Encapsulate(p);
            rig.transform.position=bounds.center-rig.transform.forward*.004f;
            rig.originalScreenSize=new Vector2(points.Max(p=>Vector3.Dot(p,right))-points.Min(p=>Vector3.Dot(p,right)),points.Max(p=>Vector3.Dot(p,up))-points.Min(p=>Vector3.Dot(p,up)));
            rig.preserveHousing=true;rig.frame.localPosition=new Vector3(0,0,.003f);rig.Fit(1.6f);
            var neck=rig.transform.Find("Monitor neck");if(neck)UnityEngine.Object.DestroyImmediate(neck.gameObject);
            Debug.Log("R1_DESKTOP_ORIGINAL station="+i+" mesh="+AssetDatabase.GetAssetPath(current.sharedMesh)+" scale="+current.transform.lossyScale+" screen="+rig.originalScreenSize);
        }
        EditorSceneManager.CloseScene(art,true);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        Build();
    }
    public static void RepairSofa()
    {
        var scene=EditorSceneManager.OpenScene(LibraryLoungeRestroomBuild.ScenePath,OpenSceneMode.Single);
        var filter=Find(scene,"cinema_sofa").GetComponentInChildren<MeshFilter>();
        var original=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ArtV3/Models/sofa_arc_cream.fbx").GetComponentInChildren<MeshFilter>().sharedMesh;
        var patch=JsonUtility.FromJson<CapPatches>(File.ReadAllText(Path.Combine(Work,"Docs/R1/sofa-cap-patch.json"))).patches.ToDictionary(p=>p.triangle);
        if(patch.Count!=62)throw new Exception("Expected exactly two 31-triangle endpoint caps");
        var mesh=UnityEngine.Object.Instantiate(original);mesh.name="Sofa endpoint union v013 R1";
        var positions=original.vertices.ToList();var normals=original.normals.ToList();var tangents=original.tangents.ToList();
        var uv=new System.Collections.Generic.List<Vector4>[8];
        for(int channel=0;channel<8;channel++){uv[channel]=new System.Collections.Generic.List<Vector4>();original.GetUVs(channel,uv[channel]);}
        var submeshes=new System.Collections.Generic.List<int>[original.subMeshCount];int globalTriangle=0;
        for(int sub=0;sub<original.subMeshCount;sub++)
        {
            var indices=original.GetTriangles(sub);var result=submeshes[sub]=new System.Collections.Generic.List<int>();
            for(int offset=0;offset<indices.Length;offset+=3,globalTriangle++)
            {
                if(!patch.TryGetValue(globalTriangle,out var change)){result.AddRange(new[]{indices[offset],indices[offset+1],indices[offset+2]});continue;}
                int a=indices[offset],b=indices[offset+1],c=indices[offset+2];
                foreach(var w in change.weights)
                {
                    result.Add(positions.Count);
                    positions.Add(original.vertices[a]*w.x+original.vertices[b]*w.y+original.vertices[c]*w.z);
                    if(normals.Count>0)normals.Add((original.normals[a]*w.x+original.normals[b]*w.y+original.normals[c]*w.z).normalized);
                    if(tangents.Count>0)tangents.Add(original.tangents[a]*w.x+original.tangents[b]*w.y+original.tangents[c]*w.z);
                    for(int channel=0;channel<8;channel++)if(uv[channel].Count>0)uv[channel].Add(uv[channel][a]*w.x+uv[channel][b]*w.y+uv[channel][c]*w.z);
                }
            }
        }
        mesh.SetVertices(positions);if(normals.Count>0)mesh.SetNormals(normals);if(tangents.Count>0)mesh.SetTangents(tangents);
        for(int channel=0;channel<8;channel++)if(uv[channel].Count>0)mesh.SetUVs(channel,uv[channel]);
        for(int sub=0;sub<submeshes.Length;sub++)mesh.SetTriangles(submeshes[sub],sub);mesh.RecalculateBounds();
        const string path="Assets/ArtV3/Models/Playable013/SofaCapsR1.asset";
        if(AssetDatabase.LoadAssetAtPath<Mesh>(path)!=null)throw new Exception("R1 sofa asset already exists; inspect it before changing");
        AssetDatabase.CreateAsset(mesh,path);filter.sharedMesh=mesh;filter.GetComponent<MeshCollider>().sharedMesh=mesh;
        // Existing UV/lightmap coordinates are interpolated from each original
        // triangle. Curved surfaces, footprint and the original FBX stay intact.
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        var dump=new MeshDump{vertices=mesh.vertices.Select(filter.transform.TransformPoint).ToArray(),triangles=mesh.triangles,path=path,position=filter.transform.position,scale=filter.transform.lossyScale};
        File.WriteAllText(Path.Combine(Work,"Docs/R1/sofa-mesh-after.json"),JsonUtility.ToJson(dump));
        Debug.Log("R1_SOFA_CAP_UNION_PASS original="+original.triangles.Length/3+" repaired="+mesh.triangles.Length/3+" caps="+patch.Count);
        Build();
    }
    public static void Validate()
    {
        var scene=EditorSceneManager.OpenScene(LibraryLoungeRestroomBuild.ScenePath,OpenSceneMode.Single);
        var cafe=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<CafePrototype>(true)).Single();
        var art=EditorSceneManager.OpenScene(LibraryLoungeRestroomBuild.ArtScene,OpenSceneMode.Additive);
        for(int i=0;i<2;i++)
        {
            var original=Find(art,"monitor_ne_"+i).GetComponentInChildren<MeshFilter>();
            var current=Find(scene,"monitor_ne_"+i).GetComponentInChildren<MeshFilter>();
            if(current.sharedMesh!=original.sharedMesh||current.transform.localPosition!=original.transform.localPosition||current.transform.localRotation!=original.transform.localRotation||current.transform.localScale!=original.transform.localScale)throw new Exception("Original desktop model changed "+i);
            if(current.GetComponent<MeshCollider>().sharedMesh!=original.sharedMesh)throw new Exception("Original desktop collision mismatch "+i);
            Debug.Log("R1_VALIDATE_ORIGINAL_DESKTOP_PASS station="+i+" mesh="+AssetDatabase.GetAssetPath(current.sharedMesh)+" lightmap="+current.GetComponent<MeshRenderer>().lightmapIndex);
        }
        EditorSceneManager.CloseScene(art,true);
        // Reopen with no additive scene, so an index cannot accidentally refer
        // to a lightmap belonging only to the original art scene.
        scene=EditorSceneManager.OpenScene(LibraryLoungeRestroomBuild.ScenePath,OpenSceneMode.Single);
        for(int i=0;i<2;i++)
        {
            var renderer=Find(scene,"monitor_ne_"+i).GetComponentInChildren<MeshRenderer>();
            if(renderer.lightmapIndex<0||renderer.lightmapIndex>=LightmapSettings.lightmaps.Length||LightmapSettings.lightmaps[renderer.lightmapIndex].lightmapColor==null)throw new Exception("Desktop baked-light binding invalid "+i);
        }
        var sofa=Find(scene,"cinema_sofa").GetComponentInChildren<MeshFilter>();var mesh=sofa.sharedMesh;
        if(mesh.triangles.Length!=5923*3||mesh.vertexCount!=mesh.normals.Length||mesh.uv.Length!=mesh.vertexCount||mesh.uv2.Length!=mesh.vertexCount||sofa.GetComponent<MeshCollider>().sharedMesh!=mesh)throw new Exception("Sofa geometry/attributes/collision mismatch");
        foreach(var p in mesh.vertices)if(!float.IsFinite(p.x)||!float.IsFinite(p.y)||!float.IsFinite(p.z))throw new Exception("Nonfinite sofa vertex");
        if(scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<MonoBehaviour>(true)).Any(c=>c==null))throw new Exception("Missing scene script");
        Debug.Log("R1_VALIDATE_PASS desktops=2 sofaTriangles=5923 normals/UV/UV2/collider valid lightmaps="+LightmapSettings.lightmaps.Length);
    }
}

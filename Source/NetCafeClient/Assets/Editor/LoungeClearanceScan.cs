using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
public static class LoungeClearanceScan
{
 public static void Run() {
  var scene=EditorSceneManager.OpenScene(LibraryLoungeBuild.ScenePath,OpenSceneMode.Single);
  Physics.SyncTransforms();
  var cafes=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<CafePrototype>(true)).ToArray();
  if(cafes.Length!=1)throw new Exception("Cafe count "+cafes.Length);
  var body=cafes[0].body;
  var sb=new StringBuilder();
  sb.AppendLine("{\"originalRadius\":"+body.radius.ToString("F2")+",\"expandedRadius\":"+(body.radius+.5f).ToString("F2")+",\"results\":[");
  bool first=true;
  for(float z=-5.4f;z<=5.401f;z+=.1f) {
   float r=body.radius+.5f;
   float half=Mathf.Max(0f,body.height*.5f-r);
   Vector3 c0=new Vector3(0f,.05f,z)+body.center;
   Vector3 c1=new Vector3(20f,.05f,z)+body.center;
   Vector3 p0=c0-Vector3.up*half, p1=c1+Vector3.up*half;
   var hits=Physics.OverlapCapsule(p0,p1,r,~0,QueryTriggerInteraction.Ignore)
    .Where(c=>c!=body&&c.enabled&&c.gameObject.activeInHierarchy)
    .Select(c=>new { C=c, MinX=c.bounds.min.x, MaxX=c.bounds.max.x })
    .Where(x=>x.MinX<4.8f)
    .OrderBy(x=>x.MinX).ToArray();
   if(!first)sb.Append(",");
   first=false;
   string names=string.Join(";",hits.Select(x=>Hierarchy(x.C.transform)+"["+x.C.GetType().Name+
    " x="+x.MinX.ToString("F2")+".."+x.MaxX.ToString("F2")+
    " z="+x.C.bounds.min.z.ToString("F2")+".."+x.C.bounds.max.z.ToString("F2")+"]"));
   sb.Append("{\"z\":"+z.ToString("F2")+",\"blockerCount\":"+hits.Length+",\"blockers\":\""+Esc(names)+"\"}");
  }
  sb.AppendLine("]}");
  var path=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../Evidence/WalkingClearanceScan.json"));
  System.IO.File.WriteAllText(path,sb.ToString());
  Debug.Log("WALKING_CLEARANCE_SCAN_WRITTEN "+path);
 }
 static string Hierarchy(Transform t){var p=new System.Collections.Generic.List<string>();while(t){p.Add(t.name);t=t.parent;}p.Reverse();return string.Join("/",p);}
 static string Esc(string s)=>(s??"").Replace("\\","\\\\").Replace("\"","\\\"").Replace("\r","\\r").Replace("\n","\\n");
}

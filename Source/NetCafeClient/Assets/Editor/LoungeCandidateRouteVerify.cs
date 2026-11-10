using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
public static class LoungeCandidateRouteVerify
{
 public static void Run() {
  var scene=EditorSceneManager.OpenScene(LibraryLoungeBuild.ScenePath,OpenSceneMode.Single);
  Physics.SyncTransforms();
  var cafe=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<CafePrototype>(true)).Single();
  var body=cafe.body;
  Debug.Log("PUBLIC_SCREEN_POSE pos="+cafe.publicScreen.transform.position+" forward="+cafe.publicScreen.transform.forward+" up="+cafe.publicScreen.transform.up+" parentScale="+cafe.publicScreen.transform.parent.lossyScale);
  var sb=new StringBuilder();
  foreach(float radius in new[]{body.radius,body.radius+.5f}) {
   float half=Mathf.Max(0,body.height*.5f-radius);
   Vector3 c0=new Vector3(0,.05f,.3f)+body.center;
   Vector3 c1=new Vector3(20,.05f,.3f)+body.center;
   Vector3 p0=c0-Vector3.up*half,p1=c1+Vector3.up*half;
   var hits=Physics.OverlapCapsule(p0,p1,radius,~0,QueryTriggerInteraction.Ignore)
    .Where(c=>c!=body&&c.enabled&&c.gameObject.activeInHierarchy)
    .OrderBy(c=>c.bounds.min.x).ToArray();
   sb.AppendLine("radius="+radius.ToString("F2")+" bodyLayer="+body.gameObject.layer+" ignoreRaycast="+Physics.GetIgnoreLayerCollision(body.gameObject.layer,2)+" hits="+hits.Length);
   foreach(var c in hits)sb.AppendLine(Hierarchy(c.transform)+" | "+c.GetType().Name+" | layer="+c.gameObject.layer+":"+LayerMask.LayerToName(c.gameObject.layer)+" ignored="+Physics.GetIgnoreLayerCollision(body.gameObject.layer,c.gameObject.layer)+" | trigger="+c.isTrigger+" | pos="+c.transform.position.ToString("F3")+" | bounds="+c.bounds.min.ToString("F3")+".."+c.bounds.max.ToString("F3"));
  }
  string path=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../Evidence/CandidateRouteVerify.txt"));
  System.IO.File.WriteAllText(path,sb.ToString());
  Debug.Log("CANDIDATE_ROUTE_VERIFY_WRITTEN "+path);
 }
 static string Hierarchy(Transform t){var p=new System.Collections.Generic.List<string>();while(t){p.Add(t.name);t=t.parent;}p.Reverse();return string.Join("/",p);}
}

using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
public static class LibraryLoungeDesignGeometryAudit {
  [MenuItem("NetCafe/Library Lounge/Inspect current cinema area geometry")]
  public static void Inspect() {
    Scene scene=EditorSceneManager.OpenScene(LibraryLoungeBuild.ScenePath,OpenSceneMode.Single);
    var all=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<Transform>(true)).ToArray();
    var cafe=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<CafePrototype>(true)).Single();
    var names=new[]{"West ivory wall","North ivory wall","South wall left","Central conversation rug","Social sofa - sage","Guest rest sofa","Corner reading chair","Quiet corner round table","Social laptop table","Conversation floor lamp","Reading library shelves west","Entry cabinet","Tall indoor greenery"};
    foreach(var name in names) {
      var m=all.Where(x=>x.name==name).ToArray();
      foreach(var t in m) {
        var renderers=t.GetComponentsInChildren<Renderer>(true).Where(x=>x.enabled).ToArray();
        Bounds bounds=renderers.Length>0?renderers[0].bounds:new Bounds(t.position,Vector3.zero);
        foreach(var ren in renderers.Skip(1))bounds.Encapsulate(ren.bounds);
        Debug.Log("CINEMA_GEOM "+name+" position="+t.position.ToString("F3")+" rotation="+t.eulerAngles.ToString("F1")+" scale="+t.localScale.ToString("F3")+" bounds="+bounds.ToString()+" min="+bounds.min.ToString("F3")+" max="+bounds.max.ToString("F3"));
      }
    }
    Debug.Log("CINEMA_PUBLIC_SCREEN "+cafe.publicScreen.name+" worldPos="+cafe.publicScreen.transform.position.ToString("F3")+" rotation="+cafe.publicScreen.transform.eulerAngles.ToString("F1")+" localScale="+cafe.publicScreen.transform.localScale.ToString("F3")+" bounds="+cafe.publicScreen.bounds.ToString()+" parent="+cafe.publicScreen.transform.parent.name);
    foreach(var hit in scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<CafeInteractable>(true)))
       if(hit.action==CafeInteractable.Action.Display || hit.action==CafeInteractable.Action.Sofa)
        Debug.Log("CINEMA_INTERACTION "+hit.name+" action="+hit.action+" seat="+hit.seatIndex+" pos="+hit.transform.position.ToString("F3"));
    for(int i=0;i<cafe.seats.Length;i++) Debug.Log("CINEMA_SEAT "+i+" "+cafe.seats[i].position.ToString("F3"));
    Debug.Log("CINEMA_GEOMETRY_INSPECTION_PASS");
  }
}

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Linq;
public static class LaptopFixAudit {
public static void Inspect(){var s=EditorSceneManager.OpenScene(LibraryLoungeBuild.ScenePath,OpenSceneMode.Single);var cafe=s.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<CafePrototype>(true)).Single();
for(int i=0;i<6;i++){var t=cafe.screens[i].transform;var q=cafe.seats[i];Debug.Log($"LAPTOP_BASELINE_{i} screenName={t.name} pos={t.position:F3} euler={t.eulerAngles:F2} normal={t.forward:F3} scale={t.lossyScale:F3} seat={q.position:F3} seatForward={q.forward:F3} bounds={cafe.screens[i].bounds}");
Debug.Log($"LAPTOP_BASELINE_{i} parent={t.parent?.name} parentPos={t.parent?.position:F3} childCount={t.childCount}");}
}
}
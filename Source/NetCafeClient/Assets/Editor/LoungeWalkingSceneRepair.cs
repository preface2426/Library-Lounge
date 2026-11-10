using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class LoungeWalkingSceneRepair
{
    const string DecorRoot = "LIBRARY LOUNGE V2 - KENNEY CC0 + WARM ARCHITECTURE";
    const string PlantName = "Tall indoor greenery";
    const string BarrierName = "Public screen - walk blocker (no gaze ray)";
    public static void Apply()
    {
        Scene scene = EditorSceneManager.OpenScene(LibraryLoungeBuild.ScenePath, OpenSceneMode.Single);
        Physics.SyncTransforms();
        var cafe = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<CafePrototype>(true)).Single();
        Transform decor = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Transform>(true))
            .Single(x => x.name == DecorRoot);
        Transform plant = decor.GetComponentsInChildren<Transform>(true).Single(x => x.name == PlantName);
        Vector3 plantBefore = plant.position;
        Vector3 plantRotationBefore = plant.rotation.eulerAngles;
        Bounds plantBoundsBefore = plant.GetComponent<Collider>().bounds;

        Transform existing = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Transform>(true))
            .SingleOrDefault(x => x.name == BarrierName);
        if (existing != null) throw new InvalidOperationException("Screen walk blocker already exists; refusing duplicate");

        // Move only the non-interactive decorative plant out of the selected clear aisle.
        plant.position = new Vector3(plant.position.x, plant.position.y, 1.9000f);
        Bounds plantBoundsAfter = plant.GetComponent<Collider>().bounds;

        // A low-cost Ignore-Raycaster box protects the cinema panel physically while
        // allowing the default interaction ray to reach the actual public screen.
        var barrier = new GameObject(BarrierName);
        barrier.transform.SetParent(cafe.publicScreen.transform.parent, true);
        barrier.transform.SetPositionAndRotation(new Vector3(5.4500f, 1.6500f, -2.1000f), Quaternion.identity);
        barrier.transform.localScale = Vector3.one;
        barrier.layer = 2;
        var box = barrier.AddComponent<BoxCollider>();
        box.center = Vector3.zero;
        box.size = new Vector3(.0800f, 2.9000f, 5.1200f);
        box.isTrigger = false;
        box.enabled = true;

        EditorUtility.SetDirty(plant);
        EditorUtility.SetDirty(box);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save furnished scene repair");
        Physics.SyncTransforms();

        var cafeAfter = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<CafePrototype>(true)).Single();
        var seats = cafeAfter.seats ?? Array.Empty<Transform>();
        var exits = cafeAfter.exits ?? Array.Empty<Transform>();
        var screens = cafeAfter.screens ?? Array.Empty<Renderer>();
        var interactables = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<CafeInteractable>(true)).ToArray();
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("\"scene\":\"" + LibraryLoungeBuild.ScenePath + "\",");
        sb.AppendLine("\"plant\":{\"path\":\"" + Esc(Hierarchy(plant)) + "\",\"collider\":\"" +
            plant.GetComponent<Collider>().GetType().Name + "\",\"beforePosition\":\"" + plantBefore.ToString("F4") +
            "\",\"afterPosition\":\"" + plant.position.ToString("F4") + "\",\"beforeRotation\":\"" +
            plantRotationBefore.ToString("F3") + "\",\"afterRotation\":\"" + plant.rotation.eulerAngles.ToString("F3") +
            "\",\"beforeBoundsCenter\":\"" + plantBoundsBefore.center.ToString("F4") + "\",\"beforeBoundsSize\":\"" +
            plantBoundsBefore.size.ToString("F4") + "\",\"afterBoundsCenter\":\"" + plantBoundsAfter.center.ToString("F4") +
            "\",\"afterBoundsSize\":\"" + plantBoundsAfter.size.ToString("F4") + "\"},");
        sb.AppendLine("\"barrier\":{\"path\":\"" + Esc(Hierarchy(barrier.transform)) + "\",\"collider\":\"BoxCollider\",\"layer\":2,\"isTrigger\":false,\"position\":\"" +
            barrier.transform.position.ToString("F4") + "\",\"rotation\":\"" + barrier.transform.rotation.eulerAngles.ToString("F3") +
            "\",\"size\":\"" + box.size.ToString("F4") + "\",\"boundsMin\":\"" + box.bounds.min.ToString("F4") +
            "\",\"boundsMax\":\"" + box.bounds.max.ToString("F4") + "\"},");
        sb.AppendLine("\"anchors\":{");
        sb.Append("\"seatCount\":" + seats.Length + ",\"seats\":[");
        for (int i=0;i<seats.Length;i++) { if(i>0)sb.Append(","); var t=seats[i]; sb.Append("{\"i\":"+i+",\"path\":\""+Esc(Hierarchy(t))+"\",\"position\":\""+t.position.ToString("F4")+"\",\"rotation\":\""+t.rotation.eulerAngles.ToString("F3")+"\"}"); }
        sb.AppendLine("],");
        sb.Append("\"exitCount\":" + exits.Length + ",\"exits\":[");
        for (int i=0;i<exits.Length;i++) { if(i>0)sb.Append(","); var t=exits[i]; sb.Append("{\"i\":"+i+",\"path\":\""+Esc(Hierarchy(t))+"\",\"position\":\""+t.position.ToString("F4")+"\",\"rotation\":\""+t.rotation.eulerAngles.ToString("F3")+"\"}"); }
        sb.AppendLine("],");
        sb.Append("\"computerScreenCount\":" + screens.Length + ",\"computerScreens\":[");
        for (int i=0;i<screens.Length;i++) { if(i>0)sb.Append(","); var t=screens[i].transform; sb.Append("{\"i\":"+i+",\"path\":\""+Esc(Hierarchy(t))+"\",\"position\":\""+t.position.ToString("F4")+"\",\"rotation\":\""+t.rotation.eulerAngles.ToString("F3")+"\"}"); }
        sb.AppendLine("],");
        sb.Append("\"publicScreen\":{\"path\":\""+Esc(Hierarchy(cafeAfter.publicScreen.transform))+"\",\"position\":\""+cafeAfter.publicScreen.transform.position.ToString("F4")+"\",\"rotation\":\""+cafeAfter.publicScreen.transform.rotation.eulerAngles.ToString("F3")+"\",\"forward\":\""+cafeAfter.publicScreen.transform.forward.ToString("F3")+"\"}");
        sb.AppendLine("},");
        sb.AppendLine("\"interactableCount\":"+interactables.Length+",\"interactables\":[");
        for(int i=0;i<interactables.Length;i++) { if(i>0)sb.Append(","); var x=interactables[i]; sb.Append("{\"path\":\""+Esc(Hierarchy(x.transform))+"\",\"action\":\""+x.action+"\",\"position\":\""+x.transform.position.ToString("F4")+"\",\"rotation\":\""+x.transform.rotation.eulerAngles.ToString("F3")+"\"}"); }
        sb.AppendLine("]");
        sb.AppendLine("}");
        string output = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../Evidence/A2SceneRepairAfter.json"));
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(output));
        System.IO.File.WriteAllText(output, sb.ToString());
        Debug.Log("A2_SCENE_REPAIR_PASS: moved Tall indoor greenery; added screen walk blocker; anchors=" +
            seats.Length + "; screens=" + screens.Length + "; interactables=" + interactables.Length + "; audit=" + output);
    }
    static string Hierarchy(Transform t) {
        var parts=new System.Collections.Generic.List<string>();
        while(t!=null){parts.Add(t.name);t=t.parent;}
        parts.Reverse();return string.Join("/",parts);
    }
    static string Esc(string s) => (s??"").Replace("\\","\\\\").Replace("\"","\\\"").Replace("\r","\\r").Replace("\n","\\n");
}

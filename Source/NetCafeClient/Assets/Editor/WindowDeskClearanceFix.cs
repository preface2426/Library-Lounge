#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class WindowDeskClearanceFix
{
    const float RaiseMonitor = .20f;
    const float RaiseLegacyKeyboard = .25f;
    const float RaiseStationKeyboard = .14f;

    static void MoveY(Transform t, float dy)
    {
        if (t == null) throw new InvalidOperationException("Missing expected window-computer component");
        Undo.RecordObject(t, "Correct window reading desk computer installation");
        t.localPosition += Vector3.up * dy;
        PrefabUtility.RecordPrefabInstancePropertyModifications(t);
        EditorUtility.SetDirty(t);
    }

    public static void AlignWindowComputers()
    {
        var scene=EditorSceneManager.OpenScene(LibraryLoungeBuild.ScenePath,OpenSceneMode.Single);
        var cafe=scene.GetRootGameObjects()
            .SelectMany(o=>o.GetComponentsInChildren<CafePrototype>(true)).Single();
        var roots=scene.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<Transform>(true)).ToArray();
        // Only station 0 intersects the taller new reading desk: its top
        // is 0.939 m, vs the original monitor bottom 0.88 m.
        // Station 1's reading desk mesh top is only 0.452 m, so lifting its
        // PC by the same amount would create an unnatural floating monitor.
        for(int i=0;i<1;i++)
        {
            var oldRoot=cafe.screens[i].transform.parent;
            if(oldRoot==null || oldRoot.name!="Computer position "+(i+1) ||
                Mathf.Abs(oldRoot.localPosition.y)>.002f)
                throw new InvalidOperationException("Unexpected legacy computer position "+i);
            var station=roots.Single(t=>t.name=="Station_"+i+"_DESKTOP_WINDOW");
            var desk=roots.Single(t=>t.name=="Window reading desk "+(i+1));
            var surface=desk.GetComponent<Renderer>();
            if(surface==null) throw new InvalidOperationException("Window reading desk missing renderer");
            float deskTop=surface.bounds.max.y;
            MoveY(oldRoot,RaiseMonitor);
            foreach(var t in oldRoot.GetComponentsInChildren<Transform>(true))
            {
                if(t.name=="computerKeyboard" || t.name=="computerMouse")
                    MoveY(t,RaiseLegacyKeyboard);
            }
            foreach(string n in new[]{"Widescreen monitor frame","SCREEN_PRIVATE","Monitor stand"})
                MoveY(station.Find(n),RaiseMonitor);
            MoveY(station.Find("Keyboard placeholder"),RaiseStationKeyboard);
            var baseRenderer=oldRoot.Find("Monitor base").GetComponent<Renderer>();
            var activeFace=cafe.screens[i].transform;
            var faceRenderer=activeFace.GetComponent<Renderer>();
            if(faceRenderer.bounds.min.y < deskTop+.075f ||
                baseRenderer.bounds.min.y < deskTop-.035f ||
                baseRenderer.bounds.min.y > deskTop+.06f)
                throw new InvalidOperationException("Monitor clearance still wrong seat "+i+
                    " deskTop="+deskTop+" faceBottom="+faceRenderer.bounds.min.y+
                    " monitorBaseBottom="+baseRenderer.bounds.min.y);
            Debug.Log("WINDOW_DESK_CLEARANCE_PASS seat="+i+
                " deskTop="+deskTop+" faceBottom="+faceRenderer.bounds.min.y+
                " baseBottom="+baseRenderer.bounds.min.y+
                " screenY="+activeFace.position.y);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene))
            throw new InvalidOperationException("Failed to save adjusted candidate scene");
        Debug.Log("WINDOW_DESK_ALIGNMENT_SCENE_SAVED");
    }
}
#endif

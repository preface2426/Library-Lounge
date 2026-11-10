using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Regression guard for the actual visible floor. The old prototype/greybox
// architecture is intentionally inactive and MUST NOT be activated again.
public static class CozyLoungeFloorRepair
{
    const string Name = "Warm plank floor";
    [MenuItem("NetCafe/Library Lounge/Repair visible floor collision")]
    public static void Repair()
    {
        Scene scene = EditorSceneManager.OpenScene(LibraryLoungeBuild.ScenePath, OpenSceneMode.Single);
        BoxCollider floor = Find(scene);
        if (floor == null) throw new InvalidOperationException("Visible Warm plank floor BoxCollider not found");
        floor.enabled = true; // Layer 2 only affects gaze rays; physics still collides.
        // Preserve IgnoreRaycast so the floor does not steal station E-selection.
        if (floor.gameObject.layer != 2) floor.gameObject.layer = 2;        int triggerCount = 0;
        foreach (var marker in scene.GetRootGameObjects()
            .SelectMany(x=>x.GetComponentsInChildren<CafeInteractable>(true)))
        {
            if (marker.action != CafeInteractable.Action.Seat &&
                marker.action != CafeInteractable.Action.Sofa) continue;
            var hot = marker.GetComponent<Collider>();
            if (hot == null) throw new InvalidOperationException("Seat hotspot has no Collider: " + marker.name);
            hot.isTrigger = true; // Actual furniture walk-blockers stay solid.
            EditorUtility.SetDirty(hot);
            triggerCount++;
        }
        if (triggerCount != 8) throw new InvalidOperationException("Expected 8 seat/rest triggers; got " + triggerCount);
        EditorUtility.SetDirty(floor);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new InvalidOperationException("Could not save corrected furnished scene");
        Verify(scene);
        Debug.Log("COZY_FLOOR_REPAIR_SAVED: visible plank floor solid; legacy roots unchanged");
    }

    [MenuItem("NetCafe/Library Lounge/Audit active floor collision")]
    public static void Audit()
    {
        Scene scene = EditorSceneManager.OpenScene(LibraryLoungeBuild.ScenePath, OpenSceneMode.Single);
        Verify(scene);
    }

    public static void Verify(Scene scene)
    {
        var floor = Find(scene);
        var cafe = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<CafePrototype>(true)).Single();
        if (floor == null || !floor.enabled || !floor.gameObject.activeInHierarchy || floor.isTrigger)
            throw new InvalidOperationException("LIVE_FLOOR_COLLISION_MISSING: visible floor is not solid");
        if (cafe.body == null || !cafe.body.enabled || Physics.GetIgnoreLayerCollision(cafe.body.gameObject.layer, floor.gameObject.layer))
            throw new InvalidOperationException("LIVE_FLOOR_COLLISION_IGNORED_BY_PLAYER");
        Physics.SyncTransforms();        var hotspots = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<CafeInteractable>(true))
            .Where(x=>x.action == CafeInteractable.Action.Seat || x.action == CafeInteractable.Action.Sofa).ToArray();
        if (hotspots.Length != 8 || hotspots.Any(x => x.GetComponent<Collider>() == null || !x.GetComponent<Collider>().isTrigger))
            throw new InvalidOperationException("SEAT_HOTSPOTS_BLOCK_WALKING: required 8 non-solid physics triggers");
        var bounds = floor.bounds;
        float top = bounds.max.y;
        if (Mathf.Abs(top) > .10f) throw new InvalidOperationException("Unexpected floor top at " + top);
        var spawn = cafe.body.transform.position;
        if (!Over(spawn, bounds, top)) throw new InvalidOperationException("PLAYER_SPAWN_OUTSIDE_LIVE_FLOOR: " + spawn);
        for (int i=0;i<cafe.exits.Length;i++)
        {
            Vector3 pos = cafe.exits[i].position;
            if (!Over(pos,bounds,top)) throw new InvalidOperationException("SEAT_EXIT_OUTSIDE_LIVE_FLOOR " + i + " " + pos);
        }
        Debug.Log("LIVE_FLOOR_COLLISION_PASS: solid active floor; spawn and " + cafe.exits.Length +
            " exits supported; layer0-layer2 collisions enabled; y=" + top.ToString("F3"));
    }

    static bool Over(Vector3 p, Bounds b, float top)
    {
        // The center of the character's capsule must be inside the walkable support.
        return p.x > b.min.x + .30f && p.x < b.max.x - .30f &&
            p.z > b.min.z + .30f && p.z < b.max.z - .30f &&
            p.y > top - .20f && p.y < top + 2.0f;
    }

    static BoxCollider Find(Scene scene)
    {
        var matches = scene.GetRootGameObjects()
            .SelectMany(x => x.GetComponentsInChildren<BoxCollider>(true))
            .Where(x => x.gameObject.name == Name).ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException("Expected one visible plank floor; found " + matches.Length);
        return matches[0];
    }
}

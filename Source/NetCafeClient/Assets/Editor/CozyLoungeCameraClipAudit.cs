using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Inspect head positions, not just the capsule and station exits. Cameras
// follow seated head anchors, but desktop focus teleports them to a monitor
// view point; neither should be embedded in a solid table/chair/wall.
public static class CozyLoungeCameraClipAudit
{    static readonly System.Collections.Generic.List<string> Collisions = new System.Collections.Generic.List<string>();
    [MenuItem("NetCafe/Library Lounge/Audit seated and walking camera collision")]
    public static void Run()
    {
        Scene scene=EditorSceneManager.OpenScene(LibraryLoungeBuild.ScenePath,OpenSceneMode.Single);
        Verify(scene);
    }

    // Every build verifies seat directions and head clearance without altering any avatar pose.
    public static void Verify(Scene scene)
    {
        var cafe=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<CafePrototype>(true)).Single();
        Physics.SyncTransforms();        Collisions.Clear();
        Probe("walking_spawn",cafe.body.transform.position+Vector3.up*1.62f,cafe.body);
        for(int i=0;i<cafe.seats.Length;i++)
        {
            Probe("seated_observe_"+i,cafe.seats[i].position,cafe.body);            // No pose changes: verify the stored seat orientation points toward
            // the correct screen, or into the room for rest seats.
            Vector3 target = i<cafe.screens.Length ? cafe.screens[i].transform.position :
                cafe.seats[i].position+Vector3.forward;
            Vector3 front = Vector3.ProjectOnPlane(target-cafe.seats[i].position, Vector3.up).normalized;
            float facing = Vector3.Dot(cafe.seats[i].forward, front);
            if (facing < .65f)
                Collisions.Add("SEAT_FACE_DIRECTION_INVALID id="+i+" dot="+facing.ToString("F3"));
            if(i<cafe.screens.Length)
            {
                var t=cafe.screens[i].transform;
                Vector3 focus=i>=2 && i<6 ? cafe.seats[i].position :
                    t.position-t.forward*.6f;
                Probe("desktop_focus_"+i,focus,cafe.body);
            }
        }
        if (Collisions.Count > 0)
            throw new InvalidOperationException("CAMERA_COLLISION_AUDIT_FAIL: " +
                string.Join("; ", Collisions));        Debug.Log("COZY_CAMERA_CLEAR_PASS: 1 walking + "+cafe.seats.Length+" seat heads + "+
            cafe.screens.Length+" desktop focus probes; no solid clipping at 9cm radius");
    }

    static void Probe(string name,Vector3 center,Collider player)
    {
        const float radius=.09f;
        var hits=Physics.OverlapSphere(center,radius,~0,QueryTriggerInteraction.Ignore)
            .Where(c=>c!=player && c.enabled && c.gameObject.activeInHierarchy)
            .Select(c=>c.name).Distinct().ToArray();
        if(hits.Length>0)
        {
            string detail="CAMERA_HEAD_INSIDE_COLLIDER "+name+" position="+center+
                " blockers="+string.Join(", ",hits);
            Debug.LogWarning(detail);
            Collisions.Add(detail);
        }
    }
}

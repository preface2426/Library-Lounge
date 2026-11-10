using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// L-01/L-02 non-PlayMode geometry audit: never invokes native desktop capture/input.
public static class CozyLoungeSpatialAudit
{
    [MenuItem("NetCafe/Cozy Lounge/Audit seats and geometry (edit mode)")]
    public static void Run()
    {
        Scene scene = EditorSceneManager.OpenScene(LibraryLoungeBuild.ScenePath, OpenSceneMode.Single);
        var roots = scene.GetRootGameObjects();
        var cafe = roots.SelectMany(r => r.GetComponentsInChildren<CafePrototype>(true)).Single();
        var interactables = roots.SelectMany(r => r.GetComponentsInChildren<CafeInteractable>(true)).ToArray();
        var seats = interactables.Where(x => x.action == CafeInteractable.Action.Seat).ToArray();
        var rest = interactables.Where(x => x.action == CafeInteractable.Action.Sofa).ToArray();
        int warnings = 0, blockers = 0;
        Physics.SyncTransforms();
        if (seats.Length != 6 || rest.Length != 2)
            throw new InvalidOperationException("Expected 6 actual computer hotspots and 2 rest hotspots");

        for (int i = 0; i < cafe.seats.Length; i++)
        {
            var hit = interactables.SingleOrDefault(x => x.seatIndex == i &&
                (x.action == CafeInteractable.Action.Seat || x.action == CafeInteractable.Action.Sofa));
            if (hit == null || i >= cafe.exits.Length)
            { Debug.LogError("COZY_SEAT_FAIL id=" + i + " missing hotspot or exit"); blockers++; continue; }

            Vector3 seat = cafe.seats[i].position;
            Vector3 exit = cafe.exits[i].position;
            if (float.IsNaN(seat.x) || float.IsNaN(exit.x))
            { blockers++; Debug.LogError("COZY_SEAT_FAIL id=" + i + " non-finite coordinates"); continue; }
            if (Mathf.Abs(exit.x) > 5.6f || Mathf.Abs(exit.z) > 4.7f)
            { blockers++; Debug.LogError("COZY_SEAT_FAIL id=" + i + " exit outside interior"); }
            if (Vector3.Distance(seat, exit) > 4f)
            { warnings++; Debug.LogWarning("COZY_SEAT_WARN id=" + i + " exit far from seat"); }

            // Capsule identical to the original Stand() check, excluding triggers and floor.
            var all = Physics.OverlapCapsule(exit + Vector3.up * .36f,
                exit + Vector3.up * 1.44f, .30f, ~0, QueryTriggerInteraction.Ignore);
            var obstacles = all.Where(c => c != cafe.body && c.gameObject.activeInHierarchy)
                .Select(c => c.name).Distinct().ToArray();
            if (obstacles.Length > 0)
            { blockers++; Debug.LogError("COZY_EXIT_BLOCKED id=" + i + " [" + string.Join(", ", obstacles) + "]"); }

            Vector3 from = exit + Vector3.up * 1.55f;
            Collider itemCollider = hit.GetComponent<Collider>();
            if (itemCollider == null || !itemCollider.isTrigger)
            { blockers++; Debug.LogError("COZY_HOTSPOT_NOT_TRIGGER id=" + i); continue; }

            Vector3 aim = itemCollider.bounds.center;
            Vector3 line = aim - from;
            if (line.magnitude > 3f)
            { warnings++; Debug.LogWarning("COZY_HOTSPOT_TOO_FAR id=" + i + " distance=" + line.magnitude.ToString("F2")); }
            else if (line.magnitude > .01f)
            {
                // Match CafePrototype.FindTarget: default layers exclude Ignore Raycast (2).
                // Walk-only furniture colliders on that layer should not block E interactions.
                var intersections = Physics.RaycastAll(from, line.normalized, line.magnitude,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide)
                    .OrderBy(x => x.distance).ToArray();
                Collider front = intersections.Length > 0 ? intersections[0].collider : null;
                if (front != null && front != itemCollider)
                { warnings++; Debug.LogWarning("COZY_HOTSPOT_OCCLUDED_FROM_EXIT id=" + i +
                    " by=" + front.name + " wanted=" + hit.name); }
            }
            if (i < cafe.screens.Length)
            {
                Renderer screen = cafe.screens[i];
                if (!screen.gameObject.activeInHierarchy || screen.bounds.size.sqrMagnitude < .001f)
                { blockers++; Debug.LogError("COZY_SCREEN_BAD id=" + i); }
                float eyeToScreen = Vector3.Distance(seat, screen.transform.position);
                if (eyeToScreen < .53f || eyeToScreen > .82f)
                {
                    blockers++;
                    Debug.LogError("LIBRARY_SCREEN_SEAT_DISTANCE_FAIL id=" + i +
                        " distance=" + eyeToScreen.ToString("F3") + " expected=0.53..0.82m");
                }
                Vector3 towardsScreen = (screen.transform.position - seat).normalized;
                if (Vector3.Dot(cafe.seats[i].forward, towardsScreen) < .65f)
                {
                    blockers++;
                    Debug.LogError("LIBRARY_SEAT_ORIENTATION_FAIL id=" + i);
                }
                Debug.Log("LIBRARY_SEAT_SCREEN_PASS id=" + i +
                    " eye_screen_m=" + eyeToScreen.ToString("F3"));
            }
            Debug.Log("COZY_SEAT_AUDIT id=" + i + " type=" +
                (i < cafe.screens.Length ? "computer" : "rest") +
                " seat=" + seat + " exit=" + exit + " distance=" + line.magnitude.ToString("F2"));
        }
        var styled = roots.SingleOrDefault(x => x.name.StartsWith(
            "LIBRARY LOUNGE V2 - KENNEY CC0", StringComparison.Ordinal));
        if (styled == null || !styled.activeInHierarchy)
        { blockers++; Debug.LogError("LIBRARY_FURNISHINGS_ROOT_MISSING"); }
        else
        {
            int furnishedRenderers = styled.GetComponentsInChildren<Renderer>(true).Length;
            if (furnishedRenderers < 35)
            { blockers++; Debug.LogError("LIBRARY_FURNITURE_TOO_SPARSE renderers=" + furnishedRenderers); }
            else Debug.Log("LIBRARY_FURNISHINGS_PASS renderers=" + furnishedRenderers);
        }
        Debug.Log("COZY_SPATIAL_AUDIT: blockers=" + blockers + "; warnings=" + warnings +
            "; eight_hotspots=" + (seats.Length + rest.Length) + "; NOT actual gameplay test");
        if (blockers > 0)
            throw new InvalidOperationException("Cozy Lounge spatial audit blockers=" + blockers);
    }
}

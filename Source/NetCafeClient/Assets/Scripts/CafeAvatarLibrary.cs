using System;
using UnityEngine;

// All player-visible characters are prepackaged; never load models supplied by other clients.
public sealed class CafeAvatarLibrary : MonoBehaviour
{
    public const string DefaultId = "Corgi";
    public static readonly string[] Ids = {
        "Anglerfish", "Bat", "Corgi", "Duck", "Hippo",
        "Jellyfish", "Platypus", "Red", "Rhino", "Shark"
    };
    const string PrefKey = "NetCafe.AvatarId";
    public GameObject[] prefabs;

    public static string Valid(string id)
    {
        foreach (string allowed in Ids)
            if (string.Equals(allowed, id, StringComparison.Ordinal)) return allowed;
        return DefaultId;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    static string testOverride;
    public static void SetForSmoke(string id) { testOverride = Valid(id); }
#endif
    public static string SelectedId
    {
        get
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (testOverride != null) return testOverride;
#endif
            return Valid(PlayerPrefs.GetString(PrefKey, DefaultId));
        }
    }
    public static void Select(string id)
    {
        string normalized = Valid(id);
        PlayerPrefs.SetString(PrefKey, normalized);
        PlayerPrefs.Save();
    }

    public GameObject Create(string id, Transform parent)
    {
        string normalized = Valid(id);
        int index = Array.IndexOf(Ids, normalized);
        if (prefabs == null || index >= prefabs.Length || prefabs[index] == null)
        {
            index = Array.IndexOf(Ids, DefaultId);
            if (prefabs == null || index >= prefabs.Length || prefabs[index] == null) return null;
        }
        var model = Instantiate(prefabs[index], parent);
        model.name = "Avatar_" + normalized;
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        var renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) { Destroy(model); return null; }
        Bounds bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        model.transform.localScale *= 1.05f / Mathf.Max(.001f, bounds.size.y);
        bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        model.transform.position += new Vector3(parent.position.x - bounds.center.x,
            parent.position.y - bounds.min.y, parent.position.z - bounds.center.z);
        var animator = model.GetComponentInChildren<Animator>();
        if (animator != null) animator.applyRootMotion = false;
        return model;
    }
}

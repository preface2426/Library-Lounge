using System;
using System.Runtime.InteropServices;
using UnityEngine;

// Budget covered/minimized rendering without suspending capture, voice or native hotkeys.
[DefaultExecutionOrder(500)]
public sealed class CafeRenderBudget : MonoBehaviour
{
    CafePrototype cafe;
    CafeEntryUi entry;
    int originalVSync, originalTarget, policy = -1;
    bool initialized, cameraSuppressed;
    IntPtr window;
    float nextWindowLookup;
    delegate bool WindowCallback(IntPtr handle, IntPtr value);
    [DllImport("user32.dll")] static extern bool EnumWindows(WindowCallback callback, IntPtr value);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr handle, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr handle, System.Text.StringBuilder name, int count);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr handle);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr handle);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Attach()
    {
        var owner = FindFirstObjectByType<CafePrototype>();
        if (owner != null && owner.GetComponent<CafeRenderBudget>() == null)
            owner.gameObject.AddComponent<CafeRenderBudget>();
    }
    void Start()
    {
        cafe = GetComponent<CafePrototype>(); entry = GetComponent<CafeEntryUi>();
        originalVSync = QualitySettings.vSyncCount; originalTarget = Application.targetFrameRate;
        initialized = true;
    }
    bool Minimized()
    {
        if (Application.platform != RuntimePlatform.WindowsPlayer || Application.isBatchMode) return false;
        if (window != IntPtr.Zero && IsWindow(window)) return IsIconic(window);
        if (Time.unscaledTime < nextWindowLookup) return false;
        nextWindowLookup = Time.unscaledTime + 1;
        uint ownPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        EnumWindows((handle, _) => {
            GetWindowThreadProcessId(handle, out var pid);
            if (pid != ownPid) return true;
            var name = new System.Text.StringBuilder(80); GetClassName(handle, name, 80);
            if (name.ToString() != "UnityWndClass") return true;
            window = handle; return false;
        }, IntPtr.Zero);
        return window != IntPtr.Zero && IsIconic(window);
    }
    void LateUpdate()
    {
        if (!initialized || cafe == null) return;
        bool minimized = Minimized();
        bool covered = entry != null && entry.Ready && !entry.TransitionActive &&
            entry.ScreenshotOverlayRoot != null && entry.ScreenshotOverlayRoot.activeInHierarchy;
        bool suppress = minimized || covered;
        if (cafe.eyes != null)
        {
            if (suppress && cafe.eyes.enabled) { cafe.eyes.enabled = false; cameraSuppressed = true; }
            else if (!suppress && cameraSuppressed) { cafe.eyes.enabled = true; cameraSuppressed = false; }
        }
        int next = minimized ? 2 : covered ? 1 : 0;
        if (policy == next) return;
        policy = next;
        QualitySettings.vSyncCount = next == 0 ? originalVSync : 0;
        Application.targetFrameRate = next == 0 ? originalTarget : next == 1 ? 60 : 30;
    }
    void OnDisable()
    {
        if (!initialized) return;
        if (cameraSuppressed && cafe != null && cafe.eyes != null) cafe.eyes.enabled = true;
        cameraSuppressed = false; policy = -1;
        QualitySettings.vSyncCount = originalVSync; Application.targetFrameRate = originalTarget;
    }
}

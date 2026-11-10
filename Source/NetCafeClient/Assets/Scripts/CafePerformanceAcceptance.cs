#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using TMPro;
using UnityEngine;

// Bounded full-UI checks. Never publishes desktop pixels or opens a microphone.
public sealed class CafePerformanceAcceptance : MonoBehaviour
{
    int checks, failures, savedMotion;
    CafePrototype cafe;
    CafeEntryUi entry;
    delegate bool WindowCallback(IntPtr window, IntPtr value);
    [DllImport("user32.dll")] static extern bool EnumWindows(WindowCallback callback, IntPtr value);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window, System.Text.StringBuilder name, int count);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Attach()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-performanceAcceptance") < 0) return;
        FindFirstObjectByType<CafePrototype>()?.gameObject.AddComponent<CafePerformanceAcceptance>();
    }
    void Check(bool value, string name)
    {
        checks++; if (!value) failures++;
        Debug.Log((value ? "PERF_PASS " : "PERF_FAIL ") + name);
    }
    static object Field(object owner, string name) => owner.GetType().GetField(name, BindingFlags.Instance|BindingFlags.NonPublic).GetValue(owner);
    static void Call(object owner, string name) => owner.GetType().GetMethod(name, BindingFlags.Instance|BindingFlags.NonPublic).Invoke(owner, null);
    IntPtr OwnWindow()
    {
        IntPtr found = IntPtr.Zero;
        uint ownPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        EnumWindows((window, _) => {
            GetWindowThreadProcessId(window, out var pid);
            var name = new System.Text.StringBuilder(80); GetClassName(window, name, 80);
            if (pid != ownPid || name.ToString() != "UnityWndClass") return true;
            found = window; return false;
        }, IntPtr.Zero);
        return found;
    }
    IEnumerator Start()
    {
        cafe = GetComponent<CafePrototype>(); entry = GetComponent<CafeEntryUi>();
        savedMotion = PlayerPrefs.GetInt("CafeReduceMotion", 0);
        yield return new WaitForSecondsRealtime(2);
        var window = OwnWindow();
        Check(window != IntPtr.Zero, "owned Unity window exists");
        ShowWindow(window, 9); SetForegroundWindow(window);
        yield return new WaitForSecondsRealtime(.5f);
        Check(entry.Ready && entry.BlocksRoomMenu, "full home UI ready");
        Check(!cafe.eyes.enabled, "opaque home skips hidden scene rendering");
        Check(QualitySettings.vSyncCount == 0 && Application.targetFrameRate == 60, "home animation has 60Hz budget");
        foreach (string name in new[] { "NotoSansSC-Dynamic", "NotoSerifSC-Dynamic" })
        {
            var font = Resources.Load<TMP_FontAsset>("LoungeFonts/" + name);
            Check(font != null && font.atlasTextureCount <= 2, "font avoids five prefilled atlases " + name);
            string missing;
            Check(font.TryAddCharacters("中文昵称优雅房间麦克风设置龘", out missing), "dynamic Chinese and uncommon nickname glyphs " + name);
        }
        ShowWindow(window, 6); yield return new WaitForSecondsRealtime(.8f);
        Check(!cafe.eyes.enabled && Application.targetFrameRate == 30 && QualitySettings.vSyncCount == 0, "actual minimized window reduces rendering budget");
        Check(Application.runInBackground, "background capture and hotkey polling stay enabled");
        ShowWindow(window, 9); SetForegroundWindow(window); yield return new WaitForSecondsRealtime(.8f);
        Check(!cafe.eyes.enabled && Application.targetFrameRate == 60, "restore home restores UI budget");
        PlayerPrefs.SetInt("CafeReduceMotion", 0);
        Call(entry, "EnterSingle"); yield return new WaitForSecondsRealtime(.3f);
        var transition = GetComponent<CafeRoomTransition>();
        Check(transition.Active && cafe.eyes.enabled, "entry transition restores scene camera");
        var frame = Field(transition, "frame") as Texture2D;
        Check(frame != null && frame.width > 0, "transition captures destination scene");
        yield return new WaitForSecondsRealtime(3);
        Check(!transition.Active && cafe.eyes.enabled && cafe.Mode == CafePrototype.InputMode.Walking, "natural completion restores walking and camera");
        Check((Texture2D)Field(transition, "frame") == null && frame == null, "natural completion releases captured texture");
        Check(QualitySettings.vSyncCount == 1 && Application.targetFrameRate == -1, "visible room restores original frame policy");
        entry.OpenMembers(); yield return new WaitForSecondsRealtime(.3f);
        Check(!cafe.eyes.enabled, "opaque members page skips covered scene");
        Call(entry, "CloseModal"); yield return new WaitForSecondsRealtime(.3f);
        Check(cafe.eyes.enabled, "closing members restores scene");
        entry.ReturnHome(); yield return new WaitForSecondsRealtime(.3f);
        Check(transition.Active && transition.Leaving, "exit transition remains animated");
        frame = Field(transition, "frame") as Texture2D;
        transition.Finish(); yield return null; yield return null;
        Check((Texture2D)Field(transition, "frame") == null && frame == null, "skip releases captured texture");
        Check(!cafe.eyes.enabled && entry.BlocksRoomMenu, "exit returns to opaque page without rendering scene");
        SetForegroundWindow(window); yield return new WaitForSecondsRealtime(.2f);
        Call(entry, "EnterSingle"); yield return new WaitForSecondsRealtime(.2f);
        frame = Field(transition, "frame") as Texture2D;
        Check(transition.Active && frame != null, "second transition has its own frame");
        transition.Cancel(); yield return null; yield return null;
        Check((Texture2D)Field(transition, "frame") == null && frame == null, "cancel releases captured texture");
        PlayerPrefs.SetInt("CafeReduceMotion", savedMotion); PlayerPrefs.Save();
        Debug.Log("PERFORMANCE_ACCEPTANCE_" + (failures == 0 ? "PASS" : "FAIL") + " checks=" + checks + " failures=" + failures);
        Application.Quit(failures == 0 ? 0 : 1);
    }
}
#endif

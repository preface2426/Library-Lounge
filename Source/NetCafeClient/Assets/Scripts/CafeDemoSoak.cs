using System;
using System.Collections;
using System.Diagnostics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 30-minute GUI state/resource soak. No OS input injection or desktop capture.
// Deliberately NOT evidence for real LiveKit audio/video or 6 physical clients.
public sealed class CafeDemoSoak : MonoBehaviour
{
    const double Duration = 1800.0;
    IEnumerator Start()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-demoSoak") < 0)
        {
            Destroy(this);
            yield break;
        }
        var entry = GetComponent<CafeEntryUi>();
        var cafe = GetComponent<CafePrototype>();
        var network = GetComponent<NetCafeNetworkSession>();
        var voice = GetComponent<CafeSpatialVoice>();
        while (entry == null || !entry.Ready) yield return null;
        Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
        yield return new WaitForSecondsRealtime(1f);
        foreach (var button in entry.GetComponentsInChildren<Button>(true))
        {
            if (!button.gameObject.activeInHierarchy) continue;
            var title = button.GetComponentInChildren<TMP_Text>(true);
            if (title != null && title.text == "单人体验")
            {
                button.onClick.Invoke();
                break;
            }
        }
        if (entry.BlocksRoomMenu || cafe.Mode != CafePrototype.InputMode.Walking)
        {
            UnityEngine.Debug.LogError("DEMO_SOAK_FAIL: unable to enter single room");
            Application.Quit(1);
            yield break;
        }
        var watch = Process.GetCurrentProcess();
        double started = Time.realtimeSinceStartupAsDouble;
        double last = started;
        int frames = Time.frameCount, cycles = 0, samples = 0, minFrames = int.MaxValue;
        long minimumPrivate = long.MaxValue, maximumPrivate = 0, baseline = 0, final = 0;
        UnityEngine.Debug.Log("DEMO_30MIN_SOAK_START: 1800s GUI, room/menu/six-seat cycles, no native input or LiveKit");
        while (Time.realtimeSinceStartupAsDouble - started < Duration)
        {
            int seat = cycles % cafe.screens.Length;
            cafe.Sit(seat);
            yield return new WaitForSecondsRealtime(2f);
            cafe.Observe();
            yield return new WaitForSecondsRealtime(2f);
            cafe.Stand();
            cafe.OpenMenu();
            yield return new WaitForSecondsRealtime(1f);
            cafe.Continue();
            cycles++;
            while (Time.realtimeSinceStartupAsDouble - last < 10)
                yield return null;
            watch.Refresh();
            double now = Time.realtimeSinceStartupAsDouble, dt = now - last;
            int deltaFrames = Time.frameCount - frames;
            double fps = deltaFrames / Math.Max(.001, dt);
            frames = Time.frameCount; last = now;
            minFrames = Math.Min(minFrames, deltaFrames);
            long memory = watch.PrivateMemorySize64;
            if (baseline == 0) baseline = memory;
            final = memory;
            minimumPrivate = Math.Min(minimumPrivate, memory);
            maximumPrivate = Math.Max(maximumPrivate, memory);
            samples++;
            UnityEngine.Debug.Log("DEMO_SOAK_METRIC: seconds=" +
                (now - started).ToString("F1") +
                "; cycles=" + cycles +
                "; privateMiB=" + (memory / 1048576d).ToString("F1") +
                "; managedMiB=" + (GC.GetTotalMemory(false) / 1048576d).ToString("F1") +
                "; fps=" + fps.ToString("F1") +
                "; voiceTracks=" + (voice == null ? -1 : voice.RemoteAudioCount) +
                "; microphone=" + (voice != null && voice.MicrophoneActive));
        }
        watch.Dispose();
        UnityEngine.Debug.Log("DEMO_30MIN_SOAK_PASS: seconds=" +
            (Time.realtimeSinceStartupAsDouble - started).ToString("F1") +
            "; cycles=" + cycles +
            "; samples=" + samples +
            "; baselineMiB=" + (baseline / 1048576d).ToString("F1") +
            "; finalMiB=" + (final / 1048576d).ToString("F1") +
            "; minMiB=" + (minimumPrivate / 1048576d).ToString("F1") +
            "; peakMiB=" + (maximumPrivate / 1048576d).ToString("F1"));
        Application.Quit(0);
    }
}

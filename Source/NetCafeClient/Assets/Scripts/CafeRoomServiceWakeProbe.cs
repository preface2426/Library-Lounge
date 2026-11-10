#if NETCAFE_B1 && (UNITY_EDITOR || DEVELOPMENT_BUILD || NETCAFE_ACCEPTANCE)
using System;
using System.Collections;
using System.IO;
using TMPro;
using UnityEngine;

// Isolated process acceptance, activated only by an explicit command-line flag.
public sealed class CafeRoomServiceWakeProbe : MonoBehaviour
{
    static string Argument(string prefix)
    {
        foreach (var arg in Environment.GetCommandLineArgs())
            if (arg.StartsWith(prefix, StringComparison.Ordinal)) return arg.Substring(prefix.Length);
        return "";
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (!string.IsNullOrEmpty(Argument("-roomServiceWakeProbe=")))
            new GameObject("RoomServiceWakeAcceptance").AddComponent<CafeRoomServiceWakeProbe>();
    }
    IEnumerator Start()
    {
        yield return null;
        var network = FindFirstObjectByType<NetCafeNetworkSession>();
        string mode = Argument("-roomServiceWakeProbe=");
        if (network == null || !Uri.TryCreate(network.RoomServiceUrl, UriKind.Absolute, out var uri) ||
            (!uri.IsLoopback && !(uri.Scheme == "https" && mode == "cancelWeb")) ||
            (mode != "create" && mode != "join" && mode != "cancel" && mode != "cancelWeb" &&
             mode != "timeout" && mode != "forbidden" && mode != "restart"))
        { Debug.LogError("ROOM_SERVICE_WAKE_FAIL CONFIGURATION"); Application.Quit(1); yield break; }
        string invite = "";
        if (mode == "join") invite = File.ReadAllText(Argument("-roomServiceWakeInviteFile=")).Trim();
        network.SetEntryFields("Wake acceptance", invite);
        if (mode == "join") network.JoinRoom(); else network.CreateRoom();
        if (mode == "cancelWeb")
        {
            bool pending = network.WaitingForRoomService && network.PendingWebRequestsForSmoke > 0;
            network.Leave();
            yield return new WaitForSecondsRealtime(5f);
            Finish(pending && network.PendingWebRequestsForSmoke == 0 &&
                   network.State == NetCafeNetworkSession.Phase.Disconnected && string.IsNullOrEmpty(network.RoomId), mode);
            yield break;
        }
        if (mode != "forbidden")
        {
            yield return new WaitForSecondsRealtime(3f);
            var entry = FindFirstObjectByType<CafeEntryUi>();
            bool estimateVisible = false;
            if (entry != null && entry.ScreenshotOverlayRoot != null)
                foreach (var label in entry.ScreenshotOverlayRoot.GetComponentsInChildren<TMP_Text>())
                    if (label.text.Contains("最长 90 秒") && label.text.Contains("已等待 ") &&
                        label.text.Contains("约 1 分钟")) estimateVisible = true;
            if (!estimateVisible)
            { network.Leave(); Finish(false, "WAIT_ESTIMATE"); yield break; }
            string evidence = Argument("-roomServiceWakeEvidence=");
            if (!string.IsNullOrEmpty(evidence) && Path.IsPathRooted(evidence) && Directory.Exists(evidence))
                ScreenCapture.CaptureScreenshot(Path.Combine(evidence, "waiting-estimate.png"));
            Debug.Log("ROOM_SERVICE_WAKE_ESTIMATE_PASS");
        }
        if (mode == "cancel" || mode == "restart")
        {
            network.Leave();
            if (mode == "restart") network.CreateRoom();
            else
            {
                yield return new WaitForSecondsRealtime(5f);
                Finish(network.State == NetCafeNetworkSession.Phase.Disconnected, mode); yield break;
            }
        }
        float deadline = Time.realtimeSinceStartup + 135f;
        while (network.State != NetCafeNetworkSession.Phase.Connected &&
               network.State != NetCafeNetworkSession.Phase.Failed && Time.realtimeSinceStartup < deadline)
            yield return null;
        bool passed = mode == "timeout" ? network.Error == "SERVICE_WAKE_TIMEOUT" :
                      mode == "forbidden" ? network.Error == "HTTP_403" :
                      network.State == NetCafeNetworkSession.Phase.Connected;
        network.Leave();
        float exitDeadline = Time.realtimeSinceStartup + 35f;
        while (network.State != NetCafeNetworkSession.Phase.Disconnected && Time.realtimeSinceStartup < exitDeadline)
            yield return null;
        Finish(passed && network.State == NetCafeNetworkSession.Phase.Disconnected, mode);
    }
    static void Finish(bool passed, string mode)
    {
        Debug.Log((passed ? "ROOM_SERVICE_WAKE_PASS " : "ROOM_SERVICE_WAKE_FAIL ") + mode);
        Application.Quit(passed ? 0 : 1);
    }
}
#endif

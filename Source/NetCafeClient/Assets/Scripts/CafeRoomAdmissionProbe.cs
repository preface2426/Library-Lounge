#if NETCAFE_B1 && (UNITY_EDITOR || DEVELOPMENT_BUILD || NETCAFE_ACCEPTANCE)
using System;
using System.Collections;
using UnityEngine;

// Explicit loopback-only failure acceptance. Never runs on ordinary startup.
public sealed class CafeRoomAdmissionProbe : MonoBehaviour
{
    static string Mode
    {
        get
        {
            foreach (string arg in Environment.GetCommandLineArgs())
                if (arg.StartsWith("-roomAdmissionProbe=", StringComparison.Ordinal))
                    return arg.Substring("-roomAdmissionProbe=".Length);
            return "";
        }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (Mode == "cleanup" || Mode == "cancel-cleanup" || Mode == "late-connect")
            new GameObject("RoomAdmissionAcceptance").AddComponent<CafeRoomAdmissionProbe>();
    }
    IEnumerator Start()
    {
        yield return null;
        var network = FindFirstObjectByType<NetCafeNetworkSession>();
        if (network == null || !Uri.TryCreate(network.RoomServiceUrl, UriKind.Absolute, out var uri) || !uri.IsLoopback)
        { Debug.LogError("ROOM_ADMISSION_CLEANUP_FAIL CONFIGURATION"); Application.Quit(1); yield break; }
        network.SetEntryFields("Admission acceptance", "");
        network.CreateRoom();
        if (Mode == "late-connect")
        {
            float startDeadline = Time.realtimeSinceStartup + 25f;
            while (network.PendingConnectionsForSmoke == 0 && Time.realtimeSinceStartup < startDeadline)
                yield return null;
            bool pending = network.PendingConnectionsForSmoke > 0;
            network.Leave();
            float finishDeadline = Time.realtimeSinceStartup + 45f;
            while ((network.PendingConnectionsForSmoke > 0 ||
                network.State != NetCafeNetworkSession.Phase.Disconnected) && Time.realtimeSinceStartup < finishDeadline)
                yield return null;
            bool releasedWhileAlive = false;
            using (var probe = new CafeLoopbackHttp(network.RoomServiceUrl + "/probe/late-release-verified", "GET"))
            {
                float verifyDeadline = Time.realtimeSinceStartup + 12f;
                while (!probe.Pending.IsCompleted && Time.realtimeSinceStartup < verifyDeadline) yield return null;
                if (probe.Pending.IsCompleted)
                    releasedWhileAlive = probe.Pending.GetAwaiter().GetResult().Body == "true";
            }
            Finish(releasedWhileAlive && pending && network.ReleasedLateConnectionsForSmoke == 1 &&
                network.PendingConnectionsForSmoke == 0 && !network.HasTransportForSmoke &&
                network.State == NetCafeNetworkSession.Phase.Disconnected && string.IsNullOrEmpty(network.RoomId),
                "LATE_CONNECT");
            yield break;
        }
        if (Mode == "cancel-cleanup")
        {
            bool cleanupStarted = false;
            float cancelDeadline = Time.realtimeSinceStartup + 50f;
            while (!cleanupStarted && Time.realtimeSinceStartup < cancelDeadline)
            {
                using (var probe = new CafeLoopbackHttp(network.RoomServiceUrl + "/probe/cleanup-started", "GET"))
                {
                    while (!probe.Pending.IsCompleted && Time.realtimeSinceStartup < cancelDeadline) yield return null;
                    if (probe.Pending.IsCompleted)
                        cleanupStarted = probe.Pending.GetAwaiter().GetResult().Body == "true";
                }
                if (!cleanupStarted) yield return new WaitForSecondsRealtime(.1f);
            }
            network.Leave();
            float leaveDeadline = Time.realtimeSinceStartup + 15f;
            while (network.State != NetCafeNetworkSession.Phase.Disconnected && Time.realtimeSinceStartup < leaveDeadline)
                yield return null;
            Finish(cleanupStarted && network.State == NetCafeNetworkSession.Phase.Disconnected &&
                string.IsNullOrEmpty(network.RoomId) && !network.HasTransportForSmoke &&
                network.PendingConnectionsForSmoke == 0, "CANCEL_CLEANUP");
            yield break;
        }
        float deadline = Time.realtimeSinceStartup + 70f;
        while (network.State != NetCafeNetworkSession.Phase.Failed && Time.realtimeSinceStartup < deadline)
            yield return null;
        bool passed = network.State == NetCafeNetworkSession.Phase.Failed &&
            network.Error == "LIVEKIT_CONNECT_FAILED" && string.IsNullOrEmpty(network.RoomId) &&
            !network.HasTransportForSmoke && network.PendingConnectionsForSmoke == 0;
        network.Leave();
        Finish(passed, "CLEANUP");
    }
    static void Finish(bool passed, string mode)
    {
        Debug.Log("ROOM_ADMISSION_" + mode + (passed ? "_PASS" : "_FAIL"));
        Application.Quit(passed ? 0 : 1);
    }
}
#endif

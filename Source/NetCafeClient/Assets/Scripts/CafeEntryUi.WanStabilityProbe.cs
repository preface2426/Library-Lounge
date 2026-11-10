#if NETCAFE_B1 && (UNITY_EDITOR || DEVELOPMENT_BUILD || NETCAFE_ACCEPTANCE)
using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class CafeEntryUi
{
    IEnumerator WanStabilityProbe(string mode)
    {
        yield return null;
        if (network == null || !CafeLocalHostRuntime.IsLoopbackUrl(network.RoomServiceUrl) ||
            (mode != "retry" && mode != "join" && mode != "ready" && mode != "transport" &&
             mode != "cancel" && mode != "confirm" && mode != "custom"))
        { FinishWanProbe(mode, false); yield break; }

        bool join = mode == "join";
        network.AllowLoopbackReservationRetryForProbe = mode == "retry" || mode == "cancel";
        network.SetEntryFields("fixture-private-nickname", "fixture-private-invite");
        Show(Page.Busy);
        if (join) network.JoinRoom(); else network.CreateRoom();
        if (mode == "cancel")
        {
            float postedBy = Time.realtimeSinceStartup + 25f;
            while (network.CurrentAdmissionStep != NetCafeNetworkSession.AdmissionStep.Reservation &&
                   Time.realtimeSinceStartup < postedBy) yield return null;
            bool started = network.CurrentAdmissionStep == NetCafeNetworkSession.AdmissionStep.Reservation;
            yield return new WaitForSecondsRealtime(.5f);
            CancelConnect();
            yield return new WaitForSecondsRealtime(2f);
            FinishWanProbe(mode, started && network.State == NetCafeNetworkSession.Phase.Disconnected &&
                network.PendingWebRequestsForSmoke == 0 && string.IsNullOrEmpty(network.AdmissionDiagnostic));
            yield break;
        }
        float deadline = Time.realtimeSinceStartup + 70f;
        while (network.State != NetCafeNetworkSession.Phase.Failed && Time.realtimeSinceStartup < deadline)
            yield return null;
        // The real lobby applies failure on its next Update.
        yield return null;
        yield return null;
        var button = overlayCanvas.GetComponentsInChildren<Button>().FindByLabel("复制诊断");
        if (button == null) { FinishWanProbe(mode, false); yield break; }
        string previous = GUIUtility.systemCopyBuffer;
        string diagnostic;
        try
        {
            button.onClick.Invoke();
            diagnostic = GUIUtility.systemCopyBuffer;
        }
        finally { GUIUtility.systemCopyBuffer = previous; }
        string expected = mode switch
        {
            "join" => "开房或加入请求 / UNKNOWN_ERROR",
            "transport" => "LiveKit 连接 / LIVEKIT_CONNECT_FAILED",
            "confirm" => "入房确认 / ROOM_CONFIRM_NO_RESPONSE",
            "custom" => "开房或加入请求 / ROOM_API_NO_RESPONSE",
            _ => "开房或加入请求 / HTTP_403"
        };
        bool passed = diagnostic.Contains("失败阶段: " + expected) && diagnostic.Contains("UTC: ") &&
            diagnostic.Contains("总耗时: ") && diagnostic.Contains(join ? "操作: 加入房间" : "操作: 创建房间") &&
            !diagnostic.Contains("fixture-", StringComparison.Ordinal) && !diagnostic.Contains("127.0.0.1") &&
            !diagnostic.Contains(new string('a', 32)) &&
            (mode != "retry" || diagnostic.Contains("开房或加入请求: 2 次")) &&
            (mode != "ready" || diagnostic.Contains("LiveKit 就绪检查: 3 次")) &&
            overlayCanvas.GetComponentsInChildren<Button>().FindByLabel("诊断已复制") != null;
        FinishWanProbe(mode, passed);
    }

    static void FinishWanProbe(string mode, bool passed)
    {
        Debug.Log((passed ? "WAN_STABILITY_PASS " : "WAN_STABILITY_FAIL ") + mode);
        Application.Quit(passed ? 0 : 1);
    }
}
static class CafeWanProbeButtons
{
    public static Button FindByLabel(this Button[] buttons, string label) =>
        Array.Find(buttons, b => b.GetComponentInChildren<TMP_Text>()?.text == label);
}
#endif

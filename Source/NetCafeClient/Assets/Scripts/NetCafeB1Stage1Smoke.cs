#if NETCAFE_B1 && (UNITY_EDITOR || DEVELOPMENT_BUILD)
using System;
using System.Collections;
using System.IO;
using UnityEngine;

// Opt-in B1 acceptance runner; never starts without explicit process arguments.
// No credentials, media tracks, or desktop contents are written to disk.
public sealed class NetCafeB1Stage1Smoke : MonoBehaviour
{
    private NetCafeNetworkSession network;
    private string role;
    private string inviteFile;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallIfRequested()
    {
        string selected = Argument("-b1SmokeRole=");
        if (selected != "host" && selected != "guest") return;
        var net = FindFirstObjectByType<NetCafeNetworkSession>();
        if (net == null) { Debug.LogError("B1_SMOKE_FAIL missing network session"); Application.Quit(1); return; }
        var smoke = net.gameObject.AddComponent<NetCafeB1Stage1Smoke>();
        smoke.network = net;
        smoke.role = selected;
        smoke.inviteFile = Argument("-b1InviteFile=");
    }

    private static string Argument(string prefix)
    {
        foreach (string arg in Environment.GetCommandLineArgs())
            if (arg.StartsWith(prefix, StringComparison.Ordinal))
                return arg.Substring(prefix.Length);
        return "";
    }

    private IEnumerator Start()
    {
        if (string.IsNullOrWhiteSpace(inviteFile))
        {
            Fail("invite file argument missing");
            yield break;
        }
        yield return null;
        if (role == "host") yield return Host();
        else yield return Guest();
    }

    private IEnumerator Until(Func<bool> condition, float seconds, string checkName)
    {
        float stop = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < stop)
        {
            if (network.State == NetCafeNetworkSession.Phase.Failed)
            {
                Fail(checkName + " " + network.Error);
                yield break;
            }
            yield return null;
        }
        if (!condition()) Fail(checkName + " timeout, state=" + network.State);
    }

    private IEnumerator Host()
    {
        network.ConfigureForSmoke("B1Host", "");
        network.CreateRoom();
        yield return Until(() => network.State == NetCafeNetworkSession.Phase.Connected, 30, "host-connect");
        if (failed) yield break;
        Directory.CreateDirectory(Path.GetDirectoryName(inviteFile));
        // File.Exists can become true before WriteAllText finishes on Windows.
        // Publish via an atomic same-directory rename so the guest never sees
        // a partially written or exclusively locked invite file.
        string temp = inviteFile + "." + System.Diagnostics.Process.GetCurrentProcess().Id + ".tmp";
        File.WriteAllText(temp, network.InviteCode);
        File.Move(temp, inviteFile);
        Debug.Log("B1_SMOKE_HOST_CONNECTED");
        yield return Until(() => network.RemotePeersForSmoke >= 1, 35, "host-peer");
        if (failed) yield break;
        Debug.Log("B1_SMOKE_HOST_SEES_GUEST");
        yield return Until(() => network.RemotePeersForSmoke == 0, 30, "host-guest-left");
        if (failed) yield break;
        Debug.Log("B1_SMOKE_GUEST_LEFT");
        yield return Until(() => network.RemotePeersForSmoke >= 1, 35, "host-guest-rejoined");
        if (failed) yield break;
        Debug.Log("B1_SMOKE_HOST_SEES_REJOIN");
        yield return Until(() => network.RemotePeersForSmoke == 0, 30, "host-guest-final-left");
        if (failed) yield break;
        network.Leave();
        yield return Until(() => network.State == NetCafeNetworkSession.Phase.Disconnected, 20, "host-left");
        if (failed) yield break;
        Debug.Log("B1_SMOKE_HOST_PASS");
        Application.Quit(0);
    }

    private IEnumerator Guest()
    {
        yield return Until(() => File.Exists(inviteFile), 35, "invite-file");
        if (failed) yield break;
        string code = "";
        float deadline = Time.realtimeSinceStartup + 5f;
        while (Time.realtimeSinceStartup < deadline)
        {
            try { code = File.ReadAllText(inviteFile).Trim(); }
            catch (IOException) { /* antivirus/file sharing transient; retry */ }
            if (!string.IsNullOrEmpty(code)) break;
            yield return null;
        }
        if (string.IsNullOrEmpty(code)) { Fail("invite-unreadable-or-empty"); yield break; }
        network.ConfigureForSmoke("B1Guest", code);
        network.JoinRoom();
        yield return Until(() => network.State == NetCafeNetworkSession.Phase.Connected, 35, "guest-connect");
        if (failed) yield break;
        yield return Until(() => network.RemotePeersForSmoke >= 1, 15, "guest-peer");
        if (failed) yield break;
        Debug.Log("B1_SMOKE_GUEST_SEES_HOST");
        yield return new WaitForSecondsRealtime(3);
        network.Leave();
        yield return Until(() => network.State == NetCafeNetworkSession.Phase.Disconnected, 20, "guest-left");
        if (failed) yield break;
        yield return new WaitForSecondsRealtime(3);
        network.ConfigureForSmoke("B1GuestAgain", code);
        network.JoinRoom();
        yield return Until(() => network.State == NetCafeNetworkSession.Phase.Connected, 35, "guest-rejoin");
        if (failed) yield break;
        yield return Until(() => network.RemotePeersForSmoke >= 1, 15, "guest-rejoin-peer");
        if (failed) yield break;
        Debug.Log("B1_SMOKE_GUEST_REJOINED");
        yield return new WaitForSecondsRealtime(5);
        network.Leave();
        yield return Until(() => network.State == NetCafeNetworkSession.Phase.Disconnected, 20, "guest-final-left");
        if (failed) yield break;
        Debug.Log("B1_SMOKE_GUEST_PASS");
        Application.Quit(0);
    }

    private bool failed;
    private void Fail(string reason)
    {
        if (failed) return;
        failed = true;
        Debug.LogError("B1_SMOKE_FAIL " + reason);
        Application.Quit(1);
    }
}
#endif

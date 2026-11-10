#if NETCAFE_B1 && (UNITY_EDITOR || DEVELOPMENT_BUILD)
using System;
using System.Collections;
using System.IO;
using LiveKit;
using LiveKit.Proto;
using UnityEngine;

// Two real Unity processes over the configured HTTPS room API and LiveKit Cloud.
// Only a generated checkerboard is published; no actual desktop is captured.
// Runs only when explicitly launched with the two b2Synthetic* command flags.
public sealed class CafeB2SyntheticSmoke : MonoBehaviour
{
    NetCafeNetworkSession network;
    CafePrototype cafe;
    CafeDesktopCapture capture;
    CafeDesktopSharing sharing;
    Texture2D pattern;
    TextureVideoSource source;
    LocalVideoTrack track;
    Coroutine videoLoop;

    void Start()
    {
        network = GetComponent<NetCafeNetworkSession>();
        cafe = GetComponent<CafePrototype>();
        capture = GetComponent<CafeDesktopCapture>();
        sharing = GetComponent<CafeDesktopSharing>();
        string codeFile = Argument("-b2SmokeCodeFile=");
        string resultFile = Argument("-b2SmokeResultFile=");
        bool host = Array.IndexOf(Environment.GetCommandLineArgs(), "-b2SyntheticHost") >= 0;
        if (string.IsNullOrEmpty(codeFile) || string.IsNullOrEmpty(resultFile) ||
            !network || !cafe || !sharing || (capture != null && capture.Active))
        {
            Debug.LogError("B2_SYNTHETIC_FAIL INVALID_FIXTURE_OR_NATIVE_CAPTURE");
            Application.Quit(1); return;
        }
        StartCoroutine(Run(host, codeFile, resultFile));
    }

    static string Argument(string prefix)
    {
        foreach (string a in Environment.GetCommandLineArgs())
            if (a.StartsWith(prefix, StringComparison.Ordinal)) return a.Substring(prefix.Length);
        return "";
    }

    IEnumerator Run(bool host, string codeFile, string resultFile)
    {
        if (!network.RoomServiceUrl.StartsWith("https://", StringComparison.Ordinal))
        {
            Debug.LogError("B2_SYNTHETIC_FAIL NOT_HTTPS");
            Application.Quit(1); yield break;
        }
        if (!host)
        {
            float deadline = Time.realtimeSinceStartup + 65f;
            while (!File.Exists(codeFile) && Time.realtimeSinceStartup < deadline) yield return null;
            if (!File.Exists(codeFile)) { Debug.LogError("B2_SYNTHETIC_FAIL NO_INVITE"); Application.Quit(1); yield break; }
            string code = File.ReadAllText(codeFile).Trim();
            network.SetEntryFields("Synthetic Video Receiver", code);
            network.JoinRoom();
        }
        else
        {
            network.SetEntryFields("Synthetic Video Source", "");
            network.CreateRoom();
        }
        float connectedDeadline = Time.realtimeSinceStartup + 65f;
        while (network.State != NetCafeNetworkSession.Phase.Connected &&
               network.State != NetCafeNetworkSession.Phase.Failed &&
               network.State != NetCafeNetworkSession.Phase.Disconnected &&
               Time.realtimeSinceStartup < connectedDeadline) yield return null;
        if (network.State != NetCafeNetworkSession.Phase.Connected)
        {
            Debug.LogError("B2_SYNTHETIC_FAIL NO_ROOM_CONNECTION");
            Application.Quit(1); yield break;
        }
        if (host)
        {
            // The self-test flag suppresses AutoStart, so the REAL desktop never
            // starts capturing. The checkerboard simulates the published pixels.
            cafe.Sit(0);
            if (capture != null && capture.Active) { Debug.LogError("B2_SYNTHETIC_FAIL DESKTOP_ACTIVE"); Application.Quit(1); yield break; }
            pattern = new Texture2D(128, 72, TextureFormat.RGBA32, false);
            for (int y=0; y<72; y++) for (int x=0; x<128; x++)
                pattern.SetPixel(x,y,(x/16+y/16)%2==0 ? Color.red : Color.cyan);
            pattern.Apply();
            source = new TextureVideoSource(pattern);
            Room origin = network.TransportRoom;
            track = LocalVideoTrack.CreateVideoTrack("synthetic-smoke-pattern", source, origin);
            var publish = origin.LocalParticipant.PublishTrack(track,new TrackPublishOptions {
                VideoCodec = VideoCodec.Vp8, Simulcast = false,
                Source = TrackSource.SourceScreenshare,
                VideoEncoding = new VideoEncoding {MaxBitrate=250000,MaxFramerate=6}
            });
            yield return publish;
            if (publish.IsError) { Debug.LogError("B2_SYNTHETIC_FAIL PUBLISH"); Application.Quit(1); yield break; }
            source.Start(); videoLoop = StartCoroutine(source.Update());
            Debug.Log("B2_SYNTHETIC_PUBLISHED_PASS");
            Directory.CreateDirectory(Path.GetDirectoryName(codeFile));
            File.WriteAllText(codeFile, network.InviteCode); // local-only transient invitation, cleaned by test runner
            float deadline = Time.realtimeSinceStartup + 75f;
            while (!File.Exists(resultFile) && Time.realtimeSinceStartup < deadline) yield return null;
            bool done = File.Exists(resultFile) && File.ReadAllText(resultFile).Trim() == "PASS";
            StopPublish();
            network.Leave();
            float exit = Time.realtimeSinceStartup + 30f;
            while (network.State != NetCafeNetworkSession.Phase.Disconnected && Time.realtimeSinceStartup < exit) yield return null;
            if (done && network.State == NetCafeNetworkSession.Phase.Disconnected)
                Debug.Log("B2_SYNTHETIC_TWO_CLIENT_PASS");
            else Debug.LogError("B2_SYNTHETIC_FAIL REMOTE_FRAME_OR_CLEANUP");
            Application.Quit(done && network.State == NetCafeNetworkSession.Phase.Disconnected ? 0 : 1);
        }
        else
        {
            float deadline = Time.realtimeSinceStartup + 55f;
            while ((sharing.RemoteReadyCount < 1 || sharing.RemoteVisibleCount < 1) &&
                   network.State == NetCafeNetworkSession.Phase.Connected &&
                   Time.realtimeSinceStartup < deadline) yield return null;
            bool received = sharing.RemoteReadyCount >= 1 && sharing.RemoteVisibleCount >= 1;
            if (received) Debug.Log("B2_SYNTHETIC_REMOTE_VISIBLE_PASS");
            else Debug.LogError("B2_SYNTHETIC_FAIL NO_VISIBLE_REMOTE_FRAME subscribed=" + sharing.RemoteVideoCount +
                " ready=" + sharing.RemoteReadyCount + " visible=" + sharing.RemoteVisibleCount);
            Directory.CreateDirectory(Path.GetDirectoryName(resultFile));
            File.WriteAllText(resultFile, received ? "PASS" : "FAIL");
            network.Leave();
            float exit = Time.realtimeSinceStartup + 30f;
            while (network.State != NetCafeNetworkSession.Phase.Disconnected && Time.realtimeSinceStartup < exit) yield return null;
            Application.Quit(received && network.State == NetCafeNetworkSession.Phase.Disconnected ? 0 : 1);
        }
    }

    void StopPublish()
    {
        if (videoLoop != null) { StopCoroutine(videoLoop); videoLoop = null; }
        source?.Stop();
        try { if (track != null && network != null && network.TransportRoom != null)
            network.TransportRoom.LocalParticipant.UnpublishTrack(track,false); }
        catch (Exception) { /* room teardown still revokes all publications */ }
        source?.Dispose(); source = null; track = null;
        if (pattern != null) { Destroy(pattern); pattern = null; }
    }
    void OnDestroy() { StopPublish(); }
}
#endif

#if NETCAFE_B1 && (UNITY_EDITOR || DEVELOPMENT_BUILD)
using System;
using System.Collections;
using System.IO;
using LiveKit;
using LiveKit.Proto;
using UnityEngine;

// Local integration only. Generates frames; never reads or publishes a real desktop.
public sealed class CafeB2StationSmoke : MonoBehaviour
{
    NetCafeNetworkSession network;
    CafeRoomSync sync;
    CafePrototype cafe;
    CafeDesktopCapture capture;
    CafeDesktopSharing sharing;
    Texture2D pattern;
    TextureVideoSource source;
    LocalVideoTrack track;
    Coroutine pump;
    bool host, failed;
    string folder;
    string role => host ? "host" : "guest";
    string peer => host ? "guest" : "host";
    string myId => network.TransportRoom?.LocalParticipant?.Identity ?? "";
    static string Arg(string prefix) => Array.Find(Environment.GetCommandLineArgs(),
        a => a.StartsWith(prefix, StringComparison.Ordinal))?.Substring(prefix.Length) ?? "";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-b2StationSmoke") < 0) return;
        var network = FindFirstObjectByType<NetCafeNetworkSession>();
        if (network == null) { Debug.LogError("STATION_FAIL no network"); Application.Quit(1); return; }
        network.gameObject.AddComponent<CafeB2StationSmoke>();
    }

    IEnumerator Start()
    {
        network = GetComponent<NetCafeNetworkSession>();
        sync = GetComponent<CafeRoomSync>();
        cafe = GetComponent<CafePrototype>();
        capture = GetComponent<CafeDesktopCapture>();
        sharing = GetComponent<CafeDesktopSharing>() ?? gameObject.AddComponent<CafeDesktopSharing>();
        folder = Arg("-stationFixture=");
        string selected = Arg("-stationRole=");
        host = selected == "host";
        if ((!host && selected != "guest") || !Path.IsPathRooted(folder) || !Directory.Exists(folder) ||
            !Uri.TryCreate(network.RoomServiceUrl, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != "http" || !endpoint.IsLoopback || !Safe())
        { Fail("unsafe fixture"); yield break; }

        network.ConfigureForSmoke("Station " + role, host ? "" : Read("invite"));
        if (host) network.CreateRoom(); else network.JoinRoom();
        yield return Wait(() => network.State == NetCafeNetworkSession.Phase.Connected, "connect");
        if (failed) yield break;
        if (host)
        {
            cafe.Sit(0);
            if (cafe.CurrentSeat != 0) { Fail("host did not reserve seat"); yield break; }
            Write("invite", network.InviteCode);
        }
        else
        {
            yield return Wait(() => !string.IsNullOrEmpty(network.HostIdentity) &&
                sync.SeatOwnerForTest(0) == network.HostIdentity, "host seat known");
            if (failed) yield break;
            cafe.Sit(0);
            if (cafe.CurrentSeat >= 0 || cafe.SeatAvailable(0) || !Safe())
            { Fail("occupied seat permitted"); yield break; }
            Debug.Log("STATION_OCCUPIED_REJECTED_PASS");
            cafe.Sit(1);
            if (cafe.CurrentSeat != -1 || !cafe.body.enabled || !Safe())
            { Fail("GUEST_ENTERED_BEFORE_AUTHORITY"); yield break; }
            Debug.Log("STATION_GUEST_WAITED_PASS");
            cafe.OpenMenu();
            if (sync.SeatRequestPendingForTest) { Fail("menu left a pending seat"); yield break; }
            yield return new WaitForSecondsRealtime(.35f);
            if (cafe.CurrentSeat >= 0 || cafe.Mode != CafePrototype.InputMode.Menu || !Safe())
            { Fail("cancelled request entered seat"); yield break; }
            Debug.Log("STATION_CANCELLED_REQUEST_PASS");
            cafe.Continue();
            cafe.Sit(1);
            if (cafe.CurrentSeat >= 0 || !sync.SeatRequestPendingForTest)
            { Fail("new request skipped authority"); yield break; }
            yield return Wait(() => cafe.CurrentSeat == 1 && sync.SeatOwnerForTest(1) == myId, "guest seat confirmed");
            if (failed) yield break;
            Write("guest-confirmed");
        }
        yield return Wait(() => Has("guest-confirmed"), "guest occupied separate seat");
        if (failed) yield break;
        cafe.Stand();
        yield return Wait(() => cafe.CurrentSeat < 0 && sync.SeatOwnerForTest(0) == "" &&
            sync.SeatOwnerForTest(1) == "" && cafe.SeatAvailable(2), "both seats released");
        if (failed) yield break;
        Write(role + "-ready");
        yield return Wait(() => Has(peer + "-ready"), "contenders ready");
        if (failed) yield break;
        cafe.Sit(2);
        if (!host && cafe.CurrentSeat >= 0) { Fail("guest raced into seat"); yield break; }
        yield return Wait(() => !sync.SeatRequestPendingForTest && !string.IsNullOrEmpty(sync.SeatOwnerForTest(2)) &&
            (sync.SeatOwnerForTest(2) == myId) == (cafe.CurrentSeat == 2), "contest resolved");
        if (failed) yield break;
        Write(role + "-race", cafe.CurrentSeat == 2 ? "won" : "lost");
        yield return Wait(() => Has(peer + "-race"), "peer contest result");
        if (failed) yield break;
        bool won = cafe.CurrentSeat == 2;
        if (Read(peer + "-race") != (won ? "lost" : "won"))
        { Fail("contest did not produce exactly one occupant"); yield break; }
        if (!won)
        {
            cafe.Sit(2);
            if (cafe.CurrentSeat >= 0 || cafe.SeatAvailable(2))
            { Fail("second occupant after contest"); yield break; }
        }
        Debug.Log("STATION_ONE_WINNER_PASS won=" + won);
        Write(role + "-checked");
        yield return Wait(() => Has(peer + "-checked"), "peer occupancy checked");
        if (failed) yield break;
        if (won) cafe.Stand();
        yield return Wait(() => cafe.CurrentSeat < 0 && sync.SeatOwnerForTest(2) == "", "contested seat released");
        if (failed) yield break;
        int ownSeat = host ? 0 : 1, otherSeat = host ? 1 : 0;
        cafe.Sit(ownSeat);
        yield return Wait(() => cafe.CurrentSeat == ownSeat && sync.SeatOwnerForTest(ownSeat) == myId, "returned to distinct station");
        if (failed) yield break;
        yield return Wait(() => !string.IsNullOrEmpty(sync.SeatOwnerForTest(otherSeat)), "peer returned to distinct station");
        if (failed) yield break;
        string peerId = sync.SeatOwnerForTest(otherSeat);
        string nextAvatar = CafeAvatarLibrary.SelectedId == "Bat" ? "Duck" : "Bat";
        Write(role + "-avatar", nextAvatar);
        yield return Wait(() => Has(peer + "-avatar"), "peer avatar change ready");
        if (failed) yield break;
        CafeAvatarLibrary.SetForSmoke(nextAvatar);
        string expectedAvatar = Read(peer + "-avatar");
        float avatarDeadline = Time.realtimeSinceStartup + 4;
        while (sync.AvatarForTest(peerId) != expectedAvatar && Time.realtimeSinceStartup < avatarDeadline &&
            sync.SeatOwnerForTest(otherSeat) == peerId) yield return null;
        if (sync.AvatarForTest(peerId) != expectedAvatar || sync.SeatOwnerForTest(otherSeat) != peerId)
        { Fail("avatar switch lost authority seat"); yield break; }
        Debug.Log("STATION_AVATAR_KEPT_SEAT_PASS");

        pattern = new Texture2D(192, 108, TextureFormat.RGBA32, false);
        var pixels = new Color32[192 * 108];
        Color32 a = host ? Color.red : Color.green, b = host ? Color.cyan : Color.magenta;
        for (int i = 0; i < pixels.Length; ++i) pixels[i] = (i % 192 / 24 + i / 192 / 18) % 2 == 0 ? a : b;
        pattern.SetPixels32(pixels); pattern.Apply();
        source = new TextureVideoSource(pattern);
        var room = network.TransportRoom;
        track = LocalVideoTrack.CreateVideoTrack("station-generated-test", source, room);
        var publish = room.LocalParticipant.PublishTrack(track, new TrackPublishOptions {
            Source = TrackSource.SourceScreenshare, VideoCodec = VideoCodec.Vp8, Simulcast = false,
            VideoEncoding = new VideoEncoding { MaxBitrate = 350000, MaxFramerate = 8 }
        });
        yield return publish;
        if (publish.IsError || !Safe()) { Fail("publish"); yield break; }
        source.Start(); pump = StartCoroutine(source.Update());
        Debug.Log("STATION_PUBLISH_PASS seat=" + ownSeat);
        yield return Wait(() => sharing.RemoteReadyCount == 1, "remote first frame");
        if (failed) yield break;
        Debug.Log("STATION_REMOTE_FRAME_PASS");
        yield return Wait(() => sharing.RemoteVisibleCount == 1 &&
            !string.IsNullOrEmpty(sync.SeatOwnerForTest(otherSeat)) && sync.SeatOwnerForTest(otherSeat) != myId,
            "remote seat binding");
        if (failed) yield break;
        bool rendered = false;
        for (int i = 0; i < 12 && !rendered; ++i)
        {
            yield return new WaitForEndOfFrame();
            rendered = RenderStation(otherSeat);
        }
        if (!rendered || !Safe()) { Fail("expected remote pixels absent at seat " + otherSeat); yield break; }
        Debug.Log("STATION_CORRECT_SCREEN_PIXELS_PASS seat=" + otherSeat);
        Write(role + "-media");
        yield return Wait(() => Has(peer + "-media"), "peer media verified");
        if (failed) yield break;
        StopVideo();
        if (host)
        {
            yield return Wait(() => network.RemotePeersForSmoke == 0, "guest left");
            if (failed) yield break;
        }
        network.Leave();
        yield return Wait(() => network.State == NetCafeNetworkSession.Phase.Disconnected, "leave", false);
        if (failed) yield break;
        Debug.Log("STATION_COMPLETE_PASS " + role);
        Application.Quit(0);
    }

    bool RenderStation(int seat)
    {
        var camera = cafe.eyes;
        var screen = cafe.screens[seat].transform;
        var position = camera.transform.position;
        var rotation = camera.transform.rotation;
        float fov = camera.fieldOfView, aspect = camera.aspect;
        var previousTarget = camera.targetTexture;
        var previousActive = RenderTexture.active;
        var target = RenderTexture.GetTemporary(320, 180, 24);
        var pixels = new Texture2D(160, 90, TextureFormat.RGB24, false);
        try
        {
            camera.transform.SetPositionAndRotation(screen.position - screen.forward * .65f, screen.rotation);
            camera.fieldOfView = 55; camera.aspect = 16f / 9f; camera.targetTexture = target;
            camera.Render(); RenderTexture.active = target;
            pixels.ReadPixels(new Rect(80, 45, 160, 90), 0, 0); pixels.Apply();
            int a = 0, b = 0;
            foreach (var p in pixels.GetPixels32())
            {
                if (host ? p.r < 90 && p.g > 155 && p.b < 90 : p.r > 155 && p.g < 90 && p.b < 90) ++a;
                if (host ? p.r > 155 && p.g < 90 && p.b > 155 : p.r < 90 && p.g > 155 && p.b > 155) ++b;
            }
            return a > 200 && b > 200;
        }
        finally
        {
            camera.transform.SetPositionAndRotation(position, rotation);
            camera.fieldOfView = fov; camera.aspect = aspect; camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive; RenderTexture.ReleaseTemporary(target); Destroy(pixels);
        }
    }

    bool Safe() => capture != null && !capture.Active && !GetComponent<CafeDesktopInput>().Controlling;
    string At(string name) => Path.Combine(folder, name + ".txt");
    string Read(string name) => File.ReadAllText(At(name)).Trim();
    bool Has(string name) => File.Exists(At(name));
    void Write(string name, string value = "ok")
    {
        File.WriteAllText(At(name + "-writing"), value);
        File.Move(At(name + "-writing"), At(name));
    }
    IEnumerator Wait(Func<bool> condition, string step, bool requireRoom = true)
    {
        float limit = Time.realtimeSinceStartup + 20;
        while (!condition() && Time.realtimeSinceStartup < limit && Safe() &&
            (!requireRoom || network.State != NetCafeNetworkSession.Phase.Failed))
            yield return null;
        if (!condition() || !Safe()) Fail(step);
    }
    void Fail(string reason)
    {
        failed = true;
        Debug.LogError("STATION_FAIL " + reason + " role=" + role + " state=" + network.State);
        StopVideo(); network.Leave(); Application.Quit(1);
    }
    void StopVideo()
    {
        if (pump != null) { StopCoroutine(pump); pump = null; }
        if (track != null) network.TransportRoom?.LocalParticipant?.UnpublishTrack(track, false);
        track = null;
        source?.Stop(); source?.Dispose(); source = null;
        if (pattern != null) { Destroy(pattern); pattern = null; }
    }
    void OnDestroy() => StopVideo();
}
#endif

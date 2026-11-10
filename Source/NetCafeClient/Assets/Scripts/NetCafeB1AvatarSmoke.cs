#if NETCAFE_B1 && (UNITY_EDITOR || DEVELOPMENT_BUILD)
using System;
using System.Collections;
using System.IO;
using UnityEngine;using TMPro;
using UnityEngine.UI;

// Real two-process, opt-in room-state acceptance. Never records tokens or desktop pixels.
public sealed class NetCafeB1AvatarSmoke : MonoBehaviour
{
    NetCafeNetworkSession network;
    CafePrototype cafe;
    CafeRoomSync sync;
    string role, inviteFile;
    bool failed;
    bool voiceTest;    bool uiFlowTest;
    CafeEntryUi entry;
    CafeSpatialVoice voice;
    CafeSyntheticAudioSource testSource;
    LiveKit.LocalAudioTrack testTrack;
    Coroutine pump;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        string role = Argument("-b1AvatarSmokeRole=");
        if (role != "host" && role != "guest") return;
        var session = FindFirstObjectByType<NetCafeNetworkSession>();
        var cafe = FindFirstObjectByType<CafePrototype>();
        var sync = FindFirstObjectByType<CafeRoomSync>();
        if (session == null || cafe == null || sync == null)
        {
            Debug.LogError("B1_AVATAR_FAIL no session/room/cafe");
            Application.Quit(1); return;
        }
        var test = session.gameObject.AddComponent<NetCafeB1AvatarSmoke>();
        test.network = session; test.cafe = cafe; test.sync = sync;
        test.role = role; test.inviteFile = Argument("-b1AvatarInviteFile=");
        test.voiceTest = Array.IndexOf(Environment.GetCommandLineArgs(), "-b1VoiceSmoke") >= 0;        test.uiFlowTest = Array.IndexOf(Environment.GetCommandLineArgs(), "-b1UiFlowSmoke") >= 0;
        test.entry = session.GetComponent<CafeEntryUi>();
        if (test.voiceTest)
        {
            test.voice = session.GetComponent<CafeSpatialVoice>();
            if (test.voice == null) test.voice = session.gameObject.AddComponent<CafeSpatialVoice>();
        }
    }
    static string Argument(string prefix)
    {
        foreach (string arg in Environment.GetCommandLineArgs())
            if (arg.StartsWith(prefix, StringComparison.Ordinal)) return arg.Substring(prefix.Length);
        return "";
    }
    IEnumerator Wait(Func<bool> condition, float seconds, string label)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < deadline)
        {
            if (failed) yield break;
            if (network.State == NetCafeNetworkSession.Phase.Failed)
            {
                Fail(label + " " + network.Error); yield break;
            }
            yield return null;
        }
        if (!condition()) Fail(label + " timeout (" + network.State + "); self=" +
            (network.TransportRoom == null ? "" : network.TransportRoom.LocalParticipant.Identity) +
            "; position=" + (network.TransportRoom == null ? "" : sync.PositionForTest(
                network.TransportRoom.LocalParticipant.Identity).ToString()) +
            "; peers=" + sync.VisibleRemoteCount);
    }
    IEnumerator Start()
    {
        if (string.IsNullOrWhiteSpace(inviteFile)) { Fail("invite path missing"); yield break; }
        yield return null;
        if (role == "host") yield return Host();
        else yield return Guest();
    }
    IEnumerator Host()
    {
        CafeAvatarLibrary.SetForSmoke("Corgi");
        cafe.body.GetComponent<CafeAvatarVisual>()?.SetAvatar("Corgi");
        network.ConfigureForSmoke("AvatarHost", "");
        if (uiFlowTest)
        {
            yield return Wait(() => entry != null && entry.Ready, 10, "host home UI ready");
            if (failed) yield break;
            ClickEntry("创建房间"); yield return null;
            if (!EntryHasText("进入房间后关闭麦克风（默认，点击可开启）"))
            { Fail("host prejoin microphone selection missing"); yield break; }
            SetEntryField("Input_昵称", "AvatarHost");
            ClickEntry("确认创建");
        }
        else network.CreateRoom();
        yield return Wait(() => network.State == NetCafeNetworkSession.Phase.Connected, 35, "host join");
        if (failed) yield break;
        if (uiFlowTest)
        {
            yield return Wait(() => entry.BlocksRoomMenu && EntryHasText("进入房间"), 10,
                "creator invite code page");
            if (failed) yield break;
            Debug.Log("B1_UI_CREATE_INVITE_PASS");
            if (!EntryHasText("开启麦克风（需要授权）"))
            { Fail("host in-room microphone menu missing"); yield break; }
            ClickEntry("进入房间"); yield return null;
            if (entry.BlocksRoomMenu) { Fail("creator could not enter room"); yield break; }
        }
        if (voiceTest)
        {
            yield return PublishSyntheticVoice();
            if (failed) yield break;
        }
        cafe.Sit(0);
        Directory.CreateDirectory(Path.GetDirectoryName(inviteFile));
        string prepared = inviteFile + ".writing";
        File.WriteAllText(prepared, network.InviteCode);
        if (File.Exists(inviteFile)) File.Delete(inviteFile);
        File.Move(prepared, inviteFile); // Publish only after the bytes are fully written.
        yield return Wait(() => network.RemotePeersForSmoke >= 1, 40, "guest arrival");
        if (failed) yield break;
        string guestId = "";
        foreach (var pair in network.TransportRoom.RemoteParticipants)
            guestId = pair.Key;
        yield return Wait(() => sync.VisibleRemoteCount == 1 &&
               sync.AvatarForTest(guestId) == "Duck" &&
               sync.PositionForTest(guestId).z > -3.7f &&
               Mathf.Abs(Mathf.DeltaAngle(sync.YawForTest(guestId), 75)) < 6f,
               30, "host receives guest position and yaw");
        if (failed) yield break;
        Debug.Log("B1_AVATAR_POSITION_YAW_PASS");
        yield return Wait(() => sync.VisibleRemoteCount == 1 &&
               sync.AvatarForTest(guestId) == "Duck" &&
               sync.SeatOwnerForTest(1) == guestId, 35, "host sees Duck in seat 2");
        if (failed) yield break;
        Debug.Log("B1_AVATAR_HOST_SEES_DUCK_SEAT_2");
        if (voiceTest)
        {
            yield return Wait(() => voice.RemoteAudioCount >= 1, 25, "host receives guest synthetic microphone");
            if (failed) yield break;
            Debug.Log("B1_VOICE_HOST_SUBSCRIBED_GUEST_PASS");
        }
        yield return Wait(() => network.RemotePeersForSmoke == 0 && sync.VisibleRemoteCount == 0,
            35, "guest removal");
        if (failed) yield break;
        StopSyntheticVoice();
        if (uiFlowTest)
        {
            cafe.OpenMenu(); entry.AskLeave(); yield return null;
            ClickEntry("确认关闭房间");
        }
        else network.Leave();
        yield return Wait(() => network.State == NetCafeNetworkSession.Phase.Disconnected,
            15, "host exit");
        if (uiFlowTest)
        {
            yield return Wait(() => entry.BlocksRoomMenu && EntryHasText("创建房间"), 8, "host returned home");
            if (!failed) Debug.Log("B1_UI_HOST_CLOSE_HOME_PASS");
        }
        if (failed) yield break;
        Debug.Log("B1_AVATAR_HOST_PASS");
        Application.Quit(0);
    }
    IEnumerator Guest()
    {
        CafeAvatarLibrary.SetForSmoke("Duck");
        cafe.body.GetComponent<CafeAvatarVisual>()?.SetAvatar("Duck");
        yield return Wait(() => File.Exists(inviteFile), 40, "invite");
        if (failed) yield break;
        string code = File.ReadAllText(inviteFile).Trim();
        network.ConfigureForSmoke("AvatarGuest", code);
        if (uiFlowTest)
        {
            yield return Wait(() => entry != null && entry.Ready, 10, "guest home UI ready");
            if (failed) yield break;
            ClickEntry("加入房间"); yield return null;
            if (!EntryHasText("进入房间后关闭麦克风（默认，点击可开启）"))
            { Fail("guest prejoin microphone selection missing"); yield break; }
            SetEntryField("Input_昵称", "AvatarGuest");
            SetEntryField("Input_邀请码", code);
            ClickEntry("确认加入");
        }
        else network.JoinRoom();
        yield return Wait(() => network.State == NetCafeNetworkSession.Phase.Connected,
            35, "guest join");
        if (failed) yield break;
        if (uiFlowTest)
        {
            yield return Wait(() => !entry.BlocksRoomMenu && cafe.Mode != CafePrototype.InputMode.Menu,
                10, "guest entered room from UI");
            if (failed) yield break;
            Debug.Log("B1_UI_JOIN_ROOM_PASS");
            entry.OpenMembers(); yield return null;
            if (!EntryHasText("开启麦克风（需要授权）"))
            { Fail("guest in-room microphone menu missing"); yield break; }
            Debug.Log("B1_UI_GUEST_MIC_MENU_PASS");
            ClickEntry("进入房间"); yield return null;
        }
        if (voiceTest)
        {
            if (voice == null || voice.MicrophoneActive || voice.MicrophoneBusy)
                Fail("guest microphone was not off by default");
            if (failed) yield break;
            yield return Wait(() => voice.RemoteAudioCount == 1, 25, "guest subscribed synthetic voice");
            if (failed) yield break;
            Debug.Log("B1_VOICE_SUBSCRIBED_PASS: remote microphone track received, default local mic off");
            // Prove that a *guest* has the same LiveKit audio publish grant as
            // the host without requesting or recording the actual mic device.
            yield return PublishSyntheticVoice();
            if (failed) yield break;
            Debug.Log("B1_VOICE_GUEST_PUBLISH_PASS");
        }
        yield return Wait(() => !string.IsNullOrEmpty(network.HostIdentity), 20, "host identity");
        if (failed) yield break;
        yield return Wait(() => sync.VisibleRemoteCount == 1 &&
               sync.AvatarForTest(network.HostIdentity) == "Corgi" &&
               sync.SeatOwnerForTest(0) == network.HostIdentity, 30, "guest sees Corgi in seat 1");
        if (failed) yield break;
        Debug.Log("B1_AVATAR_GUEST_SEES_CORGI_SEAT_1");
        cafe.body.enabled = false;
        cafe.body.transform.position += Vector3.forward * 1.0f;
        cafe.body.enabled = true;
        cafe.eyes.transform.rotation = Quaternion.Euler(0, 75f, 0);
        string myId = network.TransportRoom.LocalParticipant.Identity;
        yield return Wait(() => sync.PositionForTest(myId).z > -3.7f &&
            Mathf.Abs(Mathf.DeltaAngle(sync.YawForTest(myId), 75)) < 6f,
            25, "guest pose acknowledged by host");
        if (failed) yield break;
        Debug.Log("B1_AVATAR_GUEST_POSE_CONFIRMED");
        cafe.Sit(0);
        yield return Wait(() => cafe.CurrentSeat == -1, 25, "host denies duplicate seat 1");
        if (failed) yield break;
        Debug.Log("B1_AVATAR_SEAT_CONFLICT_REJECTED");
        cafe.Sit(1);
        yield return Wait(() => sync.SeatOwnerForTest(1) ==
                   network.TransportRoom.LocalParticipant.Identity, 30, "guest seat acknowledged");
        if (failed) yield break;
        Debug.Log("B1_AVATAR_GUEST_OWNS_SEAT_2");
        yield return new WaitForSecondsRealtime(4);
        StopSyntheticVoice();
        if (uiFlowTest)
        {
            cafe.OpenMenu(); entry.AskLeave(); yield return null;
            ClickEntry("确认离开房间");
        }
        else network.Leave();
        yield return Wait(() => network.State == NetCafeNetworkSession.Phase.Disconnected,
            15, "guest exit");
        if (uiFlowTest)
        {
            yield return Wait(() => entry.BlocksRoomMenu && EntryHasText("创建房间"), 8, "guest returned home");
            if (!failed) Debug.Log("B1_UI_GUEST_LEAVE_HOME_PASS");
        }
        if (failed) yield break;
        Debug.Log("B1_AVATAR_GUEST_PASS");
        Application.Quit(0);
    }
    bool EntryHasText(string text)
    {
        if (entry == null) return false;
        foreach (var btn in entry.GetComponentsInChildren<Button>(true))
        {
            if (!btn.gameObject.activeInHierarchy) continue;
            var label = btn.GetComponentInChildren<TMP_Text>(true);
            if (label != null && label.text == text) return true;
        }
        return false;
    }
    void ClickEntry(string text)
    {
        foreach (var btn in entry.GetComponentsInChildren<Button>(true))
        {
            if (!btn.gameObject.activeInHierarchy || !btn.interactable) continue;
            var label = btn.GetComponentInChildren<TMP_Text>(true);
            if (label != null && label.text == text) { btn.onClick.Invoke(); return; }
        }
        Fail("missing UI button " + text);
    }
    void SetEntryField(string name, string value)
    {
        foreach (var input in entry.GetComponentsInChildren<TMP_InputField>(true))
        {
            if (input.gameObject.activeInHierarchy && input.gameObject.name == name)
            { input.text = value; return; }
        }
        Fail("missing input field " + name);
    }

    IEnumerator PublishSyntheticVoice()
    {
        var room = network.TransportRoom;
        if (room == null) { Fail("voice room not connected"); yield break; }
        testSource = new CafeSyntheticAudioSource();
        testTrack = LiveKit.LocalAudioTrack.CreateAudioTrack("synthetic-test-tone", testSource, room);
        var published = room.LocalParticipant.PublishTrack(testTrack,
            new LiveKit.Proto.TrackPublishOptions {
                Source = LiveKit.Proto.TrackSource.SourceMicrophone,
                AudioEncoding = new LiveKit.Proto.AudioEncoding { MaxBitrate = 32000 }
            });
        yield return published;
        if (published.IsError) { Fail("could not publish synthetic voice track"); yield break; }
        testSource.Start();
        pump = StartCoroutine(PumpSyntheticVoice());
        Debug.Log("B1_VOICE_SYNTHETIC_PUBLISHED_PASS: local device untouched");
    }

    IEnumerator PumpSyntheticVoice()
    {
        while (network != null && network.State == NetCafeNetworkSession.Phase.Connected &&
               testSource != null)
        {
            testSource.Pump();
            yield return new WaitForSecondsRealtime(.01f);
        }
    }

    void StopSyntheticVoice()
    {
        if (pump != null) { StopCoroutine(pump); pump = null; }
        testSource?.Stop();
        if (testTrack != null && network != null && network.TransportRoom != null)
            network.TransportRoom.LocalParticipant.UnpublishTrack(testTrack, false);
        testTrack = null;
        testSource?.Dispose();
        testSource = null;
    }

    void OnDestroy() { StopSyntheticVoice(); }

    void Fail(string text)
    {
        if (failed) return;
        failed = true;
        Debug.LogError("B1_AVATAR_FAIL " + text);
        Application.Quit(1);
    }
}
#endif

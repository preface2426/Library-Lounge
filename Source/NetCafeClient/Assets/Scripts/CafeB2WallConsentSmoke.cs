#if NETCAFE_B1 && (UNITY_EDITOR || DEVELOPMENT_BUILD)
using System;
using System.Collections;
using System.IO;
using System.Linq;
using LiveKit;
using LiveKit.Proto;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Loopback-only; publish generated colors and exercise the real room dialog.
public sealed class CafeB2WallConsentSmoke : MonoBehaviour
{
    NetCafeNetworkSession network;
    CafeRoomSync sync;
    CafeDesktopSharing sharing;
    CafeDesktopCapture capture;
    CafePrototype cafe;
    CafeEntryUi ui;
    string directory;
    Texture2D checkerboard;
    TextureVideoSource source;
    LocalVideoTrack video;
    Coroutine sourceRunner;
    bool failed;
    bool Media => Flag("-b2DesktopMedia");
    bool DefaultWall => Flag("-b2WallDefault");
    bool HighResolution => Flag("-b2WallClarity");
    bool Recovery => Flag("-b2PlayerRecovery");
    Vector3 initialPosition;

    static bool Flag(string arg) => Array.IndexOf(Environment.GetCommandLineArgs(), arg) >= 0;
    string FileAt(string name) => Path.Combine(directory, name + ".txt");
    void Signal(string name) => File.WriteAllText(FileAt(name), "ok");
    bool Has(string name) => File.Exists(FileAt(name));
    void Fail(string reason)
    {
        if (failed) return;
        failed = true;
        Debug.LogError("B2_WALL_FAIL " + reason);
        if (sharing != null && (sharing.Sharing || sharing.Busy)) sharing.StopSharing();
        StopVideo();
        network?.Leave();
        Application.Quit(1);
    }
    void Start()
    {
        network = GetComponent<NetCafeNetworkSession>();
        sync = GetComponent<CafeRoomSync>();
        sharing = GetComponent<CafeDesktopSharing>();
        capture = GetComponent<CafeDesktopCapture>();
        cafe = GetComponent<CafePrototype>();
        ui = GetComponent<CafeEntryUi>();
        foreach (string arg in Environment.GetCommandLineArgs())
            if (arg.StartsWith("-b2WallDir=", StringComparison.Ordinal)) directory = arg.Substring(11);
        if (string.IsNullOrEmpty(directory) || !Path.IsPathRooted(directory) || !Directory.Exists(directory) ||
            !network || !sync || !sharing || !cafe || !ui || !capture || capture.Active ||
            !Uri.TryCreate(network.RoomServiceUrl, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttp || !endpoint.IsLoopback)
        { Fail("FIXTURE_MISSING_OR_NOT_LOOPBACK"); return; }
        StartCoroutine(Run(Flag("-b2WallHost")));
    }

    IEnumerator Wait(Func<bool> ready, string stage, float seconds = 24)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (!ready())
        {
            if (failed) yield break;
            if (Time.realtimeSinceStartup >= end) { Fail(stage); yield break; }
            yield return null;
        }
    }
    Button FindButton(string label)
    {
        foreach (var button in ui.ScreenshotOverlayRoot.GetComponentsInChildren<Button>())
        {
            var text = button.GetComponentInChildren<TMP_Text>();
            if (button.IsInteractable() && text != null && text.text == label) return button;
        }
        return null;
    }
    bool HasWallLabel(string label) => ui.ScreenshotOverlayRoot.GetComponentsInChildren<TMP_Text>()
        .Any(text => text.text == label);
    IEnumerator OpenSharePage()
    {
        cafe.OpenMenu();
        var lounge = GetComponent<CafeLoungeHud>();
        yield return Wait(() => lounge.LightRoot != null && lounge.LightRoot.gameObject.activeInHierarchy,
            "SHARE_MENU_NOT_OPEN");
        if (failed) yield break;
        var button = lounge.LightRoot.Find("HudActions/HudShareScreenButton")?.GetComponent<Button>();
        yield return Wait(() => button != null && button.isActiveAndEnabled && button.IsInteractable(),
            "SHARE_ENTRY_NOT_VISIBLE", 4);
        if (failed) yield break;
        button.onClick.Invoke();
        yield return Wait(() => FindButton("返回菜单") != null, "SHARE_PAGE_NOT_OPEN");
    }

    IEnumerator ClickShare(string label, Action legacy)
    {
        if (!Flag("-b2ShareUi")) { legacy(); yield break; }
        yield return OpenSharePage();
        if (failed) yield break;
        bool stopping = label == "停止屏幕共享";
        if (stopping)
        {
            float until = Time.realtimeSinceStartup + 5f;
            while (!sharing.BeginReadbackForTest())
            {
                if (Time.realtimeSinceStartup >= until) { Fail("SHARE_STOP_READBACK_NOT_PENDING"); yield break; }
                yield return null;
            }
        }
        var button = FindButton(label);
        if (button == null) { Fail("SHARE_CONTROL_MISSING " + label); yield break; }
        button.onClick.Invoke();
        if (stopping && !sharing.Sharing) Debug.Log("B2_WALL_SHARE_STOP_READBACK_PASS");
        CloseSharePage();
    }

    void CloseSharePage()
    {
        FindButton("返回菜单").onClick.Invoke();
        cafe.Continue();
    }

    IEnumerator StopPublisher()
    {
        if (sharing.Sharing)
        {
            yield return ClickShare("停止屏幕共享", () => sharing.SetSyntheticSharingForTest(false));
            if (failed) yield break;
            if (sharing.Sharing || sharing.AllowPublicDisplay) { Fail("SHARE_STOP"); yield break; }
            if (Flag("-b2ShareUi")) Debug.Log("B2_WALL_SHARE_BUTTON_STOP_PASS");
        }
        StopVideo();
    }
    string PeerIdentity()
    {
        foreach (var peer in network.TransportRoom.RemoteParticipants.Values) return peer.Identity;
        return "";
    }
    CafeInteractable AimAtWall()
    {
        var wall = cafe.publicScreen.transform;
        foreach (float distance in new[] {2.5f, 2f, 1.5f})
        foreach (float sideways in new[] {0f, -1f, 1f, -1.5f, 1.5f})
        {
            Vector3 eye = wall.position - wall.forward * distance + wall.right * sideways;
            eye.y = Recovery ? .06f + cafe.WalkingEyeHeight : 1.65f;
            Vector3 look = (wall.position - eye).normalized;
            if (!Physics.Raycast(eye, look, out var hit, 3f, Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Collide)) continue;
            var item = hit.collider.GetComponentInParent<CafeInteractable>();
            if (item == null || item.action != CafeInteractable.Action.Display) continue;
            cafe.body.enabled = false;
            cafe.body.transform.position = eye - Vector3.up * (Recovery ? cafe.WalkingEyeHeight : 1.62f);
            cafe.body.enabled = true;
            cafe.eyes.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look));
            return item;
        }
        return null;
    }
    bool Rendered(bool hostColors, bool station = false)
    {
        var camera = cafe.eyes;
        var screen = station ? cafe.screens[hostColors ? 0 : 1].transform : cafe.publicScreen.transform;
        var position = camera.transform.position; var rotation = camera.transform.rotation;
        float fov = camera.fieldOfView, aspect = camera.aspect;
        var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
        var target = RenderTexture.GetTemporary(640,360,24);
        var pixels = new Texture2D(640,360,TextureFormat.RGB24,false);
        try
        {
            camera.transform.SetPositionAndRotation(screen.position - screen.forward * (station ? .65f : 5f),
                station ? screen.rotation : Quaternion.LookRotation(screen.forward));
            camera.fieldOfView=station ? 55 : 70; camera.aspect=16f/9; camera.targetTexture=target;
            camera.Render(); RenderTexture.active=target;
            pixels.ReadPixels(new Rect(0,0,640,360),0,0); pixels.Apply();
            int first=0, second=0;
            int firstY = 0, secondY = 0;
            var colors = pixels.GetPixels32();
            for (int i = 0; i < colors.Length; i++)
            {
                var c = colors[i];
                if (c.r>160 && c.g<100 && (hostColors ? c.b<100 : c.b>160)) { first++; firstY += i / 640; }
                if (c.g>160 && c.r<100 && (hostColors ? c.b>160 : c.b<100)) { second++; secondY += i / 640; }
            }
            if (Media && (first < 500 || second < 500 || firstY / first <= secondY / second + 10))
            {
                if (Time.frameCount % 60 == 0)
                    Debug.Log("B2_WALL_MEDIA_PIXEL_DIAGNOSTIC station=" + station + " count=" + first + "," + second +
                        " y=" + firstY / Mathf.Max(1, first) + "," + secondY / Mathf.Max(1, second));
                return false;
            }
            if (first<500 || second<500) return false;
            Debug.Log("B2_WALL_GPU_PIXELS first="+first+" second="+second);
            return true;
        }
        finally
        {
            camera.transform.SetPositionAndRotation(position,rotation);
            camera.fieldOfView=fov; camera.aspect=aspect; camera.targetTexture=oldTarget;
            RenderTexture.active=oldActive; RenderTexture.ReleaseTemporary(target); Destroy(pixels);
        }
    }
    IEnumerator ExpectRendered(bool hostColors, string stage)
    {
        yield return Wait(() => sharing.PublicWallVisibleForTest, stage + "_NO_TEXTURE");
        if (failed) yield break;
        float deadline = Time.realtimeSinceStartup + 8;
        do
        {
            yield return new WaitForEndOfFrame();
            if (Rendered(hostColors)) { Debug.Log("B2_WALL_RENDERED_PASS " + stage); yield break; }
        } while (Time.realtimeSinceStartup < deadline);
        Fail(stage + "_NO_PIXELS");
    }
    IEnumerator Publish(int seat, bool hostColors)
    {
        if (Recovery) initialPosition = new Vector3(0, .06f, -5.5f); // Authored room spawn.
        cafe.Sit(seat);
        yield return Wait(() => cafe.CurrentSeat == seat, "PUBLISHER_SEAT");
        if (failed) yield break;
        if (Recovery) Debug.Log("B2_WALL_PLAYER_PUBLISH_SEATED " + seat);
        if (capture.Active || GetComponent<CafeDesktopInput>().Controlling)
        { Fail("NATIVE_CAPTURE_OR_INPUT"); yield break; }
        int width = HighResolution ? 2560 : Media ? 1920 : 192;
        int height = HighResolution ? 1440 : Media ? 1080 : 108;
        checkerboard = new Texture2D(width,height,TextureFormat.RGBA32,false);
        Color first = hostColors ? Color.red : Color.magenta, second = hostColors ? Color.cyan : Color.green;
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            pixels[y * width + x] = Media ? (y < height / 2 ? first : second) :
                ((x/16+y/16)%2==0 ? first : second);
        checkerboard.SetPixels32(pixels);
        checkerboard.Apply(false,false);
        if (Flag("-b2ShareUi"))
        {
            yield return OpenSharePage();
            if (failed) yield break;
            if (Recovery) Debug.Log("B2_WALL_PLAYER_SHARE_PAGE_OPEN " + seat);
            if (FindButton("开始共享整个屏幕") != null ||
                EventSystem.current.currentSelectedGameObject != FindButton("返回菜单").gameObject)
            { Fail("SHARE_INITIAL_FOCUS"); yield break; }
            if (DefaultWall)
            {
                var allow = FindButton("大屏投放权限：默认允许");
                if (allow == null || !sharing.AllowPublicDisplayOnStart || sharing.AllowPublicDisplay)
                { Fail("WALL_DEFAULT_NOT_DISCLOSED"); yield break; }
                allow.onClick.Invoke();
                var disabled = FindButton("大屏投放权限：默认关闭");
                if (disabled == null || sharing.AllowPublicDisplayOnStart || sharing.Sharing || sharing.AllowPublicDisplay)
                { Fail("WALL_DEFAULT_OPT_OUT"); yield break; }
            }
            else FindButton("大屏投放权限：默认允许")?.onClick.Invoke();
            sharing.SetTestCapture(checkerboard, Media);
            yield return Wait(() => FindButton("开始共享整个屏幕") != null, "SHARE_CAPTURE_READY", Recovery ? 8 : 24);
            if (failed) yield break;
            if (EventSystem.current.currentSelectedGameObject != FindButton("返回菜单").gameObject)
            { Fail("SHARE_FOCUS_CHANGED_TO_PUBLISH"); yield break; }
            Debug.Log("B2_WALL_SHARE_FOCUS_PASS");
            var start = FindButton("开始共享整个屏幕");
            if (start == null || sharing.Sharing || sharing.Busy || sharing.AllowPublicDisplay)
            { Fail("SHARE_START_STATE"); yield break; }
            start.onClick.Invoke();
            var cancel = FindButton("取消启动共享");
            if (cancel == null) { Fail("SHARE_CANCEL_MISSING"); yield break; }
            int late = sharing.LateUnpublishesForTest;
            cancel.onClick.Invoke();
            if (sharing.Sharing || sharing.Busy || sharing.AllowPublicDisplay)
            { Fail("SHARE_CANCEL_STATE"); yield break; }
            yield return Wait(() => sharing.PendingPublishesForTest == 0 &&
                sharing.LateUnpublishesForTest == late + 1 && network.TransportRoom.LocalParticipant.Tracks.Values
                .All(p => p.Source != TrackSource.SourceScreenshare), "CANCELLED_TRACK_NOT_REMOVED");
            if (failed) yield break;
            Debug.Log("B2_WALL_SHARE_BUTTON_CANCEL_PASS");
            FindButton("开始共享整个屏幕").onClick.Invoke();
            sharing.SetTestCapture(null);
            yield return Wait(() => !sharing.Busy && !sharing.Sharing, "CHANGED_CAPTURE_STILL_SHARING");
            if (failed) yield break;
            yield return Wait(() => sharing.PendingPublishesForTest == 0 &&
                network.TransportRoom.LocalParticipant.Tracks.Values.All(p => p.Source != TrackSource.SourceScreenshare),
                "CHANGED_CAPTURE_STILL_PUBLISHED");
            if (failed) yield break;
            Debug.Log("B2_WALL_SHARE_CAPTURE_CHANGE_PASS");
            sharing.SetTestCapture(checkerboard, Media);
            yield return Wait(() => FindButton("开始共享整个屏幕") != null, "SHARE_CAPTURE_RECOVERED");
            if (failed) yield break;
            if (DefaultWall) FindButton("大屏投放权限：默认关闭").onClick.Invoke();
            FindButton("开始共享整个屏幕").onClick.Invoke();
            yield return Wait(() => sharing.Sharing && FindButton("停止屏幕共享") != null, "SHARE_PUBLISH_BUTTON");
            if (failed) yield break;
            if (sharing.AllowPublicDisplay != DefaultWall || sharing.SoundSharing || capture.Active)
            { Fail("SHARE_DEFAULT_PERMISSION"); yield break; }
            CloseSharePage();
            Debug.Log("B2_WALL_SHARE_BUTTON_PUBLISH_PASS");
            Debug.Log("B2_WALL_SYNTHETIC_PUBLISHED_PASS");
            yield break;
        }
        source = new TextureVideoSource(checkerboard);
        var room = network.TransportRoom;
        video = LocalVideoTrack.CreateVideoTrack("b2-wall-generated",source,room);
        var publication = room.LocalParticipant.PublishTrack(video,new TrackPublishOptions {
            VideoCodec=VideoCodec.Vp8, Simulcast=false, Source=TrackSource.SourceScreenshare,
            VideoEncoding=new VideoEncoding {MaxBitrate=250000, MaxFramerate=6}
        });
        yield return publication;
        if (publication.IsError) { Fail("SYNTHETIC_PUBLISH"); yield break; }
        source.Start(); sourceRunner=StartCoroutine(source.Update());
        sharing.SetSyntheticWallVideoForTest(video, checkerboard);
        Debug.Log("B2_WALL_SYNTHETIC_PUBLISHED_PASS");
    }
    IEnumerator Project(bool publisher, bool hostColors)
    {
        string key = hostColors ? "host" : "guest";
        string label = hostColors ? "B2 Wall QA Host" : "B2 Wall QA Guest";
        string choice = "投放 · " + label + " · 电脑 " + (hostColors ? 1 : 2);
        string currentChoice = "当前 · " + label + " · 电脑 " + (hostColors ? 1 : 2);
        if (publisher)
        {
            yield return Publish(hostColors ? 0 : 1, hostColors);
            if (failed) yield break;
            Signal(key + "-published");
            if (DefaultWall)
            {
                yield return Wait(() => Has(key + "-default-listed"), "DEFAULT_NOT_VISIBLE_TO_PEER");
                if (failed) yield break;
                yield return ClickShare("撤销公共大屏投放授权", sharing.TogglePublicDisplayConsent);
                if (failed) yield break;
                if (!sharing.Sharing || sharing.AllowPublicDisplay)
                { Fail("DEFAULT_REVOKE_CHANGED_SHARE"); yield break; }
                Signal(key + "-default-revoked");
            }
            yield return Wait(() => Has(key + "-private"), "PRIVATE_CHECK");
            if (failed) yield break;
            if (Media)
            {
                if (sharing.SoundSharing || sharing.SoundBusy ||
                    network.TransportRoom.LocalParticipant.Tracks.Values.Any(t => t.Source == TrackSource.SourceScreenshareAudio))
                { Fail("DESKTOP_SOUND_ON_WITHOUT_CONSENT"); yield break; }
                yield return ClickShare("共享电脑声音", sharing.ToggleDesktopSound);
                if (failed) yield break;
                yield return Wait(() => sharing.SoundSharing && !sharing.SoundBusy, "DESKTOP_SOUND_PUBLISH");
                if (failed) yield break;
                yield return Wait(() => Has(key + "-heard"), "STATION_SOUND_NOT_HEARD");
                if (failed) yield break;
            }
            yield return ClickShare("允许投到公共大屏", sharing.TogglePublicDisplayConsent);
            if (failed) yield break;
            if (!sharing.AllowPublicDisplay) { Fail("GRANT"); yield break; }
            Signal(key + "-granted");
            yield return Wait(() => Has(key + "-selected"), "SELECTOR_DID_NOT_CHOOSE");
            if (failed) yield break;
            yield return Wait(() => sync.PublicScreenIdentity == network.TransportRoom.LocalParticipant.Identity,
                "PUBLISHER_DID_NOT_SEE_SELECTION");
            if (failed) yield break;
            yield return ExpectRendered(hostColors, key + "-publisher");
            if (failed) yield break;
            if (Flag("-b2KeepSharing"))
            {
                string selected = sync.PublicScreenTrack;
                string roomId = network.RoomId;
                if (Recovery) cafe.ReturnToRoomSpawn(); else cafe.Stand();
                if (Recovery)
                {
                    if (network.State != NetCafeNetworkSession.Phase.Connected || network.RoomId != roomId ||
                        (cafe.body.transform.position - initialPosition).sqrMagnitude > .0025f)
                    { Fail("PLAYER_RETURN_CHANGED_ROOM_OR_MISSED_SPAWN"); yield break; }
                    Debug.Log("B2_WALL_PLAYER_RETURN_SAME_ROOM_PASS");
                }
                if (cafe.CurrentSeat >= 0 || !sharing.Sharing || !sharing.AllowPublicDisplay)
                { Fail("STAND_STOPPED_SHARING"); yield break; }
                if (Media && !sharing.SoundSharing) { Fail("STAND_STOPPED_SOUND"); yield break; }
                Signal(key + "-stood");
                yield return Wait(() => Has(key + "-stand-verified"), "PEER_STAND_VERIFICATION");
                if (failed) yield break;
                if (sync.PublicScreenTrack != selected || !sharing.PublicWallHasFrame)
                { Fail("STAND_CHANGED_WALL"); yield break; }
                Debug.Log("B2_WALL_SHARE_SURVIVES_STAND_PASS publisher");
            }
            if (hostColors)
            {
                yield return ClickShare("撤销公共大屏投放授权", sharing.TogglePublicDisplayConsent);
                if (failed) yield break;
                yield return Wait(() => sharing.PublicWallClearedForTest && sync.PublicScreenIdentity == "", "REVOKE_LOCAL");
            }
            else
            {
                yield return Wait(() => Has(key + "-cleared") && sharing.PublicWallClearedForTest &&
                    sync.PublicScreenIdentity == "", "CLOSE_SYNC");
            }
            if (failed) yield break;
            if (Media && !hostColors)
            {
                yield return Wait(() => Has(key + "-sound-at-seat"), "SOUND_DID_NOT_RETURN");
                if (failed) yield break;
                yield return ClickShare("停止共享电脑声音", sharing.ToggleDesktopSound);
                if (failed) yield break;
                if (sharing.SoundSharing || !sharing.Sharing) { Fail("SOUND_STOP_CHANGED_VIDEO"); yield break; }
                Signal(key + "-sound-stopped");
            }
            Signal(key + "-publisher-clear");
            if (hostColors)
            {
                yield return Wait(() => Has(key + "-revoked-seen"), "SELECTOR_REVOKE_CHECK");
                if (failed) yield break;
                yield return ClickShare("允许投到公共大屏", sharing.TogglePublicDisplayConsent);
                if (failed) yield break;
                Signal(key + "-regranted");
                yield return Wait(() => Has(key + "-reselected") &&
                    sync.PublicScreenIdentity == network.TransportRoom.LocalParticipant.Identity, "RESELECT");
                if (failed) yield break;
                yield return StopPublisher();
                if (failed) yield break;
                Signal(key + "-stopped");
            }
            yield return Wait(() => Has(key + "-checked"), "SELECTOR_CLEAR_CHECK");
            if (failed) yield break;
            yield return StopPublisher();
            if (failed) yield break;
            cafe.Stand();
            if (cafe.CurrentSeat >= 0) { Fail("PUBLISHER_STAND"); yield break; }
            Signal(key + "-done");
        }
        else
        {
            AudioSource speaker = null;
            var seatScreen = cafe.screens[hostColors ? 0 : 1].transform;
            yield return Wait(() => Has(key + "-published") && sharing.RemoteReadyCount >= 1 &&
                sharing.RemoteVisibleCount >= 1, "REMOTE_STATION_FIRST_FRAME", Recovery ? 42 : 24);
            if (failed) yield break;
            if (DefaultWall)
            {
                var defaultHotspot = AimAtWall();
                if (defaultHotspot == null) { Fail("DEFAULT_WALL_HOTSPOT"); yield break; }
                cafe.Activate(defaultHotspot);
                yield return Wait(() => FindButton(choice) != null, "DEFAULT_SOURCE_NOT_LISTED");
                if (failed) yield break;
                if (sync.PublicScreenIdentity != "" || !sharing.PublicWallClearedForTest)
                { Fail("DEFAULT_STARTED_PROJECTION"); yield break; }
                Signal(key + "-default-listed");
                yield return Wait(() => Has(key + "-default-revoked") && FindButton(choice) == null &&
                    sharing.RemoteVisibleCount == 1, "DEFAULT_NOT_REVOKED");
                if (failed) yield break;
                FindButton("返回房间").onClick.Invoke();
                yield return Wait(() => !ui.BlocksRoomMenu && cafe.Mode == CafePrototype.InputMode.Walking, "DEFAULT_WALL_RETURN");
                if (failed) yield break;
                Debug.Log("B2_WALL_DEFAULT_LISTED_NO_AUTOPLAY_REVOKE_PASS");
            }
            if (Media)
            {
                var size = sharing.RemoteSizeForTest(PeerIdentity());
                var expected = HighResolution ? new Vector2Int(2560, 1440) : new Vector2Int(1920, 1080);
                if (size != expected) { Fail("DESKTOP_RESOLUTION " + size); yield break; }
                yield return Wait(() => Rendered(hostColors, true), "STATION_UPSIDE_DOWN_OR_MISSING", 8);
                if (failed) yield break;
                if (sharing.RemoteSoundCountForTest != 0) { Fail("REMOTE_SOUND_WITHOUT_CONSENT"); yield break; }
                Debug.Log($"B2_WALL_DESKTOP_STATION_{expected.y}_UPRIGHT_PASS");
                Signal(key + "-private");
                yield return Wait(() => (speaker = sharing.RemoteSpeakerForTest(PeerIdentity())) != null &&
                    !speaker.mute && At(speaker, seatScreen), "REMOTE_STATION_SOUND_ROUTE");
                if (failed) yield break;
                yield return Wait(() => HasPcm(speaker), "REMOTE_STATION_SOUND_PCM");
                if (failed) yield break;
                Debug.Log("B2_WALL_DESKTOP_STATION_PCM_PASS");
                Signal(key + "-heard");
            }
            var hotspot = AimAtWall();
            if (hotspot == null) { Fail("DISPLAY_HOTSPOT_UNREACHABLE"); yield break; }
            cafe.Activate(hotspot);
            yield return null;
            if (!ui.BlocksRoomMenu || FindButton("返回房间") == null)
            { Fail("WALL_PICKER_NOT_OPEN " + (hostColors ? "guest" : "host")); yield break; }
            if (FindButton(choice) != null || !sharing.PublicWallClearedForTest || sync.PublicScreenIdentity != "")
            { Fail("UNAUTHORIZED_SOURCE"); yield break; }
            Debug.Log("B2_WALL_UNAUTHORIZED_HIDDEN_PASS");
            Signal(key + "-private");
            yield return Wait(() => Has(key + "-granted") && FindButton(choice) != null, "APPROVED_PLAYER_NOT_LISTED");
            if (failed) yield break;
            if (sync.PublicScreenIdentity != "" || !sharing.PublicWallClearedForTest)
            { Fail("AUTO_PROJECTION"); yield break; }
            FindButton(choice).onClick.Invoke();
            yield return Wait(() => sync.PublicScreenIdentity == PeerIdentity() && PeerIdentity() != "",
                "PLAYER_SELECTION_NOT_SYNCED");
            if (failed) yield break;
            Debug.Log("B2_WALL_MEMBER_SELECTED_PASS " + (hostColors ? "guest" : "host"));
            yield return ExpectRendered(hostColors, key + "-viewer");
            if (failed) yield break;
            if (Media)
            {
                yield return Wait(() => speaker == sharing.RemoteSpeakerForTest(PeerIdentity()) &&
                    sharing.RemoteSoundCountForTest == 1 && !speaker.mute &&
                    At(speaker, cafe.publicScreen.transform) && HasPcm(speaker), "WALL_SOUND_PCM_OR_DUPLICATE");
                if (failed) yield break;
                Debug.Log("B2_WALL_DESKTOP_WALL_PCM_PASS");
            }
            Signal(key + "-selected");
            if (Flag("-b2KeepSharing"))
            {
                string owner = PeerIdentity();
                string selected = sync.PublicScreenTrack;
                yield return Wait(() => Has(key + "-stood") && sync.RemoteSeatFor(owner) == -1 &&
                    HasWallLabel(currentChoice),
                    "PUBLISHER_STAND_NOT_SYNCED");
                if (failed) yield break;
                if (selected == "" || sync.PublicScreenTrack != selected || sharing.RemoteVisibleCount != 1)
                { Fail("STAND_LOST_VIEW_OR_PERMISSION"); yield break; }
                if (Media && (speaker.mute || !At(speaker, cafe.publicScreen.transform)))
                { Fail("STAND_LOST_WALL_SOUND"); yield break; }
                yield return ExpectRendered(hostColors, key + "-stood-viewer");
                if (failed) yield break;
                // A live share does not occupy the original chair. The new
                // sitter's view cannot be covered by the previous user's video.
                int origin = hostColors ? 0 : 1;
                FindButton("返回房间").onClick.Invoke();
                yield return Wait(() => !ui.BlocksRoomMenu && cafe.Mode == CafePrototype.InputMode.Walking,
                    "WALL_RETURN_BEFORE_TAKE_SEAT");
                if (failed) yield break;
                cafe.Sit(origin);
                yield return Wait(() => cafe.CurrentSeat == origin && sharing.RemoteVisibleCount == 0,
                    "SHARING_BLOCKED_OR_COVERED_NEW_SITTER");
                if (failed) yield break;
                if (sync.PublicScreenTrack != selected || !sharing.PublicWallHasFrame)
                { Fail("OCCUPANCY_STOPPED_WALL"); yield break; }
                cafe.Stand();
                yield return Wait(() => cafe.CurrentSeat < 0 && sharing.RemoteVisibleCount == 1,
                    "WALKING_SHARE_NOT_RESTORED_AT_FREE_COMPUTER");
                if (failed) yield break;
                hotspot = AimAtWall();
                if (hotspot == null) { Fail("WALL_HOTSPOT_AFTER_STAND"); yield break; }
                cafe.Activate(hotspot);
                yield return Wait(() => HasWallLabel(currentChoice) && FindButton("关闭放映") != null,
                    "STANDING_PUBLISHER_NOT_IN_PICKER");
                if (failed) yield break;
                Debug.Log("B2_WALL_FREE_CHAIR_DURING_SHARE_PASS");
                Debug.Log("B2_WALL_SHARE_SURVIVES_STAND_PASS viewer");
                Signal(key + "-stand-verified");
            }
            if (!hostColors)
            {
                yield return Wait(() => FindButton("关闭放映") != null, "CLOSE_BUTTON_MISSING");
                if (failed) yield break;
                FindButton("关闭放映").onClick.Invoke();
                Signal(key + "-cleared");
                if (Media)
                {
                    yield return Wait(() => !speaker.mute && At(speaker, seatScreen), "CLOSED_WALL_SOUND_ROUTE");
                    if (failed) yield break;
                    Signal(key + "-sound-at-seat");
                    yield return Wait(() => Has(key + "-sound-stopped") && sharing.RemoteSoundCountForTest == 0 &&
                        sharing.RemoteVisibleCount == 1, "SOUND_STOP_WITHOUT_VIDEO_STOP");
                    if (failed) yield break;
                    Debug.Log("B2_WALL_DESKTOP_SOUND_STOP_KEEPS_VIDEO_PASS");
                }
            }
            yield return Wait(() => Has(key + "-publisher-clear") && sync.PublicScreenIdentity == "" &&
                sharing.PublicWallClearedForTest && (!hostColors || FindButton(choice) == null),
                "WALL_DID_NOT_CLEAR");
            if (failed) yield break;
            if (sharing.RemoteVisibleCount < 1)
            { Fail("CLEAR_CHANGED_PRIVATE_VIDEO_OR_LEFT_SOURCE"); yield break; }
            if (Media && hostColors && (speaker.mute || !At(speaker, seatScreen)))
            { Fail("REVOKED_WALL_SOUND_ROUTE"); yield break; }
            Debug.Log("B2_WALL_CLEAR_KEEPS_STATION_PASS");
            if (hostColors)
            {
                Signal(key + "-revoked-seen");
                yield return Wait(() => Has(key + "-regranted") && FindButton(choice) != null, "REGRANT_NOT_LISTED");
                if (failed) yield break;
                if (sync.PublicScreenIdentity != "" || !sharing.PublicWallClearedForTest)
                { Fail("REGRANT_AUTO_PROJECTED"); yield break; }
                Debug.Log("B2_WALL_REGRANT_NEEDS_SELECTION_PASS");
                FindButton(choice).onClick.Invoke();
                yield return Wait(() => sync.PublicScreenIdentity == PeerIdentity() &&
                    PeerIdentity() != "" && sharing.PublicWallVisibleForTest, "RESELECT_VISIBLE");
                if (failed) yield break;
                Signal(key + "-reselected");
                yield return Wait(() => Has(key + "-stopped") && sync.PublicScreenIdentity == "" &&
                    sharing.PublicWallClearedForTest && FindButton(choice) == null &&
                    (!Flag("-b2KeepSharing") || sharing.RemoteVisibleCount == 0) &&
                    (!Media || sharing.RemoteSoundCountForTest == 0), "STOP_LEFT_STALE_WALL");
                if (failed) yield break;
                Debug.Log("B2_WALL_STOP_CLEARS_PASS");
            }
            FindButton("返回房间").onClick.Invoke();
            Signal(key + "-checked");
            yield return Wait(() => Has(key + "-done"), "PUBLISHER_NOT_DONE");
        }
    }
    IEnumerator Run(bool host)
    {
        sharing.SyntheticDesktopSoundForTest = Media;
        if (host)
        {
            network.SetEntryFields("B2 Wall QA Host", "");
            network.CreateRoom();
        }
        else
        {
            yield return Wait(() => Has("invite"), "NO_INVITE");
            if (failed) yield break;
            network.SetEntryFields("B2 Wall QA Guest", File.ReadAllText(FileAt("invite")).Trim());
            network.JoinRoom();
        }
        yield return Wait(() => network.State == NetCafeNetworkSession.Phase.Connected, "CONNECT");
        if (failed) yield break;
        if (host)
        {
            yield return null;
            ui.HandleBack(); // Close the real invitation page and enter the room.
            string temp = FileAt("invite-writing");
            File.WriteAllText(temp, network.InviteCode);
            File.Move(temp, FileAt("invite"));
        }
        yield return Wait(() => !ui.BlocksRoomMenu && cafe.Mode == CafePrototype.InputMode.Walking,
            "REAL_ROOM_ENTRY");
        if (failed) yield break;
        if (Flag("-b2ShareUi"))
        {
            yield return OpenSharePage();
            if (failed) yield break;
            if (FindButton("开始共享整个屏幕") != null || sharing.Sharing || sharing.Busy)
            { Fail("UNSEATED_SHARING_NOT_BLOCKED"); yield break; }
            CloseSharePage();
            Debug.Log("B2_WALL_SHARE_ENTRY_PASS");
        }
        yield return Project(host, true);
        if (failed) yield break;
        yield return Project(!host, false);
        if (failed) yield break;
        Signal(host ? "host-finished" : "guest-finished");
        yield return Wait(() => Has(host ? "guest-finished" : "host-finished"), "PEER_NOT_FINISHED");
        if (failed) yield break;
        network.Leave();
        yield return Wait(() => network.State == NetCafeNetworkSession.Phase.Disconnected, "LEAVE");
        if (failed) yield break;
        Debug.Log("B2_WALL_TWO_CLIENT_PASS " + (host ? "host" : "guest"));
        Application.Quit(0);
    }
    static bool At(AudioSource source, Transform target) =>
        (source.transform.position - target.position).sqrMagnitude < .01f;
    readonly float[] audioSample = new float[1024];
    bool HasPcm(AudioSource source)
    {
        source.GetOutputData(audioSample, 0);
        return Array.Exists(audioSample, x => Mathf.Abs(x) > .002f);
    }
    void StopVideo()
    {
        if (sharing != null) sharing.SetTestCapture(null);
        if (sourceRunner != null) { StopCoroutine(sourceRunner); sourceRunner = null; }
        try { source?.Stop(); } catch (Exception) {}
        try
        {
            if (video != null && network != null && network.TransportRoom != null &&
                network.TransportRoom.LocalParticipant != null)
                network.TransportRoom.LocalParticipant.UnpublishTrack(video,false);
        }
        catch (Exception) {}
        source?.Dispose(); source = null; video = null;
        if (checkerboard != null) { Destroy(checkerboard); checkerboard = null; }
    }
    void OnDestroy() => StopVideo();
}
#endif

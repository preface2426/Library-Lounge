#if NETCAFE_B1
using System;
using System.Collections;
using System.Collections.Generic;
using LiveKit;
using LiveKit.Proto;
using UnityEngine;

// B2: local desktop is NEVER published on join, seating, startup or reconnect.
// The user must explicitly press the sharing button in the online room UI.
// Desktop sound is an independent opt-in. No remote input or webcam.
[DefaultExecutionOrder(120)]
public sealed partial class CafeDesktopSharing : MonoBehaviour
{
    const string LegacyDesktopTrack = "netcafe-desktop-opt-in";
    const string DesktopTrack = "netcafe-desktop-v2";
    const int ShareFrameRate = 12;
    const float ShareFrameInterval = 1f / ShareFrameRate;

    sealed class LimitedTextureSource : TextureVideoSource
    {
        float nextRead;
        public LimitedTextureSource(Texture texture) : base(texture) { }
        protected override bool ReadBuffer()
        {
            if (Time.unscaledTime < nextRead) return false;
            nextRead = Time.unscaledTime + ShareFrameInterval;
            return base.ReadBuffer();
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public bool BeginReadbackForTest()
        {
            if (!_reading) base.ReadBuffer();
            return HasReadbackInFlight;
        }
#endif
    }

    sealed class RemoteView
    {
        public string Identity, Sid;
        public VideoStream Stream;
        public Coroutine Runner;
        public GameObject Quad;
        public Material Material;
        public int Seat = -1;
        public bool HasFrame;
        public int Width, Height;
        public int UploadedFrames;
    }

    public event Action Changed;
    public bool Sharing { get; private set; }
    public bool Busy { get; private set; }
    // This visible default applies only after the publisher starts a new share.
    public bool AllowPublicDisplayOnStart { get; private set; } = true;
    public bool AllowPublicDisplay { get; private set; }
    public long PublicGrant { get; private set; }
    int sharedSeat = -1;
    internal int PublishedShareSeat => Sharing ? sharedSeat : -1;
    internal string PublishedShareTrack => Sharing ? CurrentWallTrack : null;
    string grantedWallTrack;
    public string ApprovedWallTrack => Sharing && AllowPublicDisplay && CurrentWallTrack == grantedWallTrack
        ? grantedWallTrack : null;
    string CurrentWallTrack
    {
        get
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (syntheticSharingForTest) return syntheticWallVideo?.Sid;
#endif
            return track?.Sid;
        }
    }
    public string Status { get; private set; } = "屏幕共享关闭 · 只有你可以看到自己的桌面";
    public string CaptureMonitorLabel => capture != null ? capture.MonitorLabel : "未找到显示器";
    public string StartBlockReason
    {
        get
        {
            if (!ShareRoomConnected)
                return "请先加入联网房间，或等待房间重新连接。";
            if (cafe == null || !cafe.IsComputerSeat)
                return "请先在一台空闲电脑前按 E 入座。";
            if (CaptureTexture == null)
                return "请先确认机位上能看到自己的电脑桌面。";
            return null;
        }
    }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    Texture testCapture;
    bool testCaptureFlipped;
    internal void SetTestCapture(Texture texture, bool flipped = false)
    {
        testCapture = texture;
        testCaptureFlipped = flipped;
    }
    internal Vector2Int RemoteSizeForTest(string identity)
    {
        foreach (var view in remote.Values)
            if (view.Identity == identity) return new Vector2Int(view.Width, view.Height);
        return Vector2Int.zero;
    }
    internal int PendingPublishesForTest { get; private set; }
    internal int LateUnpublishesForTest { get; private set; }
    internal bool BeginReadbackForTest() => Sharing && source is LimitedTextureSource limited && limited.BeginReadbackForTest();
#endif
    Texture CaptureTexture
    {
        get
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (testCapture != null) return testCapture;
#endif
            return capture != null && capture.FrameReady ? capture.DesktopTexture : null;
        }
    }
    public int RemoteVideoCount => remote.Count;
    // A subscribed track is not a visible remote screen until video frames arrive.
    public int RemoteReadyCount
    {
        get
        {
            int count = 0;
            foreach (var view in remote.Values) if (view.HasFrame) count++;
            return count;
        }
    }
    public int RemoteVisibleCount
    {
        get
        {
            int count = 0;
            foreach (var view in remote.Values)
                if (view.HasFrame && view.Quad != null && view.Quad.activeSelf) count++;
            return count;
        }
    }

    NetCafeNetworkSession network;
    CafeRoomSync sync;
    CafeDesktopCapture capture;
    CafePrototype cafe;
    Room hooked;
    bool ShareRoomConnected => network != null && network.State == NetCafeNetworkSession.Phase.Connected &&
        hooked != null && hooked == network.TransportRoom && hooked.IsConnected;
    TextureVideoSource source;
    LocalVideoTrack track;
    Texture authorizedCapture;
    RenderTexture scaled;
    Coroutine sourceRunner;
    readonly Dictionary<string, RemoteView> remote = new();
    GameObject publicWall;
    Material publicWallMaterial;
    Mesh publicWallMesh;
    public bool PublicWallHasFrame => publicWall != null && publicWall.activeSelf &&
        publicWallMaterial != null && publicWallMaterial.mainTexture != null;
    bool OwnWallSource => hooked != null && hooked.LocalParticipant != null && sync != null &&
        sync.PublicScreenIdentity == hooked.LocalParticipant.Identity;
    int generation;
    float nextBlit;

    void OnEnable()
    {
        network = GetComponent<NetCafeNetworkSession>();
        sync = GetComponent<CafeRoomSync>();
        capture = GetComponent<CafeDesktopCapture>();
        cafe = GetComponent<CafePrototype>();
        voice = GetComponent<CafeSpatialVoice>();
        if (network != null) network.StateChanged += OnNetworkState;
        if (cafe != null) cafe.Changed += OnLocalSeatChanged;
        if (capture != null) capture.CaptureFailed += OnCaptureFailed;
    }

    void OnDisable()
    {
        if (network != null) network.StateChanged -= OnNetworkState;
        if (cafe != null) cafe.Changed -= OnLocalSeatChanged;
        if (capture != null) capture.CaptureFailed -= OnCaptureFailed;
        ResetRoom();
    }
    void OnDestroy() { ResetRoom(); }

    void OnNetworkState(NetCafeNetworkSession.Phase phase)
    {
        if (phase == NetCafeNetworkSession.Phase.Connected)
            Hook();
        else if (phase != NetCafeNetworkSession.Phase.Reconnecting)
            ResetRoom();
        else if (Sharing || Busy)
            StopSharing(); // Reconnecting never silently republishes a desktop.
    }

    void OnLocalSeatChanged()
    {
        if (Busy && (cafe == null || !cafe.IsComputerSeat))
            StopSharing();
    }
    void OnCaptureFailed() { if (Sharing || Busy) StopSharing(); }

    void Hook()
    {
        Room now = network == null ? null : network.TransportRoom;
        if (now == null || now == hooked) return;
        ResetRoom();
        hooked = now;
        hooked.TrackSubscribed += OnSubscribed;
        hooked.TrackUnsubscribed += OnUnsubscribed;
        hooked.TrackUnpublished += OnUnpublished;
        foreach (var participant in now.RemoteParticipants.Values)
            foreach (var publication in participant.Tracks.Values)
                if (publication.Track is IRemoteTrack incoming)
                    OnSubscribed(incoming, publication, participant);
        Status = "屏幕共享关闭 · 观看他人的电脑不需要分享自己的屏幕";
        Changed?.Invoke();
    }

    void Update()
    {
        if (network != null && network.State == NetCafeNetworkSession.Phase.Connected &&
            hooked != network.TransportRoom)
            Hook();

        if (Busy && (StartBlockReason != null || authorizedCapture != CaptureTexture))
            StopSharing();
        if (Sharing)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (syntheticSharingForTest)
            {
                if (!ShareRoomConnected)
                    StopSharing();
            }
            else
#endif
            if (!ShareRoomConnected || authorizedCapture == null || authorizedCapture != CaptureTexture)
            {
                StopSharing();
            }
            else if (Time.unscaledTime >= nextBlit)
            {
                nextBlit = Time.unscaledTime + ShareFrameInterval;
                if (scaled != null) BlitDesktop(authorizedCapture);
            }
        }

        if (AllowPublicDisplay && ApprovedWallTrack == null) SetPublicDisplayConsent(null);
        UpdateWallProjection();
        foreach (var view in remote.Values)
        {
            int seat = sync == null ? -1 : sync.RemoteScreenSeatFor(view.Identity, view.Sid);
            // A walking publisher keeps the original display until another player takes it.
            if (seat < 0 || cafe == null || seat >= cafe.screens.Length ||
                seat == cafe.CurrentSeat)
            {
                if (view.Quad != null) view.Quad.SetActive(false);
                view.Seat = -1;
                continue;
            }
            bool seatChanged = view.Seat != seat;
            if (seatChanged && view.Quad != null)
            {
                var parent = cafe.screens[seat].transform;
                view.Quad.transform.SetParent(parent, false);
                // A small offset in front of the physical screen avoids z-fighting.
                view.Quad.transform.localPosition = new Vector3(0f, 0f, -.535f);
                view.Quad.transform.localRotation = Quaternion.identity;
                view.Quad.transform.localScale = Vector3.one;
                view.Seat = seat;
            }
            if (view.Quad != null)
            {
                bool visible = view.HasFrame && view.Material != null && view.Material.mainTexture != null;
                if (visible && (seatChanged || !view.Quad.activeSelf))
                    Debug.Log("[NetCafe/B2] REMOTE_SCREEN_MAPPED seat=" + (seat + 1));
                view.Quad.SetActive(visible);
            }
        }
        UpdateDesktopSound();
    }

    void BlitDesktop(Texture desktop)
    {
        bool flipped = capture != null && capture.desktopMaterial != null &&
            capture.desktopMaterial.GetFloat("_FlipY") > .5f;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (desktop == testCapture) flipped = testCaptureFlipped;
#endif
        // Normalize the native WGC rows before either LiveKit or the local wall samples them.
        if (flipped) Graphics.Blit(desktop, scaled, new Vector2(1, -1), new Vector2(0, 1));
        else Graphics.Blit(desktop, scaled);
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // QA-only synthetic pixels. Never starts native capture or publishes a
    // desktop. These hooks are absent from non-development builds.
    bool syntheticSharingForTest;
    LocalVideoTrack syntheticWallVideo;
    Texture syntheticWallTexture;
    public bool PublicWallVisibleForTest => PublicWallHasFrame;
    public void SetSyntheticWallVideoForTest(LocalVideoTrack video, Texture texture)
    {
        syntheticWallVideo = video;
        syntheticWallTexture = texture;
        SetSyntheticSharingForTest(true);
    }
    public bool PublicWallClearedForTest => (publicWall == null || !publicWall.activeSelf) &&
        (publicWallMaterial == null || publicWallMaterial.mainTexture == null);
    public void SetSyntheticSharingForTest(bool value)
    {
        if (!value) { StopSharing(); return; }
        if (network == null || network.State != NetCafeNetworkSession.Phase.Connected ||
            cafe == null || !cafe.IsComputerSeat || capture == null || capture.Active)
            throw new InvalidOperationException("Synthetic B2 fixture requires a connected seat without native capture");
        syntheticSharingForTest = true;
        sharedSeat = cafe.CurrentSeat;
        Sharing = true;
        AllowPublicDisplay = false;
        sync?.NotifySharingChanged();
    }
#endif

    public void TogglePublicDisplayOnStart()
    {
        if (Sharing || Busy) return;
        AllowPublicDisplayOnStart = !AllowPublicDisplayOnStart;
        Changed?.Invoke();
    }

    // The live permission belongs to one published track and can be revoked at any time.
    public void TogglePublicDisplayConsent()
    {
        if (!Sharing || network == null || network.State != NetCafeNetworkSession.Phase.Connected)
            return;
        string next = AllowPublicDisplay ? null : CurrentWallTrack;
        if (!AllowPublicDisplay && string.IsNullOrEmpty(next)) return;
        SetPublicDisplayConsent(next);
    }
    void SetPublicDisplayConsent(string sid)
    {
        grantedWallTrack = sid;
        AllowPublicDisplay = sid != null;
        if (AllowPublicDisplay) PublicGrant++;
        else if (OwnWallSource) HideWallProjection();
        sync?.NotifySharingChanged();
        Changed?.Invoke();
    }

    // One physical screen material displays the host-authorized video; no
    // second decoder or new media track is created for the big screen.
    void CreateWallProjection()
    {
        if (publicWall != null || cafe == null || cafe.publicScreen == null ||
            capture == null || capture.desktopMaterial == null) return;
        publicWall = new GameObject("B2 consented room big-screen video");
        publicWall.transform.SetParent(cafe.publicScreen.transform, false);
        publicWall.transform.localPosition = new Vector3(0f, 0f, -.56f);
        publicWall.transform.localRotation = Quaternion.identity;
        publicWall.transform.localScale = Vector3.one;
        publicWallMesh = new Mesh { name = "B2 public wall shared stream quad" };
        publicWallMesh.vertices = new[]{new Vector3(-.5f,-.5f,0), new Vector3(.5f,-.5f,0),
            new Vector3(-.5f,.5f,0), new Vector3(.5f,.5f,0)};
        publicWallMesh.uv = new[]{Vector2.zero, Vector2.right, Vector2.up, Vector2.one};
        publicWallMesh.triangles = new[]{0,2,1,2,3,1};
        publicWallMesh.RecalculateBounds();
        publicWall.AddComponent<MeshFilter>().sharedMesh = publicWallMesh;
        publicWallMaterial = new Material(capture.desktopMaterial) { name = "B2 public wall actual consented video" };
        publicWallMaterial.SetFloat("_FlipY", 0f);
        var renderer = publicWall.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = publicWallMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        publicWall.SetActive(false);
    }
    void HideWallProjection()
    {
        if (publicWall != null) publicWall.SetActive(false);
        if (publicWallMaterial != null) publicWallMaterial.mainTexture = null;
    }
    void DestroyWallProjection()
    {
        HideWallProjection();
        if (publicWall != null) Destroy(publicWall);
        if (publicWallMesh != null) Destroy(publicWallMesh);
        if (publicWallMaterial != null) Destroy(publicWallMaterial);
        publicWall = null; publicWallMesh = null; publicWallMaterial = null;
    }
    void UpdateWallProjection()
    {
        if (sync == null || !sync.HasApprovedWallSelection ||
            network == null || network.State != NetCafeNetworkSession.Phase.Connected)
        { HideWallProjection(); return; }
        Texture image = null;
        float flipY = 0f;
        if (OwnWallSource)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (syntheticSharingForTest) image = syntheticWallTexture;
            else
#endif
                image = scaled;
        }
        else if (remote.TryGetValue(sync.PublicScreenTrack, out var view) &&
                 view.Identity == sync.PublicScreenIdentity && view.HasFrame && view.Material != null)
        {
            image = view.Material.mainTexture;
            flipY = view.Material.GetFloat("_FlipY");
        }
        if (image == null) { HideWallProjection(); return; }
        CreateWallProjection();
        if (publicWallMaterial == null) return;
        publicWallMaterial.SetFloat("_FlipY", flipY);
        publicWallMaterial.mainTexture = image;
        publicWall.SetActive(true);
    }

    // Called only by an explicit sharing control.
    public void ToggleSharing()
    {
        if (Sharing || Busy) { StopSharing(); return; }
        string block = StartBlockReason;
        if (block != null)
        {
            Status = block;
            Changed?.Invoke();
            return;
        }
        // A new sharing session always needs a direct click, even after reconnecting.
        authorizedCapture = CaptureTexture;
        sharedSeat = cafe.CurrentSeat;
        StartCoroutine(PublishAfterClick(++generation, hooked));
    }

    IEnumerator PublishAfterClick(int requested, Room origin)
    {
        Busy = true;
        Status = "正在建立屏幕共享…共享后其他成员能看到你的整个显示器";
        Changed?.Invoke();
        PublishTrackInstruction instruction;
        LocalVideoTrack requestedTrack;
        try
        {
            var desktop = authorizedCapture;
            int nativeWidth = desktop != null ? desktop.width : 0;
            int nativeHeight = desktop != null ? desktop.height : 0;
            if (nativeWidth <= 0 || nativeHeight <= 0)
                throw new InvalidOperationException("Desktop capture size unavailable");
            float scale = Mathf.Min(1f, Mathf.Min(2560f / nativeWidth, 1440f / nativeHeight));
            int width = Mathf.Max(2, Mathf.RoundToInt(nativeWidth * scale) / 2 * 2);
            int height = Mathf.Max(2, Mathf.RoundToInt(nativeHeight * scale) / 2 * 2);
            scaled = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            { name = "Explicit shared desktop downsample", filterMode = FilterMode.Bilinear };
            scaled.Create();
            BlitDesktop(desktop);
            source = new LimitedTextureSource(scaled);
            track = requestedTrack = LocalVideoTrack.CreateVideoTrack(DesktopTrack, source, origin);
            instruction = origin.LocalParticipant.PublishTrack(track, new TrackPublishOptions {
                VideoCodec = VideoCodec.Vp8,
                Simulcast = false,
                Source = TrackSource.SourceScreenshare,
                VideoEncoding = new VideoEncoding {
                    MaxBitrate = width > 1920 || height > 1080 ? 6500000UL : 5000000UL,
                    MaxFramerate = ShareFrameRate
                }
            });
        }
        catch (Exception e)
        {
            Debug.LogWarning("[NetCafe/B2] Init failed: " + e.GetType().Name);
            StopSharing();
            Status = "无法初始化屏幕视频源；共享保持关闭";
            Changed?.Invoke();
            yield break;
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        PendingPublishesForTest++;
#endif
        yield return instruction;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        PendingPublishesForTest--;
#endif
        // Cancellation stops frame production immediately. The SDK may still
        // acknowledge the original request; remove that track once it has a SID.
        if (requested != generation)
        {
            if (!instruction.IsError)
            {
                Unpublish(origin, requestedTrack);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                LateUnpublishesForTest++;
#endif
            }
            yield break;
        }
        if (instruction.IsError || origin != hooked || StartBlockReason != null || authorizedCapture != CaptureTexture)
        {
            if (!instruction.IsError) Unpublish(origin, requestedTrack);
            StopSharing();
            Status = "共享没有建立，桌面仍只在本机可见";
            Changed?.Invoke();
            yield break;
        }
        source.Start();
        sourceRunner = StartCoroutine(source.Update());
        Sharing = true;
        Debug.Log("[NetCafe/B2] SCREEN_TRACK_PUBLISHED_PASS (explicit consent)");
        Busy = false;
        nextBlit = 0;
        Status = "正在共享本机桌面 · 原机位为电脑 " + (sharedSeat + 1) + "（无远程控制）";
        if (AllowPublicDisplayOnStart && !string.IsNullOrEmpty(CurrentWallTrack))
            SetPublicDisplayConsent(CurrentWallTrack);
        else
        {
            sync?.NotifySharingChanged();
            Changed?.Invoke();
        }
    }

    public void StopSharing()
    {
        StopDesktopSound();
        bool hadPublication = Sharing;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        syntheticSharingForTest = false;
        syntheticWallVideo = null;
        syntheticWallTexture = null;
#endif
        ++generation;
        AllowPublicDisplay = false;
        grantedWallTrack = null;
        if (OwnWallSource) HideWallProjection();
        if (sourceRunner != null) { StopCoroutine(sourceRunner); sourceRunner = null; }
        try { source?.Stop(); }
        catch (Exception e) { Debug.LogWarning("[NetCafe/B2] Source stop: " + e.GetType().Name); }
        if (Sharing) Unpublish(hooked, track);
        var oldSource = source;
        var oldTexture = scaled;
        source = null;
        scaled = null;
        track = null;
        authorizedCapture = null;
        sharedSeat = -1;
        Sharing = false;
        Busy = false;
        Status = "屏幕共享已关闭 · 不再向其他玩家发送画面";
        if (hadPublication) sync?.NotifySharingChanged();
        try { oldSource?.Dispose(); }
        catch (Exception e) { Debug.LogWarning("[NetCafe/B2] Source dispose: " + e.GetType().Name); }
        if (oldTexture != null) { oldTexture.Release(); Destroy(oldTexture); }
        Changed?.Invoke();
    }

    static void Unpublish(Room room, ILocalTrack published)
    {
        try
        {
            if (room?.LocalParticipant != null && published != null && !string.IsNullOrEmpty(published.Sid))
                room.LocalParticipant.UnpublishTrack(published, false);
        }
        catch (Exception e) { Debug.LogWarning("[NetCafe/B2] Unpublish: " + e.GetType().Name); }
    }

    void OnSubscribed(IRemoteTrack incoming, RemoteTrackPublication publication, RemoteParticipant participant)
    {
        if (incoming is RemoteVideoTrack video && publication.Source == TrackSource.SourceScreenshare)
            AddRemote(video, participant.Identity, publication.Name == LegacyDesktopTrack);
        else if (incoming is RemoteAudioTrack audio && publication.Source == TrackSource.SourceScreenshareAudio)
            AddRemoteSound(audio, publication, participant.Identity);
    }
    void OnUnsubscribed(IRemoteTrack incoming, RemoteTrackPublication publication, RemoteParticipant participant)
    {
        if (incoming is RemoteVideoTrack video) RemoveRemote(video.Sid);
        else if (incoming is RemoteAudioTrack audio) RemoveRemoteSound(audio.Sid);
    }
    void OnUnpublished(RemoteTrackPublication publication, RemoteParticipant participant)
    {
        RemoveRemote(publication.Sid);
        RemoveRemoteSound(publication.Sid);
    }
    void AddRemote(RemoteVideoTrack trackValue, string identity, bool legacyFlip)
    {
        if (trackValue == null || string.IsNullOrEmpty(identity) ||
            remote.ContainsKey(trackValue.Sid)) return;
        // A replacement subscription can precede unsubscription of the old track.
        string replacedSid = null;
        foreach (var existing in remote.Values)
            if (existing.Identity == identity) { replacedSid = existing.Sid; break; }
        if (replacedSid != null) RemoveRemote(replacedSid);
        try
        {
            var view = new RemoteView { Identity = identity, Sid = trackValue.Sid };
            // Unity URP strips the built-in Unlit/Texture shader when unused.
            // Clone the serialized scene material that is guaranteed present in builds.
            if (capture == null || capture.desktopMaterial == null)
                throw new InvalidOperationException("Remote monitor URP shader material missing");
            view.Material = new Material(capture.desktopMaterial) { name = "Subscribed desktop frame" };
            // Older publishers encoded raw WGC rows. New ones send upright video.
            view.Material.SetFloat("_FlipY", legacyFlip ? 1f : 0f);
            var quad = new GameObject("SubscribedDesktop_" + identity);
            quad.transform.SetParent(transform, false);
            var mesh = new Mesh { name = "Network desktop display quad" };
            mesh.vertices = new []{new Vector3(-.5f,-.5f,0),new Vector3(.5f,-.5f,0),
                new Vector3(-.5f,.5f,0),new Vector3(.5f,.5f,0)};
            mesh.uv = new []{Vector2.zero,Vector2.right,Vector2.up,Vector2.one};
            mesh.triangles = new []{0,2,1,2,3,1};
            mesh.RecalculateBounds();
            quad.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = quad.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = view.Material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            quad.SetActive(false);
            view.Quad = quad;
            view.Stream = new VideoStream(trackValue);
            view.Stream.TextureReceived += texture =>
            {
                if (!remote.ContainsKey(view.Sid) || view.Material == null || texture == null) return;
                view.Material.mainTexture = texture;
                view.HasFrame = true;
                view.Width = texture.width; view.Height = texture.height;
                Debug.Log("[NetCafe/B2] REMOTE_SCREEN_FRAME_PASS size=" + view.Width + "x" + view.Height);
                Changed?.Invoke();
            };
            view.Stream.TextureUploaded += () => { if (view.HasFrame) view.UploadedFrames++; };
            view.Stream.Start();
            view.Runner = StartCoroutine(view.Stream.Update());
            remote.Add(view.Sid, view);
            Debug.Log("[NetCafe/B2] REMOTE_SCREEN_TRACK_SUBSCRIBED_PASS");
            Changed?.Invoke();
        }
        catch (Exception error)
        {
            Debug.LogWarning("[NetCafe/B2] Video subscribe: " + error.GetType().Name);
            Status = "远端屏幕解码失败，其他功能不受影响";
            Changed?.Invoke();
        }
    }
    void RemoveRemote(string sid)
    {
        if (!remote.TryGetValue(sid, out var view)) return;
        remote.Remove(sid);
        if (sync != null && sync.PublicScreenTrack == sid)
            HideWallProjection();
        if (view.Quad != null) view.Quad.SetActive(false); // clear on revocation immediately
        if (view.Runner != null) StopCoroutine(view.Runner);
        view.Stream?.Stop();
        view.Stream?.Dispose();
        if (view.Material != null) { view.Material.mainTexture = null; Destroy(view.Material); }
        if (view.Quad != null)
        {
            var mf = view.Quad.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
            Destroy(view.Quad);
        }
        Changed?.Invoke();
    }
    void ResetRoom()
    {
        StopSharing();
        if (hooked != null)
        {
            hooked.TrackSubscribed -= OnSubscribed;
            hooked.TrackUnsubscribed -= OnUnsubscribed;
            hooked.TrackUnpublished -= OnUnpublished;
            hooked = null;
        }
        foreach (var id in new List<string>(remote.Keys)) RemoveRemote(id);
        foreach (var id in new List<string>(remoteSound.Keys)) RemoveRemoteSound(id);
        DestroyWallProjection();
        Status = "屏幕共享关闭 · 已离开房间";
        Changed?.Invoke();
    }
}
#endif

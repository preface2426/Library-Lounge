#if NETCAFE_B1
using System;
using System.Collections;
using System.Collections.Generic;
using LiveKit;
using LiveKit.Proto;
using UnityEngine;

// A separate media boundary: never publishes desktop pixels or native input.
// Remote playback is spatial; local microphone is OFF until an explicit click.
[DefaultExecutionOrder(100)]
public sealed class CafeSpatialVoice : MonoBehaviour
{
    sealed class RemoteAudio
    {
        public string Identity;
        public GameObject Holder;
        public AudioSource Source;
        public AudioStream Stream;
    }

    public NetCafeNetworkSession network;
    public CafeRoomSync sync;
    public bool MicrophoneActive { get; private set; }
    public bool MicrophoneBusy { get; private set; }
    public bool HearOthers { get; private set; } = true;
    public string Status { get; private set; } = "麦克风关闭 · 房间语音接收已开启";
    public int RemoteAudioCount => remote.Count;
    public event Action Changed;

    readonly Dictionary<string, RemoteAudio> remote = new();
    Room hooked;
    LocalAudioTrack localTrack;
    MicrophoneSource localSource;
    GameObject localSourceObject;
    Coroutine publishing;
    int generation;
    bool wasConnected;

    void OnEnable()
    {
        network = network != null ? network : GetComponent<NetCafeNetworkSession>();
        sync = sync != null ? sync : GetComponent<CafeRoomSync>();
        if (network != null) network.StateChanged += OnState;
    }

    void OnDisable()
    {
        if (network != null) network.StateChanged -= OnState;
        ReleaseAll();
    }
    void OnDestroy() { ReleaseAll(); }

    void OnState(NetCafeNetworkSession.Phase state)
    {
        if (state == NetCafeNetworkSession.Phase.Connected)
        {
            wasConnected = true;
            Attach();
        }
        else if (state != NetCafeNetworkSession.Phase.Reconnecting)
        {
            if (wasConnected || hooked != null || localSource != null) ReleaseAll();
            wasConnected = false;
        }
    }

    void Update()
    {
        if (network != null && network.State == NetCafeNetworkSession.Phase.Connected &&
            network.TransportRoom != null && hooked != network.TransportRoom)
            Attach();

        // The authoritative avatar interpolates in CafeRoomSync.Update. Reposition
        // AudioSources after motion; never allocate a per-frame audio buffer here.
        foreach (var entry in remote.Values)
        {
            Transform avatar = sync == null ? null : sync.FindPeerRoot(entry.Identity);
            entry.Source.mute = !HearOthers || avatar == null;
            if (avatar != null)
                entry.Holder.transform.position = avatar.position + Vector3.up * 1.25f;
        }
    }

    void Attach()
    {
        var room = network == null ? null : network.TransportRoom;
        if (room == null || room == hooked) return;
        ReleaseAll();
        hooked = room;
        hooked.TrackSubscribed += OnSubscribed;
        hooked.TrackUnsubscribed += OnUnsubscribed;
        // Tracks may be subscribed before our Connected callback fires.
        foreach (var participant in hooked.RemoteParticipants.Values)
            foreach (var publication in participant.Tracks.Values)
                if (publication.Track is RemoteAudioTrack track)
                    AddRemote(track, participant.Identity);
        Status = "麦克风关闭 · 房间语音已连接";
        Changed?.Invoke();
    }

    void OnSubscribed(IRemoteTrack track, RemoteTrackPublication pub, RemoteParticipant participant)
    {
        if (track is RemoteAudioTrack audio && pub.Source == TrackSource.SourceMicrophone)
            AddRemote(audio, participant.Identity);
    }
    void OnUnsubscribed(IRemoteTrack track, RemoteTrackPublication pub, RemoteParticipant participant)
    {
        if (track is RemoteAudioTrack audio) RemoveRemote(audio.Sid);
    }

    void AddRemote(RemoteAudioTrack track, string identity)
    {
        if (track == null || remote.ContainsKey(track.Sid)) return;
        try
        {
            var holder = new GameObject("SpatialVoice_" + identity);
            holder.transform.SetParent(transform, false);
            var source = holder.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = 1.2f;
            source.maxDistance = 18f;
            source.dopplerLevel = 0f;
            source.mute = true; // Until room avatar position has been resolved.
            var stream = new AudioStream(track, source);
            remote.Add(track.Sid, new RemoteAudio {
                Identity = identity, Holder = holder, Source = source, Stream = stream
            });
            Changed?.Invoke();
        }
        catch (Exception error)
        {
            Debug.LogWarning("[NetCafe/Voice] Playback unavailable: " + error.GetType().Name);
            Status = "远端语音播放失败，请检查音频输出设备";
            Changed?.Invoke();
        }
    }

    void RemoveRemote(string sid)
    {
        if (!remote.TryGetValue(sid, out var entry)) return;
        remote.Remove(sid);
        entry.Source?.Stop();
        entry.Stream?.Dispose();
        if (entry.Holder != null) Destroy(entry.Holder);
        Changed?.Invoke();
    }

    public void TogglePlayback()
    {
        HearOthers = !HearOthers;
        Changed?.Invoke();
    }

    // Called only from a user-facing button; never auto-enable on room join,
    // reconnect, avatar change, or load of a saved preference.
    public void ToggleMicrophone()
    {
        if (MicrophoneBusy) return;
        if (MicrophoneActive) { StopPublishing(); return; }
        EnableMicrophone();
    }

    // Same opt-in path for host and guest; the pre-join choice is an explicit
    // user action, and it publishes only after the room is connected.
    public void EnableMicrophone()
    {
        if (MicrophoneBusy || MicrophoneActive) return;
        if (network == null || network.State != NetCafeNetworkSession.Phase.Connected ||
            network.TransportRoom == null)
        {
            Status = "需要先加入一个房间才能开麦";
            Changed?.Invoke();
            return;
        }
        publishing = StartCoroutine(PublishAfterConsent(++generation));
    }

    IEnumerator PublishAfterConsent(int requested)
    {
        MicrophoneBusy = true;
        Status = "正在请求麦克风权限…";
        Changed?.Invoke();
        var room = hooked;
        yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);
        if (requested != generation || room == null || room != hooked ||
            network == null || network.State != NetCafeNetworkSession.Phase.Connected)
        {
            MicrophoneBusy = false; publishing = null;
            yield break;
        }
        if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
        {
            FailMicrophone("麦克风授权被拒绝；保持静音");
            yield break;
        }
        if (Microphone.devices == null || Microphone.devices.Length == 0)
        {
            FailMicrophone("未检测到麦克风；保持静音");
            yield break;
        }
        try
        {
            localSourceObject = new GameObject("OptInMicrophone");
            localSourceObject.transform.SetParent(transform, false);
            var options = new AudioProcessingOptions {
                EchoCancellation = true, NoiseSuppression = true, AutoGainControl = true
            };
            localSource = new MicrophoneSource(Microphone.devices[0], localSourceObject, options);
            localTrack = LocalAudioTrack.CreateAudioTrack("microphone", localSource, room);
        }
        catch (Exception error)
        {
            Debug.LogWarning("[NetCafe/Voice] Microphone initialization: " + error.GetType().Name);
            StopPublishing();
            FailMicrophone("无法初始化麦克风；保持静音");
            yield break;
        }

        var publish = room.LocalParticipant.PublishTrack(localTrack,
            new TrackPublishOptions {
                AudioEncoding = new AudioEncoding { MaxBitrate = 64000 },
                Source = TrackSource.SourceMicrophone
            });
        yield return publish;

        if (requested != generation || room != hooked ||
            network == null || network.State != NetCafeNetworkSession.Phase.Connected)
        {
            StopPublishing();
            yield break;
        }
        if (publish.IsError)
        {
            StopPublishing();
            FailMicrophone("麦克风发布失败；保持静音");
            yield break;
        }
        try
        {
            localSource.Start();
            MicrophoneActive = true;
            Status = "麦克风已开启，房间成员可以听见你";
        }
        catch (Exception error)
        {
            Debug.LogWarning("[NetCafe/Voice] Microphone start: " + error.GetType().Name);
            StopPublishing();
            FailMicrophone("麦克风无法录音；保持静音");
            yield break;
        }
        MicrophoneBusy = false;
        publishing = null;
        Changed?.Invoke();
    }

    void FailMicrophone(string message)
    {
        MicrophoneBusy = false;
        publishing = null;
        MicrophoneActive = false;
        Status = message;
        Changed?.Invoke();
    }

    public void StopPublishing()
    {
        ++generation;
        if (publishing != null)
        {
            StopCoroutine(publishing);
            publishing = null;
        }
        try { localSource?.Stop(); }
        catch (Exception e) { Debug.LogWarning("[NetCafe/Voice] Stop microphone: " + e.GetType().Name); }
        try { if (hooked != null && localTrack != null && hooked.LocalParticipant != null)
            hooked.LocalParticipant.UnpublishTrack(localTrack, false); }
        catch (Exception e) { Debug.LogWarning("[NetCafe/Voice] Unpublish: " + e.GetType().Name); }
        localSource?.Dispose();
        localSource = null;
        localTrack = null;
        if (localSourceObject != null) Destroy(localSourceObject);
        localSourceObject = null;
        MicrophoneBusy = false;
        MicrophoneActive = false;
        Status = "麦克风关闭";
        Changed?.Invoke();
    }

    void ReleaseAll()
    {
        StopPublishing();
        if (hooked != null)
        {
            hooked.TrackSubscribed -= OnSubscribed;
            hooked.TrackUnsubscribed -= OnUnsubscribed;
            hooked = null;
        }
        var ids = new List<string>(remote.Keys);
        foreach (string id in ids) RemoveRemote(id);
        Status = "已离开房间 · 麦克风关闭";
        Changed?.Invoke();
    }
}
#endif

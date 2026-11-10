#if NETCAFE_B1
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using LiveKit;
using LiveKit.Proto;
using UnityEngine;

public sealed partial class CafeDesktopSharing
{
    const string DesktopSoundPrefix = "netcafe-desktop-sound:";
    const string SoundOff = "电脑声音共享关闭";
    const int AudioFrames = 480;

    static class DesktopAudio
    {
        const string Plugin = "NetCafeCapture";
        [DllImport(Plugin, CallingConvention = CallingConvention.Cdecl)] internal static extern int NC_DesktopAudioSupported();
        [DllImport(Plugin, CallingConvention = CallingConvention.Cdecl)] internal static extern int NC_DesktopAudioStart();
        [DllImport(Plugin, CallingConvention = CallingConvention.Cdecl)] internal static extern int NC_DesktopAudioState();
        [DllImport(Plugin, CallingConvention = CallingConvention.Cdecl)] internal static extern int NC_DesktopAudioRead([Out] float[] output, int capacityFrames);
        [DllImport(Plugin, CallingConvention = CallingConvention.Cdecl)] internal static extern void NC_DesktopAudioStop();
    }

    sealed class DesktopSoundSource : RtcAudioSource
    {
        public override event Action<float[], int, int> AudioRead;
        readonly float[] scratch = new float[AudioFrames];
        readonly float[] block = new float[AudioFrames];
        int filled;
        public DesktopSoundSource() : base(RtcAudioSourceType.AudioSourceCustom, 48000, 1) {}
        public void Pump()
        {
            // Bound catch-up after a slow render frame; pass whole 10 ms PCM blocks.
            for (int i = 0; i < 8; i++)
            {
                int count = DesktopAudio.NC_DesktopAudioRead(scratch, AudioFrames - filled);
                if (count <= 0) break;
                Array.Copy(scratch, 0, block, filled, count);
                filled += count;
                if (filled != AudioFrames) continue;
                filled = 0;
                AudioRead?.Invoke(block, 1, 48000);
            }
        }
    }

    sealed class RemoteSound
    {
        public string Identity, VideoSid;
        public RemoteTrackPublication Publication;
        public AudioSource Speaker;
        public AudioStream Stream;
    }

    readonly Dictionary<string, RemoteSound> remoteSound = new();
    CafeSpatialVoice voice;
    RtcAudioSource desktopSoundSource;
    LocalAudioTrack desktopSoundTrack;
    bool nativeAudioStarted, soundPublished;
    int soundGeneration;
    static bool? audioSupported;
    public bool SoundSharing { get; private set; }
    public bool SoundBusy { get; private set; }
    public string SoundStatus { get; private set; } = SoundOff;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    internal bool SyntheticDesktopSoundForTest { get; set; }
    float nextTone;
    internal int RemoteSoundCountForTest => remoteSound.Count;
    internal AudioSource RemoteSpeakerForTest(string identity)
    {
        foreach (var sound in remoteSound.Values)
            if (sound.Identity == identity) return sound.Speaker;
        return null;
    }
#endif

    public bool CanShareDesktopSound
    {
        get
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (SyntheticDesktopSoundForTest) return true;
#endif
            if (audioSupported.HasValue) return audioSupported.Value;
            if (Application.platform != RuntimePlatform.WindowsPlayer &&
                Application.platform != RuntimePlatform.WindowsEditor)
                return (audioSupported = false).Value;
            try { audioSupported = DesktopAudio.NC_DesktopAudioSupported() == 1; }
            catch (DllNotFoundException) { audioSupported = false; }
            catch (EntryPointNotFoundException) { audioSupported = false; }
            catch (BadImageFormatException) { audioSupported = false; }
            return audioSupported.Value;
        }
    }

    public void ToggleDesktopSound()
    {
        if (SoundSharing || SoundBusy)
        {
            StopDesktopSound();
            Changed?.Invoke();
            return;
        }
        string videoSid = CurrentWallTrack;
        if (!ShareRoomConnected || !Sharing || string.IsNullOrEmpty(videoSid) || !CanShareDesktopSound) return;
        StartCoroutine(PublishDesktopSound(++soundGeneration, hooked, videoSid));
    }

    IEnumerator PublishDesktopSound(int requested, Room origin, string videoSid)
    {
        SoundBusy = true;
        SoundStatus = "正在开启电脑声音共享…";
        Changed?.Invoke();
        LocalAudioTrack requestedTrack;
        PublishTrackInstruction instruction;
        try
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (SyntheticDesktopSoundForTest) desktopSoundSource = new CafeSyntheticAudioSource();
            else
#endif
                desktopSoundSource = new DesktopSoundSource();
            desktopSoundTrack = requestedTrack = LocalAudioTrack.CreateAudioTrack(
                DesktopSoundPrefix + videoSid, desktopSoundSource, origin);
            instruction = origin.LocalParticipant.PublishTrack(requestedTrack, new TrackPublishOptions {
                Source = TrackSource.SourceScreenshareAudio,
                AudioEncoding = new AudioEncoding { MaxBitrate = 64000 },
                Dtx = false
            });
        }
        catch (Exception error)
        {
            Debug.LogWarning("[NetCafe/DesktopSound] Init: " + error.GetType().Name);
            FailDesktopSound("无法初始化电脑声音共享");
            yield break;
        }

        yield return instruction;
        if (requested != soundGeneration)
        {
            if (!instruction.IsError) Unpublish(origin, requestedTrack);
            yield break;
        }
        if (instruction.IsError || !ShareRoomConnected || origin != hooked || !Sharing || CurrentWallTrack != videoSid)
        {
            if (!instruction.IsError) Unpublish(origin, requestedTrack);
            FailDesktopSound("电脑声音未开启，房间服务可能尚未支持电脑声音共享");
            yield break;
        }
        soundPublished = true;

        // The publisher has granted permission, and the server has accepted the track.
        // Only now begin capturing Windows playback.
        if (desktopSoundSource is DesktopSoundSource)
        {
            nativeAudioStarted = DesktopAudio.NC_DesktopAudioStart() == 1;
            float deadline = Time.unscaledTime + 5f;
            while (nativeAudioStarted && DesktopAudio.NC_DesktopAudioState() == 1 &&
                   Time.unscaledTime < deadline)
            {
                yield return null;
                if (requested != soundGeneration) yield break;
            }
            if (!nativeAudioStarted || DesktopAudio.NC_DesktopAudioState() != 2)
            {
                FailDesktopSound("无法捕获电脑声音，请检查 Windows 音频服务");
                yield break;
            }
        }
        desktopSoundSource.Start();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        nextTone = Time.unscaledTime;
#endif
        SoundSharing = true;
        SoundBusy = false;
        SoundStatus = "正在共享电脑播放的声音";
        Changed?.Invoke();
    }

    void FailDesktopSound(string status)
    {
        StopDesktopSound();
        SoundStatus = status;
        Changed?.Invoke();
    }

    void StopDesktopSound()
    {
        ++soundGeneration;
        if (nativeAudioStarted)
        {
            DesktopAudio.NC_DesktopAudioStop();
            nativeAudioStarted = false;
        }
        if (soundPublished) Unpublish(hooked, desktopSoundTrack);
        soundPublished = false;
        desktopSoundTrack = null;
        if (desktopSoundSource != null)
        {
            desktopSoundSource.Stop();
            desktopSoundSource.Dispose();
            desktopSoundSource = null;
        }
        SoundSharing = SoundBusy = false;
        SoundStatus = SoundOff;
    }

    void AddRemoteSound(RemoteAudioTrack trackValue, RemoteTrackPublication publication, string identity)
    {
        if (remoteSound.ContainsKey(trackValue.Sid) || identity == hooked?.LocalParticipant?.Identity ||
            !publication.Name.StartsWith(DesktopSoundPrefix, StringComparison.Ordinal) ||
            publication.Name.Length == DesktopSoundPrefix.Length) return;
        string videoSid = publication.Name.Substring(DesktopSoundPrefix.Length);
        // A replacement can arrive before the previous track's async unpublish.
        string replacedSid = null;
        foreach (var entry in remoteSound)
            if (entry.Value.Identity == identity && entry.Value.VideoSid == videoSid)
            { replacedSid = entry.Key; break; }
        if (replacedSid != null) RemoveRemoteSound(replacedSid);
        var holder = new GameObject("SharedDesktopSound");
        holder.transform.SetParent(transform, false);
        try
        {
            var speaker = holder.AddComponent<AudioSource>();
            speaker.playOnAwake = false;
            speaker.spatialBlend = 1f;
            speaker.rolloffMode = AudioRolloffMode.Linear;
            speaker.minDistance = 3f;
            speaker.maxDistance = 18f;
            speaker.dopplerLevel = 0f;
            speaker.mute = true;
            var stream = new AudioStream(trackValue, speaker);
            remoteSound.Add(trackValue.Sid, new RemoteSound {
                Identity = identity, VideoSid = videoSid,
                Publication = publication, Speaker = speaker, Stream = stream
            });
        }
        catch (Exception error)
        {
            Destroy(holder);
            Debug.LogWarning("[NetCafe/DesktopSound] Playback: " + error.GetType().Name);
        }
    }

    void RemoveRemoteSound(string sid)
    {
        if (!remoteSound.TryGetValue(sid, out var sound)) return;
        remoteSound.Remove(sid);
        sound.Speaker.Stop();
        sound.Stream.Dispose();
        Destroy(sound.Speaker.gameObject);
    }

    void UpdateDesktopSound()
    {
        if (SoundSharing && desktopSoundSource is DesktopSoundSource native)
        {
            if (DesktopAudio.NC_DesktopAudioState() == 2) native.Pump();
            else FailDesktopSound("电脑声音捕获已中断，可重新开启");
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        else if (SoundSharing && desktopSoundSource is CafeSyntheticAudioSource synthetic)
        {
            for (int i = 0; i < 8 && Time.unscaledTime >= nextTone; i++, nextTone += .01f)
                synthetic.Pump();
        }
#endif
        foreach (var sound in remoteSound.Values)
        {
            Transform target = null;
            if (ShareRoomConnected && !sound.Publication.Muted && (voice == null || voice.HearOthers) &&
                remote.TryGetValue(sound.VideoSid, out var video) && video.Identity == sound.Identity && video.HasFrame)
            {
                if (PublicWallHasFrame && sync.PublicScreenIdentity == sound.Identity &&
                    sync.PublicScreenTrack == sound.VideoSid)
                    target = cafe.publicScreen.transform;
                else if (video.Seat >= 0 && video.Quad != null && video.Quad.activeSelf)
                    target = cafe.screens[video.Seat].transform;
            }
            // Move one speaker between the station and the wall to avoid double playback.
            if (target != null) sound.Speaker.transform.position = target.position;
            sound.Speaker.mute = target == null;
        }
    }
}
#endif

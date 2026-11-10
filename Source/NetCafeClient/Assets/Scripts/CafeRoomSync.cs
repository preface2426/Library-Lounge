#if NETCAFE_B1
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using LiveKit;
using DataPacketKind = LiveKit.Proto.DataPacketKind;
using TrackKind = LiveKit.Proto.TrackKind;
using TrackSource = LiveKit.Proto.TrackSource;
using UnityEngine;

// Host-authoritative room state over the existing LiveKit data channel.
// Desktop pixels, window input, microphones and screen sharing never enter this protocol.
public sealed class CafeRoomSync : MonoBehaviour
{
    const string Topic = "netcafe.room.v1";
    const float Tick = .1f;
    const float SnapshotInterval = 3f;
    const int MaxBytes = 4096;

    [Serializable] public sealed class Actor
    {
        public string id, name, avatar;
        public long seq;
        public long seatRequest;
        public int seat = -1;
        public float x, y, z, yaw;
        public bool moving;
        public int shareStateVersion;
        public string sharedTrack;
        public int sharedSeat = -1;
        public string publicTrack;
        public long publicGrant;
        public int PublicSourceSeat => shareStateVersion == 0 ? seat :
            !string.IsNullOrEmpty(sharedTrack) && sharedTrack == publicTrack ? sharedSeat : -1;
    }
    [Serializable] sealed class WallSelection
    {
        public string owner, track;
        public long grant;
    }
    [Serializable] sealed class Message
    {
        public string kind;
        public long version;
        public Actor actor;
        public Actor[] actors;
        public WallSelection wall;
        public long wallRequest;
    }
    sealed class Peer
    {
        public GameObject root;
        public Animator animator;
        public TextMesh label;
        public string avatar;
        public Vector3 target;
        public Quaternion rotation;
        public bool moving;
        public bool lastMoving;
    }

    public Vector3 roomMinimum = new Vector3(-5.1f, -.2f, -5.5f);
    public Vector3 roomMaximum = new Vector3(5.1f, 2.5f, 5.5f);
    public Vector3 ClampRoomPosition(Vector3 position) => new Vector3(
        Mathf.Clamp(position.x, roomMinimum.x, roomMaximum.x),
        Mathf.Clamp(position.y, roomMinimum.y, roomMaximum.y),
        Mathf.Clamp(position.z, roomMinimum.z, roomMaximum.z));

    public CafePrototype cafe;
    public CafeAvatarLibrary library;
    public NetCafeNetworkSession network;
    readonly Dictionary<string, Actor> actors = new();
    readonly Dictionary<string, Peer> peers = new();
    readonly Dictionary<string, float> lastInbound = new();
    readonly Dictionary<string, (long sequence, float time)> wallInbound = new();
    Room connectedRoom;
    long localSeq, version, receivedVersion, lastSeatRequestSequence;
    float nextTick, nextSnapshot;
    int previousLocalSeat = -2;
    int pendingSeat = -1;
    long pendingRequest;
    float seatRequestDeadline;
    int DesiredSeat => pendingSeat >= 0 ? pendingSeat : cafe.CurrentSeat;
    string previousAvatar = "";
    bool hostedBefore;
    string myId = "";
    CafeDesktopSharing sharing;
    WallSelection wall;
    long wallRequest;
    float nextWallRequest;
    public string PublicScreenIdentity => wall?.owner ?? "";
    public string PublicScreenTrack => wall?.track ?? "";
    public bool HasApprovedWallSelection => CanSelectPublicScreen &&
        wall != null && ValidWallSource(wall.owner, wall.track, wall.grant);
    public bool CanSelectPublicScreen => CanRequestSeat &&
        (network.IsHost || (!string.IsNullOrEmpty(network.HostIdentity) &&
            connectedRoom.RemoteParticipants.ContainsKey(network.HostIdentity)));

    bool ValidWallSource(string id, string sid, long grant)
    {
        if (connectedRoom == null || string.IsNullOrEmpty(id) || id.Length > 96 ||
            string.IsNullOrEmpty(sid) || sid.Length > 128 || grant <= 0 ||
            !actors.TryGetValue(id, out var actor) || actor.publicTrack != sid ||
            actor.publicGrant != grant || !ComputerSeat(actor.PublicSourceSeat)) return false;
        if (id == myId)
        {
            if (sharing == null || sharing.ApprovedWallTrack != sid || sharing.PublicGrant != grant) return false;
        }
        return HasScreenTrack(id, sid);
    }
    bool ComputerSeat(int seat) => seat >= 0 && seat < cafe.screens.Length;
    bool HasScreenTrack(string id, string sid)
    {
        if (connectedRoom == null || string.IsNullOrEmpty(sid)) return false;
        Participant publisher = connectedRoom.LocalParticipant;
        if (id != myId)
        {
            if (!connectedRoom.RemoteParticipants.TryGetValue(id, out var remote)) return false;
            publisher = remote;
        }
        return publisher != null && publisher.Tracks.TryGetValue(sid, out var published) &&
            published.Source == TrackSource.SourceScreenshare && published.Kind == TrackKind.KindVideo &&
            !published.Muted;
    }
    public void GetPublicSources(List<Actor> output)
    {
        output.Clear();
        if (!CanSelectPublicScreen) return;
        foreach (var actor in actors.Values)
            if (ValidWallSource(actor.id, actor.publicTrack, actor.publicGrant)) output.Add(actor);
        output.Sort((a, b) => a.PublicSourceSeat.CompareTo(b.PublicSourceSeat));
    }
    public void NotifySharingChanged()
    {
        nextTick = 0f;
        // Host must see opt-in/out immediately; do not wait for unreliable
        // position ticks to happen to arrive while the sharing track changes.
        if (network == null || network.State != NetCafeNetworkSession.Phase.Connected) return;
        if (network.IsHost) HostLocal(false, true);
        else PublishLocal(true);
    }
    public bool RequestPublicScreen(Actor source)
    {
        if (!CanSelectPublicScreen) return false;
        if (source != null)
        {
            if (Time.unscaledTime < nextWallRequest ||
                !ValidWallSource(source.id, source.publicTrack, source.publicGrant)) return false;
            nextWallRequest = Time.unscaledTime + .25f;
        }
        var selected = source == null ? null : new WallSelection {
            owner = source.id, track = source.publicTrack, grant = source.publicGrant
        };
        if (network.IsHost) ApplyWallRequest(selected);
        else Send(new Message {kind = selected == null ? "wall-off" : "wall-select", wall = selected,
            wallRequest = ++wallRequest},
            true, network.HostIdentity);
        return true;
    }
    void ApplyWallRequest(WallSelection selected)
    {
        if (selected == null && wall == null) return;
        if (selected != null && !ValidWallSource(selected.owner, selected.track, selected.grant)) return;
        wall = selected;
        Snapshot();
    }
    bool EnsurePublicSelectionValid()
    {
        if (wall == null || HasApprovedWallSelection) return false;
        wall = null;
        return true;
    }
    // Positional audio follows the same interpolated avatar transform as the
    // room view. A missing/unconfirmed peer must remain silent.
    public Transform FindPeerRoot(string identity)
    {
        return !string.IsNullOrEmpty(identity) && peers.TryGetValue(identity, out var peer) && peer.root != null
            ? peer.root.transform : null;
    }
    public int RemoteSeatFor(string identity)
    {
        // Host-accepted station assignments only; unknown players cannot show video.
        return !string.IsNullOrEmpty(identity) && actors.TryGetValue(identity, out var actor)
            ? actor.seat : -1;
    }

    internal int RemoteScreenSeatFor(string id, string track)
    {
        if (!CanRequestSeat || !actors.TryGetValue(id, out var actor)) return -1;
        int seat = actor.shareStateVersion == 0 ? actor.seat :
            !string.IsNullOrEmpty(actor.sharedTrack) && actor.sharedTrack == track ? actor.sharedSeat : -1;
        if (!ComputerSeat(seat) || SeatTaken(seat, id) || !HasScreenTrack(id, track)) return -1;
        if (actor.seat == seat) return seat;
        // Once unoccupied, two former users may still have streams. Pick one
        // consistently on all clients without changing either wall permission.
        foreach (var other in actors.Values)
            if (other.sharedSeat == seat && string.CompareOrdinal(other.id, id) < 0 &&
                HasScreenTrack(other.id, other.sharedTrack)) return -1;
        return seat;
    }

    void OnEnable()
    {
        network = network != null ? network : GetComponent<NetCafeNetworkSession>();
        if (network != null) network.StateChanged += OnNetworkState;
        cafe = cafe != null ? cafe : GetComponent<CafePrototype>();
        sharing = GetComponent<CafeDesktopSharing>();
        if (cafe != null) cafe.Changed += LocalChanged;
    }
    void OnDisable()
    {
        if (network != null) network.StateChanged -= OnNetworkState;
        if (cafe != null) cafe.Changed -= LocalChanged;
        Reset();
    }
    void LocalChanged()
    {
        if (!CanRequestSeat) return;
        if (network.IsHost)
        {
            if (cafe.CurrentSeat < 0 && previousLocalSeat >= 0) HostLocal(false, true);
            return;
        }
        if (pendingSeat >= 0 && (cafe.Mode != CafePrototype.InputMode.Walking || cafe.QuitRequested))
        {
            CancelSeatRequest("");
            return;
        }
        if (DesiredSeat != previousLocalSeat)
        {
            PublishLocal();
            nextTick = Time.unscaledTime + Tick;
        }
    }
    bool CanRequestSeat => network != null && network.State == NetCafeNetworkSession.Phase.Connected &&
        connectedRoom != null && connectedRoom == network.TransportRoom && connectedRoom.IsConnected &&
        !string.IsNullOrEmpty(myId) && cafe != null;

    public void RequestSeat(int index)
    {
        if (pendingSeat >= 0 || cafe == null || cafe.CurrentSeat >= 0 || cafe.QuitRequested) return;
        if (!CanRequestSeat || cafe.Mode != CafePrototype.InputMode.Walking ||
            (!network.IsHost && string.IsNullOrEmpty(network.HostIdentity)))
        {
            cafe.SetSeatNotice("座位状态尚未同步，请稍后重试。");
            return;
        }
        if (!IsSeatAvailable(index))
        {
            cafe.SetSeatNotice("该座位已有人，请选择空位。");
            return;
        }
        if (network.IsHost)
        {
            cafe.EnterConfirmedSeat(index);
            HostLocal(false, true);
            return;
        }
        // The existing pose carries the desired seat. Do not acquire local
        // capture, camera or input until an authenticated host echoes its decision.
        pendingSeat = index;
        pendingRequest = localSeq + 1;
        lastSeatRequestSequence = pendingRequest;
        seatRequestDeadline = Time.unscaledTime + 5f;
        PublishLocal(true);
        cafe.SetSeatNotice("正在确认座位…");
    }
    internal void CancelPendingSeat() => CancelSeatRequest("");

    void CancelSeatRequest(string notice)
    {
        if (pendingSeat < 0) return;
        pendingSeat = -1;
        if (CanRequestSeat) PublishLocal(true);
        cafe.SetSeatNotice(notice);
    }
    void OnNetworkState(NetCafeNetworkSession.Phase state)
    {
        if (state == NetCafeNetworkSession.Phase.Connected) Hook();
        else
        {
            CancelSeatRequest("");
            if (connectedRoom != null) cafe?.RevokeSeat();
            if (network != null && !network.IsHost) wall = null;
            if (state != NetCafeNetworkSession.Phase.Reconnecting) Reset();
        }
    }
    void Hook()
    {
        if (network == null || network.TransportRoom == null || library == null || cafe == null) return;
        var current = network.TransportRoom;
        if (connectedRoom == current)
        {
            if (CanRequestSeat)
            {
                if (network.IsHost) HostLocal(false, true);
                else PublishLocal(true);
            }
            nextTick = 0;
            return;
        }
        CancelSeatRequest("");
        Reset();
        connectedRoom = current;
        if (current.LocalParticipant == null) return;
        myId = current.LocalParticipant.Identity;
        current.DataReceived += Received;
        current.ParticipantConnected += ParticipantArrived;
        current.ParticipantDisconnected += ParticipantLeft;
        hostedBefore = network.IsHost;
        nextTick = 0; nextSnapshot = 0;
        if (network.IsHost) HostLocal(true);
    }
    void Reset()
    {
        if (connectedRoom != null)
        {
            connectedRoom.DataReceived -= Received;
            connectedRoom.ParticipantConnected -= ParticipantArrived;
            connectedRoom.ParticipantDisconnected -= ParticipantLeft;
            connectedRoom = null;
        }
        foreach (var pair in peers)
            if (pair.Value.root != null) Destroy(pair.Value.root);
        peers.Clear(); actors.Clear(); lastInbound.Clear(); wallInbound.Clear();
        myId = "";
        wall = null; wallRequest = 0; nextWallRequest = 0;
        localSeq = 0; version = 0; receivedVersion = -1; lastSeatRequestSequence = 0;
        previousLocalSeat = -2; previousAvatar = ""; hostedBefore = false;
        pendingSeat = -1;
        pendingRequest = 0;
    }
    void Update()
    {
        if (network == null || network.State != NetCafeNetworkSession.Phase.Connected ||
            network.TransportRoom == null || cafe == null) return;
        if (connectedRoom != network.TransportRoom) Hook();
        if (connectedRoom == null || string.IsNullOrEmpty(myId)) return;
        if (pendingSeat >= 0 && Time.unscaledTime >= seatRequestDeadline)
            CancelSeatRequest("座位确认超时，请重试。");
        if (Time.unscaledTime >= nextTick)
        {
            nextTick = Time.unscaledTime + Tick;
            if (network.IsHost) HostLocal(false);
            else PublishLocal();
        }
        if (network.IsHost && Time.unscaledTime >= nextSnapshot)
        {
            nextSnapshot = Time.unscaledTime + SnapshotInterval;
            Snapshot();
        }
        foreach (var pair in peers)
        {
            Peer peer = pair.Value;
            if (peer.root == null) continue;
            float blend = Mathf.Clamp01(Time.deltaTime * 12f);
            peer.root.transform.position = Vector3.Lerp(peer.root.transform.position, peer.target, blend);
            peer.root.transform.rotation = Quaternion.Slerp(peer.root.transform.rotation, peer.rotation, blend);
            if (peer.animator != null && peer.moving != peer.lastMoving)
            {
                peer.animator.Play(peer.moving ? "Walk" : "Idle", 0, 0);
                peer.lastMoving = peer.moving;
            }
            if (peer.label != null && cafe.eyes != null)
            {
                var label = peer.label.transform;
                Vector3 towardsCamera = cafe.eyes.transform.position - label.position;
                if (towardsCamera.sqrMagnitude > .01f) label.rotation = Quaternion.LookRotation(-towardsCamera);
            }
        }
    }

    Actor Local()
    {
        // CafeDesktopSharing can be added by CafeEntryUi.Start() after this
        // component's OnEnable; resolve it lazily to avoid lost consent flags.
        if (sharing == null) sharing = GetComponent<CafeDesktopSharing>();
        Vector3 position = cafe.body != null ? cafe.body.transform.position : Vector3.zero;
        return new Actor {
            id = myId,
            name = connectedRoom != null && connectedRoom.LocalParticipant != null
                ? connectedRoom.LocalParticipant.Name : "Player",
            avatar = CafeAvatarLibrary.SelectedId,
            seq = ++localSeq,
            seatRequest = pendingSeat >= 0 ? pendingRequest : 0,
            seat = DesiredSeat,
            x = position.x, y = position.y, z = position.z,
            yaw = cafe.eyes != null ? cafe.eyes.transform.eulerAngles.y : 0f,
            moving = cafe.CurrentSeat < 0 && cafe.body != null && cafe.body.enabled &&
                     cafe.body.velocity.sqrMagnitude > .03f,
            shareStateVersion = 1,
            sharedTrack = sharing?.PublishedShareTrack,
            sharedSeat = sharing?.PublishedShareSeat ?? -1,
            publicTrack = sharing?.ApprovedWallTrack,
            publicGrant = sharing?.PublicGrant ?? 0
        };
    }
    void HostLocal(bool first, bool forceReliable = false)
    {
        Actor next = Local();
        if (next.seat >= 0 && SeatTaken(next.seat, myId))
        {
            previousLocalSeat = -1; // Stand raises Changed synchronously.
            cafe.RevokeSeat();
            next.seat = -1;
            forceReliable = true;
        }
        bool reliable = forceReliable || first || previousLocalSeat != next.seat || previousAvatar != next.avatar;
        previousLocalSeat = next.seat; previousAvatar = next.avatar;
        actors[myId] = next;
        reliable |= EnsurePublicSelectionValid();
        Broadcast(next, reliable);
    }
    void PublishLocal(bool forceReliable = false)
    {
        Actor next = Local();
        bool seatChanged = previousLocalSeat != next.seat;
        bool reliable = forceReliable || seatChanged || previousAvatar != next.avatar;
        if (seatChanged) lastSeatRequestSequence = next.seq;
        previousLocalSeat = next.seat; previousAvatar = next.avatar;
        Send(new Message {kind = "pose", actor = next}, reliable);
    }
    bool SeatTaken(int seat, string exceptId)
    {
        foreach (var kv in actors)
            if (kv.Key != exceptId && kv.Value.seat == seat) return true;
        return false;
    }
    public bool IsSeatAvailable(int seat)=>cafe!=null&&seat>=0&&seat<cafe.seats.Length&&!SeatTaken(seat,myId);
    void ParticipantArrived(Participant participant)
    {
        if (network != null && network.IsHost) Snapshot();
        else nextTick = 0; // Newly arrived peers must announce their own avatar.
    }
    void ParticipantLeft(Participant participant)
    {
        if (participant == null) return;
        string id = participant.Identity;
        actors.Remove(id);
        lastInbound.Remove(id);
        wallInbound.Remove(id);
        RemoveVisual(id);
        if (network != null && network.IsHost) Snapshot();
        else if (network != null && id == network.HostIdentity) wall = null;
    }
    void Received(byte[] bytes, Participant sender, DataPacketKind kind, string topic)
    {
        if (topic != Topic || sender == null || bytes == null || bytes.Length == 0 ||
            bytes.Length > MaxBytes || connectedRoom == null || network == null) return;
        Message message;
        try { message = JsonUtility.FromJson<Message>(Encoding.UTF8.GetString(bytes)); }
        catch (Exception) { return; }
        if (message == null) return;
        if (network.IsHost)
        {
            if (sender.Identity == myId ||
                !connectedRoom.RemoteParticipants.ContainsKey(sender.Identity)) return;
            float now = Time.unscaledTime;
            if (message.kind == "wall-off" || message.kind == "wall-select")
            {
                if (!CanSelectPublicScreen || kind != DataPacketKind.KindReliable || message.wallRequest <= 0) return;
                bool stop = message.kind == "wall-off";
                float lastSelection = float.NegativeInfinity;
                if (wallInbound.TryGetValue(sender.Identity, out var last))
                {
                    if (message.wallRequest <= last.sequence) return;
                    lastSelection = last.time;
                }
                bool tooFast = !stop && now - lastSelection < .2f;
                wallInbound[sender.Identity] = (message.wallRequest, stop || tooFast ? lastSelection : now);
                if (tooFast) return;
                if (stop) ApplyWallRequest(null);
                else if (message.wall != null) ApplyWallRequest(message.wall);
                return;
            }
            if (message.kind != "pose" || message.actor == null) return;
            if (lastInbound.TryGetValue(sender.Identity, out float at) && now - at < .045f) return;
            lastInbound[sender.Identity] = now;
            Actor incoming = message.actor;
            if (incoming.seq <= 0 || !float.IsFinite(incoming.x) ||
                !float.IsFinite(incoming.y) || !float.IsFinite(incoming.z) ||
                !float.IsFinite(incoming.yaw)) return;
            if (actors.TryGetValue(sender.Identity, out var old) && incoming.seq <= old.seq) return;
            int seat = incoming.seat >= -1 && incoming.seat < cafe.seats.Length ? incoming.seat : -1;
            // Periodic poses can retry delivery of a request, but not its outcome.
            if (incoming.seatRequest > 0 && old != null && old.seatRequest == incoming.seatRequest)
                seat = old.seat;
            if (seat >= 0 && SeatTaken(seat, sender.Identity)) seat = -1;
            int sharedSeat = -1;
            if (incoming.shareStateVersion > 0 && !string.IsNullOrEmpty(incoming.sharedTrack) && incoming.sharedTrack.Length <= 128 &&
                ComputerSeat(incoming.sharedSeat))
            {
                // Only a computer actually assigned to the authenticated sender
                // can seed an origin. Keep that origin for the lifetime of this track.
                if (old != null && old.sharedTrack == incoming.sharedTrack)
                    sharedSeat = old.sharedSeat;
                else if (seat == incoming.sharedSeat || old != null && old.seat == incoming.sharedSeat)
                    sharedSeat = incoming.sharedSeat;
            }
            Vector3 position = ClampRoomPosition(new Vector3(incoming.x, incoming.y, incoming.z));
            // Keep coordinates in the playable room, never trust claimed sender identity or display name.
            Actor accepted = new Actor {
                id = sender.Identity,
                name = string.IsNullOrEmpty(sender.Name) ? "Player" :
                    sender.Name.Substring(0, Mathf.Min(24, sender.Name.Length)),
                avatar = CafeAvatarLibrary.Valid(incoming.avatar),
                seq = incoming.seq,
                seatRequest = incoming.seatRequest,
                seat = seat,
                x = position.x,
                y = position.y,
                z = position.z,
                yaw = Mathf.Repeat(incoming.yaw, 360f), moving = seat < 0 && incoming.moving,
                shareStateVersion = incoming.shareStateVersion > 0 || (old != null && old.shareStateVersion > 0) ? 1 : 0,
                sharedSeat = sharedSeat,
                sharedTrack = sharedSeat >= 0 ? incoming.sharedTrack : null,
                publicTrack = incoming.publicTrack?.Length <= 128 ? incoming.publicTrack : null,
                publicGrant = incoming.publicGrant
            };
            bool changed = old == null || old.seat != seat || old.avatar != accepted.avatar;
            if (changed) Debug.Log("[NetCafe/B1] Seat authority: " + sender.Identity +
                " seat=" + seat + " avatar=" + accepted.avatar);
            actors[sender.Identity] = accepted;
            bool cleared = EnsurePublicSelectionValid();
            Broadcast(accepted, changed || cleared || kind == DataPacketKind.KindReliable);
        }
        else
        {
            // Host identity comes from the authenticated room-service status, not from a packet.
            if (string.IsNullOrEmpty(network.HostIdentity) ||
                sender.Identity != network.HostIdentity) return;
            if (message.version <= receivedVersion) return;
            if (message.kind == "snapshot" && message.actors != null)
            {
                receivedVersion = message.version;
                wall = message.wall;
                var present = new HashSet<string>();
                foreach (var actor in message.actors)
                {
                    if (!ValidActor(actor)) continue;
                    if (actor.id != myId && !connectedRoom.RemoteParticipants.ContainsKey(actor.id)) continue;
                    present.Add(actor.id); Apply(actor);
                }
                var stale = new List<string>();
                foreach (var id in actors.Keys)
                    if (!present.Contains(id)) stale.Add(id);
                foreach (string id in stale) { actors.Remove(id); RemoveVisual(id); }
            }
            else if (message.kind == "state" && ValidActor(message.actor))
            {
                if (message.actor.id != myId &&
                    !connectedRoom.RemoteParticipants.ContainsKey(message.actor.id)) return;
                receivedVersion = message.version;
                wall = message.wall;
                Apply(message.actor);
            }
        }
    }
    bool ValidActor(Actor actor)
    {
        return actor != null && !string.IsNullOrEmpty(actor.id) && actor.id.Length <= 96 &&
               actor.seat >= -1 && cafe!=null && actor.seat < cafe.seats.Length &&
               float.IsFinite(actor.x) && float.IsFinite(actor.y) &&
               float.IsFinite(actor.z) && float.IsFinite(actor.yaw);
    }
    void Apply(Actor actor)
    {
        actors[actor.id] = actor;
        if (actor.id == myId)
        {
            if (pendingSeat >= 0 && actor.seatRequest == pendingRequest && actor.seq >= pendingRequest)
            {
                if (actor.seat == pendingSeat && CanRequestSeat && !cafe.QuitRequested &&
                    cafe.Mode == CafePrototype.InputMode.Walking && Time.unscaledTime < seatRequestDeadline)
                {
                    pendingSeat = -1;
                    cafe.EnterConfirmedSeat(actor.seat);
                }
                else CancelSeatRequest(actor.seat < 0 ? "该座位已有人，请选择空位。" : "座位请求已取消，请重试。");
                return;
            }
            // A late response for an older request must not revoke a newer seat.
            if (pendingSeat < 0 && cafe.CurrentSeat >= 0 && actor.seat != cafe.CurrentSeat &&
                actor.seq >= lastSeatRequestSequence) cafe.RevokeSeat();
            return;
        }
        if (!peers.TryGetValue(actor.id, out var remote) ||
            remote.root == null || remote.avatar != CafeAvatarLibrary.Valid(actor.avatar))
        {
            RemoveVisual(actor.id);
            var go = new GameObject("RemotePlayer_" + actor.id);
            go.transform.SetParent(transform, false);
            GameObject appearance = library.Create(actor.avatar, go.transform);
            if (appearance == null) { Destroy(go); return; }
            var nameObject = new GameObject("Nickname");
            nameObject.transform.SetParent(go.transform, false);
            nameObject.transform.localPosition = Vector3.up * 1.3f;
            var label = nameObject.AddComponent<TextMesh>();
            label.text = actor.name ?? "Player";
            label.characterSize = .085f;
            label.fontSize = 36;
            label.anchor = TextAnchor.MiddleCenter;
            label.color = new Color(.23f, .17f, .12f);
            remote = new Peer {root = go, animator = appearance.GetComponentInChildren<Animator>(),
                label = label, avatar = CafeAvatarLibrary.Valid(actor.avatar)};
            peers[actor.id] = remote;
        }
        remote.label.text = actor.name ?? "Player";
        remote.moving = actor.seat < 0 && actor.moving;
        if (actor.seat >= 0 && actor.seat < cafe.seats.Length)
        {
            var mark = cafe.seats[actor.seat];
            var cinema=GetComponent<CafeCinemaView>();
            remote.target = cinema!=null&&cinema.ContainsSeat(actor.seat) ? cinema.AvatarPosition(actor.seat) :
                mark.position + Vector3.up * (actor.seat < cafe.screens.Length ? -.67f : -.32f);
            remote.rotation = mark.rotation;
        }
        else
        {
            remote.target = new Vector3(actor.x, actor.y, actor.z);
            remote.rotation = Quaternion.Euler(0, actor.yaw, 0);
        }
    }
    void RemoveVisual(string id)
    {
        if (!peers.TryGetValue(id, out var visual)) return;
        if (visual.root != null) Destroy(visual.root);
        peers.Remove(id);
    }
    void Broadcast(Actor actor, bool reliable)
    {
        version++;
        if (actor.id != myId) Apply(actor);
        Send(new Message {kind = "state", version = version, actor = actor,
            wall = wall}, reliable);
    }
    void Snapshot()
    {
        if (network == null || !network.IsHost || connectedRoom == null) return;
        version++;
        var states = new List<Actor>();
        foreach (var actor in actors.Values) states.Add(actor);
        EnsurePublicSelectionValid();
        Send(new Message {kind = "snapshot", version = version, actors = states.ToArray(),
            wall = wall}, true);
    }
    void Send(Message message, bool reliable, string recipient = null)
    {
        if (connectedRoom == null || !connectedRoom.IsConnected ||
            connectedRoom.LocalParticipant == null) return;
        byte[] data = Encoding.UTF8.GetBytes(JsonUtility.ToJson(message));
        if (data.Length > MaxBytes) return;
        try { StartCoroutine(SendOne(connectedRoom, data, reliable, recipient)); }
        catch (Exception e) { Debug.LogWarning("[NetCafe/B1] Data send: " + e.GetType().Name); }
    }
    IEnumerator SendOne(Room origin, byte[] data, bool reliable, string recipient)
    {
        if (connectedRoom != origin) yield break;
        var instruction = origin.LocalParticipant.PublishData(data,
            destination_identities: recipient == null ? null : new[] { recipient }, reliable: reliable, topic: Topic);
        yield return instruction;
        if (instruction.IsError) Debug.LogWarning("[NetCafe/B1] State packet send failed");
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public bool SeatRequestPendingForTest => pendingSeat >= 0;
    public int VisibleRemoteCount => peers.Count;
    public string AvatarForTest(string id) => actors.TryGetValue(id, out var actor) ? actor.avatar : "";
    public Vector3 PositionForTest(string id) => actors.TryGetValue(id, out var actor)
        ? new Vector3(actor.x, actor.y, actor.z) : new Vector3(float.NaN, 0, 0);
    public float YawForTest(string id) => actors.TryGetValue(id, out var actor)
        ? actor.yaw : float.NaN;
    public string SeatOwnerForTest(int seat)
    {
        foreach (var kv in actors)
            if (kv.Value.seat == seat) return kv.Key;
        return "";
    }
#endif
}
#endif

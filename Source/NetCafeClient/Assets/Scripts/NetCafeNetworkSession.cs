#if NETCAFE_B1
using System;
using System.Collections;
using LiveKit;
using DisconnectReason = LiveKit.Proto.DisconnectReason;
using UnityEngine;
using UnityEngine.Networking;

// Experimental B1 networking surface; does not own native desktop capture or input.
public sealed partial class NetCafeNetworkSession : MonoBehaviour
{
    public enum Phase { Idle, RequestingCredentials, Connecting, Connected, Reconnecting, Leaving, Disconnected, Failed }
    [SerializeField] string roomApiUrl = "http://127.0.0.1:8787";
    [SerializeField] string displayName = "Player";
    [SerializeField] string inviteCode = "";    // A command-line loopback port overrides saved settings for this game launch only.
    // Do not persist the temporary port: manual/HTTPS server selections are retained.
    string temporaryRoomServiceUrl;
    // A LAN client must not silently talk to its own localhost. The URL is
    // configurable, but bearer credentials are allowed over TLS only, except
    // trusted loopback-only local development.
    public string RoomServiceUrl => roomApiUrl;
    public bool SetRoomServiceUrl(string value, bool persist = true)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps &&
             !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) || uri.AbsolutePath != "/")
            return false;
        string next = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        bool sameAsTemporary = !string.IsNullOrEmpty(temporaryRoomServiceUrl) &&
            string.Equals(next, temporaryRoomServiceUrl, StringComparison.OrdinalIgnoreCase);
        roomApiUrl = next;
        if (persist && !sameAsTemporary)
        {
            temporaryRoomServiceUrl = null;
            PlayerPrefs.SetString("NetCafeRoomServiceUrl", roomApiUrl);
            PlayerPrefs.Save();
        }
        return true;
    }
    public Phase State { get; private set; } = Phase.Idle;
    public string Error { get; private set; } = "";
    public bool WaitingForRoomService { get; private set; }
    AdmissionTrace admission;
    public AdmissionStep CurrentAdmissionStep => admission?.Step ?? AdmissionStep.None;
    public string AdmissionDiagnostic => admission?.Format() ?? "";
    const string PublishedRoomService = "https://librarylounge-room-service.onrender.com";
#if UNITY_EDITOR || DEVELOPMENT_BUILD || NETCAFE_ACCEPTANCE
    // Only the fault-injection probe enables retry against its loopback fixture.
    internal bool AllowLoopbackReservationRetryForProbe { get; set; }
#endif
    UnityWebRequest warmupRequest;
    float nextWarmupAt;

    public void WarmPublicRoomService(string url)
    {
        if (!IsPublishedService(url) || warmupRequest != null || Time.realtimeSinceStartup < nextWarmupAt) return;
        nextWarmupAt = Time.realtimeSinceStartup + 30f;
        warmupRequest = UnityWebRequest.Get(PublishedRoomService + "/healthz");
        warmupRequest.timeout = RoomServiceProbeSeconds;
        warmupRequest.redirectLimit = 0;
        StartCoroutine(WarmHealth(warmupRequest));
    }

    IEnumerator WarmHealth(UnityWebRequest request)
    {
        try { yield return request.SendWebRequest(); }
        finally
        {
            if (ReferenceEquals(warmupRequest, request))
            {
                warmupRequest = null;
                request.Dispose();
            }
        }
    }

    static bool IsPublishedService(string url) =>
        string.Equals(url?.Trim().TrimEnd('/'), PublishedRoomService, StringComparison.OrdinalIgnoreCase);
    float serviceWakeStartedAt;
    public int RoomServiceWaitSeconds => WaitingForRoomService ?
        Mathf.Clamp(Mathf.FloorToInt(Time.realtimeSinceStartup - serviceWakeStartedAt), 0, 90) : 0;
    public string RoomId => session == null ? "" : session.roomId;
    public string InviteCode => session == null ? "" : session.inviteCode;
    public bool IsHost => session != null && session.role == "host";
    public Room TransportRoom => State == Phase.Connected || State == Phase.Reconnecting ? room : null;
    public string HostIdentity { get; private set; } = "";

    public int MemberCount => confirmedMembers > 0 ? confirmedMembers : room == null ? 0 : room.RemoteParticipants.Count + 1;
    public string MembersSummary { get; private set; } = "正在读取房间成员…";
    public sealed class MemberInfo
    {
        public readonly string Id, Name, State;
        public readonly bool IsLocal, IsHost;
        public MemberInfo(string id,string name,string state,bool local,bool host)
        { Id=id;Name=name;State=state;IsLocal=local;IsHost=host; }
    }
    public System.Collections.Generic.IReadOnlyList<MemberInfo> Members => memberRows;
    readonly System.Collections.Generic.List<MemberInfo> memberRows = new();
    public event Action MembersChanged;
    int confirmedMembers;
    bool fetchingMembers;
    int memberFetchToken;
    public void SetEntryFields(string name, string code) { displayName = name; inviteCode = code; }
    public void RefreshMembers()
    {
        if (fetchingMembers || session == null || State != Phase.Connected) return;
        StartCoroutine(FetchMembers(generation, ++memberFetchToken, session.roomId, session.sessionCredential));
    }
    IEnumerator FetchMembers(int id, int token, string roomId, string credential)
    {
        fetchingMembers = true;
        string result = null, failure = null;
        yield return Request(id, "GET", "/v1/rooms/" + roomId + "/status", null, credential,
            text => result = text, error => failure = error);
        // A previous room's response must never clear the current request or UI.
        if (id != generation || token != memberFetchToken || session == null || roomId != session.roomId)
            yield break;
        fetchingMembers = false;
        if (failure != null) MembersSummary = "读取成员失败，请稍后重试。";
        else if (!string.IsNullOrEmpty(result))
        {
            RoomStatusResponse status = null;
            try { status = JsonUtility.FromJson<RoomStatusResponse>(result); }
            catch (ArgumentException) { /* Bad server JSON is an error, not a hanging coroutine. */ }
            if (status != null)
            {
                confirmedMembers = status.memberCount;
                HostIdentity = status.hostId ?? "";
                memberRows.Clear();
                var summary = new System.Text.StringBuilder();
                if (status.members != null)
                    foreach (var member in status.members)
                    {
                        if (member == null) continue;
                        memberRows.Add(new MemberInfo(member.id ?? "",member.displayName ?? "",member.state ?? "",member.id == session.participantId,member.id == status.hostId));
                        summary.AppendLine((member.id == session.participantId ? "你 · " : "成员 · ") + (member.displayName ?? "").Replace("<", "‹").Replace(">", "›") +
                            (member.id == status.hostId ? "（房主）" : "") + " · " +
                            (member.state == "active" ? "在线" : member.state == "reconnecting" ? "重连中" : "连接中"));
                    }
                MembersSummary = summary.Length == 0 ? "暂无成员信息" : summary.ToString();
            }
            else MembersSummary = "成员数据无法解析，请稍后刷新。";
        }
        else MembersSummary = "成员数据为空，请稍后刷新。";
        MembersChanged?.Invoke();
    }
    public event Action<Phase> StateChanged;
    RoomSessionResponse session;
    Room room;
    readonly System.Collections.Generic.HashSet<Room> pendingConnections = new();
#if UNITY_EDITOR || DEVELOPMENT_BUILD || NETCAFE_ACCEPTANCE
    int releasedLateConnections;
#endif
    int generation;
    readonly System.Collections.Generic.HashSet<CafeLoopbackHttp> localRequests=new();
    readonly System.Collections.Generic.HashSet<UnityWebRequest> webRequests=new();
    void CancelRequests()
    {
        WaitingForRoomService = false;
        foreach(var request in localRequests)request.Dispose();
        localRequests.Clear();
        foreach(var request in webRequests) { request.Abort(); request.Dispose(); }
        webRequests.Clear();
    }
    Coroutine active;
    Coroutine reconnectWatch;
    bool intentional;
    float reconnectAt;

    // Local scene owner signals exit without depending on the LiveKit SDK.
    void OnEnable()
    {
        // Public release contains ONLY a non-secret URL; it supersedes stale
        // saved localhost settings. A developer CLI override below wins last.
        string publishedUrl = "";
        try
        {
            string path = System.IO.Path.Combine(Application.streamingAssetsPath, "room-service.url");
            if (System.IO.File.Exists(path)) publishedUrl = System.IO.File.ReadAllText(path).Trim();
        }
        catch (System.IO.IOException) { /* Bad/missing publish file: keep local fallback. */ }
        bool validPublished = false;
        if (Uri.TryCreate(publishedUrl, UriKind.Absolute, out var publicUri) &&
            publicUri.Scheme == Uri.UriSchemeHttps)
            validPublished = SetRoomServiceUrl(publishedUrl, false);
        if (!validPublished)
        {
            var configuredUrl = PlayerPrefs.GetString("NetCafeRoomServiceUrl", "");
            if (!string.IsNullOrEmpty(configuredUrl)) SetRoomServiceUrl(configuredUrl, false);
        }
        // Only the explicit local test launcher may override the published URL.
        // Limit it to TCP loopback and never store it in PlayerPrefs.
        foreach (string argument in Environment.GetCommandLineArgs())
        {
            const string prefix = "-netcafeLocalRoomPort=";
            if (!argument.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (!int.TryParse(argument.Substring(prefix.Length), out int port) ||
                port < 1024 || port > 65535 ||
                !SetRoomServiceUrl("http://127.0.0.1:" + port, false))
            {
                Debug.LogError("Invalid local room service port option");
                continue;
            }
            temporaryRoomServiceUrl = roomApiUrl;
            Debug.Log("NETCAFE_LOCAL_ROOM_API=" + roomApiUrl);
        }
        var cafe = GetComponent<CafePrototype>();
        if (cafe != null) cafe.BeforeQuit += RequestNetworkExit;
    }
    void OnDisable()
    {
        var cafe = GetComponent<CafePrototype>();
        if (cafe != null) cafe.BeforeQuit -= RequestNetworkExit;
        ++generation;
        CancelRequests();
        if (warmupRequest != null)
        {
            var request = warmupRequest;
            warmupRequest = null;
            request.Abort();
            request.Dispose();
        }
        ++memberFetchToken;
        fetchingMembers = false;
        intentional = true;
        if (active != null) { StopCoroutine(active); active = null; }
        StopReconnectWatch();
        ClearTransport();
    }
    bool RequestNetworkExit()
    {
        if (session == null) return false;
        Leave();
        return true;
    }
    void StopReconnectWatch()
    {
        if (reconnectWatch == null) return;
        StopCoroutine(reconnectWatch);
        reconnectWatch = null;
    }

    [Serializable] public class Credentials { public string url; public string token; public int expiresIn; }
    [Serializable] public class RoomSessionResponse {
        public string roomId, inviteCode, participantId, role, sessionCredential;
        public Credentials connection;
    }
    [Serializable] class CredentialsResponse { public Credentials connection; }
    [Serializable] class ErrorResponse { public string error; }
    [Serializable] class MemberRecord { public string id, displayName, state; }
    [Serializable] class RoomStatusResponse { public string hostId; public int memberCount; public MemberRecord[] members; }

    void SetState(Phase next, string error = "")
    {
        State = next; Error = error;
        StateChanged?.Invoke(next);
        Debug.Log("[NetCafe/B1] " + next + (string.IsNullOrEmpty(error) ? "" : " (" + error + ")"));
    }
    public void CreateRoom() => Begin(false);
    public void JoinRoom() => Begin(true);
    void Begin(bool join)
    {
        if (State != Phase.Idle && State != Phase.Disconnected && State != Phase.Failed) return;
        admission = null;
        if (join && string.IsNullOrWhiteSpace(inviteCode)) { SetState(Phase.Failed, "INVITE_REQUIRED"); return; }
        admission = new AdmissionTrace(join);
        // Prepare before even a local SDK connection can initialize its shared
        // HTTP TLS client. Local ws remains usable if no roots can be read.
        CafeLiveKitTlsRoots.Prepare();
        intentional = false;
        var id = ++generation;
        CancelRequests();
        StopReconnectWatch();
        ClearTransport();
        session = null;
        confirmedMembers = 0; memberRows.Clear();
        HostIdentity = "";
        ++memberFetchToken;
        fetchingMembers = false;
        MembersSummary = "正在读取房间成员…";
        SetState(Phase.RequestingCredentials);
        active = StartCoroutine(ConnectFlow(id, join));
    }
    IEnumerator Request(int id, string method, string path, string body, string credential,
        Action<string> success, Action<string> failure, int timeoutSeconds = 15,
        float deadline = float.PositiveInfinity, Action<string> unavailableBody = null)
    {
        if (id != generation) yield break;
        string url=roomApiUrl.TrimEnd('/')+path,response=null;long status=0;bool okay=false;
        if(CafeLoopbackHttp.CanHandle(url))
        {
            using(var direct=new CafeLoopbackHttp(url,method,body,credential,timeoutSeconds))
            {
                localRequests.Add(direct);
                try
                {
                while(id==generation && Time.realtimeSinceStartup < deadline && !direct.Pending.IsCompleted)yield return null;
                if(id!=generation)yield break;
                if (!direct.Pending.IsCompleted) direct.Dispose();
                else
                {
                    var result=direct.Pending.GetAwaiter().GetResult();response=result.Body;status=result.Status;okay=result.Success;
                }
                }
                finally { localRequests.Remove(direct); }
            }
        }
        else using (var req = new UnityWebRequest(url, method))
        {
            webRequests.Add(req);
            try
            {
            if (body != null) {
                byte[] payload = System.Text.Encoding.UTF8.GetBytes(body);
                req.uploadHandler = new UploadHandlerRaw(payload);
                req.SetRequestHeader("Content-Type", "application/json");
            }
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = timeoutSeconds;
            req.redirectLimit = 0;
            if (!string.IsNullOrEmpty(credential)) req.SetRequestHeader("Authorization", "Bearer " + credential);
            var operation = req.SendWebRequest();
            while (id == generation && Time.realtimeSinceStartup < deadline && !operation.isDone) yield return null;
            if (id != generation) yield break;
            if (!operation.isDone) req.Abort();
            else { okay=req.result==UnityWebRequest.Result.Success;response=req.downloadHandler?.text;status=req.responseCode; }
            }
            finally { webRequests.Remove(req); }
        }
            if (okay) success(response);
            else {
                if (status == 503) unavailableBody?.Invoke(response);
                string e = response;
                ErrorResponse parsed = null;
                if (!string.IsNullOrEmpty(e) && e.TrimStart().StartsWith("{", StringComparison.Ordinal))
                {
                    try { parsed = JsonUtility.FromJson<ErrorResponse>(e); }
                    catch (ArgumentException) { /* Invalid upstream payload; use HTTP status instead. */ }
                }
                string code = method == "GET" && (path == "/healthz" || path == "/readyz") && status > 0 ? "HTTP_" + status :
                    parsed != null && !string.IsNullOrEmpty(parsed.error) ? parsed.error :
                    status == 0 ? "NETWORK_UNREACHABLE" : "HTTP_" + status;
                failure(code);
            }
    }
    [Serializable] class HealthResponse { public bool ok, ready; }
    const int RoomServiceProbeSeconds = 12;
    static bool ReadProbe(string body, HealthResponse value)
    {
        if (string.IsNullOrEmpty(body) || !body.TrimStart().StartsWith("{", StringComparison.Ordinal))
            return false;
        try { JsonUtility.FromJsonOverwrite(body, value); return true; }
        catch (ArgumentException) { return false; }
    }

    static bool IsTransientAdmissionError(string error) => error is
        "NETWORK_UNREACHABLE" or "HTTP_408" or "HTTP_502" or "HTTP_503" or "HTTP_504";

    IEnumerator WaitForRoomService(int id, Action<string> failure)
    {
        // Render health only proves that the API answered; readiness must also
        // prove that this API can reach LiveKit before we reserve a room.
        serviceWakeStartedAt = Time.realtimeSinceStartup;
        WaitingForRoomService = true;
        float deadline = serviceWakeStartedAt + 90f;
        bool checkReadiness = false;
        try
        {
            while (id == generation && Time.realtimeSinceStartup < deadline)
            {
                string body = null, error = null;
                if (!checkReadiness)
                {
                    admission.Begin(AdmissionStep.Health);
                    yield return Request(id, "GET", "/healthz", null, null,
                        value => body = value, value => error = value, RoomServiceProbeSeconds, deadline);
                    if (id != generation) yield break;
                    var health = new HealthResponse();
                    checkReadiness = error == null && ReadProbe(body, health) && health.ok;
                    admission.End(checkReadiness ? null : error ?? "INVALID_HEALTH_REPLY");
                    if (error != null && error != "HTTP_429" && !IsTransientAdmissionError(error))
                    { failure(error); yield break; }
                }
                if (checkReadiness && Time.realtimeSinceStartup < deadline)
                {
                    body = null; error = null;
                    string unavailable = null;
                    admission.Begin(AdmissionStep.Readiness);
                    yield return Request(id, "GET", "/readyz", null, null,
                        value => body = value, value => error = value, RoomServiceProbeSeconds, deadline,
                        value => unavailable = value);
                    if (id != generation) yield break;
                    var ready = new HealthResponse();
                    bool readyNow = error == null && ReadProbe(body, ready) && ready.ready;
                    admission.End(readyNow ? null : error ?? "INVALID_READY_REPLY");
                    if (readyNow && Time.realtimeSinceStartup < deadline) yield break;
                    if (error != null && error != "HTTP_429" && !IsTransientAdmissionError(error))
                    { failure(error); yield break; }
                    // Only a structured "not ready" from our API proves it is
                    // still awake. Recheck health on proxy pages or lost replies.
                    var pending = new HealthResponse { ready = true };
                    checkReadiness = error == "HTTP_503" && ReadProbe(unavailable, pending) && !pending.ready;
                }
                float retryAt = Mathf.Min(deadline, Time.realtimeSinceStartup + 2f);
                while (id == generation && Time.realtimeSinceStartup < retryAt) yield return null;
            }
            if (id == generation)
                failure(CurrentAdmissionStep == AdmissionStep.Readiness ? "LIVEKIT_NOT_READY" : "SERVICE_WAKE_TIMEOUT");
        }
        finally { if (id == generation) WaitingForRoomService = false; }
    }

    void AdmissionFailed(string error)
    {
        admission?.Fail(error);
        SetState(Phase.Failed, error);
    }

    IEnumerator ConnectFlow(int id, bool join)
    {
        string response = null, failure = null;
        yield return WaitForRoomService(id, value => failure = value);
        if (id != generation) yield break;
        if (failure != null) { AdmissionFailed(failure); yield break; }

        var payload = new InviteRequest { displayName = displayName.Trim(), inviteCode = inviteCode.Trim(),
            requestId = Guid.NewGuid().ToString("N") };
        string body = JsonUtility.ToJson(payload), address = roomApiUrl;
        string path = join ? "/v1/rooms/join" : "/v1/rooms";
        // The published service deduplicates request IDs, including while the
        // first POST is still running. Do not assume that of custom servers.
        bool retryAllowed = IsPublishedService(address);
#if UNITY_EDITOR || DEVELOPMENT_BUILD || NETCAFE_ACCEPTANCE
        retryAllowed |= AllowLoopbackReservationRetryForProbe && CafeLoopbackHttp.CanHandle(address);
#endif
        for (int attempt = 0; ; attempt++)
        {
            admission.Begin(AdmissionStep.Reservation);
            response = null; failure = null;
            yield return Request(id, "POST", path, body, null,
                value => response = value, value => failure = value);
            if (id != generation) yield break;
            if (failure == null) break;
            admission.End(failure);
            if (attempt != 0 || !retryAllowed || !IsTransientAdmissionError(failure)) break;
            yield return new WaitForSecondsRealtime(1.5f);
            if (id != generation) yield break;
            if (!string.Equals(address, roomApiUrl, StringComparison.OrdinalIgnoreCase)) break;
        }
        if (failure != null)
        {
            AdmissionFailed(failure == "NETWORK_UNREACHABLE" ? "ROOM_API_NO_RESPONSE" : failure);
            yield break;
        }
        try { session = JsonUtility.FromJson<RoomSessionResponse>(response); }
        catch (ArgumentException) { session = null; }
        if (session == null || session.connection == null || string.IsNullOrEmpty(session.connection.token) ||
            string.IsNullOrEmpty(session.connection.url) || string.IsNullOrEmpty(session.roomId) ||
            string.IsNullOrEmpty(session.sessionCredential)) {
            yield return FailAdmission(id, "INVALID_SERVER_REPLY"); yield break;
        }
        admission.End(null);
        yield return ConnectTransport(id, session.connection);
    }
    [Serializable] class InviteRequest { public string displayName, inviteCode, requestId; }
    IEnumerator ConnectTransport(int id, Credentials creds)
    {
        if (id != generation) yield break;
        admission.Begin(AdmissionStep.LiveKit);
        if (Uri.TryCreate(creds.url, UriKind.Absolute, out var transportUri) &&
            transportUri.Scheme == "wss" && !CafeLiveKitTlsRoots.Prepare())
        {
            yield return FailAdmission(id, "WINDOWS_LOW_INTEGRITY");
            yield break;
        }
        SetState(Phase.Connecting);
        ClearTransport();
        room = new Room();
        room.Reconnecting += OnReconnecting;
        room.Reconnected += OnReconnected;
        room.DisconnectedWithReason += OnDisconnected;
        var current = room;
        var instruction = current.Connect(creds.url, creds.token, new RoomOptions());
        pendingConnections.Add(current);
        // The SDK cannot cancel a pending Connect. Keep a completion owner even
        // when this coroutine is stopped or its UI deadline expires.
        instruction.GetAwaiter().OnCompleted(() => {
            pendingConnections.Remove(current);
            if (id != generation || !ReferenceEquals(room, current))
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || NETCAFE_ACCEPTANCE
                if (current.IsConnected) ++releasedLateConnections;
#endif
                current.Disconnect();
                current.Dispose();
            }
        });
        float deadline = Time.realtimeSinceStartup + 35f;
        while (id == generation && !instruction.IsDone && Time.realtimeSinceStartup < deadline)
            yield return null;
        if (id != generation) yield break;
        if (!instruction.IsDone || instruction.IsError || !current.IsConnected) {
            yield return FailAdmission(id, "LIVEKIT_CONNECT_FAILED");
            yield break;
        }
        admission.End(null);
        admission.Begin(AdmissionStep.Confirm);
        // The room API must also observe our participant before we claim admission.
        string failure = null;
        yield return Request(id, "POST", "/v1/rooms/" + session.roomId + "/confirm", "{}",
            session.sessionCredential, _ => {}, e => failure = e);
        if (id != generation) yield break;
        if (failure != null || !current.IsConnected ||
            current.ConnectionState != LiveKit.Proto.ConnectionState.ConnConnected)
        {
            yield return FailAdmission(id, failure == "NETWORK_UNREACHABLE" ? "ROOM_CONFIRM_NO_RESPONSE" :
                failure ?? "LIVEKIT_DISCONNECTED_DURING_CONFIRM");
            yield break;
        }
        admission.End(null);
        admission = null;
        SetState(Phase.Connected);
        RefreshMembers();
    }
    IEnumerator FailAdmission(int id, string error)
    {
        admission?.End(error);
        // A failed LiveKit connection must release the reserved host/member
        // before retry becomes available. Otherwise a retry strands old rooms.
        ClearTransport();
        var previous = session;
        if (previous != null && !string.IsNullOrEmpty(previous.sessionCredential) &&
            System.Text.RegularExpressions.Regex.IsMatch(previous.roomId ?? "", "^[a-f0-9]{32}$"))
        {
            string cleanupError = null;
            yield return Request(id, "POST", "/v1/rooms/" + previous.roomId + "/leave", "{}",
                previous.sessionCredential, _ => {}, value => cleanupError = value, 5);
            if (cleanupError != null) Debug.LogWarning("[NetCafe/B1] Failed admission release was not acknowledged");
        }
        if (id == generation)
        {
            session = null;
            AdmissionFailed(error);
        }
    }
    void OnReconnecting(Room r)
    {
        if (intentional || session == null || r != room || State == Phase.Disconnected || State == Phase.Failed || State == Phase.Leaving) return;
        // Initial admission is still owned by ConnectTransport and /confirm.
        // SDK recovery must not bypass the server's confirmation.
        if (State == Phase.Connecting) return;
        if (State != Phase.Reconnecting) reconnectAt = Time.realtimeSinceStartup;
        SetState(Phase.Reconnecting);
        if (reconnectWatch == null) reconnectWatch = StartCoroutine(ReconnectDeadline(generation));
    }
    IEnumerator ReconnectDeadline(int id)
    {
        // SDK attempts its own reconnect. Do not leave the UI stuck forever.
        while (id == generation && State == Phase.Reconnecting &&
               Time.realtimeSinceStartup - reconnectAt < 30f)
            yield return null;
        reconnectWatch = null;
        if (id != generation || intentional || State != Phase.Reconnecting) yield break;
        SetState(Phase.Disconnected, "RECONNECT_TIMEOUT");
        ClearTransport();
    }
    void OnReconnected(Room r)
    {
        if (intentional || session == null || r != room || State != Phase.Reconnecting) return;
        StopReconnectWatch();
        SetState(Phase.Connected);
    }
    void OnDisconnected(Room r, DisconnectReason reason)
    {
        if (intentional || session == null || r != room) return;
        // ConnectTransport owns initial admission failure and its reservation.
        if (State == Phase.Connecting) return;
        StopReconnectWatch();
        // A terminal SDK disconnection must never be silently ignored, even inside grace.
        SetState(Phase.Disconnected, reason.ToString());
        StartCoroutine(CleanupDisconnectedRoom(generation, r));
    }
    IEnumerator CleanupDisconnectedRoom(int id, Room old)
    {
        // Unsubscribe/dispose after the SDK completes its disconnect callback.
        yield return null;
        if (id == generation && room == old) ClearTransport();
    }
    void ClearTransport()
    {
        if (room == null) return;
        var current = room;
        room = null;
        current.Reconnecting -= OnReconnecting;
        current.Reconnected -= OnReconnected;
        current.DisconnectedWithReason -= OnDisconnected;
        current.Disconnect();
        current.Dispose();
    }
    public void Leave()
    {
        if (State == Phase.Idle || State == Phase.Leaving) return;
        intentional = true;
        int id = ++generation;
        CancelRequests();
        if (active != null) { StopCoroutine(active); active = null; }
        StopReconnectWatch();
        SetState(Phase.Leaving);
        ClearTransport();
        var previous = session;
        session = null;
        confirmedMembers = 0; memberRows.Clear();
        HostIdentity = "";
        ++memberFetchToken;
        fetchingMembers = false;
        if (previous == null) { SetState(Phase.Disconnected); return; }
        active = StartCoroutine(LeaveFlow(id, previous));
    }
    IEnumerator LeaveFlow(int id, RoomSessionResponse old)
    {
        string error = null;
        yield return Request(id, "POST", "/v1/rooms/" + old.roomId + "/leave", "{}",
            old.sessionCredential, _ => {}, e => error = e);
        if (id == generation) {
            if (error != null) Debug.LogWarning("[NetCafe/B1] Leave acknowledgement: " + error);
            SetState(Phase.Disconnected);
        }
    }
    // Opt-in acceptance counts requests without enabling other development hooks.
#if UNITY_EDITOR || DEVELOPMENT_BUILD || NETCAFE_ACCEPTANCE
    public int PendingWebRequestsForSmoke => webRequests.Count;
    public int PendingConnectionsForSmoke => pendingConnections.Count;
    public bool HasTransportForSmoke => room != null;
    public int ReleasedLateConnectionsForSmoke => releasedLateConnections;
#endif
    // Development-build-only hooks for the B1 two-process transport acceptance test.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public void ConfigureForSmoke(string name, string code)
    {
        displayName = name;
        inviteCode = code;
        // Respect the launcher's explicit loopback port (e.g. 8788).
        // Rejoin smoke used to overwrite this with 8787 and could hit a
        // different local service, producing misleading HTTP_502 results.
        roomApiUrl = !string.IsNullOrEmpty(temporaryRoomServiceUrl)
            ? temporaryRoomServiceUrl : roomApiUrl.StartsWith("https://", StringComparison.Ordinal)
                ? roomApiUrl : "http://127.0.0.1:8787";
    }
    public int RemotePeersForSmoke => room == null ? 0 : room.RemoteParticipants.Count;
#endif
    void OnDestroy()
    {
        var pendingWarmup = warmupRequest;
        warmupRequest = null;
        if (pendingWarmup != null) { pendingWarmup.Abort(); pendingWarmup.Dispose(); }
        ++generation; CancelRequests(); intentional = true; StopReconnectWatch(); ClearTransport();
    }
    // Only show legacy controls when no active V2 home UI exists.
    void OnGUI()
    {
        var cafe = GetComponent<CafePrototype>();
        var entry = GetComponent<CafeEntryUi>();
        if (entry != null && entry.isActiveAndEnabled) return;
        if (cafe == null || cafe.Mode != CafePrototype.InputMode.Menu) return;
        GUILayout.BeginArea(new Rect(Mathf.Max(0, Screen.width - 310), 12, 300, 205), GUI.skin.box);
        GUILayout.Label("NETCAFE / B1 连接测试");
        GUILayout.Label("状态：" + State + (Error == "" ? "" : " / " + Error));
        if (State == Phase.Idle || State == Phase.Disconnected || State == Phase.Failed) {
            GUILayout.BeginHorizontal(); GUILayout.Label("昵称", GUILayout.Width(35));
            displayName = GUILayout.TextField(displayName, 24); GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(); GUILayout.Label("邀请码", GUILayout.Width(50));
            inviteCode = GUILayout.TextField(inviteCode, 40); GUILayout.EndHorizontal();
            if (GUILayout.Button("创建房间")) CreateRoom();
            if (GUILayout.Button("加入房间")) JoinRoom();
        } else {
            if (session != null) {
                GUILayout.Label("邀请码：" + session.inviteCode);
                GUILayout.Label("房间：" + session.roomId.Substring(0, Mathf.Min(8, session.roomId.Length)));
            }
            if (GUILayout.Button("退出房间")) Leave();
        }
        GUILayout.EndArea();
    }
}
#endif

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class CafePrototype : MonoBehaviour
{
    public CharacterController body;
    public Camera eyes;
    public Transform[] seats;
    public Transform[] exits;
    public Collider walkableFloor;
    public Transform[] seatedVisualRoots;
    public Light roomLight;
    public Renderer[] screens;
    public Renderer publicScreen;
    public Material idleScreen;
    public Material activeScreen;
    public Material sharedScreen;
    public int CurrentSeat { get; private set; } = -1;
    Renderer[] locallyOccluded = Array.Empty<Renderer>();
    bool[] previousRendererEnabled = Array.Empty<bool>();
    int locallyOccludedSeat = -1;
    public bool Sharing { get; private set; }
    public bool LampOn => roomLight!=null && roomLight.enabled;
    public int PublicSource { get; private set; } = -1;
    private Vector3 standingPosition;
    Vector3 spawnPosition;
    float spawnYaw;
    CafeAvatarVisual avatarVisual;
    internal float WalkingEyeHeight => avatarVisual != null ? avatarVisual.EyeHeight : 1.62f;
    private float yaw, pitch, vertical, roomFieldOfView = 72;
    bool thirdPerson;
    public bool ThirdPerson => thirdPerson && CurrentSeat < 0;
    public string ExitWarning { get; private set; } = "";
    public enum InputMode { Walking, Desktop, Observing, Menu }
    public InputMode Mode { get; private set; } = InputMode.Walking;
    InputMode resumeMode = InputMode.Walking;
    bool paused => Mode == InputMode.Menu;
    bool observing => Mode == InputMode.Observing;
    CafeDesktopInput desktopInput;
    CafeCinemaView cinema;
    public bool IsComputerSeat => CurrentSeat >= 0 && CurrentSeat < screens.Length;
    public bool IsCinemaSeat => cinema!=null&&cinema.Active;
    public bool CinemaFocused => IsCinemaSeat&&cinema.Focused;
    private CafeInteractable target;
    bool targetWasAvailable=true;
    public event Action Changed;
    // Subscribers may initiate a bounded network leave; true requests a short shutdown grace.
    public event Func<bool> BeforeQuit;
    public bool LookingAround { get; private set; }
    public bool HasInteractionTarget=>target!=null;
    public string InteractionHint => target!=null ?
        (target.action==CafeInteractable.Action.Seat||target.action==CafeInteractable.Action.Sofa)&&!SeatAvailable(target.seatIndex)?"座位已占用":"E  "+target.label : "看向坐垫，按 E 坐下";
    public bool SeatAvailable(int index)
    {
        if(index<0||index>=seats.Length)return false;
#if NETCAFE_B1
        var sync=GetComponent<CafeRoomSync>();
        if(sync!=null)return sync.IsSeatAvailable(index);
#endif
        return true;
    }
    Vector2 lookStart;
    CafeHud hud;
    private CafeDesktopCapture desktop;
    // Cache process-invariant test flags so no per-frame command-line arrays.
    static readonly string[] LaunchArgs = Environment.GetCommandLineArgs();
    static readonly HashSet<string> LaunchFlags = new HashSet<string>(LaunchArgs, StringComparer.Ordinal);
    static readonly bool LaunchAvatarSmoke = Array.Exists(LaunchArgs,
        a => a.StartsWith("-b1AvatarSmokeRole=", StringComparison.Ordinal));
    static bool HasLaunchFlag(string flag) => LaunchFlags.Contains(flag);
    private bool Smoke => HasLaunchFlag("-prototypeSmoke");
    private bool ViewSmoke => HasLaunchFlag("-viewSmoke");
    private bool A2Smoke => HasLaunchFlag("-a2Smoke");
    bool AvatarSmoke => LaunchAvatarSmoke;
    public bool SelfTesting => Smoke || ViewSmoke || AvatarSmoke ||
        HasLaunchFlag("-b2StationSmoke") ||
        HasLaunchFlag("-b2SyntheticHost") ||
        HasLaunchFlag("-b2SyntheticGuest") ||
        HasLaunchFlag("-b2WallHost") ||
        HasLaunchFlag("-b2WallGuest") ||
        HasLaunchFlag("-a1Smoke") || HasLaunchFlag("-desktopSmoke") || HasLaunchFlag("-a2Smoke") || HasLaunchFlag("-memorySmoke") || HasLaunchFlag("-demoFlowSmoke") || HasLaunchFlag("-demoSoak") || HasLaunchFlag("-floorSmoke") || HasLaunchFlag("-collisionSmoke") || HasLaunchFlag("-roomViewSmoke");
    public bool DesktopFocused => IsComputerSeat && (Mode == InputMode.Desktop || paused && resumeMode == InputMode.Desktop);
    public bool QuitRequested { get; private set; }

    void OnEnable()
    {
        var capture = GetComponent<CafeDesktopCapture>();
        var input = GetComponent<CafeDesktopInput>();
        if (capture != null) capture.CaptureFailed += OnCaptureFailed;
        if (input != null) input.ActionRequested += OnInputAction;
    }
    void OnCaptureFailed() { if (!QuitRequested) OpenMenu(); }
    void OnInputAction(CafeDesktopInput.RequestedAction action)
    {
        if (QuitRequested) return;
        switch (action)
        {
            case CafeDesktopInput.RequestedAction.Menu: OpenMenu(); break;
            case CafeDesktopInput.RequestedAction.Observe: Observe(); break;
            case CafeDesktopInput.RequestedAction.Stand: Stand(); break;
        }
    }
    void Start()
    {
        // Native desktop control deliberately activates a different Windows window.
        // Unity must keep polling its native escape/hotkey state in the background;
        // otherwise control appears frozen until the user presses Win/Alt+Tab.
        if (Application.platform == RuntimePlatform.WindowsPlayer)
            Application.runInBackground = true;
        QualitySettings.vSyncCount = 1;
        Application.targetFrameRate = -1;
        if (Application.platform == RuntimePlatform.WindowsPlayer && !Application.isBatchMode)
            StartCoroutine(FitWindowsFullscreen());
        roomFieldOfView = eyes.fieldOfView;
        spawnPosition = body.transform.position;
        yaw = spawnYaw = body.transform.eulerAngles.y;
        avatarVisual = body.GetComponent<CafeAvatarVisual>();
        desktop = GetComponent<CafeDesktopCapture>();
        desktopInput = GetComponent<CafeDesktopInput>();
        cinema = GetComponent<CafeCinemaView>();
        hud = GetComponent<CafeHud>();
        ApplyCursor();
        if (Smoke) StartCoroutine(SmokeTest());
        if (ViewSmoke) StartCoroutine(ViewSmokeTest());
#if NETCAFE_ACCEPTANCE
        if (HasLaunchFlag("-fullscreenFitCheck")) StartCoroutine(FullscreenFitCheck());
#endif
        if (HasLaunchFlag("-roomViewSmoke"))
            StartCoroutine(RoomViewSmokeTest());
        Changed?.Invoke();

    }

    IEnumerator FitWindowsFullscreen()
    {
        // Wait for the player window to have a display, then also handle mode changes after startup.
        yield return new WaitForEndOfFrame();
        MatchWindowDisplay();
        var interval = new WaitForSecondsRealtime(.75f);
        while (true)
        {
            yield return interval;
            if (Screen.fullScreenMode == FullScreenMode.FullScreenWindow)
                MatchWindowDisplay();
        }
    }

    static void MatchWindowDisplay()
    {
        DisplayInfo display = Screen.mainWindowDisplayInfo;
        if (display.width <= 0 || display.height <= 0) return;
        if (Screen.fullScreenMode != FullScreenMode.FullScreenWindow ||
            Screen.width != display.width || Screen.height != display.height)
            Screen.SetResolution(display.width, display.height, FullScreenMode.FullScreenWindow);
    }

    void Update()
    {
        if (QuitRequested || SelfTesting || !Application.isFocused || desktopInput != null && desktopInput.Controlling) return;
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;
        bool back = keyboard != null && keyboard.escapeKey.wasPressedThisFrame || Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame;
        if (back) {
            var entry = GetComponent<CafeEntryUi>();
            if (entry != null && entry.BlocksRoomMenu) entry.HandleBack();
            else if (paused && hud != null) hud.Back(); else OpenMenu();
            return;
        }
        if (paused) return;
        if (Mode == InputMode.Desktop) return;
        if (CurrentSeat < 0 && (keyboard != null && keyboard.vKey.wasPressedThisFrame ||
            Gamepad.current != null && Gamepad.current.rightStickButton.wasPressedThisFrame))
        {
            TogglePerspective();
            return;
        }
        if (keyboard == null || mouse == null) return;
        if (CurrentSeat >= 0 && keyboard.eKey.wasPressedThisFrame) { Stand(); return; }
        bool looking = observing && mouse.rightButton.isPressed && (LookingAround || desktopInput == null || !desktopInput.OverToolbar);
        if (looking != LookingAround)
        {
            LookingAround = looking;
            if (looking)
            {
                lookStart = mouse.position.ReadValue();
                if(IsCinemaSeat)
                {
                    yaw=eyes.transform.eulerAngles.y;pitch=Mathf.DeltaAngle(0,eyes.transform.eulerAngles.x);
                    cinema.BeginLookAround();
                }
            }
            ApplyCursor();
            if (!looking) mouse.WarpCursorPosition(lookStart);
            Changed?.Invoke();
        }
        if (CurrentSeat < 0 || LookingAround)
        {
            Vector2 delta = mouse.delta.ReadValue() * .10f;
            yaw += delta.x; pitch = Mathf.Clamp(pitch - delta.y, -65, 65);
            if (observing)
                yaw = seats[CurrentSeat].eulerAngles.y + Mathf.Clamp(Mathf.DeltaAngle(seats[CurrentSeat].eulerAngles.y, yaw), -110, 110);
            eyes.transform.rotation = Quaternion.Euler(pitch, yaw, 0);
        }
        if (CurrentSeat < 0)
        {
            Vector2 move = new((keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0),
                (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0));
            move = Vector2.ClampMagnitude(move, 1);
            Vector3 velocity = Quaternion.Euler(0, yaw, 0) * new Vector3(move.x, 0, move.y) * (keyboard.leftShiftKey.isPressed ? 4f : 2.7f);
            vertical = body.isGrounded ? -2 : Mathf.Max(vertical - 20 * Time.deltaTime, -30);
            velocity.y = vertical;
            body.Move(velocity * Time.deltaTime);
            ApplyWalkingCamera();
            FindTarget();
            if (keyboard.eKey.wasPressedThisFrame && target != null) Activate(target);
        }
        else target = null;
    }

    void Transition(InputMode next)
    {
        if (Mode == next) return;
        desktopInput?.EndControl();
        Mode = next; LookingAround = false;
        if (next == InputMode.Desktop) desktopInput?.DeferControlAcquisition();
        ApplySeatedCamera(); ApplyCursor(); Changed?.Invoke();
    }
    void SetObserving(bool value) { if (value) Observe(); else ResumeDesktop(); }
    public void Observe()
    {
        if (CurrentSeat < 0) return;
        yaw = seats[CurrentSeat].eulerAngles.y; pitch = 0;
        eyes.transform.rotation = Quaternion.Euler(0, yaw, 0);
        Transition(InputMode.Observing); desktopInput?.ResetToolbar();
    }
    public void ResumeDesktop()
    {
        if (!IsComputerSeat) return;
        desktopInput?.Retry(); Transition(InputMode.Desktop); desktopInput?.ResetToolbar();
    }
    public void OpenMenu()
    {
        if (!paused) resumeMode = Mode;
        Transition(InputMode.Menu);
    }
    public void Continue()
    {
        desktopInput?.Retry();
        Transition(CurrentSeat < 0 ? InputMode.Walking : resumeMode);
    }
    public void ToggleCinemaFocus()
    {
        if(!IsCinemaSeat)return;
        cinema.ToggleFocus();
        if(paused)Continue();
        Changed?.Invoke();
    }
    void LateUpdate()
    {
        // Run even with the menu open or the window unfocused.
        if (!QuitRequested && body.transform.position.y < spawnPosition.y - 2f)
            ReturnToRoomSpawn(paused);
        SyncOwnSeatVisual();
        if (CurrentSeat >= 0) ApplySeatedCamera();
        else ApplyWalkingCamera();
        if (desktopInput != null && desktop != null)
        {
            bool testWithoutInput = HasLaunchFlag("-restroomAcceptance") || HasLaunchFlag("-uiRevisionAcceptance") || HasLaunchFlag("-uiFixLobbyQuit") || HasLaunchFlag("-uiAcceptance") || HasLaunchFlag("-uiFixAcceptance") || HasLaunchFlag("-uiFixBaseline") || HasLaunchFlag("-uiFixQuit") || Smoke || ViewSmoke || HasLaunchFlag("-a1Smoke") || HasLaunchFlag("-demoFlowSmoke") || HasLaunchFlag("-demoSoak") || HasLaunchFlag("-floorSmoke") || HasLaunchFlag("-collisionSmoke") || HasLaunchFlag("-roomViewSmoke");
            desktopInput.UpdateControl(!QuitRequested && !testWithoutInput && Mode == InputMode.Desktop && desktop.Ready,
                desktop.MonitorIndex, desktop.DesktopRect);
        }
    }
    void SyncOwnSeatVisual()
    {
        // The first-person camera can sit within a Kenney recliner / sofa back.
        // Hide ONLY the local viewed seat mesh during desktop focus. Preserve its
        // solid collision proxies; other multiplayer clients keep seeing the sofa.
        bool configured=seatedVisualRoots!=null&&seatedVisualRoots.Length>0;
        int desired=configured ? DesktopFocused&&CurrentSeat>=0&&CurrentSeat<seatedVisualRoots.Length?CurrentSeat:-1 :
            DesktopFocused&&CurrentSeat>=2&&CurrentSeat<=5?CurrentSeat:-1;
        if (desired==locallyOccludedSeat) return;
        for(int i=0;i<locallyOccluded.Length;i++)
            if(locallyOccluded[i]) locallyOccluded[i].enabled=previousRendererEnabled[i];
        locallyOccluded=Array.Empty<Renderer>();previousRendererEnabled=Array.Empty<bool>();
        locallyOccludedSeat=desired;
        if(desired<0)return;
        string name=desired<4?"Reading recliner "+(desired-1):"Social sofa - sage";
        GameObject seat=configured?seatedVisualRoots[desired]?.gameObject:GameObject.Find(name);
        if(!seat) { Debug.LogWarning("Seat visual missing: "+name);return; }
        locallyOccluded=seat.GetComponentsInChildren<Renderer>(true);
        previousRendererEnabled=new bool[locallyOccluded.Length];
        for(int i=0;i<locallyOccluded.Length;i++)
        {
            previousRendererEnabled[i]=locallyOccluded[i].enabled;
            locallyOccluded[i].enabled=false;
        }
        Debug.Log("ROOMVIEW_OCCLUDING_LOCAL_SEAT_HIDDEN station="+desired+" meshRenderers="+locallyOccluded.Length);
    }

    public void TogglePerspective()
    {
        if (CurrentSeat >= 0) return; // The desktop always keeps its calibrated first-person camera.
        thirdPerson = !thirdPerson;
        ApplyWalkingCamera();
        Changed?.Invoke();
    }

    void ApplyWalkingCamera()
    {
        if (CurrentSeat >= 0 || eyes == null || body == null) return;
        if (!thirdPerson)
        {
            eyes.transform.position = body.transform.position + Vector3.up * WalkingEyeHeight;
            return;
        }
        Vector3 pivot = body.transform.position + Vector3.up * 1.62f;
        Vector3 backwards = -(eyes.transform.rotation * Vector3.forward);
        const float distance = 2.45f;
        float clear = distance;
        // Prevent a third-person camera clipping through walls, without hiding the own avatar.
        foreach (RaycastHit hit in Physics.SphereCastAll(pivot, .18f, backwards, distance,
                     ~(1 << 28), QueryTriggerInteraction.Ignore))
            if (!hit.collider.transform.IsChildOf(body.transform))
                clear = Mathf.Min(clear, hit.distance - .08f);
        eyes.transform.position = pivot + backwards * Mathf.Clamp(clear, .3f, distance);
    }

    public void ReturnFromDesktop() { OpenMenu(); }

    public void RequestQuit()
    {
        if (QuitRequested) return;
        QuitRequested = true; // Prevent LateUpdate from rearming the native input hook.
        desktopInput?.EndControl();
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        Debug.Log("QUIT_REQUESTED: input released");
        bool pending = false;
        if (BeforeQuit != null)
            foreach (Func<bool> handler in BeforeQuit.GetInvocationList())
                try { pending |= handler(); }
                catch (Exception e) { Debug.LogWarning("Quit cleanup failed: " + e.GetType().Name); }
        if (!SelfTesting)
        {
            if (pending) StartCoroutine(QuitAfterNetworkGrace());
            else Application.Quit();
        }
    }
    IEnumerator QuitAfterNetworkGrace()
    {
        // Keep OS input released; never let a network outage block local quit.
        yield return new WaitForSecondsRealtime(2f);
        Application.Quit();
    }

    void ApplySeatedCamera()
    {
        // The camera belongs to the walking capsule after Stand(). A menu
        // transition must never lower it to the capsule's feet / floor.
        if (CurrentSeat < 0)
        {
            ApplyWalkingCamera();
            eyes.fieldOfView = roomFieldOfView;
            return;
        }
        if(cinema!=null&&cinema.Apply(roomFieldOfView))return;
        if (!DesktopFocused || Mode==InputMode.Menu)
        {
            // Only an actual seated camera is centered on the seat marker.
            eyes.transform.localPosition = Vector3.zero;
            if(Mode==InputMode.Menu)eyes.transform.rotation=Quaternion.Euler(0,seats[CurrentSeat].eulerAngles.y,0);
            eyes.fieldOfView = roomFieldOfView;
            return;
        }
        Transform display = screens[CurrentSeat].transform;
        desktop?.FitSeat(CurrentSeat);
        if (desktop != null && desktop.DisplaySurface != null && desktop.DisplaySurface.parent == display)
            display = desktop.DisplaySurface;
        // Both laptop and desktop focused views face the exact display plane.
        // The older laptop-only camera looked obliquely from the seat and converted
        // the skewed quad to a rectangular uGUI RawImage, offsetting the bottom
        // Windows taskbar. A perpendicular view makes UV and bezel coincide.
        Transform face=screens[CurrentSeat].transform;
        Vector3 size=display.lossyScale;
        bool roomView=desktop==null || desktop.ShowBorder;
        float coverage=roomView ? RoomCoverage() : 1f;
        float viewHeight=Mathf.Max(size.y,size.x/Mathf.Max(.01f,eyes.aspect));
        // Preserve the room lens and the model's physical dimensions. Only
        // camera distance changes to frame the actual desktop image.
        eyes.fieldOfView=roomFieldOfView;
        float distance=viewHeight/(2f*Mathf.Tan(roomFieldOfView*.5f*Mathf.Deg2Rad)*coverage);
        // The 3D image and its real physical bezel must share one plane. This
        // camera only frames the actual monitor: it does not draw a 2D overlay.
        // Preserve a broad ring of the surrounding library while using a PC.
        eyes.transform.SetPositionAndRotation(display.position-face.forward*distance,
                                               Quaternion.LookRotation(face.forward,Vector3.up));
    }

    // Non-fullscreen is a real 3D display, framed to 90% of the camera view.
    // The remaining 10% (5% on each side) preserves spatial context and
    // keeps the physical screen/bezel in the same projection as mouse input.
    const float RoomViewMonitorCoverage = .90f;
    float RoomCoverage() => RoomViewMonitorCoverage;

    void FindTarget()
    {
        CafeInteractable next = null;
        // Interaction volumes are triggers: they must be raycastable without
        // physically blocking the player when walking between chairs/sofas.
        if (Physics.Raycast(eyes.transform.position, eyes.transform.forward, out var hit,
            3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
            next = hit.collider.GetComponentInParent<CafeInteractable>();
        bool available=next==null||(next.action!=CafeInteractable.Action.Seat&&next.action!=CafeInteractable.Action.Sofa)||SeatAvailable(next.seatIndex);
        if (next != target||available!=targetWasAvailable) { target = next;targetWasAvailable=available; Changed?.Invoke(); }
    }

    public void Activate(CafeInteractable item)
    {
        switch (item.action)
        {
            case CafeInteractable.Action.Seat:
            case CafeInteractable.Action.Sofa: Sit(item.seatIndex); break;
            case CafeInteractable.Action.Light: ToggleLamp(); break;
            case CafeInteractable.Action.Display: CyclePublicScreen(); break;
        }
    }

    public void Sit(int index)
    {
        if (index < 0 || index >= seats.Length || CurrentSeat >= 0 || QuitRequested) return;
        if(!SeatAvailable(index)){SetSeatNotice("该座位已有人，请选择空位。");return;}
#if NETCAFE_B1
        var network = GetComponent<NetCafeNetworkSession>();
        if (network != null && (network.State == NetCafeNetworkSession.Phase.Connected ||
            network.State == NetCafeNetworkSession.Phase.Reconnecting))
        {
            var sync = GetComponent<CafeRoomSync>();
            if (sync != null) sync.RequestSeat(index);
            else SetSeatNotice("座位状态尚未同步，请稍后重试。");
            return;
        }
#endif
        EnterConfirmedSeat(index);
    }

    internal void SetSeatNotice(string notice)
    {
        if (ExitWarning == notice) return;
        ExitWarning = notice;
        Changed?.Invoke();
    }

    internal void EnterConfirmedSeat(int index)
    {
        if (index < 0 || index >= seats.Length || CurrentSeat >= 0 || QuitRequested) return;
        Vector3 previousCameraPosition=eyes.transform.position;
        Quaternion previousCameraRotation=eyes.transform.rotation;
        float previousFov=eyes.fieldOfView;
        standingPosition = body.transform.position;
        body.enabled = false;
        CurrentSeat = index;
        ExitWarning = "";
        eyes.transform.SetParent(seats[index], false);
        eyes.transform.localPosition = Vector3.zero;
        eyes.transform.localRotation = Quaternion.identity;
        yaw = seats[index].eulerAngles.y; pitch = 0;
        if(IsCinemaSeat)
        {
            cinema.Enter(previousCameraPosition,previousCameraRotation,previousFov);
            var facing=Quaternion.LookRotation(publicScreen.transform.position-seats[index].position);
            yaw=facing.eulerAngles.y;pitch=Mathf.DeltaAngle(0,facing.eulerAngles.x);
        }
        if (index < screens.Length) screens[index].sharedMaterial = idleScreen;
        // Establish the input/camera mode BEFORE beginning native capture.
        // AutoStart may synchronously report failure and call OpenMenu().
        Mode = index < screens.Length ? InputMode.Desktop : InputMode.Observing;
        LookingAround = false;
        // At 90% coverage an expanded toolbar would cover the display bezel.
        // Dock it before the first seated frame, not after a two-second idle.
        if (index < screens.Length) desktopInput?.PrepareComputerSeat();
        // A2 integration smoke must exercise the same auto-projection as normal play.
        if (index < screens.Length && desktop != null && (!SelfTesting || A2Smoke)) desktop.AutoStart();
        if (index < screens.Length) desktopInput?.DockRoomViewToolbar();
        ApplySeatedCamera();
        SyncOwnSeatVisual(); // Hide occluding chair immediately, not next LateUpdate.
        ApplyCursor(); Changed?.Invoke();
    }

    public void Stand()
    {
        if (CurrentSeat < 0) return;
        int previous = CurrentSeat;
        if (!TryFindSafeExit(previous, out Vector3 exit))
        {
            ExitWarning = "附近没有安全的离座位置，请从菜单重试。";
            Debug.LogWarning("SAFE_SEAT_EXIT_BLOCKED: "+previous);
            Changed?.Invoke();
            return;
        }
        FinishStand(previous, exit);
    }

    internal void RevokeSeat()
    {
        if (CurrentSeat < 0) return;
        int previous = CurrentSeat;
        bool keepMenu = paused;
        // Authority loss must release input even if every normal exit is blocked.
        if (!TryFindSafeExit(previous, out Vector3 exit)) exit = standingPosition;
        FinishStand(previous, exit);
        if (keepMenu) OpenMenu();
    }

    public void ReturnToRoomSpawn() => ReturnToRoomSpawn(false);

    void ReturnToRoomSpawn(bool keepMenu)
    {
        if (QuitRequested) return;
        // A background recovery must not recapture the cursor from Windows.
        if (CurrentSeat >= 0) FinishStand(CurrentSeat, spawnPosition, spawnYaw, keepMenu, Application.isFocused);
        else
        {
            desktopInput?.EndControl();
            PlaceWalkingPlayer(spawnPosition, spawnYaw, keepMenu);
#if NETCAFE_B1
            GetComponent<CafeRoomSync>()?.CancelPendingSeat();
#endif
            if (Application.isFocused) ApplyCursor();
            Changed?.Invoke();
        }
    }

    void FinishStand(int previous, Vector3 exit, float? heading = null, bool keepMenu = false, bool refreshCursor = true)
    {
        cinema?.Leave();
        desktopInput?.EndControl();
        if (previous < screens.Length) screens[previous].sharedMaterial = Sharing ? sharedScreen : idleScreen;
        PlaceWalkingPlayer(exit, heading ?? seats[previous].eulerAngles.y, keepMenu);
        SyncOwnSeatVisual(); // Restore chair immediately on standing.
        if (refreshCursor) ApplyCursor();
        Changed?.Invoke();
    }

    void PlaceWalkingPlayer(Vector3 exit, float heading, bool keepMenu)
    {
        ExitWarning = "";
        body.enabled = false;
        body.transform.position = exit;
        eyes.transform.SetParent(body.transform, false);
        eyes.fieldOfView = roomFieldOfView;
        yaw = heading; pitch = vertical = 0;
        eyes.transform.rotation = Quaternion.Euler(0, yaw, 0);
        body.enabled = true;
        CurrentSeat = -1;
        Mode = keepMenu ? InputMode.Menu : InputMode.Walking;
        resumeMode = InputMode.Walking;
        LookingAround = false;
        target = null;
        ApplyWalkingCamera();
    }

    bool TryFindSafeExit(int seatIndex, out Vector3 result)
    {
        result = default;
        var floor = walkableFloor!=null?walkableFloor:GameObject.Find("Warm plank floor")?.GetComponent<Collider>();
        if (floor == null || !floor.enabled) return false;
        Transform seat = seats[seatIndex];
        Vector3 facing = Vector3.ProjectOnPlane(seat.forward, Vector3.up).normalized;
        Vector3 side = Vector3.Cross(Vector3.up, facing);
        Vector3 origin = seat.position;
        // Start in front of the seat; desk surfaces naturally force the two side choices.
        Vector3[] offsets = {
            facing * 1.05f,
            facing * .22f + side * 1.05f, facing * .22f - side * 1.05f,
            facing * .45f + side * 1.40f, facing * .45f - side * 1.40f,
            facing * .12f + side * 1.70f, facing * .12f - side * 1.70f,
            side * 1.45f, -side * 1.45f,
        };
        Physics.SyncTransforms();
        for (int i=0;i<offsets.Length;i++)
            // Do not teleport through a tabletop just because its far side is empty.
            if (ValidExit(origin + offsets[i] * .85f, floor, out _) &&
                ValidExit(origin + offsets[i], floor, out result))
            {
                Debug.Log("SAFE_SEAT_EXIT_PASS seat="+seatIndex+" choice="+(i==0?"front":"side")+" at="+result);
                return true;
            }
        if (ValidExit(exits[seatIndex].position, floor, out result))
        {
            Debug.Log("SAFE_SEAT_EXIT_PASS seat="+seatIndex+" choice=legacy-fallback at="+result);
            return true;
        }
        if (ValidExit(standingPosition, floor, out result))
        {
            Debug.Log("SAFE_SEAT_EXIT_PASS seat="+seatIndex+" choice=previous-safe-standing at="+result);
            return true;
        }
        return false;
    }

    bool ValidExit(Vector3 position, Collider floor, out Vector3 result)
    {
        result = default;
        if (!floor.Raycast(new Ray(new Vector3(position.x, floor.bounds.max.y + 2f, position.z),
                Vector3.down), out RaycastHit ground, 3f) || ground.normal.y < .9f) return false;
        Vector3 root = new Vector3(position.x, ground.point.y + .04f, position.z);
        float radius = body.radius + .04f;
        float halfSpan = Mathf.Max(0, body.height * .5f - radius);
        Vector3 mid = root + body.center;
        if (Physics.CheckCapsule(mid - Vector3.up * halfSpan, mid + Vector3.up * halfSpan,
                radius, ~(1 << 28), QueryTriggerInteraction.Ignore)) return false;
        result = root;
        return true;
    }

    public void ToggleSharing()
    {
        Sharing = !Sharing;
        for (int i = 0; i < screens.Length; i++) screens[i].sharedMaterial = CurrentSeat == i ? activeScreen : Sharing ? sharedScreen : idleScreen;
    }

    public void ToggleLamp() { roomLight.enabled = !roomLight.enabled; Changed?.Invoke(); }
    public void CyclePublicScreen()
    {
#if NETCAFE_B1
        var entry = GetComponent<CafeEntryUi>();
        if (entry != null && entry.InNetworkRoom)
        {
            entry.OpenPublicWall();
            return;
        }
#endif
        PublicSource = (PublicSource + 2) % 7 - 1;
        publicScreen.sharedMaterial = PublicSource < 0 ? idleScreen : sharedScreen;
        Changed?.Invoke();
    }

    void ApplyCursor()
    {
        // Home, create/join and in-room menus use the OS pointer for uGUI.
        // Walking and relaxing in the room use camera look, never an OS pointer.
        // Computer/observation mode uses the separate native/software pointer.
        bool menuPointer = Mode == InputMode.Menu;
        // Observation already draws the software cursor in Unity. Do not
        // make Windows draw a second pointer in exactly the same location.
        bool observingPointer = Mode == InputMode.Observing && !LookingAround &&
            (desktopInput == null || !desktopInput.DrawsPointer);
        bool desktopPointer = Mode == InputMode.Desktop &&
            (desktopInput == null || !desktopInput.DrawsPointer);
        bool free = !LookingAround && (menuPointer || CurrentSeat >= 0);
        Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
        // Observing uses the real UI pointer; desktop control uses the software
        // pointer while native input hides the real one. Never hide both.
        Cursor.visible = menuPointer || observingPointer || desktopPointer;
    }

    void OnApplicationFocus(bool focus)
    {
        // Alt+Tab from walking/relaxing must not silently open the room menu.
        // Release cursor confinement while Windows owns focus; restore camera look
        // on return. Native desktop input manages its own handoff separately.
        if (!focus)
        {
            // Foreground transfer to the user's genuine Windows application is
            // expected while native input owns the pointer. Do not fight its
            // hidden cursor / low-level hook when Unity reports focus loss.
            if (desktopInput == null || !desktopInput.Controlling)
            {
                Cursor.lockState = CursorLockMode.None;
                // While the software pointer is drawn, do not show the OS
                // pointer on focus transitions or both become visible.
                Cursor.visible = desktopInput == null || !desktopInput.DrawsPointer;
            }
        }
        else ApplyCursor();
        Changed?.Invoke();
    }
    void OnDisable()
    {
        for(int i=0;i<locallyOccluded.Length;i++)
            if(locallyOccluded[i]) locallyOccluded[i].enabled=previousRendererEnabled[i];
        locallyOccluded=Array.Empty<Renderer>();previousRendererEnabled=Array.Empty<bool>();locallyOccludedSeat=-1;
        var capture = GetComponent<CafeDesktopCapture>();
        var input = GetComponent<CafeDesktopInput>();
        if (capture != null) capture.CaptureFailed -= OnCaptureFailed;
        if (input != null) input.ActionRequested -= OnInputAction;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
    }
    IEnumerator RoomViewSmokeTest()
    {
        IEnumerator runner=RoomViewSmokeBody();
        Exception failure=null;
        while(true)
        {
            bool more=false;
            try { more=runner.MoveNext(); }
            catch(Exception ex) { failure=ex; }
            if(failure!=null || !more)break;
            yield return runner.Current;
        }
        (runner as IDisposable)?.Dispose();
        if(failure!=null) {Debug.LogException(failure);Debug.LogError("ROOMVIEW_BEZEL_SMOKE_FAIL: "+failure.Message);}
        Application.Quit(failure==null?0:1);
    }

    IEnumerator RoomViewSmokeBody()
    {
        yield return new WaitForSecondsRealtime(1.5f);
        bool previous=desktopInput.RoomView;
        try
        {
            if (!desktopInput.RoomView) { desktopInput.ToggleView(); yield return null; }
            desktopInput.CheckRoomToolbarDrag();
            for(int i=0;i<screens.Length;i++)
            {
                Sit(i);yield return null;
                if (!desktopInput.RoomView || !DesktopFocused)
                    throw new Exception("Room view not activated station="+i);
                if(hud.desktopRoot.activeSelf)
                    throw new Exception("ROOMVIEW_OCCLUSION_UI_FRONT_FAIL seat="+i);
                if(i==0)
                {
                    float lockedFov=eyes.fieldOfView;
                    desktopInput.CheckRoomToolbarDrag();
                    yield return null;
                    if(Mathf.Abs(eyes.fieldOfView-lockedFov)>.0005f)
                        throw new Exception("ROOMVIEW_TOOLBAR_DRAG_CHANGED_CAMERA_ZOOM old="+lockedFov+" new="+eyes.fieldOfView);
                    Debug.Log("ROOMVIEW_DRAG_CAMERA_ZOOM_LOCKED_PASS FOV="+lockedFov);
                }
                if(i>=2 && (locallyOccludedSeat!=i || locallyOccluded.Length==0 ||
                    locallyOccluded.Any(r=>r && r.enabled)))
                    throw new Exception("Local seat-back still blocks laptop screen station="+i);
                Rect uv = hud.desktopImage.uvRect;
                if(Mathf.Abs(uv.x)>.0001f || Mathf.Abs(uv.width-1f)>.0001f ||
                   Mathf.Abs(uv.height+1f)>.0001f || Mathf.Abs(uv.y-1f)>.0001f)
                    throw new Exception("Captured Windows desktop UV is clipped (taskbar missing) station="+i+" uv="+uv);
                if (Vector3.Dot(eyes.transform.forward,screens[i].transform.forward)<.999f)
                    throw new Exception("Focused display is skewed against desktop mouse rectangle station="+i);
                Rect desktopBounds=desktop.DesktopRect;
                Rect bar=desktopInput.ToolbarRect;
                if (desktopBounds.width<100 || desktopBounds.height<100 ||
                    desktopBounds.xMin<0 || desktopBounds.yMin<0 ||
                    desktopBounds.xMax>Screen.width || desktopBounds.yMax>Screen.height ||
                    desktopBounds.yMin < bar.yMax+3f)
                    throw new Exception("Room view hides Windows taskbar or bottom command bar station="+i+
                        " desktop="+desktopBounds+" bar="+bar);
                string rimName=i<2?"Desktop monitor bezel":"Laptop lid bezel";
                var monitorFrame=screens[i].transform.parent.GetComponent<CafeMonitorFrame>();
                Transform rim=monitorFrame!=null?monitorFrame.frame:screens[i].transform.Find(rimName);
                if(!rim || !rim.GetComponent<Renderer>().enabled)
                    throw new Exception("Missing hardware-specific bezel station="+i);
                Transform face=screens[i].transform;
                for(int x=0;x<=4;x++)for(int y=0;y<=4;y++)
                {
                    float u=x*.25f,v=y*.25f;
                    Vector3 pt=eyes.WorldToScreenPoint(face.TransformPoint(new Vector3(u-.5f,.5f-v,0)));
                    float mappedU=(pt.x-desktopBounds.xMin)/desktopBounds.width;
                    float mappedV=(Screen.height-pt.y-desktopBounds.yMin)/desktopBounds.height;
                    if(Mathf.Abs(mappedU-u)>.008f || Mathf.Abs(mappedV-v)>.008f)
                        throw new Exception("Room view mouse UV mismatch station="+i+" point="+u+","+v);
                }
                if(!hud.toolbarPanel.gameObject.activeInHierarchy || !desktopInput.ToolbarCollapsed)
                    throw new Exception("90% room view edge-tab missing / command bar obscures monitor station="+i);
                Debug.Log("ROOMVIEW_FULL_DESKTOP_PASS station="+i+" type="+(i<2?"desktop":"laptop")+
                    " rect="+desktopBounds+" bar="+bar+" UV25");
                if(i==0 || i==2)
                {
                    // Unlike the old UI-only test, sample the final rendered
                    // 3D monitor surface from the actual seated camera. Native
                    // Windows user content is never read by this simulation.
                    var fake=new Texture2D(48,48,TextureFormat.RGBA32,false);
                    for(int py=0;py<48;py++)for(int px=0;px<48;px++)
                        fake.SetPixel(px,py,py>=40?new Color(1f,0f,1f,1f):
                                           py<8?new Color(0f,1f,1f,1f):new Color(.04f,.16f,.30f,1f));
                    fake.Apply(false,false);
                    var overlayMaterial=new Material(desktop.desktopMaterial);
                    overlayMaterial.mainTexture=fake;
                    var quad=new GameObject("Temporary 3D desktop taskbar test");
                    var filter=quad.AddComponent<MeshFilter>();
                    var renderer=quad.AddComponent<MeshRenderer>();
                    var testMesh=new Mesh();
                    testMesh.vertices=new[]{new Vector3(-.5f,-.5f,0),new Vector3(.5f,-.5f,0),
                                            new Vector3(-.5f,.5f,0),new Vector3(.5f,.5f,0)};
                    testMesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one};
                    testMesh.triangles=new[]{0,2,1,2,3,1};testMesh.RecalculateBounds();
                    filter.sharedMesh=testMesh;renderer.sharedMaterial=overlayMaterial;
                    quad.transform.SetParent(face,false);
                    quad.transform.localPosition=new Vector3(0,0,-.53f);
                    quad.transform.localScale=Vector3.one;
                    yield return new WaitForEndOfFrame();
                    var shot=ScreenCapture.CaptureScreenshotAsTexture();
                    Vector3 bottom=eyes.WorldToScreenPoint(quad.transform.TransformPoint(new Vector3(-.2f,-.47f,0)));
                    Vector3 upper=eyes.WorldToScreenPoint(quad.transform.TransformPoint(new Vector3(-.2f,.47f,0)));
                    int probeX=Mathf.Clamp(Mathf.RoundToInt(bottom.x),0,shot.width-1);
                    int bottomY=Mathf.Clamp(Mathf.RoundToInt(bottom.y),0,shot.height-1);
                    int topY=Mathf.Clamp(Mathf.RoundToInt(upper.y),0,shot.height-1);
                    Color cBottom=shot.GetPixel(probeX,bottomY);
                    Color cTop=shot.GetPixel(probeX,topY);
                    string testDocs=System.IO.Path.Combine(System.IO.Path.GetFullPath(
                        System.IO.Path.Combine(Application.dataPath,"..","..","..")),"docs");
                    if(System.IO.Directory.Exists(testDocs))
                        System.IO.File.WriteAllBytes(System.IO.Path.Combine(testDocs,
                            "ROOMVIEW-SPATIAL-SCREEN-"+i+"-20261031.png"),shot.EncodeToPNG());
                    Debug.Log("ROOMVIEW_3D_TASKBAR_PROBE seat="+i+" bottom="+cBottom+" top="+cTop+
                        " quadBottom="+bottom+" projectedMonitor="+desktopBounds);
                    if(cBottom.r<.55f || cBottom.b<.55f || cBottom.g>.38f ||
                       cTop.g<.55f || cTop.b<.55f || cTop.r>.38f)
                        throw new Exception("Physical 3D screen top/bottom texture clipped by frame or inverted seat="+i);
                    // A2-RV physical 3D occlusion test: a *synthetic* green
                    // actor moves BETWEEN the camera and the 3D screen. The
                    // renderer must depth-test the avatar above its pixels,
                    // but still show the complete taskbar when unobstructed.
                    // Drive nested MoveNext ourselves so a depth assertion
                    // propagates to the outer smoke test and exits nonzero.
                    IEnumerator occlusionCheck = RoomViewOcclusionSmoke(i, face, quad, shot);
                    while (occlusionCheck.MoveNext()) yield return occlusionCheck.Current;
                    (occlusionCheck as IDisposable)?.Dispose();
                    UnityEngine.Object.Destroy(shot);UnityEngine.Object.Destroy(quad);
                    UnityEngine.Object.Destroy(testMesh);UnityEngine.Object.Destroy(overlayMaterial);
                    UnityEngine.Object.Destroy(fake);
                    Debug.Log("ROOMVIEW_3D_TASKBAR_VISIBLE_PASS station="+i);
                }

                Stand();
            }
            Sit(2);yield return new WaitForSecondsRealtime(2.4f);
            if(!desktopInput.ToolbarCollapsed || !hud.toolbarPanel.gameObject.activeInHierarchy)
                throw new Exception("90% room view should auto-dock the command toolbar to a visible slim edge tab after idle");
            desktopInput.ToggleView();yield return null;
            if(desktopInput.RoomView || desktop.DesktopRect.width < Screen.width*.7f)
                throw new Exception("Full screen desktop toggle failed");
            Debug.Log("ROOMVIEW_FULLSCREEN_TOGGLE_PASS: immersive desktop preserved");
            Stand();
            Debug.Log("ROOMVIEW_BEZEL_SMOKE_PASS: six spatial monitors, laptop/desktop bezel, 3D taskbar, free-drag toolbar");
        }
        finally { if(desktopInput.RoomView!=previous)desktopInput.ToggleView(); }
    }

    // Generates only solid synthetic pixels. Never captures, stores, or
    // transmits the real Windows desktop and never overrides mouse handling.
    IEnumerator RoomViewOcclusionSmoke(int seat, Transform face, GameObject testQuad, Texture2D beforeImage)
    {
        if (hud.desktopRoot.activeSelf)
            throw new Exception("ROOMVIEW_OCCLUSION_UI_FRONT_FAIL seat="+seat);
        Vector3 pt=eyes.WorldToScreenPoint(testQuad.transform.position);
        int x=Mathf.Clamp(Mathf.RoundToInt(pt.x),0,beforeImage.width-1);
        int y=Mathf.Clamp(Mathf.RoundToInt(pt.y),0,beforeImage.height-1);
        Color before=beforeImage.GetPixel(x,y);
        var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name="A2RV Synthetic Remote Avatar Occlusion Marker";
        cube.transform.position=face.position-face.forward*.15f;
        cube.transform.rotation=face.rotation;
        cube.transform.localScale=new Vector3(.28f,.28f,.08f);
        Destroy(cube.GetComponent<Collider>());
        var green=new Texture2D(4,4,TextureFormat.RGBA32,false);
        for(int j=0;j<4;j++)for(int k=0;k<4;k++)green.SetPixel(j,k,Color.green);
        green.Apply(false,false);
        var material=new Material(desktop.desktopMaterial);
        material.mainTexture=green;
        material.SetFloat("_FlipY",0f);
        cube.GetComponent<Renderer>().sharedMaterial=material;
        yield return new WaitForEndOfFrame();
        var afterImage=ScreenCapture.CaptureScreenshotAsTexture();
        Color after=afterImage.GetPixel(x,y);
        UnityEngine.Object.Destroy(afterImage);
        UnityEngine.Object.Destroy(cube);
        UnityEngine.Object.Destroy(material);
        UnityEngine.Object.Destroy(green);
        if (after.g < .5f || after.g < before.g+.18f || after.r > after.g*.7f)
            throw new Exception("A2RV screen/actor depth ordering wrong seat="+seat+
                " original="+before+" occluded="+after);
        Debug.Log("ROOMVIEW_DEPTH_OCCLUSION_PASS seat="+seat+" before="+before+" after="+after);
    }

#if NETCAFE_ACCEPTANCE
    IEnumerator FullscreenFitCheck()
    {
        yield return new WaitForSecondsRealtime(1.5f);
        var display = Screen.mainWindowDisplayInfo;
        var expected = new Vector2Int(display.width, display.height);
        Debug.Log($"FULLSCREEN_FIT_START {Screen.width}x{Screen.height} monitor={expected.x}x{expected.y} mode={Screen.fullScreenMode}");
        if (expected.x <= 1280 || expected.y <= 720 ||
            Screen.fullScreenMode != FullScreenMode.FullScreenWindow ||
            Screen.width != expected.x || Screen.height != expected.y)
        {
            Debug.LogError("FULLSCREEN_FIT_FAIL startup");
            Application.Quit(1);
            yield break;
        }
        Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
        yield return new WaitForSecondsRealtime(1.25f);
        Debug.Log($"FULLSCREEN_FIT_WINDOW {Screen.width}x{Screen.height} mode={Screen.fullScreenMode}");
        if (Screen.fullScreenMode != FullScreenMode.Windowed || Screen.width != 1280 || Screen.height != 720)
        {
            Debug.LogError("FULLSCREEN_FIT_FAIL window mode");
            Application.Quit(2);
            yield break;
        }
        Screen.SetResolution(1280, 720, FullScreenMode.FullScreenWindow);
        yield return new WaitForEndOfFrame();
        yield return null;
        Debug.Log($"FULLSCREEN_FIT_LOW {Screen.width}x{Screen.height} mode={Screen.fullScreenMode}");
        bool reachedLow = Screen.width == 1280 && Screen.height == 720 &&
            Screen.fullScreenMode == FullScreenMode.FullScreenWindow;
        yield return new WaitForSecondsRealtime(3f);
        Debug.Log($"FULLSCREEN_FIT_SETTLED {Screen.width}x{Screen.height} mode={Screen.fullScreenMode}");
        bool fitted = reachedLow && Screen.fullScreenMode == FullScreenMode.FullScreenWindow &&
            Screen.width == expected.x && Screen.height == expected.y;
        Debug.Log(fitted ? "FULLSCREEN_FIT_PASS" : "FULLSCREEN_FIT_FAIL did not recover after entering 720p fullscreen");
        Application.Quit(fitted ? 0 : 3);
    }
#endif

    IEnumerator ViewSmokeTest()
    {
        yield return new WaitForSecondsRealtime(2);
        try
        {
            if (!Application.isBatchMode && (Screen.fullScreenMode != FullScreenMode.FullScreenWindow ||
                Screen.width != Screen.currentResolution.width || Screen.height != Screen.currentResolution.height))
                throw new Exception("Native borderless fullscreen was not applied");
            for (int i = 0; i < screens.Length; i++)
            {
                Sit(i);
                Transform screen = screens[i].transform;
                // A pitched laptop is a projected trapezoid, not an axis-aligned
                // quad. Measuring only its two diagonal corners incorrectly
                // reports it as too small even when all four corners fit.
                float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
                float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
                bool allInFront = true;
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sy = -1; sy <= 1; sy += 2)
                    {
                        Vector3 p = eyes.WorldToViewportPoint(
                            screen.TransformPoint(new Vector3(.5f * sx, .5f * sy, 0)));
                        if (p.z <= 0) allInFront = false;
                        minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                        minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
                    }
                float actual = Mathf.Max(maxX - minX, maxY - minY);
                float expected = desktop.ShowBorder ? RoomCoverage() : 1f;
                if (!DesktopFocused || !allInFront ||
                    minX < -.001f || minY < -.001f || maxX > 1.001f || maxY > 1.001f ||
                    Mathf.Abs(actual - expected) > .005f)
                {
                    Debug.LogError($"VIEW_FIT_DIAGNOSTIC seat={i} projectedBounds=({minX:F4},{minY:F4})-({maxX:F4},{maxY:F4}) coverage={actual:F4} target={expected:F4} FOV={eyes.fieldOfView:F3} allInFront={allInFront}");
                    throw new Exception("Seated display fit failed " + i);
                }
                if (desktop.ShowBorder)
                {
                    Rect viewport = desktop.DesktopRect;
                    // The FULL Windows desktop, including its bottom taskbar,
                    // must fit the game viewport. The game command bar is now
                    // draggable and defaults to the TOP, not pinned bottom.
                    Rect toolbarRect=desktopInput.ToolbarRect;
                    bool toolbarAtTop=toolbarRect.yMax < Screen.height * .25f;
                    if (viewport.xMin < -1 || viewport.yMin < -1 ||
                        viewport.xMax > Screen.width + 1 || viewport.yMax > Screen.height + 1 ||
                        (toolbarAtTop && viewport.yMin < toolbarRect.yMax + 3f))
                        throw new Exception("Room-view desktop or taskbar clipped by viewport/toolbar seat="+i+" viewport="+viewport);
                    Transform rim = screen.Find("Desktop monitor bezel");
                    if (i >= 2) rim = screen.Find("Laptop lid bezel");
                    var standardFrame=screen.parent.GetComponent<CafeMonitorFrame>();
                    if(standardFrame!=null)rim=standardFrame.frame;
                    if (!rim || !rim.GetComponent<Renderer>())
                        throw new Exception("Model-specific screen bezel missing seat="+i);
                    Debug.Log("ROOMVIEW_BEZEL_VIEWPORT_PASS seat="+i+" type="+(i<2?"desktop":"laptop")+" viewport="+viewport);
                }
                if (i >= 2 && desktop.ShowBorder)
                {
                    Rect mapped=desktop.DesktopRect;
                    if (Vector3.Dot(eyes.transform.forward,screen.forward)<.9995f || mapped.width<100 || mapped.height<100)
                        throw new Exception("Laptop screen not facing camera / invalid input viewport "+i);
                    foreach(float u in new[]{0f,.25f,.5f,.75f,1f})
                        foreach(float v in new[]{0f,.25f,.5f,.75f,1f})
                        {
                            Vector3 p=eyes.WorldToScreenPoint(screen.TransformPoint(new Vector3(u-.5f,.5f-v,0)));
                            float mappedU=(p.x-mapped.xMin)/mapped.width;
                            float mappedV=(Screen.height-p.y-mapped.yMin)/mapped.height;
                            if (Mathf.Abs(mappedU-u)>.005f || Mathf.Abs(mappedV-v)>.005f)
                                throw new Exception($"Laptop pointer UV mismatch seat={i} expected=({u:F2},{v:F2}) actual=({mappedU:F3},{mappedV:F3})");
                        }
                    Debug.Log("LAPTOP_POINTER_GEOMETRY_PASS seat="+i+" viewport="+mapped);
                }
                Vector3 focusedPosition = eyes.transform.position;
                SetObserving(true);
                if (DesktopFocused || eyes.transform.localPosition.sqrMagnitude > .0001f || eyes.fieldOfView != roomFieldOfView)
                    throw new Exception("Observation view failed " + i);
                SetObserving(false);
                if ((eyes.transform.position - focusedPosition).sqrMagnitude > .0001f)
                    throw new Exception("Desktop focus restore failed " + i);
                Stand();
                if (eyes.fieldOfView != roomFieldOfView || DesktopFocused ||
                    Mathf.Abs(eyes.transform.localPosition.y - WalkingEyeHeight) > .001f)
                    throw new Exception("Standing view restore failed " + i);
                OpenMenu();
                if (eyes.transform.parent != body.transform ||
                    Mathf.Abs(eyes.transform.localPosition.y - WalkingEyeHeight) > .001f)
                    throw new Exception("Esc/menu dropped standing camera toward the floor at seat " + i);
                Continue();
                if (Mode != InputMode.Walking ||
                    Mathf.Abs(eyes.transform.localPosition.y - WalkingEyeHeight) > .001f)
                    throw new Exception("Esc/menu continue failed to restore walking camera " + i);
            }
            Debug.Log($"VIEW_SMOKE_PASS: {Screen.width}x{Screen.height} {Screen.fullScreenMode}; six seats, 90%/fullscreen fit, observe/return, stand");
            Application.Quit(0);
        }
        catch (Exception e) { Debug.LogException(e); Application.Quit(1); }
    }

    IEnumerator SmokeTest()
    {
        yield return null;
        try
        {
            if (seats.Length != exits.Length || seats.Length < 8 || screens.Length != 6) throw new Exception("Seat layout incorrect");
            Physics.SyncTransforms();
            Vector3 start = body.transform.position;
            // Test premise update 2026-11-02: the Cinema refit occupies the former fixed probe at z=-3.9.
            // Use the clear central aisle at z=0.3; movement distances and wall thresholds are unchanged.
            bool restroom=GetComponent<CafeRestroomLayout>()!=null;
            body.enabled = false; body.transform.position = new Vector3(0, .05f, restroom?-5.5f:.3f); body.enabled = true;
            body.Move(Vector3.right * .4f);
            if (body.transform.position.x < .3f) throw new Exception("Walking failed");
            body.Move(Vector3.right * 20);
            if(restroom)
            {
                var wall=GameObject.Find("RestroomRoot/Wall_East").GetComponent<Collider>();
                float expected=wall.bounds.min.x-body.radius-body.skinWidth;
                if(Mathf.Abs(body.transform.position.x-expected)>.1f)throw new Exception("Restroom wall collision failed: "+body.transform.position+" expected x="+expected);
            }
            else if(body.transform.position.x>5.15f||body.transform.position.x<4.8f)throw new Exception("Wall collision failed: "+body.transform.position);
            body.enabled = false; body.transform.position = start; body.enabled = true;
            for (int i = 0; i < seats.Length; i++)
            {
                Sit(i);
                if (CurrentSeat != i || body.enabled) throw new Exception("Sit failed " + i);
                Stand();
                if (CurrentSeat != -1 || !body.enabled) throw new Exception("Stand failed " + i);
            }
            ToggleSharing(); if (!Sharing) throw new Exception("Sharing on failed");
            Sit(0); Stand(); if (!Sharing) throw new Exception("Sharing did not persist");
            ToggleSharing(); if (Sharing) throw new Exception("Sharing off failed");
            bool light = LampOn; ToggleLamp(); if (LampOn == light) throw new Exception("Lamp failed"); ToggleLamp();
            for (int i = 0; i < 7; i++) CyclePublicScreen();
            if (PublicSource != -1) throw new Exception("Screen source cycle failed");
            Debug.Log("PROTOTYPE_SMOKE_PASS: movement, wall collision, "+seats.Length+" seats, stand, persistent share, lamp, screen cycle");
        }
        catch (Exception exception) { Debug.LogException(exception); Application.Quit(1); yield break; }
        body.enabled = false;
        body.transform.position = new Vector3(4.6f, .93f, -5.1f);
        eyes.transform.rotation = Quaternion.LookRotation(new Vector3(0, .5f, .2f) - eyes.transform.position);
        Mode = InputMode.Walking;
        for (int i = 0; i < 20; i++) yield return null;
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Evidence"));
        Directory.CreateDirectory(folder);
        if (!CaptureRoom(Path.Combine(folder, "room.png"))) { Application.Quit(1); yield break; }
        for (int i = 0; i < 60; i++) yield return null;
        eyes.transform.position = new Vector3(0, 3.05f, 0);
        eyes.transform.rotation = Quaternion.Euler(90, 0, 0);
        eyes.orthographic = true; eyes.orthographicSize = 5.7f;
        if (!CaptureRoom(Path.Combine(folder, "layout.png"))) { Application.Quit(1); yield break; }
        eyes.orthographic = false;
        // Dedicated visual evidence: cinema wall/screen and the six-PC workstation area.
        Vector3 cinemaTarget = publicScreen.transform.position;
        Vector3 cinemaPosition = cinemaTarget + publicScreen.transform.forward * 3.2f + Vector3.up * .45f;
        eyes.transform.position = cinemaPosition;
        eyes.transform.rotation = Quaternion.LookRotation(cinemaTarget - cinemaPosition);
        if (!CaptureRoom(Path.Combine(folder, "cinema-area.png"))) { Application.Quit(1); yield break; }
        eyes.transform.position = new Vector3(0f, 2.05f, .45f);
        eyes.transform.rotation = Quaternion.LookRotation(new Vector3(2.45f, 1.05f, 3.15f) - eyes.transform.position);
        if (!CaptureRoom(Path.Combine(folder, "computer-area.png"))) { Application.Quit(1); yield break; }
        Sit(0); Mode = InputMode.Desktop;
        for (int i = 0; i < 10; i++) yield return null;
        if (!CaptureRoom(Path.Combine(folder, "seated-view.png"))) { Application.Quit(1); yield break; }
        if (!Application.isBatchMode)
        {
            while (!UnityEngine.Rendering.SplashScreen.isFinished) yield return null;
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, "desktop-ui.png"));
            Stand();
            // Automated prototype smoke intentionally disables CafeEntryUi;
            // capture the in-room main menu owned by CafeHud instead.
            OpenMenu();
            yield return null;
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, "main-menu-ui.png"));
        }
        for (int i = 0; i < 60; i++) yield return null;
        Application.Quit(0);
    }

    bool CaptureRoom(string path)
    {
        var target = RenderTexture.GetTemporary(1280, 720, 24);
        var previous = RenderTexture.active;
        var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        try
        {
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(eyes,
                new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply();
            Color first = texture.GetPixel(0, 0);
            int different = 0;
            for (int y = 0; y < 720; y += 40)
                for (int x = 0; x < 1280; x += 40)
                    if (texture.GetPixel(x, y) != first) different++;
            if (different < 10) throw new Exception("Rendered view is blank: " + path);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Debug.Log("PROTOTYPE_RENDER_SAVED: " + path);
            return true;
        }
        catch (Exception exception) { Debug.LogException(exception); return false; }
        finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); Destroy(texture); }
    }
}

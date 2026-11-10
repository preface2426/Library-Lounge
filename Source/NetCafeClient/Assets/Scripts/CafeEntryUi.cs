using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// UI V2 presentation layer. Home/room session pages are NOT new desktop-input modes.
[DefaultExecutionOrder(200)]
public sealed partial class CafeEntryUi : MonoBehaviour
{
    enum Page { Lobby, Home, Create, Join, Busy, Invite, Members, ConfirmLeave, Settings, Help, Avatar, RoomControls, PublicWall, ScreenSharing }
    CafePrototype cafe;
    CafeHud hud;
#if NETCAFE_B1
    NetCafeNetworkSession network;
    CafeLocalHostRuntime localHost;
    CafeSpatialVoice voice;    CafeDesktopSharing sharing;
    bool creatingRoom;
    // A fresh room starts muted unless its owner explicitly opts in before join.
    bool microphoneOnEntry;
    bool pendingMicrophoneOnEntry;
    string serviceInput = "http://127.0.0.1:8787";
    int pendingPhase = -1;
    bool membersDirty;
    CafeRoomSync wallSync;
    readonly System.Collections.Generic.List<CafeRoomSync.Actor> wallSources = new();
    string wallUiState, requestedWallOwner, wallFeedback = "";
    string lastSharingBlock, lastSharingMonitor;
    float nextWallRefresh, wallRequestDeadline;
#endif
    Page page = Page.Home;
    Page avatarReturnPage = Page.Home;
    bool visible;
    bool fromRoom;
    bool enteredOnline;
    bool quitAfterLeave;
    string nickname = "玩家";
    string inviteInput = "";
    string message = "";
    Canvas overlayCanvas;
    // Screenshot must omit the pre-room and members overlay as well as the HUD.
    public GameObject ScreenshotOverlayRoot => overlayCanvas != null ? overlayCanvas.gameObject : null;
    RectTransform card, safeRoot, content;
    ScrollRect pageScroll;
    TMP_Text membersText, memberCountText;
    TMP_Text busyProgress;
    int lastBusyProgress = int.MinValue;
    Rect lastSafeArea;
    TMP_FontAsset bodyFont, titleFont;
    TMP_InputField nicknameField, inviteField;
    Button firstButton;
    readonly System.Collections.Generic.List<Selectable> focusable = new System.Collections.Generic.List<Selectable>();
    readonly System.Collections.Generic.List<Button> avatarButtons = new System.Collections.Generic.List<Button>();
    readonly System.Collections.Generic.List<string> avatarIds = new System.Collections.Generic.List<string>();
    readonly System.Collections.Generic.List<string> avatarNames = new System.Collections.Generic.List<string>();
    TextMeshProUGUI cornerStatus;
    float nextMemberRefresh;
    int lastWidth, lastHeight;
    float lastTextScale, lastCanvasScale;

    public bool Ready => isActiveAndEnabled && overlayCanvas != null;
    public bool BlocksRoomMenu => (visible || TransitionActive) && Ready;
    public bool InNetworkRoom
    {
        get
        {
#if NETCAFE_B1
            return network != null && (network.State == NetCafeNetworkSession.Phase.Connected ||
                network.State == NetCafeNetworkSession.Phase.Reconnecting);
#else
            return false;
#endif
        }
    }
    internal string ScreenShareMenuLabel
    {
        get
        {
#if NETCAFE_B1
            if (sharing != null && sharing.Sharing) return "屏幕共享 · 进行中";
            if (sharing != null && sharing.Busy) return "屏幕共享 · 启动中";
#endif
            return "屏幕共享";
        }
    }

    internal void OpenScreenSharing()
    {
        if (!Ready || visible || !roomEntered || !InNetworkRoom || TransitionActive) return;
        message = "";
        Show(Page.ScreenSharing, true);
    }

    void Start()
    {
        cafe = GetComponent<CafePrototype>();
        hud = GetComponent<CafeHud>();
        if (cafe == null || hud == null || IsAutomatedTest()) { enabled = false; return; }
        bodyFont = hud.menuSubtitle.font;
        titleFont = hud.menuTitle.font;
#if NETCAFE_B1
        network = GetComponent<NetCafeNetworkSession>();
        if (network != null)
        {
            serviceInput = network.RoomServiceUrl;
            localHost = gameObject.GetComponent<CafeLocalHostRuntime>();
            if (localHost == null) localHost = gameObject.AddComponent<CafeLocalHostRuntime>();
            network.StateChanged += OnConnectionState;
            network.MembersChanged += OnMembersChanged;
            voice = GetComponent<CafeSpatialVoice>();
            if (voice == null) voice = gameObject.AddComponent<CafeSpatialVoice>();
            voice.Changed += OnVoiceChanged;            sharing = GetComponent<CafeDesktopSharing>();
            if (sharing == null) sharing = gameObject.AddComponent<CafeDesktopSharing>();
            sharing.Changed += OnSharingChanged;
            wallSync = GetComponent<CafeRoomSync>();
        }
#endif
        BuildCanvas();
        BuildLounge();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-uiAcceptance") >= 0) gameObject.AddComponent<CafeUiAcceptance>();
#endif
        cafe.OpenMenu(); // Releases desktop control before opening any entry form.
        Show(Page.Home);
#if UNITY_EDITOR || DEVELOPMENT_BUILD || NETCAFE_ACCEPTANCE
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-menuScrollAcceptance") >= 0)
            StartCoroutine(VerifyMenuWheel());
#endif
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-hostAutostartSmoke") >= 0)
            StartCoroutine(HostAutostartSmoke());
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-hostAutostartCreateLeaveSmoke") >= 0)
            StartCoroutine(HostAutostartCreateLeaveSmoke());
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-wanCloudB1Smoke") >= 0)
            StartCoroutine(WanCloudB1Smoke());
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-b2SyntheticHost") >= 0 ||
            Array.IndexOf(Environment.GetCommandLineArgs(), "-b2SyntheticGuest") >= 0)
            gameObject.AddComponent<CafeB2SyntheticSmoke>();
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-b2WallHost") >= 0 ||
            Array.IndexOf(Environment.GetCommandLineArgs(), "-b2WallGuest") >= 0)
            gameObject.AddComponent<CafeB2WallConsentSmoke>();
#endif
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-demoFlowSmoke") >= 0)
            gameObject.AddComponent<CafeDemoFlowSmoke>();        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-demoSoak") >= 0)
            gameObject.AddComponent<CafeDemoSoak>();
    }

    static bool IsAutomatedTest()
    {        // Full B1 GUI acceptance specifically exercises the real home/create/join pages.
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-b1UiFlowSmoke") >= 0 ||
            Array.IndexOf(Environment.GetCommandLineArgs(), "-hostAutostartSmoke") >= 0 ||
            Array.IndexOf(Environment.GetCommandLineArgs(), "-hostAutostartCreateLeaveSmoke") >= 0 ||
            Array.IndexOf(Environment.GetCommandLineArgs(), "-wanCloudB1Smoke") >= 0 ||
            Array.IndexOf(Environment.GetCommandLineArgs(), "-b2SyntheticHost") >= 0 ||
            Array.IndexOf(Environment.GetCommandLineArgs(), "-b2SyntheticGuest") >= 0 ||
            Array.IndexOf(Environment.GetCommandLineArgs(), "-b2WallHost") >= 0 ||
            Array.IndexOf(Environment.GetCommandLineArgs(), "-b2WallGuest") >= 0)
            return false;
        foreach (string arg in Environment.GetCommandLineArgs())
            if (arg != "-demoFlowSmoke" && (arg.StartsWith("-b1AvatarSmokeRole=", StringComparison.Ordinal) ||
                arg.EndsWith("Smoke", StringComparison.OrdinalIgnoreCase) ||
                arg == "-prototypeSmoke" || arg == "-b1Stage1Smoke" || arg == "-memorySmoke"))
                return true;
        return false;
    }

#if NETCAFE_B1
    void OnConnectionState(NetCafeNetworkSession.Phase phase) { pendingPhase = (int)phase; }
    void OnMembersChanged() { membersDirty = true; }    void OnVoiceChanged()
    {
        if (visible && (page == Page.Invite || page == Page.Members || page == Page.RoomControls))
            Render(true);
    }

    void OnSharingChanged()
    {
        if (visible && page == Page.ScreenSharing) Render(true);
        else OnVoiceChanged();
    }
#endif

    void Update()
    {
#if NETCAFE_B1
        if (visible && page == Page.Busy) UpdateBusyProgress();
        if (pendingPhase >= 0)
        {
            int state = pendingPhase; pendingPhase = -1;
            ApplyPhase((NetCafeNetworkSession.Phase)state);
        }
        if (visible && page == Page.ScreenSharing && sharing != null &&
            (lastSharingBlock != sharing.StartBlockReason || lastSharingMonitor != sharing.CaptureMonitorLabel))
            Render(true);
        if (membersDirty)
        {
            membersDirty = false;
            UpdateMemberLabels();
        }
        if (visible && page == Page.PublicWall && Time.unscaledTime >= nextWallRefresh)
        {
            nextWallRefresh = Time.unscaledTime + .25f;
            if (requestedWallOwner != null)
            {
                if (wallSync.PublicScreenIdentity == requestedWallOwner)
                { requestedWallOwner = null; wallFeedback = ""; }
                else if (Time.unscaledTime >= wallRequestDeadline)
                { requestedWallOwner = null; wallFeedback = "放映未切换，请确认来源仍在共享并重试。"; }
            }
            if (wallUiState != ReadWallUiState()) Render(true);
        }
        if ((page == Page.Members || page == Page.Invite) && visible && network != null && Time.unscaledTime >= nextMemberRefresh)
        {
            nextMemberRefresh = Time.unscaledTime + 4f;
            network.RefreshMembers();
        }
        if (InNetworkRoom && cornerStatus != null && Time.unscaledTime >= nextMemberRefresh &&
            !visible)
        {
            nextMemberRefresh = Time.unscaledTime + 3f;
            network.RefreshMembers();
            UpdateLoungeIdentity();
        if(visible && page == Page.Members) UpdateLoungeMembers();
        }
#endif
        UpdateLoungeIdentity();
        if (TransitionActive) return;
        if (!visible) return;
        if (hud.menuRoot.activeSelf) hud.menuRoot.SetActive(false);
        if (hud.roomHud.activeSelf) hud.roomHud.SetActive(false);
        if (Screen.width != lastWidth || Screen.height != lastHeight || Screen.safeArea != lastSafeArea ||
            Mathf.Abs(lastTextScale - hud.TextScale) > .001f ||
            Mathf.Abs(lastCanvasScale - overlayCanvas.scaleFactor) > .001f) { Resize(); Render(true); }
        // CafePrototype handles escape routing to prevent double navigation.
        if (EventSystem.current != null && Keyboard.current != null &&
            Keyboard.current.tabKey.wasPressedThisFrame && focusable.Count > 0)
        {
            var selected = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject;
            int index = focusable.FindIndex(x => x.gameObject == selected);
            int delta = Keyboard.current.shiftKey.isPressed ? -1 : 1;
            int next = (index + delta + focusable.Count) % focusable.Count;
            EventSystem.current.SetSelectedGameObject(focusable[next].gameObject);
        }
    }

#if NETCAFE_B1
    void UpdateBusyProgress()
    {
        if (busyProgress == null) return;
        int progress = network != null && network.WaitingForRoomService ? network.RoomServiceWaitSeconds :
            network != null && network.State == NetCafeNetworkSession.Phase.Connecting ? -2 : -1;
        if (progress == lastBusyProgress) return;
        lastBusyProgress = progress;
        busyProgress.text = progress >= 0
            ? "正在等待房间服务…\n通常约 1 分钟 · 最长 90 秒\n已等待 " + progress + " 秒"
            : progress == -2 ? "正在建立实时连接…\n通常只需几秒" : "正在验证房间…\n通常只需几秒";
    }
    void ApplyPhase(NetCafeNetworkSession.Phase state)
    {
        if (state == NetCafeNetworkSession.Phase.Connected)
        {
            if (enteredOnline) return; // SDK reconnect must not close the user menu or re-enable desktop input.
            enteredOnline = true;
            message = "";
            // Both hosts and guests use identical explicit mic consent.
            if (pendingMicrophoneOnEntry) voice?.EnableMicrophone();
            pendingMicrophoneOnEntry = false;
            microphoneOnEntry = false;
            // The creator sees the invite code before entering the room.
            if (creatingRoom) Show(Page.Invite, true);
            else BeginRoomEntry();
            creatingRoom = false;
            return;
        }
        if (state == NetCafeNetworkSession.Phase.RequestingCredentials ||
            state == NetCafeNetworkSession.Phase.Connecting)
        {
            if (visible) { page = Page.Busy; Render(); }
            return;
        }
        if (state == NetCafeNetworkSession.Phase.Reconnecting)
        {
            if (cornerStatus != null) cornerStatus.text = "连接中断 · 正在重连";
            if (hud != null) hud.roomHint.text = "连接中断 · 正在重连";
            return;
        }
        if (state == NetCafeNetworkSession.Phase.Failed ||
            state == NetCafeNetworkSession.Phase.Disconnected)
        {
            if (roomTransition != null && !roomTransition.Leaving) roomTransition.Cancel();
            roomEntered = false;
            pendingMicrophoneOnEntry = false;
            microphoneOnEntry = false;
            localHost?.StopOwned();
            if (enteredOnline)
            {
                enteredOnline = false;
                if (cafe.CurrentSeat >= 0) cafe.Stand();
                cafe.OpenMenu();
                fromRoom = false;
                message = state == NetCafeNetworkSession.Phase.Disconnected ?
                    "连接已结束，可以重新创建或加入房间。" : TranslateError(network.Error);
                Show(Page.Home);
            }
            else if (visible && (state == NetCafeNetworkSession.Phase.Failed || page == Page.Busy))
            {
                message = state == NetCafeNetworkSession.Phase.Failed ?
                    TranslateError(network.Error) : "已取消连接。";
                Show(Page.Home);
            }
        }
    }
#endif

    void BuildCanvas()
    {
        var obj = new GameObject("EntryUiV2", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));
        obj.transform.SetParent(transform, false);
        overlayCanvas = obj.GetComponent<Canvas>();
        overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        overlayCanvas.sortingOrder = 180;
        var scale = obj.GetComponent<CanvasScaler>();
        scale.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scale.referenceResolution = new Vector2(1600, 900);
        scale.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scale.matchWidthOrHeight = .5f;
        var backdrop = Node("Backdrop", obj.transform);
        Stretch(backdrop, 0);
        backdrop.gameObject.AddComponent<CafePaperGraphic>();
        safeRoot = Node("SafeArea", backdrop);
        Stretch(safeRoot, 0);
        card = Node("Dialog", safeRoot);
        var img = card.gameObject.AddComponent<Image>();
        img.color = CafeUiTheme.Paper;
        var outline = card.gameObject.AddComponent<Outline>();
        outline.effectColor = CafeUiTheme.Line;
        outline.effectDistance = new Vector2(1, -1);

        // V2: a clipped scroll viewport prevents button overflow at 200% text scale.
        pageScroll = card.gameObject.AddComponent<ScrollRect>();
        var viewport = Node("Viewport", card);
        Stretch(viewport, 7);
        var viewportImage = viewport.gameObject.AddComponent<Image>();
        viewportImage.color = Color.white;
        viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        content = Node("Content", viewport);
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = Vector2.one;
        content.pivot = new Vector2(.5f, 1);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;
        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(30, 30, 22, 22);
        layout.spacing = 8;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
            ContentSizeFitter.FitMode.PreferredSize;
        pageScroll.viewport = viewport;
        pageScroll.content = content;
        CafeUiScroll.Configure(pageScroll);
        var indicator = Node("RoomIndicator", obj.transform);
        indicator.anchorMin = indicator.anchorMax = indicator.pivot = new Vector2(1, 1);
        indicator.anchoredPosition = new Vector2(-26, -22);
        indicator.sizeDelta = new Vector2(290, 38);
        cornerStatus = AddText(indicator, "", 17, CafeUiTheme.InkSoft, 38);
        cornerStatus.alignment = TextAlignmentOptions.Right;
        indicator.gameObject.SetActive(false);
        overlayCanvas.gameObject.SetActive(false);
    }

    static RectTransform Node(string name, Transform parent)
    {
        var obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        return obj.GetComponent<RectTransform>();
    }
    static void Stretch(RectTransform rt, float inset)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.one * inset; rt.offsetMax = -Vector2.one * inset;
    }
    static void Height(RectTransform rt, float height)
    {
        var element = rt.gameObject.AddComponent<LayoutElement>();
        element.minHeight = element.preferredHeight = height;
        element.flexibleHeight = 0;
    }
    TextMeshProUGUI AddText(Transform parent, string content, int size, Color color, float height, bool heading = false)
    {
        var rt = Node("Text", parent);
        Height(rt, height);
        var text = rt.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = heading ? titleFont : bodyFont;
        text.fontSize = size;
        text.color = color;
        text.text = content;
        text.alignment = TextAlignmentOptions.Left;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = TextOverflowModes.Truncate;
        text.raycastTarget = false;
        return text;
    }
    Button AddButton(string label, Action click, bool primary = false, Transform holder = null)
    {
        var rt = Node("Action_" + label, holder == null ? content : holder);
        Height(rt, 52);
        var image = rt.gameObject.AddComponent<Image>();
        image.color = primary ? CafeUiTheme.Accent : CafeUiTheme.PaperDeep;
        var focus = rt.gameObject.AddComponent<Outline>();
        focus.effectColor = CafeUiTheme.Focus;
        focus.effectDistance = new Vector2(3, -3);
        focus.enabled = false;
        var border = rt.gameObject.AddComponent<Outline>();
        border.effectColor = primary ? CafeUiTheme.Amber : CafeUiTheme.Line;
        border.effectDistance = new Vector2(1, -1);
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = image;
        var colors = btn.colors;
        colors.highlightedColor = CafeUiTheme.Hover;
        colors.pressedColor = CafeUiTheme.Pressed;
        colors.selectedColor = CafeUiTheme.Hover;
        colors.fadeDuration = .08f;
        btn.colors = colors;
        rt.gameObject.AddComponent<CafeUiFocus>();
        focusable.Add(btn);
        var text = AddText(rt, label, 20, CafeUiTheme.Ink, 52);
        Stretch(text.rectTransform, 0);
        text.font=titleFont; text.fontStyle = FontStyles.Normal;
        rt.gameObject.AddComponent<CafeLoungeButton>().Configure(primary);
        btn.onClick.AddListener(() => click());
        if (firstButton == null) firstButton = btn;
        return btn;
    }
    TMP_InputField AddInput(string title, string current, string placeholder, int max, Action<string> changed)
    {
        AddText(content, title, 16, CafeUiTheme.InkSoft, 28).alignment = TextAlignmentOptions.Left;
        var rt = Node("Input_" + title, content);
        Height(rt, 56);
        var background = rt.gameObject.AddComponent<Image>();
        background.color = CafeUiTheme.Paper;
        var focus = rt.gameObject.AddComponent<Outline>();
        focus.effectColor = CafeUiTheme.Focus;
        focus.effectDistance = new Vector2(3, -3); focus.enabled = false;
        var border = rt.gameObject.AddComponent<Outline>();
        border.effectColor = CafeUiTheme.Line;
        border.effectDistance = new Vector2(1, -1);
        var field = rt.gameObject.AddComponent<TMP_InputField>();
        rt.gameObject.AddComponent<CafeUiFocus>();
        field.targetGraphic = background;
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.characterLimit = max;
        var area = Node("TextArea", rt);
        Stretch(area, 12);
        area.offsetMin = new Vector2(16, 0);
        area.offsetMax = new Vector2(-16, 0);
        var value = AddText(area, "", 20, CafeUiTheme.Ink, 56);
        Stretch(value.rectTransform, 0);
        value.alignment = TextAlignmentOptions.Left;
        var hint = AddText(area, placeholder, 18, CafeUiTheme.InkSoft, 56);
        Stretch(hint.rectTransform, 0);
        hint.alignment = TextAlignmentOptions.Left;
        field.textViewport = area;
        field.textComponent = value;
        field.placeholder = hint;
        field.text = current;
        field.onValueChanged.AddListener(s => changed(s));
        focusable.Add(field);
        return field;
    }

    void Resize()
    {
        lastWidth = Screen.width; lastHeight = Screen.height;
        lastSafeArea = Screen.safeArea;
        var scale = overlayCanvas.GetComponent<CanvasScaler>();
        lastTextScale = hud.TextScale;
        scale.referenceResolution = new Vector2(1600, 900) / Mathf.Max(1, lastTextScale);
        float pixelScale = Mathf.Max(.1f, overlayCanvas.scaleFactor);
        lastCanvasScale = overlayCanvas.scaleFactor;
        float w = Screen.safeArea.width / pixelScale;
        float h = Screen.safeArea.height / pixelScale;
        if (Screen.width > 0 && Screen.height > 0)
        {
            safeRoot.anchorMin = new Vector2(Screen.safeArea.xMin / Screen.width, Screen.safeArea.yMin / Screen.height);
            safeRoot.anchorMax = new Vector2(Screen.safeArea.xMax / Screen.width, Screen.safeArea.yMax / Screen.height);
        }
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(.5f, .5f);
        card.anchoredPosition = Vector2.zero;
        card.sizeDelta = new Vector2(Mathf.Clamp(w - 24f, 1f, 610f),
            Mathf.Clamp(h - 24f, 1f, 650f));
        LoungeResize(w, h);
    }

    void Show(Page next, bool roomModal = false)
    {
#if NETCAFE_B1
        // Returning to Home starts a fresh, muted joining choice. Never retain
        // a previous room's opt-in across a new room or cancel action.
        if (next == Page.Home && !InNetworkRoom)
        {
            microphoneOnEntry = false;
            pendingMicrophoneOnEntry = false;
        }
#endif
        page = next;
        fromRoom = roomModal;
        visible = true;
        overlayCanvas.gameObject.SetActive(true);
        Resize();
        Render();
#if NETCAFE_B1
        if (next == Page.Create && CafeLocalHostRuntime.IsLoopbackUrl(serviceInput))
            BeginAutoLocalHost();
        else if (next == Page.Home || next == Page.Lobby || next == Page.Join)
        {
            // Do not kill the room service until the async /leave completes.
            if (network != null && network.State != NetCafeNetworkSession.Phase.Leaving &&
                network.State != NetCafeNetworkSession.Phase.Connected)
                localHost?.StopOwned();
        }
#endif
        if (hud != null && hud.menuRoot != null) hud.menuRoot.SetActive(false);
    }
#if NETCAFE_B1
    void BeginAutoLocalHost()
    {
        if (localHost == null || localHost.Ready || localHost.Starting) return;
        if (!localHost.HostToolsAvailable)
        {
            message = "此客户端没有本机房主组件。如已部署服务，请填写统一 HTTPS 地址。";
            Render(); return;
        }
        message = "";
        StartCoroutine(localHost.EnsureReady((url, error) =>
        {
            if (this == null || !visible || page != Page.Create) return;
            if (error == null)
            {
                serviceInput = url;
                network.SetRoomServiceUrl(url, false);
                message = "本机房间服务已自动启动并通过健康检查（仅本机测试）。";
            }
            else message = error;
            Render(true);
        }));
    }
#endif
    void Hide()
    {
        visible = false;
        overlayCanvas.gameObject.SetActive(false);
        if (hud != null) hud.Refresh();
    }

    void Render(bool preserveScroll = false)
    {
        if (card == null || !visible) return;
        float oldScroll = preserveScroll ? pageScroll.verticalNormalizedPosition : 1f;
        int oldFocusIndex = preserveScroll && EventSystem.current != null ?
            focusable.FindIndex(x => x != null && x.gameObject == EventSystem.current.currentSelectedGameObject) : -1;
        string oldShareFocus = page == Page.ScreenSharing && oldFocusIndex >= 0 ? focusable[oldFocusIndex].name : null;
        // Destroy is deferred; hide old buttons immediately to avoid overlap and raycast theft.
        for (int i = content.childCount - 1; i >= 0; i--)
        {
            var old = content.GetChild(i).gameObject;
            old.SetActive(false);
            Destroy(old);
        }
        firstButton = null;
        focusable.Clear();
        avatarButtons.Clear(); avatarIds.Clear(); avatarNames.Clear();
        membersText = null; memberCountText = null;
        busyProgress = null; lastBusyProgress = int.MinValue;
        nicknameField = null; inviteField = null;
        string title = page switch
        {
            Page.Home => "客厅网吧", Page.Create => "创建房间", Page.Join => "加入房间",
            Page.Busy => "正在连接", Page.Invite => "邀请朋友", Page.Members => "房间成员",
            Page.ConfirmLeave => "离开房间", Page.Settings => "偏好设置",
            Page.RoomControls => "语音与屏幕", Page.PublicWall => "公共大屏", Page.Help => "使用说明",
            Page.ScreenSharing => "屏幕共享",
            Page.Avatar => "选择动物形象", _ => "客厅网吧"
        };
        bool lounge = RenderLoungePage();
        if (!lounge) AddText(content, title, 34, CafeUiTheme.Ink, 58, true);
        if (!lounge) switch (page)
        {
            case Page.Home:
                AddText(content, "和朋友在同一间客厅", 20, CafeUiTheme.InkSoft, 42);
                AddText(content, "私人房间 · 最多 6 人", 16, CafeUiTheme.InkSoft, 34);
                AddButton("我的形象  ·  " + CafeAvatarLibrary.SelectedId, () => OpenAvatarPage(Page.Home));
                AddButton("创建房间", () => Show(Page.Create), true);
                AddButton("加入房间", () => Show(Page.Join));
                AddButton("单人体验", EnterSingle);
                AddButton("设置", () => Show(Page.Settings));
                AddButton("使用说明", () => Show(Page.Help));
                AddButton("退出程序", cafe.RequestQuit);
                break;
            case Page.Create:
            case Page.Join:
                nicknameField = AddInput("昵称", nickname, "输入临时昵称", 24, v => nickname = v);
                AddInput("房间服务 HTTPS 地址", serviceInput, "https://rooms.example.com 或本机 localhost", 180,
                    v => serviceInput = v);
                if (page == Page.Join)
                    inviteField = AddInput("邀请码", inviteInput, "输入朋友发送的邀请码", 40, v => inviteInput = v);
                AddButton("动物形象  ·  " + CafeAvatarLibrary.SelectedId, () => OpenAvatarPage(page));
                AddMicrophoneEntryChoice();
                AddButton(page == Page.Create ? "确认创建" : "确认加入", () => Connect(page == Page.Join), true);
                AddButton((page == Page.Help || page == Page.Settings) ? UtilityBackLabel : "返回大厅", () => Show(page == Page.Help || page == Page.Settings ? utilityReturn : Page.Lobby, (page == Page.Help || page == Page.Settings) && utilityReturn == Page.Members));
                break;
            case Page.Avatar:
                AddText(content, "每位玩家独立选择，进入同一房间也可使用相同动物", 16, CafeUiTheme.InkSoft, 36);
                string[] names = CafeAvatarLibrary.Ids;
                string[] labels = { "灯笼鱼", "蝙蝠", "柯基", "小鸭", "河马",
                                    "水母", "鸭嘴兽", "红色小兽", "犀牛", "鲨鱼" };
                int optionCount = Mathf.Min(names.Length, labels.Length);
                for (int row = 0; row < (optionCount + 1) / 2; row++)
                {
                    var group = Node("AvatarRow_" + row, content);
                    Height(group, 56);
                    var layout = group.gameObject.AddComponent<HorizontalLayoutGroup>();
                    layout.spacing = 10;
                    layout.childControlWidth = true;
                    layout.childControlHeight = true;
                    layout.childForceExpandWidth = true;
                    layout.childForceExpandHeight = false;
                    for (int col = 0; col < 2; col++)
                    {
                        int i = row * 2 + col;
                        if (i >= optionCount) break;
                        string id = names[i];
                        var choice = AddButton((CafeAvatarLibrary.SelectedId == id ? "✓  " : "") + labels[i],
                            () => ChooseAvatar(id), CafeAvatarLibrary.SelectedId == id, group);
                        avatarButtons.Add(choice);
                        avatarIds.Add(id);
                        avatarNames.Add(labels[i]);
                    }
                }
                AddButton("确认形象并返回", () => Show(avatarReturnPage, avatarReturnPage == Page.Members), true);
                break;
            case Page.Busy:
                busyProgress = AddText(content, "正在验证房间与网络连接…", 20, CafeUiTheme.InkSoft, 112);
#if NETCAFE_B1
                UpdateBusyProgress();
#endif
                AddButton("取消连接", CancelConnect);
                break;
            case Page.Invite:
#if NETCAFE_B1
                AddText(content, "把邀请码发给朋友", 19, CafeUiTheme.InkSoft, 40);
                AddText(content, network == null ? "" : network.InviteCode, 26, CafeUiTheme.Ink, 64);
                memberCountText = AddText(content, "成员  " + (network == null ? 0 : network.MemberCount) + " / 6", 17, CafeUiTheme.InkSoft, 38);
                AddButton("复制邀请码", CopyInvite, true);

#endif
                AddButton(roomEntered ? "回到房间" : "进入房间", CloseModal, true);
                break;
            case Page.Members:
#if NETCAFE_B1
                membersText = AddText(content, network == null ? "" : network.MembersSummary, 17, CafeUiTheme.Ink, 230);
                AddButton("刷新成员", () => { network?.RefreshMembers(); message = "正在更新成员列表…"; Render(); });
                AddVoiceControls();                AddSharingControls();
#endif
                AddButton("进入房间", CloseModal, true);
                break;
            case Page.ConfirmLeave:
                AddText(content, IsHost ? "你是房主，离开将关闭整个房间，其他成员也会断开。" :
                    "离开当前房间，返回大厅。", 20, CafeUiTheme.InkSoft, 120);
                AddButton(IsHost ? "确认关闭房间" : "确认离开房间", ConfirmLeave, true);
                AddButton("取消", CloseModal);
                break;
            case Page.RoomControls:
                AddVoiceControls(); AddSharingControls();
                AddButton("返回成员", () => Show(Page.Members, true));
                break;
            case Page.ScreenSharing:
                AddButton("返回菜单", Hide).name = "ShareBack";
                AddSharingControls(true);
                break;
            case Page.Settings:
                AddButton(CafeHud.ResetTextSizeLabel, () => { hud.ResetTextScale(); Resize(); Render(true); });
                AddButton("放大界面文字", () => { hud.SetTextScale(hud.TextScale >= 2 ? CafeHud.DefaultTextScale : hud.TextScale + .25f); message = "文字缩放已更新"; Render(); });
                AddText(content, "保留 F8 作为桌面脱离键；网吧不会接管真实桌面的 Esc 或 Alt+F4。", 18, CafeUiTheme.InkSoft, 92);
                AddButton(PlayerPrefs.GetInt("CafeReduceMotion", 0) != 0 ? "界面动效   减少" : "界面动效   完整", () => { PlayerPrefs.SetInt("CafeReduceMotion", PlayerPrefs.GetInt("CafeReduceMotion", 0) == 0 ? 1 : 0); PlayerPrefs.Save(); Render(true); });
                AddButton((page == Page.Help || page == Page.Settings) ? UtilityBackLabel : "返回大厅", () => Show(page == Page.Help || page == Page.Settings ? utilityReturn : Page.Lobby, (page == Page.Help || page == Page.Settings) && utilityReturn == Page.Members));
                break;
            case Page.PublicWall:
                AddPublicWallControls();
                break;
            case Page.Help:
                AddText(content, "房间操作：WASD 移动，Shift 奔跑，鼠标环顾；V 切换第一/第三人称（第三人称可以看到自己选中的动物）。看向椅子按 E 坐下；退出座位时系统优先选择椅子前方的空位置，再尝试两侧。\n\n电脑操作：坐下自动投射本机桌面，按设置的脱离键（默认 F8）回网吧，电脑操作中的 Esc / E / Alt+F4 仍发给真实应用。点工具栏“看房间”后，按住右键环顾、松开恢复工具；观察状态按 E 起身，返回电脑可点“回到电脑”。使用工具栏切换“非全屏模式”/“电脑全屏”；非全屏会保留电脑周围的房间和其他玩家，工具栏拖动不会改变镜头大小。\n\n房间：创建或加入前都能选择是否开麦（默认关闭，开麦需要系统授权）；房主和所有成员在「房间成员 / 麦克风」菜单中均可随时开关。空间语音只有主动启用后才会发布。截图只保存在本机，不会上传。跨网络使用 HTTPS 房间服务。", 19, CafeUiTheme.InkSoft, 490);
                AddButton((page == Page.Help || page == Page.Settings) ? UtilityBackLabel : "返回大厅", () => Show(page == Page.Help || page == Page.Settings ? utilityReturn : Page.Lobby, (page == Page.Help || page == Page.Settings) && utilityReturn == Page.Members));
                break;
        }
        if (!string.IsNullOrEmpty(message))
        {
            AddText(content, message, 17, CafeUiTheme.Danger, 56);
        }
        if(footerButton!=null && footerButton.gameObject.activeInHierarchy)focusable.Add(footerButton);
        if(identityPortrait!=null && identityPortrait.gameObject.activeInHierarchy)focusable.Add(identityPortrait.GetComponent<Button>());
        if (identityMicrophone != null && identityMicrophone.gameObject.activeInHierarchy && identityMicrophone.IsInteractable()) focusable.Add(identityMicrophone);
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        if (page == Page.ScreenSharing) focusable.RemoveAll(item => !item.IsInteractable());
        for (int i = 0; i < focusable.Count; i++)
        {
            var nav = new Navigation { mode = Navigation.Mode.Explicit,
                selectOnUp = focusable[(i - 1 + focusable.Count) % focusable.Count],
                selectOnDown = focusable[(i + 1) % focusable.Count],
                selectOnLeft = focusable[(i - 1 + focusable.Count) % focusable.Count],
                selectOnRight = focusable[(i + 1) % focusable.Count] };
            focusable[i].navigation = nav;
        }
        if (focusable.Count > 0 && EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
            int focusIndex = preserveScroll && oldFocusIndex >= 0 ?
                Mathf.Min(oldFocusIndex, focusable.Count - 1) : 0;
            if (page == Page.ScreenSharing)
            {
                focusIndex = focusable.FindIndex(item => item.name == oldShareFocus);
                if (focusIndex < 0) focusIndex = focusable.FindIndex(item => item.name == "ShareBack");
                focusIndex = Mathf.Max(focusIndex, 0);
            }
            EventSystem.current.SetSelectedGameObject(focusable[focusIndex].gameObject);
        }
        if (TransitionActive) roomTransition.FocusSkip();
        pageScroll.verticalNormalizedPosition = oldScroll;
    }

    void UpdateMemberLabels()
    {
#if NETCAFE_B1
        if (network == null || !visible) return;
        if (page == Page.Invite && memberCountText != null)
            memberCountText.text = "成员  " + network.MemberCount + " / 6";
        if (page == Page.Members) UpdateLoungeMembers();
#endif
    }

    bool IsHost
    {
        get
        {
#if NETCAFE_B1
            return network != null && network.IsHost;
#else
            return false;
#endif
        }
    }
    void OpenAvatarPage(Page origin)
    {
        avatarReturnPage = origin;
        message = "";
        Show(Page.Avatar, origin == Page.Members);
    }

    void ChooseAvatar(string id)
    {
        CafeAvatarLibrary.Select(id);
        var avatar = cafe != null && cafe.body != null
            ? cafe.body.GetComponent<CafeAvatarVisual>() : null;
        if (avatar != null) avatar.SetAvatar(id);
        message = "";
        // Repaint ten existing choices without destroying/recreating the page and its focus.
        for (int i = 0; i < avatarButtons.Count; i++)
        {
            var button = avatarButtons[i];
            if (button == null) continue;
            bool selected = avatarIds[i] == id;
            var label = button.GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = (selected ? "✓  " : "") + avatarNames[i];
            if (button.targetGraphic != null)
                button.targetGraphic.color = selected ? CafeUiTheme.Accent : CafeUiTheme.PaperDeep;
            var borders = button.GetComponents<Outline>();
            if (borders.Length > 1) borders[1].effectColor =
                selected ? CafeUiTheme.Amber : CafeUiTheme.Line;
        }
    }

    // Shown on both Create and Join. Choosing "on" never opens the device
    // before the player connects; the operating-system prompt follows entry.
    void AddMicrophoneEntryChoice()
    {
#if NETCAFE_B1
        AddButton(microphoneOnEntry ? "✓  进入房间后开启麦克风（需要系统授权）" :
            "进入房间后关闭麦克风（默认，点击可开启）", () =>
        {
            microphoneOnEntry = !microphoneOnEntry;
            Render(true);
        });

#endif
    }

    void Connect(bool join)
    {
        nickname = nickname.Trim();
        inviteInput = inviteInput.Trim();
        if (nickname.Length == 0) { message = "请输入昵称。"; Render(); return; }
        if (join && inviteInput.Length == 0) { message = "请输入邀请码。"; Render(); return; }
#if NETCAFE_B1
        if (network == null) { message = "联机组件不可用，请使用单人体验。"; Render(); return; }
        bool local = CafeLocalHostRuntime.IsLoopbackUrl(serviceInput);
        if (local && !join && (localHost == null || !localHost.Ready))
        {
            message = localHost != null && localHost.Starting ? "本机房间服务正在启动，请稍候。" :
                "本机房间服务还未就绪，请返回创建房间页面重试。";
            Render(); return;
        }
        if (!network.SetRoomServiceUrl(serviceInput, !local))
        {
            message = "房间服务必须使用 HTTPS；本地测试仅允许 HTTP localhost。";
            Render(); return;
        }
        if (!local) localHost?.StopOwned();
        creatingRoom = !join;
        pendingMicrophoneOnEntry = microphoneOnEntry;
        message = "";
        network.SetEntryFields(nickname, inviteInput);
        Show(Page.Busy);
        if (join) network.JoinRoom(); else network.CreateRoom();
#else
        message = "当前客户端未启用 B1 联机支持。请使用单人体验或安装联机构建。";
        Render();
#endif
    }
    void CancelConnect()
    {
#if NETCAFE_B1
        pendingMicrophoneOnEntry = false;
        microphoneOnEntry = false;
        network?.Leave();
        if (network == null || network.State == NetCafeNetworkSession.Phase.Disconnected)
            localHost?.StopOwned();
#endif
        creatingRoom = false;
        message = "已取消连接。";
        Show(Page.Home);
    }
    void EnterSingle()
    {
        message = "";
        enteredOnline = false;
        BeginRoomEntry();
    }
    public void OpenInvite()
    {
        if (!InNetworkRoom) return;
        cafe.OpenMenu();
        message = "";
        Show(Page.Invite, true);
    }
    public void OpenMembers()
    {
        if (!InNetworkRoom&&!roomEntered) return;
        cafe.OpenMenu();
        message = "";
        nextMemberRefresh = Time.unscaledTime + 4f;
#if NETCAFE_B1
        if(InNetworkRoom)network?.RefreshMembers();
#endif
        Show(Page.Members, true);
    }
    public void ReturnHome()
    {
        if (InNetworkRoom) { AskLeave(); return; }
        if (cafe.CurrentSeat >= 0) cafe.Stand();
        cafe.OpenMenu();
        enteredOnline = false;
        message = "";
        BeginRoomLeave(() => Show(Page.Lobby));
    }
    public void AskLeave()
    {
        if (!InNetworkRoom) return;
        cafe.OpenMenu();
        message = "";
        quitAfterLeave = false;
        Show(Page.ConfirmLeave, true);
    }
    public void RequestQuitFromUi()
    {
        if (!InNetworkRoom) { cafe.RequestQuit(); return; }
        AskLeave(); quitAfterLeave = true;
    }
    void CopyInvite()
    {
#if NETCAFE_B1
        var text = network?.InviteCode ?? "";
        if (string.IsNullOrEmpty(text)) { message = "邀请码尚未就绪。"; Render(); return; }
        try { GUIUtility.systemCopyBuffer = text; message = "邀请码已复制。"; }
        catch (Exception) { message = "无法访问剪贴板，请手动记录邀请码。"; }
        Render();
#endif
    }
    void AddVoiceControls()
    {
#if NETCAFE_B1
        if (voice == null) return;
        AddText(content, voice.Status,
            16, CafeUiTheme.InkSoft, 56);
        var button = AddButton(voice.MicrophoneActive ? "关闭麦克风" :
            voice.MicrophoneBusy ? "正在申请麦克风…" : "开启麦克风（需要授权）",
            () => voice.ToggleMicrophone());
        button.interactable = !voice.MicrophoneBusy;
        AddButton(voice.HearOthers ? "静音其他玩家" : "恢复房间声音", () => voice.TogglePlayback());
#endif
    }
    void AddSharingControls(bool dedicated = false)
    {
#if NETCAFE_B1
        if (sharing == null) return;
        string block = sharing.StartBlockReason;
        if (dedicated)
        {
            lastSharingBlock = block;
            lastSharingMonitor = sharing.CaptureMonitorLabel;
            AddText(content, "共享当前显示器的整个画面。房间成员能在你的机位观看，无法控制你的电脑。请先隐藏私人内容。",
                18, CafeUiTheme.InkSoft, 100);
            AddText(content, "当前显示器：" + lastSharingMonitor, 16, CafeUiTheme.InkSoft, 36);
        }
        AddText(content, dedicated && !sharing.Sharing && !sharing.Busy && block != null ? block : sharing.Status,
            16, CafeUiTheme.InkSoft, 60);
        if (!sharing.Sharing)
        {
            AddText(content, sharing.AllowPublicDisplayOnStart ?
                "开始共享后，房间成员可以把你的画面选到公共大屏。可以在开始前关闭这项权限。" :
                "开始共享后，画面只在你的机位显示；你可以之后再允许投到公共大屏。",
                15, CafeUiTheme.InkSoft, 67);
            var wallDefault = AddButton(sharing.AllowPublicDisplayOnStart ?
                "大屏投放权限：默认允许" : "大屏投放权限：默认关闭", sharing.TogglePublicDisplayOnStart);
            wallDefault.name = "WallShareDefault";
            wallDefault.interactable = !sharing.Busy;
        }
        string actionLabel = sharing.Sharing ? (dedicated ? "停止屏幕共享" : "停止共享我的屏幕") :
            sharing.Busy ? "取消启动共享" : dedicated ? "开始共享整个屏幕" : "主动共享整个电脑屏幕（房间成员可见）";
        var action = AddButton(actionLabel, sharing.ToggleSharing, dedicated);
        action.interactable = !dedicated || sharing.Sharing || sharing.Busy || block == null;
        if (dedicated) action.name = sharing.Sharing ? "ShareStop" : sharing.Busy ? "ShareCancel" : "ShareStart";
        if (sharing.Sharing)
        {
            bool soundAvailable = sharing.CanShareDesktopSound;
            AddText(content, soundAvailable ? sharing.SoundStatus + " · 开启后会分享电脑播放的应用和系统声音。投到大屏时，声音随画面播放。" :
                "当前 Windows 不支持排除游戏声音的捕获，仍可共享画面。", 15, CafeUiTheme.InkSoft, 74);
            var sound = AddButton(sharing.SoundSharing ? "停止共享电脑声音" :
                sharing.SoundBusy ? "取消开启电脑声音" : "共享电脑声音", sharing.ToggleDesktopSound);
            sound.name = "ShareSound";
            sound.interactable = soundAvailable;
        }
        if (sharing.Sharing)
        {
            AddText(content, sharing.AllowPublicDisplay ? "已允许房间成员把我的共享画面选到公共大屏。" :
                "目前只在机位共享。允许后，房间成员可在公共大屏前选择你的画面。",
                15, CafeUiTheme.InkSoft, 67);
            var publicConsent = AddButton(sharing.AllowPublicDisplay ?
                "撤销公共大屏投放授权" : "允许投到公共大屏", sharing.TogglePublicDisplayConsent);
            if (dedicated) publicConsent.name = sharing.AllowPublicDisplay ? "WallRevoke" : "WallGrant";
        }
        AddText(content, "离座后仍会共享。可在这里停播或撤销大屏授权；离房、断线或桌面捕获中断会停止共享。", 15, CafeUiTheme.InkSoft, 70);
#endif
    }

    internal void OpenPublicWall()
    {
#if NETCAFE_B1
        if (!Ready || !roomEntered || visible || TransitionActive || !InNetworkRoom ||
            cafe.Mode != CafePrototype.InputMode.Walking || wallSync == null) return;
        message = ""; wallFeedback = ""; requestedWallOwner = null;
        nextWallRefresh = 0;
        cafe.OpenMenu();
        Show(Page.PublicWall, true);
#endif
    }

#if NETCAFE_B1
    string ReadWallUiState()
    {
        wallSync.GetPublicSources(wallSources);
        var state = new System.Text.StringBuilder().Append(wallSync.CanSelectPublicScreen)
            .Append('|').Append(wallSync.PublicScreenIdentity).Append('|').Append(wallSync.PublicScreenTrack)
            .Append('|').Append(sharing != null && sharing.PublicWallHasFrame).Append('|').Append(wallFeedback);
        foreach (var source in wallSources)
            state.Append('|').Append(source.id).Append(':').Append(source.PublicSourceSeat)
                .Append(':').Append(source.publicTrack).Append(':').Append(source.publicGrant);
        return state.ToString();
    }
    void RequestWallSource(CafeRoomSync.Actor source)
    {
        if (wallSync.RequestPublicScreen(source))
        {
            requestedWallOwner = source?.id ?? "";
            wallRequestDeadline = Time.unscaledTime + 3f;
            wallFeedback = "正在切换放映…";
        }
        else
        {
            requestedWallOwner = null;
            wallFeedback = "暂时无法切换，请稍后重试。";
        }
        Render(true);
    }
#endif
    void AddPublicWallControls()
    {
#if NETCAFE_B1
        wallUiState = ReadWallUiState();
        if (!wallSync.CanSelectPublicScreen)
            AddText(content, "正在同步房间，请稍候。", 17, CafeUiTheme.InkSoft, 44);
        else
        {
            string selected = wallSync.PublicScreenIdentity;
            string state = selected == "" ? "未放映" :
                sharing != null && sharing.PublicWallHasFrame ? "正在放映" : "正在等待画面…";
            AddText(content, wallFeedback != "" ? wallFeedback : state, 17, CafeUiTheme.InkSoft, 42);
            if (wallSources.Count == 0)
                AddText(content, "暂无可投放的玩家。请让分享者开启屏幕共享，并允许投到公共大屏。",
                    16, CafeUiTheme.InkSoft, 66);
            foreach (var source in wallSources)
            {
                bool current = source.id == selected && source.publicTrack == wallSync.PublicScreenTrack;
                var choice = AddButton((current ? "当前 · " : "投放 · ") + source.name + " · 电脑 " +
                    (source.PublicSourceSeat + 1), () => RequestWallSource(source));
                choice.GetComponentInChildren<TMP_Text>().richText = false;
                choice.interactable = !current;
            }
            if (selected != "") AddButton("关闭放映", () => RequestWallSource(null));
        }
#endif
        AddButton("返回房间", CloseModal);
    }

    void ConfirmLeave()
    {
        if (quitAfterLeave) { quitAfterLeave = false; cafe.RequestQuit(); return; }
        pendingMicrophoneOnEntry = false;
        microphoneOnEntry = false;
        if (cafe.CurrentSeat >= 0) cafe.Stand();
        cafe.OpenMenu();
#if NETCAFE_B1
        network?.Leave();
#endif
        enteredOnline = false;
        message = "";
        BeginRoomLeave(() => Show(Page.Lobby));
    }
    void CloseModal()
    {
        quitAfterLeave = false;
        if (fromRoom) { if (!roomEntered) BeginRoomEntry(); else { Hide(); cafe.Continue(); } }
        else Show(Page.Home);
    }
    public void HandleBack()
    {
        if (TransitionActive) { roomTransition.Finish(); return; }
        if (!visible) return;
        if (page == Page.Busy) CancelConnect();
        else if (page == Page.Home) { /* Home has no automatic return into game. */ }
        else if (page == Page.Lobby) Show(Page.Home);
        else if (page == Page.Avatar) Show(avatarReturnPage, avatarReturnPage == Page.Members);
        else if (page == Page.Help || page == Page.Settings) Show(utilityReturn, utilityReturn == Page.Members);
        else if (page == Page.RoomControls) Show(Page.Members, true);
        else if (page == Page.ScreenSharing) Hide();
        else if (fromRoom) CloseModal();
        else Show(Page.Lobby);
    }
    System.Collections.IEnumerator HostAutostartSmoke()
    {
#if NETCAFE_B1
        // End-to-end fixture: Enter Create, start only owned local children,
        // require /readyz, then go Home and prove both child processes stopped.
        // A stale PlayerPrefs public endpoint must not skew this local fixture.
        serviceInput = "http://127.0.0.1:8787";
        network.SetRoomServiceUrl(serviceInput, false);
        Show(Page.Create);
        float deadline = Time.realtimeSinceStartup + 35f;
        while (Time.realtimeSinceStartup < deadline && localHost != null && localHost.Starting)
            yield return null;
        if (localHost == null || !localHost.Ready ||
            !CafeLocalHostRuntime.IsLoopbackUrl(serviceInput) ||
            !string.Equals(serviceInput, network.RoomServiceUrl, StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogError("HOST_AUTOSTART_SMOKE_FAIL start/ready: " + message);
            Application.Quit(1); yield break;
        }
        Debug.Log("HOST_AUTOSTART_CREATE_READY_PASS: " + serviceInput);
        Show(Page.Home);
        yield return new WaitForSecondsRealtime(.3f);
        if (localHost.Ready || localHost.Starting)
        {
            Debug.LogError("HOST_AUTOSTART_SMOKE_FAIL cleanup");
            Application.Quit(1); yield break;
        }
        Debug.Log("HOST_AUTOSTART_STOP_OWNED_PASS");
        Application.Quit(0);
#else
        Debug.LogError("HOST_AUTOSTART_SMOKE_FAIL B1_DISABLED");
        Application.Quit(1);
#endif
        yield break;
    }

    System.Collections.IEnumerator HostAutostartCreateLeaveSmoke()
    {
#if NETCAFE_B1
        serviceInput = "http://127.0.0.1:8787";
        network.SetRoomServiceUrl(serviceInput, false);
        Show(Page.Create);
        float startDeadline = Time.realtimeSinceStartup + 35f;
        while (localHost.Starting && Time.realtimeSinceStartup < startDeadline)
            yield return null;
        if (!localHost.Ready)
        {
            Debug.LogError("HOST_ROOM_CREATE_LEAVE_FAIL local start: " + message);
            Application.Quit(1); yield break;
        }
        nickname = "验收房主";
        Connect(false); // The exact production Create Room button handler.
        float connectDeadline = Time.realtimeSinceStartup + 35f;
        while (network.State != NetCafeNetworkSession.Phase.Connected &&
               network.State != NetCafeNetworkSession.Phase.Failed &&
               network.State != NetCafeNetworkSession.Phase.Disconnected &&
               Time.realtimeSinceStartup < connectDeadline)
            yield return null;
        if (network.State != NetCafeNetworkSession.Phase.Connected || !localHost.Ready)
        {
            Debug.LogError("HOST_ROOM_CREATE_LEAVE_FAIL connect state=" + network.State +
                " reason=" + network.Error);
            Application.Quit(1); yield break;
        }
        Debug.Log("HOST_ROOM_CREATE_CONNECTED_PASS");
        yield return null; // Ensure the UI processed the Connected event.
        ConfirmLeave(); // Sends the room-service /leave before owning processes stop.
        float leaveDeadline = Time.realtimeSinceStartup + 20f;
        while ((network.State != NetCafeNetworkSession.Phase.Disconnected || localHost.Ready || localHost.Starting) &&
               Time.realtimeSinceStartup < leaveDeadline)
            yield return null;
        if (network.State != NetCafeNetworkSession.Phase.Disconnected || localHost.Ready || localHost.Starting)
        {
            Debug.LogError("HOST_ROOM_CREATE_LEAVE_FAIL cleanup state=" + network.State);
            Application.Quit(1); yield break;
        }
        Debug.Log("HOST_ROOM_LEAVE_STOP_PASS");
        Application.Quit(0);
#else
        Debug.LogError("HOST_ROOM_CREATE_LEAVE_FAIL B1_DISABLED");
        Application.Quit(1);
#endif
        yield break;
    }

    // Full real Windows Unity -> HTTPS room-service -> LiveKit Cloud B1 acceptance.
    // Uses the actual StreamingAssets room-service.url written into the Windows release.
    // No room code, session credential, or LiveKit JWT is ever printed.
    System.Collections.IEnumerator WanCloudB1Smoke()
    {
#if NETCAFE_B1
        serviceInput = network.RoomServiceUrl;
        if (string.IsNullOrEmpty(serviceInput) || !serviceInput.StartsWith("https://", StringComparison.Ordinal) ||
            CafeLocalHostRuntime.IsLoopbackUrl(serviceInput))
        {
            Debug.LogError("WAN_CLOUD_B1_FAIL INVALID_HTTPS_CONFIGURATION");
            Application.Quit(1); yield break;
        }
        Show(Page.Create);
        yield return null;
        if (localHost != null && (localHost.Ready || localHost.Starting))
        {
            Debug.LogError("WAN_CLOUD_B1_FAIL UNEXPECTED_LOCAL_HOST");
            Application.Quit(1); yield break;
        }
        nickname = "WAN 验收房主";
        Connect(false);
        float connectDeadline = Time.realtimeSinceStartup + 135f;
        while (network.State != NetCafeNetworkSession.Phase.Connected &&
               network.State != NetCafeNetworkSession.Phase.Failed &&
               network.State != NetCafeNetworkSession.Phase.Disconnected &&
               Time.realtimeSinceStartup < connectDeadline)
            yield return null;
        if (network.State != NetCafeNetworkSession.Phase.Connected)
        {
            // Keep diagnostic output coarse: network.Error may contain sensitive remote details.
            Debug.LogError("WAN_CLOUD_B1_FAIL CLOUD_CONNECT_STATE_" + network.State);
            network.Leave();
            Application.Quit(1); yield break;
        }
        Debug.Log("WAN_CLOUD_B1_CONNECTED_PASS");
        yield return null;
        ConfirmLeave();
        float leaveDeadline = Time.realtimeSinceStartup + 35f;
        while (network.State != NetCafeNetworkSession.Phase.Disconnected &&
               Time.realtimeSinceStartup < leaveDeadline)
            yield return null;
        if (network.State != NetCafeNetworkSession.Phase.Disconnected ||
            (localHost != null && (localHost.Ready || localHost.Starting)))
        {
            Debug.LogError("WAN_CLOUD_B1_FAIL LEAVE_OR_LOCAL_PROCESS_CLEANUP");
            Application.Quit(1); yield break;
        }
        Debug.Log("WAN_CLOUD_B1_LEAVE_PASS");
        Application.Quit(0);
#else
        Debug.LogError("WAN_CLOUD_B1_FAIL NETCAFE_B1_DISABLED");
        Application.Quit(1);
#endif
        yield break;
    }

    static string TranslateError(string code)
    {
        return code switch
        {
            "INVALID_INVITATION" => "邀请码无效或已过期。",
            "ROOM_FULL" => "房间人数已满（最多 6 人）。",
            "INVALID_NICKNAME" => "昵称不能为空，且不得超过 24 个字符。",
            "ROOM_CREATION_FAILED" => "无法创建房间，请稍后重试。",
            "LIVEKIT_CONNECT_FAILED" => "房间服务已分配房间，但无法连接 LiveKit 实时服务。请检查网络后重试。",
            "LIVEKIT_NOT_READY" => "房间服务已响应，但 LiveKit 实时服务尚未就绪。请稍后重试。",
            "ROOM_API_NO_RESPONSE" => "房间服务已就绪，但开房或加入请求未收到响应。请稍后重试。",
            "ROOM_CONFIRM_NO_RESPONSE" => "已连接 LiveKit，但房间服务未回应入房确认。请稍后重试。",
            "LIVEKIT_DISCONNECTED_DURING_CONFIRM" => "LiveKit 在入房确认时断开连接。请稍后重试。",
            "WINDOWS_LOW_INTEGRITY" => "无法读取系统信任证书，请更换完整解压目录后重试。单人体验和本地开服仍可使用。",
            "NETWORK_UNREACHABLE" => "无法访问房间服务，请检查服务地址与网络连接。",
            "SERVICE_WAKE_TIMEOUT" => "未能在 90 秒内收到房间服务的健康响应。公网开房请稍后重试。",
            "SERVICE_UNAVAILABLE" => "房间服务无法连接实时服务器，请检查 LiveKit 配置与运行状态。",
            "INVALID_SERVER_REPLY" => "房间服务返回了无效数据，请检查服务器日志。",
            "HTTP_502" => "房间服务返回 HTTP 502，请确认本地开房服务及 LiveKit 正常启动。",
            "HTTP_503" => "房间服务暂时不可用，请启动或检查服务器后重试。",
            "RECONNECT_TIMEOUT" => "重连超时，请重新加入房间。",
            "RATE_LIMITED" => "请求过于频繁，请稍后重试。",
            "SESSION_EXPIRED" => "会话已过期，请重新加入。",
            _ => "连接失败，请检查开房服务和网络。" 
        };
    }
    void OnDestroy()
    {
#if NETCAFE_B1
        if (network != null)
        {
            network.StateChanged -= OnConnectionState;
            network.MembersChanged -= OnMembersChanged;
        }
        if (voice != null) voice.Changed -= OnVoiceChanged;
        if (sharing != null) sharing.Changed -= OnSharingChanged;
#endif
    }
}

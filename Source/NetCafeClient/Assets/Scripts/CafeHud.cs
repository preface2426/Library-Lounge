using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

// Presentation only: room transitions, native input and capture keep their existing owners.
public sealed class CafeHud : MonoBehaviour
{
    public Canvas canvas, pointerCanvas;
    public UnityEngine.UI.CanvasScaler scaler;
    public RectTransform safeRoot, menuPanel, toolbarPanel, menuRail, menuContent;
    public GameObject roomHud, menuRoot, toolbarExpanded, toolbarTab, desktopRoot, menuFooter, railStatus, railMeta, railHotkey;
    public UnityEngine.UI.RawImage desktopImage, pointerImage;
    public TMP_Text roomHint, interactionHint, menuTitle, menuSubtitle, settingsHint, captureStatus, hotkeyHint;
    public TMP_Text menuEyebrow, lobbyStatus, railKicker, railSubtitle, screenshotStatus;
    public UnityEngine.UI.VerticalLayoutGroup menuRailLayout, menuContentLayout;
    public GameObject[] pages;
    public UnityEngine.UI.ScrollRect scroll;
    public InputSystemUIInputModule uiInput;
    public EventSystem eventSystem;
    CafePrototype cafe;
    CafeDesktopCapture capture;
    CafeDesktopInput input;
    CafeScreenshot screenshot;
    readonly Dictionary<string, UnityEngine.UI.Button> buttons = new();
    readonly List<int> stack = new();
    readonly List<UnityEngine.UI.Selectable> focusOrder = new();
    bool wasMenu;
    bool retryingDesktopCapture;
    CafeEntryUi cachedEntryUi;
    int width, height;
    float lastScale;
    Rect lastSafe;
    internal const float DefaultTextScale = 1f;
    internal const string ResetTextSizeLabel = "恢复默认字号（100%）";
    public float TextScale { get; private set; } = DefaultTextScale;
    public int Page => stack.Count == 0 ? 0 : stack[^1];
    public bool Rebinding => cafe.Mode == CafePrototype.InputMode.Menu && Page == 3;
    public const string HelpText =
        "<b>1  在客厅里</b>\nWASD 前后左右移动，Shift 奔跑，鼠标移动控制视线；V 切换第一人称 / 第三人称（可看到自己的动物角色）。看向座位按 E 坐下，离座时优先选择前方空位，受阻自动尝试两侧。Esc 打开菜单。\n\n" +
        "<b>2  使用自己的电脑</b>\n坐下后自动显示本机桌面。键盘和鼠标交给真实应用；Esc、Alt、F9 等照常使用。只有设置的返回键由网吧保留。\n\n" +
        "<b>3  看房间与离座</b>\n点击工具栏“看房间”，按住右键环顾，松开继续操作工具；点击“回到电脑”恢复。观察模式按 E 或菜单“离开座位”起身。电脑控制期间键盘归真实应用（包括 E、Esc、Alt+F4），按已设置的脱离键（默认 F8）先回网吧。第三人称只用于走动，不会干扰操作屏幕。\n\n" +
        "<b>4  不用记很多快捷键</b>\n浮窗可拖动，空闲约 2 秒后贴边收起；把鼠标移到“菜单”标签上展开。菜单中也能看房间、离座和退出。\n\n" +
        "<b>5  显示与设置</b>\n走动时按 V 可随时切换第三人称 / 第一人称；简易菜单“电脑全屏 / 房间视图”在带实体边框的房间视图与沉浸全屏之间切换。菜单 → 设置，可更改脱离键、文字大小、显示器和灯光。更换显示器前先停止预览。菜单底部可保存本地 PNG 截图，不会上传或共享。\n\n" +
        "<b>6  回到 Windows 或退出</b>\nWindows 键或 Alt+Tab 会释放控制并恢复系统光标。返回游戏后点“继续”。退出网吧请用菜单中的“退出网吧”；操作电脑时 Alt+F4 会关闭当前真实应用。\n\n" +
        "<b>7  菜单导航</b>\n鼠标点击，或 Tab / Shift+Tab、方向键选择，Enter 确认，Esc 返回上一级。手柄可用方向键／摇杆、A 确认、B 返回。\n\n" +
        "<b>屏幕共享</b>\n联网房间入座后，按脱离键（默认 F8），在主菜单点「屏幕共享」→「开始共享整个屏幕」。公共大屏投放需单独授权。";

    void Awake()
    {
        cafe = GetComponent<CafePrototype>(); capture = GetComponent<CafeDesktopCapture>(); input = GetComponent<CafeDesktopInput>(); screenshot = GetComponent<CafeScreenshot>();
        cachedEntryUi = GetComponent<CafeEntryUi>();
        // Older scenes contain an unbound duplicate PointerCanvas at the origin.
        foreach(var pointerLayer in GetComponentsInChildren<Canvas>(true))
            if(pointerLayer.name=="PointerCanvas" && pointerLayer!=pointerCanvas)
                pointerLayer.gameObject.SetActive(false);
        pointerImage.gameObject.SetActive(false);
        if (screenshot == null) screenshot = gameObject.AddComponent<CafeScreenshot>();
        uiInput.AssignDefaultActions();
        CafeUiScroll.Configure(scroll);
        foreach (var button in canvas.GetComponentsInChildren<UnityEngine.UI.Button>(true)) buttons.Add(button.name, button);
        var textSize = buttons["TextSizeButton"];
        var resetTextSize = Instantiate(textSize, textSize.transform.parent, false);
        resetTextSize.name = "ResetTextSizeButton";
        resetTextSize.transform.SetAsFirstSibling();
        textSize.transform.SetSiblingIndex(1);
        resetTextSize.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
        resetTextSize.GetComponentInChildren<TMP_Text>(true).text = ResetTextSizeLabel;
        buttons.Add(resetTextSize.name, resetTextSize);
        resetTextSize.onClick.AddListener(ResetTextScale);
        Bind("ContinueButton", cafe.Continue); Bind("ObserveButton", cafe.Observe); Bind("StandButton", cafe.Stand);
        Bind("SettingsButton", () => Push(1)); Bind("HelpButton", () => Push(2)); Bind("BackButton", Back);
        BindOptional("InviteRoomButton", () => cachedEntryUi?.OpenInvite());
        BindOptional("MembersRoomButton", () => cachedEntryUi?.OpenMembers());
        BindOptional("LeaveRoomButton", () => cachedEntryUi?.AskLeave());
        BindOptional("ReturnHomeButton", () => cachedEntryUi?.ReturnHome());
        if (buttons.ContainsKey("SaveScreenshotButton")) Bind("SaveScreenshotButton", screenshot.Capture); Bind("QuitButton", () => { if (cachedEntryUi != null && cachedEntryUi.Ready) cachedEntryUi.RequestQuitFromUi(); else cafe.RequestQuit(); });
        Bind("ViewButton", input.ToggleView); Bind("LightButton", cafe.ToggleLamp);
        Bind("RebindButton", () => Push(3)); Bind("ResetKeyButton", () => { input.TrySetHotkey(0x77, 0); Refresh(); });
        Bind("TextSizeButton", () => SetTextScale(TextScale >= 2 ? DefaultTextScale : TextScale < 1.25f ? 1.25f : TextScale < 1.5f ? 1.5f : 2));
        Bind("MonitorButton", capture.SelectNextMonitor); Bind("RefreshButton", capture.RefreshMonitors);
        Bind("CaptureButton", () => { if (capture.Active) capture.StopCapture(); else capture.BeginCapture(capture.MonitorIndex); });
        BindOptional("RetryCaptureButton", () =>
        {
            if (!cafe.IsComputerSeat || capture.Active || retryingDesktopCapture) return;
            StartCoroutine(RetryDesktopCapture());
        });
        Bind("ToolbarObserve", () => { if (cafe.Mode == CafePrototype.InputMode.Observing) cafe.ResumeDesktop(); else cafe.Observe(); });
        Bind("ToolbarStand", cafe.Stand); Bind("ToolbarView", input.ToggleView); Bind("ToolbarMenu", cafe.OpenMenu);
        Bind("ToolbarTab", input.ResetToolbar);
        cafe.Changed += Refresh; input.Changed += Refresh; input.PointerChanged += MovePointer; capture.Changed += Refresh; screenshot.Changed += Refresh;
        TextScale = Mathf.Clamp(PlayerPrefs.GetFloat("CafeTextScale", DefaultTextScale), DefaultTextScale, 2);
        scaler.referenceResolution = new Vector2(CafeUiTheme.ReferenceWidth, CafeUiTheme.ReferenceHeight) / TextScale;
        input.SetToolbarScale(TextScale);
    }
    // The native capture startup is asynchronous. A successful NC_Start only
    // queues the request; it DOES NOT imply Windows delivered usable video.
    IEnumerator RetryDesktopCapture()
    {
        retryingDesktopCapture = true;
        if (capture.BeginCapture(capture.MonitorIndex))
        {
            float until = Time.unscaledTime + 9f;
            while (capture.Active && !capture.Ready && Time.unscaledTime < until)
                yield return null;
            if (capture.Ready && cafe.Mode == CafePrototype.InputMode.Menu)
                cafe.Continue();
        }
        retryingDesktopCapture = false;
        Refresh();
    }
    string MainPageSubtitle()
    {
        if (cafe.IsComputerSeat && !capture.Active && !string.IsNullOrEmpty(capture.Error))
            return capture.Error.Contains("0x80070005")
                ? "Windows 拒绝访问当前显示器（0x80070005）。请在「设置」查看错误详情并检查系统屏幕捕获权限。"
                : "桌面投射失败：" + capture.Error;
        if (cafe.IsComputerSeat && capture.Active && !capture.Ready)
            return "正在等待 Windows 桌面画面，尚未成功投射。";
        if (cafe.IsComputerSeat && !capture.Active)
            return "当前未投射本地桌面。请进入设置开启桌面预览。";
        return "电脑还在运行，选一个动作继续。";
    }
    void Start() { ApplyLayout(); Refresh(); MovePointer(input.Pointer); }
    void Bind(string name, UnityAction action) { buttons[name].onClick.AddListener(action); }
    void BindOptional(string name, UnityAction action) { if (buttons.TryGetValue(name, out var btn)) btn.onClick.AddListener(action); }
    void Label(string name, string value) { buttons[name].GetComponentInChildren<TMP_Text>().text = value; }
    public UnityEngine.UI.Button Button(string name) => GetComponent<CafeLoungeHud>()?.VisibleMenuButton(name) ?? buttons[name];
    public Vector2 ButtonPoint(string name)
    {
        Focus(Button(name)); Canvas.ForceUpdateCanvases();
        var rect = (RectTransform)Button(name).transform;
        return RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
    }
    public void Push(int page)
    {
        if (cafe.Mode != CafePrototype.InputMode.Menu) return;
        stack.Add(page); ShowPage(); Refresh();
    }
    public void Back()
    {
        if (stack.Count > 1) { stack.RemoveAt(stack.Count - 1); ShowPage(); Refresh(); }
        else if(GetComponent<CafeLoungeHud>()?.CloseMore()!=true)cafe.Continue();
    }
    void ShowPage()
    {
        for (int i = 0; i < pages.Length; i++) pages[i].SetActive(i == Page);
        menuEyebrow.text = Page == 0 ? "LOBBY / MAIN" : Page == 1 ? "LOBBY / SETTINGS" : Page == 2 ? "LOBBY / GUIDE" : "LOBBY / SHORTCUT";
        menuTitle.text = Page == 0 ? "回到你的位置" : Page == 1 ? "按自己的习惯" : Page == 2 ? "使用说明" : "设置返回键";
        menuSubtitle.text = Page == 0 ? MainPageSubtitle() : Page == 1 ? "紧急退出：" + input.EmergencyLabel + "（界面无响应时立即结束游戏）" : Page == 2 ? "从坐下到离开，都可以用菜单完成。" : "只保留这一个网吧快捷键。";
        Canvas.ForceUpdateCanvases();
        BuildFocusOrder();
        Focus(Page == 0 ? Button("ContinueButton") : Page == 1 ? buttons["ResetTextSizeButton"] : buttons["BackButton"]);
        scroll.verticalNormalizedPosition = 1;
    }
    void BuildFocusOrder()
    {
        focusOrder.Clear();
        foreach (var control in menuPanel.GetComponentsInChildren<UnityEngine.UI.Button>())
            if (control.IsInteractable()) focusOrder.Add(control);
        for (int i = 0; i < focusOrder.Count; i++)
        {
            var nav = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.Explicit,
                selectOnUp = focusOrder[(i + focusOrder.Count - 1) % focusOrder.Count], selectOnDown = focusOrder[(i + 1) % focusOrder.Count], selectOnLeft = focusOrder[(i + focusOrder.Count - 1) % focusOrder.Count], selectOnRight = focusOrder[(i + 1) % focusOrder.Count] };
            focusOrder[i].navigation = nav;
        }
    }
    void Focus(UnityEngine.UI.Selectable control) { eventSystem.SetSelectedGameObject(null); eventSystem.SetSelectedGameObject(control.gameObject); }
    internal void ResetTextScale() => SetTextScale(DefaultTextScale);

    public void SetTextScale(float value)
    {
        TextScale = Mathf.Clamp(value, DefaultTextScale, 2); scaler.referenceResolution = new Vector2(CafeUiTheme.ReferenceWidth, CafeUiTheme.ReferenceHeight) / TextScale;
        PlayerPrefs.SetFloat("CafeTextScale", TextScale); PlayerPrefs.Save(); input.SetToolbarScale(TextScale); Refresh();
    }
    void Update()
    {
        if (cachedEntryUi?.BlocksRoomMenu == true) return;
        if (width != Screen.width || height != Screen.height || lastScale != canvas.scaleFactor || lastSafe != Screen.safeArea) ApplyLayout();
        if (!wasMenu || !Application.isFocused) return;
        // Esc/Gamepad B are dispatched once by CafePrototype.Update.
        // Handling B here too used to pop two menu levels in one frame.
        if (Keyboard.current == null) return;
        var keyboard = Keyboard.current;
        if (Rebinding)
        {
            for (int i = 0; i < 12; i++) if (keyboard[(Key)((int)Key.F1 + i)].wasPressedThisFrame)
            {
                int mods = (keyboard.ctrlKey.isPressed ? 2 : 0) | (keyboard.altKey.isPressed ? 1 : 0) | (keyboard.shiftKey.isPressed ? 4 : 0);
                if (input.TrySetHotkey(0x70 + i, mods)) Back(); else Refresh();
                break;
            }
        }
        var lounge=GetComponent<CafeLoungeHud>();
        if(keyboard.tabKey.wasPressedThisFrame && lounge!=null && lounge.Expanded && !lounge.MoreOpen && lounge.LightRoot!=null) {
            focusOrder.Clear();
            foreach(var button in lounge.LightRoot.GetComponentsInChildren<UnityEngine.UI.Button>())if(button.IsInteractable())focusOrder.Add(button);
        }
        else if(keyboard.tabKey.wasPressedThisFrame&&lounge!=null&&lounge.MoreOpen&&Page==0&&lounge.MorePanelRoot!=null){
            focusOrder.Clear();
            foreach(var button in lounge.MorePanelRoot.GetComponentsInChildren<UnityEngine.UI.Button>())if(button.IsInteractable())focusOrder.Add(button);
        }
        if (keyboard.tabKey.wasPressedThisFrame && focusOrder.Count > 0)
        {
            int index = focusOrder.FindIndex(item => item.gameObject == eventSystem.currentSelectedGameObject);
            if (index < 0 && keyboard.shiftKey.isPressed) index = 0;
            Focus(focusOrder[(index + (keyboard.shiftKey.isPressed ? focusOrder.Count - 1 : 1) + focusOrder.Count) % focusOrder.Count]);
        }
    }

    void ApplyLayout()
    {
        width = Screen.width; height = Screen.height; lastScale = canvas.scaleFactor; lastSafe = Screen.safeArea;
        if (width <= 0 || height <= 0) return;
        safeRoot.anchorMin = new Vector2(lastSafe.xMin / width, lastSafe.yMin / height);
        safeRoot.anchorMax = new Vector2(lastSafe.xMax / width, lastSafe.yMax / height);

        float safeWidth = lastSafe.width / Mathf.Max(.01f, lastScale);
        float safeHeight = lastSafe.height / Mathf.Max(.01f, lastScale);
        float ratio = safeWidth / Mathf.Max(1, safeHeight);
        bool wide = ratio >= CafeUiTheme.MenuWideBreakpoint && safeWidth >= 920 && safeHeight >= 520;
        float panelMaxHeight = wide ? CafeUiTheme.MenuMaxHeight : 920f;
        menuPanel.sizeDelta = new Vector2(
            Mathf.Max(1, Mathf.Min(CafeUiTheme.MenuMaxWidth, safeWidth - 48)),
            Mathf.Max(1, Mathf.Min(panelMaxHeight, safeHeight - 48)));
        float railHeight = Mathf.Clamp(safeHeight * .25f, 88, CafeUiTheme.MenuRailStackedHeight);
        if (wide)
        {
            menuRail.anchorMin = new Vector2(0, 0); menuRail.anchorMax = new Vector2(0, 1); menuRail.pivot = new Vector2(0, .5f);
            menuRail.anchoredPosition = Vector2.zero; menuRail.sizeDelta = new Vector2(CafeUiTheme.MenuRailWidth, 0);
            menuContent.anchorMin = Vector2.zero; menuContent.anchorMax = Vector2.one; menuContent.pivot = new Vector2(.5f, .5f);
            menuContent.offsetMin = new Vector2(CafeUiTheme.MenuRailWidth, 0); menuContent.offsetMax = Vector2.zero;
            menuRailLayout.padding = new RectOffset(30, 30, 30, 28); menuRailLayout.spacing = 9;
            menuContentLayout.padding = new RectOffset(28, 28, 24, 20);
            SetRailDetailVisible(true); SetRailStatusHeight(76);
        }
        else
        {
            menuRail.anchorMin = new Vector2(0, 1); menuRail.anchorMax = new Vector2(1, 1); menuRail.pivot = new Vector2(.5f, 1);
            menuRail.anchoredPosition = Vector2.zero; menuRail.sizeDelta = new Vector2(0, railHeight);
            menuContent.anchorMin = Vector2.zero; menuContent.anchorMax = Vector2.one; menuContent.pivot = new Vector2(.5f, .5f);
            menuContent.offsetMin = Vector2.zero; menuContent.offsetMax = new Vector2(0, -railHeight);
            bool compactRail = railHeight < 170;
            menuRailLayout.padding = compactRail ? new RectOffset(20, 20, 12, 10) : new RectOffset(24, 24, 16, 14);
            menuRailLayout.spacing = compactRail ? 4 : 7;
            menuContentLayout.padding = new RectOffset(22, 22, 16, 14);
            railKicker.gameObject.SetActive(!compactRail); railStatus.SetActive(!compactRail);
            railSubtitle.gameObject.SetActive(false); railMeta.SetActive(false); railHotkey.SetActive(false);
            SetRailStatusHeight(64);
        }

        menuRail.gameObject.SetActive(false);
        menuContent.offsetMin = Vector2.zero; menuContent.offsetMax = Vector2.zero;
        menuPanel.anchorMin = menuPanel.anchorMax = menuPanel.pivot = new Vector2(1f, .5f);
        menuPanel.anchoredPosition = new Vector2(-24, 0);
        if (safeWidth < 760) { menuPanel.anchorMin = menuPanel.anchorMax = menuPanel.pivot = new Vector2(.5f, .5f); menuPanel.anchoredPosition = Vector2.zero; }
        if (wide) railKicker.gameObject.SetActive(false);
        bool compactText = safeHeight < 500;
        menuEyebrow.gameObject.SetActive(!compactText);
        menuSubtitle.gameObject.SetActive(!compactText);
        menuFooter.SetActive(safeHeight >= 440);
        Canvas.ForceUpdateCanvases();
        PositionToolbar(); PositionDesktop(); MovePointer(input.Pointer);
    }

    void SetRailStatusHeight(float value)
    {
        var element = railStatus.GetComponent<UnityEngine.UI.LayoutElement>();
        if (element == null) element = railStatus.AddComponent<UnityEngine.UI.LayoutElement>();
        element.minHeight = element.preferredHeight = value; element.flexibleHeight = 0;
    }

    void SetRailDetailVisible(bool visible)
    {
        railSubtitle.gameObject.SetActive(visible);
        railStatus.SetActive(visible);
        railMeta.SetActive(visible);
        railHotkey.SetActive(visible);
    }
    void PositionToolbar()
    {
        Rect rect = input.ToolbarRect; float scale = canvas.scaleFactor;
        toolbarPanel.anchoredPosition = new Vector2(rect.x / scale, -rect.y / scale);
        toolbarPanel.sizeDelta = rect.size / scale;
    }
    void PositionDesktop()
    {
        if (cafe.screens == null || cafe.screens.Length == 0) return;
        Rect rect = capture.DesktopRect; float scale = canvas.scaleFactor;
        desktopImage.rectTransform.anchoredPosition = new Vector2(rect.x / scale, -rect.y / scale);
        desktopImage.rectTransform.sizeDelta = rect.size / scale;
    }
    void MovePointer(Vector2 point)
    {
        UpdateToolbarHover(point,input.Controlling);
        // A RawImage without its texture draws an opaque default white quad.
        pointerImage.texture = input.CursorTexture;
        // Native control may resume from a real Windows app on another monitor.
        // Keep the on-screen software cursor visible until the next raw delta.
        pointerImage.rectTransform.anchoredPosition = new Vector2(
            Mathf.Clamp(point.x, 1, Mathf.Max(1, Screen.width - 2)),
            -Mathf.Clamp(point.y, 1, Mathf.Max(1, Screen.height - 2)));
        pointerImage.gameObject.SetActive(pointerImage.texture != null && input.DrawsPointer &&
            cachedEntryUi?.BlocksRoomMenu != true && (input.Controlling || Application.isFocused));
    }
    static readonly string[] ToolbarHoverNames={"ToolbarDrag","ToolbarObserve","ToolbarStand","ToolbarView","ToolbarMenu","ToolbarTab"};
    void UpdateToolbarHover(Vector2 point,bool native)
    {
        // Native hooks own clicks; forward only hover visuals using their exact hit rectangle.
        Rect rect=input.ToolbarRect;
        bool hot=native&&toolbarPanel.gameObject.activeInHierarchy&&cachedEntryUi?.BlocksRoomMenu!=true&&rect.Contains(point);
        int index=input.ToolbarCollapsed?5:Mathf.Clamp(Mathf.FloorToInt((point.x-rect.x)/Mathf.Max(1,rect.width)*5),0,4);
        for(int i=0;i<ToolbarHoverNames.Length;i++)
            if(buttons.TryGetValue(ToolbarHoverNames[i],out var action))
                action.GetComponent<CafeLoungeButton>()?.SetNativeHover(native,hot&&i==index);
    }
    bool applicationQuitting;
    void OnApplicationQuit() { applicationQuitting = true; }
    public void Refresh()
    {
        if (applicationQuitting || cafe == null) return;
        bool menu = cafe.Mode == CafePrototype.InputMode.Menu;
        bool modal = cachedEntryUi?.BlocksRoomMenu == true;
        bool entered = menu && !wasMenu; wasMenu = menu;
        menuRoot.SetActive(menu && !modal);
        bool desktopWaiting = cafe.Mode == CafePrototype.InputMode.Desktop && !capture.Ready;
        roomHud.SetActive(!modal && !menu &&
            (cafe.Mode != CafePrototype.InputMode.Desktop || desktopWaiting));
        toolbarPanel.gameObject.SetActive(!menu && cafe.IsComputerSeat && !cafe.LookingAround);
        toolbarExpanded.SetActive(!input.ToolbarCollapsed); toolbarTab.SetActive(input.ToolbarCollapsed);
        // RoomView is a real 3D monitor, with a depth-tested desktop surface,
        // physical bezel and visible surrounding room/players. A full-screen
        // RawImage would float in front of the model and hide nearby avatars.
        // Only immersive full-screen uses the source desktop as UI; the real
        // Windows taskbar remains part of its pixels, with no OS overlay hacks.
        desktopRoot.SetActive(!menu && !modal && cafe.DesktopFocused && !input.RoomView && capture.Ready);
        desktopImage.texture = capture.DesktopTexture;
        pointerImage.texture = input.CursorTexture; MovePointer(input.Pointer);
        // Native hooks own toolbar clicks only while desktop control is active.
        // Capture startup/failure still needs normal uGUI pointer dispatch.
        uiInput.enabled = menu || cafe.Mode == CafePrototype.InputMode.Observing ||
            cafe.Mode == CafePrototype.InputMode.Desktop && !input.Controlling;
        roomHint.text = desktopWaiting ?
            (capture.Active ? "正在自动投射本地电脑画面…" : "本地桌面未开启 · "+input.EscapeLabel+" 打开菜单重试") :
            !string.IsNullOrEmpty(cafe.ExitWarning) ? cafe.ExitWarning :
            cafe.CurrentSeat < 0 ? "WASD 移动 · Shift 跑步 · V 视角 · E 坐下 · Esc 菜单" :
            cafe.Mode == CafePrototype.InputMode.Observing ? "右键环顾 · E 起身 · Esc 菜单" : "Esc 菜单 · 默认 F8 脱离电脑";
        interactionHint.text = cafe.CurrentSeat < 0 && cafe.HasInteractionTarget ? cafe.InteractionHint : cafe.LookingAround ? "松开右键操作工具" : "";
        interactionHint.transform.parent.gameObject.SetActive(!string.IsNullOrEmpty(interactionHint.text));
        lobbyStatus.text = cafe.CurrentSeat < 0 ? "漫步中 · 尚未入座" : cafe.IsComputerSeat ? cafe.DesktopFocused ? "已入座 · 正在使用电脑" : "已入座 · 看房间" : "沙发上 · 休息中";
        Label("ToolbarObserve", cafe.Mode == CafePrototype.InputMode.Observing ? "回到电脑" : "看房间");
        Label("ToolbarView", input.RoomView ? "电脑全屏" : "房间视图"); Label("ToolbarMenu", "完整菜单 " + (cafe.Mode == CafePrototype.InputMode.Desktop ? input.EscapeLabel : "Esc"));
        Label("ToolbarTab", "菜单");
        Label("ContinueButton", cafe.CurrentSeat < 0 ? "回到客厅" : cafe.IsComputerSeat ? cafe.DesktopFocused ? "继续使用电脑" : "继续看房间" : "继续休息");
        buttons["ObserveButton"].gameObject.SetActive(cafe.IsComputerSeat);
        buttons["StandButton"].gameObject.SetActive(cafe.CurrentSeat >= 0);
        Label("ViewButton", input.RoomView ? "显示模式    非全屏模式（可看到周围场景）" : "显示模式    沉浸全屏");
        Label("LightButton", cafe.LampOn ? "客厅灯光    开" : "客厅灯光    关");
        Label("RebindButton", "返回网吧菜单    " + input.EscapeLabel + "    更改");
        Label("TextSizeButton", $"文字大小    {TextScale * 100:F0}%    点击调整");
        Label("MonitorButton", capture.MonitorLabel); buttons["MonitorButton"].interactable = !capture.Active;
        buttons["RefreshButton"].interactable = !capture.Active;
        Label("CaptureButton", capture.Active ? "停止桌面预览" : "开启桌面预览");
        if (buttons.TryGetValue("SaveScreenshotButton", out var screenshotButton))
        {
            Label("SaveScreenshotButton", screenshot.Capturing ? "正在保存截图…" : "保存游戏截图");
            // Disabling the selected button clears EventSystem.currentSelectedGameObject.
            // Capture() already ignores a second request while its coroutine is active;
            // keeping the button interactable preserves keyboard/gamepad focus.
            if (!screenshotButton.interactable) screenshotButton.interactable = true;
        }
        if (screenshotStatus != null) screenshotStatus.text = screenshot.Status;
        bool captureFailedAtSeat = cafe.IsComputerSeat && !capture.Active && !string.IsNullOrEmpty(capture.Error);
        if (buttons.TryGetValue("RetryCaptureButton", out var retryCapture))
            retryCapture.gameObject.SetActive(captureFailedAtSeat && !retryingDesktopCapture);
        if (menu && Page == 0)
            menuSubtitle.text = MainPageSubtitle();
        var entryUi = GetComponent<CafeEntryUi>();
        bool entryReady = entryUi != null && entryUi.Ready;
        bool inRoom = entryReady && entryUi.InNetworkRoom;
        if (buttons.TryGetValue("InviteRoomButton", out var invite)) invite.gameObject.SetActive(inRoom);
        if (buttons.TryGetValue("MembersRoomButton", out var members))
        {
            members.gameObject.SetActive(inRoom);
            if (inRoom) Label("MembersRoomButton", "房间成员 / 麦克风");
        }
        if (buttons.TryGetValue("LeaveRoomButton", out var leave)) leave.gameObject.SetActive(inRoom);
        if (buttons.TryGetValue("ReturnHomeButton", out var home)) home.gameObject.SetActive(entryReady && !inRoom);
        settingsHint.text = input.Error.Length > 0 ? input.Error : "返回键只在操作电脑时保留。修改冲突会保留原设置。";
        hotkeyHint.text = input.Error.Length > 0 ? input.Error : "按下 F1–F12，可组合 Ctrl / Alt / Shift。\n\n当前：" + input.EscapeLabel + "\n\nEsc 取消，原设置保留。";
        if (menu && Page == 1) captureStatus.text = capture.Status;
        PositionToolbar(); PositionDesktop();
        if (entered) { stack.Clear(); stack.Add(0); if (!modal) ShowPage(); }
        else if (menu && !modal) BuildFocusOrder();
        GetComponent<CafeLoungeHud>()?.RefreshMenuPresentation();
    }
    void OnApplicationFocus(bool focus) { if (input != null) MovePointer(input.Pointer); }
    void OnDestroy()
    {
        if (cafe != null) cafe.Changed -= Refresh;
        if (input != null) { input.Changed -= Refresh; input.PointerChanged -= MovePointer; }
        if (capture != null) capture.Changed -= Refresh;
        if (screenshot != null) screenshot.Changed -= Refresh;
    }
}

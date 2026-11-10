using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Existing menu buttons keep their original handlers. The dock only controls presentation.
[DefaultExecutionOrder(350)]
public sealed partial class CafeLoungeHud : MonoBehaviour
{
    CafePrototype cafe;
    CafeHud hud;
    CafeEntryUi entry;
    RectTransform tab;
    RectTransform root;
    TMP_Text status,roomName,roomCount,roomVisibility,dockLabel;
    bool expanded, wasMenu;
    Button dockButton;
    CafeDesktopCapture capture;
    string lastRoomIdentity;
    int lastMemberCount=-1;
    bool lastOnline;
#if NETCAFE_B1
    NetCafeNetworkSession network;
    CafeSpatialVoice voice;
#endif
    Vector2 normalized = new Vector2(1,0);
    public RectTransform Dock => tab;
    public bool Expanded => expanded;
    public void Configure(CafePrototype owner, CafeHud source, CafeEntryUi pages, TMP_FontAsset body, TMP_FontAsset heading)
    {
        cafe = owner; hud = source; entry = pages;
        capture=GetComponent<CafeDesktopCapture>();
#if NETCAFE_B1
        network=GetComponent<NetCafeNetworkSession>();voice=GetComponent<CafeSpatialVoice>();
#endif
        foreach (var text in hud.canvas.GetComponentsInChildren<TMP_Text>(true))
        {
            bool title = text == hud.menuTitle;
            text.font = title ? heading : body; text.fontStyle = FontStyles.Normal;
            text.color = text.transform.IsChildOf(hud.menuPanel) ? CafeUiTheme.Paper : new Color(.96f,.94f,.89f,.78f);
        }
        foreach (var button in hud.canvas.GetComponentsInChildren<Button>(true))
            if (button.GetComponent<CafeLoungeButton>() == null) button.gameObject.AddComponent<CafeLoungeButton>().Configure(false, true);
        foreach (var image in hud.menuRoot.GetComponentsInChildren<Image>(true))
        {
            if (image.GetComponent<Button>() != null || image.GetComponentInParent<Button>() != null) continue;
            // A stencil viewport needs opaque alpha even when showMaskGraphic=false.
            if (image.GetComponent<Mask>() != null) { image.color = Color.white; continue; }
            image.color = image.transform == hud.menuPanel ? new Color(.12f,.11f,.09f,.86f) : Color.clear;
            image.raycastTarget = image.transform == hud.menuPanel;
        }
        foreach (var outline in hud.menuRoot.GetComponentsInChildren<Outline>(true)) outline.enabled = false;
        // Restyle the legacy hint and toolbar surfaces with the same quiet HUD palette.
        foreach(var image in hud.roomHud.GetComponentsInChildren<Image>(true)) {image.color=Color.clear;image.raycastTarget=false;}
        var hint=(RectTransform)hud.roomHint.transform.parent;
        if(hint.GetComponent<Image>()!=null){var image=hint.GetComponent<Image>();image.sprite=null;image.type=Image.Type.Simple;image.color=new Color(.157f,.149f,.118f,.77f);}
        hint.anchoredPosition=new Vector2(4,-28);hint.sizeDelta=new Vector2(420,34);
        hud.roomHint.color=new Color(1,.953f,.863f);hud.roomHint.fontSize=13;
        hud.toolbarPanel.GetComponent<Image>().color=Color.clear;
        var toolTab=hud.Button("ToolbarTab");
        var toolLabel=toolTab.GetComponentInChildren<TMP_Text>();
        // The frozen scene nests the label in a padded layout; take it out so
        // that layout cannot shrink the collapsed label back to an empty sliver.
        toolLabel.transform.SetParent(toolTab.transform,false);
        toolLabel.rectTransform.anchorMin=Vector2.zero;toolLabel.rectTransform.anchorMax=Vector2.one;
        toolLabel.text="菜单";toolLabel.font=heading;toolLabel.fontSize=13;toolLabel.enableAutoSizing=true;toolLabel.fontSizeMin=11;toolLabel.fontSizeMax=13;
        toolLabel.textWrappingMode=TextWrappingModes.NoWrap;toolLabel.alignment=TextAlignmentOptions.MidlineLeft;
        toolLabel.rectTransform.offsetMin=new Vector2(36,0);toolLabel.rectTransform.offsetMax=new Vector2(-8,0);
        var toolIcon=AddIcon(toolTab.transform,CafeLoungeIcon.Shape.More,Vector2.zero,new Vector2(16,16));
        var toolIconRect=(RectTransform)toolIcon.transform;toolIconRect.anchorMin=toolIconRect.anchorMax=new Vector2(0,.5f);toolIconRect.pivot=new Vector2(0,.5f);toolIconRect.anchoredPosition=new Vector2(12,0);
        // The simple menu shares the room action buttons' component, palette,
        // typeface and 44-unit height. Keep five equal hit cells for native input.
        var toolLayout=hud.toolbarExpanded.GetComponent<HorizontalLayoutGroup>();
        if(toolLayout!=null){toolLayout.padding=new RectOffset();toolLayout.spacing=0;toolLayout.childControlWidth=true;toolLayout.childForceExpandWidth=true;}
        foreach(var button in hud.toolbarExpanded.GetComponentsInChildren<Button>(true))
        {
            var toolActionLabel=button.GetComponentInChildren<TMP_Text>();toolActionLabel.transform.SetParent(button.transform,false);
            toolActionLabel.font=heading;toolActionLabel.fontStyle=FontStyles.Normal;toolActionLabel.fontSize=13;toolActionLabel.enableAutoSizing=true;toolActionLabel.fontSizeMin=10;toolActionLabel.fontSizeMax=13;
            toolActionLabel.alignment=TextAlignmentOptions.Center;toolActionLabel.textWrappingMode=TextWrappingModes.NoWrap;
            toolActionLabel.rectTransform.anchorMin=Vector2.zero;toolActionLabel.rectTransform.anchorMax=Vector2.one;toolActionLabel.rectTransform.offsetMin=new Vector2(8,0);toolActionLabel.rectTransform.offsetMax=new Vector2(-8,0);
            var element=button.GetComponent<LayoutElement>();if(element!=null){element.minWidth=element.preferredWidth=0;element.flexibleWidth=1;}
            var surface=button.GetComponentInChildren<CafeLoungeGraphic>();
            if(surface!=null){surface.rectTransform.offsetMin=new Vector2(3,0);surface.rectTransform.offsetMax=new Vector2(-3,0);}
        }
        hud.menuTitle.fontSize = 28;
        hud.menuSubtitle.fontSize = 15;
        hud.menuContentLayout.padding = new RectOffset(22,22,20,16);
        root = new GameObject("QuietRoomChrome", typeof(RectTransform)).GetComponent<RectTransform>();
        root.SetParent(hud.safeRoot, false); root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one; root.offsetMin = root.offsetMax = Vector2.zero;
        // Kept under roomHud so the existing screenshot owner also hides this chrome.
        root.SetParent(hud.safeRoot,false);
        root.anchorMin=new Vector2(0,1);root.anchorMax=Vector2.one;root.pivot=new Vector2(.5f,1);root.sizeDelta=new Vector2(0,44);root.anchoredPosition=Vector2.zero;
        root.gameObject.AddComponent<Image>().color=new Color(.098f,.102f,.082f,.48f);root.GetComponent<Image>().raycastTarget=false;
        status = CreateText(root,"RoomStatus",heading,10);status.text="LIBRARY LOUNGE";status.characterSpacing=1.4f;
        var sr=status.rectTransform;Place(sr,28,4,170,36);
        status.alignment = TextAlignmentOptions.Left;
        roomName=CreateText(root,"RoomName",heading,13);Place(roomName.rectTransform,200,4,330,36);roomName.alignment=TextAlignmentOptions.MidlineLeft;roomName.richText=false;roomName.textWrappingMode=TextWrappingModes.NoWrap;roomName.overflowMode=TextOverflowModes.Ellipsis;
        roomCount=CreateText(root,"RoomCount",heading,11);Place(roomCount.rectTransform,540,4,80,36);roomCount.alignment=TextAlignmentOptions.MidlineLeft;
        roomVisibility=CreateText(root,"RoomVisibility",body,10);var vr=roomVisibility.rectTransform;vr.anchorMin=vr.anchorMax=vr.pivot=new Vector2(1,.5f);vr.anchoredPosition=new Vector2(-28,0);vr.sizeDelta=new Vector2(180,36);roomVisibility.alignment=TextAlignmentOptions.MidlineRight;roomVisibility.color=new Color(.918f,.847f,.718f);
        tab = new GameObject("RoomMenuDock",typeof(RectTransform),typeof(Image),typeof(Button),typeof(CafeLoungeDock)).GetComponent<RectTransform>();
        // Tab belongs to the canvas rather than roomHud: remain usable while the menu is open.
        tab.SetParent(hud.safeRoot,false); tab.sizeDelta = new Vector2(137,44);
        tab.GetComponent<Image>().color = Color.clear;
        var label = CreateText(tab,"Label",heading,13);dockLabel=label; label.text = "展开菜单     Esc"; label.alignment = TextAlignmentOptions.Center;
        label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one; label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
        tab.GetComponent<CafeLoungeDock>().Configure(this);
        tab.gameObject.AddComponent<CafeLoungeButton>().Configure(false,true);
        dockButton=tab.GetComponent<Button>();dockButton.onClick.AddListener(Toggle);
        BuildPanels(body, heading);
        BuildMorePanel(body,heading);
        cafe.Changed+=SyncMenuState;
        var stand = hud.Button("StandButton").GetComponentInChildren<TMP_Text>(); if (stand != null) stand.text = "离开座位";
    }
    static TMP_Text CreateText(Transform parent,string name,TMP_FontAsset font,int size)
    {
        var go=new GameObject(name,typeof(RectTransform),typeof(TextMeshProUGUI));go.transform.SetParent(parent,false);
        var text=go.GetComponent<TMP_Text>();text.font=font;text.fontSize=size;text.color=CafeUiTheme.Paper;text.raycastTarget=false;return text;
    }
    public void Toggle()
    {
        if (entry.BlocksRoomMenu) return;
        if (cafe.Mode != CafePrototype.InputMode.Menu) { expanded=true;more=false;openedAt=Time.unscaledTime;cafe.OpenMenu(); }
        else { expanded=!expanded; more=false;openedAt=Time.unscaledTime; if(!expanded)cafe.Continue(); }
    }
    public void MoveDock(Vector2 local, bool snap)
    {
        Rect safe=DockBounds;
        local.x=Mathf.Clamp(local.x,safe.xMin,safe.xMax);
        local.y=Mathf.Clamp(local.y,safe.yMin,safe.yMax);
        if(snap)
        {
            float l=local.x-safe.xMin,r=safe.xMax-local.x,b=local.y-safe.yMin,t=safe.yMax-local.y;
            float min=Mathf.Min(l,r,b,t);
            if(min==l)local.x=safe.xMin;else if(min==r)local.x=safe.xMax;
            else if(min==b)local.y=safe.yMin;else local.y=safe.yMax;
        }
        var destination=new Vector2(Mathf.InverseLerp(safe.xMin,safe.xMax,local.x),Mathf.InverseLerp(safe.yMin,safe.yMax,local.y));
        UiTween.Kill(tab);
        if(snap){var from=normalized;UiTween.Drive(tab,"snap",.26f,t=>normalized=Vector2.Lerp(from,destination,CafeWebMotion.Out(t)));}
        else normalized=destination;
        tab.anchorMin=tab.anchorMax=tab.pivot=new Vector2(.5f,.5f);
        if(!snap)tab.anchoredPosition=local;
    }
    Rect DockBounds {get {var safe=hud.safeRoot.rect;float x=Mathf.Min(16+tab.rect.width*.5f,safe.width*.5f),bottom=Mathf.Min(16+tab.rect.height*.5f,safe.height*.5f),top=Mathf.Min(56+tab.rect.height*.5f,safe.height-bottom);return UnityEngine.Rect.MinMaxRect(safe.xMin+x,safe.yMin+bottom,safe.xMax-x,safe.yMax-top);}}
    // Page changes must update visibility before a following click/focus lookup,
    // including changes dispatched after LateUpdate by the EventSystem.
    public void RefreshMenuPresentation(){if(morePanel!=null)LateUpdate();}
    void LateUpdate()
    {
        if(hud==null || tab==null)return;
        bool modal=entry.BlocksRoomMenu;
        bool menu=cafe.Mode==CafePrototype.InputMode.Menu;
        SyncMenuState();
        // Paper pages own the whole screen. Hide once, then leave room layout/text alone.
        if(modal)
        {
            tab.gameObject.SetActive(false);hud.menuRoot.SetActive(false);root.gameObject.SetActive(false);
            UpdateMorePanel(false);UpdatePanels(false);
            hud.menuFooter.SetActive(false);hud.menuEyebrow.gameObject.SetActive(false);
            hud.roomHint.transform.parent.gameObject.SetActive(false);hud.lobbyStatus.gameObject.SetActive(false);
            return;
        }
        tab.gameObject.SetActive(!modal && (menu || cafe.Mode==CafePrototype.InputMode.Observing));
        // Walking uses the locked camera pointer. Esc brings up the same real menu.
        dockButton.interactable=menu || cafe.Mode==CafePrototype.InputMode.Observing;
        hud.menuRoot.SetActive(menu&&!modal&&expanded&&more&&hud.Page!=0);
        UpdateMorePanel(menu&&!modal&&expanded&&more&&hud.Page==0);
        Rect safe=hud.safeRoot.rect;
        Rect bounds=DockBounds;
        Vector2 position=new Vector2(Mathf.Lerp(bounds.xMin,bounds.xMax,normalized.x),Mathf.Lerp(bounds.yMin,bounds.yMax,normalized.y));
        tab.anchorMin=tab.anchorMax=tab.pivot=new Vector2(.5f,.5f);tab.anchoredPosition=position;
        if(menu&&!modal)
        {
            float w=Mathf.Min(368,safe.width-32),h=Mathf.Min(620,safe.height-100);
            hud.menuPanel.anchorMin=hud.menuPanel.anchorMax=hud.menuPanel.pivot=new Vector2(.5f,.5f);
            hud.menuPanel.sizeDelta=new Vector2(w,h);
            Vector2 p=position+new Vector2(position.x>safe.center.x?-w*.5f-65:w*.5f+65,0);
            p.x=Mathf.Clamp(p.x,safe.xMin+w*.5f+12,safe.xMax-w*.5f-12);
            p.y=Mathf.Clamp(p.y,safe.yMin+h*.5f+12,safe.yMax-h*.5f-12);
            hud.menuPanel.anchoredPosition=p;
        }
        root.gameObject.SetActive(!modal && cafe.Mode!=CafePrototype.InputMode.Desktop);
        UpdatePanels(menu&&!modal&&expanded&&!more);
        hud.menuFooter.SetActive(false);
        if(hud.Page==0)hud.menuTitle.text="房间菜单";
        bool compact=safe.width<650;status.gameObject.SetActive(!compact);
        Place(roomName.rectTransform,compact?16:200,4,Mathf.Max(80,Mathf.Min(330,safe.width-(compact?280:580))),36);
        if(lastRoomIdentity!=entry.RoomIdentity){lastRoomIdentity=entry.RoomIdentity;roomName.text=lastRoomIdentity+"的房间";}
        Place(roomCount.rectTransform,roomName.rectTransform.anchoredPosition.x+roomName.rectTransform.rect.width+18,4,70,36);
#if NETCAFE_B1
        bool online=entry.InNetworkRoom;int count=online?network.MemberCount:1;
        if(count!=lastMemberCount||online!=lastOnline){lastMemberCount=count;roomCount.text=online?count+" / 6":"1 / 1";}
#endif
        if(string.IsNullOrEmpty(roomVisibility.text)||lastOnline!=entry.InNetworkRoom)roomVisibility.text=entry.InNetworkRoom?"▪  私人房间":"▪  单人体验";
        lastOnline=entry.InNetworkRoom;
        dockLabel.text=expanded?"收起菜单     Esc":"展开菜单     Esc";
        hud.menuEyebrow.gameObject.SetActive(false);
        hud.menuSubtitle.gameObject.SetActive(hud.Page!=0 || !string.IsNullOrEmpty(capture.Error));
        hud.roomHint.transform.parent.gameObject.SetActive(!modal && !menu && (cafe.Mode==CafePrototype.InputMode.Desktop || !string.IsNullOrEmpty(cafe.ExitWarning)));
        hud.lobbyStatus.gameObject.SetActive(false);
    }
    void SyncMenuState()
    {
        bool menu=cafe.Mode==CafePrototype.InputMode.Menu;
        if(menu&&!wasMenu&&!entry.BlocksRoomMenu){expanded=true;more=false;openedAt=Time.unscaledTime;}
        if(!menu)expanded=false;
        wasMenu=menu;
    }
    void OnDestroy(){if(cafe!=null)cafe.Changed-=SyncMenuState;}
}

public sealed class CafeLoungeDock : MonoBehaviour, IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    CafeLoungeHud owner;
    Vector2 pointerOffset;
    bool dragging;
    public void Configure(CafeLoungeHud source){owner=source;}
    public void OnInitializePotentialDrag(PointerEventData e){e.useDragThreshold=true;}
    public void OnBeginDrag(PointerEventData e)
    {
        if(e.button!=PointerEventData.InputButton.Left)return;
        dragging=true;RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform.parent,e.position,e.pressEventCamera,out var local);
        pointerOffset=((RectTransform)transform).anchoredPosition-local;
    }
    public void OnDrag(PointerEventData e)
    {
        if(!dragging)return;
        e.eligibleForClick=false;
        RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform.parent,e.position,e.pressEventCamera,out var local);
        owner.MoveDock(local+pointerOffset,false);
    }
    public void OnEndDrag(PointerEventData e)
    {
        if(!dragging)return;dragging=false;e.eligibleForClick=false;
        RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform.parent,e.position,e.pressEventCamera,out var local);
        owner.MoveDock(local+pointerOffset,true);
    }
    void OnDisable(){dragging=false;}
}

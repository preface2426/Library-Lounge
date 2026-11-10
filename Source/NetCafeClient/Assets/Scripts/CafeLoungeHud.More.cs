using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class CafeLoungeHud
{
    RectTransform morePanel,moreContent;
    TMP_Text moreStatus;
    ScrollRect moreScroll;
    Button moreCinema;
    TMP_Text moreCinemaLabel;
    CafeScreenshot screenshot;
    RectTransform closeMore;
    LayoutElement statusSize;
    readonly Dictionary<Button,(TMP_Text original,TMP_Text visible)> moreLabels=new();
    readonly Dictionary<string,(Button original,Button visible)> moreButtons=new();
    public GameObject MorePanelRoot=>morePanel!=null?morePanel.gameObject:null;
    public Button VisibleMenuButton(string name)=>morePanel!=null&&morePanel.gameObject.activeInHierarchy&&hud.Page==0&&moreButtons.TryGetValue(name,out var row)&&row.visible.gameObject.activeInHierarchy?row.visible:null;
    void BuildMorePanel(TMP_FontAsset body,TMP_FontAsset heading)
    {
        morePanel=Rect("QuietRoomMore",hud.safeRoot);
        var surface=morePanel.gameObject.AddComponent<CafeLoungeGraphic>();surface.Radius=0;
        surface.Fill=new Color(.157f,.149f,.118f,.9f);surface.Stroke=new Color(.78f,.67f,.46f,.35f);
        var title=CreateText(morePanel,"Heading",heading,21);title.text="房间菜单";Place(title.rectTransform,20,12,220,42);
        var close=PanelButton(morePanel,"CloseMore","返回",()=>CloseMore(),body,false);Place((RectTransform)close.transform,254,14,64,40);
        closeMore=(RectTransform)close.transform;screenshot=GetComponent<CafeScreenshot>();
        close.GetComponentInChildren<TMP_Text>().fontSize=13;
        var viewport=Rect("Viewport",morePanel);viewport.anchorMin=Vector2.zero;viewport.anchorMax=Vector2.one;viewport.offsetMin=new Vector2(16,14);viewport.offsetMax=new Vector2(-16,-62);
        viewport.gameObject.AddComponent<RectMask2D>();
        moreContent=Rect("Links",viewport);moreContent.anchorMin=new Vector2(0,1);moreContent.anchorMax=Vector2.one;moreContent.pivot=new Vector2(.5f,1);moreContent.sizeDelta=Vector2.zero;
        var layout=moreContent.gameObject.AddComponent<VerticalLayoutGroup>();layout.spacing=3;layout.childControlWidth=layout.childControlHeight=true;layout.childForceExpandWidth=true;layout.childForceExpandHeight=false;
        moreContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;
        var scroll=viewport.gameObject.AddComponent<ScrollRect>();scroll.viewport=viewport;scroll.content=moreContent;
        CafeUiScroll.Configure(scroll);
        moreScroll=scroll;
        moreCinema=PanelButton(moreContent,"CinemaFocusButton","专注观看",cafe.ToggleCinemaFocus,body,true);
        moreCinema.GetComponent<CafeLoungeButton>().UseLinkStyle();moreCinema.gameObject.AddComponent<CafeUiFocus>();
        moreCinema.GetComponentInChildren<TMP_Text>().fontSize=17;
        moreCinemaLabel=moreCinema.GetComponentInChildren<TMP_Text>();
        var cinemaSize=moreCinema.gameObject.AddComponent<LayoutElement>();cinemaSize.minHeight=cinemaSize.preferredHeight=44;
        moreCinema.gameObject.SetActive(false);
        foreach(string name in new[]{"ContinueButton","ObserveButton","StandButton","SettingsButton","HelpButton","RetryCaptureButton","SaveScreenshotButton","LeaveRoomButton","ReturnHomeButton","QuitButton"})
        {
            // Capture the existing production handler once. Source state remains authoritative.
            var original=hud.Button(name);
            var button=PanelButton(moreContent,name,"",()=>{if(original.gameObject.activeSelf&&original.interactable)original.onClick.Invoke();},body,true);
            button.GetComponent<CafeLoungeButton>().UseLinkStyle();button.gameObject.AddComponent<CafeUiFocus>();
            button.GetComponentInChildren<TMP_Text>().fontSize=17;
            var element=button.gameObject.AddComponent<LayoutElement>();element.minHeight=element.preferredHeight=44;
            moreButtons.Add(name,(original,button));
            moreLabels.Add(button,(original.GetComponentInChildren<TMP_Text>(),button.GetComponentInChildren<TMP_Text>()));
        }
        var recover=PanelButton(moreContent,"ReturnToSpawnButton","返回初始位置",cafe.ReturnToRoomSpawn,body,true);
        recover.transform.SetSiblingIndex(2);
        recover.GetComponent<CafeLoungeButton>().UseLinkStyle();recover.gameObject.AddComponent<CafeUiFocus>();
        recover.GetComponentInChildren<TMP_Text>().fontSize=17;
        var recoverSize=recover.gameObject.AddComponent<LayoutElement>();recoverSize.minHeight=recoverSize.preferredHeight=44;
        moreStatus=CreateText(moreContent,"ActionStatus",body,12);moreStatus.richText=false;
        moreStatus.color=new Color(.918f,.847f,.718f);statusSize=moreStatus.gameObject.AddComponent<LayoutElement>();moreStatus.gameObject.SetActive(false);
        morePanel.gameObject.SetActive(false);
    }
    void OpenMore()
    {
        more=true;hud.Refresh();UpdateMorePanel(true);
        var target=VisibleMenuButton("ContinueButton");if(target!=null)EventSystem.current.SetSelectedGameObject(target.gameObject);
    }
    public bool CloseMore()
    {
        if(!more||hud.Page!=0)return false;
        more=false;morePanel.gameObject.SetActive(false);UpdatePanels(true);
        foreach(var button in actionButtons)if(button.name=="HudMoreButton")EventSystem.current.SetSelectedGameObject(button.gameObject);
        return true;
    }
    void UpdateMorePanel(bool show)
    {
        if(morePanel==null)return;
        bool opening=show&&!morePanel.gameObject.activeSelf;
        morePanel.gameObject.SetActive(show);if(!show)return;
        moreCinema.gameObject.SetActive(cafe.IsCinemaSeat);
        moreCinemaLabel.text=cafe.CinemaFocused?"回到座位视角":"专注观看";
        int count=cafe.IsCinemaSeat?2:1;foreach(var pair in moreButtons.Values)if(pair.original.gameObject.activeSelf)count++;
        Rect safe=hud.safeRoot.rect;float width=Mathf.Min(334,safe.width-32),height=Mathf.Min(76+count*47,safe.height-100);
        morePanel.anchorMin=morePanel.anchorMax=morePanel.pivot=new Vector2(.5f,.5f);morePanel.sizeDelta=new Vector2(width,height);
        Vector2 position=tab.anchoredPosition;position.x+=position.x>safe.center.x?-width*.5f-80:width*.5f+80;
        position.x=Mathf.Clamp(position.x,safe.xMin+width*.5f+12,safe.xMax-width*.5f-12);
        position.y=Mathf.Clamp(position.y,safe.yMin+height*.5f+12,safe.yMax-height*.5f-56);morePanel.anchoredPosition=position;
        Place(closeMore,width-80,14,64,40);
        foreach(var pair in moreButtons.Values){pair.visible.gameObject.SetActive(pair.original.gameObject.activeSelf);pair.visible.interactable=pair.original.interactable;var labels=moreLabels[pair.visible];labels.visible.text=labels.original.text;}
        string feedback=screenshot!=null&&(screenshot.Capturing||screenshot.LastCaptureSucceeded||screenshot.Status.StartsWith("截图保存失败"))?screenshot.Status:
            cafe.IsComputerSeat&&!capture.Active&&!string.IsNullOrEmpty(capture.Error)?"桌面投射失败，可在设置中查看原因。":"";
        bool changed=moreStatus.text!=feedback;moreStatus.text=feedback;moreStatus.gameObject.SetActive(feedback.Length>0);
        if(feedback.Length>0)statusSize.minHeight=statusSize.preferredHeight=Mathf.Clamp(moreStatus.GetPreferredValues(feedback,width-32,0).y+16,38,180);
        if(opening){Canvas.ForceUpdateCanvases();moreContent.anchoredPosition=Vector2.zero;}
        else if(changed&&screenshot!=null&&(screenshot.Capturing||screenshot.LastCaptureSucceeded||screenshot.Status.StartsWith("截图保存失败"))){Canvas.ForceUpdateCanvases();moreScroll.verticalNormalizedPosition=0;}
        var selected=EventSystem.current.currentSelectedGameObject;
        if(selected==null||!selected.activeInHierarchy){var target=VisibleMenuButton("ContinueButton");if(target!=null)EventSystem.current.SetSelectedGameObject(target.gameObject);}
    }
}

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class CafeLoungeHud
{
    RectTransform lightRoot,memberPanel,actionPanel,invitePanel;
    TMP_Text memberLabel,memberState,memberHeading,inviteLabel,micLabel,memberMicLabel;
    Button micButton,memberButton,inviteButton,copyButton,actionMemberButton,memberMicButton,cinemaButton;
    CafeLoungeIcon copyIcon;
    Button[] actionButtons;
    RectTransform[] panels;
    readonly Vector2[] centers=new Vector2[3];
    TMP_Text cinemaLabel,copyFeedback,shareLabel;
    int lastPanelMemberCount=-1;
    bool lastPanelOnline;
    readonly List<CanvasGroup> panelGroups=new();
    readonly float[] panelProgress=new float[3],panelFrom=new float[3];
    bool more,panelsOpen;
    float openedAt,panelChangedAt,copyFeedbackUntil;
    public bool MoreOpen=>more;
    public GameObject ChromeRoot=>root!=null?root.gameObject:null;
    public Transform LightRoot=>lightRoot;
    public GameObject PanelsRoot=>lightRoot!=null?lightRoot.gameObject:null;
    void BuildPanels(TMP_FontAsset body,TMP_FontAsset heading)
    {
        lightRoot=Rect("QuietHudPanels",hud.safeRoot);
        lightRoot.anchorMin=Vector2.zero;lightRoot.anchorMax=Vector2.one;lightRoot.offsetMin=lightRoot.offsetMax=Vector2.zero;
        memberPanel=Panel("HudMembers",new Vector2(216,92));
        memberButton=PanelButton(memberPanel,"HudMembersButton","房间成员",entry.OpenMembers,heading,false);
        Place((RectTransform)memberButton.transform,0,0,216,38);
        memberHeading=memberButton.GetComponentInChildren<TMP_Text>();memberHeading.fontSize=12;memberHeading.characterSpacing=1;
        AddIcon(memberButton.transform,CafeLoungeIcon.Shape.Arrow,new Vector2(187,-11),new Vector2(16,16));
        var rule=Rect("Rule",memberPanel);Place(rule,13,38,190,1);rule.gameObject.AddComponent<Image>().color=new Color(.78f,.67f,.46f,.35f);rule.GetComponent<Image>().raycastTarget=false;
        memberLabel=CreateText(memberPanel,"Player",body,12);memberLabel.richText=false;Place(memberLabel.rectTransform,13,46,90,34);memberLabel.alignment=TextAlignmentOptions.MidlineLeft;memberLabel.textWrappingMode=TextWrappingModes.NoWrap;memberLabel.overflowMode=TextOverflowModes.Ellipsis;
        memberState=CreateText(memberPanel,"State",body,10);Place(memberState.rectTransform,102,46,44,34);memberState.alignment=TextAlignmentOptions.MidlineRight;memberState.color=new Color(.918f,.847f,.718f);
        var memberMic=PanelButton(memberPanel,"HudMemberMicButton","麦克风关",entry.ToggleRoomMicrophone,body,false);Place((RectTransform)memberMic.transform,158,46,48,34);
        memberMicButton=memberMic;
        memberMicLabel=memberMic.GetComponentInChildren<TMP_Text>();memberMicLabel.fontSize=10;memberMicLabel.fontStyle=FontStyles.Underline;memberMicLabel.rectTransform.offsetMin=memberMicLabel.rectTransform.offsetMax=Vector2.zero;
        actionPanel=Panel("HudActions",new Vector2(188,194),false);
        actionMemberButton=ActionButton("HudOpenMembersButton","成员",entry.OpenMembers,CafeLoungeIcon.Shape.Members,heading);
        inviteButton=ActionButton("HudInviteButton","邀请",entry.OpenInvite,CafeLoungeIcon.Shape.Invite,heading);
        micButton=ActionButton("HudMicButton","开麦",entry.ToggleRoomMicrophone,CafeLoungeIcon.Shape.Microphone,heading);micLabel=micButton.GetComponentInChildren<TMP_Text>();
        shareLabel=ActionButton("HudShareScreenButton","屏幕共享",entry.OpenScreenSharing,CafeLoungeIcon.Shape.Screen,heading).GetComponentInChildren<TMP_Text>();
        cinemaButton=ActionButton("HudCinemaFocusButton","专注观看",cafe.ToggleCinemaFocus,CafeLoungeIcon.Shape.Arrow,heading);
        ActionButton("HudMoreButton","更多",OpenMore,CafeLoungeIcon.Shape.More,heading);
        invitePanel=Panel("HudInvite",new Vector2(188,80));
        var caption=CreateText(invitePanel,"Caption",heading,11);caption.text="邀请码";caption.color=new Color(.918f,.847f,.718f);Place(caption.rectTransform,12,9,150,20);
        inviteLabel=CreateText(invitePanel,"Code",heading,17);inviteLabel.characterSpacing=2;inviteLabel.richText=false;inviteLabel.enableAutoSizing=true;inviteLabel.fontSizeMin=11;inviteLabel.fontSizeMax=17;inviteLabel.textWrappingMode=TextWrappingModes.NoWrap;Place(inviteLabel.rectTransform,12,29,124,38);
        copyButton=PanelButton(invitePanel,"HudCopyButton","",()=>{
            try {GUIUtility.systemCopyBuffer=entry.RoomCode;copyButton.GetComponentInChildren<TMP_Text>().text="已复制";}
            catch(Exception){copyButton.GetComponentInChildren<TMP_Text>().text="失败";}
            copyFeedbackUntil=Time.unscaledTime+1.5f;
        },body,false);Place((RectTransform)copyButton.transform,142,29,34,36);
        var feedback=copyButton.GetComponentInChildren<TMP_Text>();feedback.fontSize=10;feedback.rectTransform.offsetMin=feedback.rectTransform.offsetMax=Vector2.zero;feedback.alignment=TextAlignmentOptions.Center;
        copyIcon=AddIcon(copyButton.transform,CafeLoungeIcon.Shape.Copy,new Vector2(9,-10),new Vector2(15,15));
        actionButtons=actionPanel.GetComponentsInChildren<Button>(true);
        panels=new[]{memberPanel,actionPanel,invitePanel};
        cinemaLabel=cinemaButton.GetComponentInChildren<TMP_Text>();copyFeedback=feedback;
    }
    RectTransform Rect(string name,Transform parent)
    {
        var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);return (RectTransform)go.transform;
    }
    static void Place(RectTransform rt,float x,float y,float width,float height)
    {rt.anchorMin=rt.anchorMax=rt.pivot=new Vector2(0,1);rt.anchoredPosition=new Vector2(x,-y);rt.sizeDelta=new Vector2(width,height);}
    RectTransform Panel(string name,Vector2 size,bool background=true)
    {
        var rt=Rect(name,lightRoot);rt.anchorMin=rt.anchorMax=rt.pivot=new Vector2(.5f,.5f);rt.sizeDelta=size;
        if(background){var surface=rt.gameObject.AddComponent<CafeLoungeGraphic>();surface.Radius=0;surface.Fill=new Color(.157f,.149f,.118f,.77f);surface.Stroke=new Color(.78f,.67f,.46f,.35f);surface.raycastTarget=false;}
        var group=rt.gameObject.AddComponent<CanvasGroup>();panelGroups.Add(group);return rt;
    }
    CafeLoungeIcon AddIcon(Transform holder,CafeLoungeIcon.Shape shape,Vector2 position,Vector2 size)
    {
        var rt=Rect("Icon_"+shape,holder);rt.anchorMin=rt.anchorMax=rt.pivot=new Vector2(0,1);rt.anchoredPosition=position;rt.sizeDelta=size;
        var icon=rt.gameObject.AddComponent<CafeLoungeIcon>();icon.Icon=shape;icon.color=new Color(.918f,.847f,.718f);icon.raycastTarget=false;return icon;
    }
    Button ActionButton(string name,string text,Action action,CafeLoungeIcon.Shape shape,TMP_FontAsset font)
    {
        var button=PanelButton(actionPanel,name,text,action,font,true);var label=button.GetComponentInChildren<TMP_Text>();label.rectTransform.offsetMin=new Vector2(42,0);label.fontSize=13;label.characterSpacing=1;
        AddIcon(button.transform,shape,new Vector2(13,-14),new Vector2(16,16));
        var arrow=AddIcon(button.transform,CafeLoungeIcon.Shape.Arrow,Vector2.zero,new Vector2(16,16));var rt=(RectTransform)arrow.transform;rt.anchorMin=rt.anchorMax=rt.pivot=new Vector2(1,.5f);rt.anchoredPosition=new Vector2(-13,0);
        return button;
    }
    Button PanelButton(Transform holder,string name,string text,Action action,TMP_FontAsset font,bool surface=true)
    {
        var rt=Rect(name,holder);rt.gameObject.AddComponent<Image>().color=Color.clear;
        var button=rt.gameObject.AddComponent<Button>();var label=CreateText(rt,"Label",font,13);label.text=text;label.alignment=TextAlignmentOptions.MidlineLeft;
        label.rectTransform.anchorMin=Vector2.zero;label.rectTransform.anchorMax=Vector2.one;label.rectTransform.offsetMin=new Vector2(13,0);label.rectTransform.offsetMax=new Vector2(-13,0);
        if(surface)rt.gameObject.AddComponent<CafeLoungeButton>().Configure(false,true);
        button.onClick.AddListener(()=>action());return button;
    }
    Vector2 AvoidDock(Vector2 center,Vector2 size,Rect safe)
    {
        var d=tab.anchoredPosition;float dx=tab.rect.width*.5f+12,dy=tab.rect.height*.5f+10;
        if(Mathf.Abs(center.x-d.x)<size.x*.5f+dx&&Mathf.Abs(center.y-d.y)<size.y*.5f+dy)center.y=d.y>safe.center.y?d.y-size.y*.5f-dy:d.y+size.y*.5f+dy;
        center.y=Mathf.Clamp(center.y,safe.yMin+size.y*.5f+12,safe.yMax-size.y*.5f-56);
        if(Mathf.Abs(center.x-d.x)<size.x*.5f+dx&&Mathf.Abs(center.y-d.y)<size.y*.5f+dy)center.x=d.x>safe.center.x?d.x-size.x*.5f-dx:d.x+size.x*.5f+dx;
        center.x=Mathf.Clamp(center.x,safe.xMin+size.x*.5f+12,safe.xMax-size.x*.5f-12);return center;
    }
    void UpdatePanels(bool show)
    {
        if(lightRoot==null)return;
        float now=Time.unscaledTime;
        if(show!=panelsOpen){panelsOpen=show;panelChangedAt=now;for(int i=0;i<3;i++)panelFrom[i]=panelProgress[i];}
        bool immediate=PlayerPrefs.GetInt("CafeReduceMotion",0)!=0||entry.BlocksRoomMenu;
        bool visible=show||!immediate&&now-panelChangedAt<.18f;
        lightRoot.gameObject.SetActive(visible);if(!visible)return;
        Rect safe=hud.safeRoot.rect;bool narrow=safe.width<650,online=entry.InNetworkRoom;
        memberPanel.sizeDelta=new Vector2(216,92);
        invitePanel.sizeDelta=new Vector2(Mathf.Min(188,safe.width-48),80);
        float actionWidth=Mathf.Min(188,(safe.width-48)*.5f);int count=0;
        foreach(var button in actionButtons)
        {
            bool active=button==cinemaButton?cafe.IsCinemaSeat:online||button.name=="HudMoreButton"||button.name=="HudOpenMembersButton";
            button.gameObject.SetActive(active);if(active){Place((RectTransform)button.transform,0,count*50,actionWidth,44);count++;}
        }
        actionPanel.sizeDelta=new Vector2(actionWidth,count*50-6);
        cinemaLabel.text=cafe.CinemaFocused?"回到座位视角":"专注观看";
        shareLabel.text=entry.ScreenShareMenuLabel;
        centers[0]=AvoidDock(new Vector2(safe.xMin+28+memberPanel.sizeDelta.x*.5f,safe.yMin+24+46),memberPanel.sizeDelta,safe);
        centers[1]=AvoidDock(new Vector2(safe.xMax-28-actionWidth*.5f,safe.yMin+80+actionPanel.sizeDelta.y*.5f),actionPanel.sizeDelta,safe);
        centers[2]=AvoidDock(new Vector2(safe.xMax-28-invitePanel.sizeDelta.x*.5f,safe.yMax-44-23-40),invitePanel.sizeDelta,safe);
        bool Overlaps(int a,Vector2 aSize,int b,Vector2 bSize)=>new UnityEngine.Rect(centers[a]-aSize*.5f,aSize).Overlaps(new UnityEngine.Rect(centers[b]-bSize*.5f,bSize));
        // Optional summaries yield to the real action buttons when space or the dragged dock conflicts.
        bool membersVisible=safe.height>=350&&!narrow&&!Overlaps(0,memberPanel.sizeDelta,1,actionPanel.sizeDelta);
        bool inviteVisible=online&&safe.height>=500&&!Overlaps(2,invitePanel.sizeDelta,1,actionPanel.sizeDelta)&&(!membersVisible||!Overlaps(2,invitePanel.sizeDelta,0,memberPanel.sizeDelta));
        invitePanel.gameObject.SetActive(inviteVisible);memberPanel.gameObject.SetActive(membersVisible);
        memberButton.interactable=actionMemberButton.interactable=true;
        inviteButton.interactable=copyButton.interactable=online;
#if NETCAFE_B1
        micButton.interactable=online&&voice!=null&&!voice.MicrophoneBusy;
        memberMicButton.interactable=micButton.interactable;
        memberMicButton.gameObject.SetActive(online);
        micLabel.text=voice!=null&&voice.MicrophoneBusy?"申请中":voice!=null&&voice.MicrophoneActive?"静音":"开麦";
        memberMicLabel.text=voice!=null&&voice.MicrophoneActive?"麦克风开":"麦克风关";
        int memberCount=online?network.MemberCount:1;
        if(lastPanelMemberCount!=memberCount||lastPanelOnline!=online){lastPanelMemberCount=memberCount;lastPanelOnline=online;memberHeading.text="房间成员     "+(online?memberCount+" / 6":"1 / 1");}
#endif
        memberLabel.text=entry.RoomIdentity;memberState.text=cafe.CurrentSeat<0?"未入座":"已入座";inviteLabel.text=online?entry.RoomCode:"";
        bool feedback=now<copyFeedbackUntil;copyIcon.gameObject.SetActive(!feedback);if(!feedback)copyFeedback.text="";
        for(int i=0;i<3;i++)
        {
            float t=immediate?1:CafeWebMotion.Out(Mathf.Clamp01((now-panelChangedAt-(show?i*.065f:0))/(show?.36f:.18f)));
            float p=Mathf.Lerp(panelFrom[i],show?1:0,t);panelProgress[i]=p;panelGroups[i].alpha=p;
            panelGroups[i].blocksRaycasts=panelGroups[i].interactable=show&&p>=.9f;
            panels[i].anchoredPosition=centers[i]+(1-p)*(i==0?new Vector2(-14,0):i==1?new Vector2(14,0):new Vector2(0,9));
        }
        var events=EventSystem.current;
        if(events==null)return;
        var selected=events.currentSelectedGameObject;
        if(show&&(selected==null||!selected.activeInHierarchy))foreach(var button in actionButtons)if(button.gameObject.activeInHierarchy){events.SetSelectedGameObject(button.gameObject);break;}
    }
}

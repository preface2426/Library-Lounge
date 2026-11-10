#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Opt-in acceptance fixture. Uses visible production Button listeners, not substitute handlers.
public sealed class CafeUiAcceptance : MonoBehaviour
{
    CafeEntryUi entry;
    CafeHud hud;
    CafePrototype cafe;
    string folder;
    int checks;
    bool failed;
    int savedMotion;
    IEnumerator Start()
    {
        entry=GetComponent<CafeEntryUi>();hud=GetComponent<CafeHud>();cafe=GetComponent<CafePrototype>();
        folder=Path.Combine(Application.dataPath,"../Evidence/UI-v012");Directory.CreateDirectory(folder);
        Screen.SetResolution(1600,1000,FullScreenMode.Windowed);
        yield return new WaitForSecondsRealtime(1);
        savedMotion=PlayerPrefs.GetInt("CafeReduceMotion",0);
        try {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-uiGuest")>=0) yield return Guest();
            else yield return Run();
        } finally {PlayerPrefs.SetInt("CafeReduceMotion",savedMotion);PlayerPrefs.Save();}
        Debug.Log("V012_UI_ACCEPTANCE_"+(failed?"FAIL":"PASS")+" checks="+checks);
        Application.Quit(failed?1:0);
    }
    void Check(bool value,string name)
    {
        checks++;if(!value){failed=true;Debug.LogError("V012_UI_FAIL "+name);}else Debug.Log("V012_UI_PASS "+name);
    }
    IEnumerator Click(string label)
    {
        var buttons=entry.ScreenshotOverlayRoot.GetComponentsInChildren<Button>().Where(b=>b.IsInteractable()).ToArray();
        var button=buttons.FirstOrDefault(b=>b.name=="Action_"+label);
        Check(button!=null,"visible button "+label);
        if(button==null)yield break;
        yield return PointerClick(button);
    }
    IEnumerator HudClick(string name)
    {
        var button=hud.Button(name);Check(button.gameObject.activeInHierarchy&&button.IsInteractable(),"visible HUD "+name);
        if(button.gameObject.activeInHierarchy&&button.IsInteractable())yield return PointerClick(button);
    }
    IEnumerator LoungeClick(string name)
    {
        var button=GetComponent<CafeLoungeHud>().LightRoot.GetComponentsInChildren<Button>().FirstOrDefault(x=>x.name==name);
        Check(button!=null&&button.IsInteractable(),"visible lounge "+name);
        if(button!=null&&button.IsInteractable())yield return PointerClick(button);
    }
    IEnumerator PointerClick(Button button)
    {
        Canvas.ForceUpdateCanvases();
        EventSystem.current.SetSelectedGameObject(button.gameObject);
        button.GetComponent<CafeUiFocus>()?.Reveal();
        Canvas.ForceUpdateCanvases();
        // ScrollRect and stencil clipping settle on the following frame, as with a real pointer.
        yield return new WaitForEndOfFrame();
        var rt=(RectTransform)button.transform;
        var point=RectTransformUtility.WorldToScreenPoint(null,rt.TransformPoint(rt.rect.center));
        var e=new PointerEventData(EventSystem.current){position=point,button=PointerEventData.InputButton.Left};
        var hits=new System.Collections.Generic.List<RaycastResult>();EventSystem.current.RaycastAll(e,hits);
        if(hits.Count==0 || hits[0].gameObject.GetComponentInParent<Button>()!=button) Debug.Log("V012_RAY_DIAG target="+button.name+" at="+point+" hits="+string.Join(",",hits.Take(6).Select(x=>x.gameObject.name)));
        Check(hits.Count>0&&hits[0].gameObject.GetComponentInParent<Button>()==button,"pointer ray reaches "+button.name);
        if(hits.Count>0&&hits[0].gameObject.GetComponentInParent<Button>()==button)
            ExecuteEvents.Execute(button.gameObject,e,ExecuteEvents.pointerClickHandler);
    }
    string Page => typeof(CafeEntryUi).GetField("page",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(entry).ToString();
    void Set(string field,object value)=>typeof(CafeEntryUi).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(entry,value);
    IEnumerator Shot(string name)
    {
        yield return new WaitForEndOfFrame();
        var image=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.Combine(folder,(Array.IndexOf(Environment.GetCommandLineArgs(),"-uiGuest")>=0?"guest-":"")+name+".png"),image.EncodeToPNG());Destroy(image);
    }
    [Serializable] sealed class JoinReady { public string url,code,runId; }
    [Serializable] sealed class GuestResult { public bool passed; public int checks; public string runId; }
    bool CurrentGuestResult(string runId)
    {
        string file=Path.Combine(folder,"guest-result.json");
        if(!File.Exists(file))return false;
        try { var value=JsonUtility.FromJson<GuestResult>(File.ReadAllText(file));return value!=null&&value.passed&&value.runId==runId; }
        catch(IOException){return false;}catch(ArgumentException){return false;}
    }
    IEnumerator Guest()
    {
#if NETCAFE_B1
        var ready=JsonUtility.FromJson<JoinReady>(File.ReadAllText(Path.Combine(folder,"join-ready.json")));
        yield return Click("进入大厅   ↗");yield return null;
        Set("inviteInput",ready.code);yield return Click("加入房间   ↗");yield return null;
        Set("serviceInput",ready.url);Set("nickname","验收成员");yield return Click("确认加入");yield return null;
        var session=GetComponent<NetCafeNetworkSession>();float deadline=Time.realtimeSinceStartup+35;
        while(session.State!=NetCafeNetworkSession.Phase.Connected&&session.State!=NetCafeNetworkSession.Phase.Failed&&Time.realtimeSinceStartup<deadline)yield return null;
        Check(session.State==NetCafeNetworkSession.Phase.Connected,"guest joins actual host through button");
        yield return new WaitForSecondsRealtime(3.2f);
        if(session.State==NetCafeNetworkSession.Phase.Connected)
        {
            entry.OpenMembers();yield return null;Check(Page=="Members","guest member page");
            yield return Shot("09-guest-members");
            entry.AskLeave();yield return null;yield return Click("确认离开房间");yield return new WaitForSecondsRealtime(3.2f);
            deadline=Time.realtimeSinceStartup+15;while(session.State!=NetCafeNetworkSession.Phase.Disconnected&&Time.realtimeSinceStartup<deadline)yield return null;
            Check(session.State==NetCafeNetworkSession.Phase.Disconnected,"guest leave actual session");
        }
        File.WriteAllText(Path.Combine(folder,"guest-result.json"),JsonUtility.ToJson(new GuestResult{passed=!failed,checks=checks,runId=ready.runId}));
#endif
        yield break;
    }
    IEnumerator Run()
    {
        hud.SetTextScale(1);PlayerPrefs.SetInt("CafeReduceMotion",0);
        Check(Page=="Home"&&entry.BlocksRoomMenu,"startup home and input release");yield return Shot("01-home");
        yield return Click("进入大厅   ↗");yield return null;Check(Page=="Lobby","home to lobby");yield return Shot("02-lobby");
        Screen.SetResolution(760,900,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.6f);
        Check(entry.ScreenshotOverlayRoot.GetComponentsInChildren<VerticalLayoutGroup>().Any(x=>x.name=="CreateJoinColumns"),"narrow stacked columns");yield return Shot("02-lobby-narrow");
        Screen.SetResolution(1600,1000,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.6f);
        Check(entry.ScreenshotOverlayRoot.GetComponentsInChildren<HorizontalLayoutGroup>().Any(x=>x.name=="CreateJoinColumns"),"wide horizontal columns restored");
        yield return Click("加入房间   ↗");yield return null;Check(Page=="Lobby","empty invite stays lobby");
        Set("inviteInput","ABC123");
        yield return Click("加入房间   ↗");yield return null;Check(Page=="Join","join confirmation form");
        Set("nickname","");yield return Click("确认加入");yield return null;Check(Page=="Join","empty nickname blocked");Set("nickname","验收玩家");
        yield return Click("返回大厅");yield return null;
        yield return Click("我的形象   ↗");yield return null;Check(Page=="Avatar","avatar page");
        var avatar=entry.ScreenshotOverlayRoot.GetComponentsInChildren<Button>().FirstOrDefault(b=>b.name.EndsWith("柯基"));
        Check(avatar!=null,"avatar Corgi action exists");if(avatar!=null)yield return PointerClick(avatar);Check(CafeAvatarLibrary.SelectedId==CafeAvatarLibrary.Ids[2],"actual avatar selection");
        yield return Click("确认形象并返回");yield return null;Check(Page=="Lobby","avatar returns origin");
        yield return Click("设置   ↗");yield return null;yield return Click("界面动效   完整");yield return null;Check(PlayerPrefs.GetInt("CafeReduceMotion",0)==1,"reduce motion preference");yield return Click("界面动效   减少");yield return null;
        float before=hud.TextScale;yield return Click("放大界面文字");yield return null;Check(hud.TextScale!=before,"text scale owner changed");hud.SetTextScale(1);yield return new WaitForSecondsRealtime(.2f);
        yield return Click("返回大厅");yield return null;
        yield return Click("使用说明   ↗");yield return null;Check(Page=="Help","help page");yield return Click("返回大厅");yield return null;
        yield return Click("单人体验");yield return new WaitForSecondsRealtime(.5f);Check(entry.TransitionActive,"entry transition active");yield return Shot("03-transition");
        yield return new WaitForSecondsRealtime(2.8f);Check(!entry.BlocksRoomMenu&&cafe.Mode==CafePrototype.InputMode.Walking,"solo real room");yield return Shot("04-room");
        cafe.OpenMenu();yield return null;yield return Shot("05-room-menu");
        var lounge=GetComponent<CafeLoungeHud>();Check(lounge!=null&&lounge.Expanded,"menu expand");
        lounge.MoveDock(new Vector2(-1000,100),true);yield return new WaitForSecondsRealtime(.3f);Check(lounge.Dock.anchoredPosition.x<0,"dock left snap");
        lounge.MoveDock(new Vector2(1000,100),true);yield return new WaitForSecondsRealtime(.3f);Check(lounge.Dock.anchoredPosition.x>0,"dock right snap");
        yield return new WaitForSecondsRealtime(.5f);yield return PointerClick(lounge.Dock.GetComponent<Button>());yield return null;
        Check(cafe.Mode==CafePrototype.InputMode.Walking,"light HUD continue actual mode");cafe.OpenMenu();yield return new WaitForSecondsRealtime(.5f);
        yield return LoungeClick("HudMoreButton");yield return null;
        yield return HudClick("SettingsButton");yield return null;Check(hud.Page==1,"room settings");
        bool lamp=cafe.LampOn;yield return HudClick("LightButton");Check(cafe.LampOn!=lamp,"real lamp owner");yield return HudClick("LightButton");
        var desktopInput=GetComponent<CafeDesktopInput>();bool oldView=desktopInput.RoomView;
        yield return HudClick("ViewButton");yield return null;Check(desktopInput.RoomView!=oldView,"display owner changed");yield return HudClick("ViewButton");
        yield return HudClick("ResetKeyButton");Check(desktopInput.EscapeLabel.Contains("F8"),"reset hotkey actual value");
        yield return HudClick("RebindButton");yield return null;Check(hud.Page==3,"hotkey page");yield return HudClick("BackButton");yield return null;
        yield return HudClick("TextSizeButton");yield return null;Check(hud.TextScale>1,"room text size");hud.SetTextScale(1);yield return new WaitForSecondsRealtime(.2f);
        yield return HudClick("RefreshButton");yield return null;int previousMonitor=GetComponent<CafeDesktopCapture>().MonitorIndex;yield return HudClick("MonitorButton");yield return null;
        Check(GetComponent<CafeDesktopCapture>().MonitorIndex>=0,"monitor owner has valid selection");
        yield return HudClick("BackButton");yield return null;yield return HudClick("HelpButton");yield return null;Check(hud.Page==2,"room help");yield return HudClick("BackButton");yield return null;
        var screenshot=GetComponent<CafeScreenshot>();screenshot.SetOutputDirectory(folder);yield return HudClick("SaveScreenshotButton");
        yield return new WaitForSecondsRealtime(1);Check(screenshot.LastCaptureSucceeded&&File.Exists(screenshot.LastSavedPath),"actual screenshot file");
        yield return HudClick("ContinueButton");yield return null;Check(cafe.Mode==CafePrototype.InputMode.Walking,"continue actual mode");
        cafe.Sit(0);yield return new WaitForSecondsRealtime(.4f);Check(cafe.CurrentSeat==0,"scene seat selects computer without UI picker");
        var capture=GetComponent<CafeDesktopCapture>();float captureDeadline=Time.realtimeSinceStartup+10;
        while(capture.Active&&!capture.Ready&&Time.realtimeSinceStartup<captureDeadline)yield return null;
        Check(capture.Ready || !string.IsNullOrEmpty(capture.Error),"mapping returns frame or visible capture error");
        if(cafe.Mode==CafePrototype.InputMode.Desktop)
        {
            yield return HudClick("ToolbarObserve");yield return null;Check(cafe.Mode==CafePrototype.InputMode.Observing,"toolbar observe actual mode");
            bool view=desktopInput.RoomView;yield return HudClick("ToolbarView");yield return null;Check(desktopInput.RoomView!=view,"toolbar view actual state");yield return HudClick("ToolbarView");
            if(desktopInput.ToolbarCollapsed){yield return null;yield return HudClick("ToolbarTab");yield return null;}
            yield return HudClick("ToolbarMenu");yield return null;Check(cafe.Mode==CafePrototype.InputMode.Menu,"toolbar real menu");
            yield return new WaitForSecondsRealtime(.5f);yield return LoungeClick("HudMoreButton");yield return null;
            yield return HudClick("StandButton");yield return null;Check(cafe.CurrentSeat<0,"stand actual scene state");
        }
        else if(cafe.CurrentSeat>=0)cafe.Stand();
        cafe.OpenMenu();yield return null;
        if(!lounge.MoreOpen){yield return new WaitForSecondsRealtime(.5f);yield return LoungeClick("HudMoreButton");yield return null;}
        yield return HudClick("ReturnHomeButton");yield return new WaitForSecondsRealtime(3.1f);Check(Page=="Lobby"&&entry.BlocksRoomMenu,"solo leave transition to lobby");
#if NETCAFE_B1
        Set("serviceInput","http://127.0.0.1:8787");
        yield return Click("创建房间   ↗");yield return null;Check(Page=="Create","create form");
        var host=GetComponent<CafeLocalHostRuntime>();float deadline=Time.realtimeSinceStartup+45;
        while(host.Starting&&Time.realtimeSinceStartup<deadline)yield return null;
        Check(host.Ready,"owned local service ready");
        if(!host.Ready){Debug.Log("V012_HOST_DIAGNOSTIC "+typeof(CafeEntryUi).GetField("message",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(entry));yield break;}
        yield return new WaitForSecondsRealtime(.15f);
        yield return Click("确认创建");yield return null;
        var session=GetComponent<NetCafeNetworkSession>();deadline=Time.realtimeSinceStartup+40;
        while(session.State!=NetCafeNetworkSession.Phase.Connected&&session.State!=NetCafeNetworkSession.Phase.Failed&&Time.realtimeSinceStartup<deadline)yield return null;
        yield return null;Check(session.State==NetCafeNetworkSession.Phase.Connected,"actual room created through button");
        if(session.State!=NetCafeNetworkSession.Phase.Connected)yield break;
        Check(Page=="Invite","invite after create");
        var ready=new JoinReady{url=host.LocalUrl,code=session.InviteCode,runId=Guid.NewGuid().ToString("N")};
        File.WriteAllText(Path.Combine(folder,"join-ready.json"),JsonUtility.ToJson(ready));
        deadline=Time.realtimeSinceStartup+55;
        while(!CurrentGuestResult(ready.runId)&&Time.realtimeSinceStartup<deadline)yield return null;
        Check(CurrentGuestResult(ready.runId),"second client completed real join and leave");yield return Click("复制邀请码");yield return null;Check(GUIUtility.systemCopyBuffer==session.InviteCode,"clipboard contains real code");
        yield return Shot("06-invite");yield return Click("进入房间");yield return new WaitForSecondsRealtime(3.1f);
        cafe.OpenMenu();yield return new WaitForSecondsRealtime(.5f);
        yield return Shot("06-room-hud-online");
        hud.SetTextScale(2);Screen.SetResolution(1600,780,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.5f);
        foreach(var point in new[]{new Vector2(10000,0),new Vector2(-10000,125),new Vector2(0,10000),new Vector2(0,-10000)}) {
            lounge.MoveDock(point,true);yield return new WaitForSecondsRealtime(.3f);
            var panels=lounge.LightRoot.GetComponentsInChildren<CanvasGroup>().Where(x=>x.gameObject.activeInHierarchy).Select(x=>(RectTransform)x.transform).Concat(new[]{lounge.Dock}).ToArray();
            bool separated=true;
            for(int a=0;a<panels.Length;a++)for(int b=a+1;b<panels.Length;b++) {
                var ac=new Vector3[4];var bc=new Vector3[4];panels[a].GetWorldCorners(ac);panels[b].GetWorldCorners(bc);
                if(Rect.MinMaxRect(ac[0].x,ac[0].y,ac[2].x,ac[2].y).Overlaps(Rect.MinMaxRect(bc[0].x,bc[0].y,bc[2].x,bc[2].y)))separated=false;
            }
            Check(separated,"short 200 percent panels and dock do not overlap at "+point);
        }
        yield return Shot("06-room-hud-short-200");
        hud.SetTextScale(1);Screen.SetResolution(1600,1000,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.5f);
        yield return LoungeClick("HudCopyButton");Check(GUIUtility.systemCopyBuffer==session.InviteCode,"light HUD copy real code");
        yield return LoungeClick("HudInviteButton");yield return null;Check(Page=="Invite","light HUD invite");yield return Click("回到房间");yield return null;
        cafe.OpenMenu();yield return new WaitForSecondsRealtime(.5f);yield return LoungeClick("HudMembersButton");yield return null;
        Check(Page=="Members","real members page");yield return Shot("07-members");
        Check(entry.ScreenshotOverlayRoot.GetComponentsInChildren<TMP_Text>().Any(x=>x.text=="电脑"),"read only member columns");
        yield return Click("我的形象   ↗");yield return null;yield return Click("确认形象并返回");yield return null;Check(Page=="Members","member avatar returns members");
        yield return Click("设置   ↗");yield return null;yield return Click("返回成员");yield return null;Check(Page=="Members","member settings returns members");
        yield return Click("使用说明   ↗");yield return null;yield return Click("返回成员");yield return null;Check(Page=="Members","member help returns members");
        yield return Click("刷新成员");yield return null;
        yield return Click("语音与屏幕");yield return null;
        var voice=GetComponent<CafeSpatialVoice>();bool hearing=voice.HearOthers;
        yield return Click(hearing?"静音其他玩家":"恢复房间声音");yield return null;Check(voice.HearOthers!=hearing,"playback owner toggled");
        yield return Click(voice.HearOthers?"静音其他玩家":"恢复房间声音");yield return null;
        yield return Click("主动共享整个电脑屏幕（房间成员可见）");yield return null;
        Check(!GetComponent<CafeDesktopSharing>().Sharing&&GetComponent<CafeDesktopSharing>().Status.Contains("请先"),"unseated sharing blocked with visible reason");
        // No device is opened silently: mic remains muted until user consent.
        Check(!GetComponent<CafeSpatialVoice>().MicrophoneActive,"default muted actual voice");
        yield return Click("返回成员");yield return null;yield return Click("离开房间");yield return null;Check(Page=="ConfirmLeave","host destructive confirmation");
        yield return Click("取消");yield return null;Check(!entry.BlocksRoomMenu,"leave cancel returns room");
        cafe.OpenMenu();yield return new WaitForSecondsRealtime(.5f);yield return LoungeClick("HudMoreButton");yield return null;
        yield return HudClick("QuitButton");yield return null;Check(Page=="ConfirmLeave","quit host shows consequence confirmation");yield return Click("取消");yield return null;
        cafe.OpenMenu();yield return new WaitForSecondsRealtime(.5f);if(!lounge.MoreOpen){yield return LoungeClick("HudMoreButton");yield return null;}
        yield return HudClick("LeaveRoomButton");yield return null;yield return Click("确认关闭房间");yield return new WaitForSecondsRealtime(3.2f);
        deadline=Time.realtimeSinceStartup+20;while(session.State!=NetCafeNetworkSession.Phase.Disconnected&&Time.realtimeSinceStartup<deadline)yield return null;
        Check(session.State==NetCafeNetworkSession.Phase.Disconnected&&!host.Ready,"leave cleans network and owned processes");
#endif
        yield return Shot("08-return");
    }
}
#endif

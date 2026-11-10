#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

public sealed class CafeUiFixAcceptance : MonoBehaviour
{
    CafePrototype cafe; CafeHud hud; CafeEntryUi entry;
    int checks,failures; string folder;
    int savedMotion;float savedScale;
    static bool Flag(string name)=>Array.IndexOf(Environment.GetCommandLineArgs(),name)>=0;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Attach()
    {
        if(!Flag("-uiFixBaseline")&&!Flag("-uiFixAcceptance")&&!Flag("-uiFixQuit")&&!Flag("-uiFixLobbyQuit"))return;
        var owner=FindFirstObjectByType<CafePrototype>();
        if(owner!=null)owner.gameObject.AddComponent<CafeUiFixAcceptance>();
    }
    void Check(bool value,string name)
    {
        checks++;if(!value)failures++;
        Debug.Log((value?"UIFIX_PASS ":"UIFIX_FAIL ")+name);
    }
    void Call(object owner,string name,params object[] args)=>owner.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,args.Select(x=>x.GetType()).ToArray(),null).Invoke(owner,args);
    IEnumerator Start()
    {
        cafe=GetComponent<CafePrototype>();hud=GetComponent<CafeHud>();entry=GetComponent<CafeEntryUi>();
        folder=Path.Combine(Application.dataPath,"../Evidence/UIFix");Directory.CreateDirectory(folder);
        if(!Application.isEditor)Screen.SetResolution(1600,1000,FullScreenMode.Windowed);
        savedMotion=PlayerPrefs.GetInt("CafeReduceMotion",0);savedScale=hud.TextScale;
        yield return new WaitForSecondsRealtime(1);
        while(!entry.Ready)yield return null;
        PlayerPrefs.SetInt("CafeReduceMotion",1);hud.SetTextScale(1);
        if(Flag("-uiFixQuit")||Flag("-uiFixLobbyQuit"))
        {
            if(Flag("-uiFixLobbyQuit")){var lobby=entry.ScreenshotOverlayRoot.GetComponentsInChildren<Button>().First(b=>b.name.StartsWith("Action_进入大厅"));yield return Click(lobby);}
            var quit=entry.ScreenshotOverlayRoot.GetComponentsInChildren<Button>().FirstOrDefault(b=>b.name=="Action_退出应用"&&(Flag("-uiFixLobbyQuit")||b.GetComponentInChildren<TMP_Text>().fontSize>=16));
            Check(quit!=null,"prominent home quit exists");
            if(quit!=null){PlayerPrefs.SetInt("CafeReduceMotion",savedMotion);hud.SetTextScale(savedScale);PlayerPrefs.Save();yield return Click(quit);yield return new WaitForSecondsRealtime(3);Check(false,"quit exits process");}
        }
        else yield return Run();
        PlayerPrefs.SetInt("CafeReduceMotion",savedMotion);hud.SetTextScale(savedScale);PlayerPrefs.Save();
        Debug.Log("UIFIX_ACCEPTANCE_"+(failures==0?"PASS":"FAIL")+" checks="+checks+" failures="+failures);
        File.WriteAllText(Path.Combine(folder,Flag("-uiFixBaseline")?"baseline-result.json":"result.json"),"{\"checks\":"+checks+",\"failures\":"+failures+"}");
#if UNITY_EDITOR
        if(Application.isEditor){UnityEditor.EditorApplication.isPlaying=false;UnityEditor.EditorApplication.Exit(failures==0?0:1);yield break;}
#endif
        Application.Quit(failures==0?0:1);
    }
    IEnumerator Click(Button button)
    {
        Canvas.ForceUpdateCanvases();if(Application.isEditor)yield return null;else yield return new WaitForEndOfFrame();
        var rt=(RectTransform)button.transform;var p=RectTransformUtility.WorldToScreenPoint(null,rt.TransformPoint(rt.rect.center));
        var data=new PointerEventData(EventSystem.current){position=p};var hits=new System.Collections.Generic.List<RaycastResult>();EventSystem.current.RaycastAll(data,hits);
        Check(hits.Count>0&&hits[0].gameObject.GetComponentInParent<Button>()==button,"unobstructed pointer "+button.name);
        // Feed the normal input module; direct listener calls would hide a disabled module.
        var mouse=Mouse.current??InputSystem.AddDevice<Mouse>();
        InputSystem.QueueStateEvent(mouse,new MouseState {position=p});yield return null;
        InputSystem.QueueStateEvent(mouse,new MouseState {position=p}.WithButton(MouseButton.Left));yield return null;
        InputSystem.QueueStateEvent(mouse,new MouseState {position=p});yield return null;yield return null;
    }
    IEnumerator Shot(string name)
    {
        if(Application.isEditor)yield break;
        yield return new WaitForEndOfFrame();var image=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.Combine(folder,name+".png"),image.EncodeToPNG());Destroy(image);
    }
    IEnumerator Run()
    {
        yield return CafeLoopbackHttpAcceptance.Run(Check);
        var homeQuit=entry.ScreenshotOverlayRoot.GetComponentsInChildren<Button>().FirstOrDefault(b=>b.name=="Action_退出应用"&&(Flag("-uiFixLobbyQuit")||b.GetComponentInChildren<TMP_Text>().fontSize>=16));
        Check(homeQuit!=null,"home quit visible beside primary actions");
        yield return Shot("home");
        var enter=entry.ScreenshotOverlayRoot.GetComponentsInChildren<Button>().First(b=>b.name.StartsWith("Action_进入大厅"));
        if(Application.isEditor)ExecuteEvents.Execute(enter.gameObject,new PointerEventData(EventSystem.current),ExecuteEvents.pointerClickHandler);
        else yield return Click(enter);
        yield return null;
        var identity=entry.ScreenshotOverlayRoot.GetComponentsInChildren<Transform>(true).First(t=>t.name=="Identity");
        Check(!identity.GetComponentsInChildren<TMP_Text>().Any(t=>t.text.Contains("未入座")),"identity omits seat status");
        Check(identity.GetComponentsInChildren<RawImage>().Any(t=>t.texture!=null)||identity.GetComponentsInChildren<Image>().Any(t=>t.sprite!=null),"selected character portrait loaded");
        var mic=identity.GetComponentsInChildren<Button>().FirstOrDefault(b=>b.GetComponentInChildren<TMP_Text>()?.text.Contains("麦克风")==true);
        Check(mic!=null&&mic.IsInteractable(),"lobby microphone can be changed before room");
        if(mic!=null)
        {
            yield return Click(mic);
            Check((bool)typeof(CafeEntryUi).GetField("microphoneOnEntry",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(entry),"lobby click opts in to microphone on entry");
            Check(!GetComponent<CafeSpatialVoice>().MicrophoneActive,"prejoin choice does not open a device");
            yield return Click(mic);
            Check(!(bool)typeof(CafeEntryUi).GetField("microphoneOnEntry",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(entry),"second lobby click cancels microphone opt in");
        }
        string savedAvatar=CafeAvatarLibrary.SelectedId;
        var portrait=identity.GetComponentsInChildren<RawImage>().Single();
        for(int i=0;i<CafeAvatarLibrary.Ids.Length;i++)
        {
            CafeAvatarLibrary.Select(CafeAvatarLibrary.Ids[i]);Call(entry,"UpdateLoungeIdentity");
            Check(portrait.texture!=null&&Mathf.Abs(portrait.uvRect.x-i%5/5f)<.0001f&&Mathf.Abs(portrait.uvRect.y-(1-i/5)/2f)<.0001f,"portrait follows selection "+CafeAvatarLibrary.Ids[i]);
        }
        CafeAvatarLibrary.Select(savedAvatar);Call(entry,"UpdateLoungeIdentity");
        yield return Shot("lobby");
        Screen.SetResolution(760,900,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.4f);
        Check(portrait.gameObject.activeInHierarchy&&mic.gameObject.activeInHierarchy,"narrow lobby keeps avatar and microphone available");yield return Shot("lobby-narrow");
        Screen.SetResolution(1600,1000,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.4f);
        PlayerPrefs.SetInt("CafeReduceMotion",0);
        var create=entry.ScreenshotOverlayRoot.GetComponentsInChildren<Button>().First(b=>b.name.StartsWith("Action_创建房间"));
        var mouse=Mouse.current??InputSystem.AddDevice<Mouse>();InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(1500,100)});yield return new WaitForSecondsRealtime(.75f);
        var surface=create.GetComponentInChildren<CafeLoungeGraphic>();Check(surface.Reveal<.01f&&surface.Radius>25,"primary button returns to rounded outline");
        var cr=(RectTransform)create.transform;InputSystem.QueueStateEvent(mouse,new MouseState{position=RectTransformUtility.WorldToScreenPoint(null,cr.TransformPoint(cr.rect.center))});
        yield return new WaitForSecondsRealtime(.1f);Check(surface.Reveal>0&&surface.Reveal<1,"primary fill animates through intermediate geometry");yield return Shot("button-hover-mid");
        yield return new WaitForSecondsRealtime(.7f);Check(surface.Reveal>.99f&&surface.Radius<3,"primary hover reaches square dark fill");yield return Shot("button-hover-end");
        Call(entry,"EnterSingle");yield return new WaitForSecondsRealtime(.7f);
        Check(entry.TransitionActive,"animated entry runs actual room transition");yield return Shot("key-inserting");
        var transition=GetComponent<CafeRoomTransition>();var outline=transition.GetComponentsInChildren<CafeKeyGraphic>().First(x=>x.OutlineOnly);
        using(var vertices=new VertexHelper()){Call(outline,"OnPopulateMesh",vertices);Check(vertices.currentVertCount>0,"key and strip outlines render without a scene texture");}
        yield return new WaitForSecondsRealtime(1.05f);yield return Shot("key-aligned");
        for(int strip=0;strip<8;strip++)Check(Mathf.Abs(CafeKeyGraphic.Contact(strip,0))<.001f,"strip joins key without clearance gap "+strip);
        yield return new WaitForSecondsRealtime(.5f);yield return Shot("key-reveal");
        yield return new WaitForSecondsRealtime(.8f);Check(!entry.TransitionActive&&cafe.Mode==CafePrototype.InputMode.Walking,"entry reveal leaves real walking destination");
        entry.ReturnHome();yield return new WaitForSecondsRealtime(.1f);
        Check(entry.TransitionActive&&transition.Leaving,"room return plays reverse transition");
        Check(transition.GetComponentInChildren<CanvasGroup>().alpha>.99f,"reverse transition keeps outer screen opaque at start");yield return Shot("key-extracting-start");
        transition.Finish();Check(!entry.TransitionActive&&entry.BlocksRoomMenu,"skip exit keeps lobby destination");
        PlayerPrefs.SetInt("CafeReduceMotion",1);Call(entry,"EnterSingle");yield return null;
        cafe.Sit(0);yield return new WaitForSecondsRealtime(.2f);
        var capture=GetComponent<CafeDesktopCapture>();capture.StopCapture();
        Call(cafe,"Transition",CafePrototype.InputMode.Desktop);hud.Refresh();yield return null;
        Check(!GetComponent<CafeDesktopInput>().Controlling,"desktop fallback has no native input owner");
        Check(hud.uiInput.enabled,"desktop fallback keeps Unity input module enabled");
        var hintImage=hud.roomHint.transform.parent.GetComponent<Image>();
        Check(hintImage==null||hintImage.color.a<.05f||hintImage.color.grayscale<.4f,"desktop hint has no legacy paper rectangle");
        hud.pointerImage.texture=null;Call(hud,"MovePointer",Vector2.zero);
        Check(!hud.pointerImage.gameObject.activeInHierarchy||hud.pointerImage.texture!=null,"software pointer never draws missing-texture rectangle");
        yield return Shot("desktop-fallback");
        // v0.1.3 seats intentionally start with the compact menu. Open its visible
        // entry through normal pointer input before testing a command inside it.
        if(GetComponent<CafeDesktopInput>().ToolbarCollapsed)
        {
            yield return Click(hud.Button("ToolbarTab"));
            Check(!GetComponent<CafeDesktopInput>().ToolbarCollapsed,"visible compact menu opens toolbar commands");
        }
        yield return Click(hud.Button("ToolbarObserve"));
        Check(cafe.Mode==CafePrototype.InputMode.Observing,"fallback toolbar click actually observes room");
        cafe.OpenMenu();yield return new WaitForSecondsRealtime(.6f);
        var lounge=GetComponent<CafeLoungeHud>();
        Check(!hud.toolbarPanel.gameObject.activeInHierarchy,"computer toolbar hidden while room menu open");
        Check(!lounge.LightRoot.GetComponentsInChildren<Button>().Any(b=>b.GetComponentInChildren<TMP_Text>()?.text=="继续"),"light room menu has no duplicate continue action");
        yield return Shot("room-menu");
        lounge.Toggle();cafe.OpenMenu();yield return new WaitForSecondsRealtime(.6f);
        Check(lounge.Expanded&&lounge.LightRoot.gameObject.activeInHierarchy,"close and reopen in one frame restores action buttons");
        var more=lounge.LightRoot.GetComponentsInChildren<Button>().First(b=>b.name=="HudMoreButton");
        EventSystem.current.SetSelectedGameObject(more.gameObject);ExecuteEvents.Execute(more.gameObject,new BaseEventData(EventSystem.current),ExecuteEvents.submitHandler);yield return new WaitForSecondsRealtime(.25f);
        Check(EventSystem.current.currentSelectedGameObject==hud.Button("ContinueButton").gameObject,"keyboard more transfers focus to actual menu");
        Check(lounge.MorePanelRoot.activeInHierarchy&&!hud.menuRoot.activeInHierarchy&&!lounge.LightRoot.gameObject.activeInHierarchy,"expanded options replace the light action group");
        if(cafe.CurrentSeat>=0)cafe.Stand();
    }
}
#endif

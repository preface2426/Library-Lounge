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

public sealed class CafeUiRevisionAcceptance : MonoBehaviour
{
    CafeEntryUi entry;CafeHud hud;CafePrototype cafe;int checks,failures,savedMotion;float savedScale;string folder;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Attach()
    {
        if(!Environment.GetCommandLineArgs().Contains("-uiRevisionAcceptance"))return;
        FindFirstObjectByType<CafePrototype>()?.gameObject.AddComponent<CafeUiRevisionAcceptance>();
    }
    void Check(bool ok,string name){checks++;if(!ok)failures++;Debug.Log((ok?"R2_PASS ":"R2_FAIL ")+name);}
    object Call(object owner,string method,params object[] args)=>owner.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public,null,args.Select(x=>x.GetType()).ToArray(),null).Invoke(owner,args);
    IEnumerator Shot(string name){yield return new WaitForEndOfFrame();var image=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.Combine(folder,name+".png"),image.EncodeToPNG());Destroy(image);}
    IEnumerator Click(Button b)
    {
        EventSystem.current.SetSelectedGameObject(b.gameObject);b.GetComponent<CafeUiFocus>()?.Reveal();
        Canvas.ForceUpdateCanvases();yield return new WaitForEndOfFrame();
        var rt=(RectTransform)b.transform;var p=RectTransformUtility.WorldToScreenPoint(null,rt.TransformPoint(rt.rect.center));
        var data=new PointerEventData(EventSystem.current){position=p};var hits=new System.Collections.Generic.List<RaycastResult>();EventSystem.current.RaycastAll(data,hits);
        Check(hits.Count>0&&hits[0].gameObject.GetComponentInParent<Button>()==b,"pointer reaches "+b.name);
        ExecuteEvents.Execute(b.gameObject,data,ExecuteEvents.pointerClickHandler);yield return new WaitForSecondsRealtime(.5f);
    }
    IEnumerator Start()
    {
        entry=GetComponent<CafeEntryUi>();hud=GetComponent<CafeHud>();cafe=GetComponent<CafePrototype>();
        folder=Path.Combine(Application.dataPath,"../Evidence/R2");Directory.CreateDirectory(folder);
        savedMotion=PlayerPrefs.GetInt("CafeReduceMotion",0);savedScale=hud.TextScale;
        Screen.SetResolution(1600,1000,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(1);
        while(!entry.Ready)yield return null;
        PlayerPrefs.SetInt("CafeReduceMotion",1);hud.SetTextScale(1);
        var enter=entry.ScreenshotOverlayRoot.GetComponentsInChildren<Button>().First(b=>b.name.StartsWith("Action_进入大厅"));yield return Click(enter);
        Check(entry.ScreenshotOverlayRoot.GetComponentsInChildren<Button>().Any(b=>b.name=="Action_退出应用"),"lobby exposes direct quit application");yield return Shot("lobby");
        Screen.SetResolution(600,900,FullScreenMode.Windowed);hud.SetTextScale(2);yield return new WaitForSecondsRealtime(.5f);
        var footer=entry.ScreenshotOverlayRoot.transform.Find("SafeRoot/LoungeFooter");
        if(footer==null)footer=entry.ScreenshotOverlayRoot.GetComponentsInChildren<RectTransform>().First(x=>x.name=="LoungeFooter");
        var footerButtons=footer.GetComponentsInChildren<Button>();
        Check(footerButtons.Length==2&&!Bounds((RectTransform)footerButtons[0].transform).Overlaps(Bounds((RectTransform)footerButtons[1].transform)),"narrow 200 percent lobby footer buttons do not overlap");yield return Shot("lobby-narrow-200");
        Screen.SetResolution(1600,1000,FullScreenMode.Windowed);hud.SetTextScale(1);yield return new WaitForSecondsRealtime(.4f);
        Call(entry,"EnterSingle");yield return null;
        PlayerPrefs.SetInt("CafeReduceMotion",0);
        foreach(var size in new[]{new Vector2Int(1600,1000),new Vector2Int(2560,1600),new Vector2Int(900,500)})
        {
            Screen.SetResolution(size.x,size.y,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.4f);
            var transition=GetComponent<CafeRoomTransition>();
            transition.Play(true,()=>{});transition.enabled=false;
            var graphic=transition.GetComponentsInChildren<CafeKeyGraphic>().First(g=>!g.OutlineOnly);
            var area=graphic.rectTransform.rect;
            var boundsMethod=typeof(CafeKeyGraphic).GetMethod("StripBounds",BindingFlags.Static|BindingFlags.Public);
            Check(boundsMethod!=null,"strip geometry shares one viewport layout");
            if(boundsMethod!=null){var bounds=(Rect)boundsMethod.Invoke(null,new object[]{area});Check(Vector2.Distance(bounds.center,area.center)<.1f,"strip block centered "+size);Check(bounds.width/area.width>.3f&&bounds.width/area.width<.5f,"enlarged strip width "+size);}
            Call(transition,"SetTime",1.74f);graphic.SetVerticesDirty();yield return Shot("key-aligned-"+size.x);
            Call(transition,"SetTime",2.05f);
            using(var mesh=new VertexHelper())
            {
                Call(graphic,"OnPopulateMesh",mesh);var vertex=new UIVertex();mesh.PopulateUIVertex(ref vertex,mesh.currentVertCount-360);
                Check(Vector2.Distance(vertex.position,area.center)<.1f,"circular reveal starts at viewport center "+size);
            }
            graphic.SetVerticesDirty();yield return Shot("key-reveal-"+size.x);
            transition.Finish();transition.enabled=true;
        }
        Screen.SetResolution(1600,1000,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.4f);
        cafe.Sit(0);yield return new WaitForSecondsRealtime(.2f);GetComponent<CafeDesktopCapture>().StopCapture();
        cafe.OpenMenu();yield return new WaitForSecondsRealtime(.6f);
        var lounge=GetComponent<CafeLoungeHud>();
        Check(lounge.Expanded&&lounge.PanelsRoot.activeInHierarchy&&!hud.menuRoot.activeInHierarchy,"computer pause opens split floating HUD");
        Check(!hud.desktopRoot.activeInHierarchy&&cafe.eyes.transform.localPosition.sqrMagnitude<.001f,"computer menu shows room instead of immersive desktop");
        Check(lounge.LightRoot.GetComponentsInChildren<Button>().Any(b=>b.name=="HudOpenMembersButton"),"solo keeps usable members entry");yield return Shot("computer-room-hud");
        var members=lounge.LightRoot.GetComponentsInChildren<Button>().FirstOrDefault(b=>b.name=="HudOpenMembersButton");
        if(members!=null){yield return Click(members);Check(entry.BlocksRoomMenu,"solo members opens real modal");yield return Shot("solo-members");entry.HandleBack();yield return null;cafe.OpenMenu();yield return new WaitForSecondsRealtime(.5f);}
        var more=lounge.LightRoot.GetComponentsInChildren<Button>().First(b=>b.name=="HudMoreButton");yield return Click(more);
        Check(hud.menuTitle.text=="房间菜单","more uses web menu heading");
        var panel=GetComponent<CafeLoungeHud>().GetType().GetProperty("MorePanelRoot");
        Check(panel!=null&&((GameObject)panel.GetValue(lounge)).activeInHierarchy&&!hud.menuRoot.activeInHierarchy,"more replaces legacy large menu");yield return Shot("more-panel");
        var screenshot=GetComponent<CafeScreenshot>();screenshot.SetOutputDirectory(folder);yield return Click(hud.Button("SaveScreenshotButton"));
        var status=lounge.MorePanelRoot.GetComponentsInChildren<TMP_Text>().FirstOrDefault(t=>t.name=="ActionStatus");
        Check(screenshot.LastCaptureSucceeded&&status!=null&&status.text.Contains(screenshot.LastSavedPath),"compact menu shows saved screenshot path");yield return Shot("screenshot-success-status");
        string blocked=Path.Combine(folder,"blocked-output-file");File.WriteAllText(blocked,"fixture");screenshot.SetOutputDirectory(Path.Combine(blocked,"child"));yield return Click(hud.Button("SaveScreenshotButton"));
        Check(!screenshot.LastCaptureSucceeded&&status.gameObject.activeInHierarchy&&status.text.StartsWith("截图保存失败"),"compact menu shows screenshot write failure");yield return Shot("screenshot-failure-status");screenshot.SetOutputDirectory(null);
        Screen.SetResolution(900,500,FullScreenMode.Windowed);hud.SetTextScale(2);yield return new WaitForSecondsRealtime(.5f);
        Check(ScreenBounds((RectTransform)lounge.MorePanelRoot.transform),"compact menu fits short screen at 200 percent");yield return Shot("more-short-200");
        yield return Click(hud.Button("SettingsButton"));Check(hud.Page==1&&hud.menuRoot.activeInHierarchy,"more opens existing settings");
        hud.Back();Check(hud.Button("HelpButton").transform.IsChildOf(lounge.MorePanelRoot.transform),"immediate settings back routes click to visible menu");
        yield return Click(hud.Button("HelpButton"));Check(hud.Page==2,"immediate help click after settings back works");hud.Back();
        yield return new WaitForSecondsRealtime(.3f);Check(hud.Page==0&&lounge.MorePanelRoot.activeInHierarchy&&!hud.menuRoot.activeInHierarchy,"settings back returns to compact menu");
        hud.Back();yield return new WaitForSecondsRealtime(.4f);Check(!lounge.MoreOpen&&lounge.PanelsRoot.activeInHierarchy,"back returns from compact menu to split HUD");
        Screen.SetResolution(1600,1000,FullScreenMode.Windowed);hud.SetTextScale(1);yield return new WaitForSecondsRealtime(.4f);
        cafe.Continue();yield return null;
        var input=GetComponent<CafeDesktopInput>();
        input.enabled=false;
        for(int edge=0;edge<4;edge++)
        {
            typeof(CafeDesktopInput).GetField("toolbarCollapsed",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(input,true);
            typeof(CafeDesktopInput).GetField("dockEdge",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(input,edge);
            hud.Refresh();Canvas.ForceUpdateCanvases();
            var text=hud.Button("ToolbarTab").GetComponentInChildren<TMP_Text>();text.ForceMeshUpdate();
            Check(text.text=="菜单"&&text.textInfo.characterCount==2,"collapsed toolbar has readable identification edge "+edge);
            Check(text.rectTransform.rect.width>=55&&text.rectTransform.rect.height>=30,"collapsed toolbar label fits edge "+edge);
            var icons=hud.toolbarTab.GetComponentsInChildren<CafeLoungeIcon>();Check(icons.Any(i=>i.Icon==CafeLoungeIcon.Shape.More&&i.rectTransform.rect.width>=16),"collapsed toolbar keeps menu icon edge "+edge);
            yield return Shot("toolbar-edge-"+edge);
            var toolbar=input.ToolbarRect;Call(input,"TickToolbar",toolbar.center,false,Time.realtimeSinceStartupAsDouble);
            Check(!input.ToolbarCollapsed,"hover expands toolbar edge "+edge);
        }
        input.enabled=true;input.ResetToolbar();yield return Shot("toolbar-expanded");
        var roomFont=lounge.LightRoot.GetComponentsInChildren<TMP_Text>(true).First(t=>t.transform.parent.name=="HudMoreButton").font;
        Check(hud.toolbarExpanded.GetComponentsInChildren<Button>().All(b=>b.GetComponent<CafeLoungeButton>()!=null&&b.GetComponentInChildren<TMP_Text>().font==roomFont&&b.GetComponentInChildren<TMP_Text>().fontSize<=13),"simple menu shares room typography and action component");
        Check(hud.Button("ToolbarMenu").GetComponentInChildren<TMP_Text>().text.StartsWith("完整菜单")&&!hud.toolbarPanel.GetComponentsInChildren<TMP_Text>(true).Any(t=>t.text.Contains("电脑工具")),"simple menu exposes full menu and uses requested name");
        string[] hoverNames={"ToolbarDrag","ToolbarObserve","ToolbarStand","ToolbarView","ToolbarMenu"};
        PlayerPrefs.SetInt("CafeReduceMotion",1);
        var hoverRect=input.ToolbarRect;
        typeof(CafeLoungeButton).GetField("selected",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(hud.Button("ToolbarObserve").GetComponent<CafeLoungeButton>(),true);
        for(int i=0;i<hoverNames.Length;i++)
        {
            Call(hud,"UpdateToolbarHover",new Vector2(hoverRect.x+hoverRect.width*(i+.5f)/5,hoverRect.center.y),true);
            foreach(var name in hoverNames)Call(hud.Button(name).GetComponent<CafeLoungeButton>(),"LateUpdate");
            Check(hoverNames.All(name=>hud.Button(name).GetComponentInChildren<CafeLoungeGraphic>().Fill.a>(name==hoverNames[i] ? .9f : .7f)&&hud.Button(name).GetComponentInChildren<CafeLoungeGraphic>().Fill.a<(name==hoverNames[i] ? 1 : .8f)),"native pointer visual highlights only menu action "+i);
        }
        Call(hud,"UpdateToolbarHover",new Vector2(-100,-100),true);
        foreach(var name in hoverNames)Call(hud.Button(name).GetComponent<CafeLoungeButton>(),"LateUpdate");
        Check(hoverNames.All(name=>hud.Button(name).GetComponentInChildren<CafeLoungeGraphic>().Fill.a<.8f),"native pointer leaving menu clears highlights");
        Call(hud,"UpdateToolbarHover",Vector2.zero,false);
        if(cafe.CurrentSeat>=0)cafe.Stand();
        PlayerPrefs.SetInt("CafeReduceMotion",savedMotion);hud.SetTextScale(savedScale);PlayerPrefs.Save();
        Debug.Log("R2_ACCEPTANCE_"+(failures==0?"PASS":"FAIL")+" checks="+checks+" failures="+failures);
        File.WriteAllText(Path.Combine(folder,"result.json"),"{\"checks\":"+checks+",\"failures\":"+failures+"}");Application.Quit(failures==0?0:1);
    }
    static Rect Bounds(RectTransform rt){var corners=new Vector3[4];rt.GetWorldCorners(corners);return Rect.MinMaxRect(corners[0].x,corners[0].y,corners[2].x,corners[2].y);}
    static bool ScreenBounds(RectTransform rt){var r=Bounds(rt);return r.xMin>=0&&r.yMin>=0&&r.xMax<=Screen.width+.1f&&r.yMax<=Screen.height+.1f;}
}
#endif

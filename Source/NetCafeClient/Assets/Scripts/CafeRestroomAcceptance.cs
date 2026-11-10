#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.UI;

// Opt-in built-player verification. The normal client never starts the controlled desktop window.
public sealed class CafeRestroomAcceptance : MonoBehaviour
{
    [DllImport("NetCafeCapture")] static extern int NC_TestBegin();
    [DllImport("NetCafeCapture")] static extern void NC_TestEnd();
    CafePrototype cafe;CafeDesktopCapture capture;CafeDesktopInput input;CafeRestroomLayout layout;
    string folder;int checks;readonly List<ColorSample> colorSamples=new();bool ownedPattern,oldView;readonly List<string> passed=new();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Attach()
    {
        if(!Environment.GetCommandLineArgs().Contains("-restroomAcceptance"))return;
        FindFirstObjectByType<CafePrototype>()?.gameObject.AddComponent<CafeRestroomAcceptance>();
    }
    static object Call(object owner,string method,params object[] args)=>owner.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).Invoke(owner,args);
    void Check(bool ok,string name)
    {
        checks++;if(!ok)throw new Exception(name);passed.Add(name);Debug.Log("RESTROOM_CHECK_PASS "+name);
    }
    IEnumerator Start()
    {
        cafe=GetComponent<CafePrototype>();capture=GetComponent<CafeDesktopCapture>();input=GetComponent<CafeDesktopInput>();layout=GetComponent<CafeRestroomLayout>();
        oldView=input.RoomView;folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../Evidence/Restroom"));Directory.CreateDirectory(folder);
        Exception failure=null;var stack=new Stack<IEnumerator>();stack.Push(Run());
        while(stack.Count>0)
        {
            var step=stack.Peek();bool more=false;
            try{more=step.MoveNext();}catch(Exception ex){failure=ex;}
            if(failure!=null)break;
            if(!more){(stack.Pop() as IDisposable)?.Dispose();continue;}
            if(step.Current is IEnumerator nested){stack.Push(nested);continue;}yield return step.Current;
        }
        while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();
        input.EndControl();capture.StopCapture();if(ownedPattern)NC_TestEnd();
        if(input.RoomView!=oldView)input.ToggleView();
        if(failure!=null){Debug.LogException(failure);Debug.LogError("RESTROOM_ACCEPTANCE_FAIL "+failure.Message);}
        else Debug.Log("RESTROOM_ACCEPTANCE_PASS checks="+checks+" six E-key seats with real GPU capture");
        File.WriteAllText(Path.Combine(folder,"result.json"),JsonUtility.ToJson(new Result{checks=checks,passed=passed.ToArray(),failure=failure?.ToString(),success=failure==null,absoluteColorFidelity=colorSamples.Count==6&&colorSamples.All(s=>s.absoluteColorFidelity)},true));
        Application.Quit(failure==null?0:1);
    }
    [Serializable] class ColorSample{public int station;public Color32[] measured;public bool absoluteColorFidelity;}
    [Serializable] class Result{public int checks;public bool success;public bool absoluteColorFidelity;public string[] passed;public string failure;}
    IEnumerator Run()
    {
        while(!SplashScreen.isFinished)yield return null;
        yield return new WaitForSecondsRealtime(1);
        var entry=GetComponent<CafeEntryUi>();while(!entry.Ready)yield return null;
        Check(layout!=null&&cafe.walkableFloor!=null,"restroom layout and floor bound");
        Check(cafe.screens.Length==6&&cafe.seats.Length==13&&layout.displays.Length==6,"six computer, six sofa and one lounge chair seats");
        var sync=GetComponent<CafeRoomSync>();
        Check(sync.ClampRoomPosition(new Vector3(-6,.1f,6.8f))==new Vector3(-6,.1f,6.8f),"restroom positions preserved beyond old network boundary");
        Check(sync.ClampRoomPosition(new Vector3(-100,100,100))==new Vector3(-9.15f,2.5f,7.575f),"out-of-room network coordinates remain bounded");
        Check(FindObjectsByType<CafePrototype>(FindObjectsSortMode.None).Length==1,"one gameplay owner");
        var single=entry.ScreenshotOverlayRoot.GetComponentsInChildren<Button>().First(b=>b.name.StartsWith("Action_单人体验"));single.onClick.Invoke();
        yield return new WaitForSecondsRealtime(5);
        Check(cafe.Mode==CafePrototype.InputMode.Walking,"single entry reaches walking state");
        yield return Shot("00-restroom-entry");
        ownedPattern=NC_TestBegin()!=0;Check(ownedPattern,"owned controlled desktop test window started");
        yield return new WaitForSecondsRealtime(.5f);
        if(!input.RoomView)input.ToggleView();
        for(int station=0;station<6;station++)
        {
            if(cafe.CurrentSeat>=0)cafe.Stand();capture.StopCapture();yield return null;
            Check(cafe.CurrentSeat==-1,"previous seat exits safely before station "+station);
            var target=FindObjectsByType<CafeInteractable>(FindObjectsSortMode.None).Single(t=>t.name=="ComputerSeatInteraction_"+station);
            Check(FindApproach(target,out var position,out var rotation),"chair reachable by gaze from collision-free standing point "+station);
            cafe.body.enabled=false;cafe.body.transform.position=position;cafe.body.enabled=true;
            cafe.eyes.transform.rotation=rotation;
            typeof(CafePrototype).GetField("yaw",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(cafe,rotation.eulerAngles.y);
            float pitch=Mathf.DeltaAngle(0,rotation.eulerAngles.x);typeof(CafePrototype).GetField("pitch",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(cafe,pitch);
            Physics.SyncTransforms();yield return null;
            Call(cafe,"FindTarget");Check(cafe.InteractionHint.Contains("坐下使用电脑"),"E prompt resolves chair "+station);
            if(station==0||station==2)yield return Shot("station-"+station+"-physical-layout");
            InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState(Key.E));yield return null;yield return null;
            InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState());yield return null;
            Check(cafe.CurrentSeat==station,"actual Input System E enters correct station "+station);
            float deadline=Time.realtimeSinceStartup+12;
            while(!capture.Ready&&Time.realtimeSinceStartup<deadline)yield return null;
            Check(capture.Ready,"E automatically starts real GPU capture "+station+": "+capture.Status);
            Check(cafe.Mode==CafePrototype.InputMode.Desktop,"desktop mode after first frame "+station);
            Check(capture.DisplaySurface.parent==cafe.screens[station].transform,"capture attached to selected computer "+station);
            Check(!input.Controlling,"fixture leaves OS input ownership untouched "+station);
            yield return new WaitForSecondsRealtime(.2f);
            Rect rect=capture.DesktopRect;float coverage=Mathf.Max(rect.width/Screen.width,rect.height/Screen.height);
            Check(Mathf.Abs(coverage-.90f)<.012f,"room-view coverage about ninety percent "+station+": "+coverage);
            Check(Mathf.Abs(rect.center.x-Screen.width*.5f)<2&&Mathf.Abs(rect.center.y-Screen.height*.5f)<2,"mapping centered in viewport "+station);
            Check(Mathf.Abs(rect.width/rect.height-capture.MonitorAspect)<.002f,"desktop aspect preserved "+station);
            var rig=layout.displays[station];var image=rig.image.lossyScale;var frame=rig.frame.lossyScale;
            Check(rig.preserveHousing ? image.x<=rig.originalScreenSize.x&&image.y<=rig.originalScreenSize.y : Mathf.Abs(image.y-CafeMonitorFrame.ImageHeight)<.0001f,"image fits original desktop or laptop dimensions "+station);
            Check(rig.preserveHousing ? Vector2.Distance(new Vector2(frame.x,frame.y),rig.originalScreenSize)<.0001f : Mathf.Abs((frame.x-image.x)*.5f-CafeMonitorFrame.Border)<.0001f&&Mathf.Abs((frame.y-image.y)*.5f-CafeMonitorFrame.Border)<.0001f,"original desktop housing or equal narrow laptop bezel "+station);
            Check(Vector3.Dot(cafe.eyes.transform.forward,rig.image.forward)>.9999f,"camera perpendicular to actual screen plane "+station);
            Check(!cafe.Sharing&&!GetComponent<CafeDesktopSharing>().Sharing,"local capture does not publish desktop "+station);
            yield return VerifyPattern(station);
            yield return Shot("station-"+station+"-room-view");
            input.ToggleView();yield return null;yield return null;
            Check(Mathf.Abs(Mathf.Max(capture.DesktopRect.width/Screen.width,capture.DesktopRect.height/Screen.height)-1)<.002f,"fullscreen aspect fit "+station);
            input.ToggleView();yield return null;yield return null;
        }
        var info=capture.Info;var infoField=typeof(CafeDesktopCapture).GetField("<Info>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic);
        foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(2560,1600),new Vector2Int(3440,1440)})
        {
            var fake=info;fake.width=size.x;fake.height=size.y;infoField.SetValue(capture,fake);Call(layout,"RefreshDisplays");Call(cafe,"ApplySeatedCamera");
            float aspect=(float)size.x/size.y;
            Check(layout.displays.All(d=>Mathf.Abs(d.image.lossyScale.x/d.image.lossyScale.y-aspect)<.0001f),"all six displays resize to source aspect "+size);
            Check(layout.displays.All(d=>d.preserveHousing ? d.image.lossyScale.x<=d.originalScreenSize.x&&d.image.lossyScale.y<=d.originalScreenSize.y : Mathf.Abs(d.image.lossyScale.y-CafeMonitorFrame.ImageHeight)<.0001f),"desktop housing preserved while source image aspect fits "+size);
            var rect=capture.DesktopRect;Check(Mathf.Abs(Mathf.Max(rect.width/Screen.width,rect.height/Screen.height)-.9f)<.012f,"different source aspect limits longest viewport edge to ninety percent "+size);
        }
        infoField.SetValue(capture,info);Call(layout,"RefreshDisplays");Call(cafe,"ApplySeatedCamera");
        foreach(var size in new[]{new Vector2Int(1600,1000),new Vector2Int(1920,1080)})
        {
            Screen.SetResolution(size.x,size.y,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.5f);
            var rect=capture.DesktopRect;Check(Mathf.Abs(Mathf.Max(rect.width/Screen.width,rect.height/Screen.height)-.9f)<.012f,"window resize keeps ninety-percent aspect fit "+size);
        }
        cafe.OpenMenu();yield return new WaitForSecondsRealtime(.3f);Check(GetComponent<CafeLoungeHud>().LightRoot.gameObject.activeInHierarchy,"room pause retains new split HUD");yield return Shot("room-hud");
        cafe.Continue();cafe.Stand();Check(cafe.CurrentSeat==-1&&cafe.body.enabled,"last computer has safe exit and restores player");
    }
    bool FindApproach(CafeInteractable target,out Vector3 position,out Quaternion rotation)
    {
        Vector3 aim=target.transform.position;
        foreach(float radius in new[]{1.3f,1.7f,2.1f})for(int i=0;i<24;i++)
        {
            Vector3 p=aim+Quaternion.Euler(0,i*15,0)*Vector3.forward*radius;p.y=.06f;
            Vector3 mid=p+cafe.body.center;float half=Mathf.Max(0,cafe.body.height*.5f-cafe.body.radius);
            if(Physics.OverlapCapsule(mid-Vector3.up*half,mid+Vector3.up*half,cafe.body.radius+.04f,~(1<<28),QueryTriggerInteraction.Ignore).Any(c=>!c.transform.IsChildOf(cafe.body.transform)))continue;
            Vector3 head=p+Vector3.up*cafe.WalkingEyeHeight;Vector3 direction=(aim-head).normalized;
            if(!Physics.Raycast(head,direction,out var hit,3f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Collide)||hit.collider.GetComponentInParent<CafeInteractable>()!=target)continue;
            position=p;rotation=Quaternion.LookRotation(direction,Vector3.up);return true;
        }
        position=default;rotation=default;return false;
    }
    [Serializable] class ColorReport{public ColorSample[] samples;}
    IEnumerator VerifyPattern(int station)
    {
        yield return null;
        int w=capture.Info.width,h=capture.Info.height;
        var previous=RenderTexture.active;
        var rt=RenderTexture.GetTemporary(w,h,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
        var texture=new Texture2D(w,h,TextureFormat.RGBA32,false);
        try
        {
            // Same supported readback path as the existing native capture acceptance.
            Graphics.Blit(capture.DesktopTexture,rt);RenderTexture.active=rt;
            texture.ReadPixels(new Rect(0,0,w,h),0,0);texture.Apply();
            var locations=new[]{new Vector2(.12f,.20f),new Vector2(.88f,.20f),new Vector2(.12f,.70f),new Vector2(.88f,.70f)};
            var expected=new[]{new Color32(220,40,40,255),new Color32(40,200,70,255),new Color32(40,80,220,255),new Color32(230,200,40,255)};
            var measured=locations.Select(p=>(Color32)texture.GetPixel((int)(w*p.x),(int)(h*p.y))).ToArray();
            bool fidelity=true;
            for(int i=0;i<expected.Length;i++)
            {
                var a=measured[i];var e=expected[i];
                fidelity&=Math.Abs(a.r-e.r)<20&&Math.Abs(a.g-e.g)<20&&Math.Abs(a.b-e.b)<20;
                float maximum=Mathf.Max(a.r,Mathf.Max(a.g,a.b));float reference=Mathf.Max(e.r,Mathf.Max(e.g,e.b));
                Check(maximum>40&&Mathf.Abs(a.r/maximum-e.r/reference)<.06f&&Mathf.Abs(a.g/maximum-e.g/reference)<.06f&&Mathf.Abs(a.b/maximum-e.b/reference)<.06f,"real capture retains controlled quadrant content "+station+"/"+i+": "+a);
            }
            colorSamples.Add(new ColorSample{station=station,measured=measured,absoluteColorFidelity=fidelity});
            File.WriteAllText(Path.Combine(folder,"color-measurements.json"),JsonUtility.ToJson(new ColorReport{samples=colorSamples.ToArray()},true));
            Check(fidelity,"absolute controlled desktop RGB colors match native test pattern "+station);
            Debug.Log("RESTROOM_ABSOLUTE_COLOR_FIDELITY station="+station+" passed="+fidelity);
        }
        finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);Destroy(texture);}
    }
    IEnumerator Shot(string name)
    {
        yield return new WaitForEndOfFrame();var texture=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.Combine(folder,name+".png"),texture.EncodeToPNG());Destroy(texture);
    }
}
#endif

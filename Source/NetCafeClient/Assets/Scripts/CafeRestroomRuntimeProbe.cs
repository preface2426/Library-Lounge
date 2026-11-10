#if UNITY_EDITOR || DEVELOPMENT_BUILD || NETCAFE_ACCEPTANCE
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Opt-in reproduction: no controlled source window, no input-control bypass, no saved desktop pixels.
public sealed class CafeRestroomRuntimeProbe : MonoBehaviour
{
    // Process-local opt-in lets the unchanged launcher exercise this same normal UI/E path.
    // Invalid/absent values must never enable unattended interaction in a normal session.
    static int EnvironmentStation()
    {
        string value=Environment.GetEnvironmentVariable("LIBRARYLOUNGE_QA_RESTROOM_STATION");
        return value!=null&&value.Length==1&&value[0]>='0'&&value[0]<='5'?value[0]-'0':-1;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Attach()
    {
        if(Environment.GetCommandLineArgs().Contains("-restroomRuntimeProbe")||EnvironmentStation()>=0)
            FindFirstObjectByType<CafePrototype>()?.gameObject.AddComponent<CafeRestroomRuntimeProbe>();
    }
    delegate bool WindowCallback(IntPtr window,IntPtr value);
    [DllImport("user32.dll")]static extern bool EnumWindows(WindowCallback callback,IntPtr value);
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr window,System.Text.StringBuilder name,int count);
    static IntPtr OwnWindow(){IntPtr result=IntPtr.Zero;EnumWindows((w,v)=>{GetWindowThreadProcessId(w,out var pid);var name=new System.Text.StringBuilder(80);GetClassName(w,name,80);if(pid==(uint)System.Diagnostics.Process.GetCurrentProcess().Id&&name.ToString()=="UnityWndClass"){result=w;return false;}return true;},IntPtr.Zero);return result;}
    [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr window,int command);
    CafePrototype cafe;CafeDesktopCapture capture;CafeDesktopInput input;
    bool passed;
    void Check(bool condition,string label){if(!condition)throw new Exception(label);Debug.Log("RUNTIME_CHECK_PASS "+label);}
    IEnumerator Start()
    {
        cafe=GetComponent<CafePrototype>();capture=GetComponent<CafeDesktopCapture>();input=GetComponent<CafeDesktopInput>();
        bool hadPreference=PlayerPrefs.HasKey("DesktopRoomView");int preference=PlayerPrefs.GetInt("DesktopRoomView",1);
        Exception failure=null;var stack=new System.Collections.Generic.Stack<IEnumerator>();stack.Push(Run());
        while(stack.Count>0)
        {
            var current=stack.Peek();bool more=false;
            try{more=current.MoveNext();}catch(Exception e){failure=e;}
            if(failure!=null)break;
            if(!more){(stack.Pop() as IDisposable)?.Dispose();continue;}
            if(current.Current is IEnumerator nested){stack.Push(nested);continue;}yield return current.Current;
        }
        while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();
        try{input.EndControl();capture.StopCapture();cafe.OpenMenu();}
        finally{if(hadPreference)PlayerPrefs.SetInt("DesktopRoomView",preference);else PlayerPrefs.DeleteKey("DesktopRoomView");PlayerPrefs.Save();}
        if(failure!=null)Debug.LogException(failure);
        Debug.Log("RUNTIME_PROBE_"+(passed&&failure==null?"PASS":"FAIL")+" finished preference restored; "+failure?.Message);
        Application.Quit(passed&&failure==null?0:1);
    }
    IEnumerator Run()
    {
        var entry=GetComponent<CafeEntryUi>();
        while(!entry.Ready||!UnityEngine.Rendering.SplashScreen.isFinished)yield return null;
        entry.ScreenshotOverlayRoot.GetComponentsInChildren<UnityEngine.UI.Button>().First(b=>b.name.StartsWith("Action_单人体验")).onClick.Invoke();
        yield return new WaitForSecondsRealtime(5);
        if(Environment.GetCommandLineArgs().Contains("-probeSofa")){yield return Sofa(cafe);passed=true;yield break;}
        var ownWindow=OwnWindow();Check(ownWindow!=IntPtr.Zero,"test owns a Unity window");ShowWindow(ownWindow,9);SetForegroundWindow(ownWindow);yield return new WaitForSecondsRealtime(.5f);
        var args=Environment.GetCommandLineArgs();bool direct=args.Contains("-probeDirectSeat");int environmentStation=EnvironmentStation();int station=environmentStation>=0?environmentStation:0;
        var option=args.FirstOrDefault(a=>a.StartsWith("-probeStation="));if(option!=null)station=int.Parse(option.Split('=')[1]);
        if((environmentStation>=0||args.Contains("-probeFullscreenBeforeSeat"))&&input.RoomView)input.ToggleView();
        float oldFov=cafe.eyes.fieldOfView;
        Check(!cafe.SelfTesting,"normal production capture and native input path");
        Debug.Log("RUNTIME_PROBE_BEFORE station="+station+" focus="+Application.isFocused+" roomView="+input.RoomView+" gpu="+SystemInfo.graphicsDeviceType+" status="+capture.Status);
        if(direct)cafe.Sit(station);
        else
        {
            Check(Application.isFocused,"actual E input has game focus");
            var target=FindObjectsByType<CafeInteractable>(FindObjectsSortMode.None).Single(t=>t.name=="ComputerSeatInteraction_"+station);bool found=false;
            foreach(float radius in new[]{1.3f,1.7f,2.1f})
            {
                for(int i=0;i<48&&!found;i++)
                {
                    Vector3 p=target.transform.position+Quaternion.Euler(0,i*7.5f,0)*Vector3.forward*radius;p.y=.06f;
                    var mid=p+cafe.body.center;float half=Mathf.Max(0,cafe.body.height*.5f-cafe.body.radius);
                    if(Physics.OverlapCapsule(mid-Vector3.up*half,mid+Vector3.up*half,cafe.body.radius+.04f,~(1<<28),QueryTriggerInteraction.Ignore).Any(c=>!c.transform.IsChildOf(cafe.body.transform)))continue;
                    var head=p+Vector3.up*cafe.WalkingEyeHeight;var direction=(target.transform.position-head).normalized;
                    if(!Physics.Raycast(head,direction,out var hit,3,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Collide)||hit.collider.GetComponentInParent<CafeInteractable>()!=target)continue;
                    cafe.body.enabled=false;cafe.body.transform.position=p;cafe.body.enabled=true;
                    var rotation=Quaternion.LookRotation(direction,Vector3.up);cafe.eyes.transform.rotation=rotation;
                    typeof(CafePrototype).GetField("yaw",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(cafe,rotation.eulerAngles.y);
                    typeof(CafePrototype).GetField("pitch",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(cafe,Mathf.DeltaAngle(0,rotation.eulerAngles.x));found=true;
                }
                if(found)break;
            }
            Check(found,"collision-free approach and gaze resolve the chair");yield return null;
            InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState(Key.E));yield return null;yield return null;
            InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState());yield return null;
        }
        Check(cafe.CurrentSeat==station,"E enters selected station "+station);
        float deadline=Time.realtimeSinceStartup+12;
        while((!capture.Ready||!input.Controlling)&&Time.realtimeSinceStartup<deadline)yield return null;
        Check(capture.Ready,"automatic mapped desktop first frame: "+capture.Status);
        Check(input.Controlling,"real native desktop control acquired: "+input.Error);
        Check(input.RoomView&&input.ToolbarCollapsed,"new seat opens non-fullscreen with compact menu");
        ulong frames=capture.Info.frames;yield return new WaitForSecondsRealtime(2);
        Check(capture.Info.frames>frames,"real desktop frames continue updating");
        Rect rect=capture.DesktopRect;float coverage=Mathf.Max(rect.width/Screen.width,rect.height/Screen.height);
        Check(Mathf.Abs(coverage-.90f)<.002f,"actual image covers ninety percent "+coverage);
        Check((rect.center-new Vector2(Screen.width*.5f,Screen.height*.5f)).magnitude<2,"desktop image centered");
        Check(Mathf.Abs(rect.width/rect.height-capture.MonitorAspect)<.002f,"source aspect ratio preserved");
        Check(Mathf.Abs(cafe.eyes.fieldOfView-oldFov)<.001f,"camera lens unchanged; distance controls coverage");
        Check(capture.DisplaySurface.parent==cafe.screens[station].transform,"texture bound to selected physical computer");
        var rig=GetComponent<CafeRestroomLayout>().displays[station];
        Check(station<2?rig.preserveHousing:!rig.preserveHousing,"original desktop housing policy "+station);
        Check(!cafe.Sharing&&!GetComponent<CafeDesktopSharing>().Sharing,"local mapping stays unpublished");
        Debug.Log("RUNTIME_STATION_PASS station="+station+" rect="+rect+" fov="+cafe.eyes.fieldOfView+" cameraDistance="+Vector3.Distance(cafe.eyes.transform.position,capture.DisplaySurface.position)+" frames="+capture.Info.frames+" input="+input.Diagnostic());
        passed=true;
    }
    IEnumerator Sofa(CafePrototype cafe)
    {
        cafe.enabled=false;var camera=cafe.eyes;camera.transform.SetParent(null,true);
        foreach(var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))canvas.enabled=false;
        var sofa=FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Single(r=>r.name=="cinema_sofa_ImportedFBX");
        var sun=FindObjectsByType<Light>(FindObjectsSortMode.None).Single(l=>l.name=="GS_Sun");
        Debug.Log("SOFA_DIAG sun bias="+sun.shadowBias+" normal="+sun.shadowNormalBias+" casting="+sofa.shadowCastingMode+" receive="+sofa.receiveShadows+" lightmap="+sofa.lightmapIndex);
        string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../../Workspaces/LibraryLounge-v0.1.3-Restroom-20261107/Docs/R1/sofa-render"));Directory.CreateDirectory(folder);
        Vector3[] positions={new Vector3(4.2f,1.7f,5.1f),new Vector3(1.8f,1.7f,4.7f),new Vector3(-1.1f,1.7f,1.5f),new Vector3(4.1f,1.7f,-5.6f)};
        Vector3[] targets={new Vector3(2.7f,1.0f,2.4f),new Vector3(2.7f,1.0f,2.2f),new Vector3(1.2f,1.0f,-.55f),new Vector3(2.7f,1.0f,-3.0f)};
        for(int mode=0;mode<3;mode++)
        {
            sofa.receiveShadows=mode!=1;if(mode==2){sun.shadowBias=1;sun.shadowNormalBias=1;var extra=sun.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();if(extra)extra.usePipelineSettings=false;}
            for(int i=0;i<positions.Length;i++)
            {
                camera.transform.SetPositionAndRotation(positions[i],Quaternion.LookRotation(targets[i]-positions[i]));
                yield return null;yield return new WaitForEndOfFrame();
                var rt=RenderTexture.GetTemporary(1440,900,24);camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
                var tex=new Texture2D(1440,900,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1440,900),0,0);tex.Apply();File.WriteAllBytes(Path.Combine(folder,"mode-"+mode+"-view-"+i+".png"),tex.EncodeToPNG());Destroy(tex);camera.targetTexture=null;RenderTexture.active=null;RenderTexture.ReleaseTemporary(rt);
            }
        }
        // Reproduce the small camera movements from the user's clip, with
        // normal shadow settings restored and without any desktop recording.
        sofa.receiveShadows=true;sun.shadowBias=.04f;sun.shadowNormalBias=.2f;
        for(int i=0;i<24;i++)
        {
            float a=i*Mathf.PI*2/24;var position=new Vector3(4.1f+Mathf.Sin(a)*.17f,1.7f+Mathf.Cos(a)*.04f,-5.6f+Mathf.Cos(a)*.18f);
            camera.transform.SetPositionAndRotation(position,Quaternion.LookRotation(new Vector3(2.7f,1.0f,-3)-position));yield return null;
            var rt=RenderTexture.GetTemporary(1440,900,24);camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
            var tex=new Texture2D(1440,900,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1440,900),0,0);tex.Apply();File.WriteAllBytes(Path.Combine(folder,"walk-"+i.ToString("D2")+".png"),tex.EncodeToPNG());Destroy(tex);camera.targetTexture=null;RenderTexture.active=null;RenderTexture.ReleaseTemporary(rt);
        }
        foreach(var approach in new[]{new Vector3(-1.1f,.06f,-.55f),new Vector3(4.25f,.06f,-5.6f)})
        {
            cafe.body.enabled=false;cafe.body.transform.position=approach;cafe.body.enabled=true;
            var direction=approach.x<0?Vector3.right:Vector3.forward;int sides=0;
            for(int i=0;i<100;i++){var flags=cafe.body.Move(direction*.04f+Vector3.down*.03f);if((flags&CollisionFlags.Sides)!=0)sides++;yield return null;}
            Check(sides>0&&Vector3.Distance(cafe.body.transform.position,approach)<3.5f,"sofa blocks real walking capsule from "+approach+" at "+cafe.body.transform.position+" contacts="+sides);
        }
        Debug.Log("SOFA_DIAG_PASS "+folder);
    }

}
#endif

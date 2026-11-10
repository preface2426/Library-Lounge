#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

// Opt-in real E-key/UI test; screenshots contain only a generated public-screen pattern.
public sealed class CafeCinemaAcceptance : MonoBehaviour
{
    [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr window,int command);
    [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,IntPtr process);
    [DllImport("user32.dll")]static extern bool AttachThreadInput(uint from,uint to,bool attach);
    [DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
    CafePrototype cafe;CafeCinemaView cinema;CafeRoomSync sync;CafeEntryUi entry;
    string folder;int checks;readonly List<string> passed=new();readonly List<Measurement> measurements=new();
    Material originalMaterial,patternMaterial;Texture2D pattern;IntPtr ownWindow;
    [Serializable]class Measurement{public int seat;public string mode;public float aspect,fov,coverage;public Vector3 position;}
    [Serializable]class Result{public bool success;public int checks;public string failure;public string[] passed;public Measurement[] views;}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Attach()
    {
        if(Environment.GetCommandLineArgs().Contains("-cinemaAcceptance"))
            FindFirstObjectByType<CafePrototype>()?.gameObject.AddComponent<CafeCinemaAcceptance>();
    }
    void Check(bool condition,string label)
    {if(!condition)throw new Exception(label);checks++;passed.Add(label);Debug.Log("CINEMA_CHECK_PASS "+label);}
    static object Call(object owner,string name,params object[] args)=>owner.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).Invoke(owner,args);
    IEnumerator Start()
    {
        cafe=GetComponent<CafePrototype>();cinema=GetComponent<CafeCinemaView>();sync=GetComponent<CafeRoomSync>();entry=GetComponent<CafeEntryUi>();
        folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../Evidence/Cinema"));Directory.CreateDirectory(folder);
        bool hadMotion=PlayerPrefs.HasKey("CafeReduceMotion");int motion=PlayerPrefs.GetInt("CafeReduceMotion",0);
        Exception failure=null;var stack=new Stack<IEnumerator>();stack.Push(Run());
        while(stack.Count>0)
        {
            var step=stack.Peek();bool more=false;
            try{more=step.MoveNext();}catch(Exception e){failure=e;}
            if(failure!=null)break;
            if(!more){(stack.Pop() as IDisposable)?.Dispose();continue;}
            if(step.Current is IEnumerator nested){stack.Push(nested);continue;}yield return step.Current;
        }
        while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();
        InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState());InputSystem.QueueStateEvent(Mouse.current,new MouseState());
        Call(sync,"Reset");
        if(originalMaterial!=null)cafe.publicScreen.sharedMaterial=originalMaterial;
        if(patternMaterial!=null)Destroy(patternMaterial);if(pattern!=null)Destroy(pattern);
        cafe.eyes.ResetAspect();
        if(hadMotion)PlayerPrefs.SetInt("CafeReduceMotion",motion);else PlayerPrefs.DeleteKey("CafeReduceMotion");PlayerPrefs.Save();
        if(failure!=null)Debug.LogException(failure);
        File.WriteAllText(Path.Combine(folder,"result.json"),JsonUtility.ToJson(new Result{success=failure==null,checks=checks,failure=failure?.ToString(),passed=passed.ToArray(),views=measurements.ToArray()},true));
        Debug.Log("CINEMA_ACCEPTANCE_"+(failure==null?"PASS":"FAIL")+" checks="+checks);
        Application.Quit(failure==null?0:1);
    }
    IEnumerator Run()
    {
        while(!entry.Ready||!UnityEngine.Rendering.SplashScreen.isFinished)yield return null;
        var own=(IntPtr)typeof(CafeRestroomRuntimeProbe).GetMethod("OwnWindow",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);ownWindow=own;
        Check(own!=IntPtr.Zero,"test owns Unity window");ShowWindow(own,9);SetForegroundWindow(own);
        PlayerPrefs.SetInt("CafeReduceMotion",1);
        entry.ScreenshotOverlayRoot.GetComponentsInChildren<Button>().First(b=>b.name.StartsWith("Action_单人体验")).onClick.Invoke();
        yield return new WaitForSecondsRealtime(.4f);PlayerPrefs.SetInt("CafeReduceMotion",0);
        yield return FocusWindow();
        Check(Application.isFocused&&!cafe.SelfTesting,"normal game input has focus without legacy smoke bypass");
        Check(cinema!=null&&cafe.seats.Length==13&&cafe.screens.Length==6,"six PC + six sofa + retained chair bindings");
        Check(!GetComponent<CafeDesktopCapture>().Active&&!cafe.Sharing,"sofa tests start without desktop capture or publication");
        pattern=new Texture2D(640,360,TextureFormat.RGB24,false);
        for(int y=0;y<360;y++)for(int x=0;x<640;x++)
            pattern.SetPixel(x,y,x<8||x>631||y<8||y>351?new Color(.72f,.58f,.35f):(x/40+y/40)%2==0?new Color(.25f,.36f,.36f):new Color(.12f,.19f,.21f));
        pattern.Apply();originalMaterial=cafe.publicScreen.sharedMaterial;
        patternMaterial=new Material(GetComponent<CafeDesktopCapture>().desktopMaterial);patternMaterial.mainTexture=pattern;cafe.publicScreen.sharedMaterial=patternMaterial;
        for(int seat=6;seat<12;seat++)
        {
            yield return EnterByE(seat);yield return new WaitForSecondsRealtime(.65f);
            Check(cafe.IsCinemaSeat&&!cafe.CinemaFocused&&cafe.Mode==CafePrototype.InputMode.Observing,"seat "+seat+" automatic initial cinema mode");
            Measure(seat,"default",.84f);CheckSightline(seat);
            yield return Shot("seat-"+seat+"-default");
            Vector3 seatPosition=cafe.seats[seat].position,bodyPosition=cafe.body.transform.position;
            yield return Press(Key.Escape);yield return new WaitForSecondsRealtime(.3f);
            Check(cafe.Mode==CafePrototype.InputMode.Menu,"Esc opens quiet room menu "+seat);
            var action=FindButton("HudCinemaFocusButton");Check(action!=null,"quiet cinema control visible "+seat);
            if(seat==6)yield return Shot("quiet-menu");
            yield return Click(action);yield return new WaitForSecondsRealtime(.65f);
            Check(cafe.CinemaFocused&&cafe.Mode==CafePrototype.InputMode.Observing,"pointer enters focused view "+seat);
            Check(cafe.CurrentSeat==seat&&cafe.seats[seat].position==seatPosition&&cafe.body.transform.position==bodyPosition,"focus preserves seat and body ownership "+seat);
            Measure(seat,"focus",.90f);CheckSightline(seat);
            if(seat==6)yield return Shot("focus-view");
            yield return Press(Key.Escape);yield return new WaitForSecondsRealtime(.25f);
            yield return Click(FindButton("HudMoreButton"));yield return new WaitForSecondsRealtime(.35f);
            var back=FindButton("CinemaFocusButton");Check(back!=null,"complete menu has cinema return control "+seat);
            yield return Click(back);yield return new WaitForSecondsRealtime(.65f);
            Check(!cafe.CinemaFocused&&cafe.CurrentSeat==seat,"complete menu returns to original seat "+seat);Measure(seat,"returned",.84f);
            foreach(float aspect in new[]{4f/3,16f/9,21f/9,9f/16})
            {
                cafe.eyes.aspect=aspect;yield return null;yield return null;Measure(seat,"aspect",.84f);
            }
            cafe.eyes.ResetAspect();yield return null;
            PlayerPrefs.SetInt("CafeReduceMotion",1);cafe.ToggleCinemaFocus();yield return null;yield return null;
            Measure(seat,"reduced-focus",.90f);PlayerPrefs.SetInt("CafeReduceMotion",0);
            if(seat==6)
            {
                var p=new Vector2(Screen.width*.5f,Screen.height*.5f);
                InputSystem.QueueStateEvent(Mouse.current,new MouseState{position=p}.WithButton(MouseButton.Right));yield return null;yield return null;
                Check(cafe.LookingAround&&!cafe.CinemaFocused,"right-button look exits focus without changing seat");
                InputSystem.QueueStateEvent(Mouse.current,new MouseState{position=p});yield return null;yield return null;
                Check(!cafe.LookingAround&&cafe.CurrentSeat==seat,"right-button release restores pointer");
            }
            yield return Press(Key.E);yield return null;
            Check(cafe.CurrentSeat==-1&&cafe.body.enabled&&!cinema.Focused,"E stands and clears cinema state "+seat);
            Check(Mathf.Abs(cafe.eyes.fieldOfView-72)<.01f,"walking lens restored "+seat);
            Check(!GetComponent<CafeDesktopCapture>().Active&&!cafe.Sharing,"cinema has not started capture/publication "+seat);
            DiagnoseExit();
            Check(ClearStanding(cafe.body.transform.position),"safe physical exit "+seat+" at "+cafe.body.transform.position);
        }
        yield return EnterByE(12);yield return new WaitForSecondsRealtime(.55f);
        Check(!cafe.IsCinemaSeat&&!cafe.CinemaFocused&&Mathf.Abs(cafe.eyes.fieldOfView-72)<.01f,"retained chair keeps original camera without sofa focus");
        yield return Press(Key.E);Check(cafe.CurrentSeat==-1,"retained chair still exits normally");
        // Exercise the production validation/ownership helpers in-process; this is not six-client WAN acceptance.
        var actors=(Dictionary<string,CafeRoomSync.Actor>)typeof(CafeRoomSync).GetField("actors",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(sync);
        for(int seat=6;seat<13;seat++)Check((bool)Call(sync,"ValidActor",new CafeRoomSync.Actor{id="fixture",seat=seat}),"guest accepts configured seat "+seat);
        foreach(int seat in new[]{-2,13,int.MaxValue})Check(!(bool)Call(sync,"ValidActor",new CafeRoomSync.Actor{id="fixture",seat=seat}),"guest rejects invalid seat "+seat);
        var allSeats=cafe.seats;
        try{cafe.seats=allSeats.Take(8).ToArray();Check(!(bool)Call(sync,"ValidActor",new CafeRoomSync.Actor{id="fixture",seat=8}),"legacy eight-seat scene still rejects index eight");}
        finally{cafe.seats=allSeats;}
        for(int seat=6;seat<12;seat++)actors["fixture-"+seat]=new CafeRoomSync.Actor{id="fixture-"+seat,seat=seat};
        for(int seat=6;seat<12;seat++)
        {
            Check(!cafe.SeatAvailable(seat)&&(bool)Call(sync,"SeatTaken",seat,"another-player"),"occupied sofa spot rejected by local and host helpers "+seat);
            cafe.Sit(seat);Check(cafe.CurrentSeat==-1&&cafe.ExitWarning.Contains("已有人"),"occupied activation gives feedback "+seat);
            Check(!(bool)Call(sync,"SeatTaken",seat,"fixture-"+seat),"owner identity excluded from own seat conflict "+seat);
        }
        actors.Clear();
        for(int seat=6;seat<12;seat++)Check(cafe.SeatAvailable(seat),"released sofa spot available "+seat);
        var appearances=new List<GameObject>();var bounds=new List<Bounds>();
        var library=GetComponent<CafeAvatarLibrary>();
        for(int i=0;i<6;i++)
        {
            var go=new GameObject("CinemaSpacingFixture_"+i);go.transform.SetPositionAndRotation(cinema.AvatarPosition(i+6),cafe.seats[i+6].rotation);
            var avatar=library.Create(new[]{"Rhino","Hippo","Corgi","Duck","Shark","Platypus"}[i],go.transform);Check(avatar!=null,"avatar fixture loaded "+i);
            var renderers=avatar.GetComponentsInChildren<Renderer>();var b=renderers[0].bounds;foreach(var r in renderers)b.Encapsulate(r.bounds);bounds.Add(b);appearances.Add(go);
        }
        yield return null;yield return new WaitForEndOfFrame();bounds.Clear();
        foreach(var go in appearances)
        {
            var renderers=go.GetComponentsInChildren<Renderer>();var b=renderers[0].bounds;
            foreach(var r in renderers){b.Encapsulate(r.bounds);Debug.Log("CINEMA_AVATAR_RENDERER "+go.name+"/"+r.name+" bounds="+r.bounds);}
            bounds.Add(b);
        }
        RenderOverview("six-seat-spacing",new Vector3(8.0f,3.0f,.0f),new Vector3(2.7f,1.2f,-.55f));
        var footprints=appearances.Select(Vertices).ToArray();
        for(int i=0;i<footprints.Length;i++)Check(footprints[i].Length>0&&footprints[i].All(p=>float.IsFinite(p.x)&&float.IsFinite(p.y)&&float.IsFinite(p.z)),"nonempty finite baked avatar footprint "+i);
        for(int i=0;i<6;i++)for(int j=i+1;j<6;j++)
        {
            float gap=Separation(footprints[i],footprints[j]);
            Check(gap>.02f,"six actual avatar footprints separated "+i+"/"+j+" gap="+gap);
        }
        foreach(var go in appearances)Destroy(go);
        Debug.Log("CINEMA_LOCAL_OWNERSHIP_AND_SPACING_PASS not a live six-client networking claim");
    }
    void Measure(int seat,string mode,float target)
    {
        float minX=1,minY=1,maxX=0,maxY=0;
        for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)
        {
            var p=cafe.eyes.WorldToViewportPoint(cafe.publicScreen.transform.TransformPoint(new Vector3(x*.5f,y*.5f,0)));
            Check(p.z>0&&p.x>=.045f&&p.x<=.955f&&p.y>=.045f&&p.y<=.955f,"complete screen corner "+seat+"/"+mode+"/"+x+"/"+y);
            minX=Mathf.Min(minX,p.x);maxX=Mathf.Max(maxX,p.x);minY=Mathf.Min(minY,p.y);maxY=Mathf.Max(maxY,p.y);
        }
        float extent=Mathf.Max(maxX-minX,maxY-minY);
        Check(extent>target-.005f&&extent<target+.005f,"large preserved image "+seat+"/"+mode+" coverage="+extent);
        measurements.Add(new Measurement{seat=seat,mode=mode,aspect=cafe.eyes.aspect,fov=cafe.eyes.fieldOfView,coverage=extent,position=cafe.eyes.transform.position});
    }
    void CheckSightline(int seat)
    {
        foreach(float x in new[]{-.49f,0,.49f})foreach(float y in new[]{-.49f,0,.49f})
        {
            var point=cafe.publicScreen.transform.TransformPoint(new Vector3(x,y,0));var direction=point-cafe.eyes.transform.position;
            foreach(var hit in Physics.RaycastAll(cafe.eyes.transform.position,direction.normalized,direction.magnitude-.06f,~(1<<28),QueryTriggerInteraction.Ignore))
                Debug.Log("CINEMA_EDGE_OCCLUSION seat="+seat+" focus="+cinema.Focused+" x="+x+" y="+y+" collider="+hit.collider.name+" point="+hit.point);
        }
        foreach(float x in new[]{-.49f,-.25f,0,.25f,.49f})foreach(float y in new[]{-.49f,-.25f,0,.25f,.49f})
        {
            var point=cafe.publicScreen.transform.TransformPoint(new Vector3(x,y,0));var direction=point-cafe.eyes.transform.position;
            var hits=Physics.RaycastAll(cafe.eyes.transform.position,direction.normalized,direction.magnitude-.06f,~(1<<28),QueryTriggerInteraction.Ignore);
            Check(hits.Length==0,"unobstructed screen sightline "+seat+"/"+x+"/"+y+" "+string.Join(",",hits.Select(h=>h.collider.name)));
        }
    }
    static Vector3[] Vertices(GameObject root)
    {
        var points=new List<Vector3>();
        foreach(var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var mesh=new Mesh();renderer.BakeMesh(mesh,true);
            points.AddRange(mesh.vertices.Select(renderer.transform.TransformPoint));Destroy(mesh);
        }
        foreach(var filter in root.GetComponentsInChildren<MeshFilter>())
            points.AddRange(filter.sharedMesh.vertices.Select(filter.transform.TransformPoint));
        if(points.Count>0){var bounds=new Bounds(points[0],Vector3.zero);foreach(var p in points)bounds.Encapsulate(p);Debug.Log("CINEMA_BAKED_WORLD "+root.name+" vertices="+points.Count+" bounds="+bounds);}
        return points.ToArray();
    }
    static float Separation(Vector3[] a,Vector3[] b)
    {
        float gap=float.NegativeInfinity;
        for(int degree=0;degree<180;degree+=2)
        {
            var axis=new Vector3(Mathf.Cos(degree*Mathf.Deg2Rad),0,Mathf.Sin(degree*Mathf.Deg2Rad));
            float minA=float.PositiveInfinity,maxA=float.NegativeInfinity,minB=minA,maxB=maxA;
            foreach(var p in a){float d=Vector3.Dot(p,axis);minA=Mathf.Min(minA,d);maxA=Mathf.Max(maxA,d);}
            foreach(var p in b){float d=Vector3.Dot(p,axis);minB=Mathf.Min(minB,d);maxB=Mathf.Max(maxB,d);}
            gap=Mathf.Max(gap,minB-maxA,minA-maxB);
        }
        return gap;
    }
    bool ClearStanding(Vector3 p)
    {
        var mid=p+cafe.body.center;float half=Mathf.Max(0,cafe.body.height*.5f-(cafe.body.radius+.02f));
        return !Physics.OverlapCapsule(mid-Vector3.up*half,mid+Vector3.up*half,cafe.body.radius+.02f,~(1<<28),QueryTriggerInteraction.Ignore).Any(c=>!c.transform.IsChildOf(cafe.body.transform));
    }
    void DiagnoseExit()
    {
        var mid=cafe.body.transform.position+cafe.body.center;
        float oldHalf=cafe.body.height*.5f-cafe.body.radius;
        foreach(var c in Physics.OverlapCapsule(mid-Vector3.up*oldHalf,mid+Vector3.up*oldHalf,cafe.body.radius+.02f,~(1<<28),QueryTriggerInteraction.Ignore))
            if(!c.transform.IsChildOf(cafe.body.transform))Debug.Log("CINEMA_EXIT_OLD_PROBE_OVERLAP "+c.name+" bounds="+c.bounds+" foot="+cafe.body.transform.position.y+" skin="+cafe.body.skinWidth);
    }
    IEnumerator EnterByE(int seat)
    {
        var target=FindObjectsByType<CafeInteractable>(FindObjectsSortMode.None).Single(t=>t.name=="SofaInteraction_"+seat);bool found=false;
        foreach(float radius in new[]{1.0f,1.35f,1.7f,2.1f,2.5f})
        {
            for(int i=0;i<72&&!found;i++)
            {
                var p=target.transform.position+Quaternion.Euler(0,i*5,0)*Vector3.forward*radius;p.y=.06f;
                if(!ClearStanding(p))continue;var head=p+Vector3.up*cafe.WalkingEyeHeight;var direction=target.transform.position-head;
                if(!Physics.Raycast(head,direction.normalized,out var hit,3,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Collide)||hit.collider.GetComponentInParent<CafeInteractable>()!=target)continue;
                cafe.body.enabled=false;cafe.body.transform.position=p;cafe.body.enabled=true;
                var rotation=Quaternion.LookRotation(direction);cafe.eyes.transform.rotation=rotation;
                typeof(CafePrototype).GetField("yaw",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(cafe,rotation.eulerAngles.y);
                typeof(CafePrototype).GetField("pitch",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(cafe,Mathf.DeltaAngle(0,rotation.eulerAngles.x));found=true;
            }
            if(found)break;
        }
        Check(found,"physical standing approach and gaze for sofa "+seat);yield return null;yield return null;
        Check(cafe.InteractionHint.Contains(seat==12?"坐下休息":"坐下看大屏"),"correct seat gaze prompt "+seat);
        if(seat==6)
        {
            var actors=(Dictionary<string,CafeRoomSync.Actor>)typeof(CafeRoomSync).GetField("actors",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(sync);
            actors["gaze-fixture"]=new CafeRoomSync.Actor{id="gaze-fixture",seat=seat};yield return null;yield return null;
            Check(GetComponent<CafeHud>().interactionHint.text.Contains("已占用"),"unchanged gaze refreshes visible occupied hint");
            actors.Remove("gaze-fixture");yield return null;yield return null;
            Check(GetComponent<CafeHud>().interactionHint.text.Contains("坐下看大屏"),"unchanged gaze refreshes released-seat hint");
        }
        yield return Press(Key.E);Check(cafe.CurrentSeat==seat,"actual E enters specific sofa seat "+seat);
    }
    IEnumerator Press(Key key)
    {yield return FocusWindow();InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState(key));yield return null;yield return null;InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState());yield return null;}
    IEnumerator FocusWindow()
    {
        for(int attempt=0;attempt<3&&!Application.isFocused;attempt++)
        {
            uint from=GetCurrentThreadId(),to=GetWindowThreadProcessId(GetForegroundWindow(),IntPtr.Zero);
            bool attached=from!=to&&to!=0&&AttachThreadInput(from,to,true);
            try{ShowWindow(ownWindow,9);SetForegroundWindow(ownWindow);}
            finally{if(attached)AttachThreadInput(from,to,false);}
            yield return new WaitForSecondsRealtime(.2f);
        }
        Check(Application.isFocused,"owned game window focused for real input");
    }
    Button FindButton(string name)=>GetComponent<CafeHud>().canvas.GetComponentsInChildren<Button>().FirstOrDefault(b=>b.name==name&&b.IsInteractable());
    IEnumerator Click(Button button)
    {
        yield return FocusWindow();
        Check(button!=null,"button exists for pointer dispatch");Canvas.ForceUpdateCanvases();yield return new WaitForEndOfFrame();
        var r=(RectTransform)button.transform;var p=RectTransformUtility.WorldToScreenPoint(null,r.TransformPoint(r.rect.center));
        var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=p},hits);
        Check(hits.Count>0&&hits[0].gameObject.GetComponentInParent<Button>()==button,"unobstructed UI pointer "+button.name);
        InputSystem.QueueStateEvent(Mouse.current,new MouseState{position=p});yield return null;
        InputSystem.QueueStateEvent(Mouse.current,new MouseState{position=p}.WithButton(MouseButton.Left));yield return null;
        InputSystem.QueueStateEvent(Mouse.current,new MouseState{position=p});yield return null;yield return null;
    }
    IEnumerator Shot(string name)
    {
        yield return new WaitForEndOfFrame();var texture=ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(Path.Combine(folder,name+".png"),texture.EncodeToPNG());Destroy(texture);
    }
    void RenderOverview(string name,Vector3 position,Vector3 target)
    {
        var camera=cafe.eyes;var oldPosition=camera.transform.position;var oldRotation=camera.transform.rotation;float oldFov=camera.fieldOfView;
        var oldTarget=camera.targetTexture;var oldActive=RenderTexture.active;var rt=RenderTexture.GetTemporary(1440,900,24);
        try
        {
            camera.transform.SetPositionAndRotation(position,Quaternion.LookRotation(target-position));camera.fieldOfView=70;camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
            var texture=new Texture2D(1440,900,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,1440,900),0,0);texture.Apply();File.WriteAllBytes(Path.Combine(folder,name+".png"),texture.EncodeToPNG());Destroy(texture);
        }
        finally{camera.transform.SetPositionAndRotation(oldPosition,oldRotation);camera.fieldOfView=oldFov;camera.targetTexture=oldTarget;RenderTexture.active=oldActive;RenderTexture.ReleaseTemporary(rt);}
    }
}
#endif

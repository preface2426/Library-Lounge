using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed class CafeRoomTransition : MonoBehaviour
{
    CafePrototype cafe;
    Canvas canvas;
    CanvasGroup group;
    CafeKeyGraphic key,outline;
    TMP_Text brand,subtitle;
    Texture2D frame;
    Action complete;
    float elapsed;
    double startedAt;
    bool leaving;
    int width, height;
    public bool Active => complete != null;
    public bool Leaving => leaving;
    public void Configure(CafePrototype owner, TMP_FontAsset body, TMP_FontAsset heading)
    {
        cafe = owner;
        var go = new GameObject("RoomKeyTransition", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup), typeof(Image));
        go.transform.SetParent(transform, false);
        canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 240;
        go.GetComponent<Image>().color = CafeUiTheme.Paper;
        group = go.GetComponent<CanvasGroup>();
        var graphic = new GameObject("ContactKey", typeof(RectTransform), typeof(CanvasRenderer), typeof(CafeKeyGraphic));
        graphic.transform.SetParent(go.transform, false);
        var rt = (RectTransform)graphic.transform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        key = graphic.GetComponent<CafeKeyGraphic>(); key.raycastTarget = false;
        var line=new GameObject("KeyOutline",typeof(RectTransform),typeof(CanvasRenderer),typeof(CafeKeyGraphic));line.transform.SetParent(go.transform,false);
        var lineRect=(RectTransform)line.transform;lineRect.anchorMin=Vector2.zero;lineRect.anchorMax=Vector2.one;lineRect.offsetMin=lineRect.offsetMax=Vector2.zero;
        outline=line.GetComponent<CafeKeyGraphic>();outline.OutlineOnly=true;outline.raycastTarget=false;
        brand=Brand(go.transform,"TransitionBrand","LIBRARY LOUNGE",heading,29);
        subtitle=Brand(go.transform,"TransitionSubtitle","线上网吧",body,13);
        var skip = new GameObject("SkipTransition", typeof(RectTransform), typeof(Image), typeof(Button),typeof(Outline));
        skip.transform.SetParent(go.transform, false);
        var sr = (RectTransform)skip.transform; sr.anchorMin = sr.anchorMax = new Vector2(1, 0); sr.pivot = new Vector2(1, 0); sr.sizeDelta = new Vector2(130, 50); sr.anchoredPosition = new Vector2(-25, 20);
        skip.GetComponent<Image>().color = CafeUiTheme.Paper;
        skip.GetComponent<Button>().onClick.AddListener(Finish);
        skip.GetComponent<Button>().navigation = new Navigation { mode = Navigation.Mode.None };
        var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI)); label.transform.SetParent(skip.transform, false);
        var lr = (RectTransform)label.transform; lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one; lr.offsetMin = lr.offsetMax = Vector2.zero;
        var text = label.GetComponent<TMP_Text>(); text.font = body; text.fontSize = 17; text.text = "跳过  Esc"; text.color = CafeUiTheme.InkSoft; text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false;
        skip.GetComponent<Outline>().effectColor=new Color(.68f,.57f,.4f,.32f);skip.GetComponent<Outline>().effectDistance=Vector2.one;
        go.SetActive(false);
    }
    static TMP_Text Brand(Transform parent,string name,string value,TMP_FontAsset font,int size)
    {
        var go=new GameObject(name,typeof(RectTransform),typeof(TextMeshProUGUI));go.transform.SetParent(parent,false);
        var text=go.GetComponent<TMP_Text>();text.text=value;text.font=font;text.fontSize=size;text.characterSpacing=5;text.alignment=TextAlignmentOptions.Center;text.raycastTarget=false;
        return text;
    }
    public void Play(bool exit, Action done)
    {
        if (Active) return;
        if (PlayerPrefs.GetInt("CafeReduceMotion", 0) != 0 || !Application.isFocused) { done(); return; }
        if(exit)CaptureCamera();
        leaving = exit; elapsed = 0; complete = done; width = Screen.width; height = Screen.height;
        canvas.gameObject.SetActive(true); group.alpha = 1;
        // Like the webpage, install the destination under the opaque transition before revealing it.
        complete=exit?()=>{}:()=>cafe.Continue();
        done();
        if(!exit)CaptureCamera();
        cafe.OpenMenu();
        key.Texture = frame;startedAt=Time.realtimeSinceStartupAsDouble;SetTime(exit?2.9f:0);
        var skip = canvas.GetComponentInChildren<Button>();
        UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(skip.gameObject);
    }
    void CaptureCamera()
    {
        var camera = cafe.eyes;
        ReleaseFrame();
        if (camera == null) return;
        var target = RenderTexture.GetTemporary(Mathf.Min(Screen.width, 1920), Mathf.Min(Screen.height, 1080), 24);
        RenderTexture oldTarget = camera.targetTexture, oldActive = RenderTexture.active;
        bool wasEnabled = camera.enabled;
        try
        {
            camera.enabled = true; camera.targetTexture = target;
            // URP cameras render through their pipeline on Render(). No overlay UI is captured.
            camera.Render(); RenderTexture.active = target;
            frame = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            frame.ReadPixels(new Rect(0,0,target.width,target.height), 0,0); frame.Apply();
        }
        finally { camera.enabled = wasEnabled; camera.targetTexture = oldTarget; RenderTexture.active = oldActive; RenderTexture.ReleaseTemporary(target); }
    }
    void Update()
    {
        if (!Active) return;
        if (width != Screen.width || height != Screen.height || PlayerPrefs.GetInt("CafeReduceMotion",0) != 0) { Finish(); return; }
        elapsed=(float)(Time.realtimeSinceStartupAsDouble-startedAt);
        SetTime(leaving?2.9f-elapsed:elapsed);
        if (elapsed >= 2.9f) Finish();
    }
    void SetTime(float time)
    {
        key.TimePosition=outline.TimePosition=time;key.SetVerticesDirty();outline.SetVerticesDirty();
        float fade=1-CafeWebMotion.Key(Mathf.InverseLerp(2.58f,2.9f,time));
        // The page reverses the assembly timeline, but its independent screen fade stays forward.
        group.alpha=1-CafeWebMotion.Key(Mathf.InverseLerp(2.58f,2.9f,elapsed));
        float scale=CafeKeyGraphic.LayoutScale(new Rect(0,0,Screen.width,Screen.height));
        float top=Screen.height*.5f+101*scale+30;
        float opacity=time<.406f?Mathf.InverseLerp(.1f,.406f,time):1-Mathf.InverseLerp(1.324f,1.8f,time);
        brand.gameObject.SetActive(Screen.height>=460);subtitle.gameObject.SetActive(Screen.height>=460);
        PlaceBrand(brand,top,new Color(.2f,.16f,.12f,opacity));
        PlaceBrand(subtitle,top+52,new Color(.506f,.392f,.227f,opacity));
        // The circle is outside the assembly in the webpage: keep its full scene opaque on exit.
        // CafeKeyGraphic applies the assembly fade to key/strips vertices only.
        outline.canvasRenderer.SetAlpha(fade);
    }
    public void Finish()
    {
        if (!Active) return;
        Action done = complete; complete = null;
        canvas.gameObject.SetActive(false);
        ReleaseFrame();
        done();
    }
    public void FocusSkip()
    {
        if (Active) UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(canvas.GetComponentInChildren<Button>().gameObject);
    }
    public void Cancel()
    {
        complete = null;
        if (canvas != null) canvas.gameObject.SetActive(false);
        ReleaseFrame();
    }
    static void PlaceBrand(TMP_Text text,float top,Color color)
    {
        var rt=text.rectTransform;rt.anchorMin=new Vector2(0,1);rt.anchorMax=new Vector2(1,1);rt.pivot=new Vector2(.5f,1);
        rt.sizeDelta=new Vector2(-48,44);rt.anchoredPosition=new Vector2(0,-top);text.color=color;
    }
    void ReleaseFrame()
    {
        if(key!=null)key.Texture=null;
        if(frame!=null)Destroy(frame);
        frame=null;
    }
    void OnApplicationFocus(bool focused) { if (!focused) Finish(); }
    void OnDestroy() { complete = null; ReleaseFrame(); }
}

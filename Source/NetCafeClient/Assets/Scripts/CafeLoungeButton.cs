using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class CafeLoungeButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
{
    Button button;
    CafeLoungeGraphic surface;
    TMP_Text label,ghost,arrow,arrowCopy;
    bool primary,dark,over,selected,pressed,lastInteractable,link,nativePointer,nativeHover;
    public void SetNativeHover(bool ownsPointer,bool hovered)
    {
        if(nativePointer!=ownsPointer)over=false;
        nativePointer=ownsPointer;nativeHover=ownsPointer&&hovered;
    }
    public void UseLinkStyle(){link=true;Paint();}
    float progress,from,target,elapsed=10,roll,rollFrom,rollTarget,rollElapsed=10;
    Vector2 labelPosition;
    Vector2 rollSize;
    string rollText;
    float rollFontSize;
    Color rollColor;
    public void Configure(bool filled,bool onDark=false)
    {
        primary=filled;dark=onDark;button=GetComponent<Button>();label=GetComponentInChildren<TMP_Text>();
        foreach(var outline in GetComponents<Outline>())outline.enabled=false;
        var hit=GetComponent<Image>();if(hit!=null)hit.color=Color.clear;
        var root=new GameObject("LoungeSurface",typeof(RectTransform),typeof(CanvasRenderer),typeof(CafeLoungeGraphic));
        var rt=(RectTransform)root.transform;rt.SetParent(transform,false);rt.SetAsFirstSibling();
        rt.anchorMin=Vector2.zero;rt.anchorMax=Vector2.one;rt.offsetMin=rt.offsetMax=Vector2.zero;
        surface=root.GetComponent<CafeLoungeGraphic>();surface.raycastTarget=false;
        button.transition=Selectable.Transition.None;
        if(label!=null)
        {
            label.fontStyle=FontStyles.Normal;label.fontSize=Mathf.Min(label.fontSize,20);
            label.rectTransform.offsetMin=new Vector2(dark?13:20,0);label.rectTransform.offsetMax=new Vector2(-20,0);
            label.raycastTarget=false;labelPosition=label.rectTransform.anchoredPosition;
            if(primary)
            {
                if(label.text.EndsWith("↗"))label.text=label.text.Substring(0,label.text.Length-1).TrimEnd();
                var box=new GameObject("ArrowBox",typeof(RectTransform),typeof(RectMask2D));box.transform.SetParent(transform,false);
                var br=(RectTransform)box.transform;br.anchorMin=br.anchorMax=br.pivot=new Vector2(1,.5f);br.sizeDelta=new Vector2(29,28);br.anchoredPosition=new Vector2(-18,0);
                arrow=MakeArrow(box.transform,0);arrowCopy=MakeArrow(box.transform,-32);
                label.rectTransform.offsetMax=new Vector2(-52,0);labelPosition=label.rectTransform.anchoredPosition;
            }
            else if(!dark)
            {
                var mask=new GameObject("TextClip",typeof(RectTransform),typeof(RectMask2D));mask.transform.SetParent(transform,false);
                var mr=(RectTransform)mask.transform;mr.anchorMin=Vector2.zero;mr.anchorMax=Vector2.one;mr.offsetMin=mr.offsetMax=Vector2.zero;
                label.transform.SetParent(mask.transform,false);label.rectTransform.anchorMin=Vector2.zero;label.rectTransform.anchorMax=Vector2.one;
                label.rectTransform.offsetMin=new Vector2(20,0);label.rectTransform.offsetMax=new Vector2(-20,0);
                ghost=Instantiate(label,label.transform.parent);ghost.name="RollSecond";ghost.raycastTarget=false;
            }
        }
        Paint();
    }
    TMP_Text MakeArrow(Transform holder,float x)
    {
        var go=new GameObject("Arrow",typeof(RectTransform),typeof(TextMeshProUGUI));go.transform.SetParent(holder,false);
        var text=go.GetComponent<TMP_Text>();text.font=label.font;text.text="→";text.fontSize=24;text.alignment=TextAlignmentOptions.Center;text.raycastTarget=false;
        var rt=text.rectTransform;rt.anchorMin=rt.anchorMax=rt.pivot=new Vector2(.5f,.5f);rt.sizeDelta=new Vector2(29,28);rt.anchoredPosition=new Vector2(x,0);return text;
    }
    void LateUpdate()
    {
        if(button==null)return;
        float wanted=button.IsInteractable()&&(nativePointer?nativeHover:over||selected)?1:0;
        if(wanted!=target)
        {
            from=progress;target=wanted;elapsed=0;
            rollFrom=roll;rollTarget=wanted;rollElapsed=0;
        }
        bool reduce=PlayerPrefs.GetInt("CafeReduceMotion",0)!=0;
        elapsed+=Time.unscaledDeltaTime;rollElapsed+=Time.unscaledDeltaTime;
        float next=reduce?target:Mathf.Lerp(from,target,CafeWebMotion.Out(Mathf.Clamp01(elapsed/.64f)));
        if(!Mathf.Approximately(next,progress)||lastInteractable!=button.IsInteractable()||elapsed<.7f)
        { progress=next;lastInteractable=button.IsInteractable();Paint(); }
        if(ghost!=null && (rollElapsed<=.48f+Mathf.Min(40,label.text.Length)*.022f || rollText!=label.text || rollFontSize!=label.fontSize || rollColor!=label.color || rollSize!=label.rectTransform.rect.size || label.havePropertiesChanged))
        {
            ghost.text=label.text;ghost.font=label.font;ghost.fontSize=label.fontSize;ghost.color=label.color;
            float duration=.4f+Mathf.Min(40,label.text.Length)*.022f;
            roll=reduce?rollTarget:Mathf.Lerp(rollFrom,rollTarget,Mathf.Clamp01(rollElapsed/duration));
            Roll(label,false,reduce);Roll(ghost,true,reduce);
            rollText=label.text;rollFontSize=label.fontSize;rollColor=label.color;rollSize=label.rectTransform.rect.size;
        }
    }
    void Roll(TMP_Text text,bool second,bool reduce)
    {
        text.ForceMeshUpdate();var info=text.textInfo;
        for(int i=0;i<info.characterCount;i++)
        {
            var ch=info.characterInfo[i];if(!ch.isVisible)continue;
            float local=reduce?rollTarget:CafeWebMotion.Out(Mathf.Clamp01((rollElapsed-i*.022f)/.4f));
            float t=Mathf.Lerp(rollFrom,rollTarget,local);
            float shift=(second?1-t:-t)*text.fontSize*.85f;
            var mesh=info.meshInfo[ch.materialReferenceIndex];
            for(int j=0;j<4;j++) {mesh.vertices[ch.vertexIndex+j].y+=shift;var color=mesh.colors32[ch.vertexIndex+j];color.a=(byte)(color.a*(second?t:1-t));mesh.colors32[ch.vertexIndex+j]=color;}
        }
        text.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices|TMP_VertexDataUpdateFlags.Colors32);
    }
    void Paint()
    {
        if(surface==null)return;
        surface.Radius=primary?Mathf.Lerp(34,2,progress):0;
        surface.RisingFill=primary;surface.Reveal=progress;
        surface.Fill=link?new Color(.7f,.6f,.4f,progress*.09f):primary?CafeUiTheme.Ink:dark?Color.Lerp(new Color(.157f,.149f,.118f,.77f),new Color(.224f,.208f,.169f,.91f),progress):Color.clear;
        surface.Stroke=primary?Color.Lerp(CafeUiTheme.Amber,CafeUiTheme.Ink,progress):dark?Color.Lerp(new Color(.78f,.67f,.46f,.35f),new Color(.87f,.76f,.55f,1),progress):selected?CafeUiTheme.Amber:Color.clear;
        if(link)surface.Stroke=new Color(.78f,.67f,.46f,progress*.65f);
        if(label!=null)
        {
            label.color=dark?new Color(1,.953f,.863f):Color.Lerp(CafeUiTheme.Ink,CafeUiTheme.Paper,primary?Mathf.Clamp01(progress*2):0);
            if(primary)label.rectTransform.anchoredPosition=labelPosition+new Vector2(progress*4,pressed?-1:0);
        }
        if(arrow!=null)
        {
            arrow.color=arrowCopy.color=label.color;
            arrow.rectTransform.anchoredPosition=new Vector2(progress*32,0);arrowCopy.rectTransform.anchoredPosition=new Vector2(-32+progress*32,0);
        }
        if(button!=null&&!button.IsInteractable())
        {
            var color=surface.Fill;color.a*=.45f;surface.Fill=color;
            if(label!=null)label.color=dark?new Color(.8f,.77f,.7f,.5f):CafeUiTheme.InkSoft;
        }
        surface.SetVerticesDirty();
    }
    public void OnPointerEnter(PointerEventData e){over=true;}
    public void OnPointerExit(PointerEventData e){over=false;}
    public void OnPointerDown(PointerEventData e){pressed=true;selected=false;Paint();}
    public void OnPointerUp(PointerEventData e){pressed=false;Paint();}
    public void OnSelect(BaseEventData e){selected=e is AxisEventData||Keyboard.current?.anyKey.wasPressedThisFrame==true||Gamepad.current!=null&&Gamepad.current.dpad.ReadValue().sqrMagnitude>.1f;}
    public void OnDeselect(BaseEventData e){selected=false;}
    void OnDisable(){nativePointer=nativeHover=over=selected=pressed=false;progress=from=target=roll=rollFrom=rollTarget=0;elapsed=rollElapsed=10;Paint();}
}

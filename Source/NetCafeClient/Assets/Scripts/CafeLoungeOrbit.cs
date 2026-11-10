using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed class CafeLoungeOrbit : MaskableGraphic
{
    float phase, hover;
    Vector2 pointer;
    protected override void Awake() { base.Awake(); raycastTarget = false; }
    void Update()
    {
        float oldPhase=phase,oldHover=hover;Vector2 oldPointer=pointer;
        bool reduced = PlayerPrefs.GetInt("CafeReduceMotion", 0) != 0;
        Vector2 local = Vector2.zero;
        bool inside = Mouse.current != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, Mouse.current.position.ReadValue(), null, out local) && rectTransform.rect.Contains(local);
        hover = Mathf.MoveTowards(hover, inside ? 1 : 0, Time.unscaledDeltaTime * 2);
        pointer = Vector2.Lerp(pointer, inside && !reduced ? Vector2.ClampMagnitude(local * .05f, 9) : Vector2.zero, 1 - Mathf.Exp(-Time.unscaledDeltaTime * 6));
        if (!reduced) phase += Time.unscaledDeltaTime * (3 + hover * 13);
        if(oldPhase!=phase||oldHover!=hover||(oldPointer-pointer).sqrMagnitude>.000001f)SetVerticesDirty();
    }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        float radius = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * .41f;
        Vector2 center = rectTransform.rect.center;
        Color tint = CafeUiTheme.Amber; tint.a = .42f + hover * .18f;
        for (int ring=0;ring<3;ring++) {
            Vector2 c=center+pointer*(ring-1)*.65f;
            float tilt=(-28+ring*32+Mathf.Sin(phase*Mathf.Deg2Rad+ring)*8)*Mathf.Deg2Rad;
            float rx=radius*(1-ring*.075f),ry=radius*(.64f-ring*.065f+hover*.05f);
            Vector2 Point(float angle) {
                Vector2 p=new Vector2(Mathf.Cos(angle)*rx,Mathf.Sin(angle)*ry);
                return c+new Vector2(p.x*Mathf.Cos(tilt)-p.y*Mathf.Sin(tilt),p.x*Mathf.Sin(tilt)+p.y*Mathf.Cos(tilt));
            }
            Color line=tint;line.a*=ring==0?1:.55f;
            for(int i=0;i<160;i++)CafeLoungeGraphic.Line(vh,Point(i*Mathf.PI*2/160),Point((i+1)*Mathf.PI*2/160),.75f,line);
            float a=(phase*(ring==1?-.6f:1)+ring*131)*Mathf.Deg2Rad;
            Vector2 node=Point(a);CafeLoungeGraphic.Line(vh,node-Vector2.one*1.5f,node+Vector2.one*1.5f,2,tint);
        }
        CafeLoungeGraphic.Line(vh, center - Vector2.right * 4, center + Vector2.right * 4, .7f, tint);
        CafeLoungeGraphic.Line(vh, center - Vector2.up * 4, center + Vector2.up * 4, .7f, tint);
    }
    static void Arc(VertexHelper vh, Vector2 center, float r, float from, float length, Color tint)
    {
        for (int i = 0; i < 90; i++)
        {
            float a = (from + length * i / 90) * Mathf.Deg2Rad, b = (from + length * (i + 1) / 90) * Mathf.Deg2Rad;
            CafeLoungeGraphic.Line(vh, center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r,
                center + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * r, .75f, tint);
        }
    }
}

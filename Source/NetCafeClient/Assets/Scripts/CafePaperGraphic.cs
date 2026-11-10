using UnityEngine;
using UnityEngine.UI;

// Soft paper illumination, matching the approved preview without an external texture.
public sealed class CafePaperGraphic : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();Rect r=rectTransform.rect;const int steps=16;
        for(int y=0;y<=steps;y++)for(int x=0;x<=steps;x++) {
            float u=(float)x/steps,v=(float)y/steps;
            float glow=Mathf.Clamp01(1-Vector2.Distance(new Vector2(u,v),new Vector2(.53f,.63f))*1.8f)*.48f;
            Color c=Color.Lerp(CafeUiTheme.Paper,new Color(1,.99f,.96f),glow);
            vh.AddVert(new Vector3(Mathf.Lerp(r.xMin,r.xMax,u),Mathf.Lerp(r.yMin,r.yMax,v)),c,new Vector2(u,v));
        }
        for(int y=0;y<steps;y++)for(int x=0;x<steps;x++){int i=y*(steps+1)+x;vh.AddTriangle(i,i+steps+1,i+1);vh.AddTriangle(i+1,i+steps+1,i+steps+2);}
    }
}

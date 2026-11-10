using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

// Native uGUI geometry, shared by every primary/link/HUD button.
public sealed class CafeLoungeGraphic : MaskableGraphic
{
    public float Radius = 25;
    public Color Fill = CafeUiTheme.Accent;
    public Color Stroke = CafeUiTheme.Amber;
    public float Reveal = 1;
    public bool RisingFill;
    readonly List<Vector2> clipped=new(),scratch=new();
    readonly Vector2[] points=new Vector2[36];
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        float radius = Mathf.Clamp(Radius, 0, Mathf.Min(r.width, r.height) * .5f);
        const int steps = 8;
        for (int corner = 0; corner < 4; corner++)
        {
            Vector2 center = new Vector2(corner == 0 || corner == 3 ? r.xMax - radius : r.xMin + radius,
                corner < 2 ? r.yMax - radius : r.yMin + radius);
            for (int j = 0; j <= steps; j++)
            {
                float angle = (corner * 90f + j * 90f / steps) * Mathf.Deg2Rad;
                points[corner * (steps + 1) + j] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            }
        }
        Color fill = Fill * color, stroke = Stroke * color;
        if(RisingFill)
        {
            clipped.Clear();
            clipped.Add(new Vector2(r.xMin,r.yMin-r.height));
            clipped.Add(new Vector2(r.xMax,r.yMin-r.height));
            for(int i=0;i<=48;i++)
            {
                float x=1-i/24f;
                float y=r.yMin+r.height*(Reveal*1.03f-.03f)-r.height*.45f*(1-Reveal)*(1-Mathf.Sqrt(Mathf.Max(0,1-x*x)));
                clipped.Add(new Vector2(r.center.x+x*r.width*.5f,y));
            }
            // Intersect the rising CSS pseudo-element with the rounded button outline.
            for(int edge=0;edge<points.Length&&clipped.Count>0;edge++)
            {
                scratch.Clear();Vector2 a=points[edge],b=points[(edge+1)%points.Length],previous=clipped[clipped.Count-1];
                float Side(Vector2 p)=>(b.x-a.x)*(p.y-a.y)-(b.y-a.y)*(p.x-a.x);
                foreach(var current in clipped)
                {
                    float p=Side(previous),c=Side(current);
                    if((p>=0)!=(c>=0))scratch.Add(Vector2.LerpUnclamped(previous,current,p/(p-c)));
                    if(c>=0)scratch.Add(current);
                    previous=current;
                }
                clipped.Clear();clipped.AddRange(scratch);
            }
            if(clipped.Count>=3)
            {
                foreach(var point in clipped)vh.AddVert(point,fill,Vector2.zero);
                for(int i=1;i<clipped.Count-1;i++)vh.AddTriangle(0,i,i+1);
            }
        }
        else
        {
            vh.AddVert(r.center, fill, Vector2.zero);
            foreach (Vector2 point in points) vh.AddVert(point, fill, Vector2.zero);
            for(int i=0;i<points.Length;i++)vh.AddTriangle(0,i+1,(i+1)%points.Length+1);
        }
        for (int i = 0; i < points.Length; i++)
        {
            int n = (i + 1) % points.Length;
            Line(vh, points[i], points[n], Mathf.Max(.8f,1/Mathf.Max(.1f,canvas!=null?canvas.scaleFactor:1)), stroke);
        }
    }
    public static void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color tint)
    {
        Vector2 normal = new Vector2(-(b - a).y, (b - a).x).normalized * width * .5f;
        int start = vh.currentVertCount;
        vh.AddVert(a - normal, tint, Vector2.zero); vh.AddVert(a + normal, tint, Vector2.zero);
        vh.AddVert(b + normal, tint, Vector2.zero); vh.AddVert(b - normal, tint, Vector2.zero);
        vh.AddTriangle(start, start + 1, start + 2); vh.AddTriangle(start, start + 2, start + 3);
    }
}

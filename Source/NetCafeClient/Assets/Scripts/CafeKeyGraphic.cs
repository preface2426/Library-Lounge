using UnityEngine;
using UnityEngine.UI;

// Same two-profile contact envelope as the approved web preview (contact-03).
public sealed class CafeKeyGraphic : MaskableGraphic
{
    Texture texture;
    public Texture Texture {get=>texture;set {if(texture==value)return;texture=value;SetMaterialDirty();SetVerticesDirty();}}
    public float TimePosition;
    public bool OutlineOnly;
    public override Texture mainTexture => !OutlineOnly && Texture != null ? Texture : s_WhiteTexture;
    static readonly Vector2[] profile = {
        new Vector2(-260,-24),new Vector2(-238,0),new Vector2(-220,0),new Vector2(-210,18),new Vector2(-202,18),
        new Vector2(-190,-20),new Vector2(-181,-20),new Vector2(-170,8),new Vector2(-159,8),new Vector2(-147,-26),
        new Vector2(-136,-26),new Vector2(-125,36),new Vector2(-113,36),new Vector2(-101,0),new Vector2(-87,0),
        new Vector2(-76,-18),new Vector2(-64,-18),new Vector2(-51,22),new Vector2(-39,22),new Vector2(-27,-12),
        new Vector2(-15,-12),new Vector2(-4,0),new Vector2(95,0) };
    static readonly float[] offsets = {18,44,72,34,60,25,53,12};
    Rect rect;
    float scale;
    Vector2 origin;
    public static float LayoutScale(Rect area)=>Mathf.Max(.05f,Mathf.Min(area.width*.38f/340,area.height*.62f/360));
    public static Rect StripBounds(Rect area)
    {
        float size=LayoutScale(area);
        return new Rect(area.center-new Vector2(170,101)*size,new Vector2(340,202)*size);
    }
    static float Edge(float x)
    {
        for(int i=1;i<profile.Length;i++) if(x<=profile[i].x)
            return Mathf.LerpUnclamped(profile[i-1].y,profile[i].y,(x-profile[i-1].x)/(profile[i].x-profile[i-1].x));
        return profile[profile.Length-1].y;
    }
    public static float Contact(int strip, float dx)
    {
        float left=-245+strip*42.5f, right=left+42.5f, first=Mathf.Max(left,-260+dx);
        if(first>right)return offsets[strip];
        float contact=Mathf.Max(Edge(first-dx)-Edge(first),Edge(right-dx)-Edge(right));
        foreach(var point in profile)
        {
            if(point.x>first && point.x<right)contact=Mathf.Max(contact,Edge(point.x-dx)-point.y);
            float shifted=point.x+dx;
            if(shifted>first && shifted<right)contact=Mathf.Max(contact,point.y-Edge(shifted));
        }
        float approach=Mathf.SmoothStep(0,1,Mathf.Clamp01((right+260-dx)/(right-left+12)));
        return Mathf.Max(contact,Mathf.Lerp(offsets[strip],contact,approach));
    }
    Color tint=Color.white;
    static float HoleRadius(float angle)
    {
        float c=Mathf.Abs(Mathf.Cos(angle)),s=Mathf.Abs(Mathf.Sin(angle));
        float side=9/Mathf.Max(c,.00001f);
        return side*s<=12?side:12*s+Mathf.Sqrt(Mathf.Max(0,81-144*c*c));
    }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear(); if(!OutlineOnly&&Texture==null)return;
        rect=rectTransform.rect; scale=LayoutScale(rect);
        origin=rect.center+new Vector2(75,101)*scale;
        float t=Mathf.Clamp(TimePosition,0,2.9f), progress=CafeWebMotion.Key(Mathf.InverseLerp(.18f,1.68f,t)),dx=380*(1-progress);
        float assemblyAlpha=1-CafeWebMotion.Key(Mathf.InverseLerp(2.58f,2.9f,t));
        if(OutlineOnly){DrawOutline(vh,t,dx);return;}
        if(t>=.1f)
        {
            tint=new Color(1,1,1,Mathf.InverseLerp(.1f,.26f,t)*assemblyAlpha);
            for(int strip=0;strip<8;strip++)
            {
                float left=-245+strip*42.5f,right=left+42.5f, offset=Contact(strip,dx);
                DrawBody(vh,left,right,offset);
            }
        }
        if(t>=.18f)
        {
            tint=new Color(1,1,1,Mathf.Clamp01(progress/.04f)*assemblyAlpha);
            for(int i=1;i<profile.Length;i++)
            {
                Vector2 a=profile[i-1],b=profile[i];
                float topA=a.x<-238?-48+(-238-a.x)*24/22:-48;
                float topB=b.x<-238?-48+(-238-b.x)*24/22:-48;
                Quad(vh,new Vector2(a.x+dx,topA),new Vector2(b.x+dx,topB),new Vector2(b.x+dx,b.y),new Vector2(a.x+dx,a.y),new Vector2(dx,0));
            }
            // The webpage translates the image and its capsule-shaped mask together.
            for(int i=0;i<120;i++)
            {
                float a=i*Mathf.PI*2/120,b=(i+1)*Mathf.PI*2/120;
                Vector2 c=new Vector2(155+dx,-24);
                Quad(vh,c+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*64.62f,c+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*64.62f,
                    c+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*HoleRadius(b),c+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*HoleRadius(a),new Vector2(dx,0));
            }
        }
        if(t>=1.88f)
        {
            Vector2 c=rect.center;
            tint=Color.white;
            float radius=CafeWebMotion.Reveal(Mathf.InverseLerp(1.88f,2.58f,t))*(new Vector2(Mathf.Max(c.x-rect.xMin,rect.xMax-c.x),Mathf.Max(c.y-rect.yMin,rect.yMax-c.y)).magnitude+2);
            for(int i=0;i<120;i++)
            {
                float a=i*Mathf.PI*2/120,b=(i+1)*Mathf.PI*2/120;
                TriangleScreen(vh,c,c+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius,c+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*radius);
            }
        }
    }
    void DrawOutline(VertexHelper vh,float t,float dx)
    {
        float keyAlpha=Mathf.Clamp01(CafeWebMotion.Key(Mathf.InverseLerp(.18f,1.68f,t))/.04f)*(1-Mathf.InverseLerp(1.5f,1.68f,t));
        var brass=new Color(.62f,.482f,.263f,keyAlpha);
        Line(vh,new Vector2(95+dx,-48),new Vector2(-238+dx,-48),brass);
        Line(vh,new Vector2(-238+dx,-48),new Vector2(-260+dx,-24),brass);
        for(int i=1;i<profile.Length;i++)Line(vh,profile[i-1]+Vector2.right*dx,profile[i]+Vector2.right*dx,brass);
        float start=Mathf.Atan2(24,-60);var center=new Vector2(155+dx,-24);
        for(int i=0;i<120;i++)
        {
            float a=Mathf.Lerp(start,-start,i/120f),b=Mathf.Lerp(start,-start,(i+1)/120f);
            Line(vh,center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*64.62f,center+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*64.62f,brass);
            a=i*Mathf.PI*2/120;b=(i+1)*Mathf.PI*2/120;
            Line(vh,center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*HoleRadius(a),center+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*HoleRadius(b),brass);
        }
    }
    void Line(VertexHelper vh,Vector2 a,Vector2 b,Color color)=>CafeLoungeGraphic.Line(vh,ScreenPoint(a),ScreenPoint(b),1,color);
    void DrawBody(VertexHelper vh,float left,float right,float offset)
    {
        float x=left;
        foreach(var point in profile)
        {
            if(point.x<=left || point.x>=right)continue;
            BodyQuad(vh,x,point.x,offset);x=point.x;
        }
        BodyQuad(vh,x,right,offset);
    }
    void BodyQuad(VertexHelper vh,float a,float b,float offset)
    {
        Quad(vh,new Vector2(a,Edge(a)+offset),new Vector2(b,Edge(b)+offset),new Vector2(b,202+offset),new Vector2(a,202+offset),new Vector2(0,offset));
    }
    Vector2 ScreenPoint(Vector2 p) => origin+new Vector2(p.x,-p.y)*scale;
    Vector2 UV(Vector2 p) => new Vector2((p.x-rect.xMin)/rect.width,(p.y-rect.yMin)/rect.height);
    void Quad(VertexHelper vh,Vector2 a,Vector2 b,Vector2 c,Vector2 d,Vector2 offset)
    {
        int n=vh.currentVertCount;
        foreach(var p in new[]{a,b,c,d})vh.AddVert(ScreenPoint(p),tint,UV(ScreenPoint(p-offset)));
        vh.AddTriangle(n,n+1,n+2);vh.AddTriangle(n,n+2,n+3);
    }
    void TriangleScreen(VertexHelper vh,Vector2 a,Vector2 b,Vector2 c)
    {
        int n=vh.currentVertCount;
        vh.AddVert(a,Color.white,UV(a));vh.AddVert(b,Color.white,UV(b));vh.AddVert(c,Color.white,UV(c));vh.AddTriangle(n,n+1,n+2);
    }
}

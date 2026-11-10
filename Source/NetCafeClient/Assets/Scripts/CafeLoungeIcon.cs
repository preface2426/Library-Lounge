using UnityEngine;
using UnityEngine.UI;

// Paths are the small 24-unit outline icons already used in the webpage HUD.
public sealed class CafeLoungeIcon : MaskableGraphic
{
    public enum Shape { Members, Invite, Microphone, More, Copy, Arrow, Screen }
    public Shape Icon;
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        void Line(float x,float y,float u,float v)=>CafeLoungeGraphic.Line(vh,P(x,y),P(u,v),Mathf.Min(rectTransform.rect.width,rectTransform.rect.height)/24,color);
        void Path(params Vector2[] points){for(int i=1;i<points.Length;i++)Line(points[i-1].x,points[i-1].y,points[i].x,points[i].y);}
        void Circle(float cx,float cy,float r,float start=0,float end=360)
        {for(int i=0;i<24;i++){float a=Mathf.Lerp(start,end,i/24f)*Mathf.Deg2Rad,b=Mathf.Lerp(start,end,(i+1)/24f)*Mathf.Deg2Rad;Line(cx+Mathf.Cos(a)*r,cy+Mathf.Sin(a)*r,cx+Mathf.Cos(b)*r,cy+Mathf.Sin(b)*r);}}
        switch(Icon)
        {
            case Shape.Members:Circle(9,7,3);Circle(9,18,6,180,360);Line(3,18,3,21);Line(15,18,15,21);Circle(16,7,3,-90,90);Circle(16,18,5,-90,0);Line(21,18,21,21);break;
            case Shape.Invite:Path(new Vector2(3,5),new Vector2(21,5),new Vector2(21,19),new Vector2(3,19),new Vector2(3,5),new Vector2(12,13),new Vector2(21,5));break;
            case Shape.Microphone:Circle(12,5,3,180,360);Circle(12,11,3,0,180);Line(9,5,9,11);Line(15,5,15,11);Circle(12,12,7,0,180);Line(5,10,5,12);Line(19,10,19,12);Line(12,19,12,22);Line(8,22,16,22);break;
            case Shape.More:Line(4,6,20,6);Line(4,12,20,12);Line(4,18,20,18);break;
            case Shape.Copy:Path(new Vector2(7,7),new Vector2(20,7),new Vector2(20,21),new Vector2(7,21),new Vector2(7,7));Path(new Vector2(16,7),new Vector2(16,3),new Vector2(3,3),new Vector2(3,17),new Vector2(7,17));break;
            case Shape.Arrow:Path(new Vector2(5,19),new Vector2(19,5),new Vector2(8,5));Line(19,5,19,16);break;
            case Shape.Screen:Path(new Vector2(3,4),new Vector2(21,4),new Vector2(21,16),new Vector2(3,16),new Vector2(3,4));Line(12,16,12,21);Line(8,21,16,21);break;
        }
    }
    Vector2 P(float x,float y)
    {
        Rect r=rectTransform.rect;float scale=Mathf.Min(r.width,r.height)/24;
        return r.center+new Vector2(x-12,12-y)*scale;
    }
}

using UnityEngine;

// The CSS curves used by the approved local webpage, evaluated in unscaled time.
public static class CafeWebMotion
{
    public static float Out(float t)=>Bezier(t,.22f,1,.36f,1);
    public static float Key(float t)=>Bezier(t,.4f,0,.3f,1);
    public static float Reveal(float t)=>Bezier(t,.65f,0,.35f,1);
    public static float Bezier(float x,float x1,float y1,float x2,float y2)
    {
        if(x<=0)return 0;if(x>=1)return 1;
        float lo=0,hi=1;
        for(int i=0;i<18;i++)
        {
            float u=(lo+hi)*.5f,v=1-u;
            if(3*v*v*u*x1+3*v*u*u*x2+u*u*u<x)lo=u;else hi=u;
        }
        float t=(lo+hi)*.5f,s=1-t;
        return 3*s*s*t*y1+3*s*t*t*y2+t*t*t;
    }
}

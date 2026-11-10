using UnityEngine;

// Camera-only cinema presentation; seat ownership and media remain with their existing owners.
public sealed class CafeCinemaView : MonoBehaviour
{
    public CafePrototype cafe;
    public int firstSeat=6;
    public int sofaCount=6;
    public const float DefaultCoverage=.84f, FocusCoverage=.90f;
    public bool ContainsSeat(int seat)=>cafe!=null&&seat>=firstSeat&&seat<firstSeat+sofaCount&&seat<cafe.seats.Length;
    public bool Active=>cafe!=null&&ContainsSeat(cafe.CurrentSeat);
    public bool Focused {get;private set;}
    bool freeLook;
    Vector3 fromPosition;
    Quaternion fromRotation;
    float fromFov,changedAt;
    const float Duration=.48f;

    public void Enter(Vector3 position,Quaternion rotation,float fov)
    {
        Focused=false;freeLook=false;
        fromPosition=position;fromRotation=rotation;fromFov=fov;changedAt=Time.unscaledTime;
    }
    void BeginTransition()
    {
        fromPosition=cafe.eyes.transform.position;fromRotation=cafe.eyes.transform.rotation;
        fromFov=cafe.eyes.fieldOfView;changedAt=Time.unscaledTime;
    }
    public void ToggleFocus()
    {
        if(!Active)return;
        BeginTransition();Focused=!Focused;freeLook=false;
    }
    public void BeginLookAround()
    {
        if(!Active)return;
        BeginTransition();Focused=false;freeLook=true;
    }
    public void Leave(){Focused=false;freeLook=false;}
    public Vector3 AvatarPosition(int seat)=>cafe.seats[seat].position-Vector3.up*.67f;

    public bool Apply(float roomFov)
    {
        if(!Active||cafe.publicScreen==null)return false;
        var camera=cafe.eyes;var screen=cafe.publicScreen.transform;
        Vector3 position=cafe.seats[cafe.CurrentSeat].position;
        Quaternion rotation=camera.transform.rotation;
        float fov=roomFov;
        if(!freeLook)
        {
            if(Focused)
            {
                // Keep the local avatar on its seat. Only the viewing camera moves.
                float halfHeight=Mathf.Max(screen.lossyScale.y,screen.lossyScale.x/Mathf.Max(.1f,camera.aspect))*.5f;
                float distance=Mathf.Max(3.2f,halfHeight/FocusCoverage);
                position=screen.position-screen.forward*distance;
            }
            rotation=FitRotation(screen,position);
            fov=FitFieldOfView(screen,position,rotation,camera.aspect,Focused?FocusCoverage:DefaultCoverage);
        }
        float t=PlayerPrefs.GetInt("CafeReduceMotion",0)!=0?1:Mathf.Clamp01((Time.unscaledTime-changedAt)/Duration);
        t=t*t*(3-2*t);
        camera.transform.position=Vector3.Lerp(fromPosition,position,t);
        if(!freeLook)camera.transform.rotation=Quaternion.Slerp(fromRotation,rotation,t);
        camera.fieldOfView=Mathf.Lerp(fromFov,fov,t);
        return true;
    }
    static Quaternion FitRotation(Transform screen,Vector3 position)
    {
        var rotation=Quaternion.LookRotation(screen.position-position,Vector3.up);
        // Perspective makes the near edge larger at the end seats. Center the
        // projected rectangle before fitting the lens so that both margins agree.
        for(int iteration=0;iteration<8;iteration++)
        {
            var inverse=Quaternion.Inverse(rotation);
            float minX=float.PositiveInfinity,minY=minX,maxX=float.NegativeInfinity,maxY=maxX;
            for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)
            {
                var p=inverse*(screen.TransformPoint(new Vector3(x*.5f,y*.5f,0))-position);
                if(p.z<=.01f)return rotation;
                minX=Mathf.Min(minX,p.x/p.z);maxX=Mathf.Max(maxX,p.x/p.z);
                minY=Mathf.Min(minY,p.y/p.z);maxY=Mathf.Max(maxY,p.y/p.z);
            }
            rotation=Quaternion.LookRotation(rotation*new Vector3((minX+maxX)*.5f,(minY+maxY)*.5f,1),Vector3.up);
        }
        return rotation;
    }
    static float FitFieldOfView(Transform screen,Vector3 position,Quaternion rotation,float aspect,float coverage)
    {
        float slope=0;Quaternion inverse=Quaternion.Inverse(rotation);
        for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)
        {
            Vector3 corner=inverse*(screen.TransformPoint(new Vector3(x*.5f,y*.5f,0))-position);
            if(corner.z<=.01f)return 150;
            slope=Mathf.Max(slope,Mathf.Abs(corner.y)/corner.z,Mathf.Abs(corner.x)/(corner.z*Mathf.Max(.1f,aspect)));
        }
        return Mathf.Clamp(2*Mathf.Atan(slope/coverage)*Mathf.Rad2Deg,10,150);
    }
}

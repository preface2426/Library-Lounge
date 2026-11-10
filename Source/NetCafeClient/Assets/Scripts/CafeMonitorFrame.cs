using UnityEngine;

// Shared physical dimensions; capture and input retain their existing owners.
public sealed class CafeMonitorFrame : MonoBehaviour
{
    public const float ImageHeight=.38f;
    public const float Border=.006f;
    public Transform image,frame,laptopBody;
    public Vector3 bodyScale;
    public float bodyWidth;
    public bool preserveHousing;
    public Vector2 originalScreenSize;
    public void Fit(float aspect)
    {
        if(!float.IsFinite(aspect)||aspect<=0)return;
        float height=preserveHousing ? Mathf.Min(originalScreenSize.y-Border*2,(originalScreenSize.x-Border*2)/aspect) : ImageHeight;
        float width=height*aspect;
        image.localScale=new Vector3(width,height,.004f);
        frame.localScale=preserveHousing ? new Vector3(originalScreenSize.x,originalScreenSize.y,.004f) : new Vector3(width+Border*2,height+Border*2,.012f);
        if(laptopBody!=null&&bodyWidth>0)
            laptopBody.localScale=new Vector3(bodyScale.x*(width+Border*2)/bodyWidth,bodyScale.y,bodyScale.z);
    }
}

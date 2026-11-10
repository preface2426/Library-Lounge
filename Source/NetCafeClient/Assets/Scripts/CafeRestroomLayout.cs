using UnityEngine;

[DefaultExecutionOrder(50)]
public sealed class CafeRestroomLayout : MonoBehaviour
{
    public CafePrototype cafe;
    public CafeDesktopCapture capture;
    public CafeMonitorFrame[] displays;
    void OnEnable(){capture.Changed+=RefreshDisplays;RefreshDisplays();}
    void OnDisable(){capture.Changed-=RefreshDisplays;}
    void RefreshDisplays()
    {
        float aspect=capture.MonitorAspect;
        foreach(var display in displays)display.Fit(aspect);
    }
}

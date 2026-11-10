using UnityEngine;
using UnityEngine.EventSystems;

public sealed class CafeUiFocus : MonoBehaviour, ISelectHandler, IDeselectHandler
{
    UnityEngine.UI.Outline outline;
    void Awake() { outline = GetComponent<UnityEngine.UI.Outline>(); }
    public void OnSelect(BaseEventData data)
    {
        if (outline != null) outline.enabled = true;
        Reveal();
    }
    public void OnDeselect(BaseEventData data) { if (outline != null) outline.enabled = false; }
    public void Reveal()
    {
        var scroll = GetComponentInParent<UnityEngine.UI.ScrollRect>();
        if (scroll == null || !scroll.isActiveAndEnabled) return;
        Canvas.ForceUpdateCanvases();
        var corners = new Vector3[4]; ((RectTransform)transform).GetWorldCorners(corners);
        float bottom = scroll.viewport.InverseTransformPoint(corners[0]).y;
        float top = scroll.viewport.InverseTransformPoint(corners[1]).y;
        float shift = top > scroll.viewport.rect.yMax ? scroll.viewport.rect.yMax - top :
            bottom < scroll.viewport.rect.yMin ? scroll.viewport.rect.yMin - bottom : 0;
        scroll.content.anchoredPosition += Vector2.up * shift;
        scroll.StopMovement();
    }
}

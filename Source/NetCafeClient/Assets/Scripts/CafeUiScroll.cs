using UnityEngine;
using UnityEngine.UI;

internal static class CafeUiScroll
{
    internal static void Configure(ScrollRect scroll)
    {
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.inertia = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        // The scene's UI input module sends six scroll units per wheel notch.
        scroll.scrollSensitivity = 8f;

        // Receive wheel events over empty padding as well as over controls.
        var hit = scroll.viewport.GetComponent<Image>();
        if (hit == null)
        {
            hit = scroll.viewport.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
        }
        hit.raycastTarget = true;
    }
}

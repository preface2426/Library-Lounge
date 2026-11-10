#if UNITY_EDITOR || DEVELOPMENT_BUILD || NETCAFE_ACCEPTANCE
using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

public sealed partial class CafeEntryUi
{
    IEnumerator VerifyMenuWheel()
    {
        float previousScale = hud.TextScale;
        int previousWidth = Screen.width, previousHeight = Screen.height;
        var previousMode = Screen.fullScreenMode;
        var mouse = InputSystem.AddDevice<Mouse>();
        bool passed = true;
        void Check(bool ok, string stage)
        {
            passed &= ok;
            if (ok) Debug.Log("MENU_WHEEL_PASS " + stage);
            else Debug.LogError("MENU_WHEEL_FAIL " + stage);
        }
        IEnumerator Wheel(ScrollRect area, RectTransform hit, string stage, bool padding = false)
        {
            Canvas.ForceUpdateCanvases();
            float range = area.content.rect.height - area.viewport.rect.height;
            Check(range > 8, stage + " has overflow (" + range.ToString("F1") + ")");
            if (range <= 8 || hit == null) { Check(false, stage + " target"); yield break; }
            Vector2 local = padding ? new Vector2(hit.rect.xMin + 5, hit.rect.center.y) : hit.rect.center;
            Vector2 point = RectTransformUtility.WorldToScreenPoint(null, hit.TransformPoint(local));
            var hits = new System.Collections.Generic.List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
            var handler = hits.Count > 0 ? ExecuteEvents.GetEventHandler<IScrollHandler>(hits[0].gameObject) : null;
            Check(handler == area.gameObject || handler != null && handler.GetComponent<TMP_InputField>() != null,
                stage + " routed (" + (handler != null ? handler.name : "none") + ")");
            float before = area.content.anchoredPosition.y;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point, scroll = Vector2.down });
            yield return null; yield return null;
            float down = area.content.anchoredPosition.y - before;
            Check(down > Mathf.Min(12, range * .45f) && down <= 52, stage + " down " + down.ToString("F1"));
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point, scroll = Vector2.up });
            yield return null; yield return null;
            float after = area.content.anchoredPosition.y;
            Check(Mathf.Abs(after - before) < 1.5f, stage + " up " + (after - before).ToString("F1"));
        }

        yield return null;
        hud.SetTextScale(2);
        Show(Page.Join);
        yield return null; yield return null;
        Canvas.ForceUpdateCanvases();
        pageScroll.verticalNormalizedPosition = 1;
        yield return Wheel(pageScroll, pageScroll.viewport, "join padding", true);
        if (inviteField != null)
        {
            inviteField.GetComponent<CafeUiFocus>().Reveal();
            pageScroll.verticalNormalizedPosition = Mathf.Max(.4f, pageScroll.verticalNormalizedPosition);
            yield return Wheel(pageScroll, (RectTransform)inviteField.transform, "join code field");
        }
        Show(Page.Help);
        yield return null;
        yield return Wheel(pageScroll, pageScroll.viewport, "paper help", true);
        pageScroll.verticalNormalizedPosition = .4f;
        Render(true);
        Check(Mathf.Abs(pageScroll.verticalNormalizedPosition - .4f) < .03f, "paper help refresh preserves position");

        EnterSingle();
        float until = Time.realtimeSinceStartup + 12f;
        while ((!roomEntered || TransitionActive) && Time.realtimeSinceStartup < until) yield return null;
        Check(roomEntered && !TransitionActive, "solo room entered");
        if (roomEntered && !TransitionActive)
        {
            // The compact More list fits at 16:9. Exercise the actual short-window overflow.
            Screen.SetResolution(1280, 400, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(.5f);
            cafe.OpenMenu();
            yield return null; yield return null;
            var lounge = GetComponent<CafeLoungeHud>();
            var more = lounge.LightRoot.Find("HudActions/HudMoreButton").GetComponent<Button>();
            more.onClick.Invoke();
            yield return null; yield return null;
            var moreArea = lounge.MorePanelRoot.GetComponentInChildren<ScrollRect>();
            if (moreArea != null) yield return Wheel(moreArea, moreArea.viewport, "in-room more", true);
            else Check(false, "in-room more open");
            hud.Push(1);
            yield return null;
            yield return Wheel(hud.scroll, hud.scroll.viewport, "in-room settings", true);
            hud.Back();
            hud.Push(2);
            yield return null;
            yield return Wheel(hud.scroll, hud.scroll.viewport, "in-room help", true);
        }
        InputSystem.RemoveDevice(mouse);
        hud.SetTextScale(previousScale);
        Screen.SetResolution(previousWidth, previousHeight, previousMode);
        Debug.Log("MENU_WHEEL_COMPLETE " + (passed ? "PASS" : "FAIL"));
        Application.Quit(passed ? 0 : 1);
    }
}
#endif

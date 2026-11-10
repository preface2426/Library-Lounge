using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UObject = UnityEngine.Object;

// Minimal, allocation-light tween layer for hand-built uGUI.
// One hidden driver MonoBehaviour owns every job; jobs are keyed by (target, channel)
// so a new tween on the same property replaces the old one instead of fighting it.
// Everything runs on unscaled time: menus must keep animating while the game is paused.
public static class UiTween
{
    public enum Ease { Enter, Exit, Out, Linear }

    public sealed class Handle
    {
        internal bool Active = true;
        internal Action OnKill;

        public bool IsActive => Active;

        public void Kill()
        {
            if (!Active) return;
            Active = false;
            Action action = OnKill;
            OnKill = null;
            action?.Invoke();
        }

        internal void Complete()
        {
            Active = false;
            OnKill = null;
        }
    }

    sealed class Job
    {
        public UObject Target;
        public string Channel;
        public float Duration;
        public float Elapsed;
        public Ease Ease;
        public Action<float> Apply;
        public Action Done;
        public Handle Handle;
    }

    sealed class Driver : MonoBehaviour
    {
        internal readonly List<Job> Jobs = new List<Job>();
        readonly List<Action> finished = new List<Action>();

        void Update()
        {
            float dt = Time.unscaledDeltaTime;

            for (int i = Jobs.Count - 1; i >= 0; i--)
            {
                Job job = Jobs[i];

                if (!job.Handle.Active || job.Target == null)
                {
                    Jobs.RemoveAt(i);
                    job.Handle.Complete();
                    continue;
                }

                job.Elapsed += dt;
                float t = job.Duration <= 0f ? 1f : Mathf.Clamp01(job.Elapsed / job.Duration);
                job.Apply(Evaluate(job.Ease, t));

                if (!job.Handle.Active || t < 1f) continue;

                Jobs.RemoveAt(i);
                job.Handle.Complete();
                if (job.Done != null) finished.Add(job.Done);
            }

            // callbacks may start new tweens, so run them after the list is stable
            for (int i = 0; i < finished.Count; i++)
            {
                Action action = finished[i];
                try { action(); }
                catch (Exception e) { Debug.LogException(e); }
            }
            finished.Clear();
        }

        void OnDestroy()
        {
            for (int i = 0; i < Jobs.Count; i++) Jobs[i].Handle.Kill();
            Jobs.Clear();
            finished.Clear();
            if (driver == this) driver = null;
        }
    }

    static Driver driver;
    static int serial;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        driver = null;
        serial = 0;
    }

    static Driver GetDriver()
    {
        if (driver != null) return driver;

        var go = new GameObject("[UiTween.Driver]");
        go.hideFlags = HideFlags.HideInHierarchy;
        UObject.DontDestroyOnLoad(go);
        driver = go.AddComponent<Driver>();
        return driver;
    }

    static Handle Run(UObject target, string channel, float seconds, Ease ease,
                      Action<float> apply, Action done = null)
    {
        var handle = new Handle();
        if (target == null || !Application.isPlaying || seconds <= 0f ||
            PlayerPrefs.GetInt("CafeReduceMotion", 0) != 0)
        {
            apply(1f);
            done?.Invoke();
            handle.Complete();
            return handle;
        }

        Driver d = GetDriver();
        for (int i = 0; i < d.Jobs.Count; i++)
        {
            Job running = d.Jobs[i];
            if (ReferenceEquals(running.Target, target) && running.Channel == channel)
                running.Handle.Kill();
        }

        d.Jobs.Add(new Job
        {
            Target = target,
            Channel = channel,
            Duration = seconds,
            Ease = ease,
            Apply = apply,
            Done = done,
            Handle = handle,
        });
        return handle;
    }

    // ---------------- public API ----------------

    public static Handle Fade(CanvasGroup group, float to, float seconds, Ease ease = Ease.Enter, Action done = null)
    {
        if (group == null) return new Handle();
        float from = group.alpha;
        return Run(group, "alpha", seconds, ease,
            t => { if (group != null) group.alpha = Mathf.LerpUnclamped(from, to, t); }, done);
    }

    public static Handle FadeGraphic(Graphic graphic, float to, float seconds, Ease ease = Ease.Enter, Action done = null)
    {
        if (graphic == null) return new Handle();
        float from = graphic.color.a;
        Color baseColor = graphic.color;
        return Run(graphic, "alpha", seconds, ease, t =>
        {
            if (graphic == null) return;
            Color c = baseColor;
            c.a = Mathf.LerpUnclamped(from, to, t);
            graphic.color = c;
        }, done);
    }

    public static Handle Move(RectTransform rect, Vector2 to, float seconds, Ease ease = Ease.Enter, Action done = null)
    {
        if (rect == null) return new Handle();
        Vector2 from = rect.anchoredPosition;
        return Run(rect, "position", seconds, ease,
            t => { if (rect != null) rect.anchoredPosition = Vector2.LerpUnclamped(from, to, t); }, done);
    }

    public static Handle Scale(RectTransform rect, Vector3 to, float seconds, Ease ease = Ease.Out, Action done = null)
    {
        if (rect == null) return new Handle();
        Vector3 from = rect.localScale;
        return Run(rect, "scale", seconds, ease,
            t => { if (rect != null) rect.localScale = Vector3.LerpUnclamped(from, to, t); }, done);
    }

    // Vertical size only: safe for elements whose height is not driven by a LayoutGroup.
    public static Handle Height(RectTransform rect, float to, float seconds, Ease ease = Ease.Enter, Action done = null)
    {
        if (rect == null) return new Handle();
        float from = rect.sizeDelta.y;
        return Run(rect, "height", seconds, ease, t =>
        {
            if (rect == null) return;
            Vector2 size = rect.sizeDelta;
            size.y = Mathf.LerpUnclamped(from, to, t);
            rect.sizeDelta = size;
        }, done);
    }

    public static Handle Color(Graphic graphic, Color to, float seconds, Ease ease = Ease.Out, Action done = null)
    {
        if (graphic == null) return new Handle();
        Color from = graphic.color;
        return Run(graphic, "color", seconds, ease,
            t => { if (graphic != null) graphic.color = UnityEngine.Color.LerpUnclamped(from, to, t); }, done);
    }

    public static Handle Delay(UObject owner, float seconds, Action done)
    {
        return Run(owner, "delay_" + (++serial), seconds, Ease.Linear, _ => { }, done);
    }

    // Runs `apply` from 0..1 over `seconds` without touching any transform.
    public static Handle Drive(UObject owner, string channel, float seconds, Action<float> apply, Ease ease = Ease.Linear, Action done = null)
    {
        return Run(owner, channel, seconds, ease, apply, done);
    }

    public static void Kill(UObject target)
    {
        if (driver == null || target == null) return;

        for (int i = 0; i < driver.Jobs.Count; i++)
        {
            Job job = driver.Jobs[i];
            bool match = ReferenceEquals(job.Target, target);

            if (!match && target is GameObject root && job.Target is Component component && component != null)
                match = component.transform.IsChildOf(root.transform);

            if (match) job.Handle.Kill();
        }
    }

    public static Handle Sequence(UObject owner, params Func<Action, Handle>[] steps)
    {
        var chain = new Handle();
        Handle current = null;
        int index = 0;

        chain.OnKill = () => current?.Kill();

        Action next = null;
        next = () =>
        {
            if (!chain.Active || owner == null) return;
            if (index >= steps.Length) { chain.Complete(); return; }
            current = steps[index++](next);
        };

        next();
        return chain;
    }

    static float Evaluate(Ease ease, float t)
    {
        t = Mathf.Clamp01(t);
        switch (ease)
        {
            case Ease.Enter: return Bezier(t, .22f, 1f, .36f, 1f);
            case Ease.Exit: return Bezier(t, .4f, 0f, 1f, 1f);
            case Ease.Out: return 1f - Mathf.Pow(1f - t, 3f);
            default: return t;
        }
    }

    // CSS-style cubic-bezier(x1,y1,x2,y2): solve u for x, evaluate y.
    static float Bezier(float x, float x1, float y1, float x2, float y2)
    {
        float lo = 0f, hi = 1f;
        for (int i = 0; i < 14; i++)
        {
            float mid = (lo + hi) * .5f;
            if (Curve(mid, x1, x2) < x) lo = mid; else hi = mid;
        }
        return Curve((lo + hi) * .5f, y1, y2);
    }

    static float Curve(float u, float p1, float p2)
    {
        float v = 1f - u;
        return 3f * v * v * u * p1 + 3f * v * u * u * p2 + u * u * u;
    }
}

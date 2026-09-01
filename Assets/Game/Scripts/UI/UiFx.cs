// UiFx — the Roblox UIFx module (ReplicatedStorage.Modules.Shared.UIFx),
// ported with its contract intact:
//   • Animates SCALE ONLY (localScale here, UIScale there) — never size, so
//     every pixel the drag/clamp math reads stays untouched mid-tween.
//   • IN: 0.14 s Back/Out — ~10% overshoot of the distance travelled ("a
//     tick of life, not a cartoon squash"). OUT: 0.10 s Quad/In —
//     accelerates away, reads as "gone" sooner than its duration.
//   • Constants: menus 0.90 → 1 → 0.92 · drag ghosts 0.78 → 1 → 0.62.
//   • GENERATION GUARD: every call supersedes the last per-object; a stale
//     popOut's onDone can never blank a freshly re-shown menu, and a rapid
//     pick-drop-pick can't strand a half-faded ghost.
//   • Fade is ONE-WAY (CanvasGroup alpha → 0) — only for throwaways about
//     to deactivate; callers reset alpha themselves on reuse.
//   • Snap() = cancel + rest at scale 1, for paths that hide a whole screen
//     (an out-tween must never be cut mid-flight and strand the scale).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.UI
{
    public static class UiFx
    {
        public const float InTime = 0.14f;
        public const float OutTime = 0.10f;
        public const float MenuFrom = 0.90f;
        public const float MenuTo = 0.92f;
        public const float GhostFrom = 0.78f;
        public const float GhostTo = 0.62f;

        class Fx
        {
            public RectTransform Rt;
            public int Gen;
            public float T, Dur, From, To;
            public bool EaseOutBack;
            public CanvasGroup Fade;
            public Action OnDone;
        }

        static readonly List<Fx> _active = new List<Fx>();
        static readonly Dictionary<RectTransform, int> _gen =
            new Dictionary<RectTransform, int>();
        static UiFxRunner _runner;

        // ALWAYS restarts from `from`, so interrupting a popOut replays from
        // scratch instead of easing out of a half-collapsed state.
        public static void PopIn(RectTransform rt, float from = MenuFrom,
            float time = InTime, float to = 1f) =>
            Start(rt, from, to, time, easeOutBack: true, fade: null, onDone: null);

        // onDone fires ONLY if no newer call has claimed this object since.
        public static void PopOut(RectTransform rt, float to = MenuTo,
            float time = OutTime, CanvasGroup fade = null, Action onDone = null) =>
            Start(rt, rt != null ? rt.localScale.x : 1f, to, time, false, fade, onDone);

        public static void Snap(RectTransform rt)
        {
            if (rt == null) return;
            Bump(rt);
            rt.localScale = Vector3.one;
        }

        static int Bump(RectTransform rt)
        {
            _gen.TryGetValue(rt, out int g);
            _gen[rt] = ++g;
            return g;
        }

        static void Start(RectTransform rt, float from, float to, float time,
            bool easeOutBack, CanvasGroup fade, Action onDone)
        {
            if (rt == null) { onDone?.Invoke(); return; }
            EnsureRunner();
            int gen = Bump(rt);
            rt.localScale = Vector3.one * from;
            _active.Add(new Fx
            {
                Rt = rt,
                Gen = gen,
                Dur = Mathf.Max(0.01f, time),
                From = from,
                To = to,
                EaseOutBack = easeOutBack,
                Fade = fade,
                OnDone = onDone,
            });
        }

        static float BackOut(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            t -= 1f;
            return 1f + c3 * t * t * t + c1 * t * t;
        }

        internal static void Tick(float dt)
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var fx = _active[i];
                bool current = fx.Rt != null
                    && _gen.TryGetValue(fx.Rt, out int g) && g == fx.Gen;
                if (!current) { _active.RemoveAt(i); continue; }   // superseded: no onDone

                fx.T += dt;
                float raw = Mathf.Clamp01(fx.T / fx.Dur);
                float e = fx.EaseOutBack ? BackOut(raw) : raw * raw;
                fx.Rt.localScale = Vector3.one * Mathf.LerpUnclamped(fx.From, fx.To, e);
                if (fx.Fade != null) fx.Fade.alpha = 1f - raw * raw;

                if (raw >= 1f)
                {
                    fx.Rt.localScale = Vector3.one * fx.To;   // land EXACTLY on target
                    if (fx.Fade != null) fx.Fade.alpha = 0f;
                    _active.RemoveAt(i);
                    fx.OnDone?.Invoke();
                }
            }
        }

        class UiFxRunner : MonoBehaviour
        {
            void Update() => Tick(Time.deltaTime);
        }

        static void EnsureRunner()
        {
            if (_runner != null) return;
            _runner = new GameObject("UiFxRunner").AddComponent<UiFxRunner>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _active.Clear();
            _gen.Clear();
            _runner = null;
        }
    }
}

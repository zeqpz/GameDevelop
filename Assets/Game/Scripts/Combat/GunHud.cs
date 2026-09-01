// GunHud — the EXACT Robloxia reticle, extracted from Studio 2026-08-31:
//   • GunCrosshair: four 2×10 bars, white @0.10 transparency, positioned by
//     crosshairGap(spread) = BASE_GAP 6 + spread × 4 px/deg + BAR_LEN/2 —
//     a flat px-per-degree mapping (deliberately NOT FOV-projected), fed
//     the EFFECTIVE cone (hip multipliers in, distance scaling out).
//   • On-target: TargetStroke (255,105,105, ~1px) lights and InnerShadow
//     tints the bar interior to alpha 0.65 (INNER_SHADOW_ALPHA 0.35) while
//     the center ray rests on a LIVING target — edge-triggered like
//     setCrosshairTargeting, fed by GunController's throttled check.
//   • GunPoint: the lowered-state dot — 4×4 white @0.20, fully round —
//     shown while a weapon is equipped but not raised.
// Ammo readout + hitmarker ride along; everything hides behind modal UI.
using UnityEngine;
using UnityEngine.UI;
using Game.UI;

namespace Game.Combat
{
    public class GunHud
    {
        const float BarLen = 10f;
        const float BarThick = 2f;
        const float BaseGap = 6f;
        const float PxPerDegree = 4f;   // CROSSHAIR_SCALE
        static readonly Color BarWhite = new Color(1f, 1f, 1f, 0.9f);
        static readonly Color TargetRed = new Color32(255, 105, 105, 255);

        class Bar
        {
            public RectTransform Rt;
            public Image Stroke;
            public Image Inner;
            public Vector2 Dir;
        }

        readonly GameObject _root;
        readonly GameObject _readyRoot;
        readonly GameObject _dotRoot;
        readonly Bar[] _bars = new Bar[4];
        readonly Image[] _marker = new Image[4];
        readonly Text _ammo;
        float _markerT;
        bool _onTarget;

        public GunHud(Transform host)
        {
            var canvasGo = new GameObject("GunHud");
            canvasGo.transform.SetParent(host, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 550;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _root = canvasGo;

            // ── Ready group: crosshair bars + hitmarker + ammo ─────────────
            // Full-canvas stretch: center-anchored children sit at screen
            // center, corner-anchored ones (ammo) at real screen corners.
            _readyRoot = Center(canvasGo.transform, "Ready");
            var readyRt = (RectTransform)_readyRoot.transform;
            readyRt.anchorMin = Vector2.zero;
            readyRt.anchorMax = Vector2.one;
            readyRt.offsetMin = readyRt.offsetMax = Vector2.zero;

            Vector2[] dirs = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
            for (int i = 0; i < 4; i++)
            {
                bool vertical = i < 2;
                float w = vertical ? BarThick : BarLen;
                float h = vertical ? BarLen : BarThick;

                var stroke = UiKit.Fill(_readyRoot.transform, $"Bar{i}",
                    new Color(TargetRed.r, TargetRed.g, TargetRed.b, 0f));
                var rt = CenterRect(stroke.transform, w + 2f, h + 2f);

                var fill = UiKit.Fill(rt, "Fill", BarWhite);
                Stretch((RectTransform)fill.transform, 1f);

                var inner = UiKit.Fill(rt, "InnerShadow",
                    new Color(TargetRed.r, TargetRed.g, TargetRed.b, 0f));
                Stretch((RectTransform)inner.transform, 1f);

                _bars[i] = new Bar { Rt = rt, Stroke = stroke, Inner = inner, Dir = dirs[i] };
            }

            for (int i = 0; i < 4; i++)
            {
                var m = UiKit.Fill(_readyRoot.transform, $"Mark{i}", new Color(1f, 1f, 1f, 0f));
                var rt = CenterRect(m.transform, 2.5f, 12f);
                rt.localRotation = Quaternion.Euler(0f, 0f, 45f + i * 90f);
                _marker[i] = m;
            }

            _ammo = UiKit.Label(_readyRoot.transform, "Ammo", "", UiKit.FontBold, 22,
                UiKit.TextCol, TextAnchor.LowerRight);
            var art = (RectTransform)_ammo.transform;
            art.anchorMin = art.anchorMax = new Vector2(1f, 0f);
            art.pivot = new Vector2(1f, 0f);
            art.anchoredPosition = new Vector2(-28f, 22f);
            art.sizeDelta = new Vector2(240f, 34f);

            // ── GunPoint dot (equipped, lowered) ───────────────────────────
            _dotRoot = Center(canvasGo.transform, "GunPoint");
            var dot = UiKit.Fill(_dotRoot.transform, "PointDot", new Color(1f, 1f, 1f, 0.8f), 2);
            CenterRect(dot.transform, 4f, 4f);

            _readyRoot.SetActive(false);
            _dotRoot.SetActive(false);
            SetSpreadDeg(0f);
        }

        GameObject Center(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Vector2.zero;
            return go;
        }

        static RectTransform CenterRect(Transform t, float w, float h)
        {
            var rt = (RectTransform)t;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        static void Stretch(RectTransform rt, float inset)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }

        public void SetState(bool crosshair, bool dot)
        {
            if (_readyRoot.activeSelf != crosshair) _readyRoot.SetActive(crosshair);
            if (_dotRoot.activeSelf != dot) _dotRoot.SetActive(dot);
        }

        // crosshairGap(spread) = BASE_GAP + spread*SCALE + BAR_LEN/2, verbatim.
        public void SetSpreadDeg(float spreadDeg)
        {
            float gap = BaseGap + spreadDeg * PxPerDegree + BarLen * 0.5f;
            foreach (var bar in _bars)
                bar.Rt.anchoredPosition = bar.Dir * gap;
        }

        // setCrosshairTargeting, verbatim: edge-triggered red light-up.
        public void SetTargeting(bool on)
        {
            if (_onTarget == on) return;
            _onTarget = on;
            foreach (var bar in _bars)
            {
                bar.Stroke.color = new Color(TargetRed.r, TargetRed.g, TargetRed.b, on ? 1f : 0f);
                bar.Inner.color = new Color(TargetRed.r, TargetRed.g, TargetRed.b, on ? 0.65f : 0f);
            }
        }

        public void SetAmmo(int mag, int size, bool reloading) =>
            _ammo.text = reloading ? $"-- / {size}" : $"{mag} / {size}";

        public void Hitmarker(bool kill)
        {
            _markerT = 0.16f;
            var c = kill ? new Color(1f, 0.25f, 0.2f, 1f) : new Color(1f, 1f, 1f, 0.95f);
            foreach (var m in _marker)
            {
                m.color = c;
                var rt = (RectTransform)m.transform;
                rt.anchoredPosition = rt.localRotation * new Vector2(0f, kill ? 15f : 12f);
            }
        }

        public void Tick(float dt)
        {
            if (_markerT <= 0f) return;
            _markerT -= dt;
            float a = Mathf.Clamp01(_markerT / 0.16f);
            foreach (var m in _marker)
            {
                var c = m.color;
                c.a = a;
                m.color = c;
            }
        }
    }
}

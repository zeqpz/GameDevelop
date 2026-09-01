// SurvivalHud — the Robloxia SurvivalHUD, transcribed 1:1 from the live
// StarterGui.HUD.SurvivalHUD template (dumped from Studio 2026-08-31) and
// wired to the Unity twins:
//   • SurvivalBars 232×132 at bottom-left (10,10): #080808 @0.6 panel,
//     corner 3, 9/7 padding, four 28px rows on a 2px list gap in LayoutOrder
//     order — HP / FOOD / H₂O / STA. Each row: 12px color-chip icon
//     (corner 2, black stroke 1.2; HP's stroke is dark red), GothamBold-10
//     tag at x17, 13px bar at x50 (#121212, corner 2, black stroke 1.5,
//     colored fill with a 3px white gloss strip), right-aligned bold-13 value.
//   • DateTimeFrame 232×24 sitting 4px above (black @0.55, corner 5):
//     "6:00 AM  |  Sep 1, 2026" — SurvivalClient's timeStr + dateStr, read
//     from TimeService here.
//   • The SurvivalClient juice, ported: 0.35s quad-out fill size+color
//     tweens, low-value (<25%) bar recolor, number flash/pop/shake on every
//     displayed-int change (red down / green up, +5 size pop, ±8° elastic
//     settle — a steady drain re-kicks it) and 5-dot bar-colored particle
//     bursts at the fill tip while a value moves (0.3s throttle per bar).
//   • Modal convention: the whole HUD hides while the Gameplay map is
//     blocked (inventory / stats screen open) — the Roblox build disables
//     SurvivalHUD from those same screens.
// HP reads the player's Health component; FOOD/H₂O/STA read StatsService.
// Replaces StatsScreen's interim bottom-center stamina bar.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Game.Combat;
using Game.Core;
using Game.Movement;
using Game.Stats;
using Game.World;

namespace Game.UI
{
    public class SurvivalHud : MonoBehaviour
    {
        // Template geometry (Roblox px)
        const float PanelW = 232f, PanelH = 132f;
        const float PadL = 9f, PadT = 7f, RowStep = 30f;   // 28 row + 2 list gap
        const float RowW = PanelW - 18f;                   // 214
        const float RowH = 28f;
        const float BarX = 50f, BarW = RowW - 80f, BarH = 13f;   // BarBg {1,-80}×13
        const float InnerW = BarW - 3f, InnerH = BarH - 3f;      // inside the 1.5 stroke

        // SurvivalClient WARN_COLOR + flash palette
        static readonly Color FlashDown = new Color32(235, 70, 60, 255);
        static readonly Color FlashUp = new Color32(110, 230, 120, 255);
        const float TweenTime = 0.35f;
        const float EmitGap = 0.3f;

        class Bar
        {
            public RectTransform Row, Fill;
            public Image FillImg;
            public Text Value;
            public Color Normal, Low;
            // Fill tween (size + color, quad-out)
            public float CurPct = 1f, FromPct = 1f, ToPct = 1f;
            public Color CurCol, FromCol, ToCol;
            public float AnimT = TweenTime;
            // Number flash/pop/shake
            public int ShownInt = int.MinValue;
            public float FlashT = 99f;
            public Color FlashCol = Color.white;
            public float RotAmp;
            public float NextEmit;
        }

        class Particle
        {
            public RectTransform Rt;
            public Image Img;
            public Vector2 Origin, Drift;   // Roblox top-left px (+y down)
            public float T, Dur, Size0;
        }

        Canvas _canvas;
        Bar _hp, _food, _h2o, _sta;
        Text _dateTime;
        readonly List<Particle> _particles = new List<Particle>();
        float _dtRefreshT;

        InputService _input;
        StatsService _stats;
        TimeService _time;
        PlayerMotor _motor;
        Health _health;

        void Start()
        {
            var canvasGo = new GameObject("SurvivalHudCanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 530;   // under GunHud, over the world
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // ── SurvivalBars panel, pinned bottom-left (10 up-from-bottom 10)
            var panel = UiKit.Fill(canvasGo.transform, "SurvivalBars",
                new Color(8f / 255f, 8f / 255f, 8f / 255f, 0.6f), 3);
            var prt = (RectTransform)panel.transform;
            prt.anchorMin = prt.anchorMax = Vector2.zero;
            prt.pivot = Vector2.zero;
            prt.anchoredPosition = new Vector2(10f, 10f);
            prt.sizeDelta = new Vector2(PanelW, PanelH);

            // LayoutOrder order: HP 0, FOOD 1, H₂O 2, STA 3
            _hp = BuildRow(prt, 0, "HP", "HealthBar",
                new Color32(220, 50, 50, 255), new Color32(140, 20, 20, 255),
                iconStroke: new Color32(180, 30, 30, 255));
            _food = BuildRow(prt, 1, "FOOD", "HungerBar",
                new Color32(220, 95, 30, 255), new Color32(185, 30, 30, 255));
            _h2o = BuildRow(prt, 2, "H₂O", "ThirstBar",
                new Color32(45, 148, 225, 255), new Color32(185, 30, 30, 255));
            _sta = BuildRow(prt, 3, "STA", "StaminaBar",
                new Color32(158, 228, 48, 255), new Color32(220, 150, 20, 255));

            // ── DateTimeFrame, 4px above the bars (spans 146..170 up) ──────
            var dtFrame = UiKit.Fill(canvasGo.transform, "DateTimeFrame",
                new Color(0f, 0f, 0f, 0.55f), 5);
            var drt = (RectTransform)dtFrame.transform;
            drt.anchorMin = drt.anchorMax = Vector2.zero;
            drt.pivot = Vector2.zero;
            drt.anchoredPosition = new Vector2(10f, 146f);
            drt.sizeDelta = new Vector2(PanelW, 24f);
            _dateTime = UiKit.Label(drt, "DateTimeLabel", "Syncing...",
                UiKit.FontBold, 11, new Color32(220, 220, 220, 255), TextAnchor.MiddleCenter);
            var lrt = (RectTransform)_dateTime.transform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = lrt.offsetMax = Vector2.zero;
        }

        Bar BuildRow(RectTransform panel, int order, string tag, string name,
            Color normal, Color low, Color? iconStroke = null)
        {
            var row = UiKit.At(UiKit.Rect(panel, name),
                PadL, PadT + order * RowStep, RowW, RowH);

            UiKit.Panel(row, "Icon", 0f, 8f, 12f, 12f, normal, 2,
                iconStroke ?? Color.black, 1.2f);
            UiKit.At((RectTransform)UiKit.Label(row, "StatLabel", tag,
                UiKit.FontBold, 10, new Color32(200, 200, 200, 255),
                TextAnchor.MiddleLeft).transform, 17f, 0f, 30f, RowH);

            var (barBg, _) = UiKit.Panel(row, "BarBg", BarX, 8f, BarW, BarH,
                new Color32(18, 18, 18, 255), 2, Color.black, 1.5f);

            var fill = UiKit.Fill(barBg, "Fill", normal, 2);
            var frt = (RectTransform)fill.transform;
            UiKit.At(frt, 1.5f, 1.5f, InnerW, InnerH);

            // Gloss strip: 3px white @0.25, hugging the fill's top edge.
            var gloss = UiKit.Fill(frt, "Gloss", new Color(1f, 1f, 1f, 0.25f), 2);
            var grt = (RectTransform)gloss.transform;
            grt.anchorMin = new Vector2(0f, 1f);
            grt.anchorMax = Vector2.one;
            grt.pivot = new Vector2(0.5f, 1f);
            grt.offsetMin = new Vector2(0f, -3f);
            grt.offsetMax = Vector2.zero;

            var value = UiKit.Label(row, "Value", "100", UiKit.FontBold, 13,
                Color.white, TextAnchor.MiddleRight);
            UiKit.At((RectTransform)value.transform, RowW - 28f, 0f, 28f, RowH);

            return new Bar
            {
                Row = row, Fill = frt, FillImg = fill, Value = value,
                Normal = normal, Low = low, CurCol = normal, FromCol = normal, ToCol = normal,
            };
        }

        void Update()
        {
            if (_input == null && !Services.TryGet(out _input)) return;
            if (_stats == null) Services.TryGet(out _stats);
            if (_time == null) Services.TryGet(out _time);
            if (_motor == null) _motor = FindAnyObjectByType<PlayerMotor>();
            if (_health == null && _motor != null) _health = _motor.GetComponent<Health>();

            // Modal convention: screens that block gameplay also hide the HUD.
            bool visible = !_input.GameplayBlocked;
            if (_canvas.enabled != visible) _canvas.enabled = visible;

            float dt = Time.deltaTime;
            if (_health != null) SetBar(_hp, _health.Current, _health.maxHealth);
            if (_stats != null)
            {
                SetBar(_food, _stats.Hunger, 100f);
                SetBar(_h2o, _stats.Thirst, 100f);
                SetBar(_sta, _stats.Stamina, 100f);
            }
            AnimateBar(_hp, dt);
            AnimateBar(_food, dt);
            AnimateBar(_h2o, dt);
            AnimateBar(_sta, dt);
            TickParticles(dt);

            _dtRefreshT -= dt;
            if (_time != null && _dtRefreshT <= 0f)
            {
                _dtRefreshT = 0.25f;
                _dateTime.text = $"{_time.TimeString}  |  {_time.DateString}";
            }
        }

        // ── SurvivalClient.updateBar ───────────────────────────────────────
        void SetBar(Bar b, float value, float max)
        {
            float pct = Mathf.Clamp01(value / Mathf.Max(1f, max));
            Color target = pct < 0.25f ? b.Low : b.Normal;
            if (Mathf.Abs(pct - b.ToPct) > 0.0005f || target != b.ToCol)
            {
                b.FromPct = b.CurPct;
                b.ToPct = pct;
                b.FromCol = b.CurCol;
                b.ToCol = target;
                b.AnimT = 0f;
            }

            int shown = Mathf.FloorToInt(value);
            if (shown != b.ShownInt)
            {
                int dir = b.ShownInt == int.MinValue ? 0 : (shown > b.ShownInt ? 1 : -1);
                b.ShownInt = shown;
                b.Value.text = shown.ToString();
                if (dir != 0)
                {
                    // Flash/pop/shake — re-kicked on every displayed change.
                    b.FlashT = 0f;
                    b.FlashCol = dir < 0 ? FlashDown : FlashUp;
                    b.RotAmp = Random.value < 0.5f ? -8f : 8f;
                    if (Time.time >= b.NextEmit)
                    {
                        b.NextEmit = Time.time + EmitGap;
                        EmitTipBurst(b, dir);
                    }
                }
            }
        }

        static float QuadOut(float t) => 1f - (1f - t) * (1f - t);

        // Elastic-out: overshoots past the target, giving the settle-shake.
        static float ElasticOut(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            const float c = 2f * Mathf.PI / 3f;
            return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t * 10f - 0.75f) * c) + 1f;
        }

        void AnimateBar(Bar b, float dt)
        {
            // Fill size + color (0.35 Quad Out)
            if (b.AnimT < TweenTime)
            {
                b.AnimT += dt;
                float e = QuadOut(Mathf.Clamp01(b.AnimT / TweenTime));
                b.CurPct = Mathf.Lerp(b.FromPct, b.ToPct, e);
                b.CurCol = Color.Lerp(b.FromCol, b.ToCol, e);
                b.Fill.sizeDelta = new Vector2(b.CurPct * InnerW, InnerH);
                b.FillImg.color = b.CurCol;
            }

            // Number flash: color/size ease back over 0.55, elastic rotation 0.45.
            if (b.FlashT < 1f)
            {
                b.FlashT += dt;
                float e = QuadOut(Mathf.Clamp01(b.FlashT / 0.55f));
                b.Value.color = Color.Lerp(b.FlashCol, Color.white, e);
                b.Value.fontSize = Mathf.RoundToInt(Mathf.Lerp(13f + 5f, 13f, e));
                float rot = b.RotAmp * (1f - ElasticOut(Mathf.Clamp01(b.FlashT / 0.45f)));
                b.Value.rectTransform.localRotation = Quaternion.Euler(0f, 0f, rot);
            }
        }

        // ── SurvivalClient.emitTipBurst: 5 dots fan upward off the fill tip ─
        void EmitTipBurst(Bar b, int dir)
        {
            Vector2 tip = new Vector2(BarX + 1.5f + b.CurPct * InnerW, RowH * 0.5f);
            for (int i = 0; i < 5; i++)
            {
                float size = Random.Range(3f, 5f);
                var img = UiKit.Fill(b.Row, "Puff", b.ToCol, 2);
                var rt = (RectTransform)img.transform;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(size, size);
                rt.anchoredPosition = new Vector2(tip.x, -tip.y);

                float ang = Random.Range(-150f, -30f) * Mathf.Deg2Rad;
                float dist = Random.Range(9f, 20f);
                var drift = new Vector2(
                    Mathf.Cos(ang) * dist + dir * Random.Range(2f, 6f),
                    Mathf.Sin(ang) * dist);   // negative = upward (top-left px)
                _particles.Add(new Particle
                {
                    Rt = rt, Img = img, Origin = tip, Drift = drift,
                    Dur = 0.4f + Random.value * 0.2f, Size0 = size,
                });
            }
        }

        void TickParticles(float dt)
        {
            for (int i = _particles.Count - 1; i >= 0; i--)
            {
                var p = _particles[i];
                p.T += dt;
                if (p.T >= p.Dur + 0.1f || p.Rt == null)
                {
                    if (p.Rt != null) Destroy(p.Rt.gameObject);
                    _particles.RemoveAt(i);
                    continue;
                }
                float e = QuadOut(Mathf.Clamp01(p.T / p.Dur));
                Vector2 pos = p.Origin + p.Drift * e;
                p.Rt.anchoredPosition = new Vector2(pos.x, -pos.y);
                float s = Mathf.Lerp(p.Size0, 1f, e);
                p.Rt.sizeDelta = new Vector2(s, s);
                var c = p.Img.color;
                c.a = Mathf.Lerp(0.9f, 0f, e);
                p.Img.color = c;
            }
        }
    }
}

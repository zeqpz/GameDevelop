// StatsScreen — the /stats panel, ported from StatsClient: dark centered
// read-only panel — SKILLS (blue bars) · VITALS (green bars) · MONEY ·
// RECORD (Clean→Most Wanted) · LICENSES — with an X close. No chat yet, so
// P stands in for typing /stats (swap to the chat command when chat lands).
// Opening blocks the Gameplay map (the Roblox build registered StatsGui in
// isUIOpen + the gun's modal list — same effect). The stamina readout lives
// on SurvivalHud's STA row (which replaced this screen's interim bar).
using UnityEngine;
using UnityEngine.UI;
using Game.Chat;
using Game.Core;
using Game.Data;
using Game.Stats;

namespace Game.UI
{
    public class StatsScreen : MonoBehaviour
    {
        static readonly string[] RecordLabels =
            { "Clean", "Suspect", "Offender", "Criminal", "Felon", "Most Wanted" };
        static readonly Color SkillBar = new Color32(90, 140, 220, 255);
        static readonly Color VitalBar = new Color32(90, 180, 110, 255);

        class Row
        {
            public RectTransform Fill;
            public Text Value;
        }

        GameObject _root;
        readonly Row[] _skills = new Row[5];
        readonly Row[] _vitals = new Row[3];
        Text _money, _record, _licenses;
        RawImage _closeRaw;

        bool _open;
        float _refreshT;
        InputService _input;
        StatsService _stats;

        const float BarInner = 186f;

        void Start()
        {
            BuildPanel();
            EventBus.Subscribe<OpenStatsRequested>(OnOpenStats);   // the /stats command
        }

        void OnDestroy() => EventBus.Unsubscribe<OpenStatsRequested>(OnOpenStats);

        void OnOpenStats(OpenStatsRequested e) => Toggle();

        public void Toggle()
        {
            if (_open) Close();
            else if (_input != null && !_input.GameplayBlocked) Open();
        }

        void BuildPanel()
        {
            var canvasGo = new GameObject("StatsCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 620;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _root = canvasGo;

            var (win, _) = UiKit.Panel(canvasGo.transform, "StatsWin", 0f, 0f, 400f, 566f,
                UiKit.WinBg, 14, UiKit.WinStroke, 1.5f);
            win.anchorMin = win.anchorMax = new Vector2(0.5f, 0.5f);
            win.pivot = new Vector2(0.5f, 0.5f);
            win.anchoredPosition = Vector2.zero;

            var title = UiKit.Fill(win, "TitleBar", UiKit.TitleBg, 8);
            UiKit.At((RectTransform)title.transform, 0f, 0f, 400f, 34f);
            UiKit.At((RectTransform)UiKit.Label(title.transform, "TitleLbl", "STATS",
                UiKit.FontBold, 13, Color.white, TextAnchor.MiddleLeft).transform,
                14f, 0f, 200f, 34f);
            var closeRt = UiKit.Rect(title.transform, "CloseBtn");
            UiKit.At(closeRt, 400f - 30f, 5f, 24f, 24f);
            _closeRaw = closeRt.gameObject.AddComponent<RawImage>();
            _closeRaw.texture = UiKit.CloseTexture();
            UiKit.At((RectTransform)UiKit.Fill(win, "AccentLine", UiKit.Gold).transform,
                14f, 34f, 40f, 2f);

            float y = 48f;
            y = Header(win, "SKILLS", y);
            string[] skillNames = { "Strength", "Agility", "Accuracy", "Intelligence", "Reputation" };
            for (int i = 0; i < skillNames.Length; i++)
                _skills[i] = BarRow(win, skillNames[i], SkillBar, ref y);

            y += 8f;
            y = Header(win, "VITALS", y);
            string[] vitalNames = { "Hunger", "Thirst", "Stamina" };
            for (int i = 0; i < vitalNames.Length; i++)
                _vitals[i] = BarRow(win, vitalNames[i], VitalBar, ref y);

            y += 8f;
            y = Header(win, "MONEY", y);
            _money = TextRow(win, ref y);

            y += 8f;
            y = Header(win, "RECORD", y);
            _record = TextRow(win, ref y);

            y += 8f;
            y = Header(win, "LICENSES", y);
            _licenses = TextRow(win, ref y);

            _root.SetActive(false);
        }

        float Header(RectTransform win, string text, float y)
        {
            UiKit.At((RectTransform)UiKit.Label(win, text, text, UiKit.FontBold, 11,
                UiKit.Gold, TextAnchor.MiddleLeft).transform, 14f, y, 300f, 20f);
            return y + 24f;
        }

        Row BarRow(RectTransform win, string label, Color barColor, ref float y)
        {
            UiKit.At((RectTransform)UiKit.Label(win, label, label + ":", UiKit.FontMedium, 11,
                Color.white, TextAnchor.MiddleLeft).transform, 14f, y, 110f, 22f);
            var (bar, _) = UiKit.Panel(win, label + "Bar", 132f, y + 5f, 190f, 12f,
                UiKit.CellBg, 3, UiKit.CellStroke);
            var fill = UiKit.Fill(bar, "Fill", barColor, 3);
            var frt = UiKit.At((RectTransform)fill.transform, 1f, 1f, 0f, 10f);
            var value = UiKit.Label(win, label + "Val", "0/100", UiKit.FontRegular, 10,
                UiKit.Muted, TextAnchor.MiddleRight);
            UiKit.At((RectTransform)value.transform, 326f, y, 60f, 22f);
            y += 26f;
            return new Row { Fill = frt, Value = value };
        }

        Text TextRow(RectTransform win, ref float y)
        {
            var t = UiKit.Label(win, "row", "", UiKit.FontRegular, 11,
                UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.At((RectTransform)t.transform, 16f, y, 360f, 20f);
            y += 24f;
            return t;
        }

        void Update()
        {
            if (_input == null && !Services.TryGet(out _input)) return;
            if (_stats == null && !Services.TryGet(out _stats)) return;

            if (_input.StatsTogglePressed)
            {
                if (_open) Close();
                else if (!_input.GameplayBlocked) Open();   // another screen owns it
                return;
            }
            if (!_open) return;

            if (_input.EscapePressed) { Close(); return; }
            if (_input.UiClickPressed
                && UiKit.Contains((RectTransform)_closeRaw.transform, _input.MousePosition))
            { Close(); return; }
            _closeRaw.color = UiKit.Contains((RectTransform)_closeRaw.transform, _input.MousePosition)
                ? new Color32(255, 120, 120, 255) : Color.white;

            _refreshT -= Time.deltaTime;
            if (_refreshT <= 0f) { _refreshT = 0.25f; Refresh(); }
        }

        void Open()
        {
            _open = true;
            _root.SetActive(true);
            _input.SetGameplayBlocked(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Refresh();
        }

        void Close()
        {
            _open = false;
            _root.SetActive(false);
            _input.SetGameplayBlocked(false);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        void Refresh()
        {
            SetRow(_skills[0], _stats.Strength);
            SetRow(_skills[1], _stats.Agility);
            SetRow(_skills[2], _stats.Accuracy);
            SetRow(_skills[3], _stats.Intelligence);
            SetRow(_skills[4], _stats.Reputation);
            SetRow(_vitals[0], _stats.Hunger);
            SetRow(_vitals[1], _stats.Thirst);
            SetRow(_vitals[2], _stats.Stamina);

            var p = SaveService.Profile;
            _money.text = p != null ? $"Cash ${p.cash:0}   ·   Bank ${p.bank:0}" : "—";
            _record.text = RecordLabels[0] + " (0)";   // criminal record system: later port
            _licenses.text = "None yet";
        }

        static void SetRow(Row row, float value)
        {
            row.Fill.sizeDelta = new Vector2(BarInner * Mathf.Clamp01(value / 100f), 10f);
            row.Value.text = $"{value:0}/100";
        }
    }
}

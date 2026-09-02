// ChatScreen — the Robloxia ChatGui + BubbleChat, transcribed from the live
// templates (Studio dump 2026-09-01) with the shipped constants:
//   • ChatFrame 480×222 at (10,10): MsgBg 188 (black, alpha 0→0.45 over
//     BG_SHOW 0.35, fades over BG_FADE 1.5), rows 20 px rich-text, list
//     pad 1, MAX_MSGS 50.
//   • Row juice: pop-in slides the LABEL from +14 px over 0.18 s (Back
//     ease, position-only — rows never reflow); /shout rows RATTLE ±3 px
//     decaying over 0.5 s instead; opacity = pop × age-fade × the 24 px
//     top-edge dissolve, via a per-row CanvasGroup.
//   • InputBar 32 px (black 0.55): gold "[IC]" tag with DYNAMIC WIDTH
//     (measures the mode tag, 48 px floor, input box slides), hand-rolled
//     text field (InputService.TextInput stream + caret blink) — no
//     EventSystem. "/" or Enter opens (slash arrives as the first char),
//     Enter sends, Esc cancels. Typing = the REAL typing-gate customer:
//     the Gameplay map blocks while the box is open.
//   • Formats/colors: the extracted COL table verbatim — IC gold/white,
//     OOC green, /me "* Name msg" italic purple, SHOUT bold *Shouts*
//     yellow, WHISPER italic *Whispers* gray-blue, [System] blue, ALERT
//     italic purple (join lines ride it). /help is client-side.
//   • Bubble: white 50% box (corner 8, pad 8/4, wrap 224) above the head —
//     pops 0.55→1 Back-out 0.22 s per line (UiFx), hover-bobs
//     sin(t·2.2)·0.12 st, /shout wobbles ±4°, typing shows the ". . ."
//     cycle (0.45 s) with the message-wins race rules.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Game.Chat;
using Game.Core;
using Game.Movement;

namespace Game.UI
{
    public class ChatScreen : MonoBehaviour
    {
        // Shipped constants (ChatClient)
        const float BgFadeTime = 1.5f;
        const float BgShowTime = 0.35f;
        const int MaxMsgs = 50;
        const float BgAlpha = 0.45f;          // BG_OPACITY 0.55 transparency
        const float PopTime = 0.18f;
        const float PopSlide = 14f;
        const float EdgeBand = 24f;
        const float ShakeTime = 0.5f;
        const float ShakeAmp = 3f;
        const float RowHold = 12f;            // age-out hold (approx; closed only)
        const float RowFade = 1.5f;
        const float TypingDotT = 0.35f;
        const float BubbleDotT = 0.45f;

        class TypeStyle { public string Tag, Name, Msg; }
        static readonly Dictionary<string, TypeStyle> Col = new Dictionary<string, TypeStyle>
        {
            ["IC"] = new TypeStyle { Tag = "#FFD700", Name = "#FFD700", Msg = "#FFFFFF" },
            ["OOC"] = new TypeStyle { Tag = "#90EE90", Name = "#90EE90", Msg = "#90EE90" },
            ["ME"] = new TypeStyle { Tag = "#C77DFF", Name = "#C77DFF", Msg = "#DCB8FF" },
            ["SHOUT"] = new TypeStyle { Tag = "#FFE135", Name = "#FFE135", Msg = "#FFF176" },
            ["WHISPER"] = new TypeStyle { Tag = "#B8C4CE", Name = "#B8C4CE", Msg = "#CDD6DE" },
            ["SYS"] = new TypeStyle { Tag = "#64C8FF", Name = "#64C8FF", Msg = "#B4DCFF" },
            ["ALERT"] = new TypeStyle { Tag = "#DCB8FF", Name = "#DCB8FF", Msg = "#DCB8FF" },
        };

        class Row
        {
            public RectTransform Rt, LabelRt;
            public Text Label;
            public CanvasGroup Cg;
            public float Born;
            public bool Shout;
        }

        GameObject _root;
        Image _msgBg;
        RectTransform _area;
        GameObject _inputBar;
        Text _tagLabel;
        Text _inputText;
        RectTransform _tagRt, _inputRt;
        readonly List<Row> _rows = new List<Row>();

        bool _open;
        string _text = "";
        string _mode = "IC";
        float _caretT;
        float _backspaceHold;
        float _bgAlphaCur;
        float _lastActivity = -99f;
        bool _joinSent;

        // Bubble
        RectTransform _bubbleCanvasRt, _bubbleRt;
        CanvasGroup _bubbleCg;
        Text _bubbleLabel;
        Image _bubbleImg;
        Transform _head;
        float _bubbleHold;
        float _bubbleWobble;
        bool _bubbleTypingMode;
        float _dotT;
        int _dotStep;

        InputService _input;
        ChatService _chat;

        const float FrameW = 480f, MsgH = 188f, AreaH = 182f, RowH = 20f, RowGap = 1f;

        void Start()
        {
            Build();
            EventBus.Subscribe<ChatMessage>(OnChat);
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe<ChatMessage>(OnChat);
            if (_input != null) _input.TextInput -= OnChar;
        }

        // ─────────────────────────────────────────────────────── build ─────
        void Build()
        {
            var canvasGo = new GameObject("ChatCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 520;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _root = canvasGo;

            var frame = UiKit.At(UiKit.Rect(canvasGo.transform, "ChatFrame"),
                10f, 10f, FrameW, 222f);

            _msgBg = UiKit.Fill(frame, "MsgBg", new Color(0f, 0f, 0f, 0f), 4);
            UiKit.At((RectTransform)_msgBg.transform, 0f, 0f, FrameW, MsgH);
            _area = UiKit.At(UiKit.Rect(_msgBg.transform, "Scroll"), 3f, 3f,
                FrameW - 6f, AreaH);
            _area.gameObject.AddComponent<RectMask2D>();

            // ── InputBar (hidden until open) ───────────────────────────────
            var bar = UiKit.Fill(frame, "InputBar", new Color(0f, 0f, 0f, 0.55f), 4);
            UiKit.At((RectTransform)bar.transform, 0f, 222f - 32f, FrameW, 32f);
            _inputBar = bar.gameObject;
            _tagLabel = UiKit.Label(bar.transform, "TagLabel", "[IC]", UiKit.FontBold, 14,
                new Color32(255, 215, 0, 255), TextAnchor.MiddleLeft);
            _tagRt = UiKit.At((RectTransform)_tagLabel.transform, 6f, 0f, 48f, 32f);
            _inputText = UiKit.Label(bar.transform, "InputBox", "", UiKit.FontRegular, 14,
                Color.white, TextAnchor.MiddleLeft);
            _inputRt = UiKit.At((RectTransform)_inputText.transform, 54f, 0f, FrameW - 60f, 32f);
            _inputBar.SetActive(false);

            BuildBubble(canvasGo.transform);
        }

        void BuildBubble(Transform parent)
        {
            // World-space billboard over the head — its own tiny canvas.
            var go = new GameObject("BubbleCanvas");
            go.transform.SetParent(transform, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            _bubbleCanvasRt = (RectTransform)go.transform;
            _bubbleCanvasRt.sizeDelta = new Vector2(240f, 70f);
            _bubbleCanvasRt.localScale = Vector3.one * 0.004f;

            var bubble = UiKit.Fill(go.transform, "Bubble", new Color(1f, 1f, 1f, 0.5f), 8);
            _bubbleImg = bubble;
            _bubbleRt = (RectTransform)bubble.transform;
            _bubbleRt.anchorMin = _bubbleRt.anchorMax = new Vector2(0.5f, 0f);
            _bubbleRt.pivot = new Vector2(0.5f, 0f);
            _bubbleRt.anchoredPosition = Vector2.zero;
            _bubbleCg = bubble.gameObject.AddComponent<CanvasGroup>();

            _bubbleLabel = UiKit.Label(_bubbleRt, "BubbleLabel", "", UiKit.FontRegular, 13,
                Color.white, TextAnchor.MiddleCenter);
            var lrt = (RectTransform)_bubbleLabel.transform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
            lrt.pivot = new Vector2(0.5f, 0.5f);
            _bubbleLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            _bubbleLabel.supportRichText = true;

            go.SetActive(false);
        }

        // ─────────────────────────────────────────────────────── input ─────
        void Update()
        {
            if (_input == null && Services.TryGet(out _input))
                _input.TextInput += OnChar;
            if (_chat == null) Services.TryGet(out _chat);
            if (_input == null || _chat == null) return;

            // The ChatReady flush: publish our own join line once the UI exists.
            if (!_joinSent)
            {
                _joinSent = true;
                _chat.Alert(ChatService.PlayerName + " joined the server");
            }

            if (!_open)
            {
                if ((_input.ChatSlashPressed || _input.ChatReturnPressed)
                    && !_input.GameplayBlocked)
                    Open();
            }
            else
            {
                if (_input.EscapePressed) { Close(); }
                else if (_input.ChatReturnPressed) { Submit(); }
                else if (_input.BackspacePressed) { Backspace(); _backspaceHold = -0.4f; }
                else if (_input.BackspaceHeld)
                {
                    _backspaceHold += Time.deltaTime;
                    if (_backspaceHold >= 1f / 12f) { Backspace(); _backspaceHold = 0f; }
                }
                RefreshInput();
            }

            AnimateRows();
            AnimateBubble();
        }

        void OnChar(char c)
        {
            if (!_open || c < ' ' || c == 127) return;
            if (_text.Length < 160) _text += c;
        }

        void Backspace()
        {
            if (_text.Length > 0) _text = _text.Substring(0, _text.Length - 1);
        }

        void Open()
        {
            _open = true;
            _text = "";
            _inputBar.SetActive(true);
            _input.SetGameplayBlocked(true);   // THE typing gate
            _chat.SetTyping(true);
            _lastActivity = Time.time;
        }

        void Close()
        {
            _open = false;
            _text = "";
            _inputBar.SetActive(false);
            _input.SetGameplayBlocked(false);
            _chat.SetTyping(false);
        }

        void Submit()
        {
            string text = _text.Trim();
            Close();
            if (text.Length == 0) return;
            // /help is fully client-side, exactly as shipped.
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"^\s*/help\s*$"))
            {
                ShowHelp();
                return;
            }
            _chat.Send(text);
        }

        void ShowHelp()
        {
            AddRaw("SYS", "<color=#64C8FF><b>[System]</b> Commands:</color>");
            string[] lines =
            {
                "/me <action> — emote (purple)",
                "/shout <msg> — shout, 30 studs",
                "/whisper <msg> — whisper, 8 studs",
                "/ooc <msg> — out of character",
                "/stats — open the stats panel",
                "/help — this list",
            };
            foreach (var l in lines)
                AddRaw("HELP", $"<color=#9AA4B0>  {l}</color>");
        }

        // Dynamic tag width: measure the mode tag, 48 px floor, slide the box.
        void RefreshInput()
        {
            string mode = "IC";
            if (_text.StartsWith("/whisper")) mode = "WHISPER";
            else if (_text.StartsWith("/shout")) mode = "SHOUT";
            else if (_text.StartsWith("/me")) mode = "ME";
            else if (_text.StartsWith("/ooc")) mode = "OOC";
            _mode = mode;
            _tagLabel.text = mode == "IC" ? "[IC]" : $"[/{mode.ToLowerInvariant()}]";
            var style = Col[mode];
            ColorUtility.TryParseHtmlString(style.Tag, out Color tagCol);
            _tagLabel.color = tagCol;
            float w = Mathf.Max(48f, _tagLabel.preferredWidth + 8f);
            _tagRt.sizeDelta = new Vector2(w, 32f);
            _inputRt.anchoredPosition = new Vector2(6f + w, 0f);
            _inputRt.sizeDelta = new Vector2(FrameW - w - 12f, 32f);

            _caretT += Time.deltaTime;
            string caret = Mathf.Repeat(_caretT, 1f) < 0.5f ? "|" : "";
            _inputText.text = _text.Length == 0
                ? "<color=#9AA4B0>Type a message...</color>" + caret
                : Esc(_text) + caret;
        }

        static string Esc(string s) => s.Replace("<", "<​");

        // ──────────────────────────────────────────────────── messages ─────
        void OnChat(ChatMessage m)
        {
            var style = Col.TryGetValue(m.Type, out var s) ? s : Col["IC"];
            string rich = m.Type switch
            {
                "ME" => $"<color={style.Msg}><i>* {Esc(m.Name)} {Esc(m.Message)}</i></color>",
                "SHOUT" => $"<color={style.Name}><b>{Esc(m.Name)}:</b></color> "
                    + $"<color={style.Msg}><b>*Shouts* \"{Esc(m.Message)}\"</b></color>",
                "WHISPER" => $"<color={style.Name}><b>{Esc(m.Name)}:</b></color> "
                    + $"<color={style.Msg}><i>*Whispers* \"{Esc(m.Message)}\"</i></color>",
                "SYS" => $"<color={style.Tag}><b>[System]</b> {m.Message}</color>",
                "ALERT" => $"<color={style.Msg}><i>{Esc(m.Message)}</i></color>",
                _ => $"<color={style.Name}><b>{Esc(m.Name)}:</b></color> "
                    + $"<color={style.Msg}>{Esc(m.Message)}</color>",
            };
            AddRaw(m.Type, rich);

            if (m.Name == ChatService.PlayerName)
                ShowBubble(m.Type, m.Message);
        }

        void AddRaw(string type, string rich)
        {
            Row row;
            if (_rows.Count >= MaxMsgs)
            {
                row = _rows[0];               // recycle the oldest (MAX_MSGS)
                _rows.RemoveAt(0);
            }
            else
            {
                var rt = UiKit.Rect(_area, "Msg");
                var cg = rt.gameObject.AddComponent<CanvasGroup>();
                var label = UiKit.Label(rt, "Label", "", UiKit.FontRegular, 14,
                    Color.white, TextAnchor.MiddleLeft);
                label.supportRichText = true;
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                row = new Row
                {
                    Rt = rt,
                    LabelRt = UiKit.At((RectTransform)label.transform, 2f, 0f,
                        FrameW - 20f, RowH),
                    Label = label,
                    Cg = cg,
                };
            }
            row.Label.text = rich;
            row.Born = Time.time;
            row.Shout = type == "SHOUT";
            _rows.Add(row);
            _lastActivity = Time.time;
        }

        void AnimateRows()
        {
            // Background: shows fast, fades slow; stays while open.
            bool anyVisible = false;
            float now = Time.time;

            // Bottom-anchored stack, newest at the bottom (list order = age).
            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                int fromNewest = _rows.Count - 1 - i;
                float y = AreaH - 2f - RowH - fromNewest * (RowH + RowGap);
                UiKit.At(row.Rt, 5f, y, FrameW - 16f, RowH);

                float age = now - row.Born;
                // pop-in: label position-only (Back ease from +14 px) — or
                // the /shout rattle (±3 px decaying) instead.
                float baseX = 2f;
                if (row.Shout && age < ShakeTime)
                {
                    float decay = 1f - age / ShakeTime;
                    row.LabelRt.anchoredPosition = new Vector2(
                        baseX + Random.Range(-ShakeAmp, ShakeAmp) * decay,
                        Random.Range(-ShakeAmp, ShakeAmp) * decay);
                }
                else
                {
                    float t = Mathf.Clamp01(age / PopTime);
                    float ease = 1f + 2.70158f * Mathf.Pow(t - 1f, 3f)
                        + 1.70158f * Mathf.Pow(t - 1f, 2f);   // Back-out
                    row.LabelRt.anchoredPosition =
                        new Vector2(baseX + PopSlide * (1f - ease), 0f);
                }

                float pop = Mathf.Clamp01(age / PopTime);
                float ageFade = _open ? 1f
                    : 1f - Mathf.Clamp01((age - RowHold) / RowFade);
                float rel = y;   // px from area top; dissolve inside the band
                float dissolve = Mathf.Clamp01(rel / EdgeBand);
                if (y + RowH < 0f) dissolve = 0f;
                row.Cg.alpha = pop * ageFade * dissolve;
                if (row.Cg.alpha > 0.02f) anyVisible = true;
            }

            float bgTarget = (_open || anyVisible) ? BgAlpha : 0f;
            float rate = bgTarget > _bgAlphaCur ? BgShowTime : BgFadeTime;
            _bgAlphaCur = Mathf.MoveTowards(_bgAlphaCur, bgTarget, Time.deltaTime / rate * BgAlpha);
            _msgBg.color = new Color(0f, 0f, 0f, _bgAlphaCur);
        }

        // ─────────────────────────────────────────────────────── bubble ────
        void ShowBubble(string type, string msg)
        {
            var style = Col.TryGetValue(type, out var s) ? s : Col["IC"];
            string body = type switch
            {
                "SHOUT" => $"*Shouts* {Esc(msg)}",
                "WHISPER" => $"*Whispers* {Esc(msg)}",
                "ME" => Esc(msg),
                _ => Esc(msg),
            };
            _bubbleTypingMode = false;
            _bubbleLabel.text = $"<color={style.Msg}>{body}</color>";
            SizeBubble();
            _bubbleCanvasRt.gameObject.SetActive(true);
            _bubbleCg.alpha = 1f;
            UiFx.PopIn(_bubbleRt, 0.55f, 0.22f);          // popBubble, verbatim
            _bubbleWobble = type == "SHOUT" ? ShakeTime : 0f;
            _bubbleHold = Mathf.Clamp(2f + msg.Length * 0.05f, 3f, 8f);
        }

        void SizeBubble()
        {
            float w = Mathf.Min(_bubbleLabel.preferredWidth + 2f, 224f);
            var lrt = (RectTransform)_bubbleLabel.transform;
            lrt.sizeDelta = new Vector2(w, 0f);
            float h = _bubbleLabel.preferredHeight;
            lrt.sizeDelta = new Vector2(w, h);
            _bubbleRt.sizeDelta = new Vector2(w + 16f, h + 8f);   // UIPadding 8/4
            lrt.anchoredPosition = new Vector2(0f, _bubbleRt.sizeDelta.y * 0.5f);
        }

        void AnimateBubble()
        {
            if (_head == null)
            {
                var motor = FindAnyObjectByType<PlayerMotor>();
                var anim = motor != null ? motor.GetComponentInChildren<Animator>() : null;
                if (anim != null && anim.isHuman)
                    _head = anim.GetBoneTransform(HumanBodyBones.Head);
                if (_head == null) return;
            }

            bool typingVisible = _open && !_bubbleTypingMode && _bubbleHold <= 0f;
            if (typingVisible)
            {
                // Typing dots — message wins; only starts when no message holds.
                _bubbleTypingMode = true;
                _bubbleCanvasRt.gameObject.SetActive(true);
                _bubbleCg.alpha = 1f;
                UiFx.PopIn(_bubbleRt, 0.55f, 0.22f);
            }
            if (_bubbleTypingMode)
            {
                if (!_open) { _bubbleTypingMode = false; _bubbleCg.alpha = 0f; _bubbleCanvasRt.gameObject.SetActive(false); }
                else
                {
                    _dotT += Time.deltaTime;
                    if (_dotT >= BubbleDotT)
                    {
                        _dotT = 0f;
                        _dotStep = (_dotStep + 1) % 3;
                        _bubbleLabel.text = "<color=#FFFFFF>"
                            + new string[] { ". ", ". . ", ". . . " }[_dotStep] + "</color>";
                        SizeBubble();
                    }
                }
            }
            else if (_bubbleHold > 0f)
            {
                _bubbleHold -= Time.deltaTime;
                if (_bubbleHold <= 0f && !_open)
                {
                    _bubbleCg.alpha = 0f;
                    _bubbleCanvasRt.gameObject.SetActive(false);
                }
            }

            if (!_bubbleCanvasRt.gameObject.activeSelf) return;

            // Hover-bob + billboard + shout wobble.
            float bob = Mathf.Sin(Time.time * 2.2f) * 0.12f * 0.28f;
            _bubbleCanvasRt.position = _head.position + Vector3.up * (0.45f + bob);
            var cam = Camera.main;
            if (cam != null)
                _bubbleCanvasRt.rotation = Quaternion.LookRotation(
                    _bubbleCanvasRt.position - cam.transform.position);
            if (_bubbleWobble > 0f)
            {
                _bubbleWobble -= Time.deltaTime;
                float decay = Mathf.Clamp01(_bubbleWobble / ShakeTime);
                _bubbleRt.localRotation = Quaternion.Euler(0f, 0f,
                    Random.Range(-4f, 4f) * decay);
            }
            else
            {
                _bubbleRt.localRotation = Quaternion.identity;
            }
        }
    }
}

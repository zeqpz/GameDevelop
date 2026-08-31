// UiKit — the uGUI toolbox that lets us transcribe Roblox GUI templates
// 1:1 in code. Provides:
//   • The Robloxia color contract (InventoryClient COL_* + template restyle).
//   • Gotham stand-ins: Montserrat Regular/Medium/Bold in Resources/UI/Fonts
//     (Gotham is proprietary; Montserrat is its standard free double).
//   • Rounded(r) — procedural white 9-sliced rounded-rect sprites (UICorner
//     twin); Panel() pairs them into a stroke+fill (UIStroke twin).
//   • Top-left, +y-down coordinate helpers so measurements lift straight
//     off the Roblox dump without sign gymnastics.
//   • CloseTexture() — drawn twin of the close-button asset (rbxassetid
//     81780211651007 is private; a 401 met the download).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    public static class UiKit
    {
        // ── Robloxia color contract ────────────────────────────────────────
        public static readonly Color WinBg = new Color32(10, 10, 10, 235);
        public static readonly Color TitleBg = new Color32(12, 12, 12, 255);
        public static readonly Color WinStroke = new Color32(104, 104, 104, 255);
        public static readonly Color Gold = new Color32(237, 190, 38, 255);
        public static readonly Color TextCol = new Color32(210, 210, 210, 255);
        public static readonly Color Muted = new Color32(120, 120, 120, 255);
        public static readonly Color CellBg = new Color32(27, 27, 27, 255);
        public static readonly Color CellStroke = new Color32(82, 82, 82, 140);   // trans 0.45
        public static readonly Color Valid = new Color32(45, 160, 45, 255);
        public static readonly Color Invalid = new Color32(160, 45, 45, 255);

        static Font _regular, _medium, _bold;
        public static Font FontRegular => _regular != null ? _regular
            : _regular = LoadFont("UI/Fonts/Montserrat-Regular");
        public static Font FontMedium => _medium != null ? _medium
            : _medium = LoadFont("UI/Fonts/Montserrat-Medium");
        public static Font FontBold => _bold != null ? _bold
            : _bold = LoadFont("UI/Fonts/Montserrat-Bold");

        static Font LoadFont(string path)
        {
            var f = Resources.Load<Font>(path);
            return f != null ? f : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        // ── Rounded-rect sprites (UICorner twin) ───────────────────────────
        static readonly Dictionary<int, Sprite> _rounded = new Dictionary<int, Sprite>();

        public static Sprite Rounded(int radius)
        {
            int r = Mathf.Max(1, radius);
            if (_rounded.TryGetValue(r, out var cached)) return cached;

            int size = r * 2 + 8;
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    var c = new Vector2(Mathf.Clamp(p.x, r, size - r), Mathf.Clamp(p.y, r, size - r));
                    float a = Mathf.Clamp01(r - Vector2.Distance(p, c) + 0.5f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, new Vector4(r + 3, r + 3, r + 3, r + 3));
            _rounded[r] = sprite;
            return sprite;
        }

        // ── Builders (Roblox top-left coords: +y goes DOWN) ────────────────
        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            return rt;
        }

        public static RectTransform At(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        public static Image Fill(Transform parent, string name, Color color, int radius = 0)
        {
            var img = Rect(parent, name).gameObject.AddComponent<Image>();
            img.color = color;
            if (radius > 0) { img.sprite = Rounded(radius); img.type = Image.Type.Sliced; }
            img.raycastTarget = false;
            return img;
        }

        // UIStroke twin: outer rounded rect in the stroke color, fill inset by
        // the stroke thickness. Position children on the OUTER rect.
        public static (RectTransform outer, Image fill) Panel(Transform parent, string name,
            float x, float y, float w, float h, Color bg, int radius,
            Color stroke, float strokeThickness = 1f)
        {
            var outerImg = Fill(parent, name, stroke, radius + 1);
            var outer = At((RectTransform)outerImg.transform, x, y, w, h);
            var fill = Fill(outer, "Fill", bg, radius);
            var frt = (RectTransform)fill.transform;
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
            frt.pivot = new Vector2(0.5f, 0.5f);
            frt.offsetMin = new Vector2(strokeThickness, strokeThickness);
            frt.offsetMax = new Vector2(-strokeThickness, -strokeThickness);
            return (outer, fill);
        }

        public static Text Label(Transform parent, string name, string text, Font font,
            int size, Color color, TextAnchor anchor)
        {
            var t = Rect(parent, name).gameObject.AddComponent<Text>();
            t.text = text;
            t.font = font;
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        // Drawn twin of the (private) Roblox close asset: a centered ×.
        static Texture2D _closeTex;
        public static Texture2D CloseTexture()
        {
            if (_closeTex != null) return _closeTex;
            const int n = 24;
            _closeTex = new Texture2D(n, n, TextureFormat.ARGB32, false);
            _closeTex.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    bool inBody = x >= 5 && x <= 18 && y >= 5 && y <= 18;
                    float d = Mathf.Min(Mathf.Abs(x - y), Mathf.Abs(x + y - (n - 1)));
                    float a = inBody ? Mathf.Clamp01(2.0f - d) : 0f;
                    _closeTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            _closeTex.Apply();
            return _closeTex;
        }

        // ── Hand-rolled hit testing (no EventSystem; Roblox-style px math) ─
        public static bool Contains(RectTransform rt, Vector2 screenPos) =>
            RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos);

        // Screen point → this rect's TOP-LEFT-origin local coords (+y down).
        public static Vector2 LocalTopLeft(RectTransform rt, Vector2 screenPos)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screenPos, null, out var local);
            var r = rt.rect;
            return new Vector2(local.x - r.xMin, r.yMax - local.y);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _rounded.Clear();
            _closeTex = null;
            _regular = _medium = _bold = null;
        }
    }
}

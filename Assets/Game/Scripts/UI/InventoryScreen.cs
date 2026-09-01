// InventoryScreen — the Robloxia InventoryGui, transcribed 1:1 from the live
// template (extracted from Studio 2026-08-31: ReplicatedStorage.GUITemplates
// .InventoryClient) and wired to the Unity data core. The numbers ARE the
// Roblox numbers:
//   • Win 840×592 centered · TitleBar 36 (#0C0C0C) · gold AccentLine 40×2
//     at (14,36) · SpecialBar 44 with six 36px key/phone cells (42px step)
//   • Content at y90: LeftPanel 250 (character box 230², "Equipment:" label,
//     six 33px equip rows at 37px step, names per template: Head Clothing /
//     Shirt / Pants / Shoes / Backpack / Jacket-Vest)
//   • Grid: CELL=22 GAP=1 → STEP=23, 20×20 = 459px (InventoryClient
//     constants), cells #1B1B1B stroke #525252@0.45
//   • Drag: ghost @0.35 transparency follows the mouse, footprint cells
//     tint COL_VALID (45,160,45) / COL_INVALID (160,45,45), R rotates
//     mid-drag; right-click opens the 188px context menu (94px header +
//     35px rows: Equip green / Discard red)
//   • Tab toggles (InventoryClient:2454); Esc closes ctx → drag → screen
// Icons are flat def-color tiles for now — the Roblox build uses 3D
// ViewportFrames, which arrive with the RenderTexture icon rig later.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Game.Audio;
using Game.Core;
using Game.Inventory;

namespace Game.UI
{
    public class InventoryScreen : MonoBehaviour
    {
        const int Cell = 22, Gap = 1, Step = 23, GridN = 20;
        const float WinW = 840f, WinH = 592f;

        static readonly (EquipSlot slot, string label)[] RowDefs =
        {
            (EquipSlot.Head, "Head Clothing:"),
            (EquipSlot.Torso, "Shirt:"),
            (EquipSlot.Legs, "Pants:"),
            (EquipSlot.Feet, "Shoes:"),
            (EquipSlot.Back, "Backpack:"),
            (EquipSlot.Jacket, "Jacket / Vest:"),
        };

        class EquipRow
        {
            public EquipSlot Slot;
            public RectTransform Rt;
            public Text ItemLabel;
            public Image Hover;
            public bool Occupied;
        }

        class CtxRow
        {
            public RectTransform Rt;
            public Image Bg;
            public System.Action Action;
        }

        GameObject _root;
        RectTransform _canvasRt, _gridRt, _itemLayer, _ghostRt, _ctxRt;
        Text _ghostLabel, _ctxName, _ctxSize, _ctxDesc;
        Image _ctxIcon;
        Image[,] _cellFills;
        readonly List<(RectTransform rt, ItemStack stack)> _tiles =
            new List<(RectTransform, ItemStack)>();
        readonly List<EquipRow> _rows = new List<EquipRow>();
        readonly List<CtxRow> _ctxRows = new List<CtxRow>();
        RectTransform _ctxRowHolder;

        bool _open;
        bool _ctxClosing;
        ItemStack _drag;
        bool _dragRot;
        float _gsx, _gsy, _gsvx, _gsvy, _grot;   // dangle spring state
        CanvasGroup _ghostCg;
        ItemStack _ctxStack;
        InputService _input;
        InventoryService _inv;

        void Start()
        {
            BuildStatic();
            EventBus.Subscribe<InventoryChanged>(OnInvChanged);
        }

        void OnDestroy() => EventBus.Unsubscribe<InventoryChanged>(OnInvChanged);

        void OnInvChanged(InventoryChanged e)
        {
            if (_open && _drag == null) RebuildDynamic();
        }

        // ─────────────────────────────────────────────────── static build ──
        void BuildStatic()
        {
            var canvasGo = new GameObject("InventoryCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 600;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _canvasRt = (RectTransform)canvasGo.transform;
            _root = canvasGo;

            // Overlay dim
            var overlay = UiKit.Fill(canvasGo.transform, "Overlay", new Color(0f, 0f, 0f, 0.5f));
            var ort = (RectTransform)overlay.transform;
            ort.anchorMin = Vector2.zero; ort.anchorMax = Vector2.one;
            ort.offsetMin = ort.offsetMax = Vector2.zero;

            // ── Win 840×592, centered, gradient-dark + stroke 104 ──────────
            var (winOuter, _) = UiKit.Panel(canvasGo.transform, "Win", 0f, 0f, WinW, WinH,
                UiKit.WinBg, 18, UiKit.WinStroke, 1.5f);
            winOuter.anchorMin = winOuter.anchorMax = new Vector2(0.5f, 0.5f);
            winOuter.pivot = new Vector2(0.5f, 0.5f);
            winOuter.anchoredPosition = Vector2.zero;
            var win = winOuter;

            // TitleBar 36px
            var title = UiKit.Fill(win, "TitleBar", UiKit.TitleBg, 8);
            UiKit.At((RectTransform)title.transform, 0f, 0f, WinW, 36f);
            UiKit.At((RectTransform)UiKit.Label(title.transform, "TitleLbl", "Inventory",
                UiKit.FontBold, 13, Color.white, TextAnchor.MiddleLeft).transform,
                14f, 0f, 600f, 36f);
            UiKit.At((RectTransform)UiKit.Label(title.transform, "SubtitleLbl",
                "View Owned and equipped items", UiKit.FontMedium, 10,
                new Color(1f, 1f, 1f, 0.55f), TextAnchor.MiddleLeft).transform,
                92f, 1f, 500f, 36f);
            var closeRt = UiKit.Rect(title.transform, "CloseBtn");
            UiKit.At(closeRt, WinW - 32f, 6f, 24f, 24f);
            var closeRaw = closeRt.gameObject.AddComponent<RawImage>();
            closeRaw.texture = UiKit.CloseTexture();
            _closeRaw = closeRaw;

            UiKit.At((RectTransform)UiKit.Fill(win, "AccentLine", UiKit.Gold).transform,
                14f, 36f, 40f, 2f);

            // ── SpecialBar (Keys / Phone) ──────────────────────────────────
            var bar = UiKit.Fill(win, "SpecialBar", new Color32(22, 22, 22, 255), 6);
            UiKit.At((RectTransform)bar.transform, 10f, 40f, WinW - 20f, 44f);
            UiKit.At((RectTransform)UiKit.Label(bar.transform, "SpecialLbl", "Keys / Phone:",
                UiKit.FontMedium, 9, Color.white, TextAnchor.MiddleLeft).transform,
                10f, 0f, 100f, 44f);
            for (int i = 0; i < 6; i++)
                UiKit.Panel(bar.transform, $"Special{i + 1}", 104f + i * 42f, 4f, 36f, 36f,
                    UiKit.CellBg, 2, new Color32(82, 82, 82, 255));

            // ── LeftPanel: character box + equipment rows ──────────────────
            UiKit.At((RectTransform)UiKit.Fill(win, "Divider",
                new Color32(45, 45, 45, 255)).transform, 250f, 100f, 1f, 482f);

            var viewport = UiKit.Fill(win, "Viewport", UiKit.CellBg, 7);
            UiKit.At((RectTransform)viewport.transform, 10f, 100f, 230f, 230f);
            UiKit.At((RectTransform)UiKit.Label(viewport.transform, "VpInnerLbl", "CHARACTER",
                UiKit.FontBold, 9, UiKit.Muted, TextAnchor.MiddleLeft).transform,
                8f, 208f, 200f, 18f);

            UiKit.At((RectTransform)UiKit.Label(win, "EqLabel", "Equipment:",
                UiKit.FontMedium, 10, Color.white, TextAnchor.MiddleLeft).transform,
                10f, 338f, 200f, 18f);
            UiKit.At((RectTransform)UiKit.Fill(win, "EqLine", UiKit.Gold).transform,
                10f, 356f, 30f, 1f);

            for (int i = 0; i < RowDefs.Length; i++)
            {
                var rowBg = UiKit.Fill(win, RowDefs[i].slot + "Slot",
                    new Color32(32, 32, 32, 255), 5);
                var rt = UiKit.At((RectTransform)rowBg.transform, 10f, 364f + i * 37f, 222f, 33f);
                UiKit.At((RectTransform)UiKit.Label(rt, "NameLabel", RowDefs[i].label,
                    UiKit.FontMedium, 11, Color.white, TextAnchor.MiddleLeft).transform,
                    12f, 0f, 110f, 33f);
                var itemLbl = UiKit.Label(rt, "ItemLabel", "Empty",
                    UiKit.FontRegular, 10, UiKit.Muted, TextAnchor.MiddleRight);
                UiKit.At((RectTransform)itemLbl.transform, 122f, 0f, 86f, 33f);
                var hover = UiKit.Fill(rt, "UnequipHover", new Color32(170, 60, 60, 0), 5);
                var hrt = (RectTransform)hover.transform;
                hrt.anchorMin = Vector2.zero; hrt.anchorMax = Vector2.one;
                hrt.offsetMin = hrt.offsetMax = Vector2.zero;
                _rows.Add(new EquipRow
                { Slot = RowDefs[i].slot, Rt = rt, ItemLabel = itemLbl, Hover = hover });
            }

            // ── RightPanel: grid header + 20×20 cell field ─────────────────
            UiKit.At((RectTransform)UiKit.Label(win, "InvLbl", "Inventory:",
                UiKit.FontMedium, 10, Color.white, TextAnchor.MiddleLeft).transform,
                270f, 100f, 200f, 18f);
            UiKit.At((RectTransform)UiKit.Fill(win, "InvLine", UiKit.Gold).transform,
                270f, 118f, 30f, 1f);
            UiKit.At((RectTransform)UiKit.Label(win, "GridSizeLbl", $"{GridN}x{GridN}",
                UiKit.FontRegular, 10, UiKit.Muted, TextAnchor.MiddleRight).transform,
                270f, 100f, 560f, 18f);

            _gridRt = UiKit.At(UiKit.Rect(win, "GridContainer"), 270f, 126f,
                GridN * Step - Gap, GridN * Step - Gap);
            var cellLayer = UiKit.Rect(_gridRt, "CellLayer");
            UiKit.At(cellLayer, 0f, 0f, _gridRt.sizeDelta.x, _gridRt.sizeDelta.y);
            _cellFills = new Image[GridN, GridN];
            for (int y = 0; y < GridN; y++)
                for (int x = 0; x < GridN; x++)
                {
                    var (_, fill) = UiKit.Panel(cellLayer, $"c{x}_{y}", x * Step, y * Step,
                        Cell, Cell, UiKit.CellBg, 2, UiKit.CellStroke);
                    _cellFills[x, y] = fill;
                }
            _itemLayer = UiKit.At(UiKit.Rect(_gridRt, "ItemLayer"), 0f, 0f,
                _gridRt.sizeDelta.x, _gridRt.sizeDelta.y);

            // ── DragGhost (Roblox: bg 163,162,165 @0.35, label bold 10) ────
            _ghostRt = UiKit.Rect(canvasGo.transform, "DragGhost");
            _ghostRt.anchorMin = _ghostRt.anchorMax = new Vector2(0.5f, 0.5f);
            _ghostRt.pivot = new Vector2(0.5f, 0.5f);
            var ghostImg = _ghostRt.gameObject.AddComponent<Image>();
            ghostImg.color = new Color32(163, 162, 165, 166);
            ghostImg.sprite = UiKit.Rounded(2);
            ghostImg.type = Image.Type.Sliced;
            ghostImg.raycastTarget = false;
            _ghostLabel = UiKit.Label(_ghostRt, "GhostLabel", "", UiKit.FontBold, 10,
                Color.white, TextAnchor.MiddleCenter);
            var glrt = (RectTransform)_ghostLabel.transform;
            glrt.anchorMin = Vector2.zero; glrt.anchorMax = Vector2.one;
            glrt.offsetMin = glrt.offsetMax = Vector2.zero;
            _ghostCg = _ghostRt.gameObject.AddComponent<CanvasGroup>();
            _ghostRt.gameObject.SetActive(false);

            // ── Context menu (188 wide, header 94, rows 35) ────────────────
            var (ctxOuter, _) = UiKit.Panel(canvasGo.transform, "CtxMenu", 0f, 0f, 188f, 129f,
                new Color32(18, 18, 18, 255), 5, new Color32(65, 65, 65, 255));
            _ctxRt = ctxOuter;
            _ctxRt.anchorMin = _ctxRt.anchorMax = new Vector2(0.5f, 0.5f);
            _ctxRt.pivot = new Vector2(0f, 1f);
            var header = UiKit.Fill(_ctxRt, "CtxHeader", new Color32(28, 28, 28, 255), 5);
            UiKit.At((RectTransform)header.transform, 1f, 1f, 186f, 93f);
            _ctxIcon = UiKit.Fill(header.transform, "CtxIcon", new Color32(80, 80, 80, 255), 4);
            UiKit.At((RectTransform)_ctxIcon.transform, 9f, 9f, 40f, 40f);
            _ctxName = UiKit.Label(header.transform, "CtxName", "Item", UiKit.FontBold, 12,
                new Color32(225, 225, 225, 255), TextAnchor.MiddleLeft);
            UiKit.At((RectTransform)_ctxName.transform, 57f, 9f, 120f, 20f);
            _ctxSize = UiKit.Label(header.transform, "CtxSize", "1x1", UiKit.FontRegular, 10,
                UiKit.Muted, TextAnchor.MiddleLeft);
            UiKit.At((RectTransform)_ctxSize.transform, 57f, 29f, 120f, 14f);
            _ctxDesc = UiKit.Label(header.transform, "CtxDesc", "", UiKit.FontRegular, 10,
                new Color32(160, 160, 160, 255), TextAnchor.UpperLeft);
            UiKit.At((RectTransform)_ctxDesc.transform, 12f, 52f, 162f, 36f);
            _ctxRowHolder = UiKit.At(UiKit.Rect(_ctxRt, "Rows"), 0f, 94f, 188f, 70f);
            _ctxRt.gameObject.SetActive(false);

            _root.SetActive(false);
        }

        RawImage _closeRaw;

        // ────────────────────────────────────────────────── dynamic build ──
        void RebuildDynamic()
        {
            if (_inv == null) return;

            foreach (var (rt, _) in _tiles)
                if (rt != null) Destroy(rt.gameObject);
            _tiles.Clear();

            foreach (var pair in _inv.Player.Grid.Entries)
            {
                var stack = pair.Key;
                var pos = pair.Value;
                var size = stack.Size;
                float w = size.x * Step - Gap, h = size.y * Step - Gap;
                var tint = stack.Def.tileColor;
                bool equipped = _inv.Player.IsEquipped(stack);   // green stroke + E badge
                var (outer, _) = UiKit.Panel(_itemLayer, "Item_" + stack.Def.id,
                    pos.x * Step, pos.y * Step, w, h,
                    new Color(tint.r, tint.g, tint.b, 0.88f), 2,
                    equipped ? (Color)new Color32(45, 160, 45, 255) : new Color(0f, 0f, 0f, 0.9f));

                var strip = UiKit.Fill(outer, "NameStrip", new Color(0f, 0f, 0f, 0.65f));
                UiKit.At((RectTransform)strip.transform, 0f, h - 16f, w, 16f);
                var nameLbl = UiKit.Label(strip.transform, "NameLabel", stack.Def.displayName,
                    UiKit.FontBold, 13, Color.white, TextAnchor.MiddleCenter);
                var nrt = (RectTransform)nameLbl.transform;
                nrt.anchorMin = Vector2.zero; nrt.anchorMax = Vector2.one;
                nrt.offsetMin = nrt.offsetMax = Vector2.zero;
                nameLbl.horizontalOverflow = HorizontalWrapMode.Wrap; // clip tiny tiles

                if (stack.Count > 1)
                {
                    var badge = UiKit.Fill(outer, "QtyBadge", new Color(0f, 0f, 0f, 0.75f), 3);
                    UiKit.At((RectTransform)badge.transform, w - 24f, 2f, 22f, 16f);
                    var qty = UiKit.Label(badge.transform, "Qty", "x" + stack.Count,
                        UiKit.FontBold, 11, new Color32(255, 230, 100, 255), TextAnchor.MiddleCenter);
                    var qrt = (RectTransform)qty.transform;
                    qrt.anchorMin = Vector2.zero; qrt.anchorMax = Vector2.one;
                    qrt.offsetMin = qrt.offsetMax = Vector2.zero;
                }

                if (equipped)   // EquipBadge template: 16×14 green "E", top-left
                {
                    var badge = UiKit.Fill(outer, "EquipBadge", new Color32(50, 140, 50, 230), 3);
                    UiKit.At((RectTransform)badge.transform, 2f, 2f, 16f, 14f);
                    var eLbl = UiKit.Label(badge.transform, "E", "E", UiKit.FontBold, 10,
                        Color.white, TextAnchor.MiddleCenter);
                    var ert = (RectTransform)eLbl.transform;
                    ert.anchorMin = Vector2.zero; ert.anchorMax = Vector2.one;
                    ert.offsetMin = ert.offsetMax = Vector2.zero;
                }
                _tiles.Add((outer, stack));
            }

            foreach (var row in _rows)
            {
                bool has = _inv.Player.Equipped.TryGetValue(row.Slot, out var eq);
                row.Occupied = has;
                row.ItemLabel.text = has ? eq.Def.displayName : "Empty";
                row.ItemLabel.color = has ? UiKit.TextCol : UiKit.Muted;
                row.Hover.color = new Color32(170, 60, 60, 0);
            }
        }

        // ─────────────────────────────────────────────────────── input ─────
        void Update()
        {
            if (_input == null) Services.TryGet(out _input);
            if (_inv == null) Services.TryGet(out _inv);
            if (_input == null || _inv == null) return;

            if (_input.InventoryTogglePressed)
            {
                if (_open) Close();
                else if (!_input.GameplayBlocked) Open();   // another screen owns it
                return;
            }
            if (!_open) return;

            Vector2 mouse = _input.MousePosition;

            if (_input.EscapePressed)
            {
                if (_ctxRt.gameObject.activeSelf) CloseCtx();
                else if (_drag != null) CancelDrag();
                else Close();
                return;
            }

            if (_drag != null) { UpdateDrag(mouse); return; }

            if (_ctxRt.gameObject.activeSelf) { UpdateCtx(mouse); return; }

            // Hovers
            _closeRaw.color = UiKit.Contains((RectTransform)_closeRaw.transform, mouse)
                ? new Color32(255, 120, 120, 255) : Color.white;
            foreach (var row in _rows)
                row.Hover.color = new Color32(170, 60, 60,
                    (byte)(row.Occupied && UiKit.Contains(row.Rt, mouse) ? 46 : 0));

            if (_input.UiClickPressed)
            {
                if (UiKit.Contains((RectTransform)_closeRaw.transform, mouse)) { Close(); return; }
                foreach (var row in _rows)
                    if (row.Occupied && UiKit.Contains(row.Rt, mouse))
                    { _inv.Player.TryUnequip(row.Slot); return; }
                for (int i = _tiles.Count - 1; i >= 0; i--)
                    if (_tiles[i].rt != null && UiKit.Contains(_tiles[i].rt, mouse))
                    { StartDrag(_tiles[i].stack); return; }
            }
            else if (_input.UiRightPressed)
            {
                for (int i = _tiles.Count - 1; i >= 0; i--)
                    if (_tiles[i].rt != null && UiKit.Contains(_tiles[i].rt, mouse))
                    { OpenCtx(_tiles[i].stack, mouse); return; }
            }
        }

        void Open()
        {
            _open = true;
            _root.SetActive(true);
            _input.SetGameplayBlocked(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            RebuildDynamic();
            PlayUi(ProceduralAudio.InvZip(), 0.55f, 1.05f);   // bag unzip
        }

        void Close()
        {
            CloseCtx(true);              // instant: the whole screen is going away
            if (_drag != null) CancelDrag(true);
            _open = false;
            _root.SetActive(false);
            _input.SetGameplayBlocked(false);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            PlayUi(ProceduralAudio.InvZip(), 0.5f, 0.88f);    // lower pitch = zip shut
        }

        void PlayUi(AudioClip clip, float vol, float pitch)
        {
            if (Services.TryGet(out AudioService audio)) audio.PlayUi(clip, vol, pitch);
        }

        // InventoryClient's per-category PlaybackSpeed offsets, mapped onto
        // our categories (weapon 0.85 · scrap 0.80 · food/med 1.1 · cloth 1.0).
        static float CategoryPitch(ItemCategory cat) => cat switch
        {
            ItemCategory.Weapon => 0.85f,
            ItemCategory.Material => 0.8f,
            ItemCategory.Consumable => 1.1f,
            ItemCategory.Container => 0.95f,
            _ => 1.0f,
        };

        // ──────────────────────────────────────────────────────── drag ─────
        void StartDrag(ItemStack stack)
        {
            _drag = stack;
            _dragRot = stack.Rotated;
            SetGhostSize();
            _ghostLabel.text = stack.Def.displayName;

            // Seed the dangle spring at the grab point (drag.sx/sy/svx/svy/rot).
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRt, _input.MousePosition, null, out var seed);
            _gsx = seed.x;
            _gsy = seed.y;
            _gsvx = _gsvy = _grot = 0f;
            _ghostRt.anchoredPosition = seed;
            _ghostRt.localRotation = Quaternion.identity;
            _ghostCg.alpha = 1f;                       // fade is one-way; reset on reuse
            _ghostRt.gameObject.SetActive(true);
            UiFx.PopIn(_ghostRt, UiFx.GhostFrom);      // pops into your hand
            PlayUi(ProceduralAudio.UiTick(), 0.7f, 1f);
            CloseCtx(true);   // Roblox: ghost pops in the same frame the menu snaps
        }

        void SetGhostSize()
        {
            var d = _drag.Def.gridSize;
            int w = _dragRot ? d.y : d.x, h = _dragRot ? d.x : d.y;
            _ghostRt.sizeDelta = new Vector2(w * Step - Gap, h * Step - Gap);
        }

        void UpdateDrag(Vector2 mouse)
        {
            if (_input.UiRotatePressed)
            {
                _dragRot = !_dragRot;
                SetGhostSize();
                UiFx.PopIn(_ghostRt, 0.90f, 0.10f);    // rotateDrag's re-pop
                PlayUi(ProceduralAudio.UiThud(), 0.45f, CategoryPitch(_drag.Def.category));
            }

            // Dangle spring (InventoryClient render loop, verbatim constants):
            // an underdamped chase — the held item trails the cursor, leans
            // into travel (±14°), overshoots and wobbles settled in ~0.5 s.
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRt, mouse, null, out var local);
            float step = Mathf.Min(Time.deltaTime, 1f / 30f);
            float sDecay = 1f - Mathf.Min(12f * step, 0.9f);
            _gsvx = _gsvx * sDecay + (local.x - _gsx) * 180f * step;
            _gsvy = _gsvy * sDecay + (local.y - _gsy) * 180f * step;
            _gsx += _gsvx * step;
            _gsy += _gsvy * step;
            _ghostRt.anchoredPosition = new Vector2(_gsx, _gsy);
            float lean = Mathf.Clamp(_gsvx * 0.035f, -14f, 14f);
            _grot += (lean - _grot) * Mathf.Min(step * 14f, 1f);
            _ghostRt.localRotation = Quaternion.Euler(0f, 0f, -_grot);

            var d = _drag.Def.gridSize;
            int w = _dragRot ? d.y : d.x, h = _dragRot ? d.x : d.y;
            var g = UiKit.LocalTopLeft(_gridRt, mouse);
            bool inGrid = g.x >= 0f && g.y >= 0f
                && g.x < _gridRt.sizeDelta.x && g.y < _gridRt.sizeDelta.y;
            int cx = Mathf.Clamp(Mathf.FloorToInt(g.x / Step), 0, GridN - w);
            int cy = Mathf.Clamp(Mathf.FloorToInt(g.y / Step), 0, GridN - h);
            bool valid = inGrid && _inv.Player.Grid.CanPlaceAt(_drag, cx, cy, _dragRot);

            ResetCellTints();
            if (inGrid)
                for (int dx = 0; dx < w; dx++)
                    for (int dy = 0; dy < h; dy++)
                        if (cx + dx < GridN && cy + dy < GridN)
                            _cellFills[cx + dx, cy + dy].color =
                                valid ? UiKit.Valid : UiKit.Invalid;

            if (_input.UiClickReleased)
            {
                ResetCellTints();
                var stack = _drag;
                _drag = null;
                bool placed = valid && _inv.Player.Grid.TryMove(stack, cx, cy, _dragRot);
                if (placed)
                    PlayUi(ProceduralAudio.UiThud(), 0.55f, CategoryPitch(stack.Def.category));
                // releaseGhost: the ghost is detached garbage now — pop-fade it
                // away; a re-pick mid-tween supersedes cleanly (gen guard).
                UiFx.PopOut(_ghostRt, UiFx.GhostTo, UiFx.OutTime, _ghostCg,
                    () => _ghostRt.gameObject.SetActive(false));
                _inv.Player.NotifyChanged();   // rebuild either way (revert too)
            }
        }

        void CancelDrag(bool instant = false)
        {
            ResetCellTints();
            _drag = null;
            if (instant)
            {
                UiFx.Snap(_ghostRt);
                _ghostRt.gameObject.SetActive(false);
            }
            else
            {
                UiFx.PopOut(_ghostRt, UiFx.GhostTo, UiFx.OutTime, _ghostCg,
                    () => _ghostRt.gameObject.SetActive(false));
            }
            RebuildDynamic();
        }

        void ResetCellTints()
        {
            for (int y = 0; y < GridN; y++)
                for (int x = 0; x < GridN; x++)
                    _cellFills[x, y].color = UiKit.CellBg;
        }

        // ──────────────────────────────────────────────── context menu ─────
        void OpenCtx(ItemStack stack, Vector2 mouse)
        {
            _ctxStack = stack;
            foreach (var r in _ctxRows) if (r.Rt != null) Destroy(r.Rt.gameObject);
            _ctxRows.Clear();

            var tint = stack.Def.tileColor;
            _ctxIcon.color = new Color(tint.r, tint.g, tint.b, 1f);
            _ctxName.text = stack.Def.displayName;
            _ctxSize.text = $"{stack.Def.gridSize.x}x{stack.Def.gridSize.y}  ·  " +
                $"{stack.WeightLbs:0.0} lbs";
            _ctxDesc.text = stack.Def.category.ToString();

            int y = 0;
            if (stack.Def.equipSlot != EquipSlot.None)
            {
                // In-grid equips (weapons) toggle here; clothing tiles only
                // ever show "Equip" (equipped clothing leaves the grid).
                bool isEquipped = _inv.Player.IsEquipped(stack);
                AddCtxRow(y++, isEquipped ? "Unequip" : "Equip",
                    new Color32(110, 205, 110, 255), () =>
                {
                    if (isEquipped) _inv.Player.TryUnequip(_ctxStack.Def.equipSlot);
                    else _inv.Player.TryEquip(_ctxStack);
                    CloseCtx();
                });
            }
            AddCtxRow(y++, "Discard", new Color32(210, 75, 75, 255), () =>
            {
                _inv.Player.Remove(_ctxStack);
                CloseCtx();
            });

            float height = 94f + y * 35f;
            _ctxRt.sizeDelta = new Vector2(188f, height);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRt, mouse, null, out var local);
            var half = _canvasRt.rect.size * 0.5f;
            local.x = Mathf.Min(local.x, half.x - 190f);
            local.y = Mathf.Max(local.y, -half.y + height + 2f);
            _ctxRt.anchoredPosition = local;
            _ctxClosing = false;
            _ctxRt.gameObject.SetActive(true);
            UiFx.PopIn(_ctxRt, UiFx.MenuFrom);   // deterministic: re-seeds every call
        }

        void AddCtxRow(int index, string label, Color textColor, System.Action action)
        {
            var bg = UiKit.Fill(_ctxRowHolder, label + "Btn", new Color32(38, 38, 38, 0));
            var rt = UiKit.At((RectTransform)bg.transform, 1f, index * 35f, 186f, 35f);
            UiKit.At((RectTransform)UiKit.Label(rt, "Lbl", label, UiKit.FontRegular, 12,
                textColor, TextAnchor.MiddleLeft).transform, 14f, 0f, 160f, 35f);
            _ctxRows.Add(new CtxRow { Rt = rt, Bg = bg, Action = action });
        }

        void UpdateCtx(Vector2 mouse)
        {
            if (_ctxClosing) return;   // backdrop-swallow while the out-tween plays
            foreach (var row in _ctxRows)
                row.Bg.color = new Color32(38, 38, 38,
                    (byte)(UiKit.Contains(row.Rt, mouse) ? 255 : 0));

            if (_input.UiClickPressed)
            {
                foreach (var row in _ctxRows)
                    if (UiKit.Contains(row.Rt, mouse)) { row.Action(); return; }
                CloseCtx();
            }
            else if (_input.UiRightPressed)
            {
                CloseCtx();
            }
        }

        void CloseCtx(bool instant = false)
        {
            _ctxStack = null;
            if (instant || !_ctxRt.gameObject.activeSelf)
            {
                UiFx.Snap(_ctxRt);          // never strand a mid-flight scale
                _ctxClosing = false;
                _ctxRt.gameObject.SetActive(false);
                return;
            }
            _ctxClosing = true;
            UiFx.PopOut(_ctxRt, UiFx.MenuTo, UiFx.OutTime, null, () =>
            {
                _ctxClosing = false;
                _ctxRt.gameObject.SetActive(false);
            });
        }
    }
}

// ItemGrid — the spatial half of the inventory: a W×H cell field where each
// stack occupies its (possibly rotated) footprint. Pure model, zero UI.
// Placement rules are the Roblox grid's: no overlap, in-bounds, rotation
// swaps the footprint. TryAdd fills existing stacks first (stacking pass),
// then first-fits new stacks in either rotation.
using System.Collections.Generic;
using UnityEngine;

namespace Game.Inventory
{
    public class ItemGrid
    {
        public readonly int Width, Height;
        readonly ItemStack[,] _cells;
        readonly Dictionary<ItemStack, Vector2Int> _origins =
            new Dictionary<ItemStack, Vector2Int>();

        public IReadOnlyDictionary<ItemStack, Vector2Int> Entries => _origins;

        public ItemGrid(int width, int height)
        {
            Width = width;
            Height = height;
            _cells = new ItemStack[width, height];
        }

        public ItemStack At(int x, int y) =>
            x >= 0 && y >= 0 && x < Width && y < Height ? _cells[x, y] : null;

        public bool Contains(ItemStack s) => _origins.ContainsKey(s);

        bool FitsAt(Vector2Int size, int x, int y, ItemStack ignore)
        {
            if (x < 0 || y < 0 || x + size.x > Width || y + size.y > Height) return false;
            for (int dx = 0; dx < size.x; dx++)
                for (int dy = 0; dy < size.y; dy++)
                {
                    var cell = _cells[x + dx, y + dy];
                    if (cell != null && cell != ignore) return false;
                }
            return true;
        }

        public bool CanPlace(ItemStack s, int x, int y) => FitsAt(s.Size, x, y, s);

        // Probe a placement in a hypothetical rotation without mutating the stack.
        public bool CanPlaceAt(ItemStack s, int x, int y, bool rotated)
        {
            var size = rotated
                ? new Vector2Int(s.Def.gridSize.y, s.Def.gridSize.x)
                : s.Def.gridSize;
            return FitsAt(size, x, y, s);
        }

        public bool Place(ItemStack s, int x, int y)
        {
            if (_origins.ContainsKey(s) || !CanPlace(s, x, y)) return false;
            SetCells(s, new Vector2Int(x, y), s);
            _origins[s] = new Vector2Int(x, y);
            return true;
        }

        public bool Remove(ItemStack s)
        {
            if (!_origins.TryGetValue(s, out var origin)) return false;
            SetCells(s, origin, null);
            _origins.Remove(s);
            return true;
        }

        // Reposition/rotate in one atomic step; rolls back on failure.
        public bool TryMove(ItemStack s, int x, int y, bool rotated)
        {
            if (!_origins.TryGetValue(s, out var oldOrigin)) return false;
            bool oldRot = s.Rotated;
            SetCells(s, oldOrigin, null);
            s.Rotated = rotated;
            if (FitsAt(s.Size, x, y, s))
            {
                _origins[s] = new Vector2Int(x, y);
                SetCells(s, _origins[s], s);
                return true;
            }
            s.Rotated = oldRot;
            SetCells(s, oldOrigin, s);
            return false;
        }

        public bool FindFirstFit(ItemStack s)
        {
            if (_origins.ContainsKey(s)) return true;
            bool startRot = s.Rotated;
            for (int pass = 0; pass < 2; pass++)
            {
                s.Rotated = pass == 0 ? startRot : !startRot;
                for (int y = 0; y < Height; y++)
                    for (int x = 0; x < Width; x++)
                        if (FitsAt(s.Size, x, y, null))
                        {
                            SetCells(s, new Vector2Int(x, y), s);
                            _origins[s] = new Vector2Int(x, y);
                            return true;
                        }
            }
            s.Rotated = startRot;
            return false;
        }

        // Stacking pass first, then new stacks; returns how many were added.
        public int TryAdd(ItemDef def, int count)
        {
            if (def == null || count <= 0) return 0;
            int remaining = count;

            foreach (var stack in _origins.Keys)
            {
                if (remaining == 0) break;
                if (stack.Def != def || stack.Count >= def.stackMax) continue;
                int take = Mathf.Min(def.stackMax - stack.Count, remaining);
                stack.Count += take;
                remaining -= take;
            }

            while (remaining > 0)
            {
                int take = Mathf.Min(def.stackMax, remaining);
                var fresh = new ItemStack(def, take);
                if (!FindFirstFit(fresh)) break;   // grid full
                remaining -= take;
            }
            return count - remaining;
        }

        public float TotalWeightLbs
        {
            get
            {
                float sum = 0f;
                foreach (var stack in _origins.Keys) sum += stack.WeightLbs;
                return sum;
            }
        }

        public void Clear()
        {
            _origins.Clear();
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                    _cells[x, y] = null;
        }

        void SetCells(ItemStack s, Vector2Int origin, ItemStack value)
        {
            var size = s.Size;
            for (int dx = 0; dx < size.x; dx++)
                for (int dy = 0; dy < size.y; dy++)
                    _cells[origin.x + dx, origin.y + dy] = value;
        }
    }
}

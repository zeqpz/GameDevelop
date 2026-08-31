// Inventory — one entity's stuff: a main grid plus equip slots. THE equip
// rule, learned the hard way on Roblox (the three-place equip-flag gotcha):
// equipped state has exactly ONE source of truth — membership in _equipped.
// Equipping REMOVES the stack from the grid; unequipping first-fits it back.
// Nothing else anywhere flags "equipped".
//
// Persistence is a flat list (SavedStack) with parent indices for container
// contents — JsonUtility-safe, parents always serialized before children.
// Publishes InventoryChanged on the EventBus after every mutation batch.
using System.Collections.Generic;
using UnityEngine;
using Game.Core;
using Game.Data;

namespace Game.Inventory
{
    public readonly struct InventoryChanged
    {
        public readonly string Owner;
        public InventoryChanged(string owner) { Owner = owner; }
    }

    public class Inventory
    {
        public readonly string OwnerId;
        public readonly ItemGrid Grid;
        readonly Dictionary<EquipSlot, ItemStack> _equipped =
            new Dictionary<EquipSlot, ItemStack>();

        public IReadOnlyDictionary<EquipSlot, ItemStack> Equipped => _equipped;

        public Inventory(string ownerId, int width, int height)
        {
            OwnerId = ownerId;
            Grid = new ItemGrid(width, height);
        }

        public int TryAdd(string defId, int count)
        {
            int added = Grid.TryAdd(ItemCatalog.Get(defId), count);
            if (added > 0) NotifyChanged();
            return added;
        }

        public ItemStack FindFirst(string defId)
        {
            foreach (var stack in Grid.Entries.Keys)
                if (stack.Def != null && stack.Def.id == defId) return stack;
            return null;
        }

        // Equip from the grid; an occupied slot swaps its item back to the grid.
        public bool TryEquip(ItemStack stack)
        {
            if (stack == null || stack.Def.equipSlot == EquipSlot.None) return false;
            EquipSlot slot = stack.Def.equipSlot;
            if (!Grid.Remove(stack)) return false;

            if (_equipped.TryGetValue(slot, out var prev))
            {
                if (!Grid.FindFirstFit(prev))
                {
                    Grid.FindFirstFit(stack);   // no room for the swap: undo
                    return false;
                }
                _equipped.Remove(slot);
            }
            _equipped[slot] = stack;
            NotifyChanged();
            return true;
        }

        public bool TryUnequip(EquipSlot slot)
        {
            if (!_equipped.TryGetValue(slot, out var stack)) return false;
            if (!Grid.FindFirstFit(stack)) return false;   // grid full: refuse
            _equipped.Remove(slot);
            NotifyChanged();
            return true;
        }

        // Discard from the grid (world-drop spawning comes later).
        public bool Remove(ItemStack stack)
        {
            if (!Grid.Remove(stack)) return false;
            NotifyChanged();
            return true;
        }

        public float TotalWeightLbs
        {
            get
            {
                float sum = Grid.TotalWeightLbs;
                foreach (var stack in _equipped.Values) sum += stack.WeightLbs;
                return sum;
            }
        }

        public void NotifyChanged() => EventBus.Publish(new InventoryChanged(OwnerId));

        // ── Persistence: flat list, parents before children ────────────────
        public List<SavedStack> ToSave()
        {
            var list = new List<SavedStack>();
            var indexOf = new Dictionary<ItemStack, int>();

            foreach (var pair in Grid.Entries)
                WriteStack(pair.Key, pair.Value, (int)EquipSlot.None - 1, -1, list, indexOf);
            foreach (var pair in _equipped)
                WriteStack(pair.Value, new Vector2Int(-1, -1), (int)pair.Key, -1, list, indexOf);
            return list;
        }

        static void WriteStack(ItemStack stack, Vector2Int pos, int slot, int parent,
            List<SavedStack> list, Dictionary<ItemStack, int> indexOf)
        {
            list.Add(new SavedStack
            {
                defId = stack.Def.id,
                count = stack.Count,
                rotated = stack.Rotated,
                x = pos.x,
                y = pos.y,
                slot = slot,
                parent = parent,
            });
            int myIndex = list.Count - 1;
            indexOf[stack] = myIndex;
            if (stack.Container == null) return;
            foreach (var inner in stack.Container.Entries)
                WriteStack(inner.Key, inner.Value, -1, myIndex, list, indexOf);
        }

        public void FromSave(List<SavedStack> saved)
        {
            Grid.Clear();
            _equipped.Clear();
            if (saved == null) { NotifyChanged(); return; }

            var stacks = new ItemStack[saved.Count];
            for (int i = 0; i < saved.Count; i++)
            {
                if (!ItemCatalog.TryGet(saved[i].defId, out var def))
                {
                    Debug.LogWarning($"[Inventory] Dropping unknown saved item '{saved[i].defId}'");
                    continue;
                }
                stacks[i] = new ItemStack(def, Mathf.Max(1, saved[i].count))
                { Rotated = saved[i].rotated };
            }

            // Parents are always earlier in the list than their children.
            for (int i = 0; i < saved.Count; i++)
            {
                var stack = stacks[i];
                if (stack == null) continue;
                var s = saved[i];

                if (s.parent >= 0)
                {
                    var parentGrid = s.parent < stacks.Length ? stacks[s.parent]?.Container : null;
                    if (parentGrid == null || !parentGrid.Place(stack, s.x, s.y))
                        if (parentGrid == null || !parentGrid.FindFirstFit(stack))
                            Grid.FindFirstFit(stack);   // container gone: spill to grid
                }
                else if (s.slot > (int)EquipSlot.None)
                {
                    var slot = (EquipSlot)s.slot;
                    if (stack.Def.equipSlot == slot && !_equipped.ContainsKey(slot))
                        _equipped[slot] = stack;
                    else
                        Grid.FindFirstFit(stack);
                }
                else if (!Grid.Place(stack, s.x, s.y))
                {
                    Grid.FindFirstFit(stack);
                }
            }
            NotifyChanged();
        }
    }
}

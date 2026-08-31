// ItemCatalog — the item database, built in code (no assets to click
// together). Starter set covers every mechanic the data core supports:
// stackables, multi-cell footprints, clothing, a weapon, and a container.
// Real content grows here; an editor bake to .asset files can come later
// without changing callers.
using System.Collections.Generic;
using UnityEngine;

namespace Game.Inventory
{
    public static class ItemCatalog
    {
        static Dictionary<string, ItemDef> _defs;

        public static ItemDef Get(string id)
        {
            if (TryGet(id, out var def)) return def;
            Debug.LogWarning($"[ItemCatalog] Unknown item id '{id}'");
            return null;
        }

        public static bool TryGet(string id, out ItemDef def)
        {
            if (_defs == null) Build();
            return _defs.TryGetValue(id, out def);
        }

        public static IEnumerable<ItemDef> All
        {
            get { if (_defs == null) Build(); return _defs.Values; }
        }

        static void Build()
        {
            _defs = new Dictionary<string, ItemDef>();
            Add("scrap_metal", "Scrap Metal", ItemCategory.Material, 1, 1, stack: 8, lbs: 0.9f,
                tint: new Color32(110, 110, 115, 255));
            Add("rice_bag", "Rice Bag", ItemCategory.Material, 2, 1, stack: 5, lbs: 2.2f,
                tint: new Color32(168, 142, 84, 255));
            Add("water_bottle", "Water Bottle", ItemCategory.Consumable, 1, 2, stack: 3, lbs: 1.1f,
                tint: new Color32(70, 130, 180, 255));
            Add("tshirt", "T-Shirt", ItemCategory.Clothing, 2, 2, stack: 1, lbs: 0.4f,
                slot: EquipSlot.Torso, tint: new Color32(100, 130, 90, 255));
            Add("pistol", "Pistol", ItemCategory.Weapon, 2, 1, stack: 1, lbs: 1.5f,
                slot: EquipSlot.Hand, tint: new Color32(58, 58, 66, 255));
            Add("backpack", "Backpack", ItemCategory.Container, 3, 3, stack: 1, lbs: 1.8f,
                slot: EquipSlot.Back, container: new Vector2Int(6, 4),
                tint: new Color32(122, 92, 62, 255));
        }

        static void Add(string id, string name, ItemCategory cat, int w, int h,
            int stack, float lbs, EquipSlot slot = EquipSlot.None,
            Vector2Int container = default, Color32 tint = default)
        {
            var def = ScriptableObject.CreateInstance<ItemDef>();
            def.name = id;
            def.id = id;
            def.displayName = name;
            def.category = cat;
            def.gridSize = new Vector2Int(w, h);
            def.stackMax = stack;
            def.weightLbs = lbs;
            def.equipSlot = slot;
            def.containerSize = container;
            if (tint.a != 0) def.tileColor = tint;
            _defs[id] = def;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _defs = null;
    }
}

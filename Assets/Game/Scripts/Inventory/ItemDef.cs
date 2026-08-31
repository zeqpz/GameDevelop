// ItemDef — one item type's immutable stats: footprint on the grid, stack
// cap, weight (lbs — the Roblox item-weight system's unit), equip slot, and
// container dimensions when the item IS a container (backpacks). Defined as
// a ScriptableObject so assets can exist later, but the shipping set is
// built in code by ItemCatalog, MovementSettings-defaults style.
using UnityEngine;

namespace Game.Inventory
{
    public enum ItemCategory { Material, Consumable, Clothing, Weapon, Container, Misc }

    // Save-stable: values are persisted as ints — append, never reorder.
    public enum EquipSlot
    { None = 0, Head = 1, Torso = 2, Legs = 3, Feet = 4, Back = 5, Hand = 6, Jacket = 7 }

    [CreateAssetMenu(menuName = "Game/Item Def")]
    public class ItemDef : ScriptableObject
    {
        public string id;
        public string displayName;
        public ItemCategory category = ItemCategory.Misc;
        public Vector2Int gridSize = Vector2Int.one;
        public int stackMax = 1;
        public float weightLbs = 1f;
        public EquipSlot equipSlot = EquipSlot.None;
        public Vector2Int containerSize;   // zero = not a container
        public Color tileColor = new Color32(105, 105, 112, 255);   // grid tile fill

        public bool IsContainer => containerSize.x > 0 && containerSize.y > 0;
    }
}

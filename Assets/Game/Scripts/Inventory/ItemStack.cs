// ItemStack — a live pile of one item type: count, grid rotation (persisted,
// per the Roblox inventory), and — when the def is a container — its own
// ItemGrid of contents, so a dropped or unequipped backpack keeps everything
// inside it. Weight recurses through contents.
using UnityEngine;

namespace Game.Inventory
{
    public class ItemStack
    {
        public readonly ItemDef Def;
        public int Count;
        public bool Rotated;
        public ItemGrid Container { get; }   // non-null only for container defs

        public Vector2Int Size => Rotated
            ? new Vector2Int(Def.gridSize.y, Def.gridSize.x)
            : Def.gridSize;

        public float WeightLbs =>
            Def.weightLbs * Count + (Container?.TotalWeightLbs ?? 0f);

        public ItemStack(ItemDef def, int count)
        {
            Def = def;
            Count = count;
            if (def.IsContainer)
                Container = new ItemGrid(def.containerSize.x, def.containerSize.y);
        }
    }
}

// InventoryService — owns the player's Inventory instance and the glue:
//   • Loads from the SaveService profile (or grants the starter kit once),
//     writes back on every save via the OnBeforeSave hook.
//   • Carry weight → PlayerMotor.ExternalSpeedMult (the Roblox item-weight
//     rule: no penalty under 40% capacity, easing to overweightSpeedMult at
//     max). Recomputed on every InventoryChanged.
//   • DumpToLog paints the grid as ASCII in the console — the data core's
//     "UI" until the real inventory screen lands.
using System.Text;
using UnityEngine;
using Game.Core;
using Game.Data;
using Game.Movement;

namespace Game.Inventory
{
    public class InventoryService
    {
        public Inventory Player { get; } = new Inventory("player", 20, 20); // Roblox GRID_SIZE

        bool _loaded;
        PlayerMotor _motor;

        public InventoryService()
        {
            EventBus.Subscribe<InventoryChanged>(OnChanged);
            SaveService.OnBeforeSave += WriteProfile;
        }

        public void Shutdown()
        {
            EventBus.Unsubscribe<InventoryChanged>(OnChanged);
            SaveService.OnBeforeSave -= WriteProfile;
        }

        public void Tick()
        {
            if (!_loaded && SaveService.Profile != null) LoadFromProfile();
            if (_motor == null)
            {
                _motor = Object.FindAnyObjectByType<PlayerMotor>();
                if (_motor != null) ApplyCarryWeight();
            }
        }

        public bool GrantPlayer(string defId, int count = 1)
        {
            int added = Player.TryAdd(defId, count);
            if (added > 0)
            {
                var def = ItemCatalog.Get(defId);
                Debug.Log($"[Inventory] +{added} {def.displayName} " +
                    $"({Player.TotalWeightLbs:0.0} lbs carried)");
            }
            else
            {
                Debug.Log("[Inventory] No room!");
            }
            return added > 0;
        }

        void OnChanged(InventoryChanged e)
        {
            if (e.Owner == Player.OwnerId) ApplyCarryWeight();
        }

        void ApplyCarryWeight()
        {
            if (_motor == null || _motor.settings == null) return;
            var s = _motor.settings;
            float w = Player.TotalWeightLbs;
            float t = Mathf.InverseLerp(s.maxCarryLbs * 0.4f, s.maxCarryLbs, w);
            _motor.ExternalSpeedMult = Mathf.Lerp(1f, s.overweightSpeedMult, Mathf.Clamp01(t));
        }

        void LoadFromProfile()
        {
            _loaded = true;
            var profile = SaveService.Profile;
            if (profile.hasInventory) Player.FromSave(profile.inventory);
            else GrantStarterKit();
            ApplyCarryWeight();
            DumpToLog();
        }

        void GrantStarterKit()
        {
            Player.TryAdd("backpack", 1);
            Player.TryEquip(Player.FindFirst("backpack"));
            Player.TryAdd("tshirt", 1);
            Player.TryEquip(Player.FindFirst("tshirt"));
            Player.TryAdd("pistol", 1);
            Player.TryAdd("rice_bag", 3);
            Player.TryAdd("scrap_metal", 2);

            // Water rides inside the equipped backpack — proves nesting and
            // the flat-save round trip in one move.
            var pack = Player.Equipped.TryGetValue(EquipSlot.Back, out var b) ? b : null;
            pack?.Container?.TryAdd(ItemCatalog.Get("water_bottle"), 2);
            Player.NotifyChanged();
            Debug.Log("[Inventory] Starter kit granted");
        }

        void WriteProfile(PlayerProfile profile)
        {
            if (!_loaded) return;
            profile.inventory = Player.ToSave();
            profile.hasInventory = true;
        }

        public void DumpToLog()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[Inventory] {Player.TotalWeightLbs:0.0} lbs " +
                $"(speed ×{(_motor != null ? _motor.ExternalSpeedMult : 1f):0.00})");

            var letters = new System.Collections.Generic.Dictionary<ItemStack, char>();
            char next = 'A';
            foreach (var stack in Player.Grid.Entries.Keys) letters[stack] = next++;

            for (int y = 0; y < Player.Grid.Height; y++)
            {
                for (int x = 0; x < Player.Grid.Width; x++)
                {
                    var cell = Player.Grid.At(x, y);
                    sb.Append(cell != null ? letters[cell] : '·');
                }
                sb.AppendLine();
            }
            foreach (var pair in letters)
                sb.AppendLine($"  {pair.Value} = {pair.Key.Def.displayName} ×{pair.Key.Count}" +
                    (pair.Key.Rotated ? " (rotated)" : ""));
            foreach (var pair in Player.Equipped)
            {
                sb.Append($"  [{pair.Key}] {pair.Value.Def.displayName}");
                if (pair.Value.Container != null)
                    sb.Append($" — holds {pair.Value.Container.TotalWeightLbs:0.0} lbs");
                sb.AppendLine();
            }
            Debug.Log(sb.ToString());
        }
    }
}

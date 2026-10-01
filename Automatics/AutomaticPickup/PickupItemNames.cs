using System.Collections.Generic;
using System.Linq;

namespace Automatics.AutomaticPickup
{
    internal enum PickupItemCategory
    {
        Ground,
        Berries,
        Flora,
        Farming
    }

    // Fixed, curated list of "hand pick" objects that the deny_pickup_item
    // setting can target, grouped into categories. This is deliberately a
    // static list (not a scan of ZNetScene) because the Pickable component is
    // an unreliable signal on its own: some objects that carry it still
    // require a tool (e.g. Pickable_Tin needs a pickaxe), so only prefabs the
    // user has actually confirmed are true hand-pick nodes are included here.
    internal static class PickupItemNames
    {
        private static readonly (string DisplayName, string PrefabName, PickupItemCategory Category)[]
            Definitions =
            {
                ("Branch", "Pickable_Branch", PickupItemCategory.Ground),
                ("Timberwood Branch", "Pickable_Branch_Snow", PickupItemCategory.Ground),
                ("Flint", "Pickable_Flint", PickupItemCategory.Ground),
                ("Stone", "Pickable_Stone", PickupItemCategory.Ground),
                ("Pet Rock", "Pickable_StoneRock", PickupItemCategory.Ground),
                ("Snowball", "Pickable_Snowball", PickupItemCategory.Ground),

                ("Raspberry", "RaspberryBush", PickupItemCategory.Berries),
                ("Blueberry", "BlueberryBush", PickupItemCategory.Berries),
                ("Cloudberry", "CloudberryBush", PickupItemCategory.Berries),
                ("Lingonberry", "LingonberryBush", PickupItemCategory.Berries),

                ("Mushroom", "Pickable_Mushroom", PickupItemCategory.Flora),
                ("Yellow Mushroom", "Pickable_Mushroom_yellow", PickupItemCategory.Flora),
                ("Blue Mushroom", "Pickable_Mushroom_blue", PickupItemCategory.Flora),
                ("Jotun Puffs", "Pickable_Mushroom_JotunPuffs", PickupItemCategory.Flora),
                ("Magecap", "Pickable_Mushroom_Magecap", PickupItemCategory.Flora),
                ("Thistle", "Pickable_Thistle", PickupItemCategory.Flora),
                ("Dandelion", "Pickable_Dandelion", PickupItemCategory.Flora),
                ("Smoke Puff", "Pickable_SmokePuff", PickupItemCategory.Flora),
                ("Fiddlehead", "Pickable_Fiddlehead", PickupItemCategory.Flora),
                ("Royal Jelly", "Pickable_RoyalJelly", PickupItemCategory.Flora),

                ("Carrot", "Pickable_Carrot", PickupItemCategory.Farming),
                ("Turnip", "Pickable_Turnip", PickupItemCategory.Farming),
                ("Onion", "Pickable_Onion", PickupItemCategory.Farming),
                ("Barley", "Pickable_Barley", PickupItemCategory.Farming),
                ("Wild Barley", "Pickable_Barley_Wild", PickupItemCategory.Farming),
                ("Flax", "Pickable_Flax", PickupItemCategory.Farming),
                ("Wild Flax", "Pickable_Flax_Wild", PickupItemCategory.Farming),
                ("Kale", "Pickable_Kale", PickupItemCategory.Farming),
                ("Oat", "Pickable_Oat", PickupItemCategory.Farming),
                ("Carrot Seeds", "Pickable_SeedCarrot", PickupItemCategory.Farming),
                ("Turnip Seeds", "Pickable_SeedTurnip", PickupItemCategory.Farming),
                ("Onion Seeds", "Pickable_SeedOnion", PickupItemCategory.Farming),
                ("Kale Seeds", "Pickable_SeedKale", PickupItemCategory.Farming)
            };

        private static readonly Dictionary<string, string> PrefabToDisplayName =
            Definitions.ToDictionary(x => x.PrefabName, x => x.DisplayName);

        private static readonly Dictionary<string, PickupItemCategory> PrefabToCategory =
            Definitions.ToDictionary(x => x.PrefabName, x => x.Category);

        public static IEnumerable<string> GetAll()
        {
            return Definitions.Select(x => x.PrefabName);
        }

        public static IEnumerable<string> GetByCategory(PickupItemCategory category)
        {
            return Definitions
                .Where(x => x.Category == category)
                .OrderBy(x => x.DisplayName)
                .Select(x => x.PrefabName);
        }

        public static string GetDisplayName(string prefabName)
        {
            return PrefabToDisplayName.TryGetValue(prefabName, out var name) ? name : prefabName;
        }

        public static bool TryGetCategory(string prefabName, out PickupItemCategory category)
        {
            return PrefabToCategory.TryGetValue(prefabName, out category);
        }
    }
}

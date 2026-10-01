using BepInEx.Configuration;
using ModUtils;

namespace Automatics.AutomaticPickup
{
    internal static class Config
    {
        private const string Section = "automatic_pickup";

        private static ConfigEntry<AutomaticsModule> _module;
        private static ConfigEntry<float> _automaticPickupRange;
        private static ConfigEntry<float> _automaticPickupInterval;
        private static ConfigEntry<KeyboardShortcut> _pickupAllNearbyKey;
        private static ConfigEntry<bool> _denyPickupCategoryGround;
        private static ConfigEntry<StringList> _denyPickupItemGround;
        private static ConfigEntry<bool> _denyPickupCategoryBerries;
        private static ConfigEntry<StringList> _denyPickupItemBerries;
        private static ConfigEntry<bool> _denyPickupCategoryFlora;
        private static ConfigEntry<StringList> _denyPickupItemFlora;
        private static ConfigEntry<bool> _denyPickupCategoryFarming;
        private static ConfigEntry<StringList> _denyPickupItemFarming;

        public static bool ModuleDisabled => _module.Value == AutomaticsModule.Disabled;

        public static float AutomaticPickupRange => _automaticPickupRange.Value;
        public static float AutomaticPickupInterval => _automaticPickupInterval.Value;
        public static KeyboardShortcut PickupAllNearbyKey => _pickupAllNearbyKey.Value;

        public static bool IsPickupDenied(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (!PickupItemNames.TryGetCategory(name, out var category)) return false;

            if (IsCategoryDenied(category)) return true;
            return DenyListOf(category).Value.Contains(name);
        }

        private static bool IsCategoryDenied(PickupItemCategory category)
        {
            switch (category)
            {
                case PickupItemCategory.Ground: return _denyPickupCategoryGround.Value;
                case PickupItemCategory.Berries: return _denyPickupCategoryBerries.Value;
                case PickupItemCategory.Flora: return _denyPickupCategoryFlora.Value;
                case PickupItemCategory.Farming: return _denyPickupCategoryFarming.Value;
                default: return false;
            }
        }

        private static ConfigEntry<StringList> DenyListOf(PickupItemCategory category)
        {
            switch (category)
            {
                case PickupItemCategory.Ground: return _denyPickupItemGround;
                case PickupItemCategory.Berries: return _denyPickupItemBerries;
                case PickupItemCategory.Flora: return _denyPickupItemFlora;
                case PickupItemCategory.Farming: return _denyPickupItemFarming;
                default: return null;
            }
        }

        public static void Initialize()
        {
            var config = global::Automatics.Config.Instance;

            config.ChangeSection(Section);
            _module = config.Bind("module", AutomaticsModule.Enabled, initializer: x =>
            {
                x.DispName = Automatics.L10N.Translate("@config_common_disable_module_name");
                x.Description = Automatics.L10N.Translate("@config_common_disable_module_description");
            });

            if (_module.Value == AutomaticsModule.Disabled) return;

            _automaticPickupRange = config.Bind("automatic_pickup_range", 4f, (1f, 64f));
            _automaticPickupInterval = config.Bind("automatic_pickup_interval", 0.5f, (0f, 4f));
            _pickupAllNearbyKey = config.Bind("pickup_all_nearby_key", new KeyboardShortcut());

            _denyPickupCategoryGround = config.Bind("deny_pickup_category_ground", false);
            _denyPickupItemGround = BindDenyPickupItem(config, "deny_pickup_item_ground",
                PickupItemCategory.Ground);

            _denyPickupCategoryBerries = config.Bind("deny_pickup_category_berries", false);
            _denyPickupItemBerries = BindDenyPickupItem(config, "deny_pickup_item_berries",
                PickupItemCategory.Berries);

            _denyPickupCategoryFlora = config.Bind("deny_pickup_category_flora", false);
            _denyPickupItemFlora = BindDenyPickupItem(config, "deny_pickup_item_flora",
                PickupItemCategory.Flora);

            _denyPickupCategoryFarming = config.Bind("deny_pickup_category_farming", false);
            _denyPickupItemFarming = BindDenyPickupItem(config, "deny_pickup_item_farming",
                PickupItemCategory.Farming);
        }

        private static ConfigEntry<StringList> BindDenyPickupItem(
            Configuration config, string key, PickupItemCategory category)
        {
            return config.Bind(key, new StringList(), initializer: x =>
            {
                x.CustomDrawer = ConfigurationCustomDrawer.MultiSelect(
                    () => PickupItemNames.GetByCategory(category), PickupItemNames.GetDisplayName);
            });
        }
    }
}

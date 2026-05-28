using System.Collections.Generic;
using System.Linq;
using Automatics.Valheim;
using ModUtils;
using UnityEngine;

namespace Automatics.AutomaticProcessing
{
    using ContainerList = List<(Container container, float distance)>;

    internal static class Globals
    {
        public static ValheimObject Container => ValheimObject.Container;
    }

    internal static class Logics
    {
        private static readonly Dictionary<string, ContainerList> Containers;
        private static float _lastContainersReset;

        static Logics()
        {
            Containers = new Dictionary<string, ContainerList>();
        }

        public static void Cleanup()
        {
            Containers.Clear();
            _lastContainersReset = 0f;
        }

        public static void CraftingLog(string materialName, int count, string fromName,
            Vector3 fromPos, string toName, Vector3 toPos, string productName)
        {
            const string format = "{0} x{1} was set from {2}{3} to {4}{5} for crafting {6}";
            Automatics.Logger.Debug(() =>
                string.Format(format, Automatics.L10N.Translate(materialName), count,
                    Automatics.L10N.Translate(fromName), fromPos, Automatics.L10N.Translate(toName),
                    toPos, Automatics.L10N.Translate(productName)));
        }

        public static void RefuelLog(string fuelName, int count, string toName, Vector3 toPos,
            string fromName, Vector3 fromPos)
        {
            const string format = "Refueled {0} x{1} in {2}{3} from {4}{5}";
            Automatics.Logger.Debug(() =>
                string.Format(format, Automatics.L10N.Translate(fuelName), count,
                    Automatics.L10N.Translate(toName), toPos,
                    Automatics.L10N.Translate(fromName), fromPos));
        }

        public static void StoreLog(string productName, int count, string toName, Vector3 toPos,
            string fromName, Vector3 fromPos)
        {
            const string format = "Stored {0} x{1} in {2}{3} from {4}{5}";
            Automatics.Logger.Debug(() =>
                string.Format(format, Automatics.L10N.Translate(productName), count,
                    Automatics.L10N.Translate(toName), toPos,
                    Automatics.L10N.Translate(fromName), fromPos));
        }

        public static void ChargeLog(string itemName, int count, string toName, Vector3 toPos,
            string fromName, Vector3 fromPos)
        {
            const string format = "Charge {0} x{1} to {2}{3} from {4}{5}";
            Automatics.Logger.Debug(() =>
                string.Format(format, Automatics.L10N.Translate(itemName), count,
                    Automatics.L10N.Translate(toName), toPos,
                    Automatics.L10N.Translate(fromName), fromPos));
        }

        public static bool IsAllowContainer(Container container)
        {
            return ContainerAccess.IsAllowed(container, Config.AllowContainer);
        }

        public static bool IsAllowProcessing(string target, Process type)
        {
            return (Config.AllowProcessing(target) & type) != 0;
        }

        // Container.OnContainerChanged only saves to the ZDO when the local
        // peer owns the ZNetView, so on dedicated servers and non-host clients
        // a mutation of the local mirror is silently dropped and reverted on
        // the next Container.Load. Claim ownership immediately before mutating
        // so the change is persisted. Returns false when the container or its
        // ZNetView became invalid between selection and claim — in that case
        // the caller must skip the mutation.
        public static bool TryClaimContainer(Container container)
        {
            return ContainerAccess.TryClaimContainer(container);
        }

        public static bool TryRemoveItem(Inventory inventory, string itemName, int minCount,
            out string prefabName)
        {
            prefabName = null;
            if (inventory == null || string.IsNullOrEmpty(itemName)) return false;

            if (!Inventories.HaveItem(inventory, itemName, 0, WorldLevelMatchMode.Ignore,
                    minCount + 1))
                return false;

            foreach (var item in Inventories.GetItems(inventory, itemName, 0,
                         WorldLevelMatchMode.Ignore))
                if (TryRemoveItem(inventory, item, out prefabName))
                    return true;

            return false;
        }

        public static bool TryRemoveItem(Inventory inventory, ItemDrop.ItemData item,
            out string prefabName)
        {
            prefabName = null;
            if (inventory == null || item == null || item.m_dropPrefab == null) return false;

            prefabName = item.m_dropPrefab.name;
            return inventory.RemoveItem(item, 1);
        }

        public static int AddItemAndGetCountDelta(Inventory inventory, GameObject prefab,
            string itemName, int amount)
        {
            if (inventory == null || prefab == null || amount <= 0) return 0;

            var itemCountBefore = inventory.CountItems(itemName);
            inventory.AddItem(prefab, amount);
            return Mathf.Max(0, inventory.CountItems(itemName) - itemCountBefore);
        }

        public static int GetProductStackLimitedAmount(Inventory inventory, string itemName,
            int maxStackSize, int maxProductStacks, int amount)
        {
            if (inventory == null || amount <= 0) return 0;
            if (maxProductStacks <= 0) return amount;

            var maxProductCount = maxProductStacks * Mathf.Max(1, maxStackSize);
            var remainingCapacity = maxProductCount - inventory.CountItems(itemName);
            return Mathf.Clamp(remainingCapacity, 0, amount);
        }

        public static IEnumerable<(Container container, float distance)> GetNearbyContainers(
            string target, Vector3 origin)
        {
            if (Time.time - _lastContainersReset > 1f)
            {
                _lastContainersReset = Time.time;
                Containers.Clear();
            }

            var range = Config.ContainerSearchRange(target);
            var limit = Config.ContainerReferenceLimit(target);
            var cacheKey = target + ":" + origin.GetHashCode() + ":" + range + ":" + limit;
            if (Containers.TryGetValue(cacheKey, out var cache)) return cache.Where(x => x.container);

            var containers = new ContainerList();
            Containers[cacheKey] = containers;

            if (range > 0)
                containers.AddRange((from x in ContainerCache.GetAllInstance()
                    let distance = Vector3.Distance(origin, x.transform.position)
                    where distance <= range && IsAllowContainer(x)
                    orderby distance
                    select (x, distance)).Take(limit > 0 ? limit : int.MaxValue));

            return containers;
        }
    }
}

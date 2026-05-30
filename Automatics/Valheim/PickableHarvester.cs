using ModUtils;
using UnityEngine;

namespace Automatics.Valheim
{
    /// <summary>
    /// Shared owner-side <see cref="Pickable"/> harvest logic (amount scaling,
    /// inventory free-space/weight checks, bonus yield, extra drops, effects, and
    /// the <c>RPC_SetPicked</c> finalize). Both Automatic Pickup and Automatic
    /// Farming harvest crops through this helper so the two modules never derive
    /// the inventory-award behaviour independently.
    /// </summary>
    internal static class PickableHarvester
    {
        internal enum HarvestOutcome
        {
            /// <summary>Nothing was picked (already picked, disabled, no yield, or no inventory room).</summary>
            NotPicked,

            /// <summary>This peer owned the ZDO and awarded the yield directly.</summary>
            PickedByOwner,

            /// <summary>This peer did not own the ZDO; pickup was delegated to the owner via vanilla RPC.</summary>
            DelegatedToOwner
        }

        // Direct inventory awards are safe only while this peer owns the
        // pickable ZDO. Non-owners delegate to vanilla owner-side pickup RPCs.
        public static HarvestOutcome Harvest(Player player, Pickable pickable, ZNetView zNetView)
        {
            if (pickable.GetPicked() || pickable.GetEnabled == 0) return HarvestOutcome.NotPicked;

            var amount = GetPickableAmount(pickable);
            if (amount <= 0) return HarvestOutcome.NotPicked;

            if (!zNetView.IsOwner())
            {
                if (CanAddItem(player, pickable.m_itemPrefab, amount))
                {
                    pickable.Interact(player, false, false);
                    return HarvestOutcome.DelegatedToOwner;
                }

                return HarvestOutcome.NotPicked;
            }

            if (!CanAddItem(player, pickable.m_itemPrefab, amount)) return HarvestOutcome.NotPicked;

            var bonus = RollPickableBonus(player, pickable);

            var inventory = player.GetInventory();
            var offset = 0;
            var itemCount = amount + bonus;
            var addableItemCount = GetAddableStack(player, pickable.m_itemPrefab, itemCount);
            var addedItemCount = AddItem(inventory, pickable.m_itemPrefab, addableItemCount);
            DropRemaining(pickable, pickable.m_itemPrefab, ref offset, itemCount - addedItemCount);
            CreatePickableEffects(pickable, bonus);

            if (!pickable.m_extraDrops.IsEmpty())
            {
                foreach (var item in pickable.m_extraDrops.GetDropListItems())
                {
                    var addableStack = GetAddableStack(player, item.m_dropPrefab, item.m_stack);
                    var addedStack = AddItem(inventory, item.m_dropPrefab, addableStack);
                    DropRemaining(pickable, item.m_dropPrefab, ref offset, item.m_stack - addedStack);
                }
            }

            if (pickable.m_aggravateRange > 0f)
                BaseAI.AggravateAllInArea(pickable.transform.position, pickable.m_aggravateRange,
                    BaseAI.AggravatedReason.Theif);

            zNetView.InvokeRPC(ZNetView.Everybody, "RPC_SetPicked", true);
            return HarvestOutcome.PickedByOwner;
        }

        public static bool CanAddItem(Player player, ItemDrop.ItemData itemData, int stack = -1)
        {
            var inventory = player.GetInventory();
            var itemStack = stack > 0 ? stack : itemData.m_stack;
            return GetFreeStackSpace(inventory, itemData) >= itemStack &&
                   CanCarryItem(player, itemData, itemStack);
        }

        public static bool CanAddItem(Player player, GameObject prefab, int stack = -1)
        {
            var itemData = CreatePickupItemData(prefab, stack);
            return itemData != null && CanAddItem(player, itemData, stack);
        }

        public static int AddItem(Inventory inventory, GameObject prefab, int amount)
        {
            if (amount <= 0) return 0;

            var itemDrop = prefab.GetComponent<ItemDrop>();
            if (itemDrop == null) return 0;

            var maxStackSize = Mathf.Max(1, itemDrop.m_itemData.m_shared.m_maxStackSize);
            var sample = CreatePickupItemData(prefab, 1);
            if (sample == null) return 0;

            var before = CountExactItems(inventory, sample);
            while (amount > 0)
            {
                var stack = Mathf.Min(amount, maxStackSize);
                var itemData = CreatePickupItemData(prefab, stack);
                if (itemData == null) break;

                if (!inventory.AddItem(itemData)) break;
                amount -= stack;
            }

            return CountExactItems(inventory, sample) - before;
        }

        public static ItemDrop.ItemData CreatePickupItemData(GameObject prefab, int stack)
        {
            var itemDrop = prefab.GetComponent<ItemDrop>();
            if (itemDrop == null) return null;

            var itemData = itemDrop.m_itemData.Clone();
            itemData.m_dropPrefab = prefab;
            itemData.m_stack = stack > 0 ? stack : itemDrop.m_itemData.m_stack;
            itemData.m_worldLevel = (byte)Game.m_worldLevel;
            return itemData;
        }

        public static int GetAddableStack(Player player, GameObject prefab, int stack)
        {
            var itemData = CreatePickupItemData(prefab, stack);
            if (itemData == null) return 0;

            return Mathf.Min(stack, GetFreeStackSpace(player.GetInventory(), itemData),
                GetCarryableStack(player, itemData));
        }

        public static int GetFreeStackSpace(Inventory inventory, ItemDrop.ItemData itemData)
        {
            var maxStackSize = Mathf.Max(1, itemData.m_shared.m_maxStackSize);
            var space = inventory.GetEmptySlots() * maxStackSize;

            foreach (var item in inventory.GetAllItems())
            {
                if (item.m_shared.m_name != itemData.m_shared.m_name) continue;
                if (item.m_quality != itemData.m_quality) continue;
                if (item.m_worldLevel != itemData.m_worldLevel) continue;
                if (item.m_stack >= item.m_shared.m_maxStackSize) continue;

                space += item.m_shared.m_maxStackSize - item.m_stack;
            }

            return space;
        }

        public static bool CanCarryItem(Player player, ItemDrop.ItemData itemData, int stack)
        {
            return GetCarryableStack(player, itemData) >= stack;
        }

        public static int GetCarryableStack(Player player, ItemDrop.ItemData itemData)
        {
            var itemWeight = itemData.GetWeight(1);
            if (itemWeight <= 0f) return int.MaxValue;

            var remainingWeight = player.GetMaxCarryWeight() - player.GetInventory().GetTotalWeight();
            return Mathf.Max(0, Mathf.FloorToInt(remainingWeight / itemWeight));
        }

        public static int CountExactItems(Inventory inventory, ItemDrop.ItemData itemData)
        {
            var count = 0;
            foreach (var item in inventory.GetAllItems())
            {
                if (item.m_shared.m_name != itemData.m_shared.m_name) continue;
                if (item.m_quality != itemData.m_quality) continue;
                if (item.m_worldLevel != itemData.m_worldLevel) continue;

                count += item.m_stack;
            }

            return count;
        }

        private static void DropRemaining(Pickable pickable, GameObject prefab, ref int offset,
            int stack)
        {
            if (stack <= 0) return;

            Reflections.InvokeMethod(pickable, "Drop", prefab, offset++, stack);
        }

        private static int GetPickableAmount(Pickable pickable)
        {
            return pickable.m_dontScale
                ? pickable.m_amount
                : Mathf.Max(pickable.m_minAmountScaled,
                    Game.instance.ScaleDrops(pickable.m_itemPrefab, pickable.m_amount));
        }

        private static int RollPickableBonus(Player player, Pickable pickable)
        {
            if (pickable.m_pickRaiseSkill == Skills.SkillType.None) return 0;

            player.RaiseSkill(pickable.m_pickRaiseSkill);
            var skillFactor = player.GetSkillFactor(pickable.m_pickRaiseSkill);
            return Random.value < skillFactor * pickable.m_maxLevelBonusChance
                ? pickable.m_bonusYieldAmount
                : 0;
        }

        private static void CreatePickableEffects(Pickable pickable, int bonus)
        {
            if (bonus > 0)
            {
                if (DamageText.instance)
                    DamageText.instance.ShowText(DamageText.TextType.Bonus,
                        pickable.transform.position + Vector3.up * pickable.m_spawnOffset,
                        $"+{bonus}", player: true);
                pickable.m_bonusEffect.Create(pickable.transform.position, Quaternion.identity);
                ZLog.Log("Bonus food picked!");
            }

            var basePos = pickable.m_pickEffectAtSpawnPoint
                ? pickable.transform.position + Vector3.up * pickable.m_spawnOffset
                : pickable.transform.position;
            pickable.m_pickEffector.Create(basePos, Quaternion.identity);
        }
    }
}

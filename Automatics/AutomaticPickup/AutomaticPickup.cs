using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using ModUtils;
using UnityEngine;

namespace Automatics.AutomaticPickup
{
    [DisallowMultipleComponent]
    internal sealed class AutomaticPickup : MonoBehaviour
    {
        private Player _player;
        private float _pickupTimer;
        private bool _active;

        private void Awake()
        {
            _player = GetComponent<Player>();
            if (_player.IsOwner())
                StartCoroutine(nameof(Pickup));
        }

        private void OnDestroy()
        {
            if (_player.IsOwner())
                StopCoroutine(nameof(Pickup));
            _player = null;
        }

        [SuppressMessage("ReSharper", "IteratorNeverReturns")]
        private IEnumerator Pickup()
        {
            while (true)
            {
                while (!_active) yield return new WaitForSeconds(0.1f);
                _active = false;

                if (Config.PickupAllNearbyKey.MainKey == KeyCode.None)
                {
                    PickupAllNearby(_player, (Pickable x) => true);
                    yield return null;
                    PickupAllNearby(_player, (PickableItem x) => true);
                    yield return null;
                    PickupAllNearby(_player, (ItemDrop x) => x.m_autoPickup);
                }
                else
                {
                    var hovering = Reflections.GetField<GameObject>(_player, "m_hovering");
                    if (!hovering) continue;

                    var lastHoverInteractTime =
                        Reflections.GetField<float>(_player, "m_lastHoverInteractTime");
                    if (Time.time - lastHoverInteractTime < 0.20000000298023224) continue;
                    Reflections.SetField(_player, "m_lastHoverInteractTime", Time.time);

                    var pickable = hovering.GetComponentInParent<Pickable>();
                    if (pickable)
                    {
                        var pickableName = GetPickableName(pickable);
                        PickupAllNearby(_player, x => GetPickableName(x) == pickableName);
                        Reflections.InvokeMethod(_player, "DoInteractAnimation",
                            hovering.transform.position);
                        continue;
                    }

                    yield return null;

                    var pickableItem = hovering.GetComponentInParent<PickableItem>();
                    if (pickableItem)
                    {
                        var itemName = GetPickableItemName(pickableItem);
                        PickupAllNearby(_player, x => GetPickableItemName(x) == itemName);
                        Reflections.InvokeMethod(_player, "DoInteractAnimation",
                            hovering.transform.position);
                        continue;
                    }

                    yield return null;

                    var itemDrop = hovering.GetComponentInParent<ItemDrop>();
                    if (itemDrop)
                    {
                        var itemName = GetItemDropName(itemDrop);
                        PickupAllNearby(_player, x => GetItemDropName(x) == itemName);
                        Reflections.InvokeMethod(_player, "DoInteractAnimation",
                            hovering.transform.position);
                        continue;
                    }
                }
            }
        }

        private void Update()
        {
            if (Game.IsPaused()) return;
            if (Config.PickupAllNearbyKey.MainKey == KeyCode.None) return;
            if (_player.InAttack() || _player.InDodge()) return;
            if (!Reflections.InvokeMethod<bool>(_player, "TakeInput")) return;
            if (!Config.PickupAllNearbyKey.IsDown()) return;

            _active = true;
        }

        private void FixedUpdate()
        {
            if (Game.IsPaused()) return;
            if (Config.PickupAllNearbyKey.MainKey != KeyCode.None) return;
            if (Config.AutomaticPickupInterval <= 0) return;

            _pickupTimer += Time.deltaTime;
            if (_pickupTimer < Config.AutomaticPickupInterval) return;
            _pickupTimer = 0f;

            _active = true;
        }

        private static string GetPickableName(Pickable pickable)
        {
            return pickable.GetHoverName();
        }

        private static string GetPickableItemName(PickableItem pickableItem)
        {
            return !pickableItem.m_itemPrefab
                ? ""
                : pickableItem.m_itemPrefab.m_itemData.m_shared.m_name;
        }

        private static string GetItemDropName(ItemDrop itemDrop)
        {
            return itemDrop.m_itemData.m_shared.m_name;
        }

        private static void PickupAllNearby(Player player, Predicate<PickableItem> predicate)
        {
            var origin = player.transform.position;

            var range = Config.AutomaticPickupRange;
            foreach (var pickableItem in PickableItemCache.GetAllInstance())
            {
                if (Vector3.Distance(origin, pickableItem.transform.position) > range) continue;
                if (!predicate.Invoke(pickableItem)) continue;
                if (Objects.GetZNetView(pickableItem, out var zNetView) && zNetView.IsValid())
                    PickPickableItem(player, pickableItem, zNetView);
            }
        }

        private static void PickupAllNearby(Player player, Predicate<Pickable> predicate)
        {
            var origin = player.transform.position;

            var range = Config.AutomaticPickupRange;
            foreach (var pickable in PickableCache.GetAllInstance())
            {
                if (Vector3.Distance(origin, pickable.transform.position) > range) continue;
                if (!predicate.Invoke(pickable)) continue;

                if (pickable.m_tarPreventsPicking)
                {
                    var floating = Reflections.GetField<Floating>(pickable, "m_floating");
                    if (!floating)
                    {
                        floating = pickable.GetComponent<Floating>();
                        if (floating)
                            Reflections.SetField(pickable, "m_floating", floating);
                    }

                    if (floating && floating.IsInTar())
                        continue;
                }

                if (Objects.GetZNetView(pickable, out var zNetView) && zNetView.IsValid())
                    PickPickable(player, pickable, zNetView);
            }
        }

        private static void PickupAllNearby(Player player, Predicate<ItemDrop> predicate)
        {
            var origin = player.transform.position;

            var range = Config.AutomaticPickupRange;
            foreach (var itemDrop in GetAllItemDrop())
            {
                if (!itemDrop) continue;
                if (itemDrop.IsPiece()) continue;
                if (Vector3.Distance(origin, itemDrop.transform.position) > range) continue;
                if (!predicate.Invoke(itemDrop)) continue;
                if (itemDrop.InTar()) continue;
                if (Objects.GetZNetView(itemDrop, out var zNetView) && zNetView.GetZDO() != null)
                    PickItemDrop(player, itemDrop);
            }
        }

        // Direct inventory awards are safe only while this peer owns the
        // pickable ZDO. Non-owners delegate to vanilla owner-side pickup RPCs.
        private static void PickPickable(Player player, Pickable pickable, ZNetView zNetView)
        {
            if (pickable.GetPicked() || pickable.GetEnabled == 0) return;

            var amount = GetPickableAmount(pickable);
            if (amount <= 0) return;

            if (!zNetView.IsOwner())
            {
                if (CanAddItem(player, pickable.m_itemPrefab, amount))
                    pickable.Interact(player, false, false);
                return;
            }

            if (!CanAddItem(player, pickable.m_itemPrefab, amount)) return;

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
        }

        private static void PickPickableItem(Player player, PickableItem pickableItem,
            ZNetView zNetView)
        {
            if (Reflections.GetField<bool>(pickableItem, "m_picked")) return;

            var stackSize = Reflections.InvokeMethod<int>(pickableItem, "GetStackSize");
            if (stackSize <= 0) return;
            if (!CanAddItem(player, pickableItem.m_itemPrefab.gameObject, stackSize)) return;

            if (!zNetView.IsOwner())
            {
                pickableItem.Interact(player, false, false);
                return;
            }

            pickableItem.m_pickEffector.Create(pickableItem.transform.position, Quaternion.identity);
            var addedStack = AddItem(player.GetInventory(), pickableItem.m_itemPrefab.gameObject,
                stackSize);
            if (addedStack <= 0) return;

            Reflections.SetField(pickableItem, "m_picked", true);
            if (addedStack < stackSize)
                DropPickableItemRemainder(pickableItem, stackSize - addedStack);
            zNetView.Destroy();
        }

        private static void PickItemDrop(Player player, ItemDrop itemDrop)
        {
            if (!CanAddItem(player, itemDrop.m_itemData)) return;
            itemDrop.Pickup(player);
        }

        private static bool CanAddItem(Player player, ItemDrop.ItemData itemData, int stack = -1)
        {
            var inventory = player.GetInventory();
            var itemStack = stack > 0 ? stack : itemData.m_stack;
            return GetFreeStackSpace(inventory, itemData) >= itemStack &&
                   CanCarryItem(player, itemData, itemStack);
        }

        private static bool CanAddItem(Player player, GameObject prefab, int stack = -1)
        {
            var itemData = CreatePickupItemData(prefab, stack);
            return itemData != null && CanAddItem(player, itemData, stack);
        }

        private static int AddItem(Inventory inventory, GameObject prefab, int amount)
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

        private static ItemDrop.ItemData CreatePickupItemData(GameObject prefab, int stack)
        {
            var itemDrop = prefab.GetComponent<ItemDrop>();
            if (itemDrop == null) return null;

            var itemData = itemDrop.m_itemData.Clone();
            itemData.m_dropPrefab = prefab;
            itemData.m_stack = stack > 0 ? stack : itemDrop.m_itemData.m_stack;
            itemData.m_worldLevel = (byte)Game.m_worldLevel;
            return itemData;
        }

        private static int GetAddableStack(Player player, GameObject prefab, int stack)
        {
            var itemData = CreatePickupItemData(prefab, stack);
            if (itemData == null) return 0;

            return Mathf.Min(stack, GetFreeStackSpace(player.GetInventory(), itemData),
                GetCarryableStack(player, itemData));
        }

        private static int GetFreeStackSpace(Inventory inventory, ItemDrop.ItemData itemData)
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

        private static bool CanCarryItem(Player player, ItemDrop.ItemData itemData, int stack)
        {
            return GetCarryableStack(player, itemData) >= stack;
        }

        private static int GetCarryableStack(Player player, ItemDrop.ItemData itemData)
        {
            var itemWeight = itemData.GetWeight(1);
            if (itemWeight <= 0f) return int.MaxValue;

            var remainingWeight = player.GetMaxCarryWeight() - player.GetInventory().GetTotalWeight();
            return Mathf.Max(0, Mathf.FloorToInt(remainingWeight / itemWeight));
        }

        private static int CountExactItems(Inventory inventory, ItemDrop.ItemData itemData)
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

        private static void DropPickableItemRemainder(PickableItem pickableItem, int stack)
        {
            if (stack <= 0) return;

            var position = pickableItem.transform.position + Vector3.up * 0.2f;
            var obj = UnityEngine.Object.Instantiate(pickableItem.m_itemPrefab.gameObject, position,
                pickableItem.transform.rotation);
            var itemDrop = obj.GetComponent<ItemDrop>();
            if (itemDrop != null)
            {
                itemDrop.m_itemData.m_stack = stack;
                ItemDrop.OnCreateNew(itemDrop);
            }

            var rigidbody = obj.GetComponent<Rigidbody>();
            if (rigidbody)
                rigidbody.linearVelocity = Vector3.up * 4f;
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
            return UnityEngine.Random.value < skillFactor * pickable.m_maxLevelBonusChance
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

        private static IEnumerable<ItemDrop> GetAllItemDrop()
        {
            return Reflections.GetStaticField<ItemDrop, List<ItemDrop>>("s_instances") ??
                   Reflections.GetStaticField<ItemDrop, List<ItemDrop>>("m_instances") ??
                   Enumerable.Empty<ItemDrop>();
        }
    }
}

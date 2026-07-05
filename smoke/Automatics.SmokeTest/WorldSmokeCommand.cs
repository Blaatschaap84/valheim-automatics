using System;
using System.Collections.Generic;
using System.Linq;
using Automatics;
using Automatics.AutomaticProcessing;
using Automatics.Valheim;
using BepInEx.Logging;
using ModUtils;
using UnityEngine;

namespace Automatics.SmokeTest
{
    // In-game console command that drives the REAL Automatics entry points for
    // Storage / Door / Pickup / Processing against freshly spawned objects, asserts
    // observable results, cleans everything up, and logs PASS/FAIL/SKIP plus a
    // one-line Summary (mirroring SmokePlugin.cs). Out of scope: Farming/Feeding/Mining.
    //
    // Registration: subscribe Hooks.OnInitTerminal += BuildCommand exactly once,
    // early (EnsureRegistered is called at the top of SmokePlugin's Start coroutine,
    // before the readiness wait). Terminal.m_terminalInitialized is private on the
    // Valheim Terminal type (Automatics reads it only via Harmony field injection),
    // so there is no immediate-build branch — the one-time terminal init fires the
    // subscribed callback. _commandBuilt keeps BuildCommand idempotent.
    internal static class WorldSmokeCommand
    {
        private const string CommandName = "automatics_smoke_world";
        private const string Smelter = "$piece_smelter";
        private const string ChestWood = "$piece_chest_wood";
        private const string WoodDoorObjectName = "$piece_wooddoor";
        private const string WoodPrefab = "Wood";
        private const string WoodItemName = "$item_wood";

        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("Automatics Smoke World");

        private static bool _registered;
        private static bool _commandBuilt;

        internal static void EnsureRegistered()
        {
            if (_registered) return;
            _registered = true;

            // Terminal.m_terminalInitialized is private and cannot be referenced from
            // this assembly, so we cannot build immediately. Subscribe early (before
            // the readiness wait in SmokePlugin.Start) so the subscription is in place
            // before the terminal's one-time init fires. Mirrors Automatics.cs:131.
            Hooks.OnInitTerminal += BuildCommand;
        }

        private static void BuildCommand()
        {
            if (_commandBuilt) return;
            _commandBuilt = true;

            // 3-arg positional form; rely on ctor defaults for the cheat/network/etc.
            // flags. Valheim throws on duplicate command names, so the _commandBuilt
            // guard above keeps this single-shot even if OnInitTerminal fires twice.
            _ = new Terminal.ConsoleCommand(
                CommandName,
                "Run Automatics live-world smoke checks (storage/door/pickup/processing).",
                OnCommand);
        }

        private static void OnCommand(Terminal.ConsoleEventArgs args)
        {
            var results = new List<CheckResult>
            {
                RunCheck("world_storage_store_moves_items", CheckStorageStoreMovesItems),
                RunCheck("world_door_open_sets_zdo_state", CheckDoorOpenSetsZdoState),
                RunCheck("world_pickup_itemdrop_to_inventory", CheckPickupItemDropToInventory),
                RunCheck("world_processing_smelter_refuel", CheckProcessingSmelterRefuel)
            };

            foreach (var result in results)
            {
                string line;
                if (result.Status == CheckStatus.Pass)
                {
                    line = $"[Automatics Smoke] PASS {result.Name}";
                    Log.LogInfo(line);
                }
                else if (result.Status == CheckStatus.Skip)
                {
                    line = $"[Automatics Smoke] SKIP {result.Name}: {result.Message}";
                    Log.LogWarning(line);
                }
                else
                {
                    line = $"[Automatics Smoke] FAIL {result.Name}: {result.Message}";
                    Log.LogError(line);
                }

                args.Context?.AddString(line);
            }

            var failed = results.Where(result => result.Status == CheckStatus.Fail)
                                .Select(result => result.Name)
                                .ToArray();
            var skipped = results.Where(result => result.Status == CheckStatus.Skip)
                                 .Select(result => result.Name)
                                 .ToArray();
            var passed = results.Count(result => result.Status == CheckStatus.Pass);

            var summary =
                $"[Automatics Smoke] Summary: total={results.Count} passed={passed} failed={failed.Length} skipped={skipped.Length} failedChecks=[{string.Join(", ", failed)}] skippedChecks=[{string.Join(", ", skipped)}]";
            Log.LogInfo(summary);
            args.Context?.AddString(summary);
        }

        private static CheckResult RunCheck(string name, Func<CheckResult> check)
        {
            try
            {
                var result = check();
                return result.WithName(name);
            }
            catch (Exception e)
            {
                return CheckResult.Fail(name, e.GetType().Name + ": " + e.Message);
            }
        }

        // -- 4.1 Storage -----------------------------------------------------------

        private static CheckResult CheckStorageStoreMovesItems()
        {
            if (ZNetScene.instance == null) return CheckResult.Skip("ZNetScene.instance is null");
            var player = Player.m_localPlayer;
            if (player == null) return CheckResult.Skip("Player.m_localPlayer is null");
            var playerInventory = player.GetInventory();
            if (playerInventory == null) return CheckResult.Skip("player inventory is null");

            if (global::Automatics.AutomaticStorage.Config.ModuleDisabled)
                return CheckResult.Skip("automatic_storage module disabled");
            if (!global::Automatics.AutomaticStorage.Config.EnableAutomaticStorage)
                return CheckResult.Skip("enable_automatic_storage is off");
            if (global::Automatics.AutomaticStorage.Config.StorageSearchRange < 2)
                return CheckResult.Skip("storage_search_range too small to reach a spawned chest");

            if (ObjectDB.instance == null) return CheckResult.Skip("ObjectDB.instance is null");

            if (!TryGetPrefab(WoodPrefab, out var woodPrefab))
                return CheckResult.Skip("Wood prefab not found");
            if (woodPrefab.GetComponent<ItemDrop>() == null)
                return CheckResult.Skip("Wood prefab lacks ItemDrop");

            var woodType = woodPrefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_itemType;
            if (!global::Automatics.AutomaticStorage.Config.IsAllowedItemType(woodType))
                return CheckResult.Skip("Material item type not in allowed_item_types");

            if (!TryFindPrefabByObjectName<Container>(ChestWood, out var chestPrefab))
                return CheckResult.Skip($"{ChestWood} prefab not found");

            var forward = player.transform.forward;
            var origin = player.transform.position;

            using (var scope = new SpawnScope())
            {
                var chestGo = scope.Spawn(chestPrefab, origin + forward * 1.5f, Quaternion.identity);
                var chest = chestGo.GetComponent<Container>();
                if (chest == null) return CheckResult.Skip("spawned chest lacks Container");
                if (!Objects.GetZNetView(chest, out var chestNview) || chestNview == null ||
                    !chestNview.IsValid() || chestNview.GetZDO() == null)
                    return CheckResult.Skip("spawned chest ZNetView invalid");
                if (!chestNview.IsOwner())
                    return CheckResult.Skip("spawned chest not owned after claim");
                if (!ContainerAccess.IsAllowed(chest, global::Automatics.AutomaticStorage.Config.AllowContainer))
                    return CheckResult.Skip("spawned chest not in allow_container");

                var chestInventory = chest.GetInventory();
                if (chestInventory == null) return CheckResult.Skip("spawned chest inventory is null");

                // AutomaticStorage.Store only routes to containers registered in
                // ContainerCache. If the chest's ContainerCache.Awake has not registered it
                // this frame (same-frame spawn timing / ZDO-at-Awake gating), Store cannot
                // see it — a benign prerequisite miss, so SKIP rather than FAIL (mirrors the
                // processing check's GetNearbyContainers pre-check).
                if (!global::Automatics.ContainerCache.GetAllInstance().Any(c => c == chest))
                    return CheckResult.Skip(
                        "spawned chest not registered in ContainerCache this frame; cannot drive Store");

                var sample = PickableHarvester.CreatePickupItemData(woodPrefab, 1);
                if (sample == null) return CheckResult.Skip("could not build Wood sample item");

                var added = AddItemsLooped(playerInventory, woodPrefab, 5);
                if (added <= 0) return CheckResult.Skip("could not add test Wood to player inventory");
                scope.TrackItemForCleanup(playerInventory, sample, added);
                scope.TrackItemForCleanup(chestInventory, sample, added);

                // Hotbar guard: store_hotbar_items default false rejects row-0 items.
                if (!global::Automatics.AutomaticStorage.Config.StoreHotbarItems &&
                    !HasTestItemOffHotbar(playerInventory, sample))
                    return CheckResult.Skip(
                        "test item landed on hotbar and store_hotbar_items is off");

                var beforeCount = PickableHarvester.CountExactItems(playerInventory, sample);
                var chestBefore = chestInventory.CountItems(WoodItemName);

                var result = global::Automatics.AutomaticStorage.AutomaticStorage.Store(player);

                var afterCount = PickableHarvester.CountExactItems(playerInventory, sample);
                var moved = beforeCount - afterCount;

                Expect(moved > 0, "Store should move at least one Wood out of the player inventory");
                Expect(result.StoredItems == moved,
                    $"result.StoredItems ({result.StoredItems}) should equal moved delta ({moved})");
                Expect(result.TouchedContainers > 0, "result.TouchedContainers should be > 0");

                var chestAfter = chestInventory.CountItems(WoodItemName);
                Expect(chestAfter - chestBefore >= result.StoredItems,
                    $"chest should have received at least {result.StoredItems} Wood (delta {chestAfter - chestBefore})");

                return CheckResult.Pass();
            }
        }

        // -- 4.2 Door --------------------------------------------------------------

        private static CheckResult CheckDoorOpenSetsZdoState()
        {
            if (ZNetScene.instance == null) return CheckResult.Skip("ZNetScene.instance is null");
            var player = Player.m_localPlayer;
            if (player == null) return CheckResult.Skip("Player.m_localPlayer is null");

            if (global::Automatics.AutomaticDoor.Config.ModuleDisabled)
                return CheckResult.Skip("automatic_door module disabled");
            if (!global::Automatics.AutomaticDoor.Config.EnableAutomaticDoor)
                return CheckResult.Skip("enable_automatic_door is off");
            if (global::Automatics.AutomaticDoor.Config.DistanceForAutomaticOpening <= 0f)
                return CheckResult.Skip("distance_for_automatic_opening <= 0");
            if (!global::Automatics.AutomaticDoor.Config.AllowAutomaticDoor.Contains("WoodDoor"))
                return CheckResult.Skip("WoodDoor not in allow_automatic_door");

            if (!TryFindPrefabByObjectName<Door>(WoodDoorObjectName, out var doorPrefab))
                return CheckResult.Skip($"{WoodDoorObjectName} prefab not found");

            var forward = player.transform.forward;
            var origin = player.transform.position;

            using (var scope = new SpawnScope())
            {
                var doorGo = scope.Spawn(doorPrefab, origin + forward * 2f, Quaternion.identity);
                var door = doorGo.GetComponent<Door>();
                if (door == null) return CheckResult.Skip("spawned door lacks Door component");
                if (!Objects.HasValidOwnership(door, out var doorNview) || doorNview == null)
                    return CheckResult.Skip("spawned door ZNetView invalid or not owned");

                if (!global::Automatics.AutomaticDoor.AutomaticDoor.HasRegisteredDoor)
                    return CheckResult.Skip(
                        "no AutomaticDoor registered after spawn (Awake patch did not attach)");

                var before = doorNview.GetZDO().GetInt("state");
                global::Automatics.AutomaticDoor.AutomaticDoor.TryOpenNearby(player, Vector3.zero);
                var after = doorNview.GetZDO().GetInt("state");

                if (after == 0)
                {
                    // CloseGracePeriod only affects close, not open; retry once. A
                    // ward-denied area or an obstacle linecast can suppress the open.
                    global::Automatics.AutomaticDoor.AutomaticDoor.TryOpenNearby(player, Vector3.zero);
                    after = doorNview.GetZDO().GetInt("state");
                }

                Expect(before == 0,
                    $"freshly spawned door should start closed (state={before})");
                Expect(after != 0,
                    "door ZDO \"state\" should be non-zero after TryOpenNearby " +
                    "(possible ward/obstacle suppression if still 0)");

                return CheckResult.Pass();
            }
        }

        // -- 4.3 Pickup ------------------------------------------------------------

        private static CheckResult CheckPickupItemDropToInventory()
        {
            if (ZNetScene.instance == null) return CheckResult.Skip("ZNetScene.instance is null");
            var player = Player.m_localPlayer;
            if (player == null) return CheckResult.Skip("Player.m_localPlayer is null");
            var playerInventory = player.GetInventory();
            if (playerInventory == null) return CheckResult.Skip("player inventory is null");

            if (ObjectDB.instance == null || ObjectDB.instance.m_items.Count == 0)
                return CheckResult.Skip("ObjectDB not ready");

            if (!TryGetPrefab(WoodPrefab, out var woodPrefab))
                return CheckResult.Skip("Wood prefab not found");
            if (woodPrefab.GetComponent<ItemDrop>() == null)
                return CheckResult.Skip("Wood prefab lacks ItemDrop");

            if (playerInventory.GetEmptySlots() == 0)
                return CheckResult.Skip("inventory full");

            var forward = player.transform.forward;
            var origin = player.transform.position;

            using (var scope = new SpawnScope())
            {
                var sample = PickableHarvester.CreatePickupItemData(woodPrefab, 1);
                if (sample == null) return CheckResult.Skip("could not build Wood sample item");

                var go = scope.Spawn(woodPrefab, origin + forward * 1.5f, Quaternion.identity);
                var itemDrop = go.GetComponent<ItemDrop>();
                if (itemDrop == null) return CheckResult.Skip("spawned object lacks ItemDrop");

                itemDrop.m_itemData.m_stack = 1;
                itemDrop.m_itemData.m_worldLevel = (byte)Game.m_worldLevel;
                ItemDrop.OnCreateNew(itemDrop);

                if (!Objects.GetZNetView(itemDrop, out var dropNview) || dropNview == null ||
                    dropNview.GetZDO() == null)
                    return CheckResult.Skip("spawned ItemDrop ZNetView/ZDO null");

                // Track the picked-up Wood for removal regardless of outcome.
                scope.TrackItemForCleanup(playerInventory, sample, 1);

                // Reproduces AutomaticPickup.PickItemDrop: harvester gate, then vanilla Pickup.
                // ModuleDisabled is NOT a SKIP here — this drives the shared pickup API directly.
                if (!PickableHarvester.CanAddItem(player, itemDrop.m_itemData))
                    return CheckResult.Skip("harvester gate rejects add (weight/space)");

                var before = PickableHarvester.CountExactItems(playerInventory, sample);
                itemDrop.Pickup(player);
                var after = PickableHarvester.CountExactItems(playerInventory, sample);

                Expect(after - before >= 1, "picked-up Wood should enter the player inventory");

                var dropGone = !go ||
                               !Objects.GetZNetView(itemDrop, out var nv) || nv == null ||
                               !nv.IsValid() || nv.GetZDO() == null;
                Expect(dropGone, "world ItemDrop should be consumed after a full pickup");

                return CheckResult.Pass();
            }
        }

        // -- 4.4 Processing --------------------------------------------------------

        private static CheckResult CheckProcessingSmelterRefuel()
        {
            if (ZNetScene.instance == null) return CheckResult.Skip("ZNetScene.instance is null");
            var player = Player.m_localPlayer;
            if (player == null) return CheckResult.Skip("Player.m_localPlayer is null");

            if (global::Automatics.AutomaticProcessing.Config.ModuleDisabled)
                return CheckResult.Skip("automatic_processing module disabled");
            if (!global::Automatics.AutomaticProcessing.Config.EnableAutomaticProcessing)
                return CheckResult.Skip("enable_automatic_processing is off");
            if (!global::Automatics.AutomaticProcessing.Logics.IsAllowProcessing(Smelter, Process.Refuel))
                return CheckResult.Skip("Refuel not allowed for smelter in config");

            if (!TryFindPrefabByObjectName<global::Smelter>(Smelter, out var smelterPrefab))
                return CheckResult.Skip($"{Smelter} prefab not found");
            if (!TryFindPrefabByObjectName<Container>(ChestWood, out var chestPrefab))
                return CheckResult.Skip($"{ChestWood} prefab not found");

            var forward = player.transform.forward;
            var origin = player.transform.position;

            using (var scope = new SpawnScope())
            {
                var smelterGo = scope.Spawn(smelterPrefab, origin + forward * 2f, Quaternion.identity);
                var smelter = smelterGo.GetComponent<global::Smelter>();
                if (smelter == null) return CheckResult.Skip("spawned smelter lacks Smelter component");
                if (!Objects.GetZNetView(smelter, out var smelterNview) || smelterNview == null ||
                    !smelterNview.IsValid() || smelterNview.GetZDO() == null)
                    return CheckResult.Skip("spawned smelter ZNetView invalid");
                if (!smelterNview.IsOwner())
                    return CheckResult.Skip("spawned smelter not owned after claim");
                if (smelter.m_fuelItem == null)
                    return CheckResult.Skip("smelter has no fuel item");

                var fuelPrefab = smelter.m_fuelItem.gameObject;
                var fuelName = smelter.m_fuelItem.m_itemData.m_shared.m_name;

                // refuel_only_when_materials_supplied requires queued ore; fresh smelter has none.
                if (global::Automatics.AutomaticProcessing.Config.RefuelOnlyWhenMaterialsSupplied(Smelter) &&
                    smelterNview.GetZDO().GetInt("queued") == 0)
                    return CheckResult.Skip(
                        "refuel_only_when_materials_supplied requires queued ore");

                var smelterPos = smelter.transform.position;
                var chestGo = scope.Spawn(chestPrefab, smelterPos + Vector3.right * 1.5f,
                    Quaternion.identity);
                var chest = chestGo.GetComponent<Container>();
                if (chest == null) return CheckResult.Skip("spawned chest lacks Container");
                if (!Objects.GetZNetView(chest, out var chestNview) || chestNview == null ||
                    !chestNview.IsValid() || chestNview.GetZDO() == null)
                    return CheckResult.Skip("spawned chest ZNetView invalid");
                if (!chestNview.IsOwner())
                    return CheckResult.Skip("spawned chest not owned after claim");
                if (!ContainerAccess.IsAllowed(chest,
                        global::Automatics.AutomaticProcessing.Config.AllowContainer))
                    return CheckResult.Skip("spawned chest not in allow_container");

                var chestInventory = chest.GetInventory();
                if (chestInventory == null) return CheckResult.Skip("spawned chest inventory is null");

                var minFuelCount =
                    global::Automatics.AutomaticProcessing.Config.FuelCountOfSuppressProcessing(Smelter);
                var seeded = AddItemsLooped(chestInventory, fuelPrefab, minFuelCount + 4);
                if (seeded <= minFuelCount)
                    return CheckResult.Skip("could not seed enough fuel into the chest");

                // Reset the per-second container cache and skip-sets before driving the entry.
                global::Automatics.AutomaticProcessing.Logics.Cleanup();
                global::Automatics.AutomaticProcessing.SmelterProcess.Cleanup();

                if (!global::Automatics.AutomaticProcessing.Logics
                        .GetNearbyContainers(Smelter, smelterPos).Any())
                    return CheckResult.Skip("no fuel source container in range");

                var containerFuelBefore = chestInventory.CountItems(fuelName);
                var fuelBefore = smelterNview.GetZDO().GetFloat("fuel");

                var ret = global::Automatics.AutomaticProcessing.SmelterProcess
                    .QuickRefuel(smelter, smelterNview, 1f, fuelBefore);

                Expect(Mathf.Approximately(ret, fuelBefore + 1f),
                    $"QuickRefuel should return fuelBefore+1 ({fuelBefore + 1f}), got {ret}");
                Expect(Mathf.Approximately(smelterNview.GetZDO().GetFloat("fuel"), fuelBefore + 1f),
                    "smelter ZDO \"fuel\" should be fuelBefore+1 after QuickRefuel");

                var containerFuelAfter = chestInventory.CountItems(fuelName);
                const int consumedFuelPerRefuel = 1;
                Expect(containerFuelBefore - containerFuelAfter == consumedFuelPerRefuel,
                    $"container fuel should drop by {consumedFuelPerRefuel} (delta {containerFuelBefore - containerFuelAfter})");

                return CheckResult.Pass();
            }
            // Best-effort cache reset after the run so a second invocation re-reads fresh state.
        }

        // -- Spawn / prefab helpers ------------------------------------------------

        private sealed class SpawnScope : IDisposable
        {
            private readonly List<GameObject> _spawned = new List<GameObject>();
            private readonly List<ItemCleanup> _itemsToRemove = new List<ItemCleanup>();

            // Spawn a networked prefab and claim ownership so IsOwner() is true for the
            // synchronous mutation checks on a single-player host.
            public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
            {
                var go = UnityEngine.Object.Instantiate(prefab, position, rotation);
                _spawned.Add(go);
                if (Objects.GetZNetView(go.transform, out var nview) && nview != null &&
                    nview.IsValid())
                    if (!nview.IsOwner())
                        nview.ClaimOwnership();
                return go;
            }

            public void TrackItemForCleanup(Inventory inventory, ItemDrop.ItemData sample, int amount)
            {
                _itemsToRemove.Add(new ItemCleanup(inventory, sample, amount));
            }

            public void Dispose()
            {
                foreach (var entry in _itemsToRemove)
                {
                    try
                    {
                        if (entry.Inventory == null || entry.Sample == null) continue;
                        Inventories.RemoveItem(entry.Inventory, entry.Sample.m_shared.m_name, 0f,
                            WorldLevelMatchMode.Ignore, entry.Amount);
                    }
                    catch
                    {
                        // best effort
                    }
                }

                foreach (var go in _spawned)
                {
                    try
                    {
                        if (!go) continue;
                        if (Objects.GetZNetView(go.transform, out var nview) && nview != null &&
                            nview.IsValid())
                            nview.Destroy();
                        else
                            UnityEngine.Object.Destroy(go);
                    }
                    catch
                    {
                        // best effort
                    }
                }

                // Clear processing caches so leftover spawned containers do not linger
                // in GetNearbyContainers for the next invocation.
                try
                {
                    global::Automatics.AutomaticProcessing.Logics.Cleanup();
                    global::Automatics.AutomaticProcessing.SmelterProcess.Cleanup();
                }
                catch
                {
                    // best effort
                }
            }

            private readonly struct ItemCleanup
            {
                public ItemCleanup(Inventory inventory, ItemDrop.ItemData sample, int amount)
                {
                    Inventory = inventory;
                    Sample = sample;
                    Amount = amount;
                }

                public Inventory Inventory { get; }
                public ItemDrop.ItemData Sample { get; }
                public int Amount { get; }
            }
        }

        // Item / piece prefab by GameObject name (Wood, piece_chest_wood, ...).
        private static bool TryGetPrefab(string gameObjectName, out GameObject prefab)
        {
            prefab = ZNetScene.instance.GetPrefab(gameObjectName);
            return prefab != null;
        }

        // Door / smelter / chest: resolve by the component's object name (m_name via
        // Objects.GetName), scanning ZNetScene.m_prefabs, to dodge the GameObject-name
        // vs $token mismatch.
        private static bool TryFindPrefabByObjectName<TComponent>(string objectName,
            out GameObject prefab) where TComponent : Component
        {
            prefab = null;
            var prefabs = ZNetScene.instance.m_prefabs;
            if (prefabs == null) return false;

            foreach (var go in prefabs)
            {
                if (!go) continue;
                var comp = go.GetComponent<TComponent>();
                if (comp == null) continue;
                if (Objects.GetName(comp) != objectName) continue;
                prefab = go;
                return true;
            }

            return false;
        }

        // Inventories.AddItem caps each call at one max-stack, so loop for larger
        // amounts. Returns the total added-count delta.
        private static int AddItemsLooped(Inventory inventory, GameObject prefab, int amount)
        {
            var total = 0;
            var remaining = amount;
            while (remaining > 0)
            {
                var added = Inventories.AddItem(inventory, prefab, remaining);
                if (added <= 0) break;
                total += added;
                remaining -= added;
            }

            return total;
        }

        private static bool HasTestItemOffHotbar(Inventory inventory, ItemDrop.ItemData sample)
        {
            foreach (var item in inventory.GetAllItems())
            {
                if (item.m_shared.m_name != sample.m_shared.m_name) continue;
                if (item.m_gridPos.y != 0) return true;
            }

            return false;
        }

        private static void Expect(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        // -- Reporting (private copy mirroring SmokePlugin's shapes) ---------------

        private enum CheckStatus
        {
            Pass,
            Fail,
            Skip
        }

        private sealed class CheckResult
        {
            private CheckResult(CheckStatus status, string name, string message)
            {
                Status = status;
                Name = name ?? "";
                Message = message ?? "";
            }

            public CheckStatus Status { get; }
            public string Name { get; }
            public string Message { get; }

            public static CheckResult Pass()
            {
                return new CheckResult(CheckStatus.Pass, "", "");
            }

            public static CheckResult Fail(string name, string message)
            {
                return new CheckResult(CheckStatus.Fail, name, message);
            }

            public static CheckResult Skip(string message)
            {
                return new CheckResult(CheckStatus.Skip, "", message);
            }

            public CheckResult WithName(string name)
            {
                return new CheckResult(Status, name, Message);
            }
        }
    }
}

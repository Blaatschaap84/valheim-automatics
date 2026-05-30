using System.Collections.Generic;
using Automatics.Valheim;
using ModUtils;
using UnityEngine;

namespace Automatics.AutomaticFarming
{
    internal static class AutomaticFarming
    {
        // Per-axis cap on the proactive-sow grid (MaxSowStepsPerAxis^2 tiles per
        // crop per pass). Keeps a large farming_range from turning each sow pass
        // into a multi-thousand-tile physics sweep.
        private const int MaxSowStepsPerAxis = 64;

        public static void TryFarming(Player player)
        {
            var origin = player.transform.position;
            var range = Config.FarmingRange;

            var crops = new List<(Pickable pickable, string identifier, float distance)>();
            foreach (var pickable in PickableCache.GetAllInstance())
            {
                if (!pickable) continue;
                if (pickable.GetPicked() || pickable.GetEnabled == 0) continue;

                var distance = Vector3.Distance(origin, pickable.transform.position);
                if (distance > range) continue;
                if (!TryGetCropIdentifier(pickable, out var identifier)) continue;

                crops.Add((pickable, identifier, distance));
            }

            crops.Sort((a, b) => a.distance.CompareTo(b.distance));

            foreach (var (pickable, identifier, _) in crops)
            {
                if (!pickable) continue;
                HarvestAndReplant(player, pickable, identifier);
            }

            if (Config.EnableProactiveSowing)
                Sow(player);
        }

        /// <summary>
        /// True when Automatic Farming owns the harvest of <paramref name="pickable"/>
        /// (module enabled, feature on, and the crop is allowlisted). Automatic Pickup
        /// queries this to skip crops that farming will harvest and replant. The
        /// module-disabled short-circuit keeps it safe even before this module's
        /// configuration is bound.
        /// </summary>
        public static bool ClaimsCrop(Pickable pickable)
        {
            if (!pickable) return false;
            if (Config.ModuleDisabled) return false;
            if (!Config.EnableAutomaticFarming) return false;

            return TryGetCropIdentifier(pickable, out _);
        }

        private static void HarvestAndReplant(Player player, Pickable pickable, string identifier)
        {
            if (!Objects.GetZNetView(pickable, out var zNetView) || !zNetView.IsValid()) return;

            // Only harvest crops this peer owns, so farming never picks a crop it
            // cannot also replant. In single-player every nearby crop is locally
            // owned, so this is a no-op there; it only prevents a destructive
            // delegated harvest of another peer's crop in multiplayer.
            if (!zNetView.IsOwner()) return;

            var position = pickable.transform.position;
            var rotation = pickable.transform.rotation;

            var outcome = PickableHarvester.Harvest(player, pickable, zNetView);
            if (outcome != PickableHarvester.HarvestOutcome.PickedByOwner)
            {
                // The crop was not picked (e.g. the inventory is full or over
                // weight), so there is nothing to replant. Non-owned crops were
                // already skipped by the ownership guard above.
                return;
            }

            Replant(player, identifier, position, rotation);
        }

        private static void Replant(Player player, string identifier, Vector3 pos, Quaternion rot)
        {
            if (!CropPlanting.TryResolveSapling(identifier, out var sapling))
            {
                Automatics.Logger.Debug(() =>
                    $"Farming replant skipped [{identifier}]: no sapling regrows this crop");
                return;
            }

            if (CropPlanting.TryPlant(player, sapling, pos, rot, Config.SeedReserve, out var reason))
                Automatics.Logger.Debug(() => $"Farming replanted [{identifier}] at {pos}");
            else
                Automatics.Logger.Debug(() => $"Farming replant skipped [{identifier}]: {reason}");
        }

        private static bool TryGetCropIdentifier(Pickable pickable, out string identifier)
        {
            var name = Objects.GetName(pickable);
            return ValheimObject.Flora.GetIdentify(name, out identifier) &&
                   Config.AllowFarmingCrops.Contains(identifier);
        }

        // Sows allowlisted seeds (kept above the reserve) into nearby empty
        // cultivated ground on a per-sapling spacing grid. Only ground that is
        // already cultivated is targeted; terrain is never modified.
        private static void Sow(Player player)
        {
            var origin = player.transform.position;
            var range = Config.FarmingRange;
            var factor = Config.SowingSpacingFactor;
            var reserve = Config.SeedReserve;

            foreach (var identifier in Config.AllowFarmingCrops)
            {
                if (!CropPlanting.TryResolveSapling(identifier, out var sapling)) continue;

                var plant = sapling.GetComponent<Plant>();
                if (plant == null) continue;

                // Attach/vine crops are never plantable by this automation (see
                // CropPlanting.CanPlantAt), so skip the whole grid instead of
                // rejecting every cell.
                if (plant.m_attachDistance > 0f) continue;
                if (!CropPlanting.CanAffordPlanting(player, sapling, reserve)) continue;

                var step = plant.m_growRadius * factor;
                if (step < 0.1f) continue;

                // Bound the grid to a tile budget. Rather than skip the crop
                // entirely (which would silently disable sowing at a large
                // farming_range), shrink the sown radius to the budget so sowing
                // still works in the area nearest the player.
                float sowRange = range;
                if (Mathf.CeilToInt(sowRange * 2f / step) + 1 > MaxSowStepsPerAxis)
                    sowRange = step * (MaxSowStepsPerAxis - 1) / 2f;

                var sown = SowCrop(player, sapling, plant, origin, sowRange, step, reserve);
                if (sown > 0)
                {
                    var cropId = identifier;
                    Automatics.Logger.Debug(() => $"Farming sowed {sown} [{cropId}]");
                }
            }
        }

        private static int SowCrop(Player player, Piece sapling, Plant plant, Vector3 origin,
            float range, float step, int reserve)
        {
            var sown = 0;
            var rangeSqr = range * range;
            var maxX = origin.x + range;
            var maxZ = origin.z + range;
            var startX = Mathf.Ceil((origin.x - range) / step) * step;
            var startZ = Mathf.Ceil((origin.z - range) / step) * step;
            var zone = ZoneSystem.instance;

            for (var x = startX; x <= maxX; x += step)
            for (var z = startZ; z <= maxZ; z += step)
            {
                // Cheapest checks first: a horizontal squared-distance disc cull,
                // then cultivated ground, then the seed reserve, before the
                // physics-heavy plantability check in TryPlant.
                var dx = x - origin.x;
                var dz = z - origin.z;
                if (dx * dx + dz * dz > rangeSqr) continue;

                var pos = new Vector3(x, origin.y, z);
                if (zone != null) pos.y = zone.GetGroundHeight(pos);
                if (!CropPlanting.IsCultivated(pos)) continue;
                if (!CropPlanting.CanAffordPlanting(player, sapling, reserve)) return sown;

                if (CropPlanting.TryPlant(player, sapling, plant, pos, Quaternion.identity, reserve,
                        out _))
                    sown++;
            }

            return sown;
        }
    }
}

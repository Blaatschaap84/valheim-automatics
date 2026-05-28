using System.Collections.Generic;
using ModUtils;
using UnityEngine;

namespace Automatics.Valheim
{
    internal static class ContainerAccess
    {
        public static bool IsAllowed(Container container, ICollection<string> allowedIdentifiers)
        {
            return container != null &&
                   allowedIdentifiers != null &&
                   ValheimObject.Container.GetIdentify(Objects.GetName(container), out var identifier) &&
                   allowedIdentifiers.Contains(identifier);
        }

        public static bool CanUseContainer(Player player, Container container)
        {
            if (player == null || container == null) return false;
            if (!Objects.GetZNetView(container, out var nview)) return false;
            if (nview == null || !nview.IsValid() || nview.GetZDO() == null) return false;
            if (container.GetInventory() == null) return false;
            if (IsInUse(container, nview)) return false;
            if ((bool)container.m_wagon && container.m_wagon.InUse()) return false;
            if (container.m_checkGuardStone &&
                !PrivateArea.CheckAccess(container.transform.position, 0f, false))
                return false;

            return HasContainerAccess(player, container);
        }

        public static bool CanDirectlyMutateContainer(Player player, Container container)
        {
            return CanUseContainer(player, container) &&
                   Objects.GetZNetView(container, out var nview) &&
                   nview != null &&
                   nview.IsValid() &&
                   nview.GetZDO() != null &&
                   nview.IsOwner();
        }

        // Container.CheckAccess is private, but it is the vanilla ownership
        // rule for public/private/group containers.
        public static bool HasContainerAccess(Player player, Container container)
        {
            if (player == null || container == null) return false;
            if (Game.instance == null) return false;

            var profile = Game.instance.GetPlayerProfile();
            if (profile == null) return false;

            return Reflections.InvokeMethod<bool>(container, "CheckAccess",
                profile.GetPlayerID());
        }

        public static bool IsInUse(Container container)
        {
            if (container == null) return false;
            if (!Objects.GetZNetView(container, out var nview))
                return container.IsInUse();

            return IsInUse(container, nview);
        }

        public static bool TryClaimContainer(Container container)
        {
            if (container == null) return false;
            if (!Objects.GetZNetView(container, out var nview)) return false;
            if (nview == null || !nview.IsValid() || nview.GetZDO() == null) return false;
            if (!nview.IsOwner()) nview.ClaimOwnership();
            return true;
        }

        public static bool TryPrepareOwnedContainerMutation(Container container)
        {
            if (container == null) return false;
            if (!Objects.GetZNetView(container, out var nview)) return false;
            if (nview == null || !nview.IsValid() || nview.GetZDO() == null) return false;
            if (!nview.IsOwner()) return false;
            if (IsInUse(container, nview)) return false;
            if ((bool)container.m_wagon && container.m_wagon.InUse()) return false;
            return true;
        }

        private static bool IsInUse(Container container, ZNetView nview)
        {
            if (container == null) return false;
            if (nview == null || !nview.IsValid() || nview.GetZDO() == null)
                return container.IsInUse();

            return nview.IsOwner()
                ? container.IsInUse()
                : nview.GetZDO().GetInt(ZDOVars.s_inUse) == 1;
        }
    }
}

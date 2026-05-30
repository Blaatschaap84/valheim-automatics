using System.Collections.Generic;
using HarmonyLib;
using ModUtils;
using UnityEngine;

namespace Automatics.AutomaticFarming
{
    internal static class Module
    {
        private static readonly Dictionary<long, float> FarmingTimers;

        static Module()
        {
            FarmingTimers = new Dictionary<long, float>();
        }

        private static void Cleanup()
        {
            FarmingTimers.Clear();
        }

        [AutomaticsInitializer(9)]
        private static void Initialize()
        {
            Config.Initialize();
            if (Config.ModuleDisabled) return;

            Hooks.OnPlayerAwake += (player, zNetView) => { Cleanup(); };
            Hooks.OnPlayerUpdate += OnPlayerUpdate;
            Hooks.OnPlayerFixedUpdate += OnPlayerFixedUpdate;
            Harmony.CreateAndPatchAll(typeof(Patches),
                Automatics.GetHarmonyId("automatic-farming"));
        }

        private static void OnPlayerUpdate(Player player, bool takeInput)
        {
            if (!Config.EnableAutomaticFarming) return;
            if (Player.m_localPlayer != player || !player.IsOwner()) return;
            if (!takeInput) return;

            // Container designation is independent of the farming-key/interval
            // path, so it is handled before those guards: a player can tag chests
            // whether farming runs on a key or on an interval.
            TryDesignateContainer(player);

            if (Config.FarmingInterval < 0.1f) return;
            if (Config.FarmingKey.MainKey == KeyCode.None) return;

            if (Config.FarmingKey.IsDown())
                AutomaticFarming.TryFarming(player);
        }

        // Cycles the role of the container the player is looking at. Doing nothing
        // when no container is hovered keeps the key harmless elsewhere; a hovered
        // but inaccessible container reports a clear failure instead of silently
        // mutating or claiming a chest a peer owns.
        private static void TryDesignateContainer(Player player)
        {
            if (Config.DesignateContainerKey.MainKey == KeyCode.None) return;
            if (!Config.DesignateContainerKey.IsDown()) return;

            var hovering = Reflections.GetField<GameObject>(player, "m_hovering");
            if (!hovering) return;

            var container = hovering.GetComponentInParent<Container>();
            if (!container) return;

            if (ContainerRoles.TryCycleRole(player, container, out var role))
            {
                var roleLabel = Automatics.L10N.Localize(ContainerRoles.RoleLabelKey(role));
                player.Message(MessageHud.MessageType.Center,
                    Automatics.L10N.Localize("@message_automatic_farming_container_role_changed",
                        roleLabel));
            }
            else
            {
                player.Message(MessageHud.MessageType.Center,
                    Automatics.L10N.Localize("@message_automatic_farming_container_designate_failed"));
            }
        }

        private static void OnPlayerFixedUpdate(Player player, float delta)
        {
            if (!Config.EnableAutomaticFarming) return;
            if (Player.m_localPlayer != player || !player.IsOwner()) return;
            if (Config.FarmingInterval < 0.1f) return;
            if (Config.FarmingKey.MainKey != KeyCode.None) return;

            var id = player.GetPlayerID();
            if (!FarmingTimers.TryGetValue(id, out var timer))
                timer = 0f;

            timer += delta;
            if (timer >= Config.FarmingInterval)
            {
                FarmingTimers[id] = 0f;
                AutomaticFarming.TryFarming(player);
            }
            else
            {
                FarmingTimers[id] = timer;
            }
        }
    }
}

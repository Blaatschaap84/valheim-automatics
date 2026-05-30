using System.Collections.Generic;
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
        }

        private static void OnPlayerUpdate(Player player, bool takeInput)
        {
            if (!Config.EnableAutomaticFarming) return;
            if (Player.m_localPlayer != player || !player.IsOwner()) return;
            if (!takeInput) return;
            if (Config.FarmingInterval < 0.1f) return;
            if (Config.FarmingKey.MainKey == KeyCode.None) return;

            if (Config.FarmingKey.IsDown())
                AutomaticFarming.TryFarming(player);
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

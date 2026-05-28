using UnityEngine;

namespace Automatics.AutomaticStorage
{
    internal static class Module
    {
        [AutomaticsInitializer(7)]
        private static void Initialize()
        {
            Config.Initialize();
            if (Config.ModuleDisabled) return;

            Hooks.OnPlayerUpdate += OnPlayerUpdate;
        }

        private static void OnPlayerUpdate(Player player, bool takeInput)
        {
            if (!Config.EnableAutomaticStorage) return;
            if (Player.m_localPlayer != player || !player.IsOwner()) return;
            if (!takeInput) return;
            if (Game.IsPaused()) return;
            if (player.InAttack() || player.InDodge()) return;
            if (Config.StoreItemsKey.MainKey == KeyCode.None) return;
            if (!Config.StoreItemsKey.IsDown()) return;

            var result = AutomaticStorage.Store(player);
            AutomaticStorage.ShowResultMessage(player, result);
        }
    }
}

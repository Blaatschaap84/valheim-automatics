using System;
using System.Linq;
using System.Reflection;

namespace Automatics
{
    internal static class ConfigurationManagerBridge
    {
        public static void Refresh()
        {
            ClearDrawerCache();
            RebuildSettingList();
        }

        private static void ClearDrawerCache()
        {
            try
            {
                // The original BepInEx Configuration Manager exposes this as a
                // static method. shudnal's compatible-but-different manager has
                // only an instance method, so do not probe it through AccessTools:
                // AccessTools logs a warning for that expected absence.
                var type = FindLoadedType("ConfigurationManager.SettingFieldDrawer");
                var clearCache = type?.GetMethod("ClearCache",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                clearCache?.Invoke(null, Array.Empty<object>());
            }
            catch (Exception e)
            {
                Automatics.Logger?.Debug($"Failed to clear ConfigurationManager cache\n{e}");
            }
        }

        private static void RebuildSettingList()
        {
            try
            {
                var type = FindLoadedType("ConfigurationManager.ConfigurationManager");
                if (type == null) return;

                // The legacy manager provides a static Instance property. The
                // shudnal manager intentionally does not, and refreshes its own
                // list, so simply leave it alone.
                var instance = type.GetProperty("Instance",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    ?.GetValue(null, null);
                type.GetMethod("BuildSettingList",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.Invoke(instance,
                    Array.Empty<object>());
            }
            catch (Exception e)
            {
                Automatics.Logger?.Debug(
                    $"Failed to rebuild ConfigurationManager setting list\n{e}");
            }
        }

        private static Type FindLoadedType(string fullName)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(fullName, false))
                .FirstOrDefault(type => type != null);
        }
    }
}

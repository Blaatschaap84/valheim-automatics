using System;
using System.IO;
using System.Linq;
using BepInEx.Configuration;

namespace Automatics
{
    internal static class Program
    {
        private static int _failures;

        private static int Main()
        {
            Run("regex migrations preserve captured processing suffixes", RegexMigrationsPreserveCapturedProcessingSuffixes);
            Run("old automatic_repair section keeps disabled repair", OldAutomaticRepairSectionKeepsDisabledRepair);
            Run("misplaced automatic_feeding repair key remains compatible", MisplacedAutomaticFeedingRepairKeyRemainsCompatible);
            Run("door migration appends PieceHexagonalDoor", DoorMigrationAppendsPieceHexagonalDoor);

            return _failures == 0 ? 0 : 1;
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                Console.WriteLine($"ok: {name}");
            }
            catch (Exception e)
            {
                _failures++;
                Console.Error.WriteLine($"FAIL: {name}");
                Console.Error.WriteLine(e.Message);
            }
        }

        private static string Migrate(params string[] lines)
        {
            var directory = Path.Combine(Path.GetTempPath(), "automatics-config-migration-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);

            var path = Path.Combine(directory, "Automatics.cfg");
            File.WriteAllLines(path, lines);

            try
            {
                ConfigMigration.Migration(new ConfigFile(path));
                return File.ReadAllText(path);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        private static void RegexMigrationsPreserveCapturedProcessingSuffixes()
        {
            var migrated = Migrate(
                "# Automatics v1.2.0",
                "[automatic_processing]",
                "smelter_allow_automatic_processing = All",
                "blast_furnace_container_search_range = 42",
                "charcoal_kiln_material_count_that_suppress_automatic_process = 10",
                "smelter_fuel_count_that_suppress_automatic_process = 20");

            AssertContains(migrated, "allow_processing_by_smelter = Craft, Refuel, Store");
            AssertContains(migrated, "container_search_range_by_blast_furnace = 42");
            AssertContains(migrated, "charcoal_kiln_material_count_of_suppress_processing = 10");
            AssertContains(migrated, "smelter_fuel_count_of_suppress_processing = 20");
            AssertDoesNotContain(migrated, "r/^");
            AssertDoesNotContain(migrated, "$1");
        }

        private static void OldAutomaticRepairSectionKeepsDisabledRepair()
        {
            var migrated = Migrate(
                "# Automatics v1.2.0",
                "[automatic_repair]",
                "automatic_repair_enabled = false");

            AssertContains(migrated, "[automatic_repair]");
            AssertContains(migrated, "enable_automatic_repair = false");
            AssertDoesNotContain(migrated, "automatic_repair_enabled = false");
        }

        private static void MisplacedAutomaticFeedingRepairKeyRemainsCompatible()
        {
            var migrated = Migrate(
                "# Automatics v1.2.0",
                "[automatic_feeding]",
                "automatic_repair_enabled = false");

            AssertContains(migrated, "[automatic_feeding]");
            AssertContains(migrated, "enable_automatic_repair = false");
            AssertDoesNotContain(migrated, "automatic_repair_enabled = false");
        }

        private static void DoorMigrationAppendsPieceHexagonalDoor()
        {
            var migrated = Migrate(
                "# Automatics v1.5.1",
                "[automatic_door]",
                "allow_automatic_door = WoodDoor, AshwoodDoor");

            var valueLine = migrated
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Single(line => line.StartsWith("allow_automatic_door = ", StringComparison.Ordinal));

            AssertContains(valueLine, "WoodDoor");
            AssertContains(valueLine, "AshwoodDoor");
            AssertContains(valueLine, "PieceHexagonalDoor");
            AssertEqual(1, CountOccurrences(valueLine, "AshwoodDoor"), "AshwoodDoor should not be duplicated.");
            AssertEqual(1, CountOccurrences(valueLine, "PieceHexagonalDoor"), "PieceHexagonalDoor should be appended once.");
        }

        private static int CountOccurrences(string value, string needle)
        {
            var count = 0;
            var index = 0;
            while ((index = value.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }

            return count;
        }

        private static void AssertContains(string value, string expected)
        {
            if (!value.Contains(expected))
                throw new Exception($"Expected to find '{expected}' in:\n{value}");
        }

        private static void AssertDoesNotContain(string value, string unexpected)
        {
            if (value.Contains(unexpected))
                throw new Exception($"Did not expect to find '{unexpected}' in:\n{value}");
        }

        private static void AssertEqual(int expected, int actual, string message)
        {
            if (expected != actual)
                throw new Exception($"{message} Expected {expected}, got {actual}.");
        }
    }
}

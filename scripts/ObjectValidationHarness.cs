using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Automatics.AutomaticMapping;
using Automatics.Valheim;

internal static class ObjectValidationHarness
{
    private static int _failures;

    public static int Main()
    {
        TestObjectJsonFileIsolationAndDuplicateWarnings();
        TestCustomEntriesFilterInvalidDefinitions();
        TestObjectMatcherInvalidRegexIsNoMatch();
        TestIconRegexValidationAndRuntimeFallback();
        TestFirstCustomIconNameTagBoundary();

        if (_failures != 0)
            return 1;

        Console.WriteLine("PASS object validation harness");
        return 0;
    }

    private static void TestObjectJsonFileIsolationAndDuplicateWarnings()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"automatics-object-harness-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        File.WriteAllText(
            Path.Combine(directory, "invalid.json"),
            "{\"type\":\"flora\",\"values\":[null]}");
        File.WriteAllText(
            Path.Combine(directory, "valid.json"),
            "{\"type\":\"flora\",\"values\":[{\"identifier\":\"GoodFlora\",\"label\":\"Good Flora\",\"matches\":[{\"value\":\"GoodPrefab\"}]}]}");
        File.WriteAllText(
            Path.Combine(directory, "duplicate.json"),
            "{\"type\":\"flora\",\"values\":[{\"identifier\":\"DupA\",\"label\":\"Dup A\",\"matches\":[{\"value\":\"DuplicatePrefab\"}]},{\"identifier\":\"DupB\",\"label\":\"Dup B\",\"matches\":[{\"value\":\"DuplicatePrefab\"}]}]}");

        try
        {
            Automatics.Automatics.Logger.Clear();
            ValheimObject.Initialize(new[] { directory });

            ExpectTrue(
                "valid object file still registers when a sibling file is invalid",
                ValheimObject.Flora.GetIdentify("GoodPrefab", out var identifier) &&
                identifier == "GoodFlora");
            ExpectLogContains("invalid object file warning", "Invalid object data skipped");
            ExpectLogContains("duplicate exact matcher warning", "Duplicate exact matcher `DuplicatePrefab`");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
            ValheimObject.PostInitialize();
        }
    }

    private static void TestCustomEntriesFilterInvalidDefinitions()
    {
        var custom = new ValheimObject("custom-harness");
        Automatics.Automatics.Logger.Clear();

        custom.RegisterCustom(new ObjectElement[]
        {
            null,
            new ObjectElement
            {
                identifier = "BadRegex",
                label = "Bad Regex",
                matches = new List<ObjectMatcher>
                {
                    new ObjectMatcher { regex = true, value = "[" }
                }
            },
            new ObjectElement
            {
                identifier = "GoodCustom",
                label = "Good Custom",
                matches = new List<ObjectMatcher>
                {
                    new ObjectMatcher { value = "GoodCustomPrefab" }
                }
            }
        });

        ExpectTrue(
            "valid custom entry remains registered when invalid entries are skipped",
            custom.GetIdentify("GoodCustomPrefab", out var identifier) &&
            identifier == "GoodCustom");
        ExpectLogContains("null custom entry warning", "null element");
        ExpectLogContains("invalid custom regex warning", "invalid regex matcher");
    }

    private static void TestObjectMatcherInvalidRegexIsNoMatch()
    {
        var matcher = new ObjectMatcher { regex = true, value = "[" };
        ExpectFalse("invalid object regex is a runtime no-match", matcher.Matches("anything"));
    }

    private static void TestIconRegexValidationAndRuntimeFallback()
    {
        var validateRegexTarget = typeof(IconPack).GetMethod(
            "ValidateRegexTarget",
            BindingFlags.Static | BindingFlags.NonPublic);
        var validateArgs = new object[] { new Target { name = "r/[" }, null };
        ExpectFalse(
            "invalid icon regex is rejected during validation",
            (bool)validateRegexTarget.Invoke(null, validateArgs));

        var isNameMatch = typeof(IconPack).GetMethod(
            "IsNameMatch",
            BindingFlags.Static | BindingFlags.NonPublic);
        ExpectFalse(
            "invalid icon regex is a runtime no-match",
            (bool)isNameMatch.Invoke(null, new object[] { "Boar", "r/[", true }));
    }

    private static void TestFirstCustomIconNameTagBoundary()
    {
        var iconPack = typeof(IconPack);
        var vanillaLength = iconPack.GetField(
            "_vanillaPinTypeLength",
            BindingFlags.Static | BindingFlags.NonPublic);
        vanillaLength.SetValue(null, (Minimap.PinType)10);

        var iconsField = iconPack.GetField("Icons", BindingFlags.Static | BindingFlags.NonPublic);
        var icons = (IList)iconsField.GetValue(null);
        icons.Clear();

        var iconType = iconPack.GetNestedType("Icon", BindingFlags.NonPublic);
        var icon = Activator.CreateInstance(iconType, nonPublic: true);
        iconType.GetField("PinType").SetValue(icon, (Minimap.PinType)10);
        iconType.GetField("Options").SetValue(icon, new Options { hideNameTag = true });
        icons.Add(icon);

        ExpectTrue(
            "first custom pin type honors hideNameTag",
            IconPack.IsNameTagHidden(new Minimap.PinData { m_type = (Minimap.PinType)10 }));
    }

    private static void ExpectLogContains(string name, string expected)
    {
        ExpectTrue(
            name,
            Automatics.Automatics.Logger.Warnings.Any(x => x.IndexOf(expected, StringComparison.Ordinal) >= 0));
    }

    private static void ExpectTrue(string name, bool actual)
    {
        if (actual)
            return;

        Console.Error.WriteLine($"FAIL: {name}");
        _failures++;
    }

    private static void ExpectFalse(string name, bool actual)
    {
        ExpectTrue(name, !actual);
    }
}

namespace Automatics
{
    internal static class Automatics
    {
        public static readonly HarnessLogger Logger = new HarnessLogger();
        public static readonly HarnessL10N L10N = new HarnessL10N();

        public static IEnumerable<string> GetAllResourcePath(string pathname)
        {
            return Enumerable.Empty<string>();
        }
    }

    internal sealed class HarnessLogger
    {
        public readonly List<string> Warnings = new List<string>();

        public void Clear()
        {
            Warnings.Clear();
        }

        public void Debug(string message)
        {
        }

        public void Error(string message)
        {
        }

        public void Info(string message)
        {
        }

        public void Info(Func<string> message)
        {
        }

        public void Warning(string message)
        {
            Warnings.Add(message);
        }
    }

    internal sealed class HarnessL10N
    {
        public string Translate(string key)
        {
            return key;
        }

        public string TranslateInternalName(string name)
        {
            return name;
        }

        public string LocalizeTextOnly(string key, params object[] args)
        {
            return key;
        }
    }
}

namespace BepInEx.Configuration
{
    public sealed class ConfigEntryBase
    {
        public object BoxedValue { get; set; }
    }

    public sealed class TypeConverter
    {
        public Func<string, Type, object> ConvertToObject { get; set; }
        public Func<object, Type, string> ConvertToString { get; set; }
    }

    public static class TomlTypeConverter
    {
        public static void AddConverter(Type type, TypeConverter converter)
        {
        }
    }
}

namespace JetBrains.Annotations
{
    [AttributeUsage(AttributeTargets.All)]
    public sealed class UsedImplicitlyAttribute : Attribute
    {
    }
}

namespace ModUtils
{
    using System.Text.Json;
    using BepInEx.Configuration;

    public static class ConfigurationCustomDrawer
    {
        public static void Register(
            Func<Type, object, bool> matcher,
            Func<Action<ConfigEntryBase>> supplier)
        {
        }
    }

    public static class Json
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            IncludeFields = true,
            PropertyNameCaseInsensitive = false
        };

        public static T Parse<T>(string jsonText)
        {
            return JsonSerializer.Deserialize<T>(jsonText, Options);
        }

        public static string ToString(object obj)
        {
            return JsonSerializer.Serialize(obj, Options);
        }
    }

    public static class L10N
    {
        public static bool IsInternalName(string value)
        {
            return !string.IsNullOrEmpty(value) && value[0] == '$';
        }
    }

    public static class Reflections
    {
        public static T GetField<T>(object instance, string fieldName)
        {
            return default;
        }

        public static void SetField<T>(object instance, string fieldName, T value)
        {
        }
    }

    public sealed class SpriteLoader
    {
        public void SetDebugLogger(object logger)
        {
        }

        public UnityEngine.Sprite Load(string path, int width, int height)
        {
            return null;
        }

        public static string GetTextureFileName(UnityEngine.Sprite sprite)
        {
            return "";
        }
    }
}

namespace UnityEngine
{
    public sealed class Sprite
    {
    }

    public static class Screen
    {
        public static int width = 800;
    }

    public static class Mathf
    {
        public static int FloorToInt(float value) => (int)Math.Floor(value);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Max(float a, float b, float c) => Math.Max(a, Math.Max(b, c));
        public static float Min(float a, float b) => Math.Min(a, b);
        public static int RoundToInt(float value) => (int)Math.Round(value);
    }

    public static class GUI
    {
        public static readonly GUISkin skin = new GUISkin();
    }

    public sealed class GUISkin
    {
        public readonly GUIStyle label = new GUIStyle();
        public readonly GUIStyle button = new GUIStyle();
    }

    public sealed class GUIStyle
    {
        public Vector2 CalcSize(GUIContent content)
        {
            return new Vector2();
        }
    }

    public struct Vector2
    {
        public float x;
    }

    public sealed class GUIContent
    {
        public GUIContent(string text)
        {
        }

        public GUIContent(string text, string tooltip)
        {
        }
    }

    public sealed class GUILayoutOption
    {
    }

    public static class GUILayout
    {
        public static GUILayoutOption ExpandWidth(bool expand) => new GUILayoutOption();
        public static GUILayoutOption MaxWidth(float width) => new GUILayoutOption();
        public static GUILayoutOption Width(float width) => new GUILayoutOption();
        public static void BeginHorizontal(params GUILayoutOption[] options) { }
        public static void BeginVertical(params GUILayoutOption[] options) { }
        public static bool Button(string text, params GUILayoutOption[] options) => false;
        public static void EndHorizontal() { }
        public static void EndVertical() { }
        public static void FlexibleSpace() { }
        public static void Label(object content, params GUILayoutOption[] options) { }
        public static string TextField(string text, params GUILayoutOption[] options) => text;
    }
}

public sealed class Minimap
{
    public enum PinType
    {
        Icon0 = 0,
        Icon1 = 1,
        Icon2 = 2,
        Icon3 = 3,
        Icon4 = 4,
        EventArea = 9
    }

    public enum MapMode
    {
        None,
        Small,
        Large
    }

    public sealed class PinData
    {
        public PinType m_type;
    }

    public sealed class SpriteData
    {
        public PinType m_name;
        public UnityEngine.Sprite m_icon;
    }

    public static readonly Minimap instance = new Minimap();
    public MapMode m_mode;
    public readonly List<SpriteData> m_icons = new List<SpriteData>();
    private bool[] m_visibleIconTypes = new bool[10];
}

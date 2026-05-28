using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using ModUtils;
using UnityEngine;

namespace Automatics.Valheim
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    [Serializable]
    internal class ObjectMatcher
    {
        [NonSerialized]
        private bool _regex;

        [NonSerialized]
        private string _value;

        [NonSerialized]
        private Regex _pattern;

        [NonSerialized]
        private bool _regexPatternValid = true;

        public bool regex
        {
            get => _regex;
            set
            {
                _regex = value;
                OnUpdate();
            }
        }

        public string value
        {
            get => _value;
            set
            {
                _value = value;
                OnUpdate();
            }
        }

        public bool Matches(string name)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(_value))
                return false;

            if (!regex)
                return string.Equals(name, _value, StringComparison.OrdinalIgnoreCase);

            return _regexPatternValid && _pattern != null && _pattern.IsMatch(name);
        }

        public bool TryValidate(string source, out string message)
        {
            if (string.IsNullOrEmpty(_value))
            {
                message = $"{source} has an empty matcher value.";
                return false;
            }

            if (!regex)
            {
                message = "";
                return true;
            }

            if (TryCompileRegex(_value, out _pattern, out var error))
            {
                _regexPatternValid = true;
                message = "";
                return true;
            }

            _regexPatternValid = false;
            message = $"{source} has an invalid regex matcher `{_value}`: {error}";
            return false;
        }

        private void OnUpdate()
        {
            _pattern = null;
            _regexPatternValid = true;

            if (_regex && !string.IsNullOrEmpty(_value))
                _regexPatternValid = TryCompileRegex(_value, out _pattern, out _);
        }

        private static bool TryCompileRegex(string pattern, out Regex regex, out string error)
        {
            regex = null;
            error = "";
            try
            {
                regex = new Regex(pattern);
                return true;
            }
            catch (ArgumentException e)
            {
                error = e.Message;
                return false;
            }
        }
    }

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    [Serializable]
    internal class ObjectElement
    {
        public string identifier { get; set; }
        public string label { get; set; }
        public List<ObjectMatcher> matches { get; set; }

        public bool IsValid()
        {
            return TryValidate("", out _);
        }

        public bool TryValidate(string source, out string message)
        {
            if (string.IsNullOrEmpty(identifier))
            {
                message = $"{source} has an empty identifier.";
                return false;
            }

            if (string.IsNullOrEmpty(label))
            {
                message = $"{source} `{identifier}` has an empty label.";
                return false;
            }

            if (matches is null || matches.Count == 0)
            {
                message = $"{source} `{identifier}` has no matchers.";
                return false;
            }

            for (var i = 0; i < matches.Count; i++)
            {
                var matcher = matches[i];
                if (matcher is null)
                {
                    message = $"{source} `{identifier}` has a null matcher at index {i}.";
                    return false;
                }

                if (!matcher.TryValidate($"{source} `{identifier}` matcher {i}", out message))
                    return false;
            }

            message = "";
            return true;
        }
    }

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    [Serializable]
    internal class ObjectDataJson
    {
        public string type { get; set; }
        public int order { get; set; }
        public List<ObjectElement> values { get; set; }

        public bool IsValid()
        {
            return TryValidate("", out _);
        }

        public bool TryValidate(string source, out string message)
        {
            if (string.IsNullOrEmpty(type))
            {
                message = $"{source} has an empty type.";
                return false;
            }

            if (values is null)
            {
                message = $"{source} `{type}` has no values.";
                return false;
            }

            for (var i = 0; i < values.Count; i++)
            {
                var element = values[i];
                if (element is null)
                {
                    message = $"{source} `{type}` has a null value at index {i}.";
                    return false;
                }

                if (!element.TryValidate($"{source} `{type}` value {i}", out message))
                    return false;
            }

            message = "";
            return true;
        }
    }

    internal static class ObjectElementList
    {
        private static bool _initialized;

        private static string EscapeForToml(string json)
        {
            var sb = new StringBuilder();
            var backslash = false;
            foreach (var c in json.ToCharArray())
            {
                switch (c)
                {
                    case '\\':
                        backslash = true;
                        sb.Append("$$");
                        continue;
                    case '"':
                        if (!backslash)
                            sb.Append('\\');
                        backslash = false;
                        break;
                    default:
                        backslash = false;
                        break;
                }

                sb.Append(c);
            }

            return sb.ToString();
        }

        private static string UnescapeToml(string toml)
        {
            var sb = new StringBuilder();
            var dollar = false;
            foreach (var c in toml.ToCharArray())
            {
                switch (c)
                {
                    case '\\':
                        if (dollar)
                            sb.Append('$');
                        dollar = false;
                        continue;
                    case '$':
                        if (dollar)
                            sb.Append('\\');
                        dollar = !dollar;
                        continue;
                    default:
                        if (dollar)
                            sb.Append('$');
                        dollar = false;
                        break;
                }

                sb.Append(c);
            }

            return sb.ToString();
        }

        private static Action<ConfigEntryBase> GetCustomDrawer()
        {
            var identifierInput = "";
            var labelInput = "";
            var valueInput = "";
            return entry =>
            {
                var guiWidth = Mathf.Min(Screen.width, 650);
                var maxWidth = guiWidth - Mathf.RoundToInt(guiWidth / 2.5f) - 115;
                var addButtonText = Automatics.L10N.Translate("mod_utils_config_button_add");
                var removeButtonText = Automatics.L10N.Translate("mod_utils_config_button_remove");
                var identifierLabel =
                    Automatics.L10N.Translate("@config_object_element_identifier");
                var labelLabel = Automatics.L10N.Translate("@config_object_element_label");
                var valueLabel = Automatics.L10N.Translate("@config_object_element_value");
                var labelsMaxWidth = Mathf.Max(
                    GUI.skin.label.CalcSize(new GUIContent(identifierLabel)).x,
                    GUI.skin.label.CalcSize(new GUIContent(labelLabel)).x,
                    GUI.skin.label.CalcSize(new GUIContent(valueLabel)).x);

                var elements = new List<ObjectElement>((List<ObjectElement>)entry.BoxedValue);

                GUILayout.BeginVertical(GUILayout.MaxWidth(maxWidth));

                GUILayout.BeginHorizontal();

                GUILayout.Label(new GUIContent(identifierLabel, ""),
                    GUILayout.Width(labelsMaxWidth));
                identifierInput = GUILayout.TextField(
                    string.Concat(identifierInput.Where(x => !char.IsWhiteSpace(x))),
                    GUILayout.ExpandWidth(true));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();

                GUILayout.Label(labelLabel, GUILayout.Width(labelsMaxWidth));
                labelInput = GUILayout.TextField(labelInput, GUILayout.ExpandWidth(true));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label(valueLabel, GUILayout.Width(labelsMaxWidth));
                valueInput = GUILayout.TextField(valueInput, GUILayout.ExpandWidth(true));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                var clicked = GUILayout.Button(addButtonText, GUILayout.ExpandWidth(true));
                if (clicked && !string.IsNullOrEmpty(identifierInput) &&
                    !string.IsNullOrEmpty(labelInput) && !string.IsNullOrEmpty(valueInput))
                {
                    var regex = valueInput.StartsWith("r/");
                    valueInput = regex ? valueInput.Substring(2) : valueInput;
                    elements.Add(new ObjectElement
                    {
                        identifier = identifierInput,
                        label = labelInput,
                        matches = new List<ObjectMatcher>
                        {
                            new ObjectMatcher
                            {
                                regex = regex,
                                value = valueInput
                            }
                        }
                    });
                    entry.BoxedValue = elements;

                    identifierInput = "";
                    labelInput = "";
                    valueInput = "";
                }

                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                var lineWidth = 0.0;
                foreach (var element in elements.Where(x => x != null).ToList())
                {
                    var identifier = element.identifier;
                    var rawLabel = element.label;
                    var label = Automatics.L10N.TranslateInternalName(rawLabel);
                    var pattern = element.matches?.FirstOrDefault()?.value ?? "";

                    var elementWidth =
                        Mathf.FloorToInt(GUI.skin.label.CalcSize(new GUIContent(label)).x) +
                        Mathf.FloorToInt(GUI.skin.button
                            .CalcSize(new GUIContent(removeButtonText)).x);

                    lineWidth += elementWidth;
                    if (lineWidth > maxWidth)
                    {
                        GUILayout.EndHorizontal();
                        lineWidth = elementWidth;
                        GUILayout.BeginHorizontal();
                    }

                    var tooltip = Automatics.L10N.LocalizeTextOnly("@config_object_element_preview", label, rawLabel, identifier, pattern);
                    GUILayout.Label(new GUIContent(label, tooltip), GUILayout.ExpandWidth(false));
                    if (GUILayout.Button(removeButtonText, GUILayout.ExpandWidth(false)))
                        if (elements.Remove(element))
                            entry.BoxedValue = elements;
                }

                GUILayout.EndHorizontal();

                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
            };
        }

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            TomlTypeConverter.AddConverter(typeof(List<ObjectElement>), new TypeConverter
            {
                ConvertToObject = (str, type) =>
                {
                    if (string.IsNullOrEmpty(str))
                        return new List<ObjectElement>();

                    try
                    {
                        return Json.Parse<List<ObjectElement>>(UnescapeToml(str.Trim('"'))) ??
                               new List<ObjectElement>();
                    }
                    catch (Exception e)
                    {
                        Automatics.Logger?.Warning($"Failed to parse custom object config: {e.Message}");
                        return new List<ObjectElement>();
                    }
                },
                ConvertToString = (obj, type) =>
                {
                    var elements = (List<ObjectElement>)obj;
                    return elements.Any() ? $"\"{EscapeForToml(Json.ToString(elements))}\"" : "";
                }
            });

            ConfigurationCustomDrawer.Register(
                (type, value) => type == typeof(List<ObjectElement>),
                GetCustomDrawer);
        }
    }

    internal class ValheimObject
    {
        private static readonly List<ObjectDataJson> JsonCache;

        public static readonly ValheimObject Animal;
        public static readonly ValheimObject Container;
        public static readonly ValheimObject Dungeon;
        public static readonly ValheimObject Flora;
        public static readonly ValheimObject Mineral;
        public static readonly ValheimObject Monster;
        public static readonly ValheimObject Spawner;
        public static readonly ValheimObject Spot;

        /// <summary>
        /// Raised whenever a ValheimObject's custom element set changes at runtime
        /// (e.g. via <see cref="RegisterCustom"/> triggered by a BepInEx config
        /// SettingChanged). The argument identifies which registry was affected so
        /// that subscribers can filter by domain (Animal/Monster for dynamic
        /// mapping, Flora/Mineral/Spawner/Dungeon/Spot for static mapping).
        /// </summary>
        public static event Action<ValheimObject> RegistryChanged;

        static ValheimObject()
        {
            JsonCache = new List<ObjectDataJson>();

            Animal = new ValheimObject("animal");
            Container = new ValheimObject("container");
            Dungeon = new ValheimObject("dungeon");
            Flora = new ValheimObject("flora");
            Mineral = new ValheimObject("mineral");
            Monster = new ValheimObject("monster");
            Spawner = new ValheimObject("spawner");
            Spot = new ValheimObject("spot");

            ObjectElementList.Initialize();
        }

        private readonly string _type;
        private readonly Dictionary<string, ObjectElement> _elements;
        private readonly Dictionary<string, ObjectElement> _customElements;
        private readonly List<ObjectElement> _allElements;

        public ValheimObject(string type)
        {
            _type = type;
            _elements = new Dictionary<string, ObjectElement>();
            _customElements = new Dictionary<string, ObjectElement>();
            _allElements = new List<ObjectElement>();

            if (JsonCache.Any())
                Register(JsonCache);
        }

        private static void Initialize(string directory)
        {
            if (!Directory.Exists(directory))
            {
                Automatics.Logger.Debug($"Directory does not exist: {directory}");
                return;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    var json = Json.Parse<ObjectDataJson>(File.ReadAllText(file));
                    if (json is null)
                    {
                        Automatics.Logger.Warning($"Invalid object data skipped: {file} did not parse into an object.");
                        continue;
                    }

                    if (json.TryValidate(file, out var message))
                    {
                        JsonCache.Add(json);
                        continue;
                    }

                    Automatics.Logger.Warning($"Invalid object data skipped: {message}");
                }
                catch (Exception e)
                {
                    Automatics.Logger.Error($"Failed to read Json file: {file}\n{e}");
                }
            }
        }

        public static void Initialize(IEnumerable<string> directories)
        {
            foreach (var directory in directories)
                Initialize(directory);

            Animal.Register(JsonCache);
            Container.Register(JsonCache);
            Dungeon.Register(JsonCache);
            Flora.Register(JsonCache);
            Mineral.Register(JsonCache);
            Monster.Register(JsonCache);
            Spawner.Register(JsonCache);
            Spot.Register(JsonCache);
        }

        public static void PostInitialize()
        {
            JsonCache.Clear();
        }

        private void UpdateElements()
        {
            _allElements.Clear();
            _allElements.AddRange(_elements.Values);
            _allElements.AddRange(_customElements.Values);
        }

        private static ObjectElement CloneElement(ObjectElement element)
        {
            return new ObjectElement
            {
                identifier = element.identifier,
                label = element.label,
                matches = new List<ObjectMatcher>(element.matches)
            };
        }

        private static void LogWarning(string message)
        {
            Automatics.Logger?.Warning(message);
        }

        private void ReportDuplicateExactMatchers()
        {
            var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var element in _elements.Values)
            foreach (var matcher in element.matches.Where(x => x != null && !x.regex &&
                                                               !string.IsNullOrEmpty(x.value)))
            {
                if (seen.TryGetValue(matcher.value, out var identifier))
                {
                    LogWarning(
                        $"Duplicate exact matcher `{matcher.value}` in `{_type}` objects: `{identifier}` and `{element.identifier}`.");
                    continue;
                }

                seen[matcher.value] = element.identifier;
            }
        }

        private void Register(IEnumerable<ObjectDataJson> jsons)
        {
            foreach (var element in jsons.Where(x => x.type.ToLower() == _type)
                         .OrderBy(x => x.order).SelectMany(x => x.values))
                _elements[element.identifier.ToLower()] = CloneElement(element);
            ReportDuplicateExactMatchers();
            UpdateElements();
        }

        public void RegisterCustom(IEnumerable<ObjectElement> elements)
        {
            _customElements.Clear();
            foreach (var element in elements ?? Enumerable.Empty<ObjectElement>())
            {
                if (element is null)
                {
                    LogWarning($"Invalid custom `{_type}` object skipped: null element.");
                    continue;
                }

                if (!element.TryValidate($"custom `{_type}` object", out var message))
                {
                    LogWarning($"Invalid custom `{_type}` object skipped: {message}");
                    continue;
                }

                _customElements[element.identifier.ToLower()] = CloneElement(element);
            }

            UpdateElements();
            RegistryChanged?.Invoke(this);
        }

        public IEnumerable<ObjectElement> GetElements()
        {
            return new List<ObjectElement>(_elements.Values);
        }

        public IEnumerable<ObjectElement> GetCustomElements()
        {
            return new List<ObjectElement>(_customElements.Values);
        }

        public IEnumerable<ObjectElement> GetAllElements()
        {
            return _allElements.ToList();
        }

        public bool GetIdentify(string name, out string identifier)
        {
            var element = _allElements.FirstOrDefault(x =>
                x != null && x.matches != null && x.matches.Any(y => y != null && y.Matches(name)));
            if (element is null || !element.IsValid())
            {
                identifier = "";
                return false;
            }

            identifier = element.identifier;
            return true;
        }

        public bool GetName(string identifier, out string name)
        {
            var key = identifier.ToLower();
            if (!_elements.TryGetValue(key, out var element) &&
                !_customElements.TryGetValue(key, out element))
            {
                name = "";
                return false;
            }

            name = element.label;
            return true;
        }

        public bool IsDefined(string nameOrIdentify)
        {
            return GetIdentify(nameOrIdentify, out _) || GetName(nameOrIdentify, out _);
        }
    }
}

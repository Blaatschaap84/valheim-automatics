using System;
using System.Collections.Generic;
using System.Text;

namespace BepInEx.Configuration
{
    public sealed class ConfigFile
    {
        public ConfigFile(string configFilePath)
        {
            ConfigFilePath = configFilePath;
        }

        public string ConfigFilePath { get; }

        public void Reload()
        {
        }
    }
}

namespace UnityEngine
{
    public static class Mathf
    {
        public static int Min(int a, int b)
        {
            return Math.Min(a, b);
        }

        public static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}

namespace ModUtils
{
    public static class Csv
    {
        public static IEnumerable<string> ParseLine(string line)
        {
            var values = new List<string>();
            var buffer = new StringBuilder();
            var quoted = false;

            for (var i = 0; i < line.Length; i++)
            {
                var current = line[i];
                if (quoted)
                {
                    if (current == '"' && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        buffer.Append('"');
                        i++;
                        continue;
                    }

                    if (current == '"')
                    {
                        quoted = false;
                        continue;
                    }

                    buffer.Append(current);
                    continue;
                }

                if (current == '"')
                {
                    quoted = true;
                    continue;
                }

                if (current == ',')
                {
                    values.Add(buffer.ToString().Trim());
                    buffer.Clear();
                    continue;
                }

                buffer.Append(current);
            }

            values.Add(buffer.ToString().Trim());
            return values;
        }

        public static string Escape(string value)
        {
            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
                return value;

            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
    }
}

namespace Automatics
{
    internal static class Automatics
    {
        public static TestLogger Logger { get; } = new TestLogger();
    }

    internal sealed class TestLogger
    {
        public void Message(string message)
        {
        }

        public void Warning(string message)
        {
        }

        public void Error(string message)
        {
        }
    }
}

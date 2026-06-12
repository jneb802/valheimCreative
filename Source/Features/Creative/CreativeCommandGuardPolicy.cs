using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ValheimCreative.Features.Creative
{
    internal static class CreativeCommandGuardPolicy
    {
        private const string FileName = "valheimCreative.command-guard.yaml";
        private const string DefaultFileContents =
            "# Command names and wildcard rules that only work inside the player's active creative zone.\n" +
            "# Exact entries match the first command token only. Example: tod does not match today.\n" +
            "# Wildcards use *. Example: tweak_* protects tweak_spawner, tweak_altar, and other tweak commands.\n" +
            "enabled: true\n" +
            "commands:\n" +
            "  - tweak_*\n";

        private static readonly IDeserializer Deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        private static DateTime _lastWriteTimeUtc;
        private static string _policyKey = string.Empty;

        internal static bool Enabled { get; private set; } = true;
        internal static IReadOnlyList<string> CommandRules { get; private set; } = new[] { "tweak_*" };
        internal static string CommandRulePayload => string.Join(",", CommandRules);
        internal static string PolicyKey => _policyKey;

        internal static void Initialize()
        {
            EnsureFileExists();
            LoadPolicy(force: true);
        }

        internal static bool Update()
        {
            string path = GetPath();
            if (!File.Exists(path))
            {
                EnsureFileExists();
            }

            DateTime writeTimeUtc = File.GetLastWriteTimeUtc(path);
            if (writeTimeUtc == _lastWriteTimeUtc)
            {
                return false;
            }

            return LoadPolicy(force: false);
        }

        internal static bool IsProtectedCommand(string normalizedCommand)
        {
            if (!Enabled || normalizedCommand.Length == 0)
            {
                return false;
            }

            foreach (string protectedCommand in CommandRules)
            {
                if (MatchesRule(normalizedCommand, protectedCommand))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool LoadPolicy(bool force)
        {
            string path = GetPath();
            try
            {
                DateTime writeTimeUtc = File.GetLastWriteTimeUtc(path);
                string yaml = File.ReadAllText(path);
                CreativeCommandGuardYaml data = Deserializer.Deserialize<CreativeCommandGuardYaml>(yaml) ?? new CreativeCommandGuardYaml();
                string[] commands = NormalizeCommands(data.Commands).ToArray();
                string newPolicyKey = data.Enabled + ":" + string.Join(",", commands);
                bool changed = force || newPolicyKey != _policyKey;

                Enabled = data.Enabled;
                CommandRules = commands;
                _lastWriteTimeUtc = writeTimeUtc;
                _policyKey = newPolicyKey;

                if (changed)
                {
                    ValheimCreativePlugin.ModLogger.LogInfo($"Loaded creative command guard policy from {path}: enabled={Enabled}, commands={CommandRulePayload}.");
                }

                return changed;
            }
            catch (Exception ex)
            {
                if (File.Exists(path))
                {
                    _lastWriteTimeUtc = File.GetLastWriteTimeUtc(path);
                }

                ValheimCreativePlugin.ModLogger.LogWarning($"Failed to load creative command guard policy from {path}: {ex.Message}");
                return false;
            }
        }

        private static bool MatchesRule(string normalizedCommand, string rule)
        {
            if (rule.IndexOf('*') < 0)
            {
                return string.Equals(normalizedCommand, rule, StringComparison.Ordinal);
            }

            return MatchesWildcard(normalizedCommand, rule);
        }

        private static bool MatchesWildcard(string value, string pattern)
        {
            int valueIndex = 0;
            int patternIndex = 0;
            int starIndex = -1;
            int retryValueIndex = 0;

            while (valueIndex < value.Length)
            {
                if (patternIndex < pattern.Length &&
                    pattern[patternIndex] == value[valueIndex])
                {
                    valueIndex++;
                    patternIndex++;
                    continue;
                }

                if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
                {
                    starIndex = patternIndex;
                    retryValueIndex = valueIndex;
                    patternIndex++;
                    continue;
                }

                if (starIndex >= 0)
                {
                    patternIndex = starIndex + 1;
                    retryValueIndex++;
                    valueIndex = retryValueIndex;
                    continue;
                }

                return false;
            }

            while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                patternIndex++;
            }

            return patternIndex == pattern.Length;
        }

        private static IEnumerable<string> NormalizeCommands(IEnumerable<string>? commands)
        {
            if (commands == null)
            {
                yield break;
            }

            HashSet<string> seen = new();
            foreach (string command in commands)
            {
                string normalized = (command ?? string.Empty).Trim().ToLowerInvariant();
                if (normalized.Length == 0 || !seen.Add(normalized))
                {
                    continue;
                }

                yield return normalized;
            }
        }

        private static void EnsureFileExists()
        {
            string path = GetPath();
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (!File.Exists(path))
            {
                File.WriteAllText(path, DefaultFileContents);
                return;
            }

            MigrateLegacyDefaultWildcard(path);
        }

        private static void MigrateLegacyDefaultWildcard(string path)
        {
            string[] lines = File.ReadAllLines(path);
            bool changed = false;
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index];
                if (!string.Equals(line.Trim(), "- tweak_", StringComparison.Ordinal))
                {
                    continue;
                }

                int dashIndex = line.IndexOf('-');
                string indent = dashIndex > 0 ? line.Substring(0, dashIndex) : string.Empty;
                lines[index] = indent + "- tweak_*";
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            File.WriteAllText(path, string.Join(Environment.NewLine, lines) + Environment.NewLine);
            ValheimCreativePlugin.ModLogger.LogInfo($"Migrated creative command guard wildcard entries in {path}.");
        }

        internal static string NormalizeCommand(string rawCommand)
        {
            if (string.IsNullOrWhiteSpace(rawCommand))
            {
                return string.Empty;
            }

            string[] parts = rawCommand.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 0 ? string.Empty : parts[0].ToLowerInvariant();
        }

        private static string GetPath()
        {
            return Path.Combine(Paths.ConfigPath, FileName);
        }

        private sealed class CreativeCommandGuardYaml
        {
            public bool Enabled { get; set; } = true;
            public List<string> Commands { get; set; } = new() { "tweak_*" };
        }
    }
}

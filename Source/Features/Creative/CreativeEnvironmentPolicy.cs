using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BepInEx;
using UnityEngine;
using ValheimCreative.Configuration;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ValheimCreative.Features.Creative
{
    internal static class CreativeEnvironmentPolicy
    {
        private const string FileName = "valheimCreative.environment.yaml";
        internal const string DefaultPresetName = "defaultMeadows";

        private static readonly IDeserializer Deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        private static DateTime _lastWriteTimeUtc;
        private static string _policyKey = string.Empty;
        private static CreativeEnvironmentYaml _policy = new();

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

        internal static CreativeEnvironmentSettings Resolve(CreativeZone zone)
        {
            CreativeEnvironmentSettings settings = CreativeEnvironmentSettings.FromYaml(_policy.Defaults);
            ApplyBiome(settings, zone.Biome);
            ApplyZone(settings, zone.SlotId);
            settings.TerrainMode = zone.TerrainMode;

            if (!settings.TerrainEnabled || zone.TerrainMode != CreativeTerrainMode.WorldSeedPatch)
            {
                settings.VegetationEnabled = false;
            }

            return settings;
        }

        internal static CreativeEnvironmentSettings ResolveAtPosition(Vector3 position)
        {
            if (CreativeSessionManager.TryGetCreativeZoneAtPosition(position, out CreativeZone? zone) && zone != null)
            {
                return Resolve(zone);
            }

            return CreativeEnvironmentSettings.CreateDisabled();
        }

        internal static bool TryGetPreset(string presetName, Heightmap.Biome activeBiome, out CreativeVegetationPreset preset, out string error)
        {
            string normalizedPresetName = NormalizeName(presetName);
            if (string.IsNullOrEmpty(normalizedPresetName))
            {
                normalizedPresetName = NormalizeName(DefaultPresetName);
            }

            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            return TryBuildPreset(normalizedPresetName, activeBiome, seen, out preset, out error);
        }

        internal static string PolicyKey => _policyKey;

        private static bool TryBuildPreset(
            string presetName,
            Heightmap.Biome activeBiome,
            HashSet<string> seen,
            out CreativeVegetationPreset preset,
            out string error)
        {
            preset = CreativeVegetationPreset.Empty(activeBiome);
            error = string.Empty;

            if (!seen.Add(presetName))
            {
                error = $"Vegetation preset inheritance loop includes {presetName}.";
                return false;
            }

            if (_policy.Presets == null ||
                !_policy.Presets.TryGetValue(presetName, out CreativeVegetationPresetYaml presetYaml) ||
                presetYaml == null)
            {
                error = $"Vegetation preset {presetName} was not found.";
                return false;
            }

            string inherit = NormalizeName(presetYaml.Inherit);
            if (!string.IsNullOrEmpty(inherit) &&
                !TryBuildPreset(inherit, activeBiome, seen, out preset, out error))
            {
                return false;
            }

            preset = preset.WithOverrides(presetYaml.Entries ?? new List<CreativeVegetationEntryYaml>());
            return true;
        }

        private static void ApplyBiome(CreativeEnvironmentSettings settings, Heightmap.Biome biome)
        {
            if (_policy.Biomes == null)
            {
                return;
            }

            string biomeName = NormalizeName(biome.ToString());
            foreach (KeyValuePair<string, CreativeEnvironmentOverrideYaml> entry in _policy.Biomes)
            {
                if (!NormalizeName(entry.Key).Equals(biomeName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                settings.Apply(entry.Value);
                return;
            }
        }

        private static void ApplyZone(CreativeEnvironmentSettings settings, string slotId)
        {
            if (_policy.Zones == null)
            {
                return;
            }

            foreach (KeyValuePair<string, CreativeEnvironmentOverrideYaml> entry in _policy.Zones)
            {
                if (!entry.Key.Trim().Equals(slotId, StringComparison.Ordinal))
                {
                    continue;
                }

                settings.Apply(entry.Value);
                return;
            }
        }

        private static bool LoadPolicy(bool force)
        {
            string path = GetPath();
            try
            {
                DateTime writeTimeUtc = File.GetLastWriteTimeUtc(path);
                string yaml = File.ReadAllText(path);
                CreativeEnvironmentYaml policy = Deserializer.Deserialize<CreativeEnvironmentYaml>(yaml) ?? new CreativeEnvironmentYaml();
                NormalizePolicy(policy);
                string policyKey = BuildPolicyKey(policy);
                bool changed = force || !_policyKey.Equals(policyKey, StringComparison.Ordinal);
                _policy = policy;
                _lastWriteTimeUtc = writeTimeUtc;
                _policyKey = policyKey;

                if (changed)
                {
                    ValheimCreativePlugin.ModLogger.LogInfo($"Loaded creative environment policy from {path}: key={_policyKey}.");
                }

                return changed;
            }
            catch (Exception ex)
            {
                if (File.Exists(path))
                {
                    _lastWriteTimeUtc = File.GetLastWriteTimeUtc(path);
                }

                ValheimCreativePlugin.ModLogger.LogWarning($"Failed to load creative environment policy from {path}: {ex.Message}");
                return false;
            }
        }

        private static void NormalizePolicy(CreativeEnvironmentYaml policy)
        {
            policy.Defaults ??= new CreativeEnvironmentOverrideYaml();
            policy.Defaults.Terrain ??= new CreativeTerrainPolicyYaml();
            policy.Defaults.Vegetation ??= new CreativeVegetationPolicyYaml();

            policy.Biomes = NormalizeDictionary(policy.Biomes);
            policy.Zones ??= new Dictionary<string, CreativeEnvironmentOverrideYaml>(StringComparer.Ordinal);
            policy.Presets = NormalizeDictionary(policy.Presets);
        }

        private static Dictionary<string, T> NormalizeDictionary<T>(Dictionary<string, T>? source)
        {
            Dictionary<string, T> result = new(StringComparer.OrdinalIgnoreCase);
            if (source == null)
            {
                return result;
            }

            foreach (KeyValuePair<string, T> entry in source)
            {
                string key = NormalizeName(entry.Key);
                if (key.Length == 0)
                {
                    continue;
                }

                result[key] = entry.Value;
            }

            return result;
        }

        private static string BuildPolicyKey(CreativeEnvironmentYaml policy)
        {
            int hash = 17;
            AddHash(ref hash, policy.Defaults);
            AddDictionaryHash(ref hash, policy.Biomes);
            AddDictionaryHash(ref hash, policy.Zones);
            AddDictionaryHash(ref hash, policy.Presets);
            return hash.ToString(CultureInfo.InvariantCulture);
        }

        private static void AddDictionaryHash<T>(ref int hash, Dictionary<string, T>? values)
        {
            if (values == null)
            {
                return;
            }

            foreach (KeyValuePair<string, T> entry in values.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
            {
                AddHash(ref hash, entry.Key);
                AddHash(ref hash, entry.Value);
            }
        }

        private static void AddHash(ref int hash, object? value)
        {
            unchecked
            {
                hash = (hash * 397) ^ (value?.ToString()?.GetHashCode() ?? 0);
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
                File.WriteAllText(path, CreativeEnvironmentDefaultPolicy.Contents);
                return;
            }

            string contents = File.ReadAllText(path);
            if (!ShouldReplaceGeneratedPolicy(contents))
            {
                return;
            }

            string backupPath = $"{path}.bak-{DateTime.UtcNow:yyyyMMddHHmmss}";
            File.Copy(path, backupPath, overwrite: false);
            File.WriteAllText(path, CreativeEnvironmentDefaultPolicy.Contents);
            ValheimCreativePlugin.ModLogger.LogInfo($"Updated generated creative environment policy. Backup: {backupPath}");
        }

        private static bool ShouldReplaceGeneratedPolicy(string contents)
        {
            return contents.IndexOf("preset: Vanilla", StringComparison.Ordinal) >= 0 &&
                   contents.IndexOf("sparseMeadows:", StringComparison.Ordinal) >= 0 &&
                   contents.IndexOf("zones: {}", StringComparison.Ordinal) >= 0 &&
                   contents.IndexOf("defaultBlackForest:", StringComparison.Ordinal) < 0;
        }

        private static string GetPath()
        {
            return Path.Combine(Paths.ConfigPath, FileName);
        }

        private static string NormalizeName(string? raw)
        {
            return (raw ?? string.Empty).Trim().Replace("_", string.Empty).Replace("-", string.Empty);
        }
    }

    internal sealed class CreativeEnvironmentSettings
    {
        internal static CreativeEnvironmentSettings CreateDisabled()
        {
            return new CreativeEnvironmentSettings
            {
                TerrainEnabled = false,
                TerrainMode = CreativeTerrainMode.FlatPad,
                VegetationEnabled = false,
                VegetationPreset = CreativeEnvironmentPolicy.DefaultPresetName,
                VegetationDropsEnabled = false
            };
        }

        internal bool TerrainEnabled { get; set; }
        internal CreativeTerrainMode TerrainMode { get; set; }
        internal bool VegetationEnabled { get; set; }
        internal string VegetationPreset { get; set; } = CreativeEnvironmentPolicy.DefaultPresetName;
        internal bool VegetationDropsEnabled { get; set; }

        internal static CreativeEnvironmentSettings FromYaml(CreativeEnvironmentOverrideYaml? yaml)
        {
            CreativeEnvironmentSettings settings = new()
            {
                TerrainEnabled = true,
                TerrainMode = CreativeTerrainMode.WorldSeedPatch,
                VegetationEnabled = true,
                VegetationPreset = CreativeEnvironmentPolicy.DefaultPresetName,
                VegetationDropsEnabled = false
            };
            settings.Apply(yaml);
            return settings;
        }

        internal void Apply(CreativeEnvironmentOverrideYaml? yaml)
        {
            if (yaml == null)
            {
                return;
            }

            if (yaml.Terrain != null)
            {
                if (yaml.Terrain.Enabled.HasValue)
                {
                    TerrainEnabled = yaml.Terrain.Enabled.Value;
                }

            }

            if (yaml.Vegetation != null)
            {
                if (yaml.Vegetation.Enabled.HasValue)
                {
                    VegetationEnabled = yaml.Vegetation.Enabled.Value;
                }

                string vegetationPreset = yaml.Vegetation.Preset ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(vegetationPreset))
                {
                    VegetationPreset = vegetationPreset.Trim();
                }

                if (yaml.Vegetation.DropsEnabled.HasValue)
                {
                    VegetationDropsEnabled = yaml.Vegetation.DropsEnabled.Value;
                }
            }
        }
    }

    internal sealed class CreativeEnvironmentYaml
    {
        public CreativeEnvironmentOverrideYaml? Defaults { get; set; }
        public Dictionary<string, CreativeEnvironmentOverrideYaml>? Biomes { get; set; }
        public Dictionary<string, CreativeEnvironmentOverrideYaml>? Zones { get; set; }
        public Dictionary<string, CreativeVegetationPresetYaml>? Presets { get; set; }
    }

    internal sealed class CreativeEnvironmentOverrideYaml
    {
        public CreativeTerrainPolicyYaml? Terrain { get; set; }
        public CreativeVegetationPolicyYaml? Vegetation { get; set; }

        public override string ToString()
        {
            return $"{Terrain}|{Vegetation}";
        }
    }

    internal sealed class CreativeTerrainPolicyYaml
    {
        public bool? Enabled { get; set; }

        public override string ToString()
        {
            return $"{Enabled}";
        }
    }

    internal sealed class CreativeVegetationPolicyYaml
    {
        public bool? Enabled { get; set; }
        public string? Preset { get; set; }
        public bool? DropsEnabled { get; set; }

        public override string ToString()
        {
            return $"{Enabled}:{Preset}:{DropsEnabled}";
        }
    }

    internal sealed class CreativeVegetationPresetYaml
    {
        public string? Inherit { get; set; }
        public List<CreativeVegetationEntryYaml>? Entries { get; set; }

        public override string ToString()
        {
            string entries = Entries == null ? string.Empty : string.Join(";", Entries.Select(entry => entry.ToString()));
            return $"{Inherit}:{entries}";
        }
    }

    internal sealed class CreativeVegetationEntryYaml
    {
        public string? Name { get; set; }
        public string? Prefab { get; set; }
        public bool? Enabled { get; set; }
        public float? Min { get; set; }
        public float? Max { get; set; }
        public bool? ForcePlacement { get; set; }
        public float? ScaleMin { get; set; }
        public float? ScaleMax { get; set; }
        public float? RandTilt { get; set; }
        public float? ChanceToUseGroundTilt { get; set; }
        public string? Biome { get; set; }
        public string? BiomeArea { get; set; }
        public bool? BlockCheck { get; set; }
        public bool? SnapToStaticSolid { get; set; }
        public float? MinAltitude { get; set; }
        public float? MaxAltitude { get; set; }
        public float? MinVegetation { get; set; }
        public float? MaxVegetation { get; set; }
        public bool? SurroundCheckVegetation { get; set; }
        public float? SurroundCheckDistance { get; set; }
        public int? SurroundCheckLayers { get; set; }
        public float? SurroundBetterThanAverage { get; set; }
        public float? MinOceanDepth { get; set; }
        public float? MaxOceanDepth { get; set; }
        public float? MinTilt { get; set; }
        public float? MaxTilt { get; set; }
        public float? TerrainDeltaRadius { get; set; }
        public float? MaxTerrainDelta { get; set; }
        public float? MinTerrainDelta { get; set; }
        public bool? SnapToWater { get; set; }
        public float? GroundOffset { get; set; }
        public int? GroupSizeMin { get; set; }
        public int? GroupSizeMax { get; set; }
        public float? GroupRadius { get; set; }
        public float? MinDistanceFromCenter { get; set; }
        public float? MaxDistanceFromCenter { get; set; }
        public bool? InForest { get; set; }
        public float? ForestTresholdMin { get; set; }
        public float? ForestTresholdMax { get; set; }

        public override string ToString()
        {
            return $"{Name}:{Prefab}:{Enabled}:{Min}:{Max}:{Biome}:{GroupRadius}";
        }
    }
}

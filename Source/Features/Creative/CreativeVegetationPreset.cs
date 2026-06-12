using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ValheimCreative.Features.Creative
{
    internal sealed class CreativeVegetationPreset
    {
        private readonly List<ZoneSystem.ZoneVegetation> _entries;

        private CreativeVegetationPreset(Heightmap.Biome sourceBiome, IEnumerable<ZoneSystem.ZoneVegetation> entries)
        {
            SourceBiome = sourceBiome;
            _entries = entries.ToList();
            PrefabNames = _entries
                .Where(entry => entry.m_prefab != null)
                .Select(entry => entry.m_prefab.name)
                .Distinct(StringComparer.Ordinal)
                .ToHashSet(StringComparer.Ordinal);
        }

        internal Heightmap.Biome SourceBiome { get; }
        internal HashSet<string> PrefabNames { get; }
        internal IReadOnlyList<ZoneSystem.ZoneVegetation> Entries => _entries;

        internal static CreativeVegetationPreset Vanilla(Heightmap.Biome biome)
        {
            List<ZoneSystem.ZoneVegetation> entries = new();
            if (ZoneSystem.instance != null)
            {
                foreach (ZoneSystem.ZoneVegetation vegetation in ZoneSystem.instance.m_vegetation)
                {
                    if (vegetation == null ||
                        vegetation.m_prefab == null ||
                        (vegetation.m_biome & biome) == Heightmap.Biome.None)
                    {
                        continue;
                    }

                    entries.Add(vegetation.Clone());
                }
            }

            return new CreativeVegetationPreset(biome, entries);
        }

        internal CreativeVegetationPreset WithOverrides(IEnumerable<CreativeVegetationEntryYaml> overrides)
        {
            List<ZoneSystem.ZoneVegetation> entries = _entries.Select(entry => entry.Clone()).ToList();
            foreach (CreativeVegetationEntryYaml overrideEntry in overrides)
            {
                ApplyOverride(entries, overrideEntry);
            }

            return new CreativeVegetationPreset(SourceBiome, entries.Where(entry => entry.m_enable));
        }

        private void ApplyOverride(List<ZoneSystem.ZoneVegetation> entries, CreativeVegetationEntryYaml overrideEntry)
        {
            string prefabName = (overrideEntry.Prefab ?? string.Empty).Trim();
            string entryName = (overrideEntry.Name ?? string.Empty).Trim();
            ZoneSystem.ZoneVegetation? entry = entries.FirstOrDefault(candidate =>
                Matches(candidate, prefabName, entryName));

            if (entry == null)
            {
                if (string.IsNullOrWhiteSpace(prefabName) || overrideEntry.Enabled == false)
                {
                    return;
                }

                GameObject? prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefabName) : null;
                if (prefab == null)
                {
                    ValheimCreativePlugin.ModLogger.LogWarning($"Creative vegetation preset references missing prefab {prefabName}.");
                    return;
                }

                entry = new ZoneSystem.ZoneVegetation
                {
                    m_name = string.IsNullOrWhiteSpace(entryName) ? prefabName : entryName,
                    m_prefab = prefab,
                    m_biome = SourceBiome,
                    m_biomeArea = Heightmap.BiomeArea.Everything
                };
                entries.Add(entry);
            }

            ApplyFields(entry, overrideEntry);
        }

        private static bool Matches(ZoneSystem.ZoneVegetation candidate, string prefabName, string entryName)
        {
            if (!string.IsNullOrWhiteSpace(prefabName) &&
                candidate.m_prefab != null &&
                candidate.m_prefab.name.Equals(prefabName, StringComparison.Ordinal))
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(entryName) &&
                   (candidate.m_name ?? string.Empty).Equals(entryName, StringComparison.OrdinalIgnoreCase);
        }

        private static void ApplyFields(ZoneSystem.ZoneVegetation entry, CreativeVegetationEntryYaml values)
        {
            string name = values.Name ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(name))
            {
                entry.m_name = name.Trim();
            }

            string prefabName = values.Prefab ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(prefabName) && ZNetScene.instance != null)
            {
                GameObject? prefab = ZNetScene.instance.GetPrefab(prefabName.Trim());
                if (prefab != null)
                {
                    entry.m_prefab = prefab;
                }
            }

            if (values.Enabled.HasValue) entry.m_enable = values.Enabled.Value;
            if (values.Min.HasValue) entry.m_min = values.Min.Value;
            if (values.Max.HasValue) entry.m_max = values.Max.Value;
            if (values.ForcePlacement.HasValue) entry.m_forcePlacement = values.ForcePlacement.Value;
            if (values.ScaleMin.HasValue) entry.m_scaleMin = values.ScaleMin.Value;
            if (values.ScaleMax.HasValue) entry.m_scaleMax = values.ScaleMax.Value;
            if (values.RandTilt.HasValue) entry.m_randTilt = values.RandTilt.Value;
            if (values.ChanceToUseGroundTilt.HasValue) entry.m_chanceToUseGroundTilt = values.ChanceToUseGroundTilt.Value;
            string biomeValue = values.Biome ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(biomeValue) && TryParseBiomeMask(biomeValue, out Heightmap.Biome biome)) entry.m_biome = biome;
            string biomeAreaValue = values.BiomeArea ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(biomeAreaValue) && TryParseBiomeAreaMask(biomeAreaValue, out Heightmap.BiomeArea biomeArea)) entry.m_biomeArea = biomeArea;
            if (values.BlockCheck.HasValue) entry.m_blockCheck = values.BlockCheck.Value;
            if (values.SnapToStaticSolid.HasValue) entry.m_snapToStaticSolid = values.SnapToStaticSolid.Value;
            if (values.MinAltitude.HasValue) entry.m_minAltitude = values.MinAltitude.Value;
            if (values.MaxAltitude.HasValue) entry.m_maxAltitude = values.MaxAltitude.Value;
            if (values.MinVegetation.HasValue) entry.m_minVegetation = values.MinVegetation.Value;
            if (values.MaxVegetation.HasValue) entry.m_maxVegetation = values.MaxVegetation.Value;
            if (values.SurroundCheckVegetation.HasValue) entry.m_surroundCheckVegetation = values.SurroundCheckVegetation.Value;
            if (values.SurroundCheckDistance.HasValue) entry.m_surroundCheckDistance = values.SurroundCheckDistance.Value;
            if (values.SurroundCheckLayers.HasValue) entry.m_surroundCheckLayers = Mathf.Max(1, values.SurroundCheckLayers.Value);
            if (values.SurroundBetterThanAverage.HasValue) entry.m_surroundBetterThanAverage = values.SurroundBetterThanAverage.Value;
            if (values.MinOceanDepth.HasValue) entry.m_minOceanDepth = values.MinOceanDepth.Value;
            if (values.MaxOceanDepth.HasValue) entry.m_maxOceanDepth = values.MaxOceanDepth.Value;
            if (values.MinTilt.HasValue) entry.m_minTilt = values.MinTilt.Value;
            if (values.MaxTilt.HasValue) entry.m_maxTilt = values.MaxTilt.Value;
            if (values.TerrainDeltaRadius.HasValue) entry.m_terrainDeltaRadius = values.TerrainDeltaRadius.Value;
            if (values.MaxTerrainDelta.HasValue) entry.m_maxTerrainDelta = values.MaxTerrainDelta.Value;
            if (values.MinTerrainDelta.HasValue) entry.m_minTerrainDelta = values.MinTerrainDelta.Value;
            if (values.SnapToWater.HasValue) entry.m_snapToWater = values.SnapToWater.Value;
            if (values.GroundOffset.HasValue) entry.m_groundOffset = values.GroundOffset.Value;
            if (values.GroupSizeMin.HasValue) entry.m_groupSizeMin = Mathf.Max(1, values.GroupSizeMin.Value);
            if (values.GroupSizeMax.HasValue) entry.m_groupSizeMax = Mathf.Max(1, values.GroupSizeMax.Value);
            if (values.GroupRadius.HasValue) entry.m_groupRadius = values.GroupRadius.Value;
            if (values.MinDistanceFromCenter.HasValue) entry.m_minDistanceFromCenter = values.MinDistanceFromCenter.Value;
            if (values.MaxDistanceFromCenter.HasValue) entry.m_maxDistanceFromCenter = values.MaxDistanceFromCenter.Value;
            if (values.InForest.HasValue) entry.m_inForest = values.InForest.Value;
            if (values.ForestTresholdMin.HasValue) entry.m_forestTresholdMin = values.ForestTresholdMin.Value;
            if (values.ForestTresholdMax.HasValue) entry.m_forestTresholdMax = values.ForestTresholdMax.Value;

            if (entry.m_groupSizeMax < entry.m_groupSizeMin)
            {
                entry.m_groupSizeMax = entry.m_groupSizeMin;
            }
        }

        private static bool TryParseBiomeMask(string raw, out Heightmap.Biome biome)
        {
            biome = Heightmap.Biome.None;
            foreach (string part in SplitMask(raw))
            {
                if (part.Equals("All", StringComparison.OrdinalIgnoreCase))
                {
                    biome = Heightmap.Biome.All;
                    return true;
                }

                if (!CreativeBiomeService.TryParseBiome(part, out Heightmap.Biome parsed))
                {
                    return false;
                }

                biome |= parsed;
            }

            return biome != Heightmap.Biome.None;
        }

        private static bool TryParseBiomeAreaMask(string raw, out Heightmap.BiomeArea biomeArea)
        {
            biomeArea = 0;
            foreach (string part in SplitMask(raw))
            {
                if (part.Equals("Everything", StringComparison.OrdinalIgnoreCase) ||
                    part.Equals("All", StringComparison.OrdinalIgnoreCase))
                {
                    biomeArea = Heightmap.BiomeArea.Everything;
                    return true;
                }

                if (!Enum.TryParse(part, ignoreCase: true, out Heightmap.BiomeArea parsed))
                {
                    return false;
                }

                biomeArea |= parsed;
            }

            return biomeArea != 0;
        }

        private static IEnumerable<string> SplitMask(string raw)
        {
            return raw.Split(new[] { ',', '|', '+', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim());
        }
    }
}

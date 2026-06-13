using System;
using UnityEngine;
using ValheimCreative.Configuration;

namespace ValheimCreative.Features.Creative
{
    internal static class CreativeBiomeService
    {
        private const int ProtocolVersion = 5;
        private const string OverrideRpcName = "DiscordTools_CreativeBiomeOverride";

        internal static Heightmap.Biome DefaultBiome =>
            TryParseBiome(ModConfig.DefaultCreativeBiome.Value, out Heightmap.Biome biome)
                ? biome
                : Heightmap.Biome.Meadows;

        internal static bool TryParseBiome(string raw, out Heightmap.Biome biome)
        {
            string normalized = raw.Trim().Replace("_", string.Empty).Replace("-", string.Empty);
            if (string.Equals(normalized, "blackforest", StringComparison.OrdinalIgnoreCase))
            {
                biome = Heightmap.Biome.BlackForest;
                return true;
            }

            if (string.Equals(normalized, "ashlands", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "ashland", StringComparison.OrdinalIgnoreCase))
            {
                biome = Heightmap.Biome.AshLands;
                return true;
            }

            if (string.Equals(normalized, "deepnorth", StringComparison.OrdinalIgnoreCase))
            {
                biome = Heightmap.Biome.DeepNorth;
                return true;
            }

            return Enum.TryParse(raw.Trim(), ignoreCase: true, out biome) &&
                   biome != Heightmap.Biome.None &&
                   biome != Heightmap.Biome.All;
        }

        internal static bool TryParseBiomeSelection(string raw, out Heightmap.Biome biome)
        {
            string normalized = raw.Trim();
            if (normalized.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                biome = Heightmap.Biome.None;
                return true;
            }

            return TryParseBiome(raw, out biome);
        }

        internal static void SendOverride(CreativeSession session)
        {
            CreativeSessionManager.TryGetTerrainSource(session.OwnerPlayerId, out CreativeTerrainSource? terrainSource);
            Heightmap.Biome biome = terrainSource?.Biome ?? session.CreativeBiome;
            bool enabled = biome != Heightmap.Biome.None;
            SendOverride(
                session.PeerId,
                session.SlotId,
                session.CreativePosition,
                session.ZoneRadius,
                biome,
                enabled,
                suppressSpawns: session.OwnerPlayerId != 0L,
                terrainSource);
        }

        internal static void ClearOverride(long peerId, CreativeSession session)
        {
            SendOverride(peerId, session.SlotId, session.CreativePosition, session.ZoneRadius, session.CreativeBiome, enabled: false, suppressSpawns: false, terrainSource: null);
        }

        private static void SendOverride(
            long peerId,
            string slotId,
            Vector3 center,
            float radius,
            Heightmap.Biome biome,
            bool enabled,
            bool suppressSpawns,
            CreativeTerrainSource? terrainSource)
        {
            if (peerId == 0L || ZRoutedRpc.instance == null)
            {
                return;
            }

            ZPackage package = new();
            package.Write(ProtocolVersion);
            package.Write(1);
            package.Write(slotId);
            package.Write(enabled);
            package.Write(center);
            package.Write(Mathf.Max(1f, radius));
            package.Write((int)biome);
            package.Write(suppressSpawns);
            package.Write(terrainSource != null);
            package.Write(terrainSource?.Center ?? Vector3.zero);
            package.Write(GetTerrainPatchHalfSize(center, terrainSource));
            package.Write(ModConfig.CreativeTerrainEdgeFalloffWidthValue);
            package.Write(ModConfig.CreativeTerrainEdgeFloorHeight.Value);
            package.Write(GetSuppressVegetationDrops(center));
            ZRoutedRpc.instance.InvokeRoutedRPC(peerId, OverrideRpcName, package);
        }

        private static float GetTerrainPatchHalfSize(Vector3 center, CreativeTerrainSource? terrainSource)
        {
            if (terrainSource == null ||
                !CreativeSessionManager.TryGetCreativeZoneAtPosition(center, out CreativeZone? zone) ||
                zone == null)
            {
                return 0f;
            }

            return CreativeSessionManager.GetCreativeTerrainPatchHalfSize(zone);
        }

        private static bool GetSuppressVegetationDrops(Vector3 center)
        {
            if (!CreativeSessionManager.TryGetCreativeZoneAtPosition(center, out CreativeZone? zone) ||
                zone == null)
            {
                return false;
            }

            CreativeEnvironmentSettings settings = CreativeEnvironmentPolicy.Resolve(zone);
            return !settings.VegetationDropsEnabled;
        }
    }
}

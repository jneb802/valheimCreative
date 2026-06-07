using System;
using UnityEngine;
using ValheimCreative.Configuration;

namespace ValheimCreative.Features.Creative
{
    internal static class CreativeBiomeService
    {
        private const int ProtocolVersion = 1;
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

        internal static void SendOverride(CreativeSession session)
        {
            SendOverride(session.PeerId, session.SlotId, session.CreativePosition, session.ZoneRadius, session.CreativeBiome, enabled: true, suppressSpawns: session.OwnerPlayerId != 0L);
        }

        internal static void ClearOverride(long peerId, CreativeSession session)
        {
            SendOverride(peerId, session.SlotId, session.CreativePosition, session.ZoneRadius, session.CreativeBiome, enabled: false, suppressSpawns: false);
        }

        private static void SendOverride(long peerId, string slotId, Vector3 center, float radius, Heightmap.Biome biome, bool enabled, bool suppressSpawns)
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
            ZRoutedRpc.instance.InvokeRoutedRPC(peerId, OverrideRpcName, package);
        }
    }
}

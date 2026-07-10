using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ValheimCreative.Features.Creative
{
    internal static class CreativeTerrainSourcePatches
    {
        private static bool _samplingSourceTerrain;

        private static bool TryMap(float x, float z, out Vector2 source, out WorldGenerator? generator)
        {
            source = Vector2.zero;
            generator = null;
            if (_samplingSourceTerrain ||
                !CreativeVegetationService.TryMapToTerrainSource(x, z, out source, out CreativeTerrainSource? terrainSource) ||
                terrainSource == null)
            {
                return false;
            }

            generator = CreativeTerrainWorldGenerator.Get(terrainSource);
            return generator != null;
        }

        [HarmonyPatch(typeof(WorldGenerator), nameof(WorldGenerator.GetBiome), typeof(float), typeof(float), typeof(float), typeof(bool))]
        private static class WorldGeneratorGetBiomePatch
        {
            private static bool Prefix(float wx, float wy, ref Heightmap.Biome __result)
            {
                if (!TryMap(wx, wy, out Vector2 source, out WorldGenerator? generator) || generator == null)
                {
                    return true;
                }

                _samplingSourceTerrain = true;
                try
                {
                    __result = generator.GetBiome(source.x, source.y);
                }
                finally
                {
                    _samplingSourceTerrain = false;
                }

                return false;
            }
        }

        [HarmonyPatch(typeof(Heightmap), nameof(Heightmap.GetBiome), typeof(Vector3), typeof(float), typeof(bool))]
        private static class HeightmapGetBiomePatch
        {
            private static bool Prefix(Vector3 point, ref Heightmap.Biome __result)
            {
                if (!TryMap(point.x, point.z, out Vector2 source, out WorldGenerator? generator) || generator == null)
                {
                    return true;
                }

                _samplingSourceTerrain = true;
                try
                {
                    __result = generator.GetBiome(source.x, source.y);
                }
                finally
                {
                    _samplingSourceTerrain = false;
                }

                return false;
            }
        }

        [HarmonyPatch(typeof(WorldGenerator), nameof(WorldGenerator.GetBiomeHeight))]
        private static class WorldGeneratorGetBiomeHeightPatch
        {
            private static void Prefix(ref Heightmap.Biome biome, ref float wx, ref float wy)
            {
                if (!TryMap(wx, wy, out Vector2 source, out WorldGenerator? generator) || generator == null)
                {
                    return;
                }

                _samplingSourceTerrain = true;
                try
                {
                    biome = generator.GetBiome(source.x, source.y);
                }
                finally
                {
                    _samplingSourceTerrain = false;
                }

                wx = source.x;
                wy = source.y;
            }
        }

        [HarmonyPatch(typeof(WorldGenerator), nameof(WorldGenerator.GetHeight), typeof(float), typeof(float))]
        private static class WorldGeneratorGetHeightPatch
        {
            private static bool Prefix(float wx, float wy, ref float __result)
            {
                if (!TryMap(wx, wy, out Vector2 source, out WorldGenerator? generator) || generator == null)
                {
                    return true;
                }

                _samplingSourceTerrain = true;
                try
                {
                    __result = generator.GetHeight(source.x, source.y);
                }
                finally
                {
                    _samplingSourceTerrain = false;
                }

                return false;
            }
        }

        [HarmonyPatch]
        private static class WorldGeneratorGetHeightWithMaskPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    typeof(WorldGenerator),
                    nameof(WorldGenerator.GetHeight),
                    new[] { typeof(float), typeof(float), typeof(Color).MakeByRefType() });
            }

            private static bool Prefix(float wx, float wy, ref Color mask, ref float __result)
            {
                if (!TryMap(wx, wy, out Vector2 source, out WorldGenerator? generator) || generator == null)
                {
                    return true;
                }

                _samplingSourceTerrain = true;
                try
                {
                    __result = generator.GetHeight(source.x, source.y, out mask);
                }
                finally
                {
                    _samplingSourceTerrain = false;
                }

                return false;
            }
        }

        [HarmonyPatch(typeof(WorldGenerator), nameof(WorldGenerator.GetForestFactor))]
        private static class WorldGeneratorGetForestFactorPatch
        {
            private static bool Prefix(Vector3 pos, ref float __result)
            {
                if (!TryMap(pos.x, pos.z, out Vector2 source, out _))
                {
                    return true;
                }

                _samplingSourceTerrain = true;
                try
                {
                    __result = WorldGenerator.GetForestFactor(new Vector3(source.x, pos.y, source.y));
                }
                finally
                {
                    _samplingSourceTerrain = false;
                }

                return false;
            }
        }
    }
}

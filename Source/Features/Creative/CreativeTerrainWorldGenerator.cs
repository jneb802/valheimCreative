using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace ValheimCreative.Features.Creative
{
    internal static class CreativeTerrainWorldGenerator
    {
        private static readonly ConstructorInfo? WorldGeneratorConstructor =
            AccessTools.Constructor(typeof(WorldGenerator), new[] { typeof(World) });

        private static readonly Dictionary<string, WorldGenerator> Generators = new(StringComparer.Ordinal);

        internal static WorldGenerator? Get(CreativeTerrainSource source)
        {
            if (WorldGenerator.instance != null && WorldGenerator.instance.GetSeed() == source.WorldSeed)
            {
                return WorldGenerator.instance;
            }

            string key = string.IsNullOrWhiteSpace(source.WorldSeedName)
                ? source.WorldSeed.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : source.WorldSeedName.Trim();
            if (Generators.TryGetValue(key, out WorldGenerator generator))
            {
                return generator;
            }

            if (WorldGeneratorConstructor == null)
            {
                return WorldGenerator.instance;
            }

            World world = CreateWorld(source);
            generator = (WorldGenerator)WorldGeneratorConstructor.Invoke(new object[] { world });
            Generators[key] = generator;
            return generator;
        }

        private static World CreateWorld(CreativeTerrainSource source)
        {
            string seedName = source.WorldSeedName ?? string.Empty;
            World world = string.IsNullOrWhiteSpace(seedName)
                ? new World()
                : new World($"creative_source_{source.WorldSeed}", seedName);

            world.m_fileName = world.m_name = $"creative_source_{source.WorldSeed}";
            world.m_seedName = seedName;
            world.m_seed = source.WorldSeed;
            world.m_worldGenVersion = Version.m_worldGenVersion;
            return world;
        }
    }
}

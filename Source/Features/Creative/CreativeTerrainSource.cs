using UnityEngine;

namespace ValheimCreative.Features.Creative
{
    internal sealed class CreativeTerrainSource
    {
        internal CreativeTerrainSource(
            Vector3 center,
            Heightmap.Biome biome,
            int worldSeed,
            string worldSeedName)
        {
            Center = center;
            Biome = biome;
            WorldSeed = worldSeed;
            WorldSeedName = worldSeedName;
        }

        internal Vector3 Center { get; }
        internal Heightmap.Biome Biome { get; }
        internal int WorldSeed { get; }
        internal string WorldSeedName { get; }
    }
}

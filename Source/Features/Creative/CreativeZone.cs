using UnityEngine;

namespace ValheimCreative.Features.Creative
{
    internal sealed class CreativeZone
    {
        internal long OwnerPlayerId { get; }
        internal string OwnerPlayerName { get; set; }
        internal int SlotIndex { get; }
        internal string SlotId { get; }
        internal Vector3 Position { get; set; }
        internal Heightmap.Biome Biome { get; set; }
        internal float Radius { get; set; }
        internal CreativeTerrainMode TerrainMode { get; set; }
        internal CreativeTerrainSource? TerrainSource { get; set; }
        internal string PoiName { get; set; }
        internal string InviteCode => CreateInviteCode(OwnerPlayerId, SlotId);

        internal CreativeZone(
            long ownerPlayerId,
            string ownerPlayerName,
            int slotIndex,
            string slotId,
            Vector3 position,
            Heightmap.Biome biome,
            float radius,
            CreativeTerrainMode terrainMode = CreativeTerrainMode.FlatPad,
            CreativeTerrainSource? terrainSource = null,
            string poiName = "")
        {
            OwnerPlayerId = ownerPlayerId;
            OwnerPlayerName = ownerPlayerName;
            SlotIndex = slotIndex;
            SlotId = slotId;
            Position = position;
            Biome = biome;
            Radius = Mathf.Max(1f, radius);
            TerrainMode = terrainMode;
            TerrainSource = terrainSource;
            PoiName = poiName;
        }

        internal static string CreateInviteCode(long ownerPlayerId, string slotId)
        {
            int hash = $"{ownerPlayerId}:{slotId}".GetStableHashCode() & int.MaxValue;
            return ToBase36(hash).PadLeft(6, '0').Substring(0, 6).ToUpperInvariant();
        }

        private static string ToBase36(int value)
        {
            const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            if (value == 0)
            {
                return "0";
            }

            string result = string.Empty;
            while (value > 0)
            {
                result = alphabet[value % alphabet.Length] + result;
                value /= alphabet.Length;
            }

            return result;
        }

    }
}

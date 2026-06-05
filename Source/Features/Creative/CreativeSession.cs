using UnityEngine;

namespace ValheimCreative.Features.Creative
{
    internal sealed class CreativeSession
    {
        internal long PlayerId { get; }
        internal long PeerId { get; set; }
        internal string PlayerName { get; }
        internal long OwnerPlayerId { get; set; }
        internal string SlotId { get; }
        internal Vector3 CreativePosition { get; }
        internal Quaternion CreativeRotation { get; }
        internal Heightmap.Biome CreativeBiome { get; set; }
        internal float ZoneRadius { get; set; }
        internal Vector3 ReturnPosition { get; }
        internal Quaternion ReturnRotation { get; }
        internal bool AwaitingRespawn { get; set; }
        internal bool WasDead { get; set; }
        internal bool CreativeKeysSent { get; set; }
        internal bool GrantCreativeKeys { get; set; }

        internal CreativeSession(
            long playerId,
            long peerId,
            string playerName,
            long ownerPlayerId,
            string slotId,
            Vector3 creativePosition,
            Quaternion creativeRotation,
            Heightmap.Biome creativeBiome,
            float zoneRadius,
            Vector3 returnPosition,
            Quaternion returnRotation,
            bool grantCreativeKeys = true)
        {
            PlayerId = playerId;
            PeerId = peerId;
            PlayerName = playerName;
            OwnerPlayerId = ownerPlayerId;
            SlotId = slotId;
            CreativePosition = creativePosition;
            CreativeRotation = creativeRotation;
            CreativeBiome = creativeBiome;
            ZoneRadius = Mathf.Max(1f, zoneRadius);
            ReturnPosition = returnPosition;
            ReturnRotation = returnRotation;
            GrantCreativeKeys = grantCreativeKeys;
        }
    }
}

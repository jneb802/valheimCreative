using UnityEngine;

namespace ValheimCreative.Features.Creative
{
    internal static class CreativeSiegePortalRpc
    {
        private const int CurrentProtocolVersion = 2;
        private const int LegacyProtocolVersion = 1;
        private const string PortalEnterRpcName = "DiscordTools_SiegePortalEnter";
        private static ZRoutedRpc? _registeredRpc;

        internal static void RegisterRoutedRpcHandler()
        {
            if (ZRoutedRpc.instance == null || ReferenceEquals(_registeredRpc, ZRoutedRpc.instance))
            {
                return;
            }

            _registeredRpc = ZRoutedRpc.instance;
            ZRoutedRpc.instance.Register<ZPackage>(PortalEnterRpcName, OnPortalEnter);
            ValheimCreativePlugin.ModLogger.LogInfo("Registered siege portal RPC handler.");
        }

        private static void OnPortalEnter(long senderPeerId, ZPackage package)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer())
            {
                return;
            }

            try
            {
                int version = package.ReadInt();
                if (version != LegacyProtocolVersion && version != CurrentProtocolVersion)
                {
                    ValheimCreativePlugin.ModLogger.LogWarning($"Ignoring siege portal RPC version {version}; expected {LegacyProtocolVersion} or {CurrentProtocolVersion}.");
                    return;
                }

                ZDOID characterId = package.ReadZDOID();
                string siegeId = package.ReadString();
                Vector3 entryPosition = version >= CurrentProtocolVersion ? package.ReadVector3() : Vector3.zero;
                ZDO? playerZdo = CreativeSessionManager.FindPlayerZdo(characterId);
                if (playerZdo == null)
                {
                    ValheimCreativePlugin.ModLogger.LogWarning($"Siege portal enter failed: character ZDO {characterId} was not found.");
                    return;
                }

                long owner = playerZdo.GetOwner();
                if (owner != 0L && owner != senderPeerId)
                {
                    ValheimCreativePlugin.ModLogger.LogWarning($"Siege portal enter failed: sender {senderPeerId} does not own character ZDO {characterId}.");
                    return;
                }

                foreach (string line in CreativeSiegeService.EnterSiege(senderPeerId, playerZdo, siegeId, entryPosition))
                {
                    ValheimCreativePlugin.ModLogger.LogInfo($"Siege portal {siegeId}: {line}");
                }
            }
            catch (System.Exception ex)
            {
                ValheimCreativePlugin.ModLogger.LogWarning($"Failed to handle siege portal enter RPC: {ex}");
            }
        }
    }
}

using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimCreative.Features.Creative
{
    internal static class CreativeCommandZoneGuard
    {
        internal const string StateRpcName = "PraetorisClient_CreativeCommandZoneState";
        internal const int ProtocolVersion = 1;
        internal const string DeniedMessage = "Creative commands can only be used inside your creative zone.";
        private static readonly Dictionary<long, string> SentStateKeyByPeerId = new();

        internal static void Initialize()
        {
            CreativeCommandGuardPolicy.Initialize();
            SentStateKeyByPeerId.Clear();
        }

        internal static void Update()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || ZDOMan.instance == null)
            {
                return;
            }

            bool policyChanged = CreativeCommandGuardPolicy.Update();
            if (policyChanged)
            {
                SentStateKeyByPeerId.Clear();
            }

            SyncPolicyToReadyPeers();
        }

        private static void SyncPolicyToReadyPeers()
        {
            PruneDisconnectedPeers();

            foreach (ZNetPeer peer in ZNet.instance.m_peers)
            {
                if (peer == null || !peer.IsReady() || peer.m_characterID.IsNone())
                {
                    continue;
                }

                ZDO playerZdo = ZDOMan.instance.GetZDO(peer.m_characterID);
                if (playerZdo == null)
                {
                    continue;
                }

                long playerId = CreativeSessionManager.GetPlayerId(playerZdo);
                CreativeSession? session = CreativeSessionManager.GetSession(playerId);
                if (session == null)
                {
                    SendState(peer.m_uid, enabled: false, Vector3.zero, 0f, 0L, playerId, string.Empty);
                    continue;
                }

                session.PeerId = CreativeSessionManager.ResolvePeerId(playerZdo, session.PeerId);
                SendState(session);
            }
        }

        internal static bool IsProtectedCommand(string rawCommand)
        {
            string normalized = NormalizeCommand(rawCommand);
            return CreativeCommandGuardPolicy.IsProtectedCommand(normalized);
        }

        internal static void SendState(CreativeSession session)
        {
            SendState(
                session.PeerId,
                enabled: true,
                session.CreativePosition,
                session.ZoneRadius,
                session.OwnerPlayerId,
                session.PlayerId,
                session.SlotId);
        }

        internal static void ClearState(long peerId)
        {
            SendState(peerId, enabled: false, Vector3.zero, 0f, 0L, 0L, string.Empty);
        }

        private static void SendState(
            long peerId,
            bool enabled,
            Vector3 center,
            float radius,
            long ownerPlayerId,
            long playerId,
            string slotId)
        {
            if (peerId == 0L || ZRoutedRpc.instance == null)
            {
                return;
            }

            string stateKey = BuildStateKey(enabled, center, radius, ownerPlayerId, playerId, slotId);
            if (SentStateKeyByPeerId.TryGetValue(peerId, out string sentStateKey) &&
                sentStateKey == stateKey)
            {
                return;
            }

            ZPackage package = new();
            package.Write(ProtocolVersion);
            package.Write(enabled);
            package.Write(center);
            package.Write(Mathf.Max(0f, radius));
            package.Write(ownerPlayerId);
            package.Write(playerId);
            package.Write(slotId ?? string.Empty);
            package.Write(CreativeCommandGuardPolicy.Enabled);
            package.Write(CreativeCommandGuardPolicy.CommandPrefixPayload);
            ZRoutedRpc.instance.InvokeRoutedRPC(peerId, StateRpcName, package);
            SentStateKeyByPeerId[peerId] = stateKey;
        }

        private static void PruneDisconnectedPeers()
        {
            HashSet<long> connectedPeerIds = new();
            foreach (ZNetPeer peer in ZNet.instance.m_peers)
            {
                if (peer != null)
                {
                    connectedPeerIds.Add(peer.m_uid);
                }
            }

            List<long> stalePeerIds = new();
            foreach (long peerId in SentStateKeyByPeerId.Keys)
            {
                if (!connectedPeerIds.Contains(peerId))
                {
                    stalePeerIds.Add(peerId);
                }
            }

            foreach (long peerId in stalePeerIds)
            {
                SentStateKeyByPeerId.Remove(peerId);
            }
        }

        private static string BuildStateKey(
            bool enabled,
            Vector3 center,
            float radius,
            long ownerPlayerId,
            long playerId,
            string slotId)
        {
            return string.Join(
                "|",
                CreativeCommandGuardPolicy.PolicyKey,
                enabled ? "1" : "0",
                center.x.ToString("G9", System.Globalization.CultureInfo.InvariantCulture),
                center.y.ToString("G9", System.Globalization.CultureInfo.InvariantCulture),
                center.z.ToString("G9", System.Globalization.CultureInfo.InvariantCulture),
                Mathf.Max(0f, radius).ToString("G9", System.Globalization.CultureInfo.InvariantCulture),
                ownerPlayerId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                playerId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                slotId ?? string.Empty,
                CreativeCommandGuardPolicy.Enabled ? "1" : "0",
                CreativeCommandGuardPolicy.CommandPrefixPayload);
        }

        private static string NormalizeCommand(string rawCommand)
        {
            if (string.IsNullOrWhiteSpace(rawCommand))
            {
                return string.Empty;
            }

            string[] parts = rawCommand.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 0 ? string.Empty : parts[0].ToLowerInvariant();
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.RPC_RemoteCommand))]
        private static class ZNetRpcRemoteCommandPatch
        {
            [HarmonyPriority(Priority.High)]
            private static bool Prefix(ZNet __instance, ZRpc rpc, string command)
            {
                if (__instance == null ||
                    !__instance.IsServer() ||
                    rpc == null ||
                    !IsProtectedCommand(command))
                {
                    return true;
                }

                ZNetPeer peer = __instance.GetPeer(rpc);
                if (peer == null ||
                    peer.m_characterID.IsNone() ||
                    ZDOMan.instance == null)
                {
                    __instance.RemotePrint(rpc, DeniedMessage);
                    return false;
                }

                ZDO playerZdo = ZDOMan.instance.GetZDO(peer.m_characterID);
                if (playerZdo == null || !CreativeSessionManager.IsPlayerInsideActiveCreativeZone(playerZdo))
                {
                    __instance.RemotePrint(rpc, DeniedMessage);
                    return false;
                }

                return true;
            }
        }
    }
}

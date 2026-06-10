using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using ValheimCreative.Configuration;

namespace ValheimCreative.Features.Creative
{
    internal static class CreativeCommandZoneGuard
    {
        internal const string StateRpcName = "PraetorisClient_CreativeCommandZoneState";
        internal const int ProtocolVersion = 1;
        internal const string DeniedMessage = "Creative commands can only be used inside your creative zone.";

        internal static bool IsProtectedCommand(string rawCommand)
        {
            if (!ModConfig.EnableCreativeCommandZoneGuard.Value)
            {
                return false;
            }

            string normalized = NormalizeCommand(rawCommand);
            if (normalized.Length == 0)
            {
                return false;
            }

            foreach (string protectedCommand in GetProtectedCommandPrefixes())
            {
                if (normalized.StartsWith(protectedCommand, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
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

            ZPackage package = new();
            package.Write(ProtocolVersion);
            package.Write(enabled);
            package.Write(center);
            package.Write(Mathf.Max(0f, radius));
            package.Write(ownerPlayerId);
            package.Write(playerId);
            package.Write(slotId ?? string.Empty);
            ZRoutedRpc.instance.InvokeRoutedRPC(peerId, StateRpcName, package);
        }

        private static IEnumerable<string> GetProtectedCommandPrefixes()
        {
            return ModConfig.CreativeCommandZoneProtectedCommands.Value
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim().ToLowerInvariant())
                .Where(value => value.Length > 0);
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

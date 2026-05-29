using System;
using System.Collections.Generic;
using UnityEngine;
using ValheimCreative.Configuration;

namespace ValheimCreative.Features.Creative
{
    internal static class CreativeInventoryGate
    {
        private const int ProtocolVersion = 1;
        private const string RequestRpcName = "DiscordTools_CreativeInventoryRequest";
        private const string ResponseRpcName = "DiscordTools_CreativeInventoryResponse";

        private static readonly Dictionary<string, PendingInventoryCommand> PendingByRequestId = new();
        private static readonly Dictionary<long, string> PendingRequestByPlayerId = new();
        private static ZRoutedRpc? _registeredRpc;

        internal static void RegisterRoutedRpcHandler()
        {
            if (ZRoutedRpc.instance == null || ReferenceEquals(_registeredRpc, ZRoutedRpc.instance))
            {
                return;
            }

            _registeredRpc = ZRoutedRpc.instance;
            ZRoutedRpc.instance.Register<ZPackage>(ResponseRpcName, OnInventoryResponse);
            LogDebug("Registered creative inventory response RPC handler.");
        }

        internal static void Update()
        {
            RegisterRoutedRpcHandler();

            if (PendingByRequestId.Count == 0)
            {
                return;
            }

            float now = Time.time;
            List<string> expired = new();
            foreach (PendingInventoryCommand pending in PendingByRequestId.Values)
            {
                if (now >= pending.ExpiresAt)
                {
                    expired.Add(pending.RequestId);
                }
            }

            foreach (string requestId in expired)
            {
                if (!PendingByRequestId.TryGetValue(requestId, out PendingInventoryCommand pending))
                {
                    continue;
                }

                RemovePending(pending);
                CreativeChatCommands.SendPrivateLine(
                    pending.PeerId,
                    pending.MessagePosition,
                    pending.Requester,
                    "Could not verify your inventory. Try again in a few seconds.");
                ValheimCreativePlugin.ModLogger.LogWarning(
                    $"Creative inventory check timed out for {pending.PlayerName} ({pending.PlayerId}) command={pending.Command} request={pending.RequestId}.");
            }
        }

        internal static bool RequiresEmptyInventory(CreativeCommand command)
        {
            return ModConfig.RequireEmptyInventory.Value &&
                   (command == CreativeCommand.Enter ||
                    command == CreativeCommand.Return ||
                    command == CreativeCommand.Join);
        }

        internal static string Begin(
            CreativeCommand command,
            long senderPeerId,
            ZDO playerZdo,
            string fallbackName,
            string inviteCode,
            UserInfo requester,
            Vector3 messagePosition)
        {
            RegisterRoutedRpcHandler();

            if (ZRoutedRpc.instance == null || ZNet.instance == null || !ZNet.instance.IsServer())
            {
                return "Server inventory verification is not ready yet.";
            }

            long playerId = playerZdo.GetLong(ZDOVars.s_playerID);
            if (playerId == 0L)
            {
                return "Could not identify your character.";
            }

            if (PendingRequestByPlayerId.ContainsKey(playerId))
            {
                return "Inventory check is already pending.";
            }

            long targetPeerId = playerZdo.GetOwner() != 0L ? playerZdo.GetOwner() : senderPeerId;
            if (targetPeerId == 0L)
            {
                return "Could not verify your inventory because your client connection was not found.";
            }

            string requestId = Guid.NewGuid().ToString("N");
            PendingInventoryCommand pending = new(
                requestId,
                command,
                targetPeerId,
                playerId,
                playerZdo.m_uid,
                fallbackName,
                inviteCode,
                requester,
                messagePosition,
                Time.time + Mathf.Max(1f, ModConfig.InventoryCheckTimeoutSeconds.Value));

            PendingByRequestId[requestId] = pending;
            PendingRequestByPlayerId[playerId] = requestId;

            ZPackage pkg = new();
            pkg.Write(ProtocolVersion);
            pkg.Write(requestId);
            pkg.Write(playerZdo.m_uid);
            pkg.Write(true);
            ZRoutedRpc.instance.InvokeRoutedRPC(targetPeerId, RequestRpcName, pkg);

            LogDebug($"Sent creative inventory check to {fallbackName} ({playerId}) command={command} request={requestId}.");
            return "Checking inventory...";
        }

        private static void OnInventoryResponse(long sender, ZPackage pkg)
        {
            try
            {
                int version = pkg.ReadInt();
                string requestId = pkg.ReadString();
                ZDOID characterId = pkg.ReadZDOID();
                bool available = pkg.ReadBool();
                string error = pkg.ReadString();
                long playerId = pkg.ReadLong();
                string playerName = pkg.ReadString();
                int playerInventoryCount = pkg.ReadInt();
                bool extraSlotsLoaded = pkg.ReadBool();
                bool extraSlotsAvailable = pkg.ReadBool();
                int extraSlotsCount = pkg.ReadInt();
                int totalUniqueCount = pkg.ReadInt();
                int itemEntryCount = pkg.ReadInt();
                List<string> itemSummary = ReadItemSummary(pkg, itemEntryCount);

                if (version != ProtocolVersion)
                {
                    ValheimCreativePlugin.ModLogger.LogWarning($"Unsupported creative inventory response protocol {version}.");
                    return;
                }

                if (!PendingByRequestId.TryGetValue(requestId, out PendingInventoryCommand pending))
                {
                    LogDebug($"Ignored unexpected creative inventory response request={requestId} sender={sender}.");
                    return;
                }

                RemovePending(pending);

                if (sender != pending.PeerId)
                {
                    Block(pending, "Could not verify your inventory because the response came from the wrong client.");
                    ValheimCreativePlugin.ModLogger.LogWarning(
                        $"Creative inventory response sender mismatch request={requestId} expected={pending.PeerId} actual={sender}.");
                    return;
                }

                if (characterId != pending.CharacterId || playerId != pending.PlayerId)
                {
                    Block(pending, "Could not verify your inventory because your character changed.");
                    ValheimCreativePlugin.ModLogger.LogWarning(
                        $"Creative inventory response character mismatch request={requestId} expected={pending.CharacterId}/{pending.PlayerId} actual={characterId}/{playerId}.");
                    return;
                }

                if (!available)
                {
                    Block(pending, "Could not verify your inventory: " + (string.IsNullOrWhiteSpace(error) ? "client inventory unavailable." : error));
                    return;
                }

                if (!extraSlotsLoaded || !extraSlotsAvailable)
                {
                    Block(pending, "Could not verify Shudnal ExtraSlots inventory.");
                    return;
                }

                if (totalUniqueCount > 0)
                {
                    Block(pending, $"Empty your inventory before using this command. Items found: {totalUniqueCount}.");
                    ValheimCreativePlugin.ModLogger.LogInfo(
                        $"Blocked creative command {pending.Command} for {playerName} ({playerId}): inventory={playerInventoryCount} extraSlots={extraSlotsCount} total={totalUniqueCount} items=[{string.Join(", ", itemSummary)}].");
                    return;
                }

                ZDO? playerZdo = CreativeSessionManager.FindPlayerZdo(pending.CharacterId);
                if (playerZdo == null)
                {
                    Block(pending, "Creative command failed: player was not found.");
                    return;
                }

                IEnumerable<string> response = CreativeChatCommands.ExecuteCommand(
                    pending.PeerId,
                    playerZdo,
                    pending.PlayerName,
                    pending.Command,
                    pending.InviteCode);
                foreach (string line in response)
                {
                    CreativeChatCommands.SendPrivateLine(pending.PeerId, playerZdo.GetPosition() + Vector3.up * 1.8f, pending.Requester, line);
                }
            }
            catch (Exception ex)
            {
                ValheimCreativePlugin.ModLogger.LogWarning($"Failed to process creative inventory response: {ex}");
            }
        }

        private static List<string> ReadItemSummary(ZPackage pkg, int itemEntryCount)
        {
            List<string> summary = new();
            for (int i = 0; i < itemEntryCount; i++)
            {
                string source = pkg.ReadString();
                string prefabName = pkg.ReadString();
                string sharedName = pkg.ReadString();
                int stack = pkg.ReadInt();
                int quality = pkg.ReadInt();
                pkg.ReadBool();
                pkg.ReadInt();
                pkg.ReadInt();

                if (summary.Count < 8)
                {
                    string name = string.IsNullOrWhiteSpace(prefabName) ? sharedName : prefabName;
                    summary.Add($"{source}:{name}x{stack}/q{quality}");
                }
            }

            return summary;
        }

        private static void Block(PendingInventoryCommand pending, string message)
        {
            CreativeChatCommands.SendPrivateLine(pending.PeerId, pending.MessagePosition, pending.Requester, message);
        }

        private static void RemovePending(PendingInventoryCommand pending)
        {
            PendingByRequestId.Remove(pending.RequestId);
            PendingRequestByPlayerId.Remove(pending.PlayerId);
        }

        private static void LogDebug(string message)
        {
            if (ModConfig.DebugLogging.Value)
            {
                ValheimCreativePlugin.ModLogger.LogInfo("[CreativeInventory] " + message);
            }
        }

        private sealed class PendingInventoryCommand
        {
            internal string RequestId { get; }
            internal CreativeCommand Command { get; }
            internal long PeerId { get; }
            internal long PlayerId { get; }
            internal ZDOID CharacterId { get; }
            internal string PlayerName { get; }
            internal string InviteCode { get; }
            internal UserInfo Requester { get; }
            internal Vector3 MessagePosition { get; }
            internal float ExpiresAt { get; }

            internal PendingInventoryCommand(
                string requestId,
                CreativeCommand command,
                long peerId,
                long playerId,
                ZDOID characterId,
                string playerName,
                string inviteCode,
                UserInfo requester,
                Vector3 messagePosition,
                float expiresAt)
            {
                RequestId = requestId;
                Command = command;
                PeerId = peerId;
                PlayerId = playerId;
                CharacterId = characterId;
                PlayerName = playerName;
                InviteCode = inviteCode;
                Requester = requester;
                MessagePosition = messagePosition;
                ExpiresAt = expiresAt;
            }
        }
    }
}

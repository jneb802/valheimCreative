using System;
using System.Collections.Generic;
using UnityEngine;
using ValheimCreative.Configuration;
using ValheimCreative.Infrastructure.Routing;

namespace ValheimCreative.Features.Creative
{
    internal static class CreativeChatCommands
    {
        private const int DirectCommandProtocolVersion = 1;
        private const string DirectCommandRpcName = "DiscordTools_CreativeCommand";
        private static readonly int SayHash = "Say".GetStableHashCode();
        private static ZRoutedRpc? _registeredDirectCommandRpc;

        internal static void RegisterRoutedRpcHandlers()
        {
            RoutedRpcDispatcher.Register("Say", HandleSay);
        }

        internal static void RegisterDirectRpcHandler()
        {
            if (ZRoutedRpc.instance == null || ReferenceEquals(_registeredDirectCommandRpc, ZRoutedRpc.instance))
            {
                return;
            }

            _registeredDirectCommandRpc = ZRoutedRpc.instance;
            ZRoutedRpc.instance.Register<ZPackage>(DirectCommandRpcName, HandleDirectCommand);
        }

        private static RoutedRpcAction HandleSay(ZRoutedRpc.RoutedRPCData rpcData)
        {
            return TryConsume(rpcData) ? RoutedRpcAction.Consume : RoutedRpcAction.Continue;
        }

        private static bool TryConsume(ZRoutedRpc.RoutedRPCData rpcData)
        {
            if (!ModConfig.EnableCreativeCommands.Value || ZNet.instance == null || !ZNet.instance.IsServer())
            {
                return false;
            }

            if (rpcData.m_methodHash != SayHash || rpcData.m_targetZDO.IsNone())
            {
                return false;
            }

            try
            {
                rpcData.m_parameters.SetPos(0);
                rpcData.m_parameters.ReadInt();
                UserInfo userInfo = new();
                userInfo.Deserialize(ref rpcData.m_parameters);
                string text = rpcData.m_parameters.ReadString();

                if (!TryParseCommand(text, out CreativeCommand command, out string inviteCode))
                {
                    return false;
                }

                ZDO? playerZdo = CreativeSessionManager.FindPlayerZdo(rpcData.m_targetZDO);
                if (playerZdo == null)
                {
                    ValheimCreativePlugin.ModLogger.LogWarning(
                        $"Creative command {command} from peer {rpcData.m_senderPeerID} user {userInfo.Name} failed: player was not found. " +
                        CreativeSessionManager.DescribePlayerLookup(rpcData.m_targetZDO));
                    SendPrivateLine(rpcData.m_senderPeerID, Vector3.zero, userInfo, "Creative command failed: player was not found.");
                    return true;
                }

                HandleCommand(rpcData.m_senderPeerID, playerZdo, userInfo, command, inviteCode);
                return true;
            }
            catch (Exception ex)
            {
                ValheimCreativePlugin.ModLogger.LogWarning($"Failed to handle creative chat command: {ex}");
                return false;
            }
        }

        private static void HandleDirectCommand(long sender, ZPackage pkg)
        {
            if (!ModConfig.EnableCreativeCommands.Value || ZNet.instance == null || !ZNet.instance.IsServer())
            {
                return;
            }

            UserInfo userInfo = new();
            string text = string.Empty;
            try
            {
                int version = pkg.ReadInt();
                if (version != DirectCommandProtocolVersion)
                {
                    ValheimCreativePlugin.ModLogger.LogWarning($"Unsupported creative command protocol {version} from peer {sender}.");
                    return;
                }

                ZDOID characterId = pkg.ReadZDOID();
                userInfo.Deserialize(ref pkg);
                text = pkg.ReadString();

                if (!TryParseCommand(text, out CreativeCommand command, out string inviteCode))
                {
                    return;
                }

                ZDO? playerZdo = CreativeSessionManager.FindPlayerZdo(characterId);
                if (playerZdo == null)
                {
                    SendPrivateLine(sender, Vector3.zero, userInfo, "Creative command failed: player was not found.");
                    ValheimCreativePlugin.ModLogger.LogWarning(
                        $"Creative direct command {command} from peer {sender} user {userInfo.Name} failed: player was not found. " +
                        CreativeSessionManager.DescribePlayerLookup(characterId));
                    return;
                }

                long owner = playerZdo.GetOwner();
                if (owner != 0L && owner != sender)
                {
                    SendPrivateLine(sender, playerZdo.GetPosition() + Vector3.up * 1.8f, userInfo, "Creative command failed: character owner did not match your connection.");
                    ValheimCreativePlugin.ModLogger.LogWarning(
                        $"Creative direct command {command} from peer {sender} user {userInfo.Name} failed: character owner mismatch owner={owner} zdo={characterId}.");
                    return;
                }

                HandleCommand(sender, playerZdo, userInfo, command, inviteCode);
            }
            catch (Exception ex)
            {
                ValheimCreativePlugin.ModLogger.LogWarning($"Failed to handle direct creative command '{text}' from peer {sender}: {ex}");
            }
        }

        private static void HandleCommand(long peerId, ZDO playerZdo, UserInfo userInfo, CreativeCommand command, string inviteCode)
        {
            Vector3 position = playerZdo.GetPosition() + Vector3.up * 1.8f;
            IEnumerable<string> response;
            if (CreativeInventoryGate.RequiresEmptyInventory(command))
            {
                response = new[]
                {
                    CreativeInventoryGate.Begin(
                        command,
                        peerId,
                        playerZdo,
                        userInfo.Name,
                        inviteCode,
                        userInfo,
                        position)
                };
            }
            else
            {
                response = ExecuteCommand(peerId, playerZdo, userInfo.Name, command, inviteCode);
            }

            foreach (string line in response)
            {
                SendPrivateLine(peerId, position, userInfo, line);
            }
        }

        internal static IEnumerable<string> ExecuteCommand(
            long peerId,
            ZDO playerZdo,
            string fallbackName,
            CreativeCommand command,
            string inviteCode)
        {
            return command switch
            {
                CreativeCommand.Enter => CreativeSessionManager.EnterCreative(peerId, playerZdo, fallbackName),
                CreativeCommand.Return => CreativeSessionManager.ReturnFromCreative(peerId, playerZdo),
                CreativeCommand.Status => CreativeSessionManager.GetStatus(playerZdo),
                CreativeCommand.Invite => CreativeSessionManager.GetInvite(playerZdo),
                CreativeCommand.Join => CreativeSessionManager.JoinCreative(peerId, playerZdo, inviteCode, fallbackName),
                _ => Array.Empty<string>()
            };
        }

        private static bool TryParseCommand(string text, out CreativeCommand command, out string inviteCode)
        {
            command = CreativeCommand.None;
            inviteCode = string.Empty;
            string trimmed = text.Trim();
            string creative = ModConfig.CreativeCommand.Value.Trim();
            string ret = ModConfig.ReturnCommand.Value.Trim();

            if (trimmed.Equals(creative, StringComparison.OrdinalIgnoreCase))
            {
                command = CreativeCommand.Enter;
                return true;
            }

            if (trimmed.Equals(ret, StringComparison.OrdinalIgnoreCase))
            {
                command = CreativeCommand.Return;
                return true;
            }

            if (trimmed.Equals(creative + " status", StringComparison.OrdinalIgnoreCase))
            {
                command = CreativeCommand.Status;
                return true;
            }

            if (trimmed.Equals(creative + " invite", StringComparison.OrdinalIgnoreCase))
            {
                command = CreativeCommand.Invite;
                return true;
            }

            string joinPrefix = creative + " join ";
            if (trimmed.StartsWith(joinPrefix, StringComparison.OrdinalIgnoreCase))
            {
                inviteCode = trimmed.Substring(joinPrefix.Length).Trim();
                if (inviteCode.Length == 0)
                {
                    command = CreativeCommand.None;
                    return false;
                }

                command = CreativeCommand.Join;
                return true;
            }

            return false;
        }

        internal static void SendPrivateLine(long targetPeerId, Vector3 position, UserInfo requester, string line)
        {
            ZRoutedRpc.instance?.InvokeRoutedRPC(
                targetPeerId,
                "ChatMessage",
                position,
                (int)Talker.Type.Normal,
                requester,
                "[Creative] " + line);
        }

    }
}

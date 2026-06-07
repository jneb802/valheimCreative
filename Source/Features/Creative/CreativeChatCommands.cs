using System;
using System.Collections.Generic;
using UnityEngine;
using ValheimCreative.Configuration;

namespace ValheimCreative.Features.Creative
{
    internal static class CreativeChatCommands
    {
        private static readonly int SayHash = "Say".GetStableHashCode();
        private const float DefaultCommandDedupeSeconds = 1f;
        private const float SlowCommandDedupeSeconds = 30f;
        private static readonly Dictionary<string, float> RecentCommandExpirations = new();

        internal static bool TryConsumeRoutedSay(ZRoutedRpc.RoutedRPCData rpcData)
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

                if (!TryParseCommand(text, out CreativeCommand command, out string commandArgument))
                {
                    return false;
                }

                if (IsDuplicateCommand(rpcData, text, command))
                {
                    return true;
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

                if (!IsSenderCharacter(rpcData, playerZdo))
                {
                    ValheimCreativePlugin.ModLogger.LogWarning(
                        $"Creative command {command} from peer {rpcData.m_senderPeerID} user {userInfo.Name} failed: target ZDO is not owned by sender. " +
                        CreativeSessionManager.DescribePlayerLookup(rpcData.m_targetZDO));
                    SendPrivateLine(rpcData.m_senderPeerID, Vector3.zero, userInfo, "Creative command failed: player ownership mismatch.");
                    return true;
                }

                Vector3 position = playerZdo.GetPosition() + Vector3.up * 1.8f;
                IEnumerable<string> response;
                if (CreativeInventoryGate.RequiresEmptyInventory(command))
                {
                    response = new[]
                    {
                        CreativeInventoryGate.Begin(
                            command,
                            rpcData.m_senderPeerID,
                            playerZdo,
                            userInfo.Name,
                            commandArgument,
                            userInfo,
                            position)
                    };
                }
                else
                {
                    response = ExecuteCommand(rpcData.m_senderPeerID, playerZdo, userInfo.Name, command, commandArgument);
                }

                foreach (string line in response)
                {
                    SendPrivateLine(rpcData.m_senderPeerID, position, userInfo, line);
                }

                return true;
            }
            catch (Exception ex)
            {
                ValheimCreativePlugin.ModLogger.LogWarning($"Failed to handle creative chat command: {ex}");
                return false;
            }
            finally
            {
                rpcData.m_parameters.SetPos(0);
            }
        }

        private static bool IsDuplicateCommand(ZRoutedRpc.RoutedRPCData rpcData, string text, CreativeCommand command)
        {
            float now = Time.realtimeSinceStartup;
            List<string> expired = new();
            foreach (KeyValuePair<string, float> recent in RecentCommandExpirations)
            {
                if (now > recent.Value)
                {
                    expired.Add(recent.Key);
                }
            }

            foreach (string keyToRemove in expired)
            {
                RecentCommandExpirations.Remove(keyToRemove);
            }

            string key = $"{rpcData.m_senderPeerID}:{rpcData.m_targetZDO.UserID}:{rpcData.m_targetZDO.ID}:{text.Trim().ToLowerInvariant()}";
            if (RecentCommandExpirations.TryGetValue(key, out float expiresAt) && now <= expiresAt)
            {
                return true;
            }

            RecentCommandExpirations[key] = now + GetDedupeSeconds(command);
            return false;
        }

        private static float GetDedupeSeconds(CreativeCommand command)
        {
            return command == CreativeCommand.Reset ||
                   command == CreativeCommand.Load ||
                   command == CreativeCommand.Save
                ? SlowCommandDedupeSeconds
                : DefaultCommandDedupeSeconds;
        }

        internal static IEnumerable<string> ExecuteCommand(
            long peerId,
            ZDO playerZdo,
            string fallbackName,
            CreativeCommand command,
            string commandArgument)
        {
            return command switch
            {
                CreativeCommand.Enter => CreativeSessionManager.EnterCreative(peerId, playerZdo, fallbackName),
                CreativeCommand.Return => CreativeSessionManager.ReturnFromCreative(peerId, playerZdo),
                CreativeCommand.Status => CreativeSessionManager.GetStatus(playerZdo),
                CreativeCommand.Invite => CreativeSessionManager.GetInvite(playerZdo),
                CreativeCommand.Join => CreativeSessionManager.JoinCreative(peerId, playerZdo, commandArgument, fallbackName),
                CreativeCommand.Tools => CreativeSessionManager.SpawnTools(playerZdo),
                CreativeCommand.Reset => CreativeSessionManager.ResetCreativeZone(playerZdo),
                CreativeCommand.Biome => CreativeSessionManager.SetCreativeBiome(playerZdo, commandArgument),
                CreativeCommand.Load => CreativeSessionManager.LoadBlueprint(playerZdo, commandArgument),
                CreativeCommand.Save => CreativeSessionManager.SaveBlueprint(playerZdo, commandArgument),
                _ => Array.Empty<string>()
            };
        }

        private static bool TryParseCommand(string text, out CreativeCommand command, out string commandArgument)
        {
            command = CreativeCommand.None;
            commandArgument = string.Empty;
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

            if (trimmed.Equals(creative + " tools", StringComparison.OrdinalIgnoreCase))
            {
                command = CreativeCommand.Tools;
                return true;
            }

            if (trimmed.Equals(creative + " reset", StringComparison.OrdinalIgnoreCase))
            {
                command = CreativeCommand.Reset;
                return true;
            }

            if (trimmed.Equals(creative + " biome", StringComparison.OrdinalIgnoreCase))
            {
                command = CreativeCommand.Biome;
                return true;
            }

            string biomePrefix = creative + " biome ";
            if (trimmed.StartsWith(biomePrefix, StringComparison.OrdinalIgnoreCase))
            {
                commandArgument = trimmed.Substring(biomePrefix.Length).Trim();
                if (commandArgument.Length == 0)
                {
                    command = CreativeCommand.None;
                    return false;
                }

                command = CreativeCommand.Biome;
                return true;
            }

            string loadPrefix = creative + " load ";
            if (trimmed.StartsWith(loadPrefix, StringComparison.OrdinalIgnoreCase))
            {
                commandArgument = trimmed.Substring(loadPrefix.Length).Trim();
                if (commandArgument.Length == 0)
                {
                    command = CreativeCommand.None;
                    return false;
                }

                command = CreativeCommand.Load;
                return true;
            }

            string savePrefix = creative + " save ";
            if (trimmed.StartsWith(savePrefix, StringComparison.OrdinalIgnoreCase))
            {
                commandArgument = trimmed.Substring(savePrefix.Length).Trim();
                if (commandArgument.Length == 0)
                {
                    command = CreativeCommand.None;
                    return false;
                }

                command = CreativeCommand.Save;
                return true;
            }

            string joinPrefix = creative + " join ";
            if (trimmed.StartsWith(joinPrefix, StringComparison.OrdinalIgnoreCase))
            {
                commandArgument = trimmed.Substring(joinPrefix.Length).Trim();
                if (commandArgument.Length == 0)
                {
                    command = CreativeCommand.None;
                    return false;
                }

                command = CreativeCommand.Join;
                return true;
            }

            return false;
        }

        private static bool IsSenderCharacter(ZRoutedRpc.RoutedRPCData rpcData, ZDO playerZdo)
        {
            long owner = playerZdo.GetOwner();
            return owner == rpcData.m_senderPeerID;
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

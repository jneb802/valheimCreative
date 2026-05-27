using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ValheimCreative.Configuration;
using ValheimCreative.Infrastructure.Routing;

namespace ValheimCreative.Features.Creative
{
    internal static class CreativeChatCommands
    {
        private static readonly int SayHash = "Say".GetStableHashCode();

        internal static void RegisterRoutedRpcHandlers()
        {
            RoutedRpcDispatcher.Register("Say", HandleSay);
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

                if (!TryParseCommand(text, out CreativeCommand command))
                {
                    return false;
                }

                Player? player = CreativeSessionManager.FindPlayer(rpcData.m_targetZDO);
                if (player == null)
                {
                    SendPrivateLine(rpcData.m_senderPeerID, Vector3.zero, userInfo, "Creative command failed: player was not found.");
                    return true;
                }

                IEnumerable<string> response = command switch
                {
                    CreativeCommand.Enter => CreativeSessionManager.EnterCreative(rpcData.m_senderPeerID, player),
                    CreativeCommand.Return => CreativeSessionManager.ReturnFromCreative(rpcData.m_senderPeerID, player),
                    CreativeCommand.Status => CreativeSessionManager.GetStatus(player),
                    _ => Array.Empty<string>()
                };

                Vector3 position = player.transform.position + Vector3.up * 1.8f;
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
        }

        private static bool TryParseCommand(string text, out CreativeCommand command)
        {
            command = CreativeCommand.None;
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

            return false;
        }

        private static void SendPrivateLine(long targetPeerId, Vector3 position, UserInfo requester, string line)
        {
            ZRoutedRpc.instance?.InvokeRoutedRPC(
                targetPeerId,
                "ChatMessage",
                position,
                (int)Talker.Type.Normal,
                requester,
                "[Creative] " + line);
        }

        private enum CreativeCommand
        {
            None,
            Enter,
            Return,
            Status
        }
    }
}


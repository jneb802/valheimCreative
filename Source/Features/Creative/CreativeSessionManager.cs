using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using UnityEngine;
using ValheimCreative.Configuration;

namespace ValheimCreative.Features.Creative
{
    internal static class CreativeSessionManager
    {
        private static readonly Dictionary<long, CreativeSession> SessionsByPlayerId = new();
        private static float _nextDeathRecoveryCheck;

        internal static IEnumerable<string> EnterCreative(long peerId, Player player)
        {
            if (!IsServerReady())
            {
                return Lines("Server is not ready yet.");
            }

            long playerId = player.GetPlayerID();
            if (playerId == 0L)
            {
                return Lines("Could not identify your character.");
            }

            if (SessionsByPlayerId.TryGetValue(playerId, out CreativeSession existing))
            {
                existing.PeerId = ResolvePeerId(player, peerId);
                SendCreativeKeys(existing);
                player.TeleportTo(existing.CreativePosition, existing.CreativeRotation, true);
                Save();
                return Lines("Creative session restored.");
            }

            if (ModConfig.RequireBed.Value && !IsInBed(player))
            {
                return Lines("Lie in your bed before using !creative.");
            }

            CreativeSession session = new(
                playerId,
                ResolvePeerId(player, peerId),
                player.GetPlayerName(),
                ModConfig.CreativeSlotId.Value,
                ModConfig.CreativePositionValue,
                ModConfig.CreativeRotationValue,
                player.transform.position,
                player.transform.rotation);

            SessionsByPlayerId[playerId] = session;
            SendCreativeKeys(session);
            player.TeleportTo(session.CreativePosition, session.CreativeRotation, true);
            Save();
            LogDebug($"Started creative session for {session.PlayerName} ({session.PlayerId}).");
            return Lines("Creative build mode enabled. Use !return to leave.");
        }

        internal static IEnumerable<string> ReturnFromCreative(long peerId, Player player)
        {
            long playerId = player.GetPlayerID();
            if (!SessionsByPlayerId.TryGetValue(playerId, out CreativeSession session))
            {
                SendNormalKeys(ResolvePeerId(player, peerId));
                return Lines("You do not have an active creative session.");
            }

            session.PeerId = ResolvePeerId(player, peerId);
            SendNormalKeys(session.PeerId);
            player.TeleportTo(session.ReturnPosition, session.ReturnRotation, true);
            SessionsByPlayerId.Remove(playerId);
            Save();
            LogDebug($"Ended creative session for {session.PlayerName} ({session.PlayerId}).");
            return Lines("Creative build mode disabled.");
        }

        internal static IEnumerable<string> GetStatus(Player player)
        {
            long playerId = player.GetPlayerID();
            if (!SessionsByPlayerId.TryGetValue(playerId, out CreativeSession session))
            {
                return Lines("No active creative session.");
            }

            return Lines($"Active creative session: {session.SlotId}.");
        }

        internal static void Update()
        {
            if (!IsServerReady() || SessionsByPlayerId.Count == 0 || Time.time < _nextDeathRecoveryCheck)
            {
                return;
            }

            _nextDeathRecoveryCheck = Time.time + Mathf.Max(0.25f, ModConfig.DeathRecoveryCheckSeconds.Value);
            foreach (CreativeSession session in SessionsByPlayerId.Values.ToList())
            {
                Player? player = FindPlayer(session.PlayerId);
                if (player == null)
                {
                    continue;
                }

                session.PeerId = ResolvePeerId(player, session.PeerId);
                bool isDead = IsDead(player);

                if (isDead)
                {
                    session.WasDead = true;
                    session.AwaitingRespawn = true;
                    Save();
                    continue;
                }

                if (session.AwaitingRespawn || session.WasDead)
                {
                    SendCreativeKeys(session);
                    player.TeleportTo(session.CreativePosition, session.CreativeRotation, true);
                    session.AwaitingRespawn = false;
                    session.WasDead = false;
                    Save();
                    LogDebug($"Recovered creative session after death for {session.PlayerName} ({session.PlayerId}).");
                }
                else if (!session.CreativeKeysSent)
                {
                    SendCreativeKeys(session);
                }
            }
        }

        internal static void ResendCreativeKeysToActivePeers()
        {
            if (!IsServerReady() || SessionsByPlayerId.Count == 0)
            {
                return;
            }

            foreach (CreativeSession session in SessionsByPlayerId.Values)
            {
                Player? player = FindPlayer(session.PlayerId);
                if (player != null)
                {
                    session.PeerId = ResolvePeerId(player, session.PeerId);
                }

                SendCreativeKeys(session);
            }
        }

        internal static Player? FindPlayer(ZDOID characterId)
        {
            foreach (Player player in Player.GetAllPlayers())
            {
                if (player != null &&
                    player.m_nview != null &&
                    player.m_nview.IsValid() &&
                    player.m_nview.GetZDO().m_uid == characterId)
                {
                    return player;
                }
            }

            return null;
        }

        internal static void Load()
        {
            SessionsByPlayerId.Clear();
            string path = GetSessionPath();
            if (!File.Exists(path))
            {
                return;
            }

            foreach (string line in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                if (CreativeSession.TryDeserialize(line, out CreativeSession? session) && session != null)
                {
                    SessionsByPlayerId[session.PlayerId] = session;
                }
            }

            LogDebug($"Loaded {SessionsByPlayerId.Count} creative session(s).");
        }

        internal static void Save()
        {
            try
            {
                string path = GetSessionPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                List<string> lines = new() { "# playerId\tpeerId\tplayerName\tslotId\tcreativePosition\tcreativeRotation\treturnPosition\treturnRotation\tawaitingRespawn" };
                lines.AddRange(SessionsByPlayerId.Values.Select(session => session.Serialize()));
                File.WriteAllLines(path, lines);
            }
            catch (Exception ex)
            {
                ValheimCreativePlugin.ModLogger.LogWarning($"Failed to save creative sessions: {ex}");
            }
        }

        private static Player? FindPlayer(long playerId)
        {
            return Player.GetAllPlayers().FirstOrDefault(player => player != null && player.GetPlayerID() == playerId);
        }

        private static bool IsServerReady()
        {
            return ZNet.instance != null &&
                   ZNet.instance.IsServer() &&
                   ZRoutedRpc.instance != null &&
                   ZoneSystem.instance != null;
        }

        private static bool IsInBed(Player player)
        {
            return player.m_nview != null &&
                   player.m_nview.IsValid() &&
                   player.m_nview.GetZDO().GetBool(ZDOVars.s_inBed);
        }

        private static bool IsDead(Player player)
        {
            return player.m_nview != null &&
                   player.m_nview.IsValid() &&
                   player.m_nview.GetZDO().GetBool(ZDOVars.s_dead);
        }

        private static long ResolvePeerId(Player player, long fallback)
        {
            if (player.m_nview != null && player.m_nview.IsValid())
            {
                long owner = player.m_nview.GetZDO().GetOwner();
                if (owner != 0L)
                {
                    return owner;
                }
            }

            return fallback;
        }

        private static void SendCreativeKeys(CreativeSession session)
        {
            List<string> keys = ZoneSystem.instance.GetGlobalKeys();
            AddKey(keys, GlobalKeys.NoBuildCost);

            if (ModConfig.IncludeNoWorkbench.Value)
            {
                AddKey(keys, GlobalKeys.NoWorkbench);
            }

            if (ModConfig.IncludeNoCraftCost.Value)
            {
                AddKey(keys, GlobalKeys.NoCraftCost);
            }

            ZRoutedRpc.instance.InvokeRoutedRPC(session.PeerId, "GlobalKeys", keys);
            session.CreativeKeysSent = true;
        }

        private static void SendNormalKeys(long peerId)
        {
            ZRoutedRpc.instance.InvokeRoutedRPC(peerId, "GlobalKeys", ZoneSystem.instance.GetGlobalKeys());
        }

        private static void AddKey(List<string> keys, GlobalKeys key)
        {
            string value = key.ToString();
            if (!keys.Any(existing => existing.Equals(value, StringComparison.OrdinalIgnoreCase)))
            {
                keys.Add(value);
            }
        }

        private static IEnumerable<string> Lines(string line)
        {
            return new[] { line };
        }

        private static string GetSessionPath()
        {
            string configured = ModConfig.SessionFile.Value;
            return Path.IsPathRooted(configured)
                ? configured
                : Path.Combine(Paths.ConfigPath, configured);
        }

        private static void LogDebug(string message)
        {
            if (ModConfig.DebugLogging.Value)
            {
                ValheimCreativePlugin.ModLogger.LogInfo("[Creative] " + message);
            }
        }
    }
}


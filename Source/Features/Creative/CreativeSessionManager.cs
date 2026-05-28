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

        internal static IEnumerable<string> EnterCreative(long peerId, ZDO playerZdo, string fallbackName)
        {
            if (!IsServerReady())
            {
                return Lines("Server is not ready yet.");
            }

            long playerId = GetPlayerId(playerZdo);
            if (playerId == 0L)
            {
                return Lines("Could not identify your character.");
            }

            if (SessionsByPlayerId.TryGetValue(playerId, out CreativeSession existing))
            {
                existing.PeerId = ResolvePeerId(playerZdo, peerId);
                SendCreativeKeys(existing);
                TeleportTo(playerZdo, existing.CreativePosition, existing.CreativeRotation);
                Save();
                return Lines("Creative session restored.");
            }

            if (ModConfig.RequireBed.Value && !IsInBed(playerZdo))
            {
                return Lines("Lie in your bed before using !creative.");
            }

            CreativeSession session = new(
                playerId,
                ResolvePeerId(playerZdo, peerId),
                GetPlayerName(playerZdo, fallbackName),
                ModConfig.CreativeSlotId.Value,
                ModConfig.CreativePositionValue,
                ModConfig.CreativeRotationValue,
                playerZdo.GetPosition(),
                playerZdo.GetRotation());

            SessionsByPlayerId[playerId] = session;
            SendCreativeKeys(session);
            TeleportTo(playerZdo, session.CreativePosition, session.CreativeRotation);
            Save();
            LogDebug($"Started creative session for {session.PlayerName} ({session.PlayerId}).");
            return Lines("Creative build mode enabled. Use !return to leave.");
        }

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

        internal static IEnumerable<string> ReturnFromCreative(long peerId, ZDO playerZdo)
        {
            long playerId = GetPlayerId(playerZdo);
            if (!SessionsByPlayerId.TryGetValue(playerId, out CreativeSession session))
            {
                SendNormalKeys(ResolvePeerId(playerZdo, peerId));
                return Lines("You do not have an active creative session.");
            }

            session.PeerId = ResolvePeerId(playerZdo, peerId);
            SendNormalKeys(session.PeerId);
            TeleportTo(playerZdo, session.ReturnPosition, session.ReturnRotation);
            SessionsByPlayerId.Remove(playerId);
            Save();
            LogDebug($"Ended creative session for {session.PlayerName} ({session.PlayerId}).");
            return Lines("Creative build mode disabled.");
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

        internal static IEnumerable<string> GetStatus(ZDO playerZdo)
        {
            long playerId = GetPlayerId(playerZdo);
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
                ZDO? playerZdo = FindPlayerZdo(session.PlayerId);
                if (playerZdo == null)
                {
                    continue;
                }

                session.PeerId = ResolvePeerId(playerZdo, session.PeerId);
                bool isDead = IsDead(playerZdo);

                if (isDead)
                {
                    if (!session.WasDead || !session.AwaitingRespawn)
                    {
                        session.WasDead = true;
                        session.AwaitingRespawn = true;
                        Save();
                    }

                    continue;
                }

                if (session.AwaitingRespawn || session.WasDead)
                {
                    SendCreativeKeys(session);
                    TeleportTo(playerZdo, session.CreativePosition, session.CreativeRotation);
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
                ZDO? playerZdo = FindPlayerZdo(session.PlayerId);
                if (playerZdo != null)
                {
                    session.PeerId = ResolvePeerId(playerZdo, session.PeerId);
                }

                SendCreativeKeys(session);
            }
        }

        internal static ZDO? FindPlayerZdo(ZDOID characterId)
        {
            return ZDOMan.instance != null && !characterId.IsNone()
                ? ZDOMan.instance.GetZDO(characterId)
                : null;
        }

        internal static Player? FindPlayer(ZDOID characterId)
        {
            Player? direct = FindPlayerFromZdo(characterId);
            if (direct != null)
            {
                return direct;
            }

            ZDO? zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(characterId) : null;
            long targetOwner = zdo != null ? zdo.GetOwner() : 0L;

            foreach (Player player in Player.GetAllPlayers())
            {
                if (player == null || player.m_nview == null || !player.m_nview.IsValid())
                {
                    continue;
                }

                ZDO playerZdo = player.m_nview.GetZDO();
                if (playerZdo.m_uid == characterId ||
                    targetOwner != 0L && playerZdo.GetOwner() == targetOwner)
                {
                    return player;
                }
            }

            return null;
        }

        internal static string DescribePlayerLookup(ZDOID characterId)
        {
            ZDO? zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(characterId) : null;
            string zdoInfo = zdo == null
                ? "targetZdoFound=false"
                : $"targetZdoFound=true targetZdo={zdo.m_uid} owner={zdo.GetOwner()} prefab={DescribePrefab(zdo.GetPrefab())} position={Format(zdo.GetPosition())}";

            string instanceInfo = "instanceFound=false";
            if (zdo != null && ZNetScene.instance != null)
            {
                ZNetView instance = ZNetScene.instance.FindInstance(zdo);
                if (instance != null)
                {
                    bool hasPlayer =
                        instance.GetComponent<Player>() != null ||
                        instance.GetComponentInParent<Player>() != null ||
                        instance.GetComponentInChildren<Player>() != null;
                    instanceInfo = $"instanceFound=true instance={instance.gameObject.name} hasPlayer={hasPlayer}";
                }
            }

            List<string> players = Player.GetAllPlayers()
                .Where(player => player != null)
                .Select(DescribePlayer)
                .ToList();

            string playerInfo = players.Count == 0
                ? "players=[]"
                : "players=[" + string.Join("; ", players) + "]";

            return $"{zdoInfo} {instanceInfo} {playerInfo}";
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

        private static ZDO? FindPlayerZdo(long playerId)
        {
            if (ZNet.instance == null || ZDOMan.instance == null)
            {
                return null;
            }

            foreach (ZNetPeer peer in ZNet.instance.m_peers)
            {
                if (peer.m_characterID.IsNone())
                {
                    continue;
                }

                ZDO? zdo = ZDOMan.instance.GetZDO(peer.m_characterID);
                if (zdo != null && GetPlayerId(zdo) == playerId)
                {
                    return zdo;
                }
            }

            return null;
        }

        private static Player? FindPlayerFromZdo(ZDOID characterId)
        {
            ZDO? zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(characterId) : null;
            if (zdo == null || ZNetScene.instance == null)
            {
                return null;
            }

            ZNetView instance = ZNetScene.instance.FindInstance(zdo);
            if (instance == null)
            {
                return null;
            }

            return instance.GetComponent<Player>() ??
                   instance.GetComponentInParent<Player>() ??
                   instance.GetComponentInChildren<Player>();
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

        private static bool IsInBed(ZDO playerZdo)
        {
            return playerZdo.GetBool(ZDOVars.s_inBed);
        }

        private static bool IsDead(Player player)
        {
            return player.m_nview != null &&
                   player.m_nview.IsValid() &&
                   player.m_nview.GetZDO().GetBool(ZDOVars.s_dead);
        }

        private static bool IsDead(ZDO playerZdo)
        {
            return playerZdo.GetBool(ZDOVars.s_dead);
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

        private static long ResolvePeerId(ZDO playerZdo, long fallback)
        {
            long owner = playerZdo.GetOwner();
            return owner != 0L ? owner : fallback;
        }

        private static long GetPlayerId(ZDO playerZdo)
        {
            return playerZdo.GetLong(ZDOVars.s_playerID);
        }

        private static string GetPlayerName(ZDO playerZdo, string fallback)
        {
            string name = playerZdo.GetString(ZDOVars.s_playerName, fallback);
            return string.IsNullOrWhiteSpace(name) ? fallback : name;
        }

        private static void TeleportTo(ZDO playerZdo, Vector3 position, Quaternion rotation)
        {
            long owner = playerZdo.GetOwner();
            if (owner == 0L)
            {
                ValheimCreativePlugin.ModLogger.LogWarning($"Cannot teleport player ZDO {playerZdo.m_uid}: no owner.");
                return;
            }

            ZRoutedRpc.instance.InvokeRoutedRPC(owner, playerZdo.m_uid, "RPC_TeleportTo", position, rotation, true);
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

        private static string DescribePrefab(int prefabHash)
        {
            if (ZNetScene.instance == null)
            {
                return prefabHash.ToString();
            }

            GameObject prefab = ZNetScene.instance.GetPrefab(prefabHash);
            return prefab != null ? $"{prefab.name}/{prefabHash}" : prefabHash.ToString();
        }

        private static string DescribePlayer(Player player)
        {
            if (player.m_nview == null || !player.m_nview.IsValid())
            {
                return $"{player.GetPlayerName()} invalid-nview";
            }

            ZDO zdo = player.m_nview.GetZDO();
            return $"{player.GetPlayerName()} playerId={player.GetPlayerID()} zdo={zdo.m_uid} owner={zdo.GetOwner()} position={Format(player.transform.position)}";
        }

        private static string Format(Vector3 vector)
        {
            return $"{vector.x:F1},{vector.y:F1},{vector.z:F1}";
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

using System;
using System.Collections.Generic;
using System.Globalization;
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
        private static readonly Dictionary<long, CreativeZone> ZonesByOwnerId = new();
        private const float CreativeLocationProxyCleanupRadius = 80f;
        private const float CreativeLocationObjectCleanupRadius = 80f;
        private const float CreativeLocationObjectCleanupRetrySeconds = 1f;
        private const float CreativeLocationObjectCleanupDurationSeconds = 8f;
        private const float CreativeZoneResetRadius = 128f;
        private static readonly string[] CreativeLocationObjectCleanupPrefabs =
        {
            "BossStone_TheQueen",
            "BossStone_Fader",
            "terrain",
            "Vegvisir_Eikthyr",
            "Rock_3_static",
            "StartPlatform",
            "Branches",
            "Stones",
            "StoneSpawner_TheQueen",
            "StoneSpawner_Fader"
        };
        private static readonly string[] CreativeToolPrefabs = { "Hoe", "Hammer", "Cultivator", "PickaxeAntler" };
        private static readonly List<CreativeLocationObjectCleanup> PendingLocationObjectCleanups = new();
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
                if (!TryEnsureCreativeLocation(existing.CreativePosition, existing.SlotId, out string locationError))
                {
                    return Lines(locationError);
                }

                existing.PeerId = ResolvePeerId(playerZdo, peerId);
                SendCreativeKeys(existing);
                CreativeBiomeService.SendOverride(existing);
                TeleportTo(playerZdo, existing.CreativePosition, existing.CreativeRotation);
                Save();
                return Lines("Creative session restored.");
            }

            string playerName = GetPlayerName(playerZdo, fallbackName);
            CreativeZone zone = GetOrCreateZone(playerId, playerName);
            if (!TryEnsureCreativeLocation(zone.Position, zone.SlotId, out string newLocationError))
            {
                return Lines(newLocationError);
            }

            CreativeSession session = new(
                playerId,
                ResolvePeerId(playerZdo, peerId),
                playerName,
                playerId,
                zone.SlotId,
                zone.Position,
                ModConfig.CreativeRotationValue,
                zone.Biome,
                playerZdo.GetPosition(),
                playerZdo.GetRotation());

            SessionsByPlayerId[playerId] = session;
            SendCreativeKeys(session);
            CreativeBiomeService.SendOverride(session);
            TeleportTo(playerZdo, session.CreativePosition, session.CreativeRotation);
            Save();
            LogDebug($"Started creative session for {session.PlayerName} ({session.PlayerId}).");
            return Lines("Creative build mode enabled. Use !return to leave.");
        }

        internal static IEnumerable<string> ReturnFromCreative(long peerId, ZDO playerZdo)
        {
            long playerId = GetPlayerId(playerZdo);
            if (playerId == 0L)
            {
                return Lines("Could not identify your character.");
            }

            if (!SessionsByPlayerId.TryGetValue(playerId, out CreativeSession session))
            {
                SendNormalKeys(ResolvePeerId(playerZdo, peerId));
                return Lines("You do not have an active creative session.");
            }

            session.PeerId = ResolvePeerId(playerZdo, peerId);
            SendNormalKeys(session.PeerId);
            CreativeBiomeService.ClearOverride(session.PeerId, session);
            TeleportTo(playerZdo, session.ReturnPosition, session.ReturnRotation);
            SessionsByPlayerId.Remove(playerId);
            Save();
            LogDebug($"Ended creative session for {session.PlayerName} ({session.PlayerId}).");
            return Lines("Creative build mode disabled.");
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

        internal static IEnumerable<string> GetInvite(ZDO playerZdo)
        {
            long playerId = GetPlayerId(playerZdo);
            if (!SessionsByPlayerId.TryGetValue(playerId, out CreativeSession session))
            {
                return Lines("Use !creative before creating an invite.");
            }

            if (session.OwnerPlayerId != playerId)
            {
                return Lines("Only the creative zone owner can create an invite.");
            }

            CreativeZone zone = GetOrCreateZone(playerId, GetPlayerName(playerZdo, session.PlayerName));
            return Lines($"Invite code: {zone.InviteCode}");
        }

        internal static IEnumerable<string> JoinCreative(long peerId, ZDO playerZdo, string inviteCode, string fallbackName)
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

            if (!TryGetZoneByInviteCode(inviteCode, out CreativeZone? zone) || zone == null)
            {
                return Lines("Creative invite code was not found.");
            }

            if (!TryEnsureCreativeLocation(zone.Position, zone.SlotId, out string locationError))
            {
                return Lines(locationError);
            }

            Vector3 returnPosition = playerZdo.GetPosition();
            Quaternion returnRotation = playerZdo.GetRotation();
            if (SessionsByPlayerId.TryGetValue(playerId, out CreativeSession existing))
            {
                if (existing.OwnerPlayerId != zone.OwnerPlayerId)
                {
                    return Lines("Use !return before joining another creative zone.");
                }

                returnPosition = existing.ReturnPosition;
                returnRotation = existing.ReturnRotation;
            }

            CreativeSession session = new(
                playerId,
                ResolvePeerId(playerZdo, peerId),
                GetPlayerName(playerZdo, fallbackName),
                zone.OwnerPlayerId,
                zone.SlotId,
                zone.Position,
                ModConfig.CreativeRotationValue,
                zone.Biome,
                returnPosition,
                returnRotation);

            SessionsByPlayerId[playerId] = session;
            SendCreativeKeys(session);
            CreativeBiomeService.SendOverride(session);
            TeleportTo(playerZdo, session.CreativePosition, session.CreativeRotation);
            Save();
            LogDebug($"Joined creative session for {session.PlayerName} ({session.PlayerId}) to owner {zone.OwnerPlayerId}.");
            return Lines($"Joined {zone.OwnerPlayerName}'s creative zone. Use !return to leave.");
        }

        internal static IEnumerable<string> SpawnTools(ZDO playerZdo)
        {
            if (!IsServerReady() || ObjectDB.instance == null)
            {
                return Lines("Server item data is not ready yet.");
            }

            long playerId = GetPlayerId(playerZdo);
            if (!SessionsByPlayerId.ContainsKey(playerId))
            {
                return Lines("Use !creative before requesting creative tools.");
            }

            Vector3 position = playerZdo.GetPosition();
            string playerName = GetPlayerName(playerZdo, "Creative");
            List<string> missing = new();
            int spawned = 0;

            for (int i = 0; i < CreativeToolPrefabs.Length; i++)
            {
                string prefabName = CreativeToolPrefabs[i];
                GameObject prefab = ObjectDB.instance.GetItemPrefab(prefabName);
                ItemDrop? template = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (template == null)
                {
                    missing.Add(prefabName);
                    continue;
                }

                ItemDrop.ItemData item = template.m_itemData.Clone();
                item.m_dropPrefab = prefab;
                item.m_stack = 1;
                item.m_quality = 1;
                item.m_variant = 0;
                item.m_crafterID = playerId;
                item.m_crafterName = playerName;
                item.m_worldLevel = (int)(byte)Game.m_worldLevel;
                if (item.m_shared.m_useDurability)
                {
                    item.m_durability = item.GetMaxDurability();
                }

                Vector3 offset = new((i - 1) * 0.75f, 0.75f, 1.25f);
                ItemDrop.DropItem(item, 1, position + offset, Quaternion.identity);
                spawned++;
            }

            if (missing.Count > 0)
            {
                return Lines($"Spawned {spawned} creative tool(s). Missing: {string.Join(", ", missing)}.");
            }

            return Lines("Creative tools spawned.");
        }

        internal static IEnumerable<string> ResetCreativeZone(ZDO playerZdo)
        {
            if (!IsServerReady() || ZDOMan.instance == null)
            {
                return Lines("Server is not ready yet.");
            }

            long playerId = GetPlayerId(playerZdo);
            if (!SessionsByPlayerId.TryGetValue(playerId, out CreativeSession session))
            {
                return Lines("Use !creative before resetting a creative zone.");
            }

            if (session.OwnerPlayerId != playerId)
            {
                return Lines("Only the creative zone owner can reset it.");
            }

            PendingLocationObjectCleanups.RemoveAll(cleanup => cleanup.SlotId.Equals(session.SlotId, StringComparison.Ordinal));
            Vector2i zone = ZoneSystem.GetZone(session.CreativePosition);
            ZoneSystem.instance.m_locationInstances.Remove(zone);
            int removed = DestroyCreativeZoneZdos(session.CreativePosition);

            if (!TryEnsureCreativeLocation(session.CreativePosition, session.SlotId, out string error))
            {
                return Lines(error);
            }

            TeleportTo(playerZdo, session.CreativePosition, session.CreativeRotation);
            CreativeBiomeService.SendOverride(session);
            ValheimCreativePlugin.ModLogger.LogInfo($"Reset creative zone {session.SlotId} at {Format(session.CreativePosition)}. Removed {removed} object(s).");
            return Lines($"Creative zone reset. Removed {removed} object(s).");
        }

        internal static IEnumerable<string> SetCreativeBiome(ZDO playerZdo, string biomeName)
        {
            long playerId = GetPlayerId(playerZdo);
            if (!SessionsByPlayerId.TryGetValue(playerId, out CreativeSession session))
            {
                return Lines("Use !creative before changing a creative zone biome.");
            }

            if (string.IsNullOrWhiteSpace(biomeName))
            {
                return Lines($"Creative biome: {session.CreativeBiome}.");
            }

            if (session.OwnerPlayerId != playerId)
            {
                return Lines("Only the creative zone owner can change its biome.");
            }

            if (!CreativeBiomeService.TryParseBiome(biomeName, out Heightmap.Biome biome))
            {
                return Lines("Unknown biome. Use Meadows, BlackForest, Swamp, Mountain, Plains, Mistlands, AshLands, DeepNorth, or Ocean.");
            }

            ApplyBiomeToZone(session.OwnerPlayerId, biome);
            Save();
            ValheimCreativePlugin.ModLogger.LogInfo($"Set creative zone {session.SlotId} biome to {biome}.");
            return Lines($"Creative biome set to {biome}.");
        }

        internal static IEnumerable<string> LoadBlueprint(ZDO playerZdo, string fileName)
        {
            long playerId = GetPlayerId(playerZdo);
            if (!SessionsByPlayerId.TryGetValue(playerId, out CreativeSession session))
            {
                return Lines("Use !creative before loading a blueprint.");
            }

            if (!TryEnsureCreativeLocation(session.CreativePosition, session.SlotId, out string locationError))
            {
                return Lines(locationError);
            }

            if (!CreativeBlueprintService.TryLoadBlueprint(
                    fileName,
                    session,
                    session.OwnerPlayerId,
                    out int spawned,
                    out List<string> missingPrefabs,
                    out Heightmap.Biome? metadataBiome,
                    out string error))
            {
                return Lines(error);
            }

            if (metadataBiome.HasValue)
            {
                ApplyBiomeToZone(session.OwnerPlayerId, metadataBiome.Value);
                Save();
            }

            ValheimCreativePlugin.ModLogger.LogInfo(
                $"Loaded blueprint {fileName} into {session.SlotId} at {Format(session.CreativePosition)}. Spawned {spawned} object(s), missing {missingPrefabs.Count} prefab(s).");

            if (missingPrefabs.Count > 0)
            {
                return Lines($"Blueprint loaded. Spawned {spawned} object(s). Missing prefabs: {string.Join(", ", missingPrefabs.Take(8))}{(missingPrefabs.Count > 8 ? "..." : "")}.");
            }

            return Lines($"Blueprint loaded. Spawned {spawned} object(s).");
        }

        internal static IEnumerable<string> LoadBlueprintForPlayerId(long playerId, string fileName)
        {
            if (!IsServerReady())
            {
                return Lines("Server is not ready yet.");
            }

            string platformId = playerId.ToString(CultureInfo.InvariantCulture);
            ZDO? playerZdo = FindPlayerZdo(playerId);
            if (playerZdo == null)
            {
                playerZdo = FindPlayerZdoByPlatformId(platformId, out string matchedIdentifier);
                if (playerZdo != null)
                {
                    ValheimCreativePlugin.ModLogger.LogInfo(
                        $"Matched creative load target {playerId} to online platform identifier {matchedIdentifier}.");
                }
            }

            if (playerZdo == null)
            {
                return Lines($"Player {playerId} was not found online by Valheim player ID or platform ID.");
            }

            return LoadBlueprint(playerZdo, fileName);
        }

        internal static IEnumerable<string> SaveBlueprint(ZDO playerZdo, string fileName)
        {
            long playerId = GetPlayerId(playerZdo);
            if (!SessionsByPlayerId.TryGetValue(playerId, out CreativeSession session))
            {
                return Lines("Use !creative before saving a blueprint.");
            }

            if (session.OwnerPlayerId != playerId)
            {
                return Lines("Only the creative zone owner can save it.");
            }

            if (!CreativeBlueprintService.TrySaveBlueprint(
                    fileName,
                    session,
                    playerId,
                    GetPlayerName(playerZdo, session.PlayerName),
                    session.CreativeBiome,
                    out int saved,
                    out string error))
            {
                return Lines(error);
            }

            ValheimCreativePlugin.ModLogger.LogInfo(
                $"Saved blueprint {fileName} from {session.SlotId} at {Format(session.CreativePosition)} with {saved} piece(s).");
            return Lines($"Blueprint saved. Wrote {saved} piece(s).");
        }

        internal static void Update()
        {
            if (!IsServerReady())
            {
                return;
            }

            RunPendingLocationObjectCleanups();

            if (SessionsByPlayerId.Count == 0 || Time.time < _nextDeathRecoveryCheck)
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
                    TryEnsureCreativeLocation(session.CreativePosition, session.SlotId, out _);
                    SendCreativeKeys(session);
                    CreativeBiomeService.SendOverride(session);
                    TeleportTo(playerZdo, session.CreativePosition, session.CreativeRotation);
                    session.AwaitingRespawn = false;
                    session.WasDead = false;
                    Save();
                    LogDebug($"Recovered creative session after death for {session.PlayerName} ({session.PlayerId}).");
                }
                else if (!session.CreativeKeysSent)
                {
                    SendCreativeKeys(session);
                    CreativeBiomeService.SendOverride(session);
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
                CreativeBiomeService.SendOverride(session);
            }
        }

        internal static ZDO? FindPlayerZdo(ZDOID characterId)
        {
            return ZDOMan.instance != null && !characterId.IsNone()
                ? ZDOMan.instance.GetZDO(characterId)
                : null;
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
            ZonesByOwnerId.Clear();
            LoadZones();

            string path = GetSessionPath();
            foreach (CreativeSession session in CreativeStateStore.LoadSessions(path))
            {
                SessionsByPlayerId[session.PlayerId] = session;
                if (session.OwnerPlayerId == session.PlayerId &&
                    !ZonesByOwnerId.ContainsKey(session.PlayerId))
                {
                    int slotIndex = GetNextZoneSlotIndex();
                    ZonesByOwnerId[session.PlayerId] = new CreativeZone(
                        session.PlayerId,
                        session.PlayerName,
                        slotIndex,
                        BuildZoneSlotId(slotIndex),
                        GetZonePosition(slotIndex),
                        session.CreativeBiome);
                }
            }

            ReconcileCreativeState();
            LogDebug($"Loaded {SessionsByPlayerId.Count} creative session(s).");
            Save();
        }

        internal static void Save()
        {
            try
            {
                string path = GetSessionPath();
                CreativeStateStore.SaveSessions(path, SessionsByPlayerId.Values);
                SaveZones();
            }
            catch (Exception ex)
            {
                ValheimCreativePlugin.ModLogger.LogWarning($"Failed to save creative sessions: {ex}");
            }
        }

        private static void LoadZones()
        {
            string path = GetZonePath();
            foreach (CreativeZone zone in CreativeStateStore.LoadZones(path))
            {
                ZonesByOwnerId[zone.OwnerPlayerId] = zone;
            }

            LogDebug($"Loaded {ZonesByOwnerId.Count} creative zone allocation(s).");
        }

        private static void SaveZones()
        {
            string path = GetZonePath();
            CreativeStateStore.SaveZones(path, ZonesByOwnerId.Values.OrderBy(zone => zone.SlotIndex));
        }

        private static CreativeZone GetOrCreateZone(long ownerPlayerId, string ownerPlayerName)
        {
            if (ZonesByOwnerId.TryGetValue(ownerPlayerId, out CreativeZone zone))
            {
                zone.OwnerPlayerName = ownerPlayerName;
                return zone;
            }

            int slotIndex = GetNextZoneSlotIndex();
            string slotId = BuildZoneSlotId(slotIndex);
            zone = new CreativeZone(ownerPlayerId, ownerPlayerName, slotIndex, slotId, GetZonePosition(slotIndex), CreativeBiomeService.DefaultBiome);
            ZonesByOwnerId[ownerPlayerId] = zone;
            SaveZones();
            LogDebug($"Allocated creative zone {slotId} for {ownerPlayerName} ({ownerPlayerId}) at {Format(zone.Position)}.");
            return zone;
        }

        private static void ApplyBiomeToZone(long ownerPlayerId, Heightmap.Biome biome)
        {
            if (ZonesByOwnerId.TryGetValue(ownerPlayerId, out CreativeZone zone))
            {
                zone.Biome = biome;
            }

            foreach (CreativeSession activeSession in SessionsByPlayerId.Values)
            {
                if (activeSession.OwnerPlayerId != ownerPlayerId)
                {
                    continue;
                }

                activeSession.CreativeBiome = biome;
                CreativeBiomeService.SendOverride(activeSession);
            }
        }

        private static void ReconcileCreativeState()
        {
            foreach (CreativeZone zone in ZonesByOwnerId.Values.ToList())
            {
                ZonesByOwnerId[zone.OwnerPlayerId] = new CreativeZone(
                    zone.OwnerPlayerId,
                    zone.OwnerPlayerName,
                    zone.SlotIndex,
                    BuildZoneSlotId(zone.SlotIndex),
                    GetZonePosition(zone.SlotIndex),
                    zone.Biome);
            }

            foreach (CreativeSession session in SessionsByPlayerId.Values.ToList())
            {
                if (!ZonesByOwnerId.TryGetValue(session.OwnerPlayerId, out CreativeZone zone))
                {
                    continue;
                }

                SessionsByPlayerId[session.PlayerId] = new CreativeSession(
                    session.PlayerId,
                    session.PeerId,
                    session.PlayerName,
                    session.OwnerPlayerId,
                    zone.SlotId,
                    zone.Position,
                    session.CreativeRotation,
                    zone.Biome,
                    session.ReturnPosition,
                    session.ReturnRotation)
                {
                    AwaitingRespawn = session.AwaitingRespawn,
                    WasDead = session.WasDead,
                    CreativeKeysSent = session.CreativeKeysSent
                };
            }
        }

        private static bool TryGetZoneByInviteCode(string inviteCode, out CreativeZone? zone)
        {
            zone = ZonesByOwnerId.Values.FirstOrDefault(existing =>
                existing.InviteCode.Equals(inviteCode.Trim(), StringComparison.OrdinalIgnoreCase) &&
                SessionsByPlayerId.TryGetValue(existing.OwnerPlayerId, out CreativeSession ownerSession) &&
                ownerSession.OwnerPlayerId == existing.OwnerPlayerId);
            return zone != null;
        }

        private static int GetNextZoneSlotIndex()
        {
            HashSet<int> usedSlots = ZonesByOwnerId.Values.Select(zone => zone.SlotIndex).ToHashSet();
            int slotIndex = 0;
            while (usedSlots.Contains(slotIndex))
            {
                slotIndex++;
            }

            return slotIndex;
        }

        private static Vector3 GetZonePosition(int slotIndex)
        {
            Vector3 origin = ModConfig.CreativePositionValue;
            float spacing = Mathf.Max(64f, ModConfig.CreativeZoneSpacing.Value);
            return origin + new Vector3(slotIndex * spacing, 0f, 0f);
        }

        private static string BuildZoneSlotId(int slotIndex)
        {
            return $"{ModConfig.CreativeSlotId.Value}_{slotIndex:000}";
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

        private static ZDO? FindPlayerZdoByPlatformId(string platformId, out string matchedIdentifier)
        {
            matchedIdentifier = string.Empty;
            if (ZNet.instance == null || ZDOMan.instance == null || string.IsNullOrWhiteSpace(platformId))
            {
                return null;
            }

            foreach (ZNetPeer peer in ZNet.instance.m_peers)
            {
                if (peer.m_characterID.IsNone())
                {
                    continue;
                }

                if (!TryGetMatchingPlatformIdentifier(peer, platformId, out matchedIdentifier))
                {
                    continue;
                }

                return ZDOMan.instance.GetZDO(peer.m_characterID);
            }

            matchedIdentifier = string.Empty;
            return null;
        }

        private static bool TryGetMatchingPlatformIdentifier(ZNetPeer peer, string platformId, out string matchedIdentifier)
        {
            matchedIdentifier = string.Empty;
            foreach (string identifier in GetPeerPlatformIdentifiers(peer))
            {
                if (PlatformIdentifierMatches(identifier, platformId))
                {
                    matchedIdentifier = identifier;
                    return true;
                }
            }

            return false;
        }

        private static bool PlatformIdentifierMatches(string identifier, string platformId)
        {
            return identifier.Equals(platformId, StringComparison.OrdinalIgnoreCase) ||
                   identifier.EndsWith("_" + platformId, StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> GetPeerPlatformIdentifiers(ZNetPeer peer)
        {
            if (peer.m_socket != null)
            {
                string hostName = peer.m_socket.GetHostName();
                if (!string.IsNullOrWhiteSpace(hostName))
                {
                    yield return hostName;
                }

                string endPoint = peer.m_socket.GetEndPointString();
                if (!string.IsNullOrWhiteSpace(endPoint))
                {
                    yield return endPoint;
                }
            }
        }

        private static bool IsServerReady()
        {
            return ZNet.instance != null &&
                   ZNet.instance.IsServer() &&
                   ZRoutedRpc.instance != null &&
                   ZoneSystem.instance != null;
        }

        private static bool IsDead(ZDO playerZdo)
        {
            return playerZdo.GetBool(ZDOVars.s_dead);
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

        private static bool TryEnsureCreativeLocation(Vector3 position, string slotId, out string error)
        {
            error = string.Empty;
            if (!ModConfig.SpawnCreativeLocation.Value)
            {
                return true;
            }

            ZoneSystem zoneSystem = ZoneSystem.instance;
            if (zoneSystem == null)
            {
                error = "Server zone system is not ready yet.";
                return false;
            }

            if (ZDOMan.instance == null)
            {
                error = "Server object system is not ready yet.";
                return false;
            }

            string locationName = ModConfig.CreativeLocationPrefab.Value.Trim();
            if (string.IsNullOrWhiteSpace(locationName))
            {
                return true;
            }

            Vector2i zone = ZoneSystem.GetZone(position);
            if (zoneSystem.m_locationInstances.TryGetValue(zone, out ZoneSystem.LocationInstance instance) &&
                instance.m_placed &&
                instance.m_location != null &&
                instance.m_location.m_prefab.Name.Equals(locationName, StringComparison.Ordinal))
            {
                DestroyCreativeLocationProxies(position, locationName, slotId);
                ScheduleCreativeLocationObjectCleanup(position, slotId);
                return true;
            }

            ZoneSystem.ZoneLocation location = zoneSystem.GetLocation(locationName.GetStableHashCode());
            if (location == null || !location.m_prefab.IsValid)
            {
                error = $"Creative location {locationName} is not loaded. Check Expand World Data config and restart the server.";
                ValheimCreativePlugin.ModLogger.LogWarning(error);
                return false;
            }

            try
            {
                int seed = (locationName + ":" + slotId).GetStableHashCode() & int.MaxValue;
                zoneSystem.SpawnLocation(
                    location,
                    seed,
                    position,
                    ModConfig.CreativeRotationValue,
                    ZoneSystem.SpawnMode.Full,
                    new List<GameObject>());

                zoneSystem.m_locationInstances[zone] = new ZoneSystem.LocationInstance
                {
                    m_location = location,
                    m_position = position,
                    m_placed = true
                };

                ValheimCreativePlugin.ModLogger.LogInfo($"Spawned creative location {locationName} at {Format(position)} in zone {zone}.");
                DestroyCreativeLocationProxies(position, locationName, slotId);
                ScheduleCreativeLocationObjectCleanup(position, slotId);
                RunCreativeLocationObjectCleanup(position, slotId);
                return true;
            }
            catch (Exception ex)
            {
                error = $"Failed to spawn creative location {locationName}.";
                ValheimCreativePlugin.ModLogger.LogWarning($"{error} {ex}");
                return false;
            }
        }

        private static void DestroyCreativeLocationProxies(Vector3 position, string locationName, string slotId)
        {
            if (ZDOMan.instance == null)
            {
                return;
            }

            int removed = 0;
            int cloneHash = locationName.GetStableHashCode();
            int baseHash = GetBaseLocationName(locationName).GetStableHashCode();
            List<ZDO> proxies = new();
            int index = 0;
            while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative("LocationProxy", proxies, ref index))
            {
            }

            foreach (ZDO proxy in proxies)
            {
                if (proxy == null ||
                    Utils.DistanceXZ(proxy.GetPosition(), position) > CreativeLocationProxyCleanupRadius)
                {
                    continue;
                }

                int proxyLocation = proxy.GetInt(ZDOVars.s_location);
                if (proxyLocation != cloneHash && proxyLocation != baseHash)
                {
                    continue;
                }

                if (!proxy.IsOwner())
                {
                    proxy.SetOwner(ZDOMan.GetSessionID());
                }

                ZDOMan.instance.DestroyZDO(proxy);
                removed++;
            }

            if (removed > 0)
            {
                ValheimCreativePlugin.ModLogger.LogInfo($"Removed {removed} creative location proxy object(s) from creative zone {slotId}.");
            }
        }

        private static int DestroyCreativeZoneZdos(Vector3 position)
        {
            List<ZDO> objects = new();
            ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(position), 2, 0, objects);
            int removed = 0;

            foreach (ZDO zdo in objects.Distinct())
            {
                if (zdo == null ||
                    !zdo.IsValid() ||
                    IsPlayerZdo(zdo) ||
                    Utils.DistanceXZ(zdo.GetPosition(), position) > CreativeZoneResetRadius)
                {
                    continue;
                }

                if (!zdo.IsOwner())
                {
                    zdo.SetOwner(ZDOMan.GetSessionID());
                }

                ZDOMan.instance.DestroyZDO(zdo);
                removed++;
            }

            return removed;
        }

        private static bool IsPlayerZdo(ZDO zdo)
        {
            return zdo.GetLong(ZDOVars.s_playerID) != 0L ||
                   zdo.GetPrefab() == "Player".GetStableHashCode();
        }

        private static string GetBaseLocationName(string locationName)
        {
            int separator = locationName.IndexOf(':');
            return separator > 0 ? locationName.Substring(0, separator) : locationName;
        }

        private static void ScheduleCreativeLocationObjectCleanup(Vector3 position, string slotId)
        {
            PendingLocationObjectCleanups.RemoveAll(cleanup => cleanup.SlotId.Equals(slotId, StringComparison.Ordinal));
            PendingLocationObjectCleanups.Add(new CreativeLocationObjectCleanup(
                position,
                slotId,
                Time.time + CreativeLocationObjectCleanupDurationSeconds));
        }

        private static void RunPendingLocationObjectCleanups()
        {
            float now = Time.time;
            foreach (CreativeLocationObjectCleanup cleanup in PendingLocationObjectCleanups.ToList())
            {
                if (now >= cleanup.Until)
                {
                    PendingLocationObjectCleanups.Remove(cleanup);
                    continue;
                }

                if (now < cleanup.NextRun)
                {
                    continue;
                }

                cleanup.NextRun = now + CreativeLocationObjectCleanupRetrySeconds;
                RunCreativeLocationObjectCleanup(cleanup.Position, cleanup.SlotId);
            }
        }

        private static void RunCreativeLocationObjectCleanup(Vector3 position, string slotId)
        {
            if (ZDOMan.instance == null)
            {
                return;
            }

            int removed = 0;
            foreach (string prefabName in CreativeLocationObjectCleanupPrefabs)
            {
                List<ZDO> objects = new();
                int index = 0;
                while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(prefabName, objects, ref index))
                {
                }

                foreach (ZDO zdo in objects)
                {
                    if (zdo == null ||
                        Utils.DistanceXZ(zdo.GetPosition(), position) > CreativeLocationObjectCleanupRadius)
                    {
                        continue;
                    }

                    if (!zdo.IsOwner())
                    {
                        zdo.SetOwner(ZDOMan.GetSessionID());
                    }

                    ZDOMan.instance.DestroyZDO(zdo);
                    removed++;
                }
            }

            if (removed > 0)
            {
                ValheimCreativePlugin.ModLogger.LogInfo($"Removed {removed} delayed location object(s) from creative zone {slotId}.");
            }
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
            return GetJsonPath(ModConfig.SessionFile.Value, "valheimCreative.sessions.json");
        }

        private static string GetZonePath()
        {
            return GetJsonPath(ModConfig.ZoneFile.Value, "valheimCreative.zones.json");
        }

        private static string GetJsonPath(string configured, string fallback)
        {
            string path = string.IsNullOrWhiteSpace(configured) ? fallback : configured;

            return Path.IsPathRooted(path)
                ? path
                : Path.Combine(Paths.ConfigPath, path);
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

        private sealed class CreativeLocationObjectCleanup
        {
            internal CreativeLocationObjectCleanup(Vector3 position, string slotId, float until)
            {
                Position = position;
                SlotId = slotId;
                Until = until;
                NextRun = Time.time;
            }

            internal Vector3 Position { get; }
            internal string SlotId { get; }
            internal float Until { get; }
            internal float NextRun { get; set; }
        }
    }
}

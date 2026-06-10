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
        private const float CreativeTerrainModifierCleanupRadius = 8f;
        private const float CreativeLocationObjectCleanupRetrySeconds = 1f;
        private const float CreativeLocationObjectCleanupDurationSeconds = 8f;
        private const float ZoneMigrationPositionTolerance = 0.1f;
        private const string CreativeTerrainModifierPrefab = "digg_v2";
        private const string TerrainModifierComponentName = "TerrainModifier";
        private const string ZdoHasFields = "HasFields";
        private const string TerrainModifierHasFields = "HasFieldsTerrainModifier";
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
        private static readonly string[] CreativeToolPrefabs = { "Hoe", "Hammer", "Cultivator", "PickaxeAntler", "Feaster" };
        private static readonly List<CreativeLocationObjectCleanup> PendingLocationObjectCleanups = new();
        private static float _nextDeathRecoveryCheck;
        private static bool _autoSpacingMigrationChecked;

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
                CreativeCommandZoneGuard.SendState(existing);
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
                zone.Radius,
                playerZdo.GetPosition(),
                playerZdo.GetRotation());

            SessionsByPlayerId[playerId] = session;
            SendCreativeKeys(session);
            CreativeBiomeService.SendOverride(session);
            CreativeCommandZoneGuard.SendState(session);
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
                long resolvedPeerId = ResolvePeerId(playerZdo, peerId);
                SendNormalKeys(resolvedPeerId);
                CreativeCommandZoneGuard.ClearState(resolvedPeerId);
                return Lines("You do not have an active creative session.");
            }

            session.PeerId = ResolvePeerId(playerZdo, peerId);
            SendNormalKeys(session.PeerId);
            CreativeBiomeService.ClearOverride(session.PeerId, session);
            CreativeCommandZoneGuard.ClearState(session.PeerId);
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

            return Lines($"Active creative session: {session.SlotId}, radius={FormatRadius(session.ZoneRadius)}m, biome={session.CreativeBiome}.");
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
                zone.Radius,
                returnPosition,
                returnRotation);

            SessionsByPlayerId[playerId] = session;
            SendCreativeKeys(session);
            CreativeBiomeService.SendOverride(session);
            CreativeCommandZoneGuard.SendState(session);
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
            int removed = DestroyCreativeZoneZdos(session.CreativePosition, session.ZoneRadius);

            if (!TryEnsureCreativeLocation(session.CreativePosition, session.SlotId, out string error))
            {
                return Lines(error);
            }

            TeleportTo(playerZdo, session.CreativePosition, session.CreativeRotation);
            CreativeBiomeService.SendOverride(session);
            CreativeCommandZoneGuard.SendState(session);
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

        internal static IEnumerable<string> ListCreativeZoneSizes()
        {
            if (ZonesByOwnerId.Count == 0)
            {
                return Lines("No creative zones are allocated.");
            }

            return ZonesByOwnerId.Values
                .OrderBy(zone => zone.SlotIndex)
                .Select(zone => $"{zone.OwnerPlayerId}: {zone.OwnerPlayerName}, slot={zone.SlotId}, radius={FormatRadius(zone.Radius)}m, biome={zone.Biome}");
        }

        internal static IEnumerable<string> GetOrSetCreativeZoneRadius(long ownerPlayerId, float? radius)
        {
            if (!IsServerReady())
            {
                return Lines("Server is not ready yet.");
            }

            if (!TryResolveCreativeZoneOwnerId(ownerPlayerId, out long resolvedOwnerId) ||
                !ZonesByOwnerId.TryGetValue(resolvedOwnerId, out CreativeZone zone))
            {
                return Lines($"Creative zone was not found for player {ownerPlayerId}.");
            }

            if (!radius.HasValue)
            {
                return Lines($"Creative zone {zone.SlotId} radius={FormatRadius(zone.Radius)}m.");
            }

            zone.Radius = Mathf.Max(1f, radius.Value);
            ApplyRadiusToZone(zone.OwnerPlayerId, zone.Radius);
            TryRefreshCreativeTerrainModifier(zone, out _);
            Save();
            ValheimCreativePlugin.ModLogger.LogInfo($"Set creative zone {zone.SlotId} radius to {FormatRadius(zone.Radius)}m.");
            return Lines($"Creative zone {zone.SlotId} radius set to {FormatRadius(zone.Radius)}m.");
        }

        internal static IEnumerable<string> GetOrSetCurrentCreativeZoneRadius(ZDO playerZdo, string rawRadius)
        {
            if (!IsServerReady())
            {
                return Lines("Server is not ready yet.");
            }

            long playerId = GetPlayerId(playerZdo);
            if (!SessionsByPlayerId.TryGetValue(playerId, out CreativeSession session))
            {
                return Lines("Use !creative before changing creative zone size.");
            }

            if (string.IsNullOrWhiteSpace(rawRadius))
            {
                return Lines($"Creative zone radius: {FormatRadius(session.ZoneRadius)}m.");
            }

            if (session.OwnerPlayerId != playerId)
            {
                return Lines("Only the creative zone owner can change its size.");
            }

            if (!float.TryParse(rawRadius.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float radius) || radius <= 0f)
            {
                return Lines("Radius must be a positive number.");
            }

            ApplyRadiusToZone(session.OwnerPlayerId, radius);
            if (ZonesByOwnerId.TryGetValue(session.OwnerPlayerId, out CreativeZone zone))
            {
                TryRefreshCreativeTerrainModifier(zone, out _);
            }
            Save();
            ValheimCreativePlugin.ModLogger.LogInfo($"Set creative zone {session.SlotId} radius to {FormatRadius(radius)}m from chat.");
            return Lines($"Creative zone radius set to {FormatRadius(radius)}m.");
        }

        internal static IEnumerable<string> GetOrSetBlueprintLoadOffset(ZDO playerZdo, string rawArgument)
        {
            long playerId = GetPlayerId(playerZdo);
            if (!SessionsByPlayerId.TryGetValue(playerId, out CreativeSession session))
            {
                return Lines("Use !creative before changing blueprint offsets.");
            }

            if (session.OwnerPlayerId != playerId)
            {
                return Lines("Only the creative zone owner can change blueprint offsets.");
            }

            string[] parts = rawArgument.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return Lines("Usage: !creative offset <blueprintName> [loadYOffset]");
            }

            if (parts.Length > 2)
            {
                return Lines("Usage: !creative offset <blueprintName> [loadYOffset]");
            }

            string blueprintName = parts[0];
            if (parts.Length == 1)
            {
                return CreativeBlueprintService.TryGetBlueprintLoadYOffset(blueprintName, out float currentOffset, out string currentSafeName, out string getError)
                    ? Lines($"{currentSafeName} loadYOffset={currentOffset.ToString("G9", CultureInfo.InvariantCulture)}.")
                    : Lines(getError);
            }

            if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float offset))
            {
                return Lines("loadYOffset must be a number.");
            }

            if (!CreativeBlueprintService.TrySetBlueprintLoadYOffset(blueprintName, offset, out string safeName, out string error))
            {
                return Lines(error);
            }

            ValheimCreativePlugin.ModLogger.LogInfo($"Set blueprint {safeName} loadYOffset to {offset.ToString("G9", CultureInfo.InvariantCulture)}.");
            return Lines($"{safeName} loadYOffset set to {offset.ToString("G9", CultureInfo.InvariantCulture)}.");
        }

        internal static void ApplyRadiusToActiveSlot(string slotId, float radius)
        {
            float normalizedRadius = Mathf.Max(1f, radius);
            foreach (CreativeSession activeSession in SessionsByPlayerId.Values)
            {
                if (!activeSession.SlotId.Equals(slotId, StringComparison.Ordinal))
                {
                    continue;
                }

                activeSession.ZoneRadius = normalizedRadius;
                CreativeBiomeService.SendOverride(activeSession);
                CreativeCommandZoneGuard.SendState(activeSession);
            }
        }

        internal static bool IsInsideCreativeZone(Vector3 point)
        {
            foreach (CreativeZone zone in ZonesByOwnerId.Values)
            {
                if (Utils.DistanceXZ(point, zone.Position) <= zone.Radius)
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool IsPlayerInsideActiveCreativeZone(ZDO playerZdo)
        {
            long playerId = GetPlayerId(playerZdo);
            if (!SessionsByPlayerId.TryGetValue(playerId, out CreativeSession session))
            {
                return false;
            }

            return Utils.DistanceXZ(playerZdo.GetPosition(), session.CreativePosition) <= session.ZoneRadius;
        }

        internal static IEnumerable<string> MigrateCreativeZoneSpacing(float targetSpacing, bool apply)
        {
            if (!IsServerReady() || ZDOMan.instance == null)
            {
                return Lines("Server is not ready yet.");
            }

            float normalizedSpacing = Mathf.Max(64f, targetSpacing);
            List<CreativeZone> oldZones = ZonesByOwnerId.Values
                .OrderBy(zone => zone.SlotIndex)
                .ToList();
            List<CreativeZoneMigration> migrations = new();

            foreach (CreativeZone zone in oldZones)
            {
                Vector3 targetPosition = GetZonePosition(zone.SlotIndex, normalizedSpacing);
                if (Utils.DistanceXZ(zone.Position, targetPosition) <= ZoneMigrationPositionTolerance)
                {
                    continue;
                }

                List<ZDO> objects = FindCreativeZoneMigrationZdos(zone, oldZones, out int skippedOverlap);
                migrations.Add(new CreativeZoneMigration(zone, zone.Position, targetPosition, objects, skippedOverlap));
            }

            if (migrations.Count == 0)
            {
                return Lines($"No creative zones need migration for spacing {FormatRadius(normalizedSpacing)}m.");
            }

            List<string> lines = new();
            int totalObjects = migrations.Sum(migration => migration.Objects.Count);
            int totalSkipped = migrations.Sum(migration => migration.SkippedOverlap);
            lines.Add(apply
                ? $"Applying creative zone spacing migration to {FormatRadius(normalizedSpacing)}m."
                : $"Dry run: creative zone spacing migration to {FormatRadius(normalizedSpacing)}m. Run again with apply to move objects.");

            foreach (CreativeZoneMigration migration in migrations)
            {
                lines.Add(
                    $"{migration.Zone.SlotId}: {Format(migration.OldPosition)} -> {Format(migration.NewPosition)}, " +
                    $"move {migration.Objects.Count} object(s), skip {migration.SkippedOverlap} overlapping object(s).");
            }

            lines.Add($"Total: move {totalObjects} object(s), skip {totalSkipped} overlapping object(s).");
            if (!apply)
            {
                return lines;
            }

            string zoneBackup = BackupJsonFile(GetZonePath());
            string sessionBackup = BackupJsonFile(GetSessionPath());
            foreach (CreativeZoneMigration migration in migrations)
            {
                MoveCreativeLocationInstance(migration.OldPosition, migration.NewPosition, migration.Zone.SlotId);
                MoveCreativeZoneObjects(migration);
                ZonesByOwnerId[migration.Zone.OwnerPlayerId] = new CreativeZone(
                    migration.Zone.OwnerPlayerId,
                    migration.Zone.OwnerPlayerName,
                    migration.Zone.SlotIndex,
                    BuildZoneSlotId(migration.Zone.SlotIndex),
                    migration.NewPosition,
                    migration.Zone.Biome,
                    migration.Zone.Radius);
            }

            ModConfig.CreativeZoneSpacing.Value = normalizedSpacing;
            ModConfig.Save();
            ReconcileCreativeState();
            Save();

            foreach (CreativeZoneMigration migration in migrations)
            {
                if (!ZonesByOwnerId.TryGetValue(migration.Zone.OwnerPlayerId, out CreativeZone migratedZone))
                {
                    continue;
                }

                if (!TryApplyCreativeTerrainModifierRadius(migratedZone.Position, migratedZone.SlotId, migratedZone.Radius, out _))
                {
                    TryRefreshCreativeTerrainModifier(migratedZone, out _);
                }
            }

            foreach (CreativeSession session in SessionsByPlayerId.Values)
            {
                CreativeBiomeService.SendOverride(session);
                CreativeCommandZoneGuard.SendState(session);
            }

            foreach (CreativeZoneMigration migration in migrations)
            {
                PokeCreativeHeightmaps(migration.OldPosition, migration.Zone.Radius);
                PokeCreativeHeightmaps(migration.NewPosition, migration.Zone.Radius);
            }

            ZNet.instance.Save(false, false, true);
            lines.Add($"Backed up zones to {zoneBackup}.");
            lines.Add($"Backed up sessions to {sessionBackup}.");
            lines.Add($"CreativeZoneSpacing is now {FormatRadius(normalizedSpacing)}m.");
            return lines;
        }

        private static void TryAutoMigrateCreativeZoneSpacing()
        {
            if (_autoSpacingMigrationChecked)
            {
                return;
            }

            if (ZDOMan.instance == null)
            {
                return;
            }

            _autoSpacingMigrationChecked = true;
            float configuredSpacing = Mathf.Max(64f, ModConfig.CreativeZoneSpacing.Value);
            bool needsMigration = ZonesByOwnerId.Values.Any(zone =>
                Utils.DistanceXZ(zone.Position, GetZonePosition(zone.SlotIndex, configuredSpacing)) > ZoneMigrationPositionTolerance);
            if (!needsMigration)
            {
                return;
            }

            foreach (string line in MigrateCreativeZoneSpacing(configuredSpacing, apply: true))
            {
                ValheimCreativePlugin.ModLogger.LogInfo("[Creative zone spacing migration] " + line);
            }
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

            TryAutoMigrateCreativeZoneSpacing();
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
                    SendSessionKeys(session);
                    CreativeBiomeService.SendOverride(session);
                    CreativeCommandZoneGuard.SendState(session);
                    TeleportTo(playerZdo, session.CreativePosition, session.CreativeRotation);
                    session.AwaitingRespawn = false;
                    session.WasDead = false;
                    Save();
                    LogDebug($"Recovered creative session after death for {session.PlayerName} ({session.PlayerId}).");
                }
                else if (!session.CreativeKeysSent)
                {
                    SendSessionKeys(session);
                    CreativeBiomeService.SendOverride(session);
                    CreativeCommandZoneGuard.SendState(session);
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

                SendSessionKeys(session);
                CreativeBiomeService.SendOverride(session);
                CreativeCommandZoneGuard.SendState(session);
            }
        }

        internal static ZDO? FindPlayerZdo(ZDOID characterId)
        {
            return ZDOMan.instance != null && !characterId.IsNone()
                ? ZDOMan.instance.GetZDO(characterId)
                : null;
        }

        internal static CreativeSession? GetSession(long playerId)
        {
            return SessionsByPlayerId.TryGetValue(playerId, out CreativeSession session) ? session : null;
        }

        internal static void SetSession(CreativeSession session)
        {
            SessionsByPlayerId[session.PlayerId] = session;
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
                        session.CreativeBiome,
                        session.ZoneRadius);
                }
            }

            ReconcileCreativeState();
            _autoSpacingMigrationChecked = false;
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
            zone = new CreativeZone(
                ownerPlayerId,
                ownerPlayerName,
                slotIndex,
                slotId,
                GetZonePosition(slotIndex),
                CreativeBiomeService.DefaultBiome,
                ModConfig.DefaultCreativeZoneRadiusValue);
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
                CreativeCommandZoneGuard.SendState(activeSession);
            }
        }

        private static void ApplyRadiusToZone(long ownerPlayerId, float radius)
        {
            float normalizedRadius = Mathf.Max(1f, radius);
            if (ZonesByOwnerId.TryGetValue(ownerPlayerId, out CreativeZone zone))
            {
                zone.Radius = normalizedRadius;
            }

            foreach (CreativeSession activeSession in SessionsByPlayerId.Values)
            {
                if (activeSession.OwnerPlayerId != ownerPlayerId)
                {
                    continue;
                }

                activeSession.ZoneRadius = normalizedRadius;
                CreativeBiomeService.SendOverride(activeSession);
                CreativeCommandZoneGuard.SendState(activeSession);
            }
        }

        internal static float GetTerrainModifierRadiusAtPosition(Vector3 position)
        {
            CreativeZone? closestZone = null;
            float closestDistance = float.MaxValue;
            foreach (CreativeZone zone in ZonesByOwnerId.Values)
            {
                float distance = Utils.DistanceXZ(zone.Position, position);
                if (distance >= closestDistance)
                {
                    continue;
                }

                closestZone = zone;
                closestDistance = distance;
            }

            return closestZone != null && closestDistance <= Mathf.Max(1f, closestZone.Radius)
                ? Mathf.Max(1f, closestZone.Radius)
                : ModConfig.DefaultCreativeZoneRadiusValue;
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
                    zone.Position,
                    zone.Biome,
                    zone.Radius);
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
                    zone.Radius,
                    session.ReturnPosition,
                    session.ReturnRotation,
                    session.GrantCreativeKeys)
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

        private static bool TryGetZoneBySlotId(string slotId, out CreativeZone? zone)
        {
            zone = ZonesByOwnerId.Values.FirstOrDefault(existing =>
                existing.SlotId.Equals(slotId, StringComparison.Ordinal));
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
            return GetZonePosition(slotIndex, ModConfig.CreativeZoneSpacing.Value);
        }

        private static Vector3 GetZonePosition(int slotIndex, float spacing)
        {
            Vector3 origin = ModConfig.CreativePositionValue;
            float normalizedSpacing = Mathf.Max(64f, spacing);
            return origin + new Vector3(slotIndex * normalizedSpacing, 0f, 0f);
        }

        private static string BuildZoneSlotId(int slotIndex)
        {
            return $"{ModConfig.CreativeSlotId.Value}_{slotIndex:000}";
        }

        internal static ZDO? FindPlayerZdo(long playerId)
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

        internal static ZDO? FindPlayerZdoByPlatformId(string platformId, out string matchedIdentifier)
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

        internal static bool IsServerReady()
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

        internal static long ResolvePeerId(ZDO playerZdo, long fallback)
        {
            long owner = playerZdo.GetOwner();
            return owner != 0L ? owner : fallback;
        }

        internal static long GetPlayerId(ZDO playerZdo)
        {
            return playerZdo.GetLong(ZDOVars.s_playerID);
        }

        internal static string GetPlayerName(ZDO playerZdo, string fallback)
        {
            string name = playerZdo.GetString(ZDOVars.s_playerName, fallback);
            return string.IsNullOrWhiteSpace(name) ? fallback : name;
        }

        internal static void TeleportTo(ZDO playerZdo, Vector3 position, Quaternion rotation)
        {
            long owner = playerZdo.GetOwner();
            if (owner == 0L)
            {
                ValheimCreativePlugin.ModLogger.LogWarning($"Cannot teleport player ZDO {playerZdo.m_uid}: no owner.");
                return;
            }

            ZRoutedRpc.instance.InvokeRoutedRPC(owner, playerZdo.m_uid, "RPC_TeleportTo", position, rotation, true);
        }

        internal static bool TryEnsureCreativeLocation(Vector3 position, string slotId, out string error)
        {
            return TryEnsureCreativeLocation(position, slotId, false, out error);
        }

        private static bool TryEnsureCreativeLocation(Vector3 position, string slotId, bool forceRespawn, out string error)
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
            bool hasPlacedCreativeLocation =
                zoneSystem.m_locationInstances.TryGetValue(zone, out ZoneSystem.LocationInstance instance) &&
                instance.m_placed &&
                instance.m_location != null &&
                instance.m_location.m_prefab.Name.Equals(locationName, StringComparison.Ordinal);

            if (hasPlacedCreativeLocation && !forceRespawn)
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

            if (forceRespawn)
            {
                DestroyCreativeTerrainModifiers(position, slotId);
                DestroyCreativeLocationProxies(position, locationName, slotId);
                if (hasPlacedCreativeLocation)
                {
                    zoneSystem.m_locationInstances.Remove(zone);
                }
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
                if (TryGetZoneBySlotId(slotId, out CreativeZone? creativeZone) && creativeZone != null)
                {
                    TryApplyCreativeTerrainModifierRadius(creativeZone.Position, creativeZone.SlotId, creativeZone.Radius, out _);
                }

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

        private static bool TryRefreshCreativeTerrainModifier(CreativeZone zone, out string error)
        {
            if (!TryEnsureCreativeLocation(zone.Position, zone.SlotId, true, out error))
            {
                ValheimCreativePlugin.ModLogger.LogWarning(
                    $"Creative zone {zone.SlotId} terrain modifier refresh failed after radius change: {error}");
                return false;
            }

            if (!TryApplyCreativeTerrainModifierRadius(zone.Position, zone.SlotId, zone.Radius, out error))
            {
                ValheimCreativePlugin.ModLogger.LogWarning(
                    $"Creative zone {zone.SlotId} terrain modifier radius refresh failed after location respawn: {error}");
                return false;
            }

            return true;
        }

        internal static IEnumerable<string> GetCreativeTerrainModifierStatus(long ownerPlayerId)
        {
            if (!IsServerReady() || ZDOMan.instance == null)
            {
                return Lines("Server is not ready yet.");
            }

            if (!TryResolveCreativeZoneOwnerId(ownerPlayerId, out long resolvedOwnerId) ||
                !ZonesByOwnerId.TryGetValue(resolvedOwnerId, out CreativeZone zone))
            {
                return Lines($"Creative zone was not found for player {ownerPlayerId}.");
            }

            List<ZDO> modifiers = FindCreativeTerrainModifierZdos(zone.Position);
            if (modifiers.Count == 0)
            {
                return Lines($"No {CreativeTerrainModifierPrefab} terrain modifier found near {zone.SlotId}.");
            }

            return modifiers
                .OrderBy(zdo => Utils.DistanceXZ(zdo.GetPosition(), zone.Position))
                .Select(zdo => FormatCreativeTerrainModifierStatus(zone, zdo));
        }

        private static bool TryApplyCreativeTerrainModifierRadius(Vector3 position, string slotId, float radius, out string error)
        {
            error = string.Empty;
            if (ZDOMan.instance == null)
            {
                error = "Server object system is not ready yet.";
                return false;
            }

            List<ZDO> modifiers = FindCreativeTerrainModifierZdos(position);
            if (modifiers.Count == 0)
            {
                error = $"No {CreativeTerrainModifierPrefab} terrain modifier found near creative zone {slotId}.";
                return false;
            }

            float normalizedRadius = Mathf.Max(1f, radius);
            int updated = 0;
            foreach (ZDO zdo in modifiers)
            {
                if (!zdo.IsOwner())
                {
                    zdo.SetOwner(ZDOMan.GetSessionID());
                }

                ApplyTerrainModifierFields(zdo, normalizedRadius);
                ZNetView? netView = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(zdo) : null;
                TerrainModifier? terrainModifier = netView != null ? netView.GetComponent<TerrainModifier>() : null;
                if (terrainModifier != null)
                {
                    ApplyTerrainModifierFields(terrainModifier, normalizedRadius);
                    PokeCreativeHeightmaps(terrainModifier);
                }

                updated++;
            }

            ValheimCreativePlugin.ModLogger.LogInfo(
                $"Updated {updated} creative terrain modifier object(s) in {slotId} to radius {FormatRadius(normalizedRadius)}m.");
            return true;
        }

        private static List<ZDO> FindCreativeTerrainModifierZdos(Vector3 position)
        {
            List<ZDO> matches = new();
            List<ZDO> objects = new();
            int index = 0;
            while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(CreativeTerrainModifierPrefab, objects, ref index))
            {
            }

            foreach (ZDO zdo in objects)
            {
                if (zdo == null ||
                    !zdo.IsValid() ||
                    Utils.DistanceXZ(zdo.GetPosition(), position) > CreativeTerrainModifierCleanupRadius)
                {
                    continue;
                }

                matches.Add(zdo);
            }

            return matches;
        }

        private static void ApplyTerrainModifierFields(ZDO zdo, float radius)
        {
            zdo.Set(ZdoHasFields, true);
            zdo.Set(TerrainModifierHasFields, true);
            zdo.Set($"{TerrainModifierComponentName}.m_level", true);
            zdo.Set($"{TerrainModifierComponentName}.m_square", false);
            zdo.Set($"{TerrainModifierComponentName}.m_smooth", true);
            zdo.Set($"{TerrainModifierComponentName}.m_paintCleared", false);
            zdo.Set($"{TerrainModifierComponentName}.m_useTerrainCompiler", false);
            zdo.Set($"{TerrainModifierComponentName}.m_playerModifiction", false);
            zdo.Set($"{TerrainModifierComponentName}.m_levelOffset", 0f);
            zdo.Set($"{TerrainModifierComponentName}.m_levelRadius", radius);
            zdo.Set($"{TerrainModifierComponentName}.m_smoothRadius", radius);
            zdo.Set($"{TerrainModifierComponentName}.m_paintRadius", radius);
            zdo.Set($"{TerrainModifierComponentName}.m_smoothPower", 4f);
        }

        private static void ApplyTerrainModifierFields(TerrainModifier terrainModifier, float radius)
        {
            terrainModifier.m_level = true;
            terrainModifier.m_square = false;
            terrainModifier.m_smooth = true;
            terrainModifier.m_paintCleared = false;
            terrainModifier.m_useTerrainCompiler = false;
            terrainModifier.m_playerModifiction = false;
            terrainModifier.m_levelOffset = 0f;
            terrainModifier.m_levelRadius = radius;
            terrainModifier.m_smoothRadius = radius;
            terrainModifier.m_paintRadius = radius;
            terrainModifier.m_smoothPower = 4f;
        }

        private static void PokeCreativeHeightmaps(TerrainModifier terrainModifier)
        {
            foreach (Heightmap heightmap in Heightmap.GetAllHeightmaps())
            {
                if (heightmap != null && heightmap.TerrainVSModifier(terrainModifier))
                {
                    heightmap.Poke(false);
                }
            }

            if (ClutterSystem.instance != null)
            {
                ClutterSystem.instance.ResetGrass(terrainModifier.transform.position, terrainModifier.GetRadius());
            }
        }

        private static string FormatCreativeTerrainModifierStatus(CreativeZone zone, ZDO zdo)
        {
            float distance = Utils.DistanceXZ(zdo.GetPosition(), zone.Position);
            bool hasFields = zdo.GetBool(ZdoHasFields);
            bool hasTerrainFields = zdo.GetBool(TerrainModifierHasFields);
            float zdoLevelRadius = zdo.GetFloat($"{TerrainModifierComponentName}.m_levelRadius");
            float zdoSmoothRadius = zdo.GetFloat($"{TerrainModifierComponentName}.m_smoothRadius");
            ZNetView? netView = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(zdo) : null;
            TerrainModifier? terrainModifier = netView != null ? netView.GetComponent<TerrainModifier>() : null;
            string liveStatus = terrainModifier != null
                ? $"live level={FormatRadius(terrainModifier.m_levelRadius)}m smooth={FormatRadius(terrainModifier.m_smoothRadius)}m radius={FormatRadius(terrainModifier.GetRadius())}m"
                : "live not instantiated on server";

            return $"{zone.SlotId} modifier={zdo.m_uid} distance={distance.ToString("0.##", CultureInfo.InvariantCulture)}m " +
                   $"zdo hasFields={hasFields} hasTerrainFields={hasTerrainFields} " +
                   $"level={FormatRadius(zdoLevelRadius)}m smooth={FormatRadius(zdoSmoothRadius)}m {liveStatus}";
        }

        private static List<ZDO> FindCreativeZoneMigrationZdos(CreativeZone zone, List<CreativeZone> oldZones, out int skippedOverlap)
        {
            List<ZDO> matches = new();
            List<ZDO> objects = new();
            float maxDistance = Mathf.Max(1f, zone.Radius);
            int sectorArea = Mathf.CeilToInt(maxDistance / ZoneSystem.c_ZoneSize) + 1;
            ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(zone.Position), sectorArea, 0, objects);
            skippedOverlap = 0;

            foreach (ZDO zdo in objects.Distinct())
            {
                if (zdo == null ||
                    !zdo.IsValid() ||
                    IsPlayerZdo(zdo) ||
                    Utils.DistanceXZ(zdo.GetPosition(), zone.Position) > maxDistance)
                {
                    continue;
                }

                if (!BelongsToMigrationZone(zdo.GetPosition(), zone, oldZones))
                {
                    skippedOverlap++;
                    continue;
                }

                matches.Add(zdo);
            }

            return matches;
        }

        private static bool BelongsToMigrationZone(Vector3 position, CreativeZone zone, List<CreativeZone> oldZones)
        {
            CreativeZone? closestZone = null;
            float closestDistance = float.MaxValue;
            bool ambiguous = false;

            foreach (CreativeZone candidate in oldZones)
            {
                float distance = Utils.DistanceXZ(position, candidate.Position);
                if (distance > Mathf.Max(1f, candidate.Radius))
                {
                    continue;
                }

                if (distance < closestDistance - ZoneMigrationPositionTolerance)
                {
                    closestZone = candidate;
                    closestDistance = distance;
                    ambiguous = false;
                    continue;
                }

                if (Mathf.Abs(distance - closestDistance) <= ZoneMigrationPositionTolerance)
                {
                    ambiguous = true;
                }
            }

            return !ambiguous &&
                   closestZone != null &&
                   closestZone.OwnerPlayerId == zone.OwnerPlayerId;
        }

        private static void MoveCreativeZoneObjects(CreativeZoneMigration migration)
        {
            Vector3 delta = migration.NewPosition - migration.OldPosition;
            foreach (ZDO zdo in migration.Objects)
            {
                if (zdo == null || !zdo.IsValid())
                {
                    continue;
                }

                if (!zdo.IsOwner())
                {
                    zdo.SetOwner(ZDOMan.GetSessionID());
                }

                Vector3 oldPosition = zdo.GetPosition();
                Vector3 newPosition = oldPosition + delta;
                ZNetView? netView = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(zdo) : null;
                zdo.SetPosition(newPosition);
                if (netView != null)
                {
                    netView.transform.position = newPosition;
                }
            }
        }

        private static void MoveCreativeLocationInstance(Vector3 oldPosition, Vector3 newPosition, string slotId)
        {
            if (ZoneSystem.instance == null)
            {
                return;
            }

            Vector2i oldZone = ZoneSystem.GetZone(oldPosition);
            Vector2i newZone = ZoneSystem.GetZone(newPosition);
            if (oldZone == newZone ||
                !ZoneSystem.instance.m_locationInstances.TryGetValue(oldZone, out ZoneSystem.LocationInstance instance))
            {
                return;
            }

            string locationName = ModConfig.CreativeLocationPrefab.Value.Trim();
            if (instance.m_location == null ||
                instance.m_location.m_prefab == null ||
                !instance.m_location.m_prefab.Name.Equals(locationName, StringComparison.Ordinal))
            {
                return;
            }

            if (ZoneSystem.instance.m_locationInstances.TryGetValue(newZone, out ZoneSystem.LocationInstance existing) &&
                existing.m_location != null &&
                existing.m_location.m_prefab != null &&
                !existing.m_location.m_prefab.Name.Equals(locationName, StringComparison.Ordinal))
            {
                ValheimCreativePlugin.ModLogger.LogWarning(
                    $"Not moving creative location instance for {slotId}: target zone {newZone} already has {existing.m_location.m_prefab.Name}.");
                return;
            }

            ZoneSystem.instance.m_locationInstances.Remove(oldZone);
            instance.m_position = newPosition;
            ZoneSystem.instance.m_locationInstances[newZone] = instance;
        }

        private static void PokeCreativeHeightmaps(Vector3 center, float radius)
        {
            foreach (Heightmap heightmap in Heightmap.GetAllHeightmaps())
            {
                if (heightmap != null && HeightmapIntersects(heightmap, center, radius))
                {
                    heightmap.Poke(false);
                }
            }

            if (ClutterSystem.instance != null)
            {
                ClutterSystem.instance.ResetGrass(center, radius);
            }
        }

        private static bool HeightmapIntersects(Heightmap heightmap, Vector3 center, float radius)
        {
            float halfSize = heightmap.m_width * heightmap.m_scale * 0.5f;
            Vector3 heightmapCenter = heightmap.transform.position;
            float dx = Mathf.Max(Mathf.Abs(heightmapCenter.x - center.x) - halfSize, 0f);
            float dz = Mathf.Max(Mathf.Abs(heightmapCenter.z - center.z) - halfSize, 0f);
            float expandedRadius = Mathf.Max(1f, radius) + 4f;
            return dx * dx + dz * dz <= expandedRadius * expandedRadius;
        }

        private static string BackupJsonFile(string path)
        {
            if (!File.Exists(path))
            {
                return $"missing:{path}";
            }

            string backupPath = path + ".bak-zone-migration-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
            File.Copy(path, backupPath, overwrite: false);
            return backupPath;
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

        private static void DestroyCreativeTerrainModifiers(Vector3 position, string slotId)
        {
            if (ZDOMan.instance == null)
            {
                return;
            }

            int removed = 0;
            List<ZDO> objects = new();
            int index = 0;
            while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(CreativeTerrainModifierPrefab, objects, ref index))
            {
            }

            foreach (ZDO zdo in objects)
            {
                if (zdo == null ||
                    Utils.DistanceXZ(zdo.GetPosition(), position) > CreativeTerrainModifierCleanupRadius)
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

            if (removed > 0)
            {
                ValheimCreativePlugin.ModLogger.LogInfo($"Removed {removed} creative terrain modifier object(s) from creative zone {slotId}.");
            }
        }

        internal static int DestroyCreativeZoneZdos(Vector3 position, float radius)
        {
            List<ZDO> objects = new();
            float maxDistance = Mathf.Max(1f, radius);
            int sectorArea = Mathf.CeilToInt(maxDistance / ZoneSystem.c_ZoneSize) + 1;
            ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(position), sectorArea, 0, objects);
            int removed = 0;

            foreach (ZDO zdo in objects.Distinct())
            {
                if (zdo == null ||
                    !zdo.IsValid() ||
                    IsPlayerZdo(zdo) ||
                    Utils.DistanceXZ(zdo.GetPosition(), position) > maxDistance)
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

        private static bool TryResolveCreativeZoneOwnerId(long rawId, out long ownerPlayerId)
        {
            if (ZonesByOwnerId.ContainsKey(rawId))
            {
                ownerPlayerId = rawId;
                return true;
            }

            string platformId = rawId.ToString(CultureInfo.InvariantCulture);
            ZDO? playerZdo = FindPlayerZdo(rawId) ?? FindPlayerZdoByPlatformId(platformId, out _);
            if (playerZdo != null)
            {
                long playerId = GetPlayerId(playerZdo);
                if (ZonesByOwnerId.ContainsKey(playerId))
                {
                    ownerPlayerId = playerId;
                    return true;
                }
            }

            ownerPlayerId = 0L;
            return false;
        }

        internal static string FormatRadius(float radius)
        {
            return Mathf.Max(1f, radius).ToString("0.##", CultureInfo.InvariantCulture);
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

        internal static void SendSessionKeys(CreativeSession session)
        {
            if (session.GrantCreativeKeys)
            {
                SendCreativeKeys(session);
                return;
            }

            SendNormalKeys(session.PeerId);
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

        internal static IEnumerable<string> Lines(string line)
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

        private sealed class CreativeZoneMigration
        {
            internal CreativeZoneMigration(
                CreativeZone zone,
                Vector3 oldPosition,
                Vector3 newPosition,
                List<ZDO> objects,
                int skippedOverlap)
            {
                Zone = zone;
                OldPosition = oldPosition;
                NewPosition = newPosition;
                Objects = objects;
                SkippedOverlap = skippedOverlap;
            }

            internal CreativeZone Zone { get; }
            internal Vector3 OldPosition { get; }
            internal Vector3 NewPosition { get; }
            internal List<ZDO> Objects { get; }
            internal int SkippedOverlap { get; }
        }
    }
}

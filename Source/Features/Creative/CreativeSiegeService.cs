using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BepInEx;
using Newtonsoft.Json;
using UnityEngine;
using ValheimCreative.Configuration;

namespace ValheimCreative.Features.Creative
{
    internal static class CreativeSiegeService
    {
        private static readonly Dictionary<string, SiegeDefinition> DefinitionsById = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, SiegeZoneState> ZonesById = new(StringComparer.OrdinalIgnoreCase);

        internal static void Load()
        {
            DefinitionsById.Clear();
            ZonesById.Clear();

            foreach (SiegeDefinition definition in LoadDefinitions(GetConfigPath(ModConfig.SiegeDefinitionsFile.Value, "valheimCreative.sieges.json")))
            {
                if (string.IsNullOrWhiteSpace(definition.Id) || string.IsNullOrWhiteSpace(definition.Blueprint))
                {
                    ValheimCreativePlugin.ModLogger.LogWarning("Skipped siege definition with missing id or blueprint.");
                    continue;
                }

                definition.Id = NormalizeId(definition.Id);
                DefinitionsById[definition.Id] = definition;
            }

            foreach (SiegeZoneState zone in LoadState(GetConfigPath(ModConfig.SiegeStateFile.Value, "valheimCreative.siege-zones.json")))
            {
                if (!string.IsNullOrWhiteSpace(zone.Id))
                {
                    zone.Id = NormalizeId(zone.Id);
                    if (DefinitionsById.TryGetValue(zone.Id, out SiegeDefinition definition) && zone.Radius <= 0f)
                    {
                        zone.Radius = definition.RadiusOrDefault;
                    }

                    ZonesById[zone.Id] = zone;
                }
            }

            ValheimCreativePlugin.ModLogger.LogInfo($"Loaded {DefinitionsById.Count} siege definition(s) and {ZonesById.Count} siege zone state record(s).");
        }

        internal static void Save()
        {
            WriteJson(
                GetConfigPath(ModConfig.SiegeStateFile.Value, "valheimCreative.siege-zones.json"),
                new SiegeStateFile
                {
                    Zones = ZonesById.Values.OrderBy(zone => zone.Id, StringComparer.OrdinalIgnoreCase).ToList()
                });
        }

        internal static IEnumerable<string> ListSieges()
        {
            if (DefinitionsById.Count == 0)
            {
                return CreativeSessionManager.Lines("No sieges are configured.");
            }

            return DefinitionsById.Values
                .OrderBy(definition => definition.Id, StringComparer.OrdinalIgnoreCase)
                .Select(definition =>
                {
                    float radius = ZonesById.TryGetValue(definition.Id, out SiegeZoneState zone)
                        ? zone.RadiusValue
                        : definition.RadiusOrDefault;
                    return $"{definition.Id}: {definition.DisplayNameOrId}, blueprint={definition.Blueprint}, biome={definition.BiomeOrDefault}, radius={CreativeSessionManager.FormatRadius(radius)}m";
                });
        }

        internal static IEnumerable<string> GetStatus(string siegeId)
        {
            if (!TryGetDefinition(siegeId, out SiegeDefinition definition, out string error))
            {
                return CreativeSessionManager.Lines(error);
            }

            SiegeZoneState zone = GetOrCreateZone(definition);
            return CreativeSessionManager.Lines(
                $"{definition.Id}: loaded={zone.Loaded}, position={Format(zone.PositionValue)}, radius={CreativeSessionManager.FormatRadius(zone.RadiusValue)}m, blueprint={definition.Blueprint}, biome={definition.BiomeOrDefault}");
        }

        internal static IEnumerable<string> GetOrSetSiegeRadius(string siegeId, float? radius)
        {
            if (!TryGetDefinition(siegeId, out SiegeDefinition definition, out string error))
            {
                return CreativeSessionManager.Lines(error);
            }

            SiegeZoneState zone = GetOrCreateZone(definition);
            if (!radius.HasValue)
            {
                return CreativeSessionManager.Lines($"Siege {definition.Id} radius={CreativeSessionManager.FormatRadius(zone.RadiusValue)}m.");
            }

            zone.Radius = Mathf.Max(1f, radius.Value);
            CreativeSessionManager.ApplyRadiusToActiveSlot(zone.SlotId, zone.RadiusValue);
            CreativeSessionManager.Save();
            Save();
            ValheimCreativePlugin.ModLogger.LogInfo($"Set siege {definition.Id} radius to {CreativeSessionManager.FormatRadius(zone.RadiusValue)}m.");
            return CreativeSessionManager.Lines($"Siege {definition.Id} radius set to {CreativeSessionManager.FormatRadius(zone.RadiusValue)}m.");
        }

        internal static IEnumerable<string> LoadSiege(string siegeId)
        {
            if (!TryEnsureSiegeLoaded(siegeId, forceReload: false, out SiegeDefinition? definition, out SiegeZoneState? zone, out int spawned, out List<string> missingPrefabs, out string error))
            {
                return CreativeSessionManager.Lines(error);
            }

            string missing = missingPrefabs.Count == 0
                ? string.Empty
                : $" Missing prefabs: {string.Join(", ", missingPrefabs.Take(8))}{(missingPrefabs.Count > 8 ? "..." : "")}.";
            return CreativeSessionManager.Lines($"Siege {definition!.Id} loaded at {Format(zone!.PositionValue)}. Spawned {spawned} object(s).{missing}");
        }

        internal static IEnumerable<string> ResetSiege(string siegeId)
        {
            if (!TryGetDefinition(siegeId, out SiegeDefinition definition, out string error))
            {
                return CreativeSessionManager.Lines(error);
            }

            SiegeZoneState zone = GetOrCreateZone(definition);
            int removed = ResetZone(definition, zone);
            zone.Loaded = false;
            zone.LoadedAt = string.Empty;
            Save();
            return CreativeSessionManager.Lines($"Siege {definition.Id} reset. Removed {removed} object(s).");
        }

        internal static IEnumerable<string> EnterSiegeForPlayerId(long playerId, string siegeId)
        {
            ZDO? playerZdo = CreativeSessionManager.FindPlayerZdo(playerId);
            if (playerZdo == null)
            {
                string platformId = playerId.ToString(CultureInfo.InvariantCulture);
                playerZdo = CreativeSessionManager.FindPlayerZdoByPlatformId(platformId, out _);
            }

            if (playerZdo == null)
            {
                return CreativeSessionManager.Lines($"Player {playerId} was not found online by Valheim player ID or platform ID.");
            }

            return EnterSiege(CreativeSessionManager.ResolvePeerId(playerZdo, 0L), playerZdo, siegeId);
        }

        internal static IEnumerable<string> EnterSiege(long peerId, ZDO playerZdo, string siegeId)
        {
            if (!CreativeSessionManager.IsServerReady())
            {
                return CreativeSessionManager.Lines("Server is not ready yet.");
            }

            long playerId = CreativeSessionManager.GetPlayerId(playerZdo);
            if (playerId == 0L)
            {
                return CreativeSessionManager.Lines("Could not identify your character.");
            }

            if (!TryEnsureSiegeLoaded(siegeId, forceReload: false, out SiegeDefinition? definition, out SiegeZoneState? zone, out _, out _, out string error))
            {
                return CreativeSessionManager.Lines(error);
            }

            CreativeSession? existing = CreativeSessionManager.GetSession(playerId);
            Vector3 returnPosition = existing?.ReturnPosition ?? playerZdo.GetPosition();
            Quaternion returnRotation = existing?.ReturnRotation ?? playerZdo.GetRotation();
            long resolvedPeerId = CreativeSessionManager.ResolvePeerId(playerZdo, peerId);
            Heightmap.Biome biome = ParseBiome(definition!.Biome);

            CreativeSession session = new(
                playerId,
                resolvedPeerId,
                CreativeSessionManager.GetPlayerName(playerZdo, "Siege"),
                0L,
                zone!.SlotId,
                zone.PositionValue,
                ModConfig.CreativeRotationValue,
                biome,
                zone.RadiusValue,
                returnPosition,
                returnRotation,
                definition.GrantCreativeKeys);

            CreativeSessionManager.SetSession(session);
            CreativeSessionManager.SendSessionKeys(session);
            CreativeBiomeService.SendOverride(session);
            CreativeSessionManager.TeleportTo(playerZdo, session.CreativePosition, session.CreativeRotation);
            CreativeSessionManager.Save();

            ValheimCreativePlugin.ModLogger.LogInfo($"Entered siege {definition.Id} for {session.PlayerName} ({session.PlayerId}) at {Format(zone.PositionValue)}.");
            return CreativeSessionManager.Lines($"Entered siege: {definition.DisplayNameOrId}. Use !return to leave.");
        }

        private static bool TryEnsureSiegeLoaded(
            string siegeId,
            bool forceReload,
            out SiegeDefinition? definition,
            out SiegeZoneState? zone,
            out int spawned,
            out List<string> missingPrefabs,
            out string error)
        {
            spawned = 0;
            missingPrefabs = new List<string>();
            zone = null;
            if (!TryGetDefinition(siegeId, out definition, out error))
            {
                return false;
            }

            zone = GetOrCreateZone(definition);
            if (zone.Loaded && !forceReload)
            {
                return true;
            }

            if (definition.ResetBeforeLoad || forceReload)
            {
                ResetZone(definition, zone);
            }

            if (!CreativeSessionManager.TryEnsureCreativeLocation(zone.PositionValue, zone.SlotId, out error))
            {
                return false;
            }

            Heightmap.Biome biome = ParseBiome(definition.Biome);
            CreativeSession loadSession = new(
                0L,
                0L,
                "Siege",
                0L,
                zone.SlotId,
                zone.PositionValue,
                ModConfig.CreativeRotationValue,
                biome,
                zone.RadiusValue,
                Vector3.zero,
                Quaternion.identity,
                definition.GrantCreativeKeys);

            if (!CreativeBlueprintService.TryLoadBlueprint(
                    definition.Blueprint,
                    loadSession,
                    0L,
                    out spawned,
                    out missingPrefabs,
                    out Heightmap.Biome? metadataBiome,
                    out error))
            {
                return false;
            }

            if (metadataBiome.HasValue)
            {
                definition.Biome = metadataBiome.Value.ToString();
            }

            zone.Loaded = true;
            zone.LoadedAt = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            Save();
            ValheimCreativePlugin.ModLogger.LogInfo($"Loaded siege {definition.Id} blueprint {definition.Blueprint} at {Format(zone.PositionValue)}. Spawned {spawned} object(s).");
            return true;
        }

        private static int ResetZone(SiegeDefinition definition, SiegeZoneState zone)
        {
            if (ZoneSystem.instance != null)
            {
                ZoneSystem.instance.m_locationInstances.Remove(ZoneSystem.GetZone(zone.PositionValue));
            }

            int removed = ZDOMan.instance != null
                ? CreativeSessionManager.DestroyCreativeZoneZdos(zone.PositionValue, zone.RadiusValue)
                : 0;

            if (!CreativeSessionManager.TryEnsureCreativeLocation(zone.PositionValue, zone.SlotId, out string error))
            {
                ValheimCreativePlugin.ModLogger.LogWarning($"Could not restore siege location for {definition.Id}: {error}");
            }

            return removed;
        }

        private static SiegeZoneState GetOrCreateZone(SiegeDefinition definition)
        {
            if (ZonesById.TryGetValue(definition.Id, out SiegeZoneState zone))
            {
                return zone;
            }

            Vector3 position = GetPosition(definition);
            zone = new SiegeZoneState
            {
                Id = definition.Id,
                SlotId = $"siege_{definition.Id}",
                Position = Format(position),
                Radius = definition.RadiusOrDefault,
                Loaded = false,
                LoadedAt = string.Empty
            };
            ZonesById[definition.Id] = zone;
            Save();
            return zone;
        }

        private static bool TryGetDefinition(string siegeId, out SiegeDefinition definition, out string error)
        {
            string id = NormalizeId(siegeId);
            if (DefinitionsById.TryGetValue(id, out definition))
            {
                error = string.Empty;
                return true;
            }

            error = $"Siege was not found: {id}.";
            return false;
        }

        private static Vector3 GetPosition(SiegeDefinition definition)
        {
            if (!string.IsNullOrWhiteSpace(definition.Position) && TryParseVector(definition.Position, out Vector3 configured))
            {
                return configured;
            }

            int slotIndex = definition.SlotIndex;
            float spacing = Mathf.Max(64f, ModConfig.SiegeZoneSpacing.Value);
            return ModConfig.SiegePositionValue + new Vector3(slotIndex * spacing, 0f, 0f);
        }

        private static Heightmap.Biome ParseBiome(string raw)
        {
            return CreativeBiomeService.TryParseBiome(raw, out Heightmap.Biome biome)
                ? biome
                : CreativeBiomeService.DefaultBiome;
        }

        private static List<SiegeDefinition> LoadDefinitions(string path)
        {
            if (!File.Exists(path))
            {
                return new List<SiegeDefinition>();
            }

            SiegeDefinitionFile file = JsonConvert.DeserializeObject<SiegeDefinitionFile>(File.ReadAllText(path)) ?? new SiegeDefinitionFile();
            return file.Sieges ?? new List<SiegeDefinition>();
        }

        private static List<SiegeZoneState> LoadState(string path)
        {
            if (!File.Exists(path))
            {
                return new List<SiegeZoneState>();
            }

            SiegeStateFile file = JsonConvert.DeserializeObject<SiegeStateFile>(File.ReadAllText(path)) ?? new SiegeStateFile();
            return file.Zones ?? new List<SiegeZoneState>();
        }

        private static void WriteJson<T>(string path, T value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonConvert.SerializeObject(value, Formatting.Indented));
        }

        private static string GetConfigPath(string raw, string fallback)
        {
            string path = string.IsNullOrWhiteSpace(raw) ? fallback : raw.Trim();
            return Path.IsPathRooted(path) ? path : Path.Combine(Paths.ConfigPath, path);
        }

        private static string NormalizeId(string raw)
        {
            return raw.Trim().ToLowerInvariant();
        }

        private static string Format(Vector3 value)
        {
            return string.Join(
                ",",
                value.x.ToString(CultureInfo.InvariantCulture),
                value.y.ToString(CultureInfo.InvariantCulture),
                value.z.ToString(CultureInfo.InvariantCulture));
        }

        private static bool TryParseVector(string raw, out Vector3 value)
        {
            value = Vector3.zero;
            string[] parts = raw.Split(',');
            if (parts.Length != 3 ||
                !float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                !float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
                !float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
            {
                return false;
            }

            value = new Vector3(x, y, z);
            return true;
        }

        private sealed class SiegeDefinitionFile
        {
            [JsonProperty("schema")]
            public int Schema { get; set; } = 1;

            [JsonProperty("sieges")]
            public List<SiegeDefinition> Sieges { get; set; } = new();
        }

        private sealed class SiegeStateFile
        {
            [JsonProperty("schema")]
            public int Schema { get; set; } = 1;

            [JsonProperty("zones")]
            public List<SiegeZoneState> Zones { get; set; } = new();
        }

        private sealed class SiegeDefinition
        {
            [JsonProperty("id")]
            public string Id { get; set; } = string.Empty;

            [JsonProperty("displayName")]
            public string DisplayName { get; set; } = string.Empty;

            [JsonProperty("blueprint")]
            public string Blueprint { get; set; } = string.Empty;

            [JsonProperty("biome")]
            public string Biome { get; set; } = string.Empty;

            [JsonProperty("slotIndex")]
            public int SlotIndex { get; set; } = 0;

            [JsonProperty("position")]
            public string Position { get; set; } = string.Empty;

            [JsonProperty("radius")]
            public float Radius { get; set; }

            [JsonProperty("resetBeforeLoad")]
            public bool ResetBeforeLoad { get; set; } = true;

            [JsonProperty("grantCreativeKeys")]
            public bool GrantCreativeKeys { get; set; } = false;

            [JsonIgnore]
            public string DisplayNameOrId => string.IsNullOrWhiteSpace(DisplayName) ? Id : DisplayName;

            [JsonIgnore]
            public string BiomeOrDefault => string.IsNullOrWhiteSpace(Biome) ? CreativeBiomeService.DefaultBiome.ToString() : Biome;

            [JsonIgnore]
            public float RadiusOrDefault => Radius > 0f ? Radius : ModConfig.DefaultSiegeZoneRadiusValue;
        }

        private sealed class SiegeZoneState
        {
            [JsonProperty("id")]
            public string Id { get; set; } = string.Empty;

            [JsonProperty("slotId")]
            public string SlotId { get; set; } = string.Empty;

            [JsonProperty("position")]
            public string Position { get; set; } = "0,0,0";

            [JsonProperty("radius")]
            public float Radius { get; set; }

            [JsonProperty("loaded")]
            public bool Loaded { get; set; }

            [JsonProperty("loadedAt")]
            public string LoadedAt { get; set; } = string.Empty;

            [JsonIgnore]
            public Vector3 PositionValue => TryParseVector(Position, out Vector3 value) ? value : Vector3.zero;

            [JsonIgnore]
            public float RadiusValue => Radius > 0f ? Radius : ModConfig.DefaultSiegeZoneRadiusValue;
        }
    }
}

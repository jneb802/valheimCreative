using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using ValheimCreative.Configuration;

namespace ValheimCreative.Features.Creative
{
    internal static class CreativeStateStore
    {
        internal static List<CreativeSession> LoadSessions(string path)
        {
            if (!File.Exists(path))
            {
                return new List<CreativeSession>();
            }

            SessionState state = ReadJson<SessionState>(path);
            return state.Sessions.Select(record => record.ToSession()).ToList();
        }

        internal static void SaveSessions(string path, IEnumerable<CreativeSession> sessions)
        {
            WriteJson(
                path,
                new SessionState
                {
                    Sessions = sessions.Select(SessionRecord.FromSession).ToList()
                });
        }

        internal static List<CreativeZone> LoadZones(string path)
        {
            if (!File.Exists(path))
            {
                return new List<CreativeZone>();
            }

            ZoneState state = ReadJson<ZoneState>(path);
            return state.Zones.Select(record => record.ToZone()).ToList();
        }

        internal static void SaveZones(string path, IEnumerable<CreativeZone> zones)
        {
            WriteJson(
                path,
                new ZoneState
                {
                    Zones = zones.Select(ZoneRecord.FromZone).ToList()
                });
        }

        private static T ReadJson<T>(string path) where T : new()
        {
            return JsonConvert.DeserializeObject<T>(File.ReadAllText(path)) ?? new T();
        }

        private static void WriteJson<T>(string path, T value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonConvert.SerializeObject(value, Formatting.Indented));
        }

        private sealed class SessionState
        {
            [JsonProperty("schema")]
            public int Schema { get; set; } = 1;

            [JsonProperty("sessions")]
            public List<SessionRecord> Sessions { get; set; } = new();
        }

        private sealed class ZoneState
        {
            [JsonProperty("schema")]
            public int Schema { get; set; } = 1;

            [JsonProperty("zones")]
            public List<ZoneRecord> Zones { get; set; } = new();
        }

        private sealed class SessionRecord
        {
            [JsonProperty("playerId")]
            public long PlayerId { get; set; }

            [JsonProperty("peerId")]
            public long PeerId { get; set; }

            [JsonProperty("playerName")]
            public string PlayerName { get; set; } = string.Empty;

            [JsonProperty("ownerPlayerId")]
            public long OwnerPlayerId { get; set; }

            [JsonProperty("slotId")]
            public string SlotId { get; set; } = string.Empty;

            [JsonProperty("creativePosition")]
            public string CreativePosition { get; set; } = "0,0,0";

            [JsonProperty("creativeRotation")]
            public string CreativeRotation { get; set; } = "0,0,0";

            [JsonProperty("creativeBiome")]
            public string CreativeBiome { get; set; } = string.Empty;

            [JsonProperty("zoneRadius")]
            public float ZoneRadius { get; set; }

            [JsonProperty("returnPosition")]
            public string ReturnPosition { get; set; } = "0,0,0";

            [JsonProperty("returnRotation")]
            public string ReturnRotation { get; set; } = "0,0,0";

            [JsonProperty("awaitingRespawn")]
            public bool AwaitingRespawn { get; set; }

            [JsonProperty("grantCreativeKeys")]
            public bool GrantCreativeKeys { get; set; } = true;

            public CreativeSession ToSession()
            {
                return new CreativeSession(
                    PlayerId,
                    PeerId,
                    PlayerName,
                    OwnerPlayerId,
                    SlotId,
                    ParseVector(CreativePosition),
                    Quaternion.Euler(ParseVector(CreativeRotation)),
                    ParseBiome(CreativeBiome),
                    RadiusOrDefault(ZoneRadius, ModConfig.DefaultCreativeZoneRadiusValue),
                    ParseVector(ReturnPosition),
                    Quaternion.Euler(ParseVector(ReturnRotation)),
                    GrantCreativeKeys)
                {
                    AwaitingRespawn = AwaitingRespawn
                };
            }

            public static SessionRecord FromSession(CreativeSession session)
            {
                return new SessionRecord
                {
                    PlayerId = session.PlayerId,
                    PeerId = session.PeerId,
                    PlayerName = session.PlayerName,
                    OwnerPlayerId = session.OwnerPlayerId,
                    SlotId = session.SlotId,
                    CreativePosition = Format(session.CreativePosition),
                    CreativeRotation = Format(session.CreativeRotation.eulerAngles),
                    CreativeBiome = session.CreativeBiome.ToString(),
                    ZoneRadius = session.ZoneRadius,
                    ReturnPosition = Format(session.ReturnPosition),
                    ReturnRotation = Format(session.ReturnRotation.eulerAngles),
                    AwaitingRespawn = session.AwaitingRespawn,
                    GrantCreativeKeys = session.GrantCreativeKeys
                };
            }
        }

        private sealed class ZoneRecord
        {
            [JsonProperty("ownerPlayerId")]
            public long OwnerPlayerId { get; set; }

            [JsonProperty("ownerPlayerName")]
            public string OwnerPlayerName { get; set; } = string.Empty;

            [JsonProperty("slotIndex")]
            public int SlotIndex { get; set; }

            [JsonProperty("slotId")]
            public string SlotId { get; set; } = string.Empty;

            [JsonProperty("position")]
            public string Position { get; set; } = "0,0,0";

            [JsonProperty("biome")]
            public string Biome { get; set; } = string.Empty;

            [JsonProperty("radius")]
            public float Radius { get; set; }

            [JsonProperty("terrainMode")]
            public string TerrainMode { get; set; } = string.Empty;

            [JsonProperty("terrainSourceCenter")]
            public string TerrainSourceCenter { get; set; } = string.Empty;

            [JsonProperty("terrainSourceBiome")]
            public string TerrainSourceBiome { get; set; } = string.Empty;

            [JsonProperty("terrainSourceWorldSeed")]
            public int TerrainSourceWorldSeed { get; set; }

            [JsonProperty("terrainSourceWorldSeedName")]
            public string TerrainSourceWorldSeedName { get; set; } = string.Empty;

            public CreativeZone ToZone()
            {
                CreativeTerrainSource? terrainSource = null;
                if (!string.IsNullOrWhiteSpace(TerrainSourceCenter))
                {
                    terrainSource = new CreativeTerrainSource(
                        ParseVector(TerrainSourceCenter),
                        ParseBiome(TerrainSourceBiome),
                        TerrainSourceWorldSeed,
                        TerrainSourceWorldSeedName);
                }

                return new CreativeZone(
                    OwnerPlayerId,
                    OwnerPlayerName,
                    SlotIndex,
                    SlotId,
                    ParseVector(Position),
                    ParseBiome(Biome),
                    RadiusOrDefault(Radius, ModConfig.DefaultCreativeZoneRadiusValue),
                    ParseTerrainMode(TerrainMode),
                    terrainSource);
            }

            public static ZoneRecord FromZone(CreativeZone zone)
            {
                CreativeTerrainSource? terrainSource = zone.TerrainSource;
                return new ZoneRecord
                {
                    OwnerPlayerId = zone.OwnerPlayerId,
                    OwnerPlayerName = zone.OwnerPlayerName,
                    SlotIndex = zone.SlotIndex,
                    SlotId = zone.SlotId,
                    Position = Format(zone.Position),
                    Biome = zone.Biome.ToString(),
                    Radius = zone.Radius,
                    TerrainMode = zone.TerrainMode.ToString(),
                    TerrainSourceCenter = terrainSource != null ? Format(terrainSource.Center) : string.Empty,
                    TerrainSourceBiome = terrainSource != null ? terrainSource.Biome.ToString() : string.Empty,
                    TerrainSourceWorldSeed = terrainSource?.WorldSeed ?? 0,
                    TerrainSourceWorldSeedName = terrainSource?.WorldSeedName ?? string.Empty
                };
            }
        }

        private static string Format(Vector3 value)
        {
            return string.Join(
                ",",
                value.x.ToString(CultureInfo.InvariantCulture),
                value.y.ToString(CultureInfo.InvariantCulture),
                value.z.ToString(CultureInfo.InvariantCulture));
        }

        private static Vector3 ParseVector(string raw)
        {
            string[] parts = raw.Split(',');
            if (parts.Length != 3 ||
                !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
                !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
            {
                return Vector3.zero;
            }

            return new Vector3(x, y, z);
        }

        private static float RadiusOrDefault(float radius, float fallback)
        {
            return ModConfig.ClampCreativeZoneRadius(radius > 0f ? radius : fallback);
        }

        private static Heightmap.Biome ParseBiome(string raw)
        {
            return CreativeBiomeService.TryParseBiome(raw, out Heightmap.Biome biome)
                ? biome
                : CreativeBiomeService.DefaultBiome;
        }

        private static CreativeTerrainMode ParseTerrainMode(string raw)
        {
            return Enum.TryParse(raw, ignoreCase: true, out CreativeTerrainMode mode)
                ? mode
                : CreativeTerrainMode.FlatPad;
        }
    }
}

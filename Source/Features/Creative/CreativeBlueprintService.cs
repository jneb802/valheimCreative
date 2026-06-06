using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BepInEx;
using Newtonsoft.Json.Linq;
using UnityEngine;
using ValheimCreative.Configuration;

namespace ValheimCreative.Features.Creative
{
    internal static class CreativeBlueprintService
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static readonly int CreativeBlueprintHash = "valheimCreativeBlueprint".GetStableHashCode();
        private static readonly int CreativeBlueprintZoneHash = "valheimCreativeZone".GetStableHashCode();
        private static readonly int CreativeBlueprintDataHash = "valheimCreativeBlueprintData".GetStableHashCode();

        internal static string GetBlueprintDirectory()
        {
            string raw = ModConfig.BlueprintDirectory.Value.Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                raw = "expand_world/blueprints";
            }

            return Path.IsPathRooted(raw)
                ? raw
                : Path.Combine(Paths.ConfigPath, raw);
        }

        internal static string ResolveBlueprintPath(string fileName)
        {
            string safeName = NormalizeBlueprintName(fileName);
            return Path.Combine(GetBlueprintDirectory(), safeName);
        }

        internal static bool TryLoadBlueprint(
            string fileName,
            CreativeSession session,
            long creatorId,
            out int spawned,
            out List<string> missingPrefabs,
            out Heightmap.Biome? metadataBiome,
            out string error)
        {
            spawned = 0;
            missingPrefabs = new List<string>();
            metadataBiome = null;
            error = "";

            if (!IsRuntimeReady())
            {
                error = "Blueprint loading is not ready yet.";
                return false;
            }

            string path;
            try
            {
                path = ResolveBlueprintPath(fileName);
            }
            catch (ArgumentException ex)
            {
                error = ex.Message;
                return false;
            }

            if (!File.Exists(path))
            {
                error = $"Blueprint was not found: {Path.GetFileName(path)}.";
                return false;
            }

            string safeName = Path.GetFileName(path);
            BlueprintFile blueprint;
            try
            {
                blueprint = Parse(path);
            }
            catch (Exception ex)
            {
                error = $"Blueprint parse failed: {ex.Message}";
                return false;
            }

            Quaternion zoneRotation = session.CreativeRotation;
            Vector3 origin = session.CreativePosition;
            Vector3 loadAnchor = GetLoadAnchor(blueprint);
            BlueprintMetadata metadata = GetMetadata(safeName);
            metadataBiome = metadata.Biome;
            float loadYOffset = metadata.LoadYOffset;
            Vector3 loadOffset = new Vector3(0f, loadYOffset, 0f);
            HashSet<string> missing = new(StringComparer.Ordinal);
            foreach (BlueprintPieceEntry piece in blueprint.Pieces)
            {
                GameObject? prefab = ZNetScene.instance.GetPrefab(piece.PrefabName);
                ZNetView? nview = prefab != null ? prefab.GetComponent<ZNetView>() : null;
                if (prefab == null || nview == null)
                {
                    missing.Add(piece.PrefabName);
                    continue;
                }

                Vector3 position = origin + loadOffset + zoneRotation * (piece.LocalPosition - loadAnchor);
                Quaternion rotation = zoneRotation * piece.LocalRotation;
                GameObject instance = UnityEngine.Object.Instantiate(prefab, position, rotation);
                if (piece.Scale != Vector3.one)
                {
                    ZNetView instanceView = instance.GetComponent<ZNetView>();
                    if (instanceView != null)
                    {
                        instanceView.SetLocalScale(piece.Scale);
                    }
                    else
                    {
                        instance.transform.localScale = piece.Scale;
                    }
                }

                Piece placedPiece = instance.GetComponent<Piece>();
                if (placedPiece != null)
                {
                    placedPiece.SetCreator(creatorId);
                }

                instance.GetComponent<WearNTear>()?.OnPlaced();
                ZNetView? placedView = instance.GetComponent<ZNetView>();
                ZDO? zdo = placedView != null ? placedView.GetZDO() : null;
                if (zdo != null)
                {
                    zdo.Set(CreativeBlueprintHash, safeName);
                    zdo.Set(CreativeBlueprintZoneHash, session.SlotId);
                    if (!string.IsNullOrEmpty(piece.Data))
                    {
                        zdo.Set(CreativeBlueprintDataHash, piece.Data);
                    }
                }

                spawned++;
            }

            missingPrefabs = missing.OrderBy(name => name, StringComparer.Ordinal).ToList();
            return true;
        }

        private static Vector3 GetLoadAnchor(BlueprintFile blueprint)
        {
            if (blueprint.Pieces.Count == 0)
            {
                return Vector3.zero;
            }

            Vector3 min = blueprint.Pieces[0].LocalPosition;
            Vector3 max = blueprint.Pieces[0].LocalPosition;
            foreach (BlueprintPieceEntry piece in blueprint.Pieces)
            {
                min = Vector3.Min(min, piece.LocalPosition);
                max = Vector3.Max(max, piece.LocalPosition);
            }

            return new Vector3((min.x + max.x) * 0.5f, min.y, (min.z + max.z) * 0.5f);
        }

        private static BlueprintMetadata GetMetadata(string blueprintFileName)
        {
            string path = GetBlueprintMetadataPath();
            if (!File.Exists(path))
            {
                return BlueprintMetadata.Empty;
            }

            try
            {
                JObject root = JObject.Parse(File.ReadAllText(path));
                JToken? entry = FindMetadataEntry(root, blueprintFileName);
                if (entry == null)
                {
                    return BlueprintMetadata.Empty;
                }

                float loadYOffset = 0f;
                Heightmap.Biome? biome = null;
                if (entry.Type == JTokenType.Object)
                {
                    JToken? offsetToken = entry["loadYOffset"];
                    if (offsetToken != null && TryParseLoadYOffset(offsetToken, out float offset))
                    {
                        loadYOffset = offset;
                    }

                    JToken? biomeToken = entry["biome"];
                    if (biomeToken != null && CreativeBiomeService.TryParseBiome(biomeToken.ToString(), out Heightmap.Biome parsedBiome))
                    {
                        biome = parsedBiome;
                    }

                    return new BlueprintMetadata(loadYOffset, biome);
                }

                return TryParseLoadYOffset(entry, out float legacyOffset)
                    ? new BlueprintMetadata(legacyOffset, null)
                    : BlueprintMetadata.Empty;
            }
            catch (Exception ex)
            {
                if (ModConfig.DebugLogging.Value)
                {
                    ValheimCreativePlugin.ModLogger.LogWarning($"Could not read blueprint metadata from {path}: {ex.Message}");
                }
                return BlueprintMetadata.Empty;
            }
        }

        private static string GetBlueprintMetadataPath()
        {
            string fileName = Path.GetFileName(ModConfig.BlueprintMetadataFile.Value.Trim());
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = "blueprint-metadata.json";
            }

            return Path.Combine(GetBlueprintDirectory(), fileName);
        }

        private static JToken? FindMetadataEntry(JObject root, string blueprintFileName)
        {
            JObject? entries = root["blueprints"] as JObject ?? root;
            string normalizedFileName = blueprintFileName.Trim().ToLowerInvariant();
            string normalizedStem = Path.GetFileNameWithoutExtension(normalizedFileName);

            foreach (JProperty property in entries.Properties())
            {
                string key = property.Name.Trim().ToLowerInvariant();
                if (key == normalizedFileName || key == normalizedStem)
                {
                    return property.Value;
                }
            }

            return null;
        }

        private static bool TryParseLoadYOffset(JToken token, out float offset)
        {
            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
            {
                offset = token.Value<float>();
                return true;
            }

            return float.TryParse(token.ToString().Trim(), NumberStyles.Float, Invariant, out offset);
        }

        internal static bool TrySaveBlueprint(
            string fileName,
            CreativeSession session,
            long creatorId,
            string creatorName,
            Heightmap.Biome biome,
            out int saved,
            out string error)
        {
            saved = 0;
            error = "";

            if (!IsRuntimeReady())
            {
                error = "Blueprint saving is not ready yet.";
                return false;
            }

            string path;
            try
            {
                path = ResolveBlueprintPath(fileName);
            }
            catch (ArgumentException ex)
            {
                error = ex.Message;
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            List<BlueprintPieceEntry> pieces = CollectPieces(session, creatorId);
            if (pieces.Count == 0)
            {
                error = "No player-built pieces were found in this creative zone.";
                return false;
            }

            BlueprintFile blueprint = new()
            {
                Name = Path.GetFileNameWithoutExtension(path),
                Creator = creatorName,
                Description = "",
                Category = "Blueprints"
            };
            blueprint.Pieces.AddRange(pieces);
            Write(path, blueprint);
            string safeName = Path.GetFileName(path);
            BlueprintMetadata currentMetadata = GetMetadata(safeName);
            WriteMetadata(safeName, new BlueprintMetadata(currentMetadata.LoadYOffset, biome));
            saved = pieces.Count;
            return true;
        }

        private static void WriteMetadata(string blueprintFileName, BlueprintMetadata metadata)
        {
            string path = GetBlueprintMetadataPath();
            JObject root = ReadMetadataRoot(path);
            JObject blueprints = root["blueprints"] as JObject ?? new JObject();
            root["blueprints"] = blueprints;
            blueprints[blueprintFileName] = new JObject
            {
                ["loadYOffset"] = metadata.LoadYOffset,
                ["biome"] = metadata.Biome?.ToString() ?? CreativeBiomeService.DefaultBiome.ToString()
            };

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, root.ToString());
        }

        private static JObject ReadMetadataRoot(string path)
        {
            if (!File.Exists(path))
            {
                return new JObject();
            }

            try
            {
                return JObject.Parse(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                ValheimCreativePlugin.ModLogger.LogWarning($"Could not read blueprint metadata from {path}; replacing it. {ex.Message}");
                return new JObject();
            }
        }

        private static List<BlueprintPieceEntry> CollectPieces(CreativeSession session, long creatorId)
        {
            List<ZDO> objects = new();
            float maxDistance = Mathf.Max(1f, session.ZoneRadius);
            int sectorArea = Mathf.CeilToInt(maxDistance / ZoneSystem.c_ZoneSize) + 1;
            ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(session.CreativePosition), sectorArea, 0, objects);
            Quaternion inverseRotation = Quaternion.Inverse(session.CreativeRotation);
            float maxDistanceSquared = maxDistance * maxDistance;
            List<BlueprintPieceEntry> pieces = new();
            HashSet<ZDOID> seen = new();

            foreach (ZDO zdo in objects)
            {
                if (zdo == null || !zdo.IsValid() || !seen.Add(zdo.m_uid))
                {
                    continue;
                }

                Vector3 position = zdo.GetPosition();
                if ((position - session.CreativePosition).sqrMagnitude > maxDistanceSquared)
                {
                    continue;
                }

                GameObject prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
                if (prefab == null || prefab.GetComponent<Piece>() == null)
                {
                    continue;
                }

                if (zdo.GetLong(ZDOVars.s_creator) != creatorId)
                {
                    continue;
                }

                Vector3 localPosition = inverseRotation * (position - session.CreativePosition);
                Quaternion localRotation = inverseRotation * zdo.GetRotation();
                Vector3 scale = Vector3.one;
                ZNetView instance = ZNetScene.instance.FindInstance(zdo);
                if (instance != null)
                {
                    scale = instance.transform.localScale;
                }
                else if (zdo.GetVec3(ZDOVars.s_scaleHash, out Vector3 zdoScale))
                {
                    scale = zdoScale;
                }
                else
                {
                    float scalar = zdo.GetFloat(ZDOVars.s_scaleScalarHash, 1f);
                    scale = new Vector3(scalar, scalar, scalar);
                }

                string data = zdo.GetString(CreativeBlueprintDataHash, "");
                pieces.Add(new BlueprintPieceEntry(
                    prefab.name,
                    ResolvePieceCategory(prefab),
                    localPosition,
                    localRotation,
                    data,
                    scale));
            }

            return pieces
                .OrderBy(piece => piece.LocalPosition.y)
                .ThenBy(piece => piece.LocalPosition.x)
                .ThenBy(piece => piece.LocalPosition.z)
                .ToList();
        }

        private static string ResolvePieceCategory(GameObject prefab)
        {
            Piece piece = prefab.GetComponent<Piece>();
            if (piece == null)
            {
                return "";
            }

            return piece.m_category.ToString();
        }

        private static BlueprintFile Parse(string path)
        {
            BlueprintFile blueprint = new();
            bool inPieces = false;
            int lineNumber = 0;
            foreach (string rawLine in File.ReadLines(path))
            {
                lineNumber++;
                string line = rawLine.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                if (line.StartsWith("#", StringComparison.Ordinal))
                {
                    inPieces = line.Equals("#Pieces", StringComparison.OrdinalIgnoreCase);
                    ReadHeader(blueprint, line);
                    continue;
                }

                if (!inPieces)
                {
                    continue;
                }

                blueprint.Pieces.Add(ParsePiece(line, lineNumber));
            }

            if (blueprint.Pieces.Count == 0)
            {
                throw new InvalidDataException("no pieces were found");
            }

            return blueprint;
        }

        private static void ReadHeader(BlueprintFile blueprint, string line)
        {
            int separator = line.IndexOf(':');
            if (separator < 0)
            {
                return;
            }

            string key = line.Substring(1, separator - 1).Trim();
            string value = line.Substring(separator + 1).Trim().Trim('"');
            switch (key.ToLowerInvariant())
            {
                case "name":
                    blueprint.Name = value;
                    break;
                case "creator":
                    blueprint.Creator = value;
                    break;
                case "description":
                    blueprint.Description = value;
                    break;
                case "category":
                    blueprint.Category = value;
                    break;
            }
        }

        private static BlueprintPieceEntry ParsePiece(string line, int lineNumber)
        {
            string[] parts = line.Split(';');
            if (parts.Length < 13)
            {
                throw new InvalidDataException($"line {lineNumber} has {parts.Length} field(s), expected at least 13");
            }

            string prefabName = parts[0].Trim();
            if (string.IsNullOrWhiteSpace(prefabName))
            {
                throw new InvalidDataException($"line {lineNumber} has an empty prefab name");
            }

            return new BlueprintPieceEntry(
                prefabName,
                parts[1].Trim(),
                new Vector3(ParseFloat(parts[2], lineNumber), ParseFloat(parts[3], lineNumber), ParseFloat(parts[4], lineNumber)),
                new Quaternion(ParseFloat(parts[5], lineNumber), ParseFloat(parts[6], lineNumber), ParseFloat(parts[7], lineNumber), ParseFloat(parts[8], lineNumber)),
                parts[9],
                new Vector3(ParseFloat(parts[10], lineNumber), ParseFloat(parts[11], lineNumber), ParseFloat(parts[12], lineNumber)));
        }

        private static float ParseFloat(string raw, int lineNumber)
        {
            if (!float.TryParse(raw.Trim(), NumberStyles.Float, Invariant, out float value))
            {
                throw new InvalidDataException($"line {lineNumber} has invalid number '{raw}'");
            }

            return value;
        }

        private static void Write(string path, BlueprintFile blueprint)
        {
            List<string> lines = new()
            {
                "#Name:" + blueprint.Name,
                "#Creator:" + blueprint.Creator,
                "#Description:\"" + blueprint.Description.Replace("\"", "\\\"") + "\"",
                "#Category:" + blueprint.Category,
                "#Pieces"
            };

            lines.AddRange(blueprint.Pieces.Select(FormatPiece));
            File.WriteAllLines(path, lines);
        }

        private static string FormatPiece(BlueprintPieceEntry piece)
        {
            return string.Join(";",
                piece.PrefabName,
                piece.Category,
                FormatFloat(piece.LocalPosition.x),
                FormatFloat(piece.LocalPosition.y),
                FormatFloat(piece.LocalPosition.z),
                FormatFloat(piece.LocalRotation.x),
                FormatFloat(piece.LocalRotation.y),
                FormatFloat(piece.LocalRotation.z),
                FormatFloat(piece.LocalRotation.w),
                piece.Data,
                FormatFloat(piece.Scale.x),
                FormatFloat(piece.Scale.y),
                FormatFloat(piece.Scale.z));
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("G9", Invariant);
        }

        private static string NormalizeBlueprintName(string fileName)
        {
            string name = fileName.Trim();
            if (name.EndsWith(".blueprint", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - ".blueprint".Length);
            }

            if (name.Length == 0)
            {
                throw new ArgumentException("Blueprint name is required.");
            }

            if (name.Length > 80)
            {
                throw new ArgumentException("Blueprint name is too long.");
            }

            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool valid = c is >= 'a' and <= 'z' ||
                             c is >= 'A' and <= 'Z' ||
                             c is >= '0' and <= '9' ||
                             c == '_' ||
                             c == '-';
                if (!valid)
                {
                    throw new ArgumentException("Blueprint names may only contain letters, numbers, underscores, and hyphens.");
                }
            }

            return name.ToLowerInvariant() + ".blueprint";
        }

        private static bool IsRuntimeReady()
        {
            return ZNet.instance != null &&
                   ZNet.instance.IsServer() &&
                   ZNetScene.instance != null &&
                   ZDOMan.instance != null &&
                   ZoneSystem.instance != null;
        }

        private readonly struct BlueprintMetadata
        {
            internal static readonly BlueprintMetadata Empty = new(0f, null);

            internal BlueprintMetadata(float loadYOffset, Heightmap.Biome? biome)
            {
                LoadYOffset = loadYOffset;
                Biome = biome;
            }

            internal float LoadYOffset { get; }
            internal Heightmap.Biome? Biome { get; }
        }
    }
}

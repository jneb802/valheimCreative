using BepInEx.Configuration;
using System.Globalization;
using UnityEngine;

namespace ValheimCreative.Configuration
{
    internal static class ModConfig
    {
        private static ConfigFile? BoundConfig;
        internal static ConfigEntry<bool> EnableCreativeCommands = null!;
        internal static ConfigEntry<string> CreativeCommand = null!;
        internal static ConfigEntry<string> ReturnCommand = null!;
        internal static ConfigEntry<string> CreativeSlotId = null!;
        internal static ConfigEntry<string> CreativePosition = null!;
        internal static ConfigEntry<string> CreativeRotation = null!;
        internal static ConfigEntry<float> CreativeZoneSpacing = null!;
        internal static ConfigEntry<string> DefaultCreativeTerrainMode = null!;
        internal static ConfigEntry<float> CreativeTerrainSourceMinDistance = null!;
        internal static ConfigEntry<float> CreativeTerrainSourceMaxDistance = null!;
        internal static ConfigEntry<float> CreativeTerrainSourceMinHeight = null!;
        internal static ConfigEntry<string> CreativeTerrainSourceMinHeightByBiome = null!;
        internal static ConfigEntry<float> CreativeTerrainSourceMaxSpawnSlopeDegrees = null!;
        internal static ConfigEntry<float> CreativeTerrainSourceValidationSampleSpacing = null!;
        internal static ConfigEntry<int> CreativeTerrainSourceSearchAttempts = null!;
        internal static ConfigEntry<float> CreativeTerrainEdgeFalloffWidth = null!;
        internal static ConfigEntry<float> CreativeTerrainEdgeFloorHeight = null!;
        internal static ConfigEntry<bool> EnableCreativeEnvironmentHotReload = null!;
        internal static ConfigEntry<float> CreativeRegenerateCooldownSeconds = null!;
        internal static ConfigEntry<bool> SpawnCreativeLocation = null!;
        internal static ConfigEntry<string> CreativeLocationPrefab = null!;
        internal static ConfigEntry<bool> IncludeNoWorkbench = null!;
        internal static ConfigEntry<bool> IncludeNoCraftCost = null!;
        internal static ConfigEntry<bool> RequireEmptyInventory = null!;
        internal static ConfigEntry<float> InventoryCheckTimeoutSeconds = null!;
        internal static ConfigEntry<float> DeathRecoveryCheckSeconds = null!;
        internal static ConfigEntry<string> DefaultCreativeBiome = null!;
        internal static ConfigEntry<float> DefaultCreativeZoneRadius = null!;
        internal static ConfigEntry<float> MaxCreativeZoneRadius = null!;
        internal static ConfigEntry<string> SessionFile = null!;
        internal static ConfigEntry<string> ZoneFile = null!;
        internal static ConfigEntry<string> BlueprintDirectory = null!;
        internal static ConfigEntry<string> BlueprintMetadataFile = null!;
        internal static ConfigEntry<string> SiegeDefinitionsFile = null!;
        internal static ConfigEntry<string> SiegeStateFile = null!;
        internal static ConfigEntry<string> SiegePosition = null!;
        internal static ConfigEntry<float> SiegeZoneSpacing = null!;
        internal static ConfigEntry<float> DefaultSiegeZoneRadius = null!;
        internal static ConfigEntry<bool> DebugLogging = null!;

        internal static void Bind(ConfigFile config)
        {
            BoundConfig = config;

            EnableCreativeCommands = config.Bind(
                "Creative",
                "EnableCreativeCommands",
                true,
                "Enables chat commands for creative sessions.");

            CreativeCommand = config.Bind(
                "Creative",
                "CreativeCommand",
                "!creative",
                "Chat command used to enter the creative zone.");

            ReturnCommand = config.Bind(
                "Creative",
                "ReturnCommand",
                "!return",
                "Chat command used to leave the creative zone.");

            CreativeSlotId = config.Bind(
                "Creative",
                "CreativeSlotId",
                "creative_slot_01",
                "Identifier saved with active creative sessions.");

            CreativePosition = config.Bind(
                "Creative",
                "CreativePosition",
                "0,45,-13000",
                "First creative zone origin position as x,y,z.");

            CreativeRotation = config.Bind(
                "Creative",
                "CreativeRotation",
                "0,0,0",
                "Creative zone rotation as x,y,z Euler angles.");

            CreativeZoneSpacing = config.Bind(
                "Creative",
                "CreativeZoneSpacing",
                1920f,
                "Distance between creative zone centers. Default is 30 * 64.");

            DefaultCreativeTerrainMode = config.Bind(
                "Creative",
                "DefaultCreativeTerrainMode",
                "WorldSeedPatch",
                "Default terrain mode for newly allocated creative zones. FlatPad uses the packaged level terrain modifier. WorldSeedPatch keeps zones outside the map but makes PraetorisClient sample an in-world terrain patch. Existing saved zones keep their saved terrainMode.");

            CreativeTerrainSourceMinDistance = config.Bind(
                "Creative",
                "CreativeTerrainSourceMinDistance",
                1000f,
                "Minimum world-center distance for random terrain source patch selection in WorldSeedPatch mode.");

            CreativeTerrainSourceMaxDistance = config.Bind(
                "Creative",
                "CreativeTerrainSourceMaxDistance",
                8500f,
                "Maximum world-center distance for random terrain source patch selection in WorldSeedPatch mode.");

            CreativeTerrainSourceMinHeight = config.Bind(
                "Creative",
                "CreativeTerrainSourceMinHeight",
                32f,
                "Fallback minimum source terrain height for random terrain source patch selection in WorldSeedPatch mode. Biome-specific values override this.");

            CreativeTerrainSourceMinHeightByBiome = config.Bind(
                "Creative",
                "CreativeTerrainSourceMinHeightByBiome",
                "Meadows=32,BlackForest=35,Swamp=31,Mountain=90,Plains=32,Mistlands=45,AshLands=32,DeepNorth=35",
                "Comma-separated biome minimum source heights for WorldSeedPatch mode, in world Y units. Example: Meadows=32,Mountain=90. Missing biomes use CreativeTerrainSourceMinHeight.");

            CreativeTerrainSourceMaxSpawnSlopeDegrees = config.Bind(
                "Creative",
                "CreativeTerrainSourceMaxSpawnSlopeDegrees",
                30f,
                "Maximum sampled slope in degrees around the creative zone spawn point when selecting a WorldSeedPatch terrain source. Higher values allow steeper terrain.");

            CreativeTerrainSourceValidationSampleSpacing = config.Bind(
                "Creative",
                "CreativeTerrainSourceValidationSampleSpacing",
                16f,
                "Sample spacing in meters for validating the copied WorldSeedPatch terrain footprint. Lower values are stricter and more expensive.");

            CreativeTerrainSourceSearchAttempts = config.Bind(
                "Creative",
                "CreativeTerrainSourceSearchAttempts",
                2000,
                "Maximum random candidate count when selecting a biome-matched source terrain patch in WorldSeedPatch mode.");

            CreativeTerrainEdgeFalloffWidth = config.Bind(
                "Creative",
                "CreativeTerrainEdgeFalloffWidth",
                16f,
                "Width in meters of the WorldSeedPatch edge ring that blends sampled terrain down to CreativeTerrainEdgeFloorHeight. Set 0 to disable the edge wall.");

            CreativeTerrainEdgeFloorHeight = config.Bind(
                "Creative",
                "CreativeTerrainEdgeFloorHeight",
                0f,
                "World Y height that the WorldSeedPatch edge falloff ring blends down to.");

            EnableCreativeEnvironmentHotReload = config.Bind(
                "Creative",
                "EnableCreativeEnvironmentHotReload",
                false,
                "Reloads valheimCreative.environment.yaml while the server is running. Disabled by default because vegetation refresh scans creative zone objects.");

            CreativeRegenerateCooldownSeconds = config.Bind(
                "Creative",
                "CreativeRegenerateCooldownSeconds",
                300f,
                "Minimum seconds between player-triggered creative zone reset or biome regeneration actions per creative zone owner. Set 0 to disable.");

            SpawnCreativeLocation = config.Bind(
                "Creative",
                "SpawnCreativeLocation",
                true,
                "Spawns and registers the configured creative location the first time a player enters creative mode.");

            CreativeLocationPrefab = config.Bind(
                "Creative",
                "CreativeLocationPrefab",
                "StartTemple:valheim_creative",
                "Location prefab id to spawn at the creative zone. Requires the matching Expand World Data location config.");

            IncludeNoWorkbench = config.Bind(
                "Creative",
                "IncludeNoWorkbench",
                true,
                "Adds a targeted NoWorkbench key during creative sessions so known station-gated build pieces can be placed.");

            IncludeNoCraftCost = config.Bind(
                "Creative",
                "IncludeNoCraftCost",
                true,
                "Adds a targeted NoCraftCost key during creative sessions. This is needed for build pieces whose free-build key is NoCraftCost.");

            RequireEmptyInventory = config.Bind(
                "Creative",
                "RequireEmptyInventory",
                true,
                "Requires the client inventory and Shudnal ExtraSlots to be empty before entering or leaving creative zones.");

            InventoryCheckTimeoutSeconds = config.Bind(
                "Creative",
                "InventoryCheckTimeoutSeconds",
                5.0f,
                "How long the server waits for the DiscordTools client inventory response before blocking the command.");

            DeathRecoveryCheckSeconds = config.Bind(
                "Creative",
                "DeathRecoveryCheckSeconds",
                1.0f,
                "How often active creative sessions are checked for death and respawn recovery.");

            DefaultCreativeBiome = config.Bind(
                "Creative",
                "DefaultCreativeBiome",
                "Meadows",
                "Default biome mask applied to new creative zones.");

            DefaultCreativeZoneRadius = config.Bind(
                "Creative",
                "DefaultCreativeZoneRadius",
                128f,
                "Default creative zone footprint radius in meters. Used for biome paint, reset cleanup, and blueprint save range.");

            MaxCreativeZoneRadius = config.Bind(
                "Creative",
                "MaxCreativeZoneRadius",
                128f,
                "Maximum creative zone footprint radius in meters. Admin size commands above this value are rejected to avoid expensive terrain and vegetation generation.");

            SessionFile = config.Bind(
                "Creative",
                "SessionFile",
                "valheimCreative.sessions.json",
                "JSON session file path. Relative paths are resolved from BepInEx/config.");

            ZoneFile = config.Bind(
                "Creative",
                "ZoneFile",
                "valheimCreative.zones.json",
                "JSON creative zone allocation file path. Relative paths are resolved from BepInEx/config.");

            BlueprintDirectory = config.Bind(
                "Blueprints",
                "BlueprintDirectory",
                "expand_world/blueprints",
                "Blueprint directory. Relative paths are resolved from BepInEx/config.");

            BlueprintMetadataFile = config.Bind(
                "Blueprints",
                "BlueprintMetadataFile",
                "blueprint-metadata.json",
                "JSON file in the blueprint directory that maps blueprint filenames to loadYOffset and biome values.");

            SiegeDefinitionsFile = config.Bind(
                "Sieges",
                "SiegeDefinitionsFile",
                "valheimCreative.sieges.json",
                "JSON siege definition file path. Relative paths are resolved from BepInEx/config.");

            SiegeStateFile = config.Bind(
                "Sieges",
                "SiegeStateFile",
                "valheimCreative.siege-zones.json",
                "JSON siege zone runtime state file path. Relative paths are resolved from BepInEx/config.");

            SiegePosition = config.Bind(
                "Sieges",
                "SiegePosition",
                "0,45,-16000",
                "First siege zone origin position as x,y,z.");

            SiegeZoneSpacing = config.Bind(
                "Sieges",
                "SiegeZoneSpacing",
                256f,
                "Distance between siege zone centers.");

            DefaultSiegeZoneRadius = config.Bind(
                "Sieges",
                "DefaultSiegeZoneRadius",
                128f,
                "Default siege zone footprint radius in meters. Used for biome paint and reset cleanup.");

            DebugLogging = config.Bind(
                "Creative",
                "DebugLogging",
                false,
                "Logs creative session decisions.");
        }

        internal static void Save()
        {
            BoundConfig?.Save();
        }

        internal static Vector3 CreativePositionValue => ParseVector3(CreativePosition.Value, new Vector3(0f, 45f, -13000f));

        internal static Quaternion CreativeRotationValue => Quaternion.Euler(ParseVector3(CreativeRotation.Value, Vector3.zero));

        internal static Vector3 SiegePositionValue => ParseVector3(SiegePosition.Value, new Vector3(0f, 45f, -16000f));

        internal static float DefaultCreativeZoneRadiusValue => ClampCreativeZoneRadius(DefaultCreativeZoneRadius.Value);

        internal static float MaxCreativeZoneRadiusValue => Mathf.Max(1f, MaxCreativeZoneRadius.Value);

        internal static float ClampCreativeZoneRadius(float radius)
        {
            return Mathf.Clamp(radius, 1f, MaxCreativeZoneRadiusValue);
        }

        internal static float DefaultSiegeZoneRadiusValue => Mathf.Max(1f, DefaultSiegeZoneRadius.Value);

        internal static float CreativeTerrainEdgeFalloffWidthValue => Mathf.Max(0f, CreativeTerrainEdgeFalloffWidth.Value);

        private static Vector3 ParseVector3(string raw, Vector3 fallback)
        {
            string[] parts = raw.Split(',');
            if (parts.Length != 3 ||
                !float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                !float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
                !float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
            {
                return fallback;
            }

            return new Vector3(x, y, z);
        }
    }
}

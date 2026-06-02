using BepInEx.Configuration;
using System.Globalization;
using UnityEngine;

namespace ValheimCreative.Configuration
{
    internal static class ModConfig
    {
        internal static ConfigEntry<bool> EnableCreativeCommands = null!;
        internal static ConfigEntry<bool> RequireBed = null!;
        internal static ConfigEntry<string> CreativeCommand = null!;
        internal static ConfigEntry<string> ReturnCommand = null!;
        internal static ConfigEntry<string> CreativeSlotId = null!;
        internal static ConfigEntry<string> CreativePosition = null!;
        internal static ConfigEntry<string> CreativeRotation = null!;
        internal static ConfigEntry<float> CreativeZoneSpacing = null!;
        internal static ConfigEntry<bool> SpawnCreativeLocation = null!;
        internal static ConfigEntry<string> CreativeLocationPrefab = null!;
        internal static ConfigEntry<bool> IncludeNoWorkbench = null!;
        internal static ConfigEntry<bool> IncludeNoCraftCost = null!;
        internal static ConfigEntry<bool> RequireEmptyInventory = null!;
        internal static ConfigEntry<float> InventoryCheckTimeoutSeconds = null!;
        internal static ConfigEntry<float> DeathRecoveryCheckSeconds = null!;
        internal static ConfigEntry<string> SessionFile = null!;
        internal static ConfigEntry<string> ZoneFile = null!;
        internal static ConfigEntry<string> BlueprintDirectory = null!;
        internal static ConfigEntry<float> BlueprintSaveRadius = null!;
        internal static ConfigEntry<bool> DebugLogging = null!;

        internal static void Bind(ConfigFile config)
        {
            EnableCreativeCommands = config.Bind(
                "Creative",
                "EnableCreativeCommands",
                true,
                "Enables chat commands for creative sessions.");

            RequireBed = config.Bind(
                "Creative",
                "RequireBed",
                true,
                "Requires the player to be in a bed before !creative starts a session.");

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
                192f,
                "Distance between creative zone centers. Default is 3 * 64.");

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

            BlueprintSaveRadius = config.Bind(
                "Blueprints",
                "BlueprintSaveRadius",
                128f,
                "Maximum distance from the creative zone origin included by !creative save.");

            DebugLogging = config.Bind(
                "Creative",
                "DebugLogging",
                false,
                "Logs creative session decisions.");
        }

        internal static Vector3 CreativePositionValue => ParseVector3(CreativePosition.Value, new Vector3(0f, 45f, -13000f));

        internal static Quaternion CreativeRotationValue => Quaternion.Euler(ParseVector3(CreativeRotation.Value, Vector3.zero));

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

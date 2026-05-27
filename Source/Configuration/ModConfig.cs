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
        internal static ConfigEntry<bool> IncludeNoWorkbench = null!;
        internal static ConfigEntry<bool> IncludeNoCraftCost = null!;
        internal static ConfigEntry<float> DeathRecoveryCheckSeconds = null!;
        internal static ConfigEntry<string> SessionFile = null!;
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
                "0,45,-12000",
                "Creative zone position as x,y,z.");

            CreativeRotation = config.Bind(
                "Creative",
                "CreativeRotation",
                "0,0,0",
                "Creative zone rotation as x,y,z Euler angles.");

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

            DeathRecoveryCheckSeconds = config.Bind(
                "Creative",
                "DeathRecoveryCheckSeconds",
                1.0f,
                "How often active creative sessions are checked for death and respawn recovery.");

            SessionFile = config.Bind(
                "Creative",
                "SessionFile",
                "valheimCreative.sessions.tsv",
                "Session file path. Relative paths are resolved from BepInEx/config.");

            DebugLogging = config.Bind(
                "Creative",
                "DebugLogging",
                false,
                "Logs creative session decisions.");
        }

        internal static Vector3 CreativePositionValue => ParseVector3(CreativePosition.Value, new Vector3(0f, 45f, -12000f));

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

using System;
using System.Globalization;
using HarmonyLib;

namespace ValheimCreative.Features.Creative
{
    [HarmonyPatch(typeof(Terminal), nameof(Terminal.InitTerminal))]
    internal static class CreativeConsoleCommands
    {
        private const string LoadPlayerCommand = "creative_load_player";
        private const string BlueprintOffsetCommand = "creative_blueprint_offset";
        private const string ZoneSizesCommand = "creative_zone_sizes";
        private const string ZoneSizeCommand = "creative_zone_size";
        private const string ZoneTerrainCommand = "creative_zone_terrain";
        private const string ZoneMigrateSpacingCommand = "creative_zone_migrate_spacing";
        private const string SiegeListCommand = "creative_siege_list";
        private const string SiegeStatusCommand = "creative_siege_status";
        private const string SiegeSizeCommand = "creative_siege_size";
        private const string SiegeLoadCommand = "creative_siege_load";
        private const string SiegeResetCommand = "creative_siege_reset";
        private const string SiegeEnterCommand = "creative_siege_enter";
        private static bool _registered;

        private static void Postfix()
        {
            Register();
        }

        internal static void Register()
        {
            if (_registered &&
                Terminal.commands.ContainsKey(LoadPlayerCommand) &&
                Terminal.commands.ContainsKey(BlueprintOffsetCommand) &&
                Terminal.commands.ContainsKey(SiegeEnterCommand) &&
                Terminal.commands.ContainsKey(SiegeSizeCommand) &&
                Terminal.commands.ContainsKey(ZoneMigrateSpacingCommand) &&
                Terminal.commands.ContainsKey(ZoneTerrainCommand))
            {
                return;
            }

            _ = new Terminal.ConsoleCommand(
                LoadPlayerCommand,
                "Load a blueprint into an online player's active creative zone. Usage: creative_load_player <playerIdOrPlatformId> <blueprintName>",
                args =>
                {
                    if (args.Length < 3)
                    {
                        args.Context.AddString("Usage: creative_load_player <playerIdOrPlatformId> <blueprintName>");
                        return;
                    }

                    if (!args.TryParameterLong(1, out long playerId) || playerId == 0L)
                    {
                        args.Context.AddString("playerIdOrPlatformId must be a non-zero number.");
                        return;
                    }

                    string blueprintName = args[2].Trim();
                    if (string.IsNullOrWhiteSpace(blueprintName))
                    {
                        args.Context.AddString("blueprintName is required.");
                        return;
                    }

                    foreach (string line in CreativeSessionManager.LoadBlueprintForPlayerId(playerId, blueprintName))
                    {
                        args.Context.AddString(line);
                    }
                });

            _ = new Terminal.ConsoleCommand(
                BlueprintOffsetCommand,
                "Show or set a blueprint load Y offset. Usage: creative_blueprint_offset <blueprintName> [loadYOffset]",
                args =>
                {
                    if (!RequireServer(args))
                    {
                        return;
                    }

                    if (args.Length < 2)
                    {
                        args.Context.AddString("Usage: creative_blueprint_offset <blueprintName> [loadYOffset]");
                        return;
                    }

                    string blueprintName = args[1].Trim();
                    if (string.IsNullOrWhiteSpace(blueprintName))
                    {
                        args.Context.AddString("blueprintName is required.");
                        return;
                    }

                    if (args.Length == 2)
                    {
                        if (CreativeBlueprintService.TryGetBlueprintLoadYOffset(blueprintName, out float currentOffset, out string currentSafeName, out string getError))
                        {
                            args.Context.AddString($"{currentSafeName} loadYOffset={currentOffset.ToString("G9", CultureInfo.InvariantCulture)}.");
                        }
                        else
                        {
                            args.Context.AddString(getError);
                        }

                        return;
                    }

                    if (args.Length > 3)
                    {
                        args.Context.AddString("Usage: creative_blueprint_offset <blueprintName> [loadYOffset]");
                        return;
                    }

                    if (!float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float offset))
                    {
                        args.Context.AddString("loadYOffset must be a number.");
                        return;
                    }

                    if (CreativeBlueprintService.TrySetBlueprintLoadYOffset(blueprintName, offset, out string safeName, out string error))
                    {
                        args.Context.AddString($"{safeName} loadYOffset set to {offset.ToString("G9", CultureInfo.InvariantCulture)}.");
                    }
                    else
                    {
                        args.Context.AddString(error);
                    }
                });

            _ = new Terminal.ConsoleCommand(
                ZoneSizesCommand,
                "List allocated creative zone sizes. Usage: creative_zone_sizes",
                args =>
                {
                    if (!RequireServer(args))
                    {
                        return;
                    }

                    foreach (string line in CreativeSessionManager.ListCreativeZoneSizes())
                    {
                        args.Context.AddString(line);
                    }
                });

            _ = new Terminal.ConsoleCommand(
                ZoneSizeCommand,
                "Show or set an allocated creative zone radius. Usage: creative_zone_size <ownerPlayerIdOrPlatformId> [radius]",
                args =>
                {
                    if (!RequireServer(args))
                    {
                        return;
                    }

                    if (args.Length < 2)
                    {
                        args.Context.AddString("Usage: creative_zone_size <ownerPlayerIdOrPlatformId> [radius]");
                        return;
                    }

                    if (!args.TryParameterLong(1, out long playerId) || playerId == 0L)
                    {
                        args.Context.AddString("ownerPlayerIdOrPlatformId must be a non-zero number.");
                        return;
                    }

                    if (!TryOptionalRadius(args, 2, out float? radius))
                    {
                        args.Context.AddString("radius must be a positive number.");
                        return;
                    }

                    foreach (string line in CreativeSessionManager.GetOrSetCreativeZoneRadius(playerId, radius))
                    {
                        args.Context.AddString(line);
                    }
                });

            _ = new Terminal.ConsoleCommand(
                ZoneTerrainCommand,
                "Show the terrain modifier radius for an allocated creative zone. Usage: creative_zone_terrain <ownerPlayerIdOrPlatformId>",
                args =>
                {
                    if (!RequireServer(args))
                    {
                        return;
                    }

                    if (args.Length < 2)
                    {
                        args.Context.AddString("Usage: creative_zone_terrain <ownerPlayerIdOrPlatformId>");
                        return;
                    }

                    if (!args.TryParameterLong(1, out long playerId) || playerId == 0L)
                    {
                        args.Context.AddString("ownerPlayerIdOrPlatformId must be a non-zero number.");
                        return;
                    }

                    foreach (string line in CreativeSessionManager.GetCreativeTerrainModifierStatus(playerId))
                    {
                        args.Context.AddString(line);
                    }
                });

            _ = new Terminal.ConsoleCommand(
                ZoneMigrateSpacingCommand,
                "Plan or apply a creative zone spacing migration. Usage: creative_zone_migrate_spacing <targetSpacing> [apply]",
                args =>
                {
                    if (!RequireServer(args))
                    {
                        return;
                    }

                    if (args.Length < 2 || args.Length > 3)
                    {
                        args.Context.AddString("Usage: creative_zone_migrate_spacing <targetSpacing> [apply]");
                        return;
                    }

                    if (!float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float targetSpacing) || targetSpacing <= 0f)
                    {
                        args.Context.AddString("targetSpacing must be a positive number.");
                        return;
                    }

                    bool apply = false;
                    if (args.Length == 3)
                    {
                        if (!args[2].Equals("apply", StringComparison.OrdinalIgnoreCase))
                        {
                            args.Context.AddString("Usage: creative_zone_migrate_spacing <targetSpacing> [apply]");
                            return;
                        }

                        apply = true;
                    }

                    foreach (string line in CreativeSessionManager.MigrateCreativeZoneSpacing(targetSpacing, apply))
                    {
                        args.Context.AddString(line);
                    }
                });

            _ = new Terminal.ConsoleCommand(
                SiegeListCommand,
                "List configured creative siege zones. Usage: creative_siege_list",
                args =>
                {
                    foreach (string line in CreativeSiegeService.ListSieges())
                    {
                        args.Context.AddString(line);
                    }
                });

            _ = new Terminal.ConsoleCommand(
                SiegeStatusCommand,
                "Show creative siege zone state. Usage: creative_siege_status <siegeId>",
                args =>
                {
                    if (args.Length < 2)
                    {
                        args.Context.AddString("Usage: creative_siege_status <siegeId>");
                        return;
                    }

                    foreach (string line in CreativeSiegeService.GetStatus(args[1].Trim()))
                    {
                        args.Context.AddString(line);
                    }
                });

            _ = new Terminal.ConsoleCommand(
                SiegeSizeCommand,
                "Show or set a creative siege zone radius. Usage: creative_siege_size <siegeId> [radius]",
                args =>
                {
                    if (!RequireServer(args))
                    {
                        return;
                    }

                    if (args.Length < 2)
                    {
                        args.Context.AddString("Usage: creative_siege_size <siegeId> [radius]");
                        return;
                    }

                    if (!TryOptionalRadius(args, 2, out float? radius))
                    {
                        args.Context.AddString("radius must be a positive number.");
                        return;
                    }

                    foreach (string line in CreativeSiegeService.GetOrSetSiegeRadius(args[1].Trim(), radius))
                    {
                        args.Context.AddString(line);
                    }
                });

            _ = new Terminal.ConsoleCommand(
                SiegeLoadCommand,
                "Load a configured creative siege blueprint. Usage: creative_siege_load <siegeId>",
                args =>
                {
                    if (args.Length < 2)
                    {
                        args.Context.AddString("Usage: creative_siege_load <siegeId>");
                        return;
                    }

                    foreach (string line in CreativeSiegeService.LoadSiege(args[1].Trim()))
                    {
                        args.Context.AddString(line);
                    }
                });

            _ = new Terminal.ConsoleCommand(
                SiegeResetCommand,
                "Reset a configured creative siege zone. Usage: creative_siege_reset <siegeId>",
                args =>
                {
                    if (args.Length < 2)
                    {
                        args.Context.AddString("Usage: creative_siege_reset <siegeId>");
                        return;
                    }

                    foreach (string line in CreativeSiegeService.ResetSiege(args[1].Trim()))
                    {
                        args.Context.AddString(line);
                    }
                });

            _ = new Terminal.ConsoleCommand(
                SiegeEnterCommand,
                "Teleport an online player into a creative siege zone. Usage: creative_siege_enter <playerIdOrPlatformId> <siegeId>",
                args =>
                {
                    if (args.Length < 3)
                    {
                        args.Context.AddString("Usage: creative_siege_enter <playerIdOrPlatformId> <siegeId>");
                        return;
                    }

                    if (!args.TryParameterLong(1, out long playerId) || playerId == 0L)
                    {
                        args.Context.AddString("playerIdOrPlatformId must be a non-zero number.");
                        return;
                    }

                    foreach (string line in CreativeSiegeService.EnterSiegeForPlayerId(playerId, args[2].Trim()))
                    {
                        args.Context.AddString(line);
                    }
                });
            _registered = true;
        }

        private static bool RequireServer(Terminal.ConsoleEventArgs args)
        {
            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                return true;
            }

            args.Context.AddString("This command must be run on the server.");
            return false;
        }

        private static bool TryOptionalRadius(Terminal.ConsoleEventArgs args, int index, out float? radius)
        {
            radius = null;
            if (args.Length <= index)
            {
                return true;
            }

            if (!float.TryParse(args[index], NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) || parsed <= 0f)
            {
                return false;
            }

            radius = parsed;
            return true;
        }
    }
}

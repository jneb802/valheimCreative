using HarmonyLib;

namespace ValheimCreative.Features.Creative
{
    [HarmonyPatch(typeof(Terminal), nameof(Terminal.InitTerminal))]
    internal static class CreativeConsoleCommands
    {
        private const string LoadPlayerCommand = "creative_load_player";
        private const string SiegeListCommand = "creative_siege_list";
        private const string SiegeStatusCommand = "creative_siege_status";
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
            if (_registered && Terminal.commands.ContainsKey(LoadPlayerCommand) && Terminal.commands.ContainsKey(SiegeEnterCommand))
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
    }
}

using HarmonyLib;

namespace ValheimCreative.Features.Creative
{
    [HarmonyPatch(typeof(Terminal), nameof(Terminal.InitTerminal))]
    internal static class CreativeConsoleCommands
    {
        private const string LoadPlayerCommand = "creative_load_player";

        private static void Postfix()
        {
            _ = new Terminal.ConsoleCommand(
                LoadPlayerCommand,
                "Load a blueprint into an online player's active creative zone. Usage: creative_load_player <playerId> <blueprintName>",
                args =>
                {
                    if (args.Length < 3)
                    {
                        args.Context.AddString("Usage: creative_load_player <playerId> <blueprintName>");
                        return;
                    }

                    if (!args.TryParameterLong(1, out long playerId) || playerId == 0L)
                    {
                        args.Context.AddString("playerId must be a non-zero number.");
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
        }
    }
}

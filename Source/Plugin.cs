using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using ValheimCreative.Configuration;
using ValheimCreative.Features.Creative;
using ValheimCreative.Infrastructure.Routing;

namespace ValheimCreative
{
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    public class ValheimCreativePlugin : BaseUnityPlugin
    {
        internal const string ModGuid = "warpalicious.valheimCreative";
        internal const string ModName = "valheimCreative";
        internal const string ModVersion = "0.1.0";

        private readonly Harmony _harmony = new(ModGuid);

        internal static readonly ManualLogSource ModLogger = BepInEx.Logging.Logger.CreateLogSource(ModName);

        public void Awake()
        {
            ModConfig.Bind(Config);
            RoutedRpcDispatcher.Clear();
            CreativeChatCommands.RegisterRoutedRpcHandlers();
            CreativeSessionManager.Load();

            _harmony.PatchAll(Assembly.GetExecutingAssembly());
            ModLogger.LogInfo($"{ModName} {ModVersion} loaded.");
        }

        private void Update()
        {
            CreativeSessionManager.Update();
        }

        private void OnDestroy()
        {
            CreativeSessionManager.Save();
            _harmony.UnpatchSelf();
            Config.Save();
        }
    }
}


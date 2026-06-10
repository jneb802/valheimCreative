using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using ValheimCreative.Configuration;
using ValheimCreative.Features.Creative;

namespace ValheimCreative
{
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    public class ValheimCreativePlugin : BaseUnityPlugin
    {
        internal const string ModGuid = "warpalicious.valheimCreative";
        internal const string ModName = "valheimCreative";
        internal const string ModVersion = "0.2.9";

        private readonly Harmony _harmony = new(ModGuid);

        internal static readonly ManualLogSource ModLogger = BepInEx.Logging.Logger.CreateLogSource(ModName);

        public void Awake()
        {
            ModConfig.Bind(Config);
            CreativeCommandZoneGuard.Initialize();
            CreativeInventoryGate.RegisterRoutedRpcHandler();
            CreativeSiegePortalRpc.RegisterRoutedRpcHandler();
            CreativeConsoleCommands.Register();
            CreativeSessionManager.Load();
            CreativeSiegeService.Load();

            _harmony.PatchAll(Assembly.GetExecutingAssembly());
            ModLogger.LogInfo($"{ModName} {ModVersion} loaded.");
        }

        private void Update()
        {
            CreativeInventoryGate.Update();
            CreativeSiegePortalRpc.RegisterRoutedRpcHandler();
            CreativeSessionManager.Update();
            CreativeCommandZoneGuard.Update();
        }

        private void OnDestroy()
        {
            CreativeSessionManager.Save();
            CreativeSiegeService.Save();
            _harmony.UnpatchSelf();
            Config.Save();
        }
    }
}

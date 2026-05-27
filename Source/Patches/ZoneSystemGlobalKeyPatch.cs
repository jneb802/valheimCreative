using HarmonyLib;
using ValheimCreative.Features.Creative;

namespace ValheimCreative.Patches
{
    [HarmonyPatch(typeof(ZoneSystem), "RPC_SetGlobalKey")]
    internal static class ZoneSystemSetGlobalKeyPatch
    {
        private static void Postfix()
        {
            CreativeSessionManager.ResendCreativeKeysToActivePeers();
        }
    }

    [HarmonyPatch(typeof(ZoneSystem), "RPC_RemoveGlobalKey")]
    internal static class ZoneSystemRemoveGlobalKeyPatch
    {
        private static void Postfix()
        {
            CreativeSessionManager.ResendCreativeKeysToActivePeers();
        }
    }

    [HarmonyPatch(typeof(ZoneSystem), "SetStartingGlobalKeys")]
    internal static class ZoneSystemSetStartingGlobalKeysPatch
    {
        private static void Postfix()
        {
            CreativeSessionManager.ResendCreativeKeysToActivePeers();
        }
    }
}


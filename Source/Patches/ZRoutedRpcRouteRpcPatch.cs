using HarmonyLib;
using ValheimCreative.Infrastructure.Routing;

namespace ValheimCreative.Patches
{
    [HarmonyPatch(typeof(ZRoutedRpc), "RouteRPC")]
    internal static class ZRoutedRpcRouteRpcPatch
    {
        [HarmonyPriority(Priority.First)]
        [HarmonyBefore("redseiko.valheim.enroute")]
        private static bool Prefix(ZRoutedRpc.RoutedRPCData rpcData)
        {
            return RoutedRpcDispatcher.Process(rpcData);
        }
    }
}


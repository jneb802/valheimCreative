using System;
using HarmonyLib;
using ValheimCreative.Features.Creative;

namespace ValheimCreative.Patches
{
    [HarmonyPatch(typeof(ZRoutedRpc), "RPC_RoutedRPC")]
    internal static class ZRoutedRpcRoutedRpcPatch
    {
        private static bool Prefix(ZPackage pkg)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer())
            {
                return true;
            }

            try
            {
                ZRoutedRpc.RoutedRPCData rpcData = new();
                rpcData.Deserialize(pkg);

                return !CreativeChatCommands.TryConsumeRoutedSay(rpcData);
            }
            catch (Exception ex)
            {
                ValheimCreativePlugin.ModLogger.LogWarning($"Failed to inspect routed RPC package: {ex}");
                return true;
            }
            finally
            {
                pkg.SetPos(0);
            }
        }
    }
}

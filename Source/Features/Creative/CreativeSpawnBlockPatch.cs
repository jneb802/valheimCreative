using HarmonyLib;
using UnityEngine;

namespace ValheimCreative.Features.Creative
{
    [HarmonyPatch(typeof(SpawnSystem), "IsSpawnPointGood")]
    internal static class CreativeSpawnBlockPatch
    {
        private static bool Prefix(ref Vector3 spawnPoint, ref bool __result)
        {
            if (ZNet.instance == null ||
                !ZNet.instance.IsServer() ||
                !CreativeSessionManager.IsInsideCreativeZone(spawnPoint))
            {
                return true;
            }

            __result = false;
            return false;
        }
    }
}

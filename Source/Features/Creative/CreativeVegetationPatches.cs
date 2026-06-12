using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ValheimCreative.Features.Creative
{
    internal static class CreativeVegetationPatches
    {
        private static readonly DropTable EmptyDropTable = new()
        {
            m_drops = new List<DropTable.DropData>(),
            m_dropMin = 0,
            m_dropMax = 0,
            m_dropChance = 0f
        };

        [HarmonyPatch]
        private static class ZoneSystemPlaceVegetationPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(typeof(ZoneSystem), "PlaceVegetation");
            }

            private static void Prefix(
                ZoneSystem __instance,
                Vector3 zoneCenterPos,
                ref PlacementPatchState? __state)
            {
                __state = null;
                if (!CreativeVegetationService.TryCreatePlacementContext(zoneCenterPos, out CreativeVegetationPlacementContext? context) ||
                    context == null)
                {
                    return;
                }

                __state = new PlacementPatchState(__instance.m_vegetation, context);
                __instance.m_vegetation = context.Preset != null
                    ? new List<ZoneSystem.ZoneVegetation>(context.Preset.Entries)
                    : new List<ZoneSystem.ZoneVegetation>();
            }

            private static void Postfix(ZoneSystem __instance, PlacementPatchState? __state)
            {
                if (__state == null)
                {
                    return;
                }

                __instance.m_vegetation = __state.OriginalVegetation;
                CreativeVegetationService.FinishPlacement(__state.Context);
            }
        }

        [HarmonyPatch]
        private static class TreeBaseRpcDamagePatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(typeof(TreeBase), "RPC_Damage");
            }

            private static void Prefix(TreeBase __instance, ref DropPatchState? __state)
            {
                __state = ReplaceDropsIfSuppressed(__instance, __instance.m_dropWhenDestroyed);
                if (__state != null)
                {
                    __instance.m_dropWhenDestroyed = EmptyDropTable;
                }
            }

            private static void Postfix(TreeBase __instance, DropPatchState? __state)
            {
                if (__state != null)
                {
                    __instance.m_dropWhenDestroyed = __state.DropTable;
                }
            }
        }

        [HarmonyPatch]
        private static class TreeBaseSpawnLogPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(typeof(TreeBase), "SpawnLog");
            }

            private static bool Prefix(TreeBase __instance)
            {
                return !CreativeVegetationService.ShouldSuppressDrops(__instance);
            }
        }

        [HarmonyPatch]
        private static class TreeLogDestroyPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(typeof(TreeLog), "Destroy");
            }

            private static void Prefix(TreeLog __instance, ref TreeLogPatchState? __state)
            {
                if (!CreativeVegetationService.ShouldSuppressDrops(__instance))
                {
                    __state = null;
                    return;
                }

                __state = new TreeLogPatchState(__instance.m_dropWhenDestroyed, __instance.m_subLogPrefab);
                __instance.m_dropWhenDestroyed = EmptyDropTable;
                __instance.m_subLogPrefab = null;
            }

            private static void Postfix(TreeLog __instance, TreeLogPatchState? __state)
            {
                if (__state == null)
                {
                    return;
                }

                __instance.m_dropWhenDestroyed = __state.DropTable;
                __instance.m_subLogPrefab = __state.SubLogPrefab;
            }
        }

        [HarmonyPatch]
        private static class PickableRpcPickPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(typeof(Pickable), "RPC_Pick");
            }

            private static bool Prefix(Pickable __instance)
            {
                if (!CreativeVegetationService.ShouldSuppressDrops(__instance))
                {
                    return true;
                }

                __instance.SetPicked(true);
                return false;
            }
        }

        [HarmonyPatch]
        private static class DropOnDestroyedPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(typeof(DropOnDestroyed), "OnDestroyed");
            }

            private static bool Prefix(DropOnDestroyed __instance)
            {
                return !CreativeVegetationService.ShouldSuppressDrops(__instance);
            }
        }

        [HarmonyPatch]
        private static class MineRockRpcHitPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(typeof(MineRock), "RPC_Hit");
            }

            private static void Prefix(MineRock __instance, HitData hit, ref DropPatchState? __state)
            {
                __state = ReplaceDropsIfSuppressed(__instance, __instance.m_dropItems, hit.m_point);
                if (__state != null)
                {
                    __instance.m_dropItems = EmptyDropTable;
                }
            }

            private static void Postfix(MineRock __instance, DropPatchState? __state)
            {
                if (__state != null)
                {
                    __instance.m_dropItems = __state.DropTable;
                }
            }
        }

        [HarmonyPatch]
        private static class MineRock5DamageAreaPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(typeof(MineRock5), "DamageArea");
            }

            private static void Prefix(MineRock5 __instance, HitData hit, ref DropPatchState? __state)
            {
                __state = ReplaceDropsIfSuppressed(__instance, __instance.m_dropItems, hit.m_point);
                if (__state != null)
                {
                    __instance.m_dropItems = EmptyDropTable;
                }
            }

            private static void Postfix(MineRock5 __instance, DropPatchState? __state)
            {
                if (__state != null)
                {
                    __instance.m_dropItems = __state.DropTable;
                }
            }
        }

        private static DropPatchState? ReplaceDropsIfSuppressed(Component component, DropTable dropTable)
        {
            return CreativeVegetationService.ShouldSuppressDrops(component)
                ? new DropPatchState(dropTable)
                : null;
        }

        private static DropPatchState? ReplaceDropsIfSuppressed(Component component, DropTable dropTable, Vector3 point)
        {
            return CreativeVegetationService.ShouldSuppressDrops(component, point)
                ? new DropPatchState(dropTable)
                : null;
        }

        private sealed class PlacementPatchState
        {
            internal PlacementPatchState(
                List<ZoneSystem.ZoneVegetation> originalVegetation,
                CreativeVegetationPlacementContext context)
            {
                OriginalVegetation = originalVegetation;
                Context = context;
            }

            internal List<ZoneSystem.ZoneVegetation> OriginalVegetation { get; }
            internal CreativeVegetationPlacementContext Context { get; }
        }

        private sealed class DropPatchState
        {
            internal DropPatchState(DropTable dropTable)
            {
                DropTable = dropTable;
            }

            internal DropTable DropTable { get; }
        }

        private sealed class TreeLogPatchState
        {
            internal TreeLogPatchState(DropTable dropTable, GameObject subLogPrefab)
            {
                DropTable = dropTable;
                SubLogPrefab = subLogPrefab;
            }

            internal DropTable DropTable { get; }
            internal GameObject SubLogPrefab { get; }
        }
    }
}

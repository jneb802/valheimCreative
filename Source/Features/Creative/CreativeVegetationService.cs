using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using ValheimCreative.Configuration;

namespace ValheimCreative.Features.Creative
{
    internal static class CreativeVegetationService
    {
        internal const string ZdoVegetationMarker = "valheimCreative.vegetation";
        internal const string ZdoVegetationSlotId = "valheimCreative.vegetationSlot";
        private const float PlacementScanPadding = 64f;
        private static int LastPlacementMarkedCount { get; set; }

        internal static bool TryCreatePlacementContext(
            Vector3 zoneCenter,
            out CreativeVegetationPlacementContext? context)
        {
            context = null;
            bool foundZone = CreativeSessionManager.TryGetCreativeZoneAtPosition(zoneCenter, out CreativeZone? zone);
            if (!foundZone)
            {
                foundZone = CreativeSessionManager.TryGetCreativeZoneForTerrainSector(zoneCenter, out zone);
            }

            if (!foundZone || zone == null)
            {
                return false;
            }

            CreativeEnvironmentSettings settings = CreativeEnvironmentPolicy.Resolve(zone);
            if (!settings.TerrainEnabled || zone.TerrainMode != CreativeTerrainMode.WorldSeedPatch)
            {
                context = CreativeVegetationPlacementContext.Empty(zone, settings);
                return true;
            }

            if (!settings.VegetationEnabled)
            {
                context = CreativeVegetationPlacementContext.Empty(zone, settings);
                return true;
            }

            if (!CreativeEnvironmentPolicy.TryGetPreset(
                    settings.VegetationPreset,
                    zone.TerrainSource?.Biome ?? zone.Biome,
                    out CreativeVegetationPreset preset,
                    out string error))
            {
                ValheimCreativePlugin.ModLogger.LogWarning(
                    $"Failed to resolve creative vegetation preset {settings.VegetationPreset} for {zone.SlotId}: {error}");
                context = CreativeVegetationPlacementContext.Empty(zone, settings);
                return true;
            }

            HashSet<ZDOID> existing = FindVegetationZdos(zone, preset.PrefabNames)
                .Select(zdo => zdo.m_uid)
                .ToHashSet();
            context = new CreativeVegetationPlacementContext(zone, settings, preset, existing);
            return true;
        }

        internal static void FinishPlacement(CreativeVegetationPlacementContext? context)
        {
            if (context == null || context.Preset == null)
            {
                return;
            }

            int marked = 0;
            int removedFromEdge = 0;
            Dictionary<string, int> markedByPrefab = new(StringComparer.Ordinal);
            foreach (ZDO zdo in FindVegetationZdos(context.Zone, context.Preset.PrefabNames, GetVegetationCleanupRadius(context.Zone)))
            {
                if (Utils.DistanceXZ(zdo.GetPosition(), context.Zone.Position) > context.Zone.Radius)
                {
                    if (!zdo.IsOwner())
                    {
                        zdo.SetOwner(ZDOMan.GetSessionID());
                    }

                    ZDOMan.instance.DestroyZDO(zdo);
                    removedFromEdge++;
                    continue;
                }

                if (context.ExistingZdos.Contains(zdo.m_uid))
                {
                    continue;
                }

                if (!zdo.IsOwner())
                {
                    zdo.SetOwner(ZDOMan.GetSessionID());
                }

                zdo.Set(ZdoVegetationMarker, true);
                zdo.Set(ZdoVegetationSlotId, context.Zone.SlotId);
                marked++;
                string prefabName = GetPrefabName(zdo);
                if (!string.IsNullOrWhiteSpace(prefabName))
                {
                    markedByPrefab[prefabName] = markedByPrefab.TryGetValue(prefabName, out int count) ? count + 1 : 1;
                }
            }

            LastPlacementMarkedCount = marked;
            if (marked > 0 || removedFromEdge > 0)
            {
                string prefabSummary = markedByPrefab.Count > 0
                    ? $"; prefabs={string.Join(",", markedByPrefab.OrderBy(entry => entry.Key, StringComparer.Ordinal).Select(entry => $"{entry.Key}:{entry.Value}"))}"
                    : string.Empty;
                ValheimCreativePlugin.ModLogger.LogInfo(
                    $"Marked {marked} creative vegetation object(s) in {context.Zone.SlotId} using preset {context.Settings.VegetationPreset}; removedEdge={removedFromEdge}{prefabSummary}.");
            }
        }

        internal static void DestroyCreativeVegetation(CreativeZone zone)
        {
            DestroyCreativeVegetation(zone, GetVegetationCleanupRadius(zone));
        }

        internal static void DestroyCreativeVegetation(CreativeZone zone, float searchRadius)
        {
            if (ZDOMan.instance == null)
            {
                return;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            int scanned = 0;
            int removed = 0;
            float cleanupRadius = GetVegetationCleanupRadius(searchRadius);
            foreach (ZDO zdo in FindZoneZdos(zone, cleanupRadius))
            {
                scanned++;
                if (zdo == null || !zdo.IsValid())
                {
                    continue;
                }

                if (!IsMarkedCreativeVegetation(zdo) && !IsKnownVegetationPrefab(zdo))
                {
                    continue;
                }

                if (!zdo.IsOwner())
                {
                    zdo.SetOwner(ZDOMan.GetSessionID());
                }

                ZDOMan.instance.DestroyZDO(zdo);
                removed++;
            }

            stopwatch.Stop();
            if (removed > 0 || ModConfig.DebugLogging.Value)
            {
                ValheimCreativePlugin.ModLogger.LogInfo(
                    $"Creative vegetation cleanup for {zone.SlotId}: scanned={scanned}, removed={removed}, radius={cleanupRadius:0.##}m, sectorArea={GetZoneSearchSectorArea(cleanupRadius)}, elapsedMs={stopwatch.ElapsedMilliseconds}.");
            }
        }

        internal static CreativeVegetationRegenerationResult RegenerateCreativeVegetation(CreativeZone zone)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            DestroyCreativeVegetation(zone);

            CreativeEnvironmentSettings settings = CreativeEnvironmentPolicy.Resolve(zone);
            if (!settings.TerrainEnabled || zone.TerrainMode != CreativeTerrainMode.WorldSeedPatch)
            {
                stopwatch.Stop();
                return LogRegenerationResult(zone, settings, stopwatch, 0, 0, 0, "terrain disabled");
            }

            if (!settings.VegetationEnabled)
            {
                stopwatch.Stop();
                return LogRegenerationResult(zone, settings, stopwatch, 0, 0, 0, "vegetation disabled");
            }

            if (ZoneSystem.instance == null)
            {
                stopwatch.Stop();
                return LogRegenerationResult(zone, settings, stopwatch, 0, 0, 0, "ZoneSystem not ready");
            }

            ZoneSystem zoneSystem = ZoneSystem.instance;
            int scannedSectors = 0;
            int processedSectors = 0;
            int spawnedObjects = 0;
            foreach (Vector2i sector in GetCreativeSectors(zone))
            {
                scannedSectors++;
                GameObject? temporaryRoot = null;
                GameObject? root = GetLoadedZoneRoot(zoneSystem, sector);
                if (root == null && !TryCreateTemporaryZoneRoot(zoneSystem, sector, out temporaryRoot))
                {
                    continue;
                }

                root ??= temporaryRoot;
                if (root == null)
                {
                    continue;
                }

                Heightmap heightmap = root.GetComponentInChildren<Heightmap>();
                processedSectors++;
                Vector3 zonePosition = ZoneSystem.GetZonePos(sector);
                List<ZoneSystem.ClearArea> clearAreas = BuildClearAreas(zoneSystem, sector);
                zoneSystem.m_tempSpawnedObjects.Clear();
                LastPlacementMarkedCount = 0;
                zoneSystem.PlaceVegetation(
                    sector,
                    zonePosition,
                    root.transform,
                    heightmap,
                    clearAreas,
                    ZoneSystem.SpawnMode.Full,
                    zoneSystem.m_tempSpawnedObjects);
                spawnedObjects += LastPlacementMarkedCount;
                zoneSystem.m_tempSpawnedObjects.Clear();
                if (temporaryRoot != null)
                {
                    UnityEngine.Object.Destroy(temporaryRoot);
                }
            }

            stopwatch.Stop();
            return LogRegenerationResult(zone, settings, stopwatch, scannedSectors, processedSectors, spawnedObjects, string.Empty);
        }

        internal static bool TryRestoreMissingCreativeVegetation(CreativeZone zone)
        {
            CreativeEnvironmentSettings settings = CreativeEnvironmentPolicy.Resolve(zone);
            if (!settings.TerrainEnabled ||
                !settings.VegetationEnabled ||
                zone.TerrainMode != CreativeTerrainMode.WorldSeedPatch ||
                ZDOMan.instance == null)
            {
                return false;
            }

            int existingMarked = FindZoneZdos(zone, zone.Radius)
                .Count(zdo => zdo != null && zdo.IsValid() && IsMarkedCreativeVegetation(zdo));
            if (existingMarked > 0)
            {
                return false;
            }

            ValheimCreativePlugin.ModLogger.LogInfo(
                $"Creative vegetation missing for {zone.SlotId}; regenerating preset {settings.VegetationPreset}.");
            RegenerateCreativeVegetation(zone);
            return true;
        }

        internal static bool ShouldSuppressDrops(Component component)
        {
            if (component == null)
            {
                return false;
            }

            return ShouldSuppressDrops(component, component.transform.position);
        }

        internal static bool ShouldSuppressDrops(Component component, Vector3 point)
        {
            if (component == null)
            {
                return false;
            }

            ZNetView netView = component.GetComponent<ZNetView>() ?? component.GetComponentInParent<ZNetView>();
            ZDO? zdo = netView != null ? netView.GetZDO() : null;
            if (zdo != null && IsMarkedCreativeVegetation(zdo))
            {
                CreativeEnvironmentSettings settings = CreativeEnvironmentPolicy.ResolveAtPosition(point);
                return !settings.VegetationDropsEnabled;
            }

            if (!CreativeSessionManager.TryGetCreativeZoneAtPosition(point, out CreativeZone? zone) || zone == null)
            {
                return false;
            }

            CreativeEnvironmentSettings zoneSettings = CreativeEnvironmentPolicy.Resolve(zone);
            if (zoneSettings.VegetationDropsEnabled)
            {
                return false;
            }

            return component.GetComponent<TreeBase>() != null ||
                   component.GetComponent<TreeLog>() != null ||
                   component.GetComponent<Pickable>() != null ||
                   component.GetComponent<MineRock>() != null ||
                   component.GetComponent<MineRock5>() != null ||
                   component.GetComponent<DropOnDestroyed>() != null;
        }

        internal static bool TryMapToTerrainSource(float x, float z, out Vector2 source, out CreativeTerrainSource? terrainSource)
        {
            source = Vector2.zero;
            terrainSource = null;
            if (!CreativeSessionManager.TryGetCreativeZoneAtPosition(new Vector3(x, 0f, z), out CreativeZone? zone) ||
                zone == null ||
                zone.TerrainMode != CreativeTerrainMode.WorldSeedPatch ||
                zone.TerrainSource == null)
            {
                return false;
            }

            CreativeEnvironmentSettings settings = CreativeEnvironmentPolicy.Resolve(zone);
            if (!settings.TerrainEnabled)
            {
                return false;
            }

            source = new Vector2(
                zone.TerrainSource.Center.x + (x - zone.Position.x),
                zone.TerrainSource.Center.z + (z - zone.Position.z));
            terrainSource = zone.TerrainSource;
            return true;
        }

        private static bool IsMarkedCreativeVegetation(ZDO zdo)
        {
            return zdo.GetBool(ZdoVegetationMarker) ||
                   !string.IsNullOrWhiteSpace(zdo.GetString(ZdoVegetationSlotId));
        }

        private static bool IsKnownVegetationPrefab(ZDO zdo)
        {
            string prefabName = GetPrefabName(zdo);
            if (string.IsNullOrWhiteSpace(prefabName) || ZoneSystem.instance == null)
            {
                return false;
            }

            foreach (ZoneSystem.ZoneVegetation vegetation in ZoneSystem.instance.m_vegetation)
            {
                if (vegetation?.m_prefab != null &&
                    vegetation.m_prefab.name.Equals(prefabName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<ZDO> FindVegetationZdos(CreativeZone zone, HashSet<string> prefabNames)
        {
            return FindVegetationZdos(zone, prefabNames, zone.Radius);
        }

        private static IEnumerable<ZDO> FindVegetationZdos(CreativeZone zone, HashSet<string> prefabNames, float searchRadius)
        {
            foreach (ZDO zdo in FindZoneZdos(zone, searchRadius))
            {
                if (zdo == null || !zdo.IsValid())
                {
                    continue;
                }

                string prefabName = GetPrefabName(zdo);
                if (prefabNames.Contains(prefabName))
                {
                    yield return zdo;
                }
            }
        }

        private static IEnumerable<ZDO> FindZoneZdos(CreativeZone zone)
        {
            return FindZoneZdos(zone, zone.Radius);
        }

        private static IEnumerable<ZDO> FindZoneZdos(CreativeZone zone, float searchRadius)
        {
            if (ZDOMan.instance == null)
            {
                yield break;
            }

            List<ZDO> objects = new();
            float radius = Mathf.Max(1f, searchRadius) + PlacementScanPadding;
            int sectorArea = GetZoneSearchSectorArea(searchRadius);
            ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(zone.Position), sectorArea, 0, objects);
            foreach (ZDO zdo in objects.Distinct())
            {
                if (zdo == null ||
                    !zdo.IsValid() ||
                    Vector2.Distance(new Vector2(zdo.GetPosition().x, zdo.GetPosition().z), new Vector2(zone.Position.x, zone.Position.z)) > radius)
                {
                    continue;
                }

                yield return zdo;
            }
        }

        private static int GetZoneSearchSectorArea(CreativeZone zone)
        {
            return GetZoneSearchSectorArea(zone.Radius);
        }

        private static int GetZoneSearchSectorArea(float radius)
        {
            float paddedRadius = Mathf.Max(1f, radius) + PlacementScanPadding;
            return Mathf.CeilToInt(paddedRadius / ZoneSystem.c_ZoneSize) + 1;
        }

        private static float GetVegetationCleanupRadius(CreativeZone zone)
        {
            return GetVegetationCleanupRadius(zone.Radius);
        }

        private static float GetVegetationCleanupRadius(float radius)
        {
            return Mathf.Max(1f, radius) + ModConfig.CreativeTerrainEdgeFalloffWidthValue;
        }

        private static IEnumerable<Vector2i> GetCreativeSectors(CreativeZone zone)
        {
            Vector2i centerSector = ZoneSystem.GetZone(zone.Position);
            int sectorArea = GetZoneSearchSectorArea(zone);
            for (int y = centerSector.y - sectorArea; y <= centerSector.y + sectorArea; y++)
            {
                for (int x = centerSector.x - sectorArea; x <= centerSector.x + sectorArea; x++)
                {
                    Vector2i sector = new(x, y);
                    Vector3 sectorPosition = ZoneSystem.GetZonePos(sector);
                    if (CreativeSessionManager.DoesSectorIntersectCreativeRadius(zone, sectorPosition))
                    {
                        yield return sector;
                    }
                }
            }
        }

        private static List<ZoneSystem.ClearArea> BuildClearAreas(ZoneSystem zoneSystem, Vector2i sector)
        {
            zoneSystem.m_tempClearAreas.Clear();
            if (zoneSystem.m_locationInstances.TryGetValue(sector, out ZoneSystem.LocationInstance location) &&
                location.m_location.m_clearArea)
            {
                zoneSystem.m_tempClearAreas.Add(new ZoneSystem.ClearArea(location.m_position, location.m_location.m_exteriorRadius));
            }

            return zoneSystem.m_tempClearAreas;
        }

        private static GameObject? GetLoadedZoneRoot(ZoneSystem zoneSystem, Vector2i sector)
        {
            return zoneSystem.IsZoneLoaded(sector) &&
                   zoneSystem.m_zones.TryGetValue(sector, out ZoneSystem.ZoneData zoneData) &&
                   zoneData?.m_root != null
                ? zoneData.m_root
                : null;
        }

        private static bool TryCreateTemporaryZoneRoot(ZoneSystem zoneSystem, Vector2i sector, out GameObject? root)
        {
            root = null;
            if (zoneSystem.m_zonePrefab == null ||
                HeightmapBuilder.instance == null ||
                WorldGenerator.instance == null)
            {
                return false;
            }

            Vector3 zonePosition = ZoneSystem.GetZonePos(sector);
            Heightmap prefabHeightmap = zoneSystem.m_zonePrefab.GetComponentInChildren<Heightmap>();
            if (prefabHeightmap == null)
            {
                return false;
            }

            HeightmapBuilder.instance.RequestTerrainSync(
                zonePosition,
                prefabHeightmap.m_width,
                prefabHeightmap.m_scale,
                prefabHeightmap.IsDistantLod,
                WorldGenerator.instance);
            root = UnityEngine.Object.Instantiate(zoneSystem.m_zonePrefab, zonePosition, Quaternion.identity);
            return root.GetComponentInChildren<Heightmap>() != null;
        }

        private static CreativeVegetationRegenerationResult LogRegenerationResult(
            CreativeZone zone,
            CreativeEnvironmentSettings settings,
            Stopwatch stopwatch,
            int scannedSectors,
            int processedSectors,
            int spawnedObjects,
            string skippedReason)
        {
            CreativeVegetationRegenerationResult result = new(scannedSectors, processedSectors, spawnedObjects, stopwatch.ElapsedMilliseconds, skippedReason);
            string suffix = string.IsNullOrWhiteSpace(skippedReason) ? string.Empty : $", skipped={skippedReason}";
            ValheimCreativePlugin.ModLogger.LogInfo(
                $"Creative vegetation regeneration for {zone.SlotId}: preset={settings.VegetationPreset}, scannedSectors={scannedSectors}, processedSectors={processedSectors}, spawnedObjects={spawnedObjects}, elapsedMs={stopwatch.ElapsedMilliseconds}{suffix}.");
            return result;
        }

        private static string GetPrefabName(ZDO zdo)
        {
            if (ZNetScene.instance == null)
            {
                return string.Empty;
            }

            GameObject prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
            return prefab != null ? prefab.name : string.Empty;
        }
    }

    internal sealed class CreativeVegetationPlacementContext
    {
        internal CreativeVegetationPlacementContext(
            CreativeZone zone,
            CreativeEnvironmentSettings settings,
            CreativeVegetationPreset? preset,
            HashSet<ZDOID> existingZdos)
        {
            Zone = zone;
            Settings = settings;
            Preset = preset;
            ExistingZdos = existingZdos;
        }

        internal CreativeZone Zone { get; }
        internal CreativeEnvironmentSettings Settings { get; }
        internal CreativeVegetationPreset? Preset { get; }
        internal HashSet<ZDOID> ExistingZdos { get; }

        internal static CreativeVegetationPlacementContext Empty(CreativeZone zone, CreativeEnvironmentSettings settings)
        {
            return new CreativeVegetationPlacementContext(zone, settings, null, new HashSet<ZDOID>());
        }
    }

    internal sealed class CreativeVegetationRegenerationResult
    {
        internal CreativeVegetationRegenerationResult(
            int scannedSectors,
            int processedSectors,
            int spawnedObjects,
            long elapsedMilliseconds,
            string skippedReason)
        {
            ScannedSectors = scannedSectors;
            ProcessedSectors = processedSectors;
            SpawnedObjects = spawnedObjects;
            ElapsedMilliseconds = elapsedMilliseconds;
            SkippedReason = skippedReason;
        }

        internal int ScannedSectors { get; }

        internal int ProcessedSectors { get; }

        internal int SpawnedObjects { get; }

        internal long ElapsedMilliseconds { get; }

        internal string SkippedReason { get; }
    }
}

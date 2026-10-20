using System;
using System.Collections.Generic;
using System.Linq;
using RunicAutomation.Operations;
using UnityEngine;

namespace RunicCrafting.Integration
{
    // A dedicated server need not have the station or its upgrades instantiated. Resolve
    // native extension rules from authoritative world records instead of trusting a client
    // range/level or calling GetStationBuildRange on the uninstantiated prefab.
    internal static class AuthorityStationRange
    {
        private static ZNetScene catalogScene;
        private static int catalogCount = -1;
        private static readonly Dictionary<int, StationExtension> extensions = new();

        internal static float Resolve(ZDO station, CraftingStation descriptor)
        {
            if (station == null || descriptor == null || ZNetScene.instance == null ||
                !Finite(descriptor.m_rangeBuild) || !Finite(descriptor.m_extraRangePerLevel))
                throw new OperationRejectedException("invalid-crafting-station-range");
            if (descriptor.m_extraRangePerLevel == 0f) return descriptor.m_rangeBuild;
            RefreshCatalog();
            var matching = new Dictionary<int, StationExtension>();
            float radius = 0;
            foreach (var entry in extensions)
            {
                var extension = entry.Value;
                if (extension == null || extension.m_craftingStation == null ||
                    extension.m_craftingStation.m_name != descriptor.m_name) continue;
                float distance = extension.m_maxStationDistance;
                if (!Finite(distance) || distance > 2048f)
                    throw new OperationRejectedException("invalid-crafting-extension-range");
                if (distance <= 0f) continue;
                matching[entry.Key] = extension;
                radius = Math.Max(radius, distance);
            }
            if (matching.Count == 0) return descriptor.m_rangeBuild;

            int count = 0;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var candidate in NetworkInventoryAccess.Nearby(station.GetPosition(), radius)
                .OrderBy(x => x.m_uid.ToString(), StringComparer.Ordinal))
            {
                if (!matching.TryGetValue(candidate.GetPrefab(), out var extension) ||
                    (candidate.GetPosition() - station.GetPosition()).sqrMagnitude >= extension.m_maxStationDistance * extension.m_maxStationDistance)
                    continue;
                var piece = extension.GetComponent<Piece>();
                if (piece == null) continue;
                // Native StationExtension.ExtensionInList compares Piece.m_name, not the
                // prefab ID; stacked upgrades still appear in that list for later entries.
                if (extension.m_stack || !names.Contains(piece.m_name))
                { count++; names.Add(piece.m_name); }
            }
            float range = descriptor.m_rangeBuild + count * descriptor.m_extraRangePerLevel;
            if (!Finite(range)) throw new OperationRejectedException("invalid-crafting-station-range");
            return range;
        }

        private static void RefreshCatalog()
        {
            var scene = ZNetScene.instance;
            if (ReferenceEquals(catalogScene, scene) && catalogCount == scene.m_prefabs.Count) return;
            extensions.Clear(); catalogScene = scene; catalogCount = scene.m_prefabs.Count;
            foreach (var prefab in scene.m_prefabs)
            {
                var extension = prefab == null ? null : prefab.GetComponent<StationExtension>();
                if (extension != null && prefab.GetComponent<Piece>() != null)
                    extensions[prefab.name.GetStableHashCode()] = extension;
            }
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

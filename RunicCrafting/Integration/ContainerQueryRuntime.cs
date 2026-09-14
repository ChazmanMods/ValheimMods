using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RunicCrafting.Domain;
using UnityEngine;

namespace RunicCrafting.Integration
{
    internal sealed class ContainerQueryRuntime
    {
        private readonly WorkshopAccessRuntime _workshopAccess;

        internal ContainerQueryRuntime(WorkshopAccessRuntime workshopAccess) =>
            _workshopAccess = workshopAccess ?? throw new ArgumentNullException(nameof(workshopAccess));

        internal IReadOnlyList<IMutableMaterialSource> ResolveSources(
            Player player,
            CraftingStation station,
            Vector3 origin,
            float radius,
            IEnumerable<MaterialRequirement> requirements,
            string purposeId,
            bool stationlessAccessAuthorized,
            out string reasonCode,
            bool requireWritable = false,
            bool allowRefreshCache = true)
        {
            if (requireWritable)
            {
                PreviewRefreshRuntime.Invalidate();
                UiPreviewCache.Invalidate();
            }
            if (!requireWritable && allowRefreshCache && !PreviewRefreshRuntime.InAction &&
                PreviewRefreshRuntime.Cache.Active && ValheimReflection.CanMutateLocalPlayer(player))
            {
                var key = new PreviewRefreshRuntime.QueryKey(player, station, origin, radius, stationlessAccessAuthorized);
                if (PreviewRefreshRuntime.Cache.TryGet(key, out PreviewRefreshRuntime.Sources cached))
                {
                    CachePerformance.RefreshHits++;
                    reasonCode = cached.Reason;
                    return cached.Items;
                }
                long epoch = PreviewRefreshRuntime.Cache.Epoch;
                IReadOnlyList<IMutableMaterialSource> sources = ResolveSourcesUncached(player, station,
                    origin, radius, requirements, purposeId, stationlessAccessAuthorized,
                    out reasonCode, requireWritable: false, allPreviewResources: true);
                PreviewRefreshRuntime.Cache.Store(key, new PreviewRefreshRuntime.Sources(sources, reasonCode), epoch);
                return sources;
            }
            return ResolveSourcesUncached(player, station, origin, radius, requirements, purposeId,
                stationlessAccessAuthorized, out reasonCode, requireWritable);
        }

        private IReadOnlyList<IMutableMaterialSource> ResolveSourcesUncached(
            Player player, CraftingStation station, Vector3 origin, float radius,
            IEnumerable<MaterialRequirement> requirements, string purposeId,
            bool stationlessAccessAuthorized, out string reasonCode,
            bool requireWritable, bool allPreviewResources = false)
        {
            long queryStarted = CachePerformance.StartQuery();
            try
            {
                return ResolveSourcesCore(player, station, origin, radius, requirements, purposeId,
                    stationlessAccessAuthorized, out reasonCode, requireWritable, allPreviewResources);
            }
            finally { CachePerformance.EndQuery(queryStarted); }
        }

        private IReadOnlyList<IMutableMaterialSource> ResolveSourcesCore(
            Player player, CraftingStation station, Vector3 origin, float radius,
            IEnumerable<MaterialRequirement> requirements, string purposeId,
            bool stationlessAccessAuthorized, out string reasonCode,
            bool requireWritable, bool allPreviewResources)
        {
            var result = new List<IMutableMaterialSource>();
            reasonCode = "ok";
            if (!ValheimReflection.CanMutateLocalPlayer(player))
            {
                reasonCode = "local-player-owner-required";
                CraftingDiagnostics.TraceGate(purposeId + ":container-query", reasonCode);
                return result.AsReadOnly();
            }

            if (!requireWritable)
            {
                UiPreviewCache.Watch(player.GetInventory());
                UiPreviewCache.Watch(ValheimReflection.StationZdo(station));
                ValheimReflection.ObservePreviewWards();
            }

            string[] resources = allPreviewResources ? Array.Empty<string>() : requirements
                .Select(requirement => requirement.ResourceId)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            if (allPreviewResources)
                result.Add(new ReadOnlyMaterialSource(new PreviewMaterialCounts(player.GetInventory(), Game.m_worldLevel)
                    .ToSnapshot(PrincipalValue(player), MaterialSourceKind.PlayerInventory, 0f)));
            else result.Add(new ValheimMaterialSource(
                PrincipalValue(player),
                player.GetInventory(),
                MaterialSourceKind.PlayerInventory,
                0f,
                resources,
                () =>
                    ValheimReflection.CanMutateLocalPlayer(player) &&
                    (station == null || _workshopAccess.Evaluate(
                        station, player, WorkshopAction.StationUse).Allowed)));

            if (station == null && !stationlessAccessAuthorized)
            {
                reasonCode = "station-required-for-nearby-materials";
                CraftingDiagnostics.TraceGate(purposeId + ":container-query", reasonCode);
                return result.AsReadOnly();
            }
            if (station != null)
            {
                WorkshopAccessDecision stationAccess = _workshopAccess.Evaluate(
                    station, player, WorkshopAction.StationUse);
                if (!stationAccess.Allowed)
                {
                    reasonCode = "station-use:" + stationAccess.ReasonCode;
                    CraftingDiagnostics.TraceGate(purposeId + ":container-query", reasonCode);
                    return result.AsReadOnly();
                }
                WorkshopAccessDecision materialAccess = _workshopAccess.Evaluate(
                    station, player, WorkshopAction.LocalMaterialUse);
                if (!materialAccess.Allowed)
                {
                    reasonCode = "local-materials:" + materialAccess.ReasonCode;
                    CraftingDiagnostics.TraceGate(purposeId + ":container-query", reasonCode);
                    return result.AsReadOnly();
                }
            }

            radius = Math.Max(1f, Math.Min(Configuration.SafeRangeCap, radius));
            IReadOnlyList<Container> candidates = ContainerSpatialIndex.Query(
                origin, radius, Configuration.SafeMaximumCandidates);
            if (!requireWritable)
                foreach (Container candidate in candidates)
                    if (candidate != null)
                    {
                        UiPreviewCache.Watch(candidate.GetInventory());
                        UiPreviewCache.Watch(ValheimReflection.GetView(candidate)?.GetZDO());
                    }
            var rejectionCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            int returned = 0;
            foreach (Container container in candidates)
            {
                if (returned >= Configuration.SafeMaximumReturned) break;
                if (!IsEligibleContainer(container, player, origin, radius, out string rejection, requireWritable))
                {
                    rejectionCounts.TryGetValue(rejection, out int count);
                    rejectionCounts[rejection] = count + 1;
                    continue;
                }

                Container captured = container;
                if (!requireWritable)
                {
                    if (!ValheimReflection.TryReadContainerInventory(captured, out PreviewMaterialCounts preview))
                    {
                        rejectionCounts.TryGetValue("preview-pending-sync", out int count);
                        rejectionCounts["preview-pending-sync"] = count + 1;
                        continue;
                    }
                    if (allPreviewResources)
                        result.Add(new ReadOnlyMaterialSource(preview.ToSnapshot(
                            ValheimReflection.ContainerEndpointId(captured), MaterialSourceKind.NearbyContainer,
                            (captured.transform.position - origin).sqrMagnitude)));
                    else
                    {
                        var quantities = new Dictionary<string, int>(StringComparer.Ordinal);
                        foreach (string resource in resources)
                            quantities[resource] = preview.Count(resource);
                        result.Add(new ReadOnlyMaterialSource(new MaterialSourceSnapshot(
                            ValheimReflection.ContainerEndpointId(captured), MaterialSourceKind.NearbyContainer,
                            (captured.transform.position - origin).sqrMagnitude, quantities)));
                    }
                    returned++;
                    continue;
                }
                result.Add(new ValheimMaterialSource(
                    ValheimReflection.ContainerEndpointId(captured),
                    captured.GetInventory(),
                    MaterialSourceKind.NearbyContainer,
                    (captured.transform.position - origin).sqrMagnitude,
                    resources,
                    () =>
                        (station == null
                            ? stationlessAccessAuthorized &&
                              ValheimReflection.CanMutateLocalPlayer(player)
                            : _workshopAccess.Evaluate(
                                  station, player, WorkshopAction.StationUse).Allowed &&
                              _workshopAccess.Evaluate(
                                  station, player, WorkshopAction.LocalMaterialUse).Allowed) &&
                        IsEligibleContainer(captured, player, origin, radius, out _, true)));
                returned++;
            }

            reasonCode = requireWritable ? "guarded-native-ownership" : "read-only-preview";
            CraftingDiagnostics.TraceGate(
                purposeId + ":container-query",
                reasonCode,
                "candidates=" + candidates.Count + "; selected=" + returned +
                FormatRejections(rejectionCounts));
            return result.AsReadOnly();
        }

        private static bool IsEligibleContainer(
            Container container,
            Player player,
            Vector3 origin,
            float radius,
            out string rejectionReason,
            bool requireWritable)
        {
            rejectionReason = "eligible";
            if (container == null || !container.isActiveAndEnabled)
            {
                rejectionReason = "inactive";
                return false;
            }
            if (!ValheimReflection.CanMutateLocalPlayer(player))
            {
                rejectionReason = "player-owner";
                return false;
            }
            if ((container.transform.position - origin).sqrMagnitude > radius * radius)
            {
                rejectionReason = "out-of-range";
                return false;
            }
            ZNetView view = ValheimReflection.GetView(container);
            ZDO zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            if (zdo == null)
            {
                rejectionReason = "missing-zdo";
                return false;
            }
            if (container.IsInUse() || zdo.GetInt(ZDOVars.s_inUse, 0) != 0 ||
                container.m_wagon != null && container.m_wagon.InUse())
            {
                rejectionReason = "in-use";
                return false;
            }
            if (Configuration.ExcludePersonalContainers.Value &&
                container.m_privacy == Container.PrivacySetting.Private)
            {
                rejectionReason = "personal-excluded";
                return false;
            }
            if (!ValheimReflection.ContainerAllows(container, player.GetPlayerID()))
            {
                rejectionReason = "container-access-denied";
                return false;
            }
            if ((container.m_checkGuardStone || Configuration.RequireWardAccess.Value) &&
                !PrivateArea.CheckAccess(
                    container.transform.position, 0f, flash: false, wardCheck: false))
            {
                rejectionReason = "ward-denied";
                return false;
            }
            if (!requireWritable) return true;
            if (!view.IsOwner()) view.ClaimOwnership();
            if (!view.IsOwner() || zdo.GetOwner() != ZNet.GetUID())
            {
                rejectionReason = "ownership-claim-failed";
                return false;
            }
            if (!ValheimReflection.RefreshOwnedContainer(container, zdo))
            {
                rejectionReason = "synchronization-failed";
                return false;
            }
            return true;
        }

        private static string FormatRejections(IEnumerable<KeyValuePair<string, int>> counts)
        {
            string[] values = counts.Select(pair => pair.Key + "=" + pair.Value).ToArray();
            return values.Length == 0 ? string.Empty : "; rejected[" + string.Join(",", values) + "]";
        }

        internal static string PrincipalValue(Player player) =>
            "valheim.player:" + player.GetPlayerID().ToString(CultureInfo.InvariantCulture);
    }
}

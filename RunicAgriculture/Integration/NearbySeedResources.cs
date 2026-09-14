using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RunicAgriculture.Core;
using UnityEngine;

namespace RunicAgriculture.Integration
{
    /// <summary>
    /// Bounded spatial index of loaded static chests. Index membership is discovery evidence,
    /// never authorization; the resource service resolves and rechecks every exact endpoint.
    /// </summary>
    internal static class NearbySeedContainerIndex
    {
        internal const float HardMaximumRangeMeters = 30f;
        internal const int HardMaximumCandidates = 64;
        private const float CellSizeMeters = 10f;

        private static readonly object Gate = new object();
        private static readonly Dictionary<CellKey, List<IndexedContainer>> Cells =
            new Dictionary<CellKey, List<IndexedContainer>>();
        private static readonly Dictionary<int, CellKey> Membership =
            new Dictionary<int, CellKey>();

        internal static void Register(Container container)
        {
            if (container == null) return;
            int instanceId;
            Vector3 position;
            try
            {
                instanceId = container.GetInstanceID();
                position = container.transform.position;
            }
            catch { return; }
            if (!IsFinite(position)) return;

            lock (Gate)
            {
                CellKey next = CellKey.From(position);
                if (Membership.TryGetValue(instanceId, out CellKey current) &&
                    current.Equals(next) && Cells.TryGetValue(current, out List<IndexedContainer> existing) &&
                    ContainsExact(existing, instanceId, container)) return;
                UnregisterLocked(container, instanceId);
                if (!Cells.TryGetValue(next, out List<IndexedContainer> entries))
                {
                    entries = new List<IndexedContainer>();
                    Cells.Add(next, entries);
                }
                entries.Add(new IndexedContainer(instanceId, container));
                Membership[instanceId] = next;
            }
        }

        internal static void Unregister(Container container)
        {
            if (ReferenceEquals(container, null)) return;
            int instanceId;
            try { instanceId = container.GetInstanceID(); }
            catch { return; }
            lock (Gate) UnregisterLocked(container, instanceId);
        }

        internal static IReadOnlyList<NearbySeedContainerCandidate> Query(
            Vector3 origin,
            float rangeMeters)
        {
            if (!IsFinite(origin) || float.IsNaN(rangeMeters) || float.IsInfinity(rangeMeters))
                return Array.Empty<NearbySeedContainerCandidate>();
            float range = Mathf.Clamp(rangeMeters, 1f, HardMaximumRangeMeters);
            float rangeSquared = range * range;
            CellKey minimum = CellKey.From(origin - new Vector3(range, 0f, range));
            CellKey maximum = CellKey.From(origin + new Vector3(range, 0f, range));
            var candidates = new Dictionary<ZDOID, NearbySeedContainerCandidate>();
            var ambiguous = new HashSet<ZDOID>();

            lock (Gate)
            {
                for (int x = minimum.X; x <= maximum.X; x++)
                for (int z = minimum.Z; z <= maximum.Z; z++)
                {
                    var cell = new CellKey(x, z);
                    if (!Cells.TryGetValue(cell, out List<IndexedContainer> entries)) continue;
                    for (int index = entries.Count - 1; index >= 0; index--)
                    {
                        IndexedContainer indexed = entries[index];
                        if (indexed.Container == null)
                        {
                            entries.RemoveAt(index);
                            Membership.Remove(indexed.InstanceId);
                            continue;
                        }
                        if (!TryCandidate(indexed.Container, origin, rangeSquared,
                                out NearbySeedContainerCandidate candidate)) continue;
                        if (ambiguous.Contains(candidate.EndpointId)) continue;
                        if (candidates.ContainsKey(candidate.EndpointId))
                        {
                            candidates.Remove(candidate.EndpointId);
                            ambiguous.Add(candidate.EndpointId);
                            continue;
                        }
                        candidates.Add(candidate.EndpointId, candidate);
                    }
                    if (entries.Count == 0) Cells.Remove(cell);
                }
            }

            var ordered = new List<NearbySeedContainerCandidate>(candidates.Values);
            ordered.Sort(CompareCandidate);
            if (ordered.Count > HardMaximumCandidates)
                ordered.RemoveRange(HardMaximumCandidates, ordered.Count - HardMaximumCandidates);
            return ordered.AsReadOnly();
        }

        internal static bool IsStaticChest(Container container)
        {
            if (container == null || container.m_wagon != null) return false;
            try
            {
                Rigidbody body = container.GetComponentInParent<Rigidbody>();
                return body == null || body.isKinematic;
            }
            catch { return false; }
        }

        internal static void Clear()
        {
            lock (Gate)
            {
                Cells.Clear();
                Membership.Clear();
            }
        }

        private static bool TryCandidate(
            Container container,
            Vector3 origin,
            float rangeSquared,
            out NearbySeedContainerCandidate candidate)
        {
            candidate = default;
            if (container == null || !container.isActiveAndEnabled || !IsStaticChest(container))
                return false;
            ZNetView view = NearbySeedResourceService.ContainerNetworkView(container);
            ZDO zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            if (zdo == null || !zdo.IsValid() || zdo.m_uid.IsNone()) return false;
            Vector3 position = container.transform.position;
            float distanceSquared = (position - origin).sqrMagnitude;
            if (!IsFinite(position) || float.IsNaN(distanceSquared) ||
                float.IsInfinity(distanceSquared) || distanceSquared > rangeSquared) return false;
            candidate = new NearbySeedContainerCandidate(container, zdo.m_uid, distanceSquared);
            return true;
        }

        private static int CompareCandidate(
            NearbySeedContainerCandidate left,
            NearbySeedContainerCandidate right)
        {
            int distance = left.DistanceSquared.CompareTo(right.DistanceSquared);
            if (distance != 0) return distance;
            int user = left.EndpointId.UserID.CompareTo(right.EndpointId.UserID);
            return user != 0 ? user : left.EndpointId.ID.CompareTo(right.EndpointId.ID);
        }

        private static void UnregisterLocked(Container container, int instanceId)
        {
            if (!Membership.TryGetValue(instanceId, out CellKey cell)) return;
            if (Cells.TryGetValue(cell, out List<IndexedContainer> entries))
            {
                entries.RemoveAll(value => value.InstanceId == instanceId ||
                                           ReferenceEquals(value.Container, container));
                if (entries.Count == 0) Cells.Remove(cell);
            }
            Membership.Remove(instanceId);
        }

        private static bool ContainsExact(
            IReadOnlyList<IndexedContainer> entries,
            int instanceId,
            Container container)
        {
            for (int index = 0; index < entries.Count; index++)
                if (entries[index].InstanceId == instanceId &&
                    ReferenceEquals(entries[index].Container, container)) return true;
            return false;
        }

        private static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);

        private readonly struct CellKey : IEquatable<CellKey>
        {
            internal CellKey(int x, int z) { X = x; Z = z; }
            internal int X { get; }
            internal int Z { get; }
            internal static CellKey From(Vector3 position) => new CellKey(
                Mathf.FloorToInt(position.x / CellSizeMeters),
                Mathf.FloorToInt(position.z / CellSizeMeters));
            public bool Equals(CellKey other) => X == other.X && Z == other.Z;
            public override bool Equals(object obj) => obj is CellKey other && Equals(other);
            public override int GetHashCode() => unchecked((X * 397) ^ Z);
        }

        private readonly struct IndexedContainer
        {
            internal IndexedContainer(int instanceId, Container container)
            {
                InstanceId = instanceId;
                Container = container;
            }
            internal int InstanceId { get; }
            internal Container Container { get; }
        }
    }

    internal readonly struct NearbySeedContainerCandidate
    {
        internal NearbySeedContainerCandidate(Container container, ZDOID endpointId, float distanceSquared)
        {
            Container = container;
            EndpointId = endpointId;
            DistanceSquared = distanceSquared;
        }
        internal Container Container { get; }
        internal ZDOID EndpointId { get; }
        internal float DistanceSquared { get; }
    }

    /// <summary>
    /// Resolves planting resources from the local player's inventory first, then exact nearby
    /// chest endpoints in distance/ZDO order. Chest use requires native access, ward permission,
    /// current local ZDO ownership, and an inventory that exactly matches its synchronized ZDO.
    /// This code never claims ownership and never sends a custom RPC.
    /// </summary>
    internal static class NearbySeedResourceService
    {
        private static readonly MethodInfo CheckAccessMethod = AccessTools.Method(
            typeof(Container), "CheckAccess", new[] { typeof(long) });
        private static readonly MethodInfo LoadMethod = AccessTools.Method(
            typeof(Container), "Load", Type.EmptyTypes);
        private static readonly FieldInfo LoadingField = AccessTools.Field(
            typeof(Container), "m_loading");
        private static readonly FieldInfo ContainerViewField = AccessTools.Field(
            typeof(Container), "m_nview");

        internal static ZNetView ContainerNetworkView(Container container)
        {
            if (container == null) return null;
            if (container.m_rootObjectOverride != null) return container.m_rootObjectOverride;
            try { return ContainerViewField?.GetValue(container) as ZNetView; }
            catch { return null; }
        }

        internal static int AvailablePlantings(Player player, Piece piece, float rangeMeters)
        {
            if (!CanMutatePlayer(player) || piece == null) return 0;
            IReadOnlyList<PlantResourceRequirement> requirements = Requirements(piece);
            if (requirements.Count == 0) return int.MaxValue;
            var totals = new Dictionary<string, int>(StringComparer.Ordinal);
            AddInventoryCounts(player.GetInventory(), requirements, totals);
            IReadOnlyList<NearbySeedContainerCandidate> candidates =
                NearbySeedContainerIndex.Query(player.transform.position, rangeMeters);
            for (int index = 0; index < candidates.Count; index++)
                if (TryGetExactWritableInventory(
                        candidates[index], player, player.transform.position, rangeMeters,
                        out Inventory inventory))
                    AddInventoryCounts(inventory, requirements, totals);
            return SeedResourceMath.MaximumPlantings(requirements, totals);
        }

        internal static bool TryDebitOne(
            Player player,
            Piece piece,
            float rangeMeters,
            bool consumeResources,
            out PlantResourceDebit debit)
        {
            debit = null;
            if (!CanMutatePlayer(player) || piece == null) return false;
            if (!consumeResources)
            {
                debit = PlantResourceDebit.Empty;
                return true;
            }

            IReadOnlyList<PlantResourceRequirement> requirements = Requirements(piece);
            if (requirements.Count == 0)
            {
                debit = PlantResourceDebit.Empty;
                return true;
            }
            List<ResourceSource> sources = ResolveSources(player, rangeMeters);
            var snapshots = new List<ResourceSnapshot>();
            try
            {
                // Synchronize every selected source once, then create the removal plan from those
                // final live ItemData references. Later authority checks deliberately do not call
                // Container.Load, which could replace the exact planned stack objects.
                for (int index = sources.Count - 1; index >= 0; index--)
                {
                    ResourceSource source = sources[index];
                    if (SourceIsCurrent(source, player, rangeMeters)) continue;
                    if (source.Container == null) return false;
                    sources.RemoveAt(index);
                }
                List<PlantResourceRemoval> removals = PlanRemovals(requirements, sources);
                if (removals == null) return false;
                for (int index = 0; index < sources.Count; index++)
                {
                    ResourceSource source = sources[index];
                    if (!IsTouched(source.Inventory, removals)) continue;
                    snapshots.Add(new ResourceSnapshot(source, Save(source.Inventory).GetArray()));
                }
                for (int index = 0; index < removals.Count; index++)
                {
                    PlantResourceRemoval removal = removals[index];
                    if (!SourceAuthorityStillCurrent(removal.Source, player, rangeMeters) ||
                        removal.Item == null || removal.Item.m_stack < removal.Amount ||
                        !removal.Source.Inventory.RemoveItem(removal.Item, removal.Amount))
                        throw new InvalidOperationException("A planting resource changed before debit.");
                }
                for (int index = 0; index < snapshots.Count; index++)
                    if (!SourceIsCurrent(snapshots[index].Source, player, rangeMeters) ||
                        snapshots[index].Source.Container != null &&
                        !InventoryMatchesZdo(
                            snapshots[index].Source.Container,
                            snapshots[index].Source.Inventory,
                            snapshots[index].Source.EndpointId))
                        throw new InvalidOperationException(
                            "A nearby planting chest did not publish its exact debit.");
                debit = new PlantResourceDebit(player, rangeMeters, snapshots);
                return true;
            }
            catch (Exception failure)
            {
                try { Restore(player, rangeMeters, snapshots); }
                catch (Exception rollbackFailure)
                {
                    throw new AggregateException(
                        "A planting debit failed and one or more rollbacks also failed.",
                        failure,
                        rollbackFailure);
                }
                throw;
            }
        }

        internal static IReadOnlyList<PlantResourceRequirement> Requirements(Piece piece)
        {
            var result = new List<PlantResourceRequirement>();
            Piece.Requirement[] requirements = piece?.m_resources ?? Array.Empty<Piece.Requirement>();
            for (int index = 0; index < requirements.Length; index++)
            {
                Piece.Requirement requirement = requirements[index];
                if (requirement?.m_resItem == null) continue;
                string name = requirement.m_resItem.m_itemData?.m_shared?.m_name;
                if (string.IsNullOrWhiteSpace(name)) continue;
                result.Add(new PlantResourceRequirement(
                    name,
                    Math.Max(1, requirement.GetAmount(0))));
            }
            return result.AsReadOnly();
        }

        private static List<ResourceSource> ResolveSources(Player player, float rangeMeters)
        {
            var sources = new List<ResourceSource>
            {
                new ResourceSource(player.GetInventory(), null, default, 0f)
            };
            IReadOnlyList<NearbySeedContainerCandidate> candidates =
                NearbySeedContainerIndex.Query(player.transform.position, rangeMeters);
            for (int index = 0; index < candidates.Count; index++)
            {
                NearbySeedContainerCandidate candidate = candidates[index];
                if (TryGetExactWritableInventory(
                        candidate, player, player.transform.position, rangeMeters,
                        out Inventory inventory))
                    sources.Add(new ResourceSource(
                        inventory, candidate.Container, candidate.EndpointId,
                        candidate.DistanceSquared));
            }
            return sources;
        }

        private static List<PlantResourceRemoval> PlanRemovals(
            IReadOnlyList<PlantResourceRequirement> requirements,
            IReadOnlyList<ResourceSource> sources)
        {
            var required = new SortedDictionary<string, int>(StringComparer.Ordinal);
            for (int index = 0; index < requirements.Count; index++)
            {
                PlantResourceRequirement requirement = requirements[index];
                required.TryGetValue(requirement.ResourceId, out int current);
                required[requirement.ResourceId] = AddSaturated(current, requirement.Amount);
            }

            var result = new List<PlantResourceRemoval>();
            foreach (KeyValuePair<string, int> requirement in required)
            {
                int remaining = requirement.Value;
                for (int sourceIndex = 0; sourceIndex < sources.Count && remaining > 0; sourceIndex++)
                {
                    ResourceSource source = sources[sourceIndex];
                    List<ItemDrop.ItemData> items = MatchingItems(source.Inventory, requirement.Key);
                    for (int itemIndex = 0; itemIndex < items.Count && remaining > 0; itemIndex++)
                    {
                        ItemDrop.ItemData item = items[itemIndex];
                        int amount = Math.Min(remaining, Math.Max(0, item.m_stack));
                        if (amount <= 0) continue;
                        result.Add(new PlantResourceRemoval(source, item, amount));
                        remaining -= amount;
                    }
                }
                if (remaining > 0) return null;
            }
            return result;
        }

        private static void AddInventoryCounts(
            Inventory inventory,
            IReadOnlyList<PlantResourceRequirement> requirements,
            IDictionary<string, int> totals)
        {
            if (inventory == null) return;
            var wanted = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < requirements.Count; index++)
                wanted.Add(requirements[index].ResourceId);
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
            {
                string name = item?.m_shared?.m_name;
                if (string.IsNullOrEmpty(name) || !wanted.Contains(name) || item.m_stack <= 0) continue;
                totals.TryGetValue(name, out int current);
                totals[name] = AddSaturated(current, item.m_stack);
            }
        }

        private static List<ItemDrop.ItemData> MatchingItems(Inventory inventory, string resourceId)
        {
            var result = new List<ItemDrop.ItemData>();
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
                if (item != null && item.m_stack > 0 &&
                    string.Equals(item.m_shared?.m_name, resourceId, StringComparison.Ordinal))
                    result.Add(item);
            result.Sort((left, right) =>
            {
                int row = left.m_gridPos.y.CompareTo(right.m_gridPos.y);
                return row != 0 ? row : left.m_gridPos.x.CompareTo(right.m_gridPos.x);
            });
            return result;
        }

        private static bool TryGetExactWritableInventory(
            NearbySeedContainerCandidate candidate,
            Player player,
            Vector3 origin,
            float rangeMeters,
            out Inventory inventory)
        {
            inventory = null;
            Container container = candidate.Container;
            float range = Mathf.Clamp(rangeMeters, 1f, NearbySeedContainerIndex.HardMaximumRangeMeters);
            if (!CanMutatePlayer(player) || ZNet.instance == null || container == null ||
                !container.isActiveAndEnabled || !NearbySeedContainerIndex.IsStaticChest(container) ||
                (container.transform.position - origin).sqrMagnitude > range * range ||
                container.IsInUse() || !container.IsOwner() ||
                CheckAccessMethod == null || LoadMethod == null || LoadingField == null)
                return false;
            if (!PrivateArea.CheckAccess(
                    container.transform.position, 0f, flash: false, wardCheck: false)) return false;
            try
            {
                if (!(bool)CheckAccessMethod.Invoke(
                        container, new object[] { player.GetPlayerID() }) ||
                    (bool)LoadingField.GetValue(container)) return false;
                ZNetView view = ContainerNetworkView(container);
                ZDO zdo = view != null && view.IsValid() && view.IsOwner() ? view.GetZDO() : null;
                if (zdo == null || !zdo.IsValid() || zdo.m_uid != candidate.EndpointId ||
                    zdo.GetOwner() != ZNet.GetUID() || zdo.GetInt(ZDOVars.s_inUse, 0) != 0) return false;
                LoadMethod.Invoke(container, Array.Empty<object>());
                inventory = container.GetInventory();
                if (inventory == null || !InventoryMatchesZdo(container, inventory, candidate.EndpointId))
                {
                    inventory = null;
                    return false;
                }
                return true;
            }
            catch
            {
                inventory = null;
                return false;
            }
        }

        private static bool SourceIsCurrent(ResourceSource source, Player player, float rangeMeters)
        {
            if (source.Container == null)
                return CanMutatePlayer(player) && ReferenceEquals(source.Inventory, player.GetInventory());
            var candidate = new NearbySeedContainerCandidate(
                source.Container, source.EndpointId, source.DistanceSquared);
            return TryGetExactWritableInventory(
                       candidate, player, player.transform.position, rangeMeters,
                       out Inventory current) && ReferenceEquals(current, source.Inventory);
        }

        private static bool SourceAuthorityStillCurrent(
            ResourceSource source,
            Player player,
            float rangeMeters)
        {
            if (source.Container == null)
                return CanMutatePlayer(player) && ReferenceEquals(source.Inventory, player.GetInventory());
            Container container = source.Container;
            float range = Mathf.Clamp(rangeMeters, 1f, NearbySeedContainerIndex.HardMaximumRangeMeters);
            if (!CanMutatePlayer(player) || ZNet.instance == null || container == null ||
                !container.isActiveAndEnabled || !NearbySeedContainerIndex.IsStaticChest(container) ||
                (container.transform.position - player.transform.position).sqrMagnitude > range * range ||
                container.IsInUse() || !container.IsOwner() || LoadingField == null ||
                CheckAccessMethod == null || !ReferenceEquals(container.GetInventory(), source.Inventory))
                return false;
            if (!PrivateArea.CheckAccess(
                    container.transform.position, 0f, flash: false, wardCheck: false)) return false;
            try
            {
                if ((bool)LoadingField.GetValue(container) ||
                    !(bool)CheckAccessMethod.Invoke(
                        container, new object[] { player.GetPlayerID() })) return false;
                ZNetView view = ContainerNetworkView(container);
                ZDO zdo = view != null && view.IsValid() && view.IsOwner() ? view.GetZDO() : null;
                return zdo != null && zdo.IsValid() && zdo.m_uid == source.EndpointId &&
                       zdo.GetOwner() == ZNet.GetUID() && zdo.GetInt(ZDOVars.s_inUse, 0) == 0;
            }
            catch { return false; }
        }

        private static bool InventoryMatchesZdo(
            Container container,
            Inventory inventory,
            ZDOID endpointId)
        {
            ZNetView view = ContainerNetworkView(container);
            ZDO zdo = view != null && view.IsValid() && view.IsOwner() ? view.GetZDO() : null;
            if (zdo == null || zdo.m_uid != endpointId || zdo.GetOwner() != ZNet.GetUID()) return false;
            byte[] persisted = zdo.GetByteArray(ZDOVars.s_items);
            byte[] current = Save(inventory).GetArray();
            return persisted == null || persisted.Length == 0
                ? inventory.GetAllItems().Count == 0
                : AgricultureInventoryPayloadComparison.MatchesLoaded(persisted, current);
        }

        private static bool CanMutatePlayer(Player player) =>
            player != null && ReferenceEquals(player, Player.m_localPlayer) && player.IsOwner();

        private static bool IsTouched(
            Inventory inventory,
            IReadOnlyList<PlantResourceRemoval> removals)
        {
            for (int index = 0; index < removals.Count; index++)
                if (ReferenceEquals(inventory, removals[index].Source.Inventory)) return true;
            return false;
        }

        private static ZPackage Save(Inventory inventory)
        {
            var package = new ZPackage();
            inventory.Save(package);
            return package;
        }

        private static void Restore(
            Player player,
            float rangeMeters,
            IReadOnlyList<ResourceSnapshot> snapshots)
        {
            var failures = new List<Exception>();
            for (int index = snapshots.Count - 1; index >= 0; index--)
            {
                ResourceSnapshot snapshot = snapshots[index];
                try
                {
                    if (!SourceIsCurrent(snapshot.Source, player, rangeMeters))
                        throw new InvalidOperationException(
                            "A planting resource endpoint lost authority before rollback.");
                    string expected = Convert.ToBase64String(snapshot.Payload);
                    ValidateSnapshotRoundTrip(snapshot.Source.Inventory, snapshot.Payload, expected);
                    snapshot.Source.Inventory.Load(new ZPackage(snapshot.Payload));
                    if (!string.Equals(
                            Save(snapshot.Source.Inventory).GetBase64(), expected,
                            StringComparison.Ordinal))
                        throw new InvalidOperationException(
                            "A planting resource snapshot did not restore exactly.");
                    if (snapshot.Source.Container != null &&
                        !InventoryMatchesZdo(
                            snapshot.Source.Container,
                            snapshot.Source.Inventory,
                            snapshot.Source.EndpointId))
                        throw new InvalidOperationException(
                            "A restored planting chest did not publish exactly.");
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }
            if (failures.Count > 0)
                throw new AggregateException(
                    "One or more planting-resource rollbacks failed.", failures);
        }

        private static void ValidateSnapshotRoundTrip(
            Inventory shape,
            byte[] payload,
            string expected)
        {
            if (shape == null || payload == null || string.IsNullOrEmpty(expected))
                throw new InvalidOperationException("A planting resource snapshot is unavailable.");
            var shadow = new Inventory(
                shape.GetName(), null, shape.GetWidth(), shape.GetHeight());
            shadow.Load(new ZPackage(payload));
            if (!string.Equals(Save(shadow).GetBase64(), expected, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "A planting resource snapshot cannot round-trip exactly.");
        }

        private static int AddSaturated(int left, int right) =>
            left > int.MaxValue - right ? int.MaxValue : left + right;

        internal readonly struct ResourceSource
        {
            internal ResourceSource(
                Inventory inventory,
                Container container,
                ZDOID endpointId,
                float distanceSquared)
            {
                Inventory = inventory;
                Container = container;
                EndpointId = endpointId;
                DistanceSquared = distanceSquared;
            }
            internal Inventory Inventory { get; }
            internal Container Container { get; }
            internal ZDOID EndpointId { get; }
            internal float DistanceSquared { get; }
        }

        private readonly struct PlantResourceRemoval
        {
            internal PlantResourceRemoval(ResourceSource source, ItemDrop.ItemData item, int amount)
            {
                Source = source;
                Item = item;
                Amount = amount;
            }
            internal ResourceSource Source { get; }
            internal ItemDrop.ItemData Item { get; }
            internal int Amount { get; }
        }

        internal readonly struct ResourceSnapshot
        {
            internal ResourceSnapshot(ResourceSource source, byte[] payload)
            {
                Source = source;
                Payload = payload;
            }
            internal ResourceSource Source { get; }
            internal byte[] Payload { get; }
        }

        internal sealed class PlantResourceDebit : IDisposable
        {
            internal static readonly PlantResourceDebit Empty =
                new PlantResourceDebit(null, 0f, Array.Empty<ResourceSnapshot>(), completed: true);
            private readonly Player _player;
            private readonly float _rangeMeters;
            private readonly IReadOnlyList<ResourceSnapshot> _snapshots;
            private bool _completed;

            internal PlantResourceDebit(
                Player player,
                float rangeMeters,
                IReadOnlyList<ResourceSnapshot> snapshots,
                bool completed = false)
            {
                _player = player;
                _rangeMeters = rangeMeters;
                _snapshots = snapshots;
                _completed = completed;
            }

            internal void Complete() => _completed = true;

            public void Dispose()
            {
                if (_completed) return;
                Restore(_player, _rangeMeters, _snapshots);
                _completed = true;
            }
        }
    }

    [HarmonyPatch(typeof(Container), "Awake")]
    internal static class AgricultureSeedContainerAwakePatch
    {
        private static void Postfix(Container __instance) =>
            NearbySeedContainerIndex.Register(__instance);
    }

    [HarmonyPatch(typeof(Container), "OnDestroyed")]
    internal static class AgricultureSeedContainerDestroyedPatch
    {
        private static void Postfix(Container __instance, bool __runOriginal)
        {
            if (__runOriginal) NearbySeedContainerIndex.Unregister(__instance);
        }
    }

    [HarmonyPatch(typeof(Container), "CheckForChanges")]
    internal static class AgricultureSeedContainerRefreshPatch
    {
        private static void Postfix(Container __instance) =>
            NearbySeedContainerIndex.Register(__instance);
    }
}

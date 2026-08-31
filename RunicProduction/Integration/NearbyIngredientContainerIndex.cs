using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using HarmonyLib;
using UnityEngine;

namespace RunicProduction.Integration
{
    /// <summary>
    /// One exact, currently loaded container returned by the station-centered nearby-ingredient
    /// index. This is discovery evidence only; the recipe runtime must still resolve the exact
    /// ZDOID and recheck native ownership, access, wards, and the synchronized inventory
    /// immediately before planning or mutation.
    /// </summary>
    internal readonly struct NearbyIngredientContainerCandidate
    {
        internal NearbyIngredientContainerCandidate(
            Container container,
            ZDOID endpointId,
            float distanceSquared)
        {
            Container = container ?? throw new ArgumentNullException(nameof(container));
            if (endpointId.IsNone())
                throw new ArgumentException("An exact container endpoint is required.", nameof(endpointId));
            if (float.IsNaN(distanceSquared) || float.IsInfinity(distanceSquared) ||
                distanceSquared < 0f)
                throw new ArgumentOutOfRangeException(nameof(distanceSquared));
            EndpointId = endpointId;
            DistanceSquared = distanceSquared;
        }

        internal Container Container { get; }
        internal ZDOID EndpointId { get; }
        internal float DistanceSquared { get; }
    }

    internal sealed class NearbyIngredientContainerQueryResult
    {
        private readonly ReadOnlyCollection<NearbyIngredientContainerCandidate> _candidates;

        internal NearbyIngredientContainerQueryResult(
            IEnumerable<NearbyIngredientContainerCandidate> candidates,
            int candidateCount,
            bool truncated)
        {
            var copy = candidates == null
                ? new List<NearbyIngredientContainerCandidate>()
                : new List<NearbyIngredientContainerCandidate>(candidates);
            if (candidateCount < copy.Count)
                throw new ArgumentOutOfRangeException(nameof(candidateCount));
            CandidateCount = candidateCount;
            Truncated = truncated;
            _candidates = new ReadOnlyCollection<NearbyIngredientContainerCandidate>(copy);
        }

        internal IReadOnlyList<NearbyIngredientContainerCandidate> Candidates => _candidates;
        internal int CandidateCount { get; }
        internal bool Truncated { get; }
    }

    /// <summary>
    /// Event-maintained spatial index for loaded, static ingredient chests. Queries are bounded,
    /// station-centered, and deterministic by distance followed by numeric ZDOID. Index membership
    /// is not authorization and never exposes an Inventory.
    /// </summary>
    internal static class NearbyIngredientContainerIndex
    {
        internal const float CellSizeMeters = 10f;
        internal const float HardMaximumRadiusMeters = 30f;
        internal const int HardMaximumSourceChests = 64;

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
            catch
            {
                return;
            }

            lock (Gate)
            {
                if (!IsFinite(position))
                {
                    UnregisterLocked(container, instanceId);
                    return;
                }
                CellKey cell = CellKey.From(position);
                bool membershipExists = Membership.TryGetValue(instanceId, out CellKey oldCell);
                if (membershipExists && oldCell.Equals(cell) &&
                    Cells.TryGetValue(oldCell, out List<IndexedContainer> currentEntries) &&
                    ContainsExact(currentEntries, instanceId, container))
                    return;

                UnregisterLocked(container, instanceId);
                if (!Cells.TryGetValue(cell, out List<IndexedContainer> entries))
                {
                    entries = new List<IndexedContainer>();
                    Cells.Add(cell, entries);
                }
                entries.Add(new IndexedContainer(instanceId, container));
                Membership[instanceId] = cell;
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

        internal static void SeedLoadedContainers()
        {
            Clear();
            Container[] loaded = UnityEngine.Object.FindObjectsByType<Container>(
                FindObjectsSortMode.None);
            if (loaded == null) return;
            foreach (Container container in loaded) Register(container);
        }

        internal static void Clear()
        {
            lock (Gate)
            {
                Cells.Clear();
                Membership.Clear();
            }
        }

        internal static NearbyIngredientContainerQueryResult Query(
            Vector3 origin,
            float radiusMeters,
            int maximumSourceChests)
        {
            if (!IsFinite(origin) || float.IsNaN(radiusMeters) ||
                float.IsInfinity(radiusMeters) || radiusMeters <= 0f ||
                maximumSourceChests <= 0)
                return new NearbyIngredientContainerQueryResult(
                    Array.Empty<NearbyIngredientContainerCandidate>(), 0, false);

            float radius = Math.Min(HardMaximumRadiusMeters, radiusMeters);
            int maximum = Math.Min(HardMaximumSourceChests, maximumSourceChests);
            float radiusSquared = radius * radius;
            CellKey minimum = CellKey.From(origin - new Vector3(radius, 0f, radius));
            CellKey maximumCell = CellKey.From(origin + new Vector3(radius, 0f, radius));
            var unique = new Dictionary<ZDOID, NearbyIngredientContainerCandidate>();
            var ambiguous = new HashSet<ZDOID>();

            lock (Gate)
            {
                for (int x = minimum.X; x <= maximumCell.X; x++)
                {
                    for (int z = minimum.Z; z <= maximumCell.Z; z++)
                    {
                        var cell = new CellKey(x, z);
                        if (!Cells.TryGetValue(cell, out List<IndexedContainer> entries)) continue;
                        for (int index = entries.Count - 1; index >= 0; index--)
                        {
                            IndexedContainer indexed = entries[index];
                            Container container = indexed.Container;
                            if (container == null)
                            {
                                entries.RemoveAt(index);
                                Membership.Remove(indexed.InstanceId);
                                continue;
                            }
                            if (!TryCreateCandidate(
                                    container, origin, radiusSquared,
                                    out NearbyIngredientContainerCandidate candidate)) continue;
                            if (ambiguous.Contains(candidate.EndpointId)) continue;
                            if (unique.ContainsKey(candidate.EndpointId))
                            {
                                unique.Remove(candidate.EndpointId);
                                ambiguous.Add(candidate.EndpointId);
                                continue;
                            }
                            unique.Add(candidate.EndpointId, candidate);
                        }
                        if (entries.Count == 0) Cells.Remove(cell);
                    }
                }
            }

            var ordered = new List<NearbyIngredientContainerCandidate>(unique.Values);
            ordered.Sort(CompareCandidates);
            int candidateCount = ordered.Count;
            bool truncated = candidateCount > maximum;
            if (truncated) ordered.RemoveRange(maximum, candidateCount - maximum);
            return new NearbyIngredientContainerQueryResult(
                ordered, candidateCount, truncated);
        }

        /// <summary>
        /// Rejects carts, ships, and other dynamically moving container roots. The runtime still
        /// has to re-evaluate this immediately before using a returned source.
        /// </summary>
        internal static bool IsStaticNonWagon(Container container)
        {
            if (container == null || container.m_wagon != null) return false;
            try
            {
                Rigidbody body = container.GetComponentInParent<Rigidbody>();
                return body == null || body.isKinematic;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryCreateCandidate(
            Container container,
            Vector3 origin,
            float radiusSquared,
            out NearbyIngredientContainerCandidate candidate)
        {
            candidate = default;
            if (container == null || !container.isActiveAndEnabled ||
                !IsStaticNonWagon(container)) return false;
            ZNetView view = ValheimAccess.View(container);
            ZDO zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            if (zdo == null || !zdo.IsValid() || zdo.m_uid.IsNone()) return false;
            Vector3 position = container.transform.position;
            if (!IsFinite(position)) return false;
            float distanceSquared = (position - origin).sqrMagnitude;
            if (float.IsNaN(distanceSquared) || float.IsInfinity(distanceSquared) ||
                distanceSquared > radiusSquared) return false;
            candidate = new NearbyIngredientContainerCandidate(
                container, zdo.m_uid, distanceSquared);
            return true;
        }

        private static int CompareCandidates(
            NearbyIngredientContainerCandidate left,
            NearbyIngredientContainerCandidate right)
            => CompareKeys(
                left.DistanceSquared, left.EndpointId,
                right.DistanceSquared, right.EndpointId);

        /// <summary>Pure ordering primitive shared with structural/domain tests.</summary>
        internal static int CompareKeys(
            float leftDistanceSquared,
            ZDOID leftEndpointId,
            float rightDistanceSquared,
            ZDOID rightEndpointId)
        {
            int distance = leftDistanceSquared.CompareTo(rightDistanceSquared);
            if (distance != 0) return distance;
            int user = leftEndpointId.UserID.CompareTo(rightEndpointId.UserID);
            return user != 0 ? user : leftEndpointId.ID.CompareTo(rightEndpointId.ID);
        }

        private static void UnregisterLocked(Container container, int instanceId)
        {
            if (!Membership.TryGetValue(instanceId, out CellKey oldCell)) return;
            if (Cells.TryGetValue(oldCell, out List<IndexedContainer> entries))
            {
                entries.RemoveAll(value =>
                    value.InstanceId == instanceId ||
                    ReferenceEquals(value.Container, container));
                if (entries.Count == 0) Cells.Remove(oldCell);
            }
            Membership.Remove(instanceId);
        }

        private static bool ContainsExact(
            List<IndexedContainer> entries,
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
            internal CellKey(int x, int z)
            {
                X = x;
                Z = z;
            }

            internal int X { get; }
            internal int Z { get; }

            internal static CellKey From(Vector3 position) =>
                new CellKey(
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

    [HarmonyPatch(typeof(Container), "Awake")]
    internal static class NearbyIngredientContainerAwakePatch
    {
        private static void Postfix(Container __instance) =>
            NearbyIngredientContainerIndex.Register(__instance);
    }

    [HarmonyPatch(typeof(Container), "OnDestroyed")]
    internal static class NearbyIngredientContainerDestroyedPatch
    {
        private static void Postfix(Container __instance, bool __runOriginal)
        {
            if (!__runOriginal) return;
            NearbyIngredientContainerIndex.Unregister(__instance);
        }
    }

    [HarmonyPatch(typeof(Container), "CheckForChanges")]
    internal static class NearbyIngredientContainerRefreshPatch
    {
        private static void Postfix(Container __instance) =>
            NearbyIngredientContainerIndex.Register(__instance);
    }
}

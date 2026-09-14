using System;
using System.Collections.Generic;
using RunicCrafting.Domain;
using UnityEngine;

namespace RunicCrafting.Integration
{
    internal static class ContainerSpatialIndex
    {
        private const float CellSize = 10f;
        private static readonly object Gate = new object();
        private static readonly Dictionary<CellKey, List<Container>> Cells = new Dictionary<CellKey, List<Container>>();
        private static readonly Dictionary<int, CellKey> Membership = new Dictionary<int, CellKey>();

        internal static void Register(Container container)
        {
            if (container == null) return;
            lock (Gate)
            {
                int instanceId = container.GetInstanceID();
                CellKey cell = CellKey.From(container.transform.position);
                bool membershipExists = Membership.TryGetValue(instanceId, out CellKey oldCell);
                bool cellUnchanged = membershipExists && oldCell.Equals(cell);
                bool exactContainerPresent =
                    cellUnchanged &&
                    Cells.TryGetValue(oldCell, out List<Container> oldEntries) &&
                    ContainsExact(oldEntries, container);
                if (SpatialMembershipPolicy.CanRetainExistingRegistration(
                        membershipExists,
                        cellUnchanged,
                        exactContainerPresent)) return;

                PreviewRefreshRuntime.Invalidate();
                UiPreviewCache.Invalidate();
                UnregisterLocked(container, instanceId);
                if (!Cells.TryGetValue(cell, out List<Container> entries))
                {
                    entries = new List<Container>();
                    Cells.Add(cell, entries);
                }
                entries.Add(container);
                Membership[instanceId] = cell;
            }
        }

        internal static void Unregister(Container container)
        {
            if (ReferenceEquals(container, null)) return;
            lock (Gate) UnregisterLocked(container, container.GetInstanceID());
        }

        internal static IReadOnlyList<Container> Query(Vector3 origin, float radius, int maximumCandidates)
        {
            radius = Math.Max(1f, Math.Min(50f, radius));
            maximumCandidates = Math.Max(1, Math.Min(256, maximumCandidates));
            var candidates = new List<Candidate>(maximumCandidates);
            float radiusSquared = radius * radius;
            CellKey minimum = CellKey.From(origin - new Vector3(radius, 0f, radius));
            CellKey maximum = CellKey.From(origin + new Vector3(radius, 0f, radius));

            lock (Gate)
            {
                for (int x = minimum.X; x <= maximum.X; x++)
                {
                    for (int z = minimum.Z; z <= maximum.Z; z++)
                    {
                        var cell = new CellKey(x, z);
                        if (!Cells.TryGetValue(cell, out List<Container> entries)) continue;
                        for (int index = entries.Count - 1; index >= 0; index--)
                        {
                            Container container = entries[index];
                            if (container == null)
                            {
                                entries.RemoveAt(index);
                                continue;
                            }

                            float distanceSquared = (container.transform.position - origin).sqrMagnitude;
                            if (distanceSquared > radiusSquared) continue;
                            candidates.Add(new Candidate(container, distanceSquared));
                            if (candidates.Count > maximumCandidates)
                                RemoveWorstCandidate(candidates);
                        }
                        if (entries.Count == 0) Cells.Remove(cell);
                    }
                }
            }

            candidates.Sort(CompareCandidate);
            var result = new List<Container>(candidates.Count);
            foreach (Candidate candidate in candidates) result.Add(candidate.Container);
            return result.AsReadOnly();
        }

        private static void RemoveWorstCandidate(List<Candidate> candidates)
        {
            int worst = 0;
            for (int index = 1; index < candidates.Count; index++)
                if (CompareCandidate(candidates[worst], candidates[index]) < 0) worst = index;
            candidates.RemoveAt(worst);
        }

        private static int CompareCandidate(Candidate left, Candidate right)
        {
            int comparison = left.DistanceSquared.CompareTo(right.DistanceSquared);
            return comparison != 0
                ? comparison
                : StringComparer.Ordinal.Compare(
                    ValheimReflection.ContainerEndpointId(left.Container),
                    ValheimReflection.ContainerEndpointId(right.Container));
        }

        internal static void Clear()
        {
            PreviewRefreshRuntime.Invalidate();
            UiPreviewCache.Invalidate();
            lock (Gate)
            {
                Cells.Clear();
                Membership.Clear();
            }
        }

        private static void UnregisterLocked(Container container, int instanceId)
        {
            if (!Membership.TryGetValue(instanceId, out CellKey oldCell)) return;
            PreviewRefreshRuntime.Invalidate();
            UiPreviewCache.Invalidate();
            if (Cells.TryGetValue(oldCell, out List<Container> entries))
            {
                entries.RemoveAll(value => value == null || ReferenceEquals(value, container));
                if (entries.Count == 0) Cells.Remove(oldCell);
            }
            Membership.Remove(instanceId);
        }

        private static bool ContainsExact(List<Container> entries, Container container)
        {
            if (entries == null) return false;
            for (int index = 0; index < entries.Count; index++)
                if (ReferenceEquals(entries[index], container)) return true;
            return false;
        }

        private readonly struct Candidate
        {
            internal Candidate(Container container, float distanceSquared)
            {
                Container = container;
                DistanceSquared = distanceSquared;
            }

            internal Container Container { get; }
            internal float DistanceSquared { get; }
        }

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
                new CellKey(Mathf.FloorToInt(position.x / CellSize), Mathf.FloorToInt(position.z / CellSize));
            public bool Equals(CellKey other) => X == other.X && Z == other.Z;
            public override bool Equals(object obj) => obj is CellKey other && Equals(other);
            public override int GetHashCode() => unchecked((X * 397) ^ Z);
        }
    }
}

using System;
using System.Collections.Generic;

namespace RunicStorage.Engine
{
    internal readonly struct SpatialCandidateKey : IComparable<SpatialCandidateKey>
    {
        internal SpatialCandidateKey(float distanceSquared, string endpointId, int instanceId)
        {
            if (float.IsNaN(distanceSquared) || float.IsInfinity(distanceSquared) || distanceSquared < 0f)
                throw new ArgumentOutOfRangeException(nameof(distanceSquared));
            DistanceSquared = distanceSquared;
            EndpointId = endpointId ?? string.Empty;
            InstanceId = instanceId;
        }

        internal float DistanceSquared { get; }
        internal string EndpointId { get; }
        internal int InstanceId { get; }

        public int CompareTo(SpatialCandidateKey other)
        {
            int distance = DistanceSquared.CompareTo(other.DistanceSquared);
            if (distance != 0) return distance;
            int endpoint = StringComparer.Ordinal.Compare(EndpointId, other.EndpointId);
            return endpoint != 0 ? endpoint : InstanceId.CompareTo(other.InstanceId);
        }
    }

    internal sealed class SpatialSelectionProfile
    {
        internal SpatialSelectionProfile(IReadOnlyList<SpatialCandidateKey> selected, int visited, bool truncated)
        { Selected = selected; Visited = visited; Truncated = truncated; }
        internal IReadOnlyList<SpatialCandidateKey> Selected { get; }
        internal int Visited { get; }
        internal bool Truncated { get; }
    }

    internal static class ContainerSpatialPolicy
    {
        internal const float CellSizeMeters = 10f;
        internal const float HardMaximumRadiusMeters = 50f;
        internal const int HardMaximumCandidates = 256;
        internal const int HardMaximumAbsoluteCell = 1_000_000;

        internal static bool CanRetain(bool membershipExists, bool cellUnchanged,
            bool endpointUnchanged, bool exactContainerPresent) =>
            membershipExists && cellUnchanged && endpointUnchanged && exactContainerPresent;

        internal static bool CanIndexEndpoint(string endpointId) =>
            !string.IsNullOrEmpty(endpointId);

        internal static bool IsUniqueEndpointMemberCount(int memberCount) => memberCount == 1;

        internal static int CellCoordinate(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            double coordinate = Math.Floor((double)value / CellSizeMeters);
            if (coordinate <= -HardMaximumAbsoluteCell) return -HardMaximumAbsoluteCell;
            if (coordinate >= HardMaximumAbsoluteCell) return HardMaximumAbsoluteCell;
            return (int)coordinate;
        }

        internal static int MaximumCellsForRadius(float radiusMeters)
        {
            float radius = Math.Max(0f, Math.Min(HardMaximumRadiusMeters, radiusMeters));
            int diameter = (int)Math.Ceiling(radius * 2f / CellSizeMeters) + 1;
            return checked(diameter * diameter);
        }

        internal static SpatialSelectionProfile ProfileNearest(
            IEnumerable<SpatialCandidateKey> source, int maximum)
        {
            maximum = Math.Max(1, Math.Min(HardMaximumCandidates, maximum));
            var selected = new SortedSet<SpatialCandidateKey>();
            int visited = 0;
            foreach (SpatialCandidateKey candidate in source ?? Array.Empty<SpatialCandidateKey>())
            {
                if (visited < int.MaxValue) visited++;
                selected.Add(candidate);
                if (selected.Count > maximum) selected.Remove(selected.Max);
            }
            return new SpatialSelectionProfile(new List<SpatialCandidateKey>(selected).AsReadOnly(),
                visited, visited > maximum);
        }
    }
}

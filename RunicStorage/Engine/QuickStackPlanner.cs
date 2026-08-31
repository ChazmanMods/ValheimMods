using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace RunicStorage.Engine
{
    public sealed class QuickStackSource
    {
        public QuickStackSource(string stackId, string resourceId, int quantity, bool protectedSlot = false)
        {
            StackId = Require(stackId, nameof(stackId));
            ResourceId = Require(resourceId, nameof(resourceId));
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            Quantity = quantity;
            ProtectedSlot = protectedSlot;
        }

        public string StackId { get; }
        public string ResourceId { get; }
        public int Quantity { get; }
        public bool ProtectedSlot { get; }

        internal static string Require(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A stable identifier is required.", parameterName);
            return value.Trim();
        }
    }

    public sealed class QuickStackDestination
    {
        private readonly ReadOnlyDictionary<string, int> _capacityByResource;

        public QuickStackDestination(
            string endpointId,
            float distanceSquared,
            bool authorized,
            bool personal,
            bool inUse,
            IDictionary<string, int> capacityByExistingResource)
        {
            EndpointId = QuickStackSource.Require(endpointId, nameof(endpointId));
            if (float.IsNaN(distanceSquared) || float.IsInfinity(distanceSquared) || distanceSquared < 0f)
                throw new ArgumentOutOfRangeException(nameof(distanceSquared));
            DistanceSquared = distanceSquared;
            Authorized = authorized;
            Personal = personal;
            InUse = inUse;
            var copy = new Dictionary<string, int>(StringComparer.Ordinal);
            if (capacityByExistingResource != null)
            {
                foreach (KeyValuePair<string, int> pair in capacityByExistingResource)
                {
                    string resource = QuickStackSource.Require(pair.Key, nameof(capacityByExistingResource));
                    if (pair.Value < 0) throw new ArgumentOutOfRangeException(nameof(capacityByExistingResource));
                    copy.Add(resource, pair.Value);
                }
            }
            _capacityByResource = new ReadOnlyDictionary<string, int>(copy);
        }

        public string EndpointId { get; }
        public float DistanceSquared { get; }
        public bool Authorized { get; }
        public bool Personal { get; }
        public bool InUse { get; }
        public IReadOnlyDictionary<string, int> CapacityByExistingResource => _capacityByResource;
    }

    public sealed class QuickStackMove
    {
        internal QuickStackMove(string stackId, string destinationEndpointId, string resourceId, int quantity)
        {
            StackId = stackId;
            DestinationEndpointId = destinationEndpointId;
            ResourceId = resourceId;
            Quantity = quantity;
        }

        public string StackId { get; }
        public string DestinationEndpointId { get; }
        public string ResourceId { get; }
        public int Quantity { get; }
    }

    public sealed class QuickStackPlan
    {
        internal QuickStackPlan(IReadOnlyList<QuickStackMove> moves, int requestedQuantity, int plannedQuantity)
        {
            Moves = moves;
            RequestedQuantity = requestedQuantity;
            PlannedQuantity = plannedQuantity;
        }

        public IReadOnlyList<QuickStackMove> Moves { get; }
        public int RequestedQuantity { get; }
        public int PlannedQuantity { get; }
        public int RemainingQuantity => RequestedQuantity - PlannedQuantity;
    }

    /// <summary>
    /// Pure deterministic planner. The caller supplies only authorized endpoint metadata, and the
    /// planner still fails closed for personal/in-use endpoints and protected carried stacks.
    /// </summary>
    public static class QuickStackPlanner
    {
        public static QuickStackPlan Plan(
            IEnumerable<QuickStackSource> sources,
            IEnumerable<QuickStackDestination> destinations,
            bool includePersonalContainers = false)
        {
            if (sources == null) throw new ArgumentNullException(nameof(sources));
            if (destinations == null) throw new ArgumentNullException(nameof(destinations));

            var orderedSources = new List<QuickStackSource>();
            int requested = 0;
            foreach (QuickStackSource source in sources)
            {
                if (source == null) throw new ArgumentException("Sources cannot contain null.", nameof(sources));
                orderedSources.Add(source);
                if (!source.ProtectedSlot) requested = AddSaturated(requested, source.Quantity);
            }
            orderedSources.Sort((left, right) => StringComparer.Ordinal.Compare(left.StackId, right.StackId));

            var orderedDestinations = new List<DestinationState>();
            foreach (QuickStackDestination destination in destinations)
            {
                if (destination == null) throw new ArgumentException("Destinations cannot contain null.", nameof(destinations));
                if (!destination.Authorized || destination.InUse || destination.Personal && !includePersonalContainers) continue;
                orderedDestinations.Add(new DestinationState(destination));
            }
            orderedDestinations.Sort((left, right) =>
            {
                int distance = left.DistanceSquared.CompareTo(right.DistanceSquared);
                return distance != 0 ? distance : StringComparer.Ordinal.Compare(left.EndpointId, right.EndpointId);
            });

            var moves = new List<QuickStackMove>();
            int planned = 0;
            foreach (QuickStackSource source in orderedSources)
            {
                if (source.ProtectedSlot) continue;
                int remaining = source.Quantity;
                foreach (DestinationState destination in orderedDestinations)
                {
                    int capacity = destination.GetCapacity(source.ResourceId);
                    if (capacity <= 0) continue;
                    int quantity = Math.Min(remaining, capacity);
                    destination.Consume(source.ResourceId, quantity);
                    moves.Add(new QuickStackMove(source.StackId, destination.EndpointId, source.ResourceId, quantity));
                    remaining -= quantity;
                    planned = AddSaturated(planned, quantity);
                    if (remaining == 0) break;
                }
            }

            return new QuickStackPlan(moves.AsReadOnly(), requested, planned);
        }

        private static int AddSaturated(int left, int right) =>
            left > int.MaxValue - right ? int.MaxValue : left + right;

        private sealed class DestinationState
        {
            private readonly Dictionary<string, int> _capacity;

            internal DestinationState(QuickStackDestination destination)
            {
                EndpointId = destination.EndpointId;
                DistanceSquared = destination.DistanceSquared;
                _capacity = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, int> pair in destination.CapacityByExistingResource)
                    _capacity.Add(pair.Key, pair.Value);
            }

            internal string EndpointId { get; }
            internal float DistanceSquared { get; }
            internal int GetCapacity(string resourceId) => _capacity.TryGetValue(resourceId, out int value) ? value : 0;
            internal void Consume(string resourceId, int amount) => _capacity[resourceId] -= amount;
        }
    }
}

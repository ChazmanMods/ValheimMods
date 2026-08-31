using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace RunicTransactions.Contracts
{
    /// <summary>Engine-neutral world position used by optional container providers.</summary>
    public readonly struct WorldPosition : IEquatable<WorldPosition>
    {
        public WorldPosition(float x, float y, float z)
        {
            if (!IsFinite(x)) throw new ArgumentOutOfRangeException(nameof(x));
            if (!IsFinite(y)) throw new ArgumentOutOfRangeException(nameof(y));
            if (!IsFinite(z)) throw new ArgumentOutOfRangeException(nameof(z));
            X = x;
            Y = y;
            Z = z;
        }

        public float X { get; }
        public float Y { get; }
        public float Z { get; }

        public float DistanceSquaredTo(WorldPosition other)
        {
            double x = (double)X - other.X;
            double y = (double)Y - other.Y;
            double z = (double)Z - other.Z;
            double squared = x * x + y * y + z * z;
            return squared >= float.MaxValue ? float.MaxValue : (float)squared;
        }

        public bool Equals(WorldPosition other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        public override bool Equals(object obj) => obj is WorldPosition other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = X.GetHashCode();
                hash = (hash * 397) ^ Y.GetHashCode();
                return (hash * 397) ^ Z.GetHashCode();
            }
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public sealed class ContainerResourceSnapshot
    {
        public ContainerResourceSnapshot(ResourceId resourceId, int quantity)
        {
            if (!resourceId.IsValid) throw new ArgumentException("A resource ID is required.", nameof(resourceId));
            if (quantity < 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            ResourceId = resourceId;
            Quantity = quantity;
        }

        public ResourceId ResourceId { get; }
        public int Quantity { get; }
    }

    /// <summary>
    /// Authorized, minimal container information. Providers must not create this
    /// object for a container whose existence or contents the principal may not discover.
    /// </summary>
    public sealed class ContainerEndpointSnapshot
    {
        private readonly ReadOnlyCollection<ContainerResourceSnapshot> _resources;

        public ContainerEndpointSnapshot(
            EndpointId endpointId,
            long version,
            WorldPosition position,
            string typeId,
            bool isPersonal,
            IEnumerable<ContainerResourceSnapshot> resources)
        {
            if (!endpointId.IsValid) throw new ArgumentException("An endpoint ID is required.", nameof(endpointId));
            if (version < 0) throw new ArgumentOutOfRangeException(nameof(version));
            EndpointId = endpointId;
            Version = version;
            Position = position;
            TypeId = StableIdentifier.Require(typeId, nameof(typeId), 160);
            IsPersonal = isPersonal;

            var copy = new List<ContainerResourceSnapshot>();
            var seen = new HashSet<ResourceId>();
            if (resources != null)
            {
                foreach (ContainerResourceSnapshot resource in resources)
                {
                    if (resource == null) throw new ArgumentException("Resource snapshots cannot contain null.", nameof(resources));
                    if (!seen.Add(resource.ResourceId))
                        throw new ArgumentException("Duplicate resource ID in endpoint snapshot.", nameof(resources));
                    copy.Add(resource);
                }
            }
            if (copy.Count > ContainerQueryPolicy.AbsoluteMaximumResourceKindsPerEndpoint)
                throw new ArgumentOutOfRangeException(nameof(resources));
            copy.Sort((left, right) => left.ResourceId.CompareTo(right.ResourceId));
            _resources = copy.AsReadOnly();
        }

        public EndpointId EndpointId { get; }
        public long Version { get; }
        public WorldPosition Position { get; }
        public string TypeId { get; }
        public bool IsPersonal { get; }
        public IReadOnlyList<ContainerResourceSnapshot> Resources => _resources;
    }

    public sealed class ContainerQueryRequest
    {
        private readonly ReadOnlyCollection<ResourceId> _resourceFilter;

        public ContainerQueryRequest(
            PrincipalId principalId,
            string purposeId,
            WorldPosition origin,
            ContainerQueryPolicy policy,
            IEnumerable<ResourceId> resourceFilter = null,
            bool includePersonalContainers = false,
            bool requireWritable = false)
        {
            if (!principalId.IsValid) throw new ArgumentException("A principal ID is required.", nameof(principalId));
            PrincipalId = principalId;
            PurposeId = StableIdentifier.Require(purposeId, nameof(purposeId), 160);
            Origin = origin;
            Policy = policy ?? throw new ArgumentNullException(nameof(policy));
            IncludePersonalContainers = includePersonalContainers;
            RequireWritable = requireWritable;

            var copy = new List<ResourceId>();
            var seen = new HashSet<ResourceId>();
            if (resourceFilter != null)
            {
                foreach (ResourceId resource in resourceFilter)
                {
                    if (!resource.IsValid) throw new ArgumentException("Resource filters must be valid.", nameof(resourceFilter));
                    if (seen.Add(resource)) copy.Add(resource);
                }
            }
            if (copy.Count > ContainerQueryPolicy.AbsoluteMaximumResourceKindsPerEndpoint)
                throw new ArgumentOutOfRangeException(nameof(resourceFilter));
            copy.Sort();
            _resourceFilter = copy.AsReadOnly();
        }

        public PrincipalId PrincipalId { get; }
        public string PurposeId { get; }
        public WorldPosition Origin { get; }
        public ContainerQueryPolicy Policy { get; }
        public IReadOnlyList<ResourceId> ResourceFilter => _resourceFilter;
        public bool IncludePersonalContainers { get; }
        public bool RequireWritable { get; }
    }

    public enum ContainerQueryStatus
    {
        Succeeded = 1,
        Denied = 2,
        InvalidRequest = 3,
        ServerAuthorityRequired = 4,
        ProviderUnavailable = 5,
        Failed = 6
    }

    public sealed class ContainerQueryResult
    {
        private readonly ReadOnlyCollection<ContainerEndpointSnapshot> _endpoints;

        public ContainerQueryResult(
            ContainerQueryStatus status,
            IEnumerable<ContainerEndpointSnapshot> endpoints,
            int candidateCount,
            bool truncated,
            string reasonCode)
        {
            if (!Enum.IsDefined(typeof(ContainerQueryStatus), status))
                throw new ArgumentOutOfRangeException(nameof(status));
            if (candidateCount < 0 || candidateCount > ContainerQueryPolicy.AbsoluteMaximumCandidateEndpoints)
                throw new ArgumentOutOfRangeException(nameof(candidateCount));
            Status = status;
            CandidateCount = candidateCount;
            Truncated = truncated;
            ReasonCode = StableIdentifier.Require(reasonCode, nameof(reasonCode), 160);

            var copy = new List<ContainerEndpointSnapshot>();
            var seen = new HashSet<EndpointId>();
            if (endpoints != null)
            {
                foreach (ContainerEndpointSnapshot endpoint in endpoints)
                {
                    if (endpoint == null) throw new ArgumentException("Endpoints cannot contain null.", nameof(endpoints));
                    if (!seen.Add(endpoint.EndpointId))
                        throw new ArgumentException("Duplicate endpoint ID in query result.", nameof(endpoints));
                    copy.Add(endpoint);
                }
            }
            if (copy.Count > ContainerQueryPolicy.AbsoluteMaximumReturnedEndpoints)
                throw new ArgumentOutOfRangeException(nameof(endpoints));
            if (status != ContainerQueryStatus.Succeeded && copy.Count != 0)
                throw new ArgumentException("A failed query cannot disclose endpoint data.", nameof(endpoints));
            copy.Sort((left, right) => left.EndpointId.CompareTo(right.EndpointId));
            _endpoints = copy.AsReadOnly();
        }

        public ContainerQueryStatus Status { get; }
        public IReadOnlyList<ContainerEndpointSnapshot> Endpoints => _endpoints;
        public int CandidateCount { get; }
        public bool Truncated { get; }
        public string ReasonCode { get; }
        public bool Succeeded => Status == ContainerQueryStatus.Succeeded;
    }

    public interface IContainerQueryService
    {
        ContainerQueryResult Query(ContainerQueryRequest request);
    }

    public sealed class ContainerTransferLeg
    {
        public ContainerTransferLeg(EndpointId source, EndpointId destination, ResourceId resourceId, int quantity)
        {
            if (!source.IsValid) throw new ArgumentException("A source endpoint is required.", nameof(source));
            if (!destination.IsValid) throw new ArgumentException("A destination endpoint is required.", nameof(destination));
            if (source == destination) throw new ArgumentException("Source and destination must differ.", nameof(destination));
            if (!resourceId.IsValid) throw new ArgumentException("A resource ID is required.", nameof(resourceId));
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            Source = source;
            Destination = destination;
            ResourceId = resourceId;
            Quantity = quantity;
        }

        public EndpointId Source { get; }
        public EndpointId Destination { get; }
        public ResourceId ResourceId { get; }
        public int Quantity { get; }
    }

    public sealed class ContainerTransferRequest
    {
        public const int AbsoluteMaximumLegs = 256;
        private readonly ReadOnlyCollection<ContainerTransferLeg> _legs;

        public ContainerTransferRequest(
            TransactionId transactionId,
            CorrelationId correlationId,
            IdempotencyKey idempotencyKey,
            PrincipalId principalId,
            string purposeId,
            IEnumerable<ContainerTransferLeg> legs,
            bool requireAtomic = true)
        {
            if (!transactionId.IsValid) throw new ArgumentException("A transaction ID is required.", nameof(transactionId));
            if (!correlationId.IsValid) throw new ArgumentException("A correlation ID is required.", nameof(correlationId));
            if (!idempotencyKey.IsValid) throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
            if (!principalId.IsValid) throw new ArgumentException("A principal ID is required.", nameof(principalId));
            TransactionId = transactionId;
            CorrelationId = correlationId;
            IdempotencyKey = idempotencyKey;
            PrincipalId = principalId;
            PurposeId = StableIdentifier.Require(purposeId, nameof(purposeId), 160);
            RequireAtomic = requireAtomic;

            var copy = new List<ContainerTransferLeg>();
            long totalQuantity = 0;
            if (legs != null)
            {
                foreach (ContainerTransferLeg leg in legs)
                {
                    if (leg == null) throw new ArgumentException("Transfer legs cannot contain null.", nameof(legs));
                    copy.Add(leg);
                    totalQuantity += leg.Quantity;
                    if (totalQuantity > int.MaxValue)
                        throw new ArgumentOutOfRangeException(nameof(legs), "Aggregate transfer quantity is too large.");
                }
            }
            if (copy.Count == 0 || copy.Count > AbsoluteMaximumLegs)
                throw new ArgumentOutOfRangeException(nameof(legs));
            _legs = copy.AsReadOnly();
        }

        public TransactionId TransactionId { get; }
        public CorrelationId CorrelationId { get; }
        public IdempotencyKey IdempotencyKey { get; }
        public PrincipalId PrincipalId { get; }
        public string PurposeId { get; }
        public IReadOnlyList<ContainerTransferLeg> Legs => _legs;
        public bool RequireAtomic { get; }
    }

    public enum ContainerTransferStatus
    {
        Succeeded = 1,
        PartiallySucceeded = 2,
        Denied = 3,
        InvalidRequest = 4,
        Conflict = 5,
        InsufficientSource = 6,
        InsufficientDestinationCapacity = 7,
        ServerAuthorityRequired = 8,
        ProviderUnavailable = 9,
        Failed = 10
    }

    public sealed class ContainerTransferResult
    {
        public ContainerTransferResult(
            ContainerTransferStatus status,
            int requestedQuantity,
            int movedQuantity,
            string reasonCode)
        {
            if (!Enum.IsDefined(typeof(ContainerTransferStatus), status))
                throw new ArgumentOutOfRangeException(nameof(status));
            if (requestedQuantity < 0) throw new ArgumentOutOfRangeException(nameof(requestedQuantity));
            if (movedQuantity < 0 || movedQuantity > requestedQuantity)
                throw new ArgumentOutOfRangeException(nameof(movedQuantity));
            if (status == ContainerTransferStatus.Succeeded && movedQuantity != requestedQuantity)
                throw new ArgumentException("A successful transfer must move the full request.", nameof(movedQuantity));
            Status = status;
            RequestedQuantity = requestedQuantity;
            MovedQuantity = movedQuantity;
            ReasonCode = StableIdentifier.Require(reasonCode, nameof(reasonCode), 160);
        }

        public ContainerTransferStatus Status { get; }
        public int RequestedQuantity { get; }
        public int MovedQuantity { get; }
        public int RemainingQuantity => RequestedQuantity - MovedQuantity;
        public string ReasonCode { get; }
        public bool Succeeded => Status == ContainerTransferStatus.Succeeded;
    }

    public interface IContainerTransferService
    {
        ContainerTransferResult Transfer(ContainerTransferRequest request);
    }
}

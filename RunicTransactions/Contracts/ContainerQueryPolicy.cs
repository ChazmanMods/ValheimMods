using System;

namespace RunicTransactions.Contracts
{
    /// <summary>
    /// Hard bounds a container discovery request before any world scan occurs. Callers may choose
    /// smaller values, but cannot use this contract to request an unbounded scan.
    /// </summary>
    public sealed class ContainerQueryPolicy
    {
        public const float AbsoluteMaximumRadiusMeters = 50f;
        public const int AbsoluteMaximumCandidateEndpoints = 256;
        public const int AbsoluteMaximumReturnedEndpoints = 128;
        public const int AbsoluteMaximumResourceKindsPerEndpoint = 256;

        public ContainerQueryPolicy(
            float radiusMeters,
            int maximumCandidateEndpoints,
            int maximumReturnedEndpoints,
            int maximumResourceKindsPerEndpoint,
            bool requireLineOfSight,
            bool requireAuthorization)
        {
            if (float.IsNaN(radiusMeters) || float.IsInfinity(radiusMeters) ||
                radiusMeters <= 0f || radiusMeters > AbsoluteMaximumRadiusMeters)
                throw new ArgumentOutOfRangeException(nameof(radiusMeters));
            if (maximumCandidateEndpoints <= 0 || maximumCandidateEndpoints > AbsoluteMaximumCandidateEndpoints)
                throw new ArgumentOutOfRangeException(nameof(maximumCandidateEndpoints));
            if (maximumReturnedEndpoints <= 0 || maximumReturnedEndpoints > AbsoluteMaximumReturnedEndpoints ||
                maximumReturnedEndpoints > maximumCandidateEndpoints)
                throw new ArgumentOutOfRangeException(nameof(maximumReturnedEndpoints));
            if (maximumResourceKindsPerEndpoint <= 0 ||
                maximumResourceKindsPerEndpoint > AbsoluteMaximumResourceKindsPerEndpoint)
                throw new ArgumentOutOfRangeException(nameof(maximumResourceKindsPerEndpoint));

            RadiusMeters = radiusMeters;
            MaximumCandidateEndpoints = maximumCandidateEndpoints;
            MaximumReturnedEndpoints = maximumReturnedEndpoints;
            MaximumResourceKindsPerEndpoint = maximumResourceKindsPerEndpoint;
            RequireLineOfSight = requireLineOfSight;
            RequireAuthorization = requireAuthorization;
        }

        public float RadiusMeters { get; }
        public int MaximumCandidateEndpoints { get; }
        public int MaximumReturnedEndpoints { get; }
        public int MaximumResourceKindsPerEndpoint { get; }
        public bool RequireLineOfSight { get; }
        public bool RequireAuthorization { get; }

        public static ContainerQueryPolicy ConservativeDefault => new ContainerQueryPolicy(
            radiusMeters: 10f,
            maximumCandidateEndpoints: 64,
            maximumReturnedEndpoints: 32,
            maximumResourceKindsPerEndpoint: 128,
            requireLineOfSight: true,
            requireAuthorization: true);
    }
}

using System;

namespace RunicPortals.Api
{
    public enum PortalMutationKind
    {
        ConfigureMetadata = 1,
        CommitTravel = 2
    }

    public enum AuthorityStopCode
    {
        Allowed = 0,
        FeatureDisabled = 1,
        ServerAuthorityMissing = 2,
        DedicatedTransportUnavailable = 3,
        SenderIdentityUnbound = 4,
        ActorNotLocalAuthority = 5,
        SourceMissing = 6,
        DestinationMissing = 7,
        SourceObjectNotOwned = 8,
        PlayerObjectNotOwned = 9,
        OwnerPermissionDenied = 10,
        SourceWardDenied = 11,
        DestinationWardDenied = 12,
        ActorOutOfRange = 13,
        CurrentStateChanged = 14,
        VanillaTravelPolicyDenied = 15,
        UnsupportedMutation = 16
    }

    public sealed class PortalAuthorityEvidence
    {
        public PortalAuthorityEvidence(
            PortalMutationKind mutation,
            bool featureEnabled,
            bool isServer,
            bool isDedicated,
            bool dedicatedTransportAvailable,
            bool senderIdentityBound,
            bool actorIsLocalAuthority,
            bool sourceExists,
            bool destinationExists,
            bool sourceObjectOwned,
            bool playerObjectOwned,
            bool ownerPermissionAllowed,
            bool sourceWardAllowed,
            bool destinationWardAllowed,
            bool actorInRange,
            bool currentStateMatches,
            bool vanillaTravelPolicyAllowed)
        {
            if (!Enum.IsDefined(typeof(PortalMutationKind), mutation))
                throw new ArgumentOutOfRangeException(nameof(mutation));
            Mutation = mutation;
            FeatureEnabled = featureEnabled;
            IsServer = isServer;
            IsDedicated = isDedicated;
            DedicatedTransportAvailable = dedicatedTransportAvailable;
            SenderIdentityBound = senderIdentityBound;
            ActorIsLocalAuthority = actorIsLocalAuthority;
            SourceExists = sourceExists;
            DestinationExists = destinationExists;
            SourceObjectOwned = sourceObjectOwned;
            PlayerObjectOwned = playerObjectOwned;
            OwnerPermissionAllowed = ownerPermissionAllowed;
            SourceWardAllowed = sourceWardAllowed;
            DestinationWardAllowed = destinationWardAllowed;
            ActorInRange = actorInRange;
            CurrentStateMatches = currentStateMatches;
            VanillaTravelPolicyAllowed = vanillaTravelPolicyAllowed;
        }

        public PortalMutationKind Mutation { get; }
        public bool FeatureEnabled { get; }
        public bool IsServer { get; }
        public bool IsDedicated { get; }
        public bool DedicatedTransportAvailable { get; }
        public bool SenderIdentityBound { get; }
        public bool ActorIsLocalAuthority { get; }
        public bool SourceExists { get; }
        public bool DestinationExists { get; }
        public bool SourceObjectOwned { get; }
        public bool PlayerObjectOwned { get; }
        public bool OwnerPermissionAllowed { get; }
        public bool SourceWardAllowed { get; }
        public bool DestinationWardAllowed { get; }
        public bool ActorInRange { get; }
        public bool CurrentStateMatches { get; }
        public bool VanillaTravelPolicyAllowed { get; }
    }

    public sealed class PortalAuthorityDecision
    {
        internal PortalAuthorityDecision(AuthorityStopCode stopCode)
        {
            StopCode = stopCode;
        }

        public AuthorityStopCode StopCode { get; }
        public bool IsAllowed => StopCode == AuthorityStopCode.Allowed;
    }

    public interface IPortalAuthorityGate
    {
        PortalAuthorityDecision Evaluate(PortalAuthorityEvidence evidence);
    }
}

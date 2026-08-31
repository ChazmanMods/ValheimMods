using System;
using RunicPortals.Api;

namespace RunicPortals.Core
{
    internal sealed class PortalAuthorityGate : IPortalAuthorityGate
    {
        public PortalAuthorityDecision Evaluate(PortalAuthorityEvidence evidence)
        {
            if (evidence == null) throw new ArgumentNullException(nameof(evidence));
            if (!evidence.FeatureEnabled) return Stop(AuthorityStopCode.FeatureDisabled);
            if (!evidence.SenderIdentityBound) return Stop(AuthorityStopCode.SenderIdentityUnbound);
            if (!evidence.ActorIsLocalAuthority) return Stop(AuthorityStopCode.ActorNotLocalAuthority);
            if (!evidence.SourceExists) return Stop(AuthorityStopCode.SourceMissing);
            if (evidence.Mutation == PortalMutationKind.CommitTravel && !evidence.DestinationExists)
                return Stop(AuthorityStopCode.DestinationMissing);
            if (evidence.Mutation == PortalMutationKind.ConfigureMetadata && !evidence.SourceObjectOwned)
                return Stop(AuthorityStopCode.SourceObjectNotOwned);
            if (evidence.Mutation == PortalMutationKind.CommitTravel && !evidence.PlayerObjectOwned)
                return Stop(AuthorityStopCode.PlayerObjectNotOwned);
            if (!evidence.OwnerPermissionAllowed) return Stop(AuthorityStopCode.OwnerPermissionDenied);
            if (!evidence.SourceWardAllowed) return Stop(AuthorityStopCode.SourceWardDenied);
            if (evidence.Mutation == PortalMutationKind.CommitTravel && !evidence.DestinationWardAllowed)
                return Stop(AuthorityStopCode.DestinationWardDenied);
            if (!evidence.ActorInRange) return Stop(AuthorityStopCode.ActorOutOfRange);
            if (!evidence.CurrentStateMatches) return Stop(AuthorityStopCode.CurrentStateChanged);
            if (evidence.Mutation == PortalMutationKind.CommitTravel &&
                !evidence.VanillaTravelPolicyAllowed)
                return Stop(AuthorityStopCode.VanillaTravelPolicyDenied);
            if (evidence.Mutation != PortalMutationKind.ConfigureMetadata &&
                evidence.Mutation != PortalMutationKind.CommitTravel)
                return Stop(AuthorityStopCode.UnsupportedMutation);
            return Stop(AuthorityStopCode.Allowed);
        }

        private static PortalAuthorityDecision Stop(AuthorityStopCode code) =>
            new PortalAuthorityDecision(code);
    }
}

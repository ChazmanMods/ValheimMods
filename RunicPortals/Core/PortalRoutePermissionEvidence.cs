using System;
using RunicPortals.Api;

namespace RunicPortals.Core
{
    /// <summary>
    /// Keeps portal policy authorization independent from native ward evidence. A caller must
    /// pass all three values to the authority gate; a ward or stale-state failure must never be
    /// mislabeled as an owner/private/Group policy denial.
    /// </summary>
    internal sealed class PortalRoutePermissionEvidence
    {
        private PortalRoutePermissionEvidence(
            bool policyAllowed,
            bool sourceWardAllowed,
            bool destinationWardAllowed)
        {
            PolicyAllowed = policyAllowed;
            SourceWardAllowed = sourceWardAllowed;
            DestinationWardAllowed = destinationWardAllowed;
        }

        internal bool PolicyAllowed { get; }
        internal bool SourceWardAllowed { get; }
        internal bool DestinationWardAllowed { get; }

        internal static PortalRoutePermissionEvidence Evaluate(
            IPortalAccessEvaluator permissions,
            PortalEndpoint source,
            PortalEndpoint destination,
            string travelerStableId,
            bool sourceWardAllowed,
            bool destinationWardAllowed)
        {
            if (permissions == null) throw new ArgumentNullException(nameof(permissions));
            bool policyAllowed = source != null && destination != null &&
                                 permissions.Allows(
                                     source, travelerStableId, PortalAccessAction.Depart) &&
                                 permissions.Allows(
                                     destination, travelerStableId, PortalAccessAction.Arrive);
            return new PortalRoutePermissionEvidence(
                policyAllowed,
                sourceWardAllowed,
                destinationWardAllowed);
        }
    }
}

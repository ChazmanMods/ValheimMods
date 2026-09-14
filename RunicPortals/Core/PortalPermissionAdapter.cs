using System;
using System.Globalization;
using RunicPermissions.Contracts;
using RunicPortals.Api;
using RunicPortals.Integration;

namespace RunicPortals.Core
{
    internal interface IPortalGroupMembershipResolver
    {
        bool TryIsMember(string groupId, long playerId, out bool isMember);
    }

    internal sealed class PortalPermissionAdapter : IPortalAccessEvaluator
    {
        private readonly IPortalGroupMembershipResolver _groups;

        internal PortalPermissionAdapter(IPortalGroupMembershipResolver groups)
        {
            _groups = groups;
        }

        public bool Allows(
            PortalEndpoint endpoint,
            string travelerStableId,
            PortalAccessAction action)
        {
            if (endpoint == null || string.IsNullOrEmpty(travelerStableId))
                return false;
            PortalAccessPolicy policy = endpoint.Access.GetPolicy(action);
            switch (policy.Kind)
            {
                case PortalPolicyKind.Everyone:
                case PortalPolicyKind.Ward:
                    return true;
                case PortalPolicyKind.Approved:
                case PortalPolicyKind.WardWithExceptions:
                    return policy.IsExplicitlyApproved(travelerStableId);
                case PortalPolicyKind.Owner:
                    return string.Equals(
                        endpoint.OwnerStableId, travelerStableId, StringComparison.Ordinal);
                case PortalPolicyKind.Group:
                    return TryPlayerId(travelerStableId, out long playerId) &&
                           _groups != null &&
                           _groups.TryIsMember(policy.GroupId, playerId, out bool member) && member;
                default:
                    return false;
            }
        }

        internal static string Identity(long playerId) =>
            "valheim.player:" + playerId.ToString(CultureInfo.InvariantCulture);

        internal static bool TryPlayerId(string canonical, out long playerId)
        {
            playerId = 0L;
            const string prefix = "valheim.player:";
            if (canonical == null || !canonical.StartsWith(prefix, StringComparison.Ordinal))
                return false;
            string value = canonical.Substring(prefix.Length);
            // Valheim generates signed character IDs. Zero alone means uninitialized.
            return long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out playerId) &&
                   playerId != 0L && string.Equals(
                       value, playerId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }

        internal static bool TryParseIdentity(string canonical, out StableIdentity identity)
        {
            identity = null;
            if (!TryPlayerId(canonical, out long playerId)) return false;
            return StableIdentity.TryCreate(
                "valheim.player",
                playerId.ToString(CultureInfo.InvariantCulture),
                out identity);
        }
    }
}

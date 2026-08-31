using System;
using System.Collections.Generic;
using System.Linq;

namespace RunicCrafting.Domain
{
    public enum WorkshopPolicyKind
    {
        Everyone = 1,
        Approved = 2,
        Owner = 3,
        Nobody = 4,
        Ward = 5,
        WardWithExceptions = 6,
        Group = 7
    }

    public enum WorkshopAction
    {
        StationUse = 1,
        LocalMaterialUse = 2
    }

    public sealed class WorkshopAccessProfile
    {
        private readonly HashSet<string> _approved;

        public WorkshopAccessProfile(
            string ownerId,
            WorkshopPolicyKind stationUse,
            WorkshopPolicyKind localMaterialUse,
            IEnumerable<string> approvedIds = null)
        {
            OwnerId = (ownerId ?? string.Empty).Trim();
            StationUse = stationUse;
            LocalMaterialUse = localMaterialUse;
            _approved = new HashSet<string>(
                (approvedIds ?? Array.Empty<string>()).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()),
                StringComparer.Ordinal);
        }

        public string OwnerId { get; }
        public WorkshopPolicyKind StationUse { get; }
        public WorkshopPolicyKind LocalMaterialUse { get; }
        public IReadOnlyCollection<string> ApprovedIds => _approved;
        public bool IsApproved(string subjectId) => subjectId != null && _approved.Contains(subjectId);
    }

    public sealed class WorkshopAccessContext
    {
        public WorkshopAccessContext(bool wardAllows, bool groupProviderAvailable, bool groupMember)
        {
            WardAllows = wardAllows;
            GroupProviderAvailable = groupProviderAvailable;
            GroupMember = groupMember;
        }

        public bool WardAllows { get; }
        public bool GroupProviderAvailable { get; }
        public bool GroupMember { get; }
    }

    public sealed class WorkshopAccessDecision
    {
        public WorkshopAccessDecision(bool allowed, WorkshopAction action, string reasonCode)
        {
            Allowed = allowed;
            Action = action;
            ReasonCode = string.IsNullOrWhiteSpace(reasonCode) ? "denied" : reasonCode;
        }

        public bool Allowed { get; }
        public WorkshopAction Action { get; }
        public string ReasonCode { get; }
    }

    public sealed class WorkshopAccessEvaluator
    {
        public WorkshopAccessDecision Evaluate(
            WorkshopAccessProfile profile,
            WorkshopAction action,
            string subjectId,
            WorkshopAccessContext context)
        {
            if (profile == null || string.IsNullOrWhiteSpace(subjectId) || context == null)
                return new WorkshopAccessDecision(false, action, "invalid-context");

            WorkshopPolicyKind policy = action == WorkshopAction.StationUse
                ? profile.StationUse
                : profile.LocalMaterialUse;
            bool owner = string.Equals(profile.OwnerId, subjectId, StringComparison.Ordinal);
            bool approved = owner || profile.IsApproved(subjectId);
            switch (policy)
            {
                case WorkshopPolicyKind.Everyone:
                    return new WorkshopAccessDecision(true, action, "everyone");
                case WorkshopPolicyKind.Approved:
                    return new WorkshopAccessDecision(approved, action, approved ? "approved" : "not-approved");
                case WorkshopPolicyKind.Owner:
                    return new WorkshopAccessDecision(owner, action, owner ? "owner" : "not-owner");
                case WorkshopPolicyKind.Nobody:
                    return new WorkshopAccessDecision(false, action, "nobody");
                case WorkshopPolicyKind.Ward:
                    return new WorkshopAccessDecision(context.WardAllows, action, context.WardAllows ? "ward" : "ward-denied");
                case WorkshopPolicyKind.WardWithExceptions:
                    return new WorkshopAccessDecision(approved || context.WardAllows, action,
                        approved ? "approved-exception" : context.WardAllows ? "ward" : "ward-denied");
                case WorkshopPolicyKind.Group:
                    bool groupAllowed = context.GroupProviderAvailable && context.GroupMember;
                    return new WorkshopAccessDecision(groupAllowed, action,
                        !context.GroupProviderAvailable ? "group-provider-missing" : groupAllowed ? "group" : "not-group-member");
                default:
                    return new WorkshopAccessDecision(false, action, "unknown-policy");
            }
        }
    }
}

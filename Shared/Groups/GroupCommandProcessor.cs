using System;
using System.Linq;
using RunicPermissions.Contracts;

namespace RunicPermissions.Groups
{
    public sealed class GroupCommandExecutionResult
    {
        internal GroupCommandExecutionResult(
            GroupMutationCode code,
            string reasonCode,
            GroupCatalog catalog,
            GroupRecord group)
        {
            Code = code;
            ReasonCode = reasonCode ?? string.Empty;
            Catalog = catalog;
            Group = group;
        }

        public GroupMutationCode Code { get; }
        public string ReasonCode { get; }
        public GroupCatalog Catalog { get; }
        public GroupRecord Group { get; }
        public bool Success => Code >= GroupMutationCode.Created && Code <= GroupMutationCode.NoChange;
    }

    /// <summary>
    /// Synchronous server-side command processor. One catalog primary is the only irreversible
    /// state; CAS publication completes before a success response is returned.
    /// </summary>
    public sealed class GroupCommandProcessor
    {
        private readonly IGroupWorldStore _store;
        private readonly Func<string> _worldScopeProvider;
        private readonly Func<long> _utcTicksProvider;

        public GroupCommandProcessor(
            IGroupWorldStore store,
            Func<string> worldScopeProvider,
            Func<long> utcTicksProvider = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _worldScopeProvider = worldScopeProvider ??
                                  throw new ArgumentNullException(nameof(worldScopeProvider));
            _utcTicksProvider = utcTicksProvider ?? (() => DateTime.UtcNow.Ticks);
        }

        public GroupCommandExecutionResult Execute(StableIdentity actor, GroupCommand command)
            => Execute(actor, command, 0);

        public GroupCommandExecutionResult Execute(StableIdentity actor, GroupCommand command, long expectedInvitationRevision)
        {
            if (actor == null || command == null)
                return Fail(GroupMutationCode.InvalidRequest, "group-command-invalid", null);
            string scope;
            try { scope = _worldScopeProvider() ?? string.Empty; }
            catch { return Fail(GroupMutationCode.RevisionConflict, "group-world-unavailable", null); }
            if (scope.Length == 0)
                return Fail(GroupMutationCode.RevisionConflict, "group-world-unavailable", null);

            GroupWorldReadResult read;
            try { read = _store.Read(scope); }
            catch { return Fail(GroupMutationCode.RevisionConflict, "group-store-unavailable", null); }
            if (read == null || read.State == GroupWorldReadState.Corrupt ||
                read.State == GroupWorldReadState.EvidenceConflict)
                return Fail(GroupMutationCode.RevisionConflict, "group-store-evidence-conflict", read?.Catalog);
            if (read.State == GroupWorldReadState.Unavailable)
                return Fail(GroupMutationCode.RevisionConflict, "group-store-unavailable", read.Catalog);

            GroupCatalog current = read.State == GroupWorldReadState.Missing
                ? GroupCatalog.Empty
                : read.Catalog;
            if (current == null)
                return Fail(GroupMutationCode.RevisionConflict, "group-store-invalid", null);

            if (expectedInvitationRevision != 0 &&
                (expectedInvitationRevision < 1 ||
                 command.Kind != GroupCommandKind.Accept && command.Kind != GroupCommandKind.Decline ||
                 !current.TryGetGroup(command.GroupId, out GroupRecord invitedGroup) ||
                 !invitedGroup.TryGetInvitation(actor, out GroupInvitation invitation) ||
                 invitation.IssuedRevision != expectedInvitationRevision))
                return Fail(GroupMutationCode.RevisionConflict, "group-invitation-changed", current);

            GroupCommandExecutionResult replay = TryExactDesiredReplay(current, actor, command);
            if (replay != null) return replay;

            GroupMutationResult mutation;
            if (command.Kind == GroupCommandKind.Create)
            {
                mutation = current.Create(current.Revision, command.GroupId, command.DisplayName, actor);
            }
            else
            {
                if (!current.TryGetGroup(command.GroupId, out GroupRecord group))
                    return Fail(GroupMutationCode.GroupMissing, "group-missing", current);
                long now = _utcTicksProvider();
                mutation = Apply(current, group, actor, command, now);
            }

            if (!mutation.Success)
                return From(mutation);
            if (mutation.Code == GroupMutationCode.NoChange ||
                ReferenceEquals(mutation.Catalog, current))
                return From(mutation);

            GroupWorldCommitResult commit;
            try { commit = _store.TryCommit(scope, current.Revision, mutation.Catalog); }
            catch { return Fail(GroupMutationCode.RevisionConflict, "group-store-commit-failed", current); }
            if (commit == null || !commit.Success)
                return Fail(
                    GroupMutationCode.RevisionConflict,
                    commit?.ReasonCode ?? "group-store-commit-failed",
                    commit?.Current?.Catalog ?? current);
            return From(mutation);
        }

        internal static GroupCommandExecutionResult EvaluateIssued(
            GroupCatalog current,
            StableIdentity actor,
            GroupCommand command,
            long expectedCatalogRevision,
            long expectedGroupRevision,
            long nowUtcTicks)
        {
            if (current == null || actor == null || command == null || nowUtcTicks <= 0)
                return Fail(GroupMutationCode.InvalidRequest, "group-command-invalid", current);
            if (current.Revision != expectedCatalogRevision)
                return Fail(
                    GroupMutationCode.RevisionConflict,
                    "group-catalog-revision-conflict",
                    current);
            GroupMutationResult mutation;
            if (command.Kind == GroupCommandKind.Create)
            {
                if (expectedGroupRevision != -1 || current.TryGetGroup(command.GroupId, out _))
                    return Fail(
                        GroupMutationCode.RevisionConflict,
                        "group-create-revision-conflict",
                        current);
                mutation = current.Create(
                    expectedCatalogRevision, command.GroupId, command.DisplayName, actor);
            }
            else
            {
                if (expectedGroupRevision < 1 ||
                    !current.TryGetGroup(command.GroupId, out GroupRecord group) ||
                    group.Revision != expectedGroupRevision)
                    return Fail(
                        GroupMutationCode.RevisionConflict,
                        "group-revision-conflict",
                        current);
                mutation = Apply(current, group, actor, command, nowUtcTicks);
            }
            return From(mutation);
        }

        private static GroupMutationResult Apply(
            GroupCatalog catalog,
            GroupRecord group,
            StableIdentity actor,
            GroupCommand command,
            long now)
        {
            switch (command.Kind)
            {
                case GroupCommandKind.Rename:
                    return catalog.Rename(catalog.Revision, group.Id, group.Revision, actor, command.DisplayName);
                case GroupCommandKind.Invite:
                    return catalog.Invite(
                        catalog.Revision, group.Id, group.Revision, actor, command.Target,
                        now, command.InvitationExpiresUtcTicks);
                case GroupCommandKind.CancelInvitation:
                    return catalog.CancelInvitation(
                        catalog.Revision, group.Id, group.Revision, actor, command.Target);
                case GroupCommandKind.Accept:
                    return catalog.Accept(catalog.Revision, group.Id, group.Revision, actor, now);
                case GroupCommandKind.Decline:
                    return catalog.Decline(catalog.Revision, group.Id, group.Revision, actor, now);
                case GroupCommandKind.Leave:
                    return catalog.Leave(catalog.Revision, group.Id, group.Revision, actor);
                case GroupCommandKind.Remove:
                    return catalog.Remove(catalog.Revision, group.Id, group.Revision, actor, command.Target);
                case GroupCommandKind.SetRole:
                    return catalog.SetRole(
                        catalog.Revision, group.Id, group.Revision, actor, command.Target, command.Role);
                case GroupCommandKind.TransferOwnership:
                    return catalog.TransferOwnership(
                        catalog.Revision, group.Id, group.Revision, actor, command.Target);
                case GroupCommandKind.Delete:
                    return catalog.Delete(catalog.Revision, group.Id, group.Revision, actor);
                default:
                    return new GroupMutationResult(
                        GroupMutationCode.InvalidRequest,
                        "group-command-kind-invalid",
                        catalog,
                        group);
            }
        }

        private static GroupCommandExecutionResult TryExactDesiredReplay(
            GroupCatalog catalog,
            StableIdentity actor,
            GroupCommand command)
        {
            if (command.Kind == GroupCommandKind.Create &&
                catalog.TryGetGroup(command.GroupId, out GroupRecord created) &&
                string.Equals(created.DisplayName, command.DisplayName, StringComparison.Ordinal) &&
                created.TryGetMember(actor, out GroupMember owner) && owner.Role == GroupRole.Owner)
                return NoChange("group-create-already-applied", catalog, created);

            if (command.Kind == GroupCommandKind.Delete &&
                catalog.RetiredGroupIds.Any(value => value.Id == command.GroupId))
                return NoChange("group-delete-already-applied", catalog, null);

            if (!catalog.TryGetGroup(command.GroupId, out GroupRecord group)) return null;
            if (command.Kind == GroupCommandKind.Accept && group.TryGetMember(actor, out _))
                return NoChange("group-accept-already-applied", catalog, group);
            if (command.Kind == GroupCommandKind.Leave && !group.TryGetMember(actor, out _))
                return NoChange("group-leave-already-applied", catalog, group);
            if (command.Kind == GroupCommandKind.TransferOwnership &&
                command.Target != null && group.TryGetMember(command.Target, out GroupMember target) &&
                target.Role == GroupRole.Owner && group.TryGetMember(actor, out _))
                return NoChange("group-transfer-already-applied", catalog, group);
            return null;
        }

        private static GroupCommandExecutionResult From(GroupMutationResult result) =>
            new GroupCommandExecutionResult(result.Code, result.ReasonCode, result.Catalog, result.Group);

        private static GroupCommandExecutionResult NoChange(
            string reason,
            GroupCatalog catalog,
            GroupRecord group) => new GroupCommandExecutionResult(
                GroupMutationCode.NoChange, reason, catalog, group);

        private static GroupCommandExecutionResult Fail(
            GroupMutationCode code,
            string reason,
            GroupCatalog catalog) => new GroupCommandExecutionResult(code, reason, catalog, null);
    }
}

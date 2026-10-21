using System;
using System.IO;
using System.Linq;
using RunicPermissions.Contracts;
using RunicPermissions.Groups;

namespace RunicPermissions.Tests
{
    internal static class GroupCommandTests
    {
        private const string World = "valheim.0000000000000001";
        private static readonly Guid Id =
            Guid.ParseExact("33333333333333333333333333333333", "N");
        private static readonly StableIdentity Owner = Identity("owner");
        private static readonly StableIdentity Member = Identity("member");

        internal static void Register()
        {
            TestRunner.Run("group command codec round trips every command shape", RoundTripsCommands);
            TestRunner.Run("group command codec rejects trailing and malformed payloads", RejectsMalformedWire);
            TestRunner.Run("group processor commits create invite and accept", CommitsLifecycle);
            TestRunner.Run("group processor replay is idempotent across sessions", ReplaysDesiredState);
            TestRunner.Run("group processor fails closed on store conflict", DeniesStoreConflict);
            TestRunner.Run("durable Group command envelope round trips canonically", DurableEnvelopeRoundTrip);
            TestRunner.Run("durable Group command receipt replays after store reload", DurableReplayAfterReload);
            TestRunner.Run("durable Group prepare replays its exact issued token after reload", DurablePrepareReplayAfterReload);
            TestRunner.Run("durable Group command token rejects a different request", DurableTokenConflict);
            TestRunner.Run("durable Group command stale revision cannot overwrite later state", DurableStaleRevision);
            TestRunner.Run("durable Group command expired issue is terminally denied", DurableExpiry);
            TestRunner.Run("ordinary Group commit cannot erase durable command evidence", OrdinaryCommitCannotEraseLedger);
            TestRunner.Run("durable Group receipt compaction rejects the exact old frontier", DurableReceiptFrontier);
        }

        private static void RoundTripsCommands()
        {
            long expiry = DateTime.UtcNow.AddDays(1).Ticks;
            GroupCommand[] commands =
            {
                new GroupCommand(GroupCommandKind.Create, Id, "Builders"),
                new GroupCommand(GroupCommandKind.Rename, Id, "Builders II"),
                new GroupCommand(GroupCommandKind.Invite, Id, target: Member, invitationExpiresUtcTicks: expiry),
                new GroupCommand(GroupCommandKind.CancelInvitation, Id, target: Member),
                new GroupCommand(GroupCommandKind.Accept, Id),
                new GroupCommand(GroupCommandKind.Leave, Id),
                new GroupCommand(GroupCommandKind.Remove, Id, target: Member),
                new GroupCommand(GroupCommandKind.SetRole, Id, target: Member, role: GroupRole.Officer),
                new GroupCommand(GroupCommandKind.TransferOwnership, Id, target: Member),
                new GroupCommand(GroupCommandKind.Delete, Id)
            };
            foreach (GroupCommand expected in commands)
            {
                byte[] payload = GroupCommandCodec.Encode(expected);
                TestAssert.True(GroupCommandCodec.TryDecode(payload, out GroupCommand actual, out string reason), reason);
                TestAssert.Equal(expected.Kind, actual.Kind);
                TestAssert.Equal(expected.GroupId, actual.GroupId);
                TestAssert.Equal(expected.DisplayName, actual.DisplayName);
                TestAssert.Equal(expected.Target?.CanonicalKey, actual.Target?.CanonicalKey);
                TestAssert.Equal(expected.Role, actual.Role);
                TestAssert.Equal(expected.InvitationExpiresUtcTicks, actual.InvitationExpiresUtcTicks);
            }
        }

        private static void RejectsMalformedWire()
        {
            byte[] exact = GroupCommandCodec.Encode(
                new GroupCommand(GroupCommandKind.Create, Id, "Builders"));
            byte[] trailing = exact.Concat(new byte[] { 0 }).ToArray();
            TestAssert.False(GroupCommandCodec.TryDecode(trailing, out _, out _));
            for (int length = 0; length < exact.Length; length++)
                TestAssert.False(GroupCommandCodec.TryDecode(exact.Take(length).ToArray(), out _, out _));
            byte[] invalidRole = (byte[])exact.Clone();
            invalidRole[invalidRole.Length - 9] = 99;
            TestAssert.False(GroupCommandCodec.TryDecode(invalidRole, out _, out _));
        }

        private static void CommitsLifecycle()
        {
            var store = new MemoryStore();
            long now = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
            var processor = new GroupCommandProcessor(store, () => World, () => now);

            GroupCommandExecutionResult created = processor.Execute(
                Owner, new GroupCommand(GroupCommandKind.Create, Id, "Builders"));
            TestAssert.Equal(GroupMutationCode.Created, created.Code);

            GroupCommandExecutionResult invited = processor.Execute(
                Owner,
                new GroupCommand(
                    GroupCommandKind.Invite,
                    Id,
                    target: Member,
                    invitationExpiresUtcTicks: now + TimeSpan.FromDays(1).Ticks));
            TestAssert.Equal(GroupMutationCode.Invited, invited.Code);

            GroupCommandExecutionResult accepted = processor.Execute(
                Member, new GroupCommand(GroupCommandKind.Accept, Id));
            TestAssert.Equal(GroupMutationCode.Accepted, accepted.Code);
            TestAssert.True(store.Catalog.TryGetGroup(Id, out GroupRecord group));
            TestAssert.True(group.TryGetMember(Member, out _));
        }

        private static void ReplaysDesiredState()
        {
            var store = new MemoryStore();
            var processor = new GroupCommandProcessor(store, () => World, () => DateTime.UtcNow.Ticks);
            var create = new GroupCommand(GroupCommandKind.Create, Id, "Builders");
            TestAssert.True(processor.Execute(Owner, create).Success);
            TestAssert.Equal(GroupMutationCode.NoChange, processor.Execute(Owner, create).Code);

            GroupCommand delete = new GroupCommand(GroupCommandKind.Delete, Id);
            TestAssert.True(processor.Execute(Owner, delete).Success);
            TestAssert.Equal(GroupMutationCode.NoChange, processor.Execute(Owner, delete).Code);
        }

        private static void DeniesStoreConflict()
        {
            var store = new MemoryStore { Conflict = true };
            var processor = new GroupCommandProcessor(store, () => World);
            GroupCommandExecutionResult result = processor.Execute(
                Owner, new GroupCommand(GroupCommandKind.Create, Id, "Builders"));
            TestAssert.False(result.Success);
            TestAssert.Equal(GroupMutationCode.RevisionConflict, result.Code);
        }

        private static void DurableEnvelopeRoundTrip()
        {
            string token = GroupCommandToken.Format(
                Guid.ParseExact("44444444444444444444444444444444", "N"), 7L);
            var issue = new GroupCommandIssueResponse(token, 12L, 3L);
            byte[] issueBytes = GroupCommandDurableCodec.EncodeIssue(issue);
            TestAssert.True(GroupCommandDurableCodec.TryDecodeIssue(
                issueBytes, out GroupCommandIssueResponse decodedIssue, out string issueReason), issueReason);
            TestAssert.Equal(token, decodedIssue.Token);
            TestAssert.Equal(12L, decodedIssue.ExpectedCatalogRevision);
            TestAssert.Equal(3L, decodedIssue.ExpectedGroupRevision);

            var execution = new GroupCommandExecution(
                token,
                12L,
                3L,
                new GroupCommand(GroupCommandKind.Rename, Id, "Builders II"));
            byte[] exact = GroupCommandDurableCodec.EncodeExecution(execution);
            TestAssert.True(GroupCommandDurableCodec.TryDecodeExecution(
                exact, out GroupCommandExecution decoded, out string reason), reason);
            TestAssert.Equal(token, decoded.Token);
            TestAssert.Equal("Builders II", decoded.Command.DisplayName);
            TestAssert.False(GroupCommandDurableCodec.TryDecodeExecution(
                exact.Concat(new byte[] { 0 }).ToArray(), out _, out _));
        }

        private static void DurableReplayAfterReload()
        {
            WithFileStore((store, root) =>
            {
                long now = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
                var command = new GroupCommand(GroupCommandKind.Create, Id, "Builders");
                TestAssert.True(store.TryIssueCommand(
                    World, Owner, command, now, out GroupCommandIssue issue, out string issueReason),
                    issueReason);
                var execution = new GroupCommandExecution(
                    issue.Token,
                    issue.ExpectedCatalogRevision,
                    issue.ExpectedGroupRevision,
                    command);
                TestAssert.True(store.TryExecuteIssuedCommand(
                    World, Owner, execution, now + 1,
                    out GroupCommandReceipt first, out string executeReason), executeReason);
                TestAssert.Equal(GroupMutationCode.Created, first.Code);

                var reloaded = new FileGroupWorldStore(root);
                TestAssert.True(reloaded.TryExecuteIssuedCommand(
                    World, Owner, execution, now + 2,
                    out GroupCommandReceipt replay, out string replayReason), replayReason);
                TestAssert.Equal(first.Token, replay.Token);
                TestAssert.Equal(first.Code, replay.Code);
                TestAssert.Equal(1L, reloaded.Read(World).Catalog.Revision);
            });
        }

        private static void DurablePrepareReplayAfterReload()
        {
            WithFileStore((store, root) =>
            {
                long now = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
                var command = new GroupCommand(GroupCommandKind.Create, Id, "Builders");
                TestAssert.True(store.TryIssueCommand(
                    World, Owner, command, now, out GroupCommandIssue first, out string firstReason),
                    firstReason);
                string firstHash = store.Read(World).ExactSha256;

                var reloaded = new FileGroupWorldStore(root);
                TestAssert.True(reloaded.TryIssueCommand(
                    World, Owner, command, now + 1, out GroupCommandIssue replay, out string replayReason),
                    replayReason);
                TestAssert.Equal(first.Token, replay.Token);
                GroupWorldReadResult current = reloaded.Read(World);
                TestAssert.Equal(firstHash, current.ExactSha256);
                TestAssert.Equal(1L, current.Catalog.CommandLedger.NextSequence);
                TestAssert.Equal(1, current.Catalog.CommandLedger.Issues.Count);
            });
        }

        private static void DurableTokenConflict()
        {
            WithFileStore((store, ignoredRoot) =>
            {
                long now = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
                var create = new GroupCommand(GroupCommandKind.Create, Id, "Builders");
                TestAssert.True(store.TryIssueCommand(
                    World, Owner, create, now, out GroupCommandIssue issue, out _));
                var changed = new GroupCommandExecution(
                    issue.Token,
                    issue.ExpectedCatalogRevision,
                    issue.ExpectedGroupRevision,
                    new GroupCommand(GroupCommandKind.Create, Id, "Raiders"));
                TestAssert.False(store.TryExecuteIssuedCommand(
                    World, Owner, changed, now + 1, out _, out string reason));
                TestAssert.Equal("group-command-token-conflict", reason);
                TestAssert.Equal(0L, store.Read(World).Catalog.Revision);
            });
        }

        private static void DurableStaleRevision()
        {
            WithFileStore((store, ignoredRoot) =>
            {
                long now = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
                GroupMutationResult created = GroupCatalog.Empty.Create(0, Id, "Builders", Owner);
                TestAssert.True(store.TryCommit(World, 0, created.Catalog).Success);
                var stale = new GroupCommand(GroupCommandKind.Rename, Id, "Old Request");
                TestAssert.True(store.TryIssueCommand(
                    World, Owner, stale, now, out GroupCommandIssue issue, out _));

                GroupWorldReadResult current = store.Read(World);
                GroupRecord group = current.Catalog.Groups[0];
                GroupMutationResult later = current.Catalog.Rename(
                    current.Catalog.Revision, Id, group.Revision, Owner, "Newer State");
                TestAssert.True(store.TryCommit(
                    World, current.Catalog.Revision, later.Catalog).Success);

                var execution = new GroupCommandExecution(
                    issue.Token,
                    issue.ExpectedCatalogRevision,
                    issue.ExpectedGroupRevision,
                    stale);
                TestAssert.True(store.TryExecuteIssuedCommand(
                    World, Owner, execution, now + 1,
                    out GroupCommandReceipt receipt, out string reason), reason);
                TestAssert.Equal(GroupMutationCode.RevisionConflict, receipt.Code);
                TestAssert.Equal("Newer State", store.Read(World).Catalog.Groups[0].DisplayName);
            });
        }

        private static void DurableExpiry()
        {
            WithFileStore((store, ignoredRoot) =>
            {
                long now = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
                var command = new GroupCommand(GroupCommandKind.Create, Id, "Builders");
                TestAssert.True(store.TryIssueCommand(
                    World, Owner, command, now, out GroupCommandIssue issue, out _));
                var execution = new GroupCommandExecution(
                    issue.Token,
                    issue.ExpectedCatalogRevision,
                    issue.ExpectedGroupRevision,
                    command);
                TestAssert.True(store.TryExecuteIssuedCommand(
                    World,
                    Owner,
                    execution,
                    now + GroupCommandDurabilityLimits.MaximumIssueLifetime.Ticks + 1,
                    out GroupCommandReceipt receipt,
                    out string reason), reason);
                TestAssert.Equal(GroupMutationCode.RevisionConflict, receipt.Code);
                TestAssert.Equal("group-command-token-expired", receipt.ReasonCode);
                TestAssert.Equal(0L, store.Read(World).Catalog.Revision);
            });
        }

        private static void OrdinaryCommitCannotEraseLedger()
        {
            WithFileStore((store, ignoredRoot) =>
            {
                long now = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
                var command = new GroupCommand(GroupCommandKind.Create, Id, "Builders");
                TestAssert.True(store.TryIssueCommand(
                    World, Owner, command, now, out GroupCommandIssue issue, out string issueReason),
                    issueReason);

                GroupWorldCommitResult attempted = store.TryCommit(
                    World,
                    0,
                    new GroupCatalog(1));
                TestAssert.Equal(GroupWorldCommitState.InvalidReplacement, attempted.State);
                TestAssert.Equal("group-store-command-ledger-mismatch", attempted.ReasonCode);

                GroupWorldReadResult retained = store.Read(World);
                TestAssert.Equal(0L, retained.Catalog.Revision);
                TestAssert.True(retained.Catalog.CommandLedger.TryGetIssue(issue.Token, out _));
            });
        }

        private static void DurableReceiptFrontier()
        {
            Guid epoch = Guid.ParseExact("55555555555555555555555555555555", "N");
            string hash = new string('a', 64);
            var receipts = Enumerable.Range(
                    1,
                    GroupCommandDurabilityLimits.MaximumRetainedReceipts)
                .Select(sequence => new GroupCommandReceipt(
                    epoch,
                    sequence,
                    Owner,
                    hash,
                    GroupMutationCode.NoChange,
                    "group-command-no-change",
                    0,
                    -1,
                    0,
                    -1))
                .ToArray();
            long newestSequence = GroupCommandDurabilityLimits.MaximumRetainedReceipts + 1L;
            var issue = new GroupCommandIssue(
                epoch,
                newestSequence,
                Owner,
                hash,
                Id,
                0,
                -1,
                DateTime.UtcNow.AddMinutes(1).Ticks);
            var ledger = new GroupCommandLedger(
                epoch,
                newestSequence,
                0,
                new[] { issue },
                receipts);

            GroupCommandLedger completed = ledger.Complete(
                issue,
                GroupMutationCode.NoChange,
                "group-command-no-change",
                0,
                -1,
                out GroupCommandReceipt newest);
            TestAssert.Equal(1L, completed.MinimumAcceptedSequence);
            TestAssert.Equal(
                GroupCommandDurabilityLimits.MaximumRetainedReceipts,
                completed.Receipts.Count);
            TestAssert.False(completed.TryGetReceipt(GroupCommandToken.Format(epoch, 1), out _));
            TestAssert.True(completed.TryGetReceipt(newest.Token, out GroupCommandReceipt replay));
            TestAssert.Equal(newest.Sequence, replay.Sequence);
        }

        private static void WithFileStore(Action<FileGroupWorldStore, string> test)
        {
            string root = Path.Combine(
                Path.GetTempPath(), "RunicPermissionsGroupCommandTests", Guid.NewGuid().ToString("N"));
            try { test(new FileGroupWorldStore(root), root); }
            finally
            {
                try { if (Directory.Exists(root)) Directory.Delete(root, true); }
                catch { }
            }
        }

        private static StableIdentity Identity(string subject) => new StableIdentity("steam", subject);

        private sealed class MemoryStore : IGroupWorldStore
        {
            internal GroupCatalog Catalog { get; private set; } = GroupCatalog.Empty;
            internal bool Conflict { get; set; }

            public GroupWorldReadResult Read(string worldScope) => new GroupWorldReadResult(
                Catalog.Revision == 0 ? GroupWorldReadState.Missing : GroupWorldReadState.Ready,
                "ok",
                Catalog,
                "hash");

            public GroupWorldCommitResult TryCommit(
                string worldScope,
                long expectedCatalogRevision,
                GroupCatalog replacement)
            {
                if (Conflict || Catalog.Revision != expectedCatalogRevision)
                    return new GroupWorldCommitResult(
                        GroupWorldCommitState.RevisionConflict,
                        "group-store-revision-conflict",
                        Read(worldScope));
                Catalog = replacement;
                return new GroupWorldCommitResult(
                    GroupWorldCommitState.Committed,
                    "ok",
                    Read(worldScope));
            }
        }
    }
}

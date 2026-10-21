using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using RunicPermissions.Contracts;

namespace RunicPermissions.Groups
{
    internal static class GroupCommandDurabilityLimits
    {
        internal const int MaximumOutstandingIssues = 256;
        internal const int MaximumRetainedReceipts = 4096;
        internal const int MaximumReasonCharacters = 96;
        internal static readonly TimeSpan MaximumIssueLifetime = TimeSpan.FromMinutes(10);
    }

    internal sealed class GroupCommandIssue
    {
        internal GroupCommandIssue(
            Guid epoch,
            long sequence,
            StableIdentity actor,
            string requestSha256,
            Guid groupId,
            long expectedCatalogRevision,
            long expectedGroupRevision,
            long expiresUtcTicks)
        {
            if (epoch == Guid.Empty || sequence < 1 || actor == null || groupId == Guid.Empty ||
                expectedCatalogRevision < 0 || expectedGroupRevision < -1 || expiresUtcTicks <= 0)
                throw new ArgumentException("The Group command issue is invalid.");
            Epoch = epoch;
            Sequence = sequence;
            Actor = actor;
            RequestSha256 = RequireSha256(requestSha256);
            GroupId = groupId;
            ExpectedCatalogRevision = expectedCatalogRevision;
            ExpectedGroupRevision = expectedGroupRevision;
            ExpiresUtcTicks = expiresUtcTicks;
        }

        internal Guid Epoch { get; }
        internal long Sequence { get; }
        internal StableIdentity Actor { get; }
        internal string RequestSha256 { get; }
        internal Guid GroupId { get; }
        internal long ExpectedCatalogRevision { get; }
        internal long ExpectedGroupRevision { get; }
        internal long ExpiresUtcTicks { get; }
        internal string Token => GroupCommandToken.Format(Epoch, Sequence);

        internal bool Matches(StableIdentity actor, string requestSha256) =>
            actor != null && Actor.Equals(actor) &&
            string.Equals(RequestSha256, requestSha256, StringComparison.Ordinal);

        internal static string RequireSha256(string value)
        {
            if (value == null || value.Length != 64) throw new ArgumentException("SHA-256 is invalid.");
            for (int index = 0; index < value.Length; index++)
            {
                char current = value[index];
                if (!(current >= '0' && current <= '9' || current >= 'a' && current <= 'f'))
                    throw new ArgumentException("SHA-256 is not canonical lowercase hexadecimal.");
            }
            return value;
        }
    }

    internal sealed class GroupCommandReceipt
    {
        internal GroupCommandReceipt(
            Guid epoch,
            long sequence,
            StableIdentity actor,
            string requestSha256,
            GroupMutationCode code,
            string reasonCode,
            long expectedCatalogRevision,
            long expectedGroupRevision,
            long catalogRevision,
            long groupRevision)
        {
            if (epoch == Guid.Empty || sequence < 1 || actor == null ||
                !Enum.IsDefined(typeof(GroupMutationCode), code) ||
                expectedCatalogRevision < 0 || expectedGroupRevision < -1 ||
                catalogRevision < 0 || groupRevision < -1)
                throw new ArgumentException("The Group command receipt is invalid.");
            string reason = reasonCode ?? string.Empty;
            if (reason.Length < 1 || reason.Length > GroupCommandDurabilityLimits.MaximumReasonCharacters)
                throw new ArgumentOutOfRangeException(nameof(reasonCode));
            for (int index = 0; index < reason.Length; index++)
            {
                char current = reason[index];
                bool valid = current >= 'a' && current <= 'z' ||
                             current >= '0' && current <= '9' ||
                             current == '.' || current == '-' || current == '_';
                if (!valid) throw new ArgumentException("The Group receipt reason is not canonical.");
            }
            Epoch = epoch;
            Sequence = sequence;
            Actor = actor;
            RequestSha256 = GroupCommandIssue.RequireSha256(requestSha256);
            Code = code;
            ReasonCode = reason;
            ExpectedCatalogRevision = expectedCatalogRevision;
            ExpectedGroupRevision = expectedGroupRevision;
            CatalogRevision = catalogRevision;
            GroupRevision = groupRevision;
        }

        internal Guid Epoch { get; }
        internal long Sequence { get; }
        internal StableIdentity Actor { get; }
        internal string RequestSha256 { get; }
        internal GroupMutationCode Code { get; }
        internal string ReasonCode { get; }
        internal long ExpectedCatalogRevision { get; }
        internal long ExpectedGroupRevision { get; }
        internal long CatalogRevision { get; }
        internal long GroupRevision { get; }
        internal string Token => GroupCommandToken.Format(Epoch, Sequence);
        internal bool Success => Code >= GroupMutationCode.Created && Code <= GroupMutationCode.NoChange;

        internal bool Matches(StableIdentity actor, string requestSha256) =>
            actor != null && Actor.Equals(actor) &&
            string.Equals(RequestSha256, requestSha256, StringComparison.Ordinal);
    }

    internal static class GroupCommandToken
    {
        internal static string Format(Guid epoch, long sequence)
        {
            if (epoch == Guid.Empty || sequence < 1) throw new ArgumentException("Token identity is invalid.");
            return epoch.ToString("N") + ":" + sequence.ToString("x16", CultureInfo.InvariantCulture);
        }

        internal static bool TryParse(string value, out Guid epoch, out long sequence)
        {
            epoch = Guid.Empty;
            sequence = 0;
            if (value == null || value.Length != 49 || value[32] != ':' ||
                !Guid.TryParseExact(value.Substring(0, 32), "N", out epoch) || epoch == Guid.Empty ||
                !long.TryParse(value.Substring(33), NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture, out sequence) || sequence < 1)
            {
                epoch = Guid.Empty;
                sequence = 0;
                return false;
            }
            return string.Equals(value, Format(epoch, sequence), StringComparison.Ordinal);
        }
    }

    internal sealed class GroupCommandLedger
    {
        private readonly GroupCommandIssue[] _issues;
        private readonly GroupCommandReceipt[] _receipts;
        private readonly ReadOnlyCollection<GroupCommandIssue> _issueView;
        private readonly ReadOnlyCollection<GroupCommandReceipt> _receiptView;

        internal GroupCommandLedger(
            Guid epoch,
            long nextSequence,
            long minimumAcceptedSequence,
            IEnumerable<GroupCommandIssue> issues = null,
            IEnumerable<GroupCommandReceipt> receipts = null)
        {
            if (nextSequence < 0 || minimumAcceptedSequence < 0 ||
                minimumAcceptedSequence > nextSequence ||
                (epoch == Guid.Empty) != (nextSequence == 0))
                throw new ArgumentException("The Group command ledger frontier is invalid.");
            Epoch = epoch;
            NextSequence = nextSequence;
            MinimumAcceptedSequence = minimumAcceptedSequence;
            _issues = SortedIssues(issues, epoch, minimumAcceptedSequence, nextSequence);
            _receipts = SortedReceipts(receipts, epoch, minimumAcceptedSequence, nextSequence);
            if (_issues.Length > GroupCommandDurabilityLimits.MaximumOutstandingIssues ||
                _receipts.Length > GroupCommandDurabilityLimits.MaximumRetainedReceipts)
                throw new ArgumentOutOfRangeException(nameof(issues));
            int issueIndex = 0;
            int receiptIndex = 0;
            for (long sequence = minimumAcceptedSequence + 1; sequence <= nextSequence; sequence++)
            {
                bool issue = issueIndex < _issues.Length && _issues[issueIndex].Sequence == sequence;
                bool receipt = receiptIndex < _receipts.Length && _receipts[receiptIndex].Sequence == sequence;
                if (issue == receipt) throw new ArgumentException("The Group command ledger has a gap or duplicate.");
                if (issue) issueIndex++;
                else receiptIndex++;
            }
            _issueView = Array.AsReadOnly(_issues);
            _receiptView = Array.AsReadOnly(_receipts);
        }

        internal static GroupCommandLedger Empty { get; } =
            new GroupCommandLedger(Guid.Empty, 0, 0);

        internal Guid Epoch { get; }
        internal long NextSequence { get; }
        internal long MinimumAcceptedSequence { get; }
        internal IReadOnlyList<GroupCommandIssue> Issues => _issueView;
        internal IReadOnlyList<GroupCommandReceipt> Receipts => _receiptView;

        internal GroupCommandLedger Issue(
            StableIdentity actor,
            string requestSha256,
            Guid groupId,
            long expectedCatalogRevision,
            long expectedGroupRevision,
            long nowUtcTicks,
            long expiresUtcTicks,
            out GroupCommandIssue issue)
        {
            if (actor == null || nowUtcTicks <= 0 || expiresUtcTicks <= nowUtcTicks ||
                expiresUtcTicks - nowUtcTicks > GroupCommandDurabilityLimits.MaximumIssueLifetime.Ticks)
                throw new ArgumentException("The Group command issue lifetime is invalid.");
            GroupCommandLedger pruned = PruneExpired(nowUtcTicks);
            foreach (GroupCommandIssue current in pruned._issues)
            {
                if (current.Matches(actor, requestSha256) && current.GroupId == groupId &&
                    current.ExpectedCatalogRevision == expectedCatalogRevision &&
                    current.ExpectedGroupRevision == expectedGroupRevision)
                {
                    // Lost prepare responses and reconnect retries must recover the exact issued
                    // token, not consume another durable sequence or create competing commands.
                    issue = current;
                    return pruned;
                }
            }
            if (pruned._issues.Length >= GroupCommandDurabilityLimits.MaximumOutstandingIssues ||
                pruned.NextSequence == long.MaxValue)
                throw new InvalidOperationException("group-command-issue-capacity");
            Guid epoch = pruned.Epoch == Guid.Empty ? Guid.NewGuid() : pruned.Epoch;
            long sequence = checked(pruned.NextSequence + 1);
            issue = new GroupCommandIssue(
                epoch, sequence, actor, requestSha256, groupId,
                expectedCatalogRevision, expectedGroupRevision, expiresUtcTicks);
            var issues = new List<GroupCommandIssue>(pruned._issues) { issue };
            return new GroupCommandLedger(
                epoch, sequence, pruned.MinimumAcceptedSequence, issues, pruned._receipts);
        }

        internal GroupCommandLedger Complete(
            GroupCommandIssue issue,
            GroupMutationCode code,
            string reasonCode,
            long catalogRevision,
            long groupRevision,
            out GroupCommandReceipt receipt)
        {
            if (issue == null || issue.Epoch != Epoch ||
                !TryGetIssue(issue.Token, out GroupCommandIssue exact) ||
                !ReferenceEquals(exact, issue) && !SameIssue(exact, issue))
                throw new ArgumentException("The Group command issue is not current.", nameof(issue));
            receipt = new GroupCommandReceipt(
                Epoch, issue.Sequence, issue.Actor, issue.RequestSha256,
                code, reasonCode,
                issue.ExpectedCatalogRevision, issue.ExpectedGroupRevision,
                catalogRevision, groupRevision);
            var issues = _issues.Where(value => value.Sequence != issue.Sequence).ToList();
            var receipts = new List<GroupCommandReceipt>(_receipts) { receipt };
            receipts.Sort((left, right) => left.Sequence.CompareTo(right.Sequence));
            return Compact(Epoch, NextSequence, MinimumAcceptedSequence, issues, receipts);
        }

        internal GroupCommandLedger PruneExpired(long nowUtcTicks)
        {
            if (nowUtcTicks <= 0) throw new ArgumentOutOfRangeException(nameof(nowUtcTicks));
            var issues = new List<GroupCommandIssue>(_issues.Length);
            var receipts = new List<GroupCommandReceipt>(_receipts);
            bool changed = false;
            foreach (GroupCommandIssue issue in _issues)
            {
                if (issue.ExpiresUtcTicks >= nowUtcTicks)
                {
                    issues.Add(issue);
                    continue;
                }
                receipts.Add(new GroupCommandReceipt(
                    Epoch, issue.Sequence, issue.Actor, issue.RequestSha256,
                    GroupMutationCode.RevisionConflict, "group-command-token-expired",
                    issue.ExpectedCatalogRevision, issue.ExpectedGroupRevision,
                    issue.ExpectedCatalogRevision, issue.ExpectedGroupRevision));
                changed = true;
            }
            if (!changed) return this;
            receipts.Sort((left, right) => left.Sequence.CompareTo(right.Sequence));
            return Compact(Epoch, NextSequence, MinimumAcceptedSequence, issues, receipts);
        }

        internal bool TryGetIssue(string token, out GroupCommandIssue issue)
        {
            issue = null;
            if (!GroupCommandToken.TryParse(token, out Guid epoch, out long sequence) ||
                epoch != Epoch || sequence <= MinimumAcceptedSequence || sequence > NextSequence)
                return false;
            // Active issues are hard-bounded at 256, so the simple scan is deterministic,
            // allocation-free, and avoids framework-specific comparer behavior.
            for (int current = 0; current < _issues.Length; current++)
                if (_issues[current].Sequence == sequence) { issue = _issues[current]; return true; }
            return false;
        }

        internal bool TryGetReceipt(string token, out GroupCommandReceipt receipt)
        {
            receipt = null;
            if (!GroupCommandToken.TryParse(token, out Guid epoch, out long sequence) ||
                epoch != Epoch || sequence <= MinimumAcceptedSequence || sequence > NextSequence)
                return false;
            for (int index = 0; index < _receipts.Length; index++)
                if (_receipts[index].Sequence == sequence) { receipt = _receipts[index]; return true; }
            return false;
        }

        private static GroupCommandLedger Compact(
            Guid epoch,
            long nextSequence,
            long minimum,
            List<GroupCommandIssue> issues,
            List<GroupCommandReceipt> receipts)
        {
            while (receipts.Count > GroupCommandDurabilityLimits.MaximumRetainedReceipts)
            {
                long next = minimum + 1;
                int receiptIndex = receipts.FindIndex(value => value.Sequence == next);
                if (receiptIndex < 0)
                    throw new InvalidOperationException("group-command-receipt-capacity");
                receipts.RemoveAt(receiptIndex);
                minimum = next;
            }
            return new GroupCommandLedger(epoch, nextSequence, minimum, issues, receipts);
        }

        private static GroupCommandIssue[] SortedIssues(
            IEnumerable<GroupCommandIssue> values,
            Guid epoch,
            long minimum,
            long maximum)
        {
            GroupCommandIssue[] result = (values ?? Array.Empty<GroupCommandIssue>())
                .OrderBy(value => value?.Sequence ?? 0).ToArray();
            long previous = minimum;
            foreach (GroupCommandIssue value in result)
            {
                if (value == null || value.Epoch != epoch || value.Sequence <= previous ||
                    value.Sequence > maximum)
                    throw new ArgumentException("The Group command issue set is invalid.");
                previous = value.Sequence;
            }
            return result;
        }

        private static GroupCommandReceipt[] SortedReceipts(
            IEnumerable<GroupCommandReceipt> values,
            Guid epoch,
            long minimum,
            long maximum)
        {
            GroupCommandReceipt[] result = (values ?? Array.Empty<GroupCommandReceipt>())
                .OrderBy(value => value?.Sequence ?? 0).ToArray();
            long previous = minimum;
            foreach (GroupCommandReceipt value in result)
            {
                if (value == null || value.Epoch != epoch || value.Sequence <= previous ||
                    value.Sequence > maximum)
                    throw new ArgumentException("The Group command receipt set is invalid.");
                previous = value.Sequence;
            }
            return result;
        }

        private static bool SameIssue(GroupCommandIssue left, GroupCommandIssue right) =>
            left != null && right != null && left.Epoch == right.Epoch &&
            left.Sequence == right.Sequence && left.Actor.Equals(right.Actor) &&
            string.Equals(left.RequestSha256, right.RequestSha256, StringComparison.Ordinal) &&
            left.GroupId == right.GroupId &&
            left.ExpectedCatalogRevision == right.ExpectedCatalogRevision &&
            left.ExpectedGroupRevision == right.ExpectedGroupRevision &&
            left.ExpiresUtcTicks == right.ExpiresUtcTicks;
    }
}

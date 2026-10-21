using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using RunicPermissions.Contracts;

namespace RunicPermissions.Groups
{
    internal static class GroupCatalogCodec
    {
        private const uint Magic = 0x47505247; // GRPG
        private const ushort SchemaVersion = 2;
        private const int DigestBytes = 32;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        internal static byte[] Encode(string worldScope, GroupCatalog catalog)
        {
            worldScope = GroupIdentity.RequireWorldScope(worldScope);
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            byte[] payload;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, StrictUtf8, true))
            {
                writer.Write(Magic);
                writer.Write(SchemaVersion);
                WriteText(writer, worldScope, GroupLimits.MaximumWorldScopeUtf8Bytes);
                writer.Write(catalog.Revision);
                writer.Write(catalog.Groups.Count);
                foreach (GroupRecord group in catalog.Groups)
                {
                    WriteText(writer, group.IdText, 32);
                    WriteText(writer, group.DisplayName, GroupLimits.MaximumDisplayNameUtf8Bytes);
                    writer.Write(group.Revision);
                    writer.Write(group.Members.Count);
                    foreach (GroupMember member in group.Members)
                    {
                        WriteIdentity(writer, member.Identity);
                        writer.Write((byte)member.Role);
                        writer.Write(member.JoinedRevision);
                    }
                    writer.Write(group.Invitations.Count);
                    foreach (GroupInvitation invitation in group.Invitations)
                    {
                        WriteIdentity(writer, invitation.Invitee);
                        WriteIdentity(writer, invitation.InvitedBy);
                        writer.Write(invitation.IssuedRevision);
                        writer.Write(invitation.ExpiresUtcTicks);
                    }
                }
                writer.Write(catalog.RetiredGroupIds.Count);
                foreach (RetiredGroupId retired in catalog.RetiredGroupIds)
                {
                    WriteText(writer, retired.IdText, 32);
                    writer.Write(retired.DeletedCatalogRevision);
                }
                WriteCommandLedger(writer, catalog.CommandLedger);
                writer.Flush();
                payload = stream.ToArray();
            }
            byte[] digest = Sha256(payload);
            if (payload.Length > GroupLimits.MaximumCatalogBytes - digest.Length)
                throw new InvalidOperationException("The group catalog exceeds its hard byte limit.");
            var result = new byte[payload.Length + digest.Length];
            Buffer.BlockCopy(payload, 0, result, 0, payload.Length);
            Buffer.BlockCopy(digest, 0, result, payload.Length, digest.Length);
            return result;
        }

        internal static bool TryDecode(
            byte[] bytes,
            string expectedWorldScope,
            out GroupCatalog catalog,
            out string reason)
        {
            catalog = null;
            reason = "group-catalog-corrupt";
            try
            {
                expectedWorldScope = GroupIdentity.RequireWorldScope(expectedWorldScope);
                if (bytes == null || bytes.Length < 4 + 2 + 4 + 1 + 8 + 4 + DigestBytes ||
                    bytes.Length > GroupLimits.MaximumCatalogBytes)
                    return false;
                int payloadLength = bytes.Length - DigestBytes;
                byte[] expectedDigest = Sha256(bytes, 0, payloadLength);
                if (!FixedEquals(expectedDigest, bytes, payloadLength))
                {
                    reason = "group-catalog-digest-mismatch";
                    return false;
                }

                using (var stream = new MemoryStream(bytes, 0, payloadLength, false, true))
                using (var reader = new BinaryReader(stream, StrictUtf8, true))
                {
                    if (reader.ReadUInt32() != Magic)
                    {
                        reason = "group-catalog-schema-unsupported";
                        return false;
                    }
                    ushort schema = reader.ReadUInt16();
                    if (schema != 1 && schema != SchemaVersion)
                    {
                        reason = "group-catalog-schema-unsupported";
                        return false;
                    }
                    string worldScope = ReadText(reader, GroupLimits.MaximumWorldScopeUtf8Bytes);
                    if (!string.Equals(worldScope, expectedWorldScope, StringComparison.Ordinal))
                    {
                        reason = "group-catalog-world-mismatch";
                        return false;
                    }
                    long revision = reader.ReadInt64();
                    int groupCount = ReadCount(reader, GroupLimits.MaximumGroups);
                    var groups = new List<GroupRecord>(groupCount);
                    for (int groupIndex = 0; groupIndex < groupCount; groupIndex++)
                    {
                        string idText = ReadText(reader, 32);
                        if (!GroupIdentity.TryParseCanonicalId(idText, out Guid id)) return false;
                        string displayName = ReadText(reader, GroupLimits.MaximumDisplayNameUtf8Bytes);
                        long groupRevision = reader.ReadInt64();
                        int memberCount = ReadCount(reader, GroupLimits.MaximumMembersPerGroup);
                        if (memberCount < 1) return false;
                        var members = new List<GroupMember>(memberCount);
                        for (int memberIndex = 0; memberIndex < memberCount; memberIndex++)
                            members.Add(new GroupMember(
                                ReadIdentity(reader),
                                (GroupRole)reader.ReadByte(),
                                reader.ReadInt64()));
                        int invitationCount = ReadCount(reader, GroupLimits.MaximumInvitationsPerGroup);
                        var invitations = new List<GroupInvitation>(invitationCount);
                        for (int invitationIndex = 0; invitationIndex < invitationCount; invitationIndex++)
                            invitations.Add(new GroupInvitation(
                                ReadIdentity(reader),
                                ReadIdentity(reader),
                                reader.ReadInt64(),
                                reader.ReadInt64()));
                        groups.Add(new GroupRecord(
                            id, displayName, groupRevision, members, invitations));
                    }
                    int retiredCount = ReadCount(reader, GroupLimits.MaximumRetiredGroupIds);
                    var retired = new List<RetiredGroupId>(retiredCount);
                    for (int index = 0; index < retiredCount; index++)
                    {
                        string idText = ReadText(reader, 32);
                        if (!GroupIdentity.TryParseCanonicalId(idText, out Guid id)) return false;
                        retired.Add(new RetiredGroupId(id, reader.ReadInt64()));
                    }
                    GroupCommandLedger ledger = schema == 1
                        ? GroupCommandLedger.Empty
                        : ReadCommandLedger(reader);
                    if (stream.Position != payloadLength)
                    {
                        reason = "group-catalog-trailing-data";
                        return false;
                    }
                    catalog = new GroupCatalog(revision, groups, retired, ledger);
                    byte[] canonical = schema == SchemaVersion
                        ? Encode(expectedWorldScope, catalog)
                        : null;
                    if (canonical != null && !ExactEquals(bytes, canonical))
                    {
                        catalog = null;
                        reason = "group-catalog-noncanonical";
                        return false;
                    }
                }
                reason = "group-catalog-ready";
                return true;
            }
            catch (Exception exception) when (
                exception is ArgumentException || exception is IOException ||
                exception is EndOfStreamException || exception is DecoderFallbackException ||
                exception is OverflowException)
            {
                catalog = null;
                return false;
            }
        }

        internal static string ComputeSha256(byte[] bytes)
        {
            byte[] digest = Sha256(bytes ?? Array.Empty<byte>());
            var text = new StringBuilder(digest.Length * 2);
            foreach (byte value in digest) text.Append(value.ToString("x2"));
            return text.ToString();
        }

        private static void WriteIdentity(BinaryWriter writer, StableIdentity identity)
        {
            if (identity == null) throw new ArgumentNullException(nameof(identity));
            WriteText(writer, identity.Authority, StableIdentity.MaximumAuthorityLength);
            WriteText(writer, identity.SubjectId, StableIdentity.MaximumSubjectIdLength * 4);
        }

        private static StableIdentity ReadIdentity(BinaryReader reader) => new StableIdentity(
            ReadText(reader, StableIdentity.MaximumAuthorityLength),
            ReadText(reader, StableIdentity.MaximumSubjectIdLength * 4));

        private static void WriteCommandLedger(BinaryWriter writer, GroupCommandLedger ledger)
        {
            ledger = ledger ?? GroupCommandLedger.Empty;
            writer.Write(ledger.Epoch.ToByteArray());
            writer.Write(ledger.NextSequence);
            writer.Write(ledger.MinimumAcceptedSequence);
            writer.Write(ledger.Issues.Count);
            foreach (GroupCommandIssue issue in ledger.Issues)
            {
                writer.Write(issue.Sequence);
                WriteIdentity(writer, issue.Actor);
                WriteText(writer, issue.RequestSha256, 64);
                WriteText(writer, GroupIdentity.ToCanonicalId(issue.GroupId), 32);
                writer.Write(issue.ExpectedCatalogRevision);
                writer.Write(issue.ExpectedGroupRevision);
                writer.Write(issue.ExpiresUtcTicks);
            }
            writer.Write(ledger.Receipts.Count);
            foreach (GroupCommandReceipt receipt in ledger.Receipts)
            {
                writer.Write(receipt.Sequence);
                WriteIdentity(writer, receipt.Actor);
                WriteText(writer, receipt.RequestSha256, 64);
                writer.Write((int)receipt.Code);
                WriteText(writer, receipt.ReasonCode,
                    GroupCommandDurabilityLimits.MaximumReasonCharacters);
                writer.Write(receipt.ExpectedCatalogRevision);
                writer.Write(receipt.ExpectedGroupRevision);
                writer.Write(receipt.CatalogRevision);
                writer.Write(receipt.GroupRevision);
            }
        }

        private static GroupCommandLedger ReadCommandLedger(BinaryReader reader)
        {
            byte[] epochBytes = reader.ReadBytes(16);
            if (epochBytes.Length != 16) throw new EndOfStreamException();
            var epoch = new Guid(epochBytes);
            long nextSequence = reader.ReadInt64();
            long minimumAccepted = reader.ReadInt64();
            int issueCount = ReadCount(
                reader, GroupCommandDurabilityLimits.MaximumOutstandingIssues);
            var issues = new List<GroupCommandIssue>(issueCount);
            for (int index = 0; index < issueCount; index++)
            {
                long sequence = reader.ReadInt64();
                StableIdentity actor = ReadIdentity(reader);
                string requestHash = ReadText(reader, 64);
                string groupText = ReadText(reader, 32);
                if (!GroupIdentity.TryParseCanonicalId(groupText, out Guid groupId))
                    throw new InvalidDataException();
                issues.Add(new GroupCommandIssue(
                    epoch,
                    sequence,
                    actor,
                    requestHash,
                    groupId,
                    reader.ReadInt64(),
                    reader.ReadInt64(),
                    reader.ReadInt64()));
            }
            int receiptCount = ReadCount(
                reader, GroupCommandDurabilityLimits.MaximumRetainedReceipts);
            var receipts = new List<GroupCommandReceipt>(receiptCount);
            for (int index = 0; index < receiptCount; index++)
            {
                receipts.Add(new GroupCommandReceipt(
                    epoch,
                    reader.ReadInt64(),
                    ReadIdentity(reader),
                    ReadText(reader, 64),
                    (GroupMutationCode)reader.ReadInt32(),
                    ReadText(reader, GroupCommandDurabilityLimits.MaximumReasonCharacters),
                    reader.ReadInt64(),
                    reader.ReadInt64(),
                    reader.ReadInt64(),
                    reader.ReadInt64()));
            }
            return new GroupCommandLedger(
                epoch, nextSequence, minimumAccepted, issues, receipts);
        }

        private static void WriteText(BinaryWriter writer, string value, int maximumBytes)
        {
            byte[] bytes = StrictUtf8.GetBytes(value ?? string.Empty);
            if (bytes.Length < 1 || bytes.Length > maximumBytes)
                throw new ArgumentOutOfRangeException(nameof(value));
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        private static string ReadText(BinaryReader reader, int maximumBytes)
        {
            int length = reader.ReadInt32();
            if (length < 1 || length > maximumBytes) throw new InvalidDataException();
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            return StrictUtf8.GetString(bytes);
        }

        private static int ReadCount(BinaryReader reader, int maximum)
        {
            int value = reader.ReadInt32();
            if (value < 0 || value > maximum) throw new InvalidDataException();
            return value;
        }

        private static byte[] Sha256(byte[] bytes) => Sha256(bytes, 0, bytes.Length);

        private static byte[] Sha256(byte[] bytes, int offset, int count)
        {
            using (SHA256 algorithm = SHA256.Create())
                return algorithm.ComputeHash(bytes, offset, count);
        }

        private static bool FixedEquals(byte[] expected, byte[] source, int offset)
        {
            if (expected.Length != source.Length - offset) return false;
            int difference = 0;
            for (int index = 0; index < expected.Length; index++)
                difference |= expected[index] ^ source[offset + index];
            return difference == 0;
        }

        private static bool ExactEquals(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            int difference = 0;
            for (int index = 0; index < left.Length; index++) difference |= left[index] ^ right[index];
            return difference == 0;
        }
    }
}

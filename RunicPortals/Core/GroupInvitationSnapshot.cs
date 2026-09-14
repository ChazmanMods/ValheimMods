using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using RunicPermissions.Contracts;
using RunicPermissions.Groups;

namespace RunicPortals.Core
{
    internal sealed class GroupInvitationChoice
    {
        internal GroupInvitationChoice(string groupId, string name, string inviter, long revision, long expires)
        {
            if (!GroupIdentity.IsCanonicalId(groupId) || revision < 1 || expires < 1)
                throw new ArgumentException("Invalid invitation identity.");
            GroupId = groupId;
            Name = GroupIdentity.RequireDisplayName(name);
            if (string.IsNullOrWhiteSpace(inviter) || Encoding.UTF8.GetByteCount(inviter) > 256 || inviter.Any(char.IsControl))
                throw new ArgumentException("Invalid inviter label.");
            Inviter = inviter;
            Revision = revision;
            Expires = expires;
        }
        internal string GroupId { get; }
        internal string Name { get; }
        internal string Inviter { get; }
        internal long Revision { get; }
        internal long Expires { get; }
        internal string Token => GroupId + ":" + Revision.ToString(CultureInfo.InvariantCulture);
    }

    internal static class GroupInvitationSnapshot
    {
        internal const int MaximumChoices = 16;
        private const string Prefix = "RunicInvites1:";
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

        internal static GroupInvitationChoice[] Select(GroupCatalog catalog, StableIdentity actor,
            long now, Func<StableIdentity, string> label)
        {
            if (catalog == null || actor == null || now <= 0) return Array.Empty<GroupInvitationChoice>();
            return catalog.Groups.Where(group => group.TryGetInvitation(actor, out GroupInvitation invite) && !invite.IsExpired(now))
                .OrderBy(group => group.Id).Take(MaximumChoices).Select(group =>
                {
                    group.TryGetInvitation(actor, out GroupInvitation invite);
                    return new GroupInvitationChoice(group.Id.ToString("N"), group.DisplayName,
                        label(invite.InvitedBy), invite.IssuedRevision, invite.ExpiresUtcTicks);
                }).ToArray();
        }

        internal static bool Matches(GroupInvitation invitation, string issuedRevision, long now) =>
            invitation != null && !invitation.IsExpired(now) &&
            (string.IsNullOrEmpty(issuedRevision) ||
             string.Equals(invitation.IssuedRevision.ToString(CultureInfo.InvariantCulture), issuedRevision, StringComparison.Ordinal));

        internal static string Encode(IReadOnlyList<GroupInvitationChoice> values)
        {
            if (values == null || values.Count > MaximumChoices) throw new ArgumentException("Invitation limit.");
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Utf8, true))
            {
                writer.Write(values.Count);
                foreach (var value in values)
                {
                    writer.Write(value.GroupId); writer.Write(value.Name); writer.Write(value.Inviter);
                    writer.Write(value.Revision); writer.Write(value.Expires);
                }
                writer.Flush();
                return Prefix + Convert.ToBase64String(stream.ToArray());
            }
        }

        internal static bool TryDecode(string text, out GroupInvitationChoice[] values)
        {
            values = Array.Empty<GroupInvitationChoice>();
            if (text == null || text.Length > 24000 || !text.StartsWith(Prefix, StringComparison.Ordinal)) return false;
            try
            {
                using (var stream = new MemoryStream(Convert.FromBase64String(text.Substring(Prefix.Length))))
                using (var reader = new BinaryReader(stream, Utf8))
                {
                    int count = reader.ReadInt32();
                    if (count < 0 || count > MaximumChoices) return false;
                    var result = new GroupInvitationChoice[count];
                    var ids = new HashSet<string>(StringComparer.Ordinal);
                    for (int index = 0; index < count; index++)
                    {
                        result[index] = new GroupInvitationChoice(reader.ReadString(), reader.ReadString(), reader.ReadString(), reader.ReadInt64(), reader.ReadInt64());
                        if (!ids.Add(result[index].GroupId)) return false;
                    }
                    if (stream.Position != stream.Length) return false;
                    values = result;
                    return true;
                }
            }
            catch { return false; }
        }
    }
}

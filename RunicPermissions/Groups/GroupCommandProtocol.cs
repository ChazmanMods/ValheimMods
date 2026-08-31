using System;
using System.IO;
using System.Linq;
using System.Text;
using RunicPermissions.Contracts;

namespace RunicPermissions.Groups
{
    public enum GroupCommandKind : byte
    {
        Create = 1,
        Rename = 2,
        Invite = 3,
        CancelInvitation = 4,
        Accept = 5,
        Leave = 6,
        Remove = 7,
        SetRole = 8,
        TransferOwnership = 9,
        Delete = 10
    }

    public sealed class GroupCommand
    {
        public GroupCommand(
            GroupCommandKind kind,
            Guid groupId,
            string displayName = "",
            StableIdentity target = null,
            GroupRole role = GroupRole.Member,
            long invitationExpiresUtcTicks = 0)
        {
            Kind = kind;
            GroupId = groupId;
            DisplayName = displayName ?? string.Empty;
            Target = target;
            Role = role;
            InvitationExpiresUtcTicks = invitationExpiresUtcTicks;
            Validate();
        }

        public GroupCommandKind Kind { get; }
        public Guid GroupId { get; }
        public string DisplayName { get; }
        public StableIdentity Target { get; }
        public GroupRole Role { get; }
        public long InvitationExpiresUtcTicks { get; }

        private void Validate()
        {
            if (!Enum.IsDefined(typeof(GroupCommandKind), Kind) || GroupId == Guid.Empty)
                throw new ArgumentException("The group command kind or ID is invalid.");
            if (!Enum.IsDefined(typeof(GroupRole), Role))
                throw new ArgumentOutOfRangeException(nameof(Role));

            bool needsName = Kind == GroupCommandKind.Create || Kind == GroupCommandKind.Rename;
            bool needsTarget = Kind == GroupCommandKind.Invite ||
                               Kind == GroupCommandKind.CancelInvitation ||
                               Kind == GroupCommandKind.Remove ||
                               Kind == GroupCommandKind.SetRole ||
                               Kind == GroupCommandKind.TransferOwnership;
            if (needsName)
                GroupIdentity.RequireDisplayName(DisplayName);
            else if (DisplayName.Length != 0)
                throw new ArgumentException("This group command does not accept a display name.");
            if (needsTarget != (Target != null))
                throw new ArgumentException("The group command target shape is invalid.");
            if (Kind == GroupCommandKind.SetRole)
            {
                if (Role != GroupRole.Member && Role != GroupRole.Officer)
                    throw new ArgumentException("SetRole accepts Member or Officer only.");
            }
            else if (Role != GroupRole.Member)
            {
                throw new ArgumentException("This group command does not accept a role.");
            }
            if (Kind == GroupCommandKind.Invite)
            {
                if (InvitationExpiresUtcTicks <= 0)
                    throw new ArgumentOutOfRangeException(nameof(InvitationExpiresUtcTicks));
            }
            else if (InvitationExpiresUtcTicks != 0)
            {
                throw new ArgumentException("This group command does not accept an expiry.");
            }
        }
    }

    public static class GroupCommandCodec
    {
        private const uint Magic = 0x43505247; // GRPC in little-endian bytes.
        private const byte Schema = 1;
        public const int MaximumPayloadBytes = 1024;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public static byte[] Encode(GroupCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, StrictUtf8, true))
            {
                writer.Write(Magic);
                writer.Write(Schema);
                writer.Write((byte)command.Kind);
                writer.Write(command.GroupId.ToByteArray());
                WriteText(writer, command.DisplayName, GroupLimits.MaximumDisplayNameUtf8Bytes);
                WriteText(writer, command.Target?.Authority ?? string.Empty, StableIdentity.MaximumAuthorityLength);
                WriteText(writer, command.Target?.SubjectId ?? string.Empty, StableIdentity.MaximumSubjectIdLength * 4);
                writer.Write((byte)command.Role);
                writer.Write(command.InvitationExpiresUtcTicks);
                writer.Flush();
                if (stream.Length > MaximumPayloadBytes)
                    throw new InvalidOperationException("The group command exceeds its wire bound.");
                return stream.ToArray();
            }
        }

        public static bool TryDecode(byte[] payload, out GroupCommand command, out string failureCode)
        {
            command = null;
            failureCode = "group-command-invalid";
            if (payload == null || payload.Length < 33 || payload.Length > MaximumPayloadBytes)
                return false;
            try
            {
                using (var stream = new MemoryStream(payload, false))
                using (var reader = new BinaryReader(stream, StrictUtf8, true))
                {
                    if (reader.ReadUInt32() != Magic || reader.ReadByte() != Schema)
                    {
                        failureCode = "group-command-schema-invalid";
                        return false;
                    }
                    var kind = (GroupCommandKind)reader.ReadByte();
                    byte[] idBytes = reader.ReadBytes(16);
                    if (idBytes.Length != 16)
                    {
                        failureCode = "group-command-id-truncated";
                        return false;
                    }
                    var id = new Guid(idBytes);
                    string name = ReadText(reader, GroupLimits.MaximumDisplayNameUtf8Bytes);
                    string authority = ReadText(reader, StableIdentity.MaximumAuthorityLength);
                    string subject = ReadText(reader, StableIdentity.MaximumSubjectIdLength * 4);
                    var role = (GroupRole)reader.ReadByte();
                    long expiry = reader.ReadInt64();
                    if (stream.Position != stream.Length)
                    {
                        failureCode = "group-command-trailing-data";
                        return false;
                    }
                    StableIdentity target = authority.Length == 0 && subject.Length == 0
                        ? null
                        : new StableIdentity(authority, subject);
                    if ((authority.Length == 0) != (subject.Length == 0))
                    {
                        failureCode = "group-command-target-invalid";
                        return false;
                    }
                    command = new GroupCommand(kind, id, name, target, role, expiry);
                }
                if (!payload.SequenceEqual(Encode(command)))
                {
                    command = null;
                    failureCode = "group-command-noncanonical";
                    return false;
                }
                failureCode = "ok";
                return true;
            }
            catch (Exception exception) when (
                exception is ArgumentException || exception is EndOfStreamException ||
                exception is IOException || exception is DecoderFallbackException ||
                exception is OverflowException)
            {
                command = null;
                failureCode = "group-command-invalid";
                return false;
            }
        }

        private static void WriteText(BinaryWriter writer, string value, int maximumBytes)
        {
            byte[] bytes = StrictUtf8.GetBytes(value ?? string.Empty);
            if (bytes.Length > maximumBytes || bytes.Length > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(value));
            writer.Write((ushort)bytes.Length);
            writer.Write(bytes);
        }

        private static string ReadText(BinaryReader reader, int maximumBytes)
        {
            int length = reader.ReadUInt16();
            if (length > maximumBytes || length > reader.BaseStream.Length - reader.BaseStream.Position)
                throw new InvalidDataException("The group command text length is invalid.");
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            string value = StrictUtf8.GetString(bytes);
            if (!StrictUtf8.GetBytes(value).SequenceEqual(bytes))
                throw new InvalidDataException("The group command text is not canonical UTF-8.");
            return value;
        }
    }
}

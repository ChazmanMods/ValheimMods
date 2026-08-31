using System;
using System.IO;
using System.Linq;
using System.Text;

namespace RunicPermissions.Groups
{
    internal enum GroupFriendlyOperation : byte
    {
        List = 1,
        Active = 2,
        Members = 3,
        WhoAmI = 4,
        Create = 16,
        Select = 17,
        Invite = 18,
        Accept = 19,
        Leave = 20,
        Rename = 21,
        CancelInvitation = 22,
        Remove = 23,
        SetRole = 24,
        TransferOwnership = 25,
        Delete = 26
    }

    internal sealed class GroupFriendlyRequest
    {
        internal GroupFriendlyRequest(
            GroupFriendlyOperation operation,
            string primary = "",
            string secondary = "",
            int number = 0,
            Guid proposedGroupId = default)
        {
            if (!Enum.IsDefined(typeof(GroupFriendlyOperation), operation))
                throw new ArgumentOutOfRangeException(nameof(operation));
            Operation = operation;
            Primary = FriendlyText(primary, 256, nameof(primary));
            Secondary = FriendlyText(secondary, 64, nameof(secondary));
            if (number < 0 || number > 720) throw new ArgumentOutOfRangeException(nameof(number));
            Number = number;
            ProposedGroupId = proposedGroupId;
            ValidateShape();
        }

        internal GroupFriendlyOperation Operation { get; }
        internal string Primary { get; }
        internal string Secondary { get; }
        internal int Number { get; }
        internal Guid ProposedGroupId { get; }
        internal bool IsQuery => Operation < GroupFriendlyOperation.Create;

        private void ValidateShape()
        {
            bool primary = Primary.Length != 0;
            bool secondary = Secondary.Length != 0;
            switch (Operation)
            {
                case GroupFriendlyOperation.List:
                case GroupFriendlyOperation.Active:
                case GroupFriendlyOperation.Members:
                case GroupFriendlyOperation.WhoAmI:
                case GroupFriendlyOperation.Leave:
                case GroupFriendlyOperation.Delete:
                    if (primary || secondary || Number != 0 || ProposedGroupId != Guid.Empty)
                        throw new ArgumentException("This Group operation does not accept arguments.");
                    return;
                case GroupFriendlyOperation.Create:
                    if (!primary || secondary || Number != 0 || ProposedGroupId == Guid.Empty)
                        throw new ArgumentException("Create requires a name and proposed UUID.");
                    GroupIdentity.RequireDisplayName(Primary);
                    return;
                case GroupFriendlyOperation.Select:
                case GroupFriendlyOperation.Accept:
                case GroupFriendlyOperation.Rename:
                case GroupFriendlyOperation.CancelInvitation:
                case GroupFriendlyOperation.Remove:
                case GroupFriendlyOperation.TransferOwnership:
                    if (!primary || secondary || Number != 0 || ProposedGroupId != Guid.Empty)
                        throw new ArgumentException("This Group operation requires one text argument.");
                    if (Operation == GroupFriendlyOperation.Rename)
                        GroupIdentity.RequireDisplayName(Primary);
                    return;
                case GroupFriendlyOperation.Invite:
                    if (!primary || secondary || Number < 1 || ProposedGroupId != Guid.Empty)
                        throw new ArgumentException("Invite requires a player and bounded lifetime.");
                    return;
                case GroupFriendlyOperation.SetRole:
                    if (!primary || !secondary || Number != 0 || ProposedGroupId != Guid.Empty ||
                        !string.Equals(Secondary, "member", StringComparison.Ordinal) &&
                        !string.Equals(Secondary, "officer", StringComparison.Ordinal))
                        throw new ArgumentException("Role requires a player and member/officer.");
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(Operation));
            }
        }

        private static string FriendlyText(string value, int maximumBytes, string parameter)
        {
            string text = value ?? string.Empty;
            if (text.Length == 0) return string.Empty;
            if (!string.Equals(text, text.Trim(), StringComparison.Ordinal) ||
                !text.IsNormalized(NormalizationForm.FormC) ||
                Encoding.UTF8.GetByteCount(text) > maximumBytes)
                throw new ArgumentException("Group command text is not canonical.", parameter);
            for (int index = 0; index < text.Length; index++)
                if (char.IsControl(text[index]))
                    throw new ArgumentException("Group command text contains a control character.", parameter);
            return text;
        }
    }

    internal sealed class GroupFriendlyResponse
    {
        internal GroupFriendlyResponse(string text, ActiveGroupSelection active)
        {
            Text = text ?? string.Empty;
            if (Encoding.UTF8.GetByteCount(Text) > GroupQueryProtocol.MaximumWireBytes - 256)
                throw new ArgumentOutOfRangeException(nameof(text));
            Active = active ?? ActiveGroupSelection.Stale;
        }

        internal string Text { get; }
        internal ActiveGroupSelection Active { get; }
    }

    internal static class GroupFriendlyProtocol
    {
        internal const int MaximumRequestBytes = 1024;
        internal const int MaximumResponseBytes = GroupQueryProtocol.MaximumWireBytes;
        private const uint RequestMagic = 0x31514652; // "RFQ1"
        private const uint ResponseMagic = 0x31534652; // "RFS1"
        private const ushort Schema = 1;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        internal static byte[] EncodeRequest(GroupFriendlyRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, StrictUtf8, true))
            {
                writer.Write(RequestMagic);
                writer.Write(Schema);
                writer.Write((byte)request.Operation);
                WriteText(writer, request.Primary, 256);
                WriteText(writer, request.Secondary, 64);
                writer.Write(request.Number);
                writer.Write(request.ProposedGroupId.ToByteArray());
                writer.Flush();
                byte[] exact = stream.ToArray();
                if (exact.Length > MaximumRequestBytes) throw new InvalidOperationException();
                return exact;
            }
        }

        internal static bool TryDecodeRequest(
            byte[] payload,
            out GroupFriendlyRequest request,
            out string failure)
        {
            request = null;
            failure = "group-friendly-request-invalid";
            if (payload == null || payload.Length < 31 || payload.Length > MaximumRequestBytes)
                return false;
            try
            {
                using (var stream = new MemoryStream(payload, false))
                using (var reader = new BinaryReader(stream, StrictUtf8, true))
                {
                    if (reader.ReadUInt32() != RequestMagic || reader.ReadUInt16() != Schema)
                        return false;
                    var operation = (GroupFriendlyOperation)reader.ReadByte();
                    string primary = ReadText(reader, 256);
                    string secondary = ReadText(reader, 64);
                    int number = reader.ReadInt32();
                    byte[] id = reader.ReadBytes(16);
                    if (id.Length != 16 || stream.Position != stream.Length) return false;
                    request = new GroupFriendlyRequest(
                        operation, primary, secondary, number, new Guid(id));
                    failure = "ok";
                    return true;
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException || exception is IOException ||
                exception is DecoderFallbackException || exception is OverflowException)
            {
                request = null;
                return false;
            }
        }

        internal static byte[] EncodeResponse(GroupFriendlyResponse response)
        {
            if (response == null) throw new ArgumentNullException(nameof(response));
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, StrictUtf8, true))
            {
                writer.Write(ResponseMagic);
                writer.Write(Schema);
                writer.Write((byte)response.Active.Status);
                WriteText(writer, response.Active.GroupId, 32);
                WriteText(writer, response.Active.DisplayName, GroupLimits.MaximumDisplayNameUtf8Bytes);
                WriteText(writer, response.Text, MaximumResponseBytes - 128);
                writer.Flush();
                byte[] exact = stream.ToArray();
                if (exact.Length > MaximumResponseBytes) throw new InvalidOperationException();
                return exact;
            }
        }

        internal static bool TryDecodeResponse(
            byte[] payload,
            out GroupFriendlyResponse response,
            out string failure)
        {
            response = null;
            failure = "group-friendly-response-invalid";
            if (payload == null || payload.Length < 13 || payload.Length > MaximumResponseBytes)
                return false;
            try
            {
                using (var stream = new MemoryStream(payload, false))
                using (var reader = new BinaryReader(stream, StrictUtf8, true))
                {
                    if (reader.ReadUInt32() != ResponseMagic || reader.ReadUInt16() != Schema)
                        return false;
                    var status = (ActiveGroupSelectionStatus)reader.ReadByte();
                    string groupId = ReadText(reader, 32);
                    string name = ReadText(reader, GroupLimits.MaximumDisplayNameUtf8Bytes);
                    string text = ReadText(reader, MaximumResponseBytes - 128);
                    if (stream.Position != stream.Length) return false;
                    response = new GroupFriendlyResponse(
                        text, new ActiveGroupSelection(status, groupId, name));
                    failure = "ok";
                    return true;
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException || exception is IOException ||
                exception is DecoderFallbackException || exception is OverflowException)
            {
                response = null;
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
                throw new InvalidDataException();
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            string value = StrictUtf8.GetString(bytes);
            if (!StrictUtf8.GetBytes(value).SequenceEqual(bytes)) throw new InvalidDataException();
            return value;
        }
    }
}

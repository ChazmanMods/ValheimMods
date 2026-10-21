using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace RunicPermissions.Groups
{
    internal sealed class GroupCommandIssueResponse
    {
        internal GroupCommandIssueResponse(
            string token,
            long expectedCatalogRevision,
            long expectedGroupRevision)
        {
            if (!GroupCommandToken.TryParse(token, out _, out _) ||
                expectedCatalogRevision < 0 || expectedGroupRevision < -1)
                throw new ArgumentException("The Group command issue response is invalid.");
            Token = token;
            ExpectedCatalogRevision = expectedCatalogRevision;
            ExpectedGroupRevision = expectedGroupRevision;
        }

        internal string Token { get; }
        internal long ExpectedCatalogRevision { get; }
        internal long ExpectedGroupRevision { get; }
    }

    internal sealed class GroupCommandExecution
    {
        internal GroupCommandExecution(
            string token,
            long expectedCatalogRevision,
            long expectedGroupRevision,
            GroupCommand command)
        {
            if (!GroupCommandToken.TryParse(token, out _, out _) ||
                expectedCatalogRevision < 0 || expectedGroupRevision < -1)
                throw new ArgumentException("The Group command execution is invalid.");
            Token = token;
            ExpectedCatalogRevision = expectedCatalogRevision;
            ExpectedGroupRevision = expectedGroupRevision;
            Command = command ?? throw new ArgumentNullException(nameof(command));
        }

        internal string Token { get; }
        internal long ExpectedCatalogRevision { get; }
        internal long ExpectedGroupRevision { get; }
        internal GroupCommand Command { get; }
    }

    internal static class GroupCommandDurableCodec
    {
        private const uint IssueMagic = 0x49505247; // GRPI
        private const uint ExecutionMagic = 0x45505247; // GRPE
        private const ushort Schema = 1;
        internal const int MaximumExecutionBytes = 2048;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        internal static byte[] EncodeIssue(GroupCommandIssueResponse issue)
        {
            if (issue == null) throw new ArgumentNullException(nameof(issue));
            using (var stream = new MemoryStream(96))
            using (var writer = new BinaryWriter(stream, StrictUtf8, true))
            {
                writer.Write(IssueMagic);
                writer.Write(Schema);
                WriteText(writer, issue.Token, 64);
                writer.Write(issue.ExpectedCatalogRevision);
                writer.Write(issue.ExpectedGroupRevision);
                writer.Flush();
                return stream.ToArray();
            }
        }

        internal static bool TryDecodeIssue(
            byte[] payload,
            out GroupCommandIssueResponse issue,
            out string reason)
        {
            issue = null;
            reason = "group-command-issue-invalid";
            if (payload == null || payload.Length < 24 || payload.Length > 128) return false;
            try
            {
                using (var stream = new MemoryStream(payload, false))
                using (var reader = new BinaryReader(stream, StrictUtf8, true))
                {
                    if (reader.ReadUInt32() != IssueMagic || reader.ReadUInt16() != Schema)
                        return false;
                    issue = new GroupCommandIssueResponse(
                        ReadText(reader, 64), reader.ReadInt64(), reader.ReadInt64());
                    if (stream.Position != stream.Length) { issue = null; return false; }
                }
                if (!Exact(payload, EncodeIssue(issue))) { issue = null; return false; }
                reason = "ok";
                return true;
            }
            catch (Exception exception) when (
                exception is ArgumentException || exception is IOException ||
                exception is EndOfStreamException || exception is DecoderFallbackException)
            {
                issue = null;
                return false;
            }
        }

        internal static byte[] EncodeExecution(GroupCommandExecution execution)
        {
            if (execution == null) throw new ArgumentNullException(nameof(execution));
            byte[] command = GroupCommandCodec.Encode(execution.Command);
            using (var stream = new MemoryStream(command.Length + 96))
            using (var writer = new BinaryWriter(stream, StrictUtf8, true))
            {
                writer.Write(ExecutionMagic);
                writer.Write(Schema);
                WriteText(writer, execution.Token, 64);
                writer.Write(execution.ExpectedCatalogRevision);
                writer.Write(execution.ExpectedGroupRevision);
                writer.Write(command.Length);
                writer.Write(command);
                writer.Flush();
                if (stream.Length > MaximumExecutionBytes)
                    throw new InvalidOperationException("The Group command execution exceeds its wire bound.");
                return stream.ToArray();
            }
        }

        internal static bool TryDecodeExecution(
            byte[] payload,
            out GroupCommandExecution execution,
            out string reason)
        {
            execution = null;
            reason = "group-command-execution-invalid";
            if (payload == null || payload.Length < 32 || payload.Length > MaximumExecutionBytes)
                return false;
            try
            {
                using (var stream = new MemoryStream(payload, false))
                using (var reader = new BinaryReader(stream, StrictUtf8, true))
                {
                    if (reader.ReadUInt32() != ExecutionMagic || reader.ReadUInt16() != Schema)
                        return false;
                    string token = ReadText(reader, 64);
                    long catalogRevision = reader.ReadInt64();
                    long groupRevision = reader.ReadInt64();
                    int commandLength = reader.ReadInt32();
                    if (commandLength < 1 || commandLength > GroupCommandCodec.MaximumPayloadBytes ||
                        commandLength > stream.Length - stream.Position)
                        return false;
                    byte[] commandBytes = reader.ReadBytes(commandLength);
                    if (commandBytes.Length != commandLength || stream.Position != stream.Length ||
                        !GroupCommandCodec.TryDecode(
                            commandBytes, out GroupCommand command, out _)) return false;
                    execution = new GroupCommandExecution(
                        token, catalogRevision, groupRevision, command);
                }
                if (!Exact(payload, EncodeExecution(execution))) { execution = null; return false; }
                reason = "ok";
                return true;
            }
            catch (Exception exception) when (
                exception is ArgumentException || exception is IOException ||
                exception is EndOfStreamException || exception is DecoderFallbackException ||
                exception is OverflowException)
            {
                execution = null;
                return false;
            }
        }

        internal static string ComputeRequestSha256(GroupCommand command) =>
            ComputeSha256(GroupCommandCodec.Encode(command));

        internal static string ComputeSha256(byte[] payload)
        {
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] digest = algorithm.ComputeHash(payload ?? Array.Empty<byte>());
                var text = new StringBuilder(64);
                foreach (byte value in digest) text.Append(value.ToString("x2"));
                return text.ToString();
            }
        }

        private static void WriteText(BinaryWriter writer, string value, int maximumBytes)
        {
            byte[] bytes = StrictUtf8.GetBytes(value ?? string.Empty);
            if (bytes.Length < 1 || bytes.Length > maximumBytes)
                throw new ArgumentOutOfRangeException(nameof(value));
            writer.Write((ushort)bytes.Length);
            writer.Write(bytes);
        }

        private static string ReadText(BinaryReader reader, int maximumBytes)
        {
            int length = reader.ReadUInt16();
            if (length < 1 || length > maximumBytes ||
                length > reader.BaseStream.Length - reader.BaseStream.Position)
                throw new InvalidDataException();
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            return StrictUtf8.GetString(bytes);
        }

        private static bool Exact(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            int difference = 0;
            for (int index = 0; index < left.Length; index++) difference |= left[index] ^ right[index];
            return difference == 0;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Runic.Foundation.Persistence
{
    internal enum RpcFrameKind : byte
    {
        Challenge = 1,
        Offer = 2,
        Accept = 3,
        Reject = 4,
        Request = 5,
        Response = 6,
        Cancel = 7
    }

    internal sealed class RpcWireFrame
    {
        internal RpcFrameKind Kind;
        internal string SessionId = string.Empty;
        internal long Sequence;
        internal string CorrelationId = string.Empty;
        internal string ModuleId = string.Empty;
        internal string EndpointId = string.Empty;
        internal string IdempotencyKey = string.Empty;
        internal int TimeoutMilliseconds;
        internal RpcResultCode ResultCode;
        internal string ReasonCode = string.Empty;
        internal bool Replayed;
        internal byte[] Payload = Array.Empty<byte>();
    }

    internal static class RpcWireCodec
    {
        internal const int Magic = 0x31505252; // RRP1, little endian on the wire.
        internal const ushort WireVersion = 1;
        internal const int MaximumFrameBytes = 160 * 1024;
        internal const int MaximumIdentifierBytes = 192;
        internal const int MaximumIdempotencyBytes = 256;
        internal const int MaximumHandshakeBytes = 128 * 1024;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        internal static byte[] Encode(RpcWireFrame frame)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            ValidateFrame(frame);

            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(Magic);
                writer.Write(WireVersion);
                writer.Write((byte)frame.Kind);
                writer.Write(frame.Replayed);
                WriteGuid(writer, frame.SessionId, false, nameof(frame.SessionId));
                writer.Write(frame.Sequence);
                WriteGuid(writer, frame.CorrelationId, true, nameof(frame.CorrelationId));
                WriteBoundedString(writer, frame.ModuleId, MaximumIdentifierBytes, true);
                WriteBoundedString(writer, frame.EndpointId, MaximumIdentifierBytes, true);
                WriteBoundedString(writer, frame.IdempotencyKey, MaximumIdempotencyBytes, true);
                writer.Write(frame.TimeoutMilliseconds);
                writer.Write((int)frame.ResultCode);
                WriteBoundedString(writer, frame.ReasonCode, MaximumIdentifierBytes, true);
                byte[] payload = frame.Payload ?? Array.Empty<byte>();
                writer.Write(payload.Length);
                writer.Write(payload);
                writer.Flush();
                if (stream.Length > MaximumFrameBytes)
                    throw new ArgumentOutOfRangeException(nameof(frame), "RPC frame exceeds the wire bound.");
                return stream.ToArray();
            }
        }

        internal static bool TryDecode(byte[] bytes, out RpcWireFrame frame, out string error)
        {
            frame = null;
            error = string.Empty;
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaximumFrameBytes)
            {
                error = "frame-size";
                return false;
            }

            try
            {
                using (var stream = new MemoryStream(bytes, false))
                using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
                {
                    if (reader.ReadInt32() != Magic || reader.ReadUInt16() != WireVersion)
                    {
                        error = "wire-version";
                        return false;
                    }

                    var decoded = new RpcWireFrame
                    {
                        Kind = (RpcFrameKind)reader.ReadByte(),
                        Replayed = reader.ReadBoolean(),
                        SessionId = ReadGuid(reader, false),
                        Sequence = reader.ReadInt64(),
                        CorrelationId = ReadGuid(reader, true),
                        ModuleId = ReadBoundedString(reader, MaximumIdentifierBytes, true),
                        EndpointId = ReadBoundedString(reader, MaximumIdentifierBytes, true),
                        IdempotencyKey = ReadBoundedString(reader, MaximumIdempotencyBytes, true),
                        TimeoutMilliseconds = reader.ReadInt32()
                    };
                    int resultCode = reader.ReadInt32();
                    if (!Enum.IsDefined(typeof(RpcResultCode), resultCode))
                        throw new InvalidDataException("Unknown result code.");
                    decoded.ResultCode = (RpcResultCode)resultCode;
                    decoded.ReasonCode = ReadBoundedString(reader, MaximumIdentifierBytes, true);
                    int payloadLength = reader.ReadInt32();
                    if (payloadLength < 0 || payloadLength > RpcEndpointDescriptor.HardMaximumPayloadBytes &&
                        decoded.Kind != RpcFrameKind.Challenge && decoded.Kind != RpcFrameKind.Offer)
                        throw new InvalidDataException("Payload length exceeds the endpoint bound.");
                    if (payloadLength > MaximumHandshakeBytes)
                        throw new InvalidDataException("Payload length exceeds the handshake bound.");
                    if (payloadLength > stream.Length - stream.Position)
                        throw new EndOfStreamException();
                    decoded.Payload = reader.ReadBytes(payloadLength);
                    if (decoded.Payload.Length != payloadLength || stream.Position != stream.Length)
                        throw new InvalidDataException("RPC frame is truncated or has trailing bytes.");
                    ValidateFrame(decoded);
                    frame = decoded;
                    return true;
                }
            }
            catch (Exception exception) when (
                exception is EndOfStreamException || exception is IOException ||
                exception is InvalidDataException ||
                exception is ArgumentException || exception is InvalidOperationException)
            {
                error = "malformed-frame";
                return false;
            }
        }

        internal static string Fingerprint(RpcWireFrame frame)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            using (SHA256 algorithm = SHA256.Create())
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                WriteBoundedString(writer, frame.ModuleId, MaximumIdentifierBytes, false);
                WriteBoundedString(writer, frame.EndpointId, MaximumIdentifierBytes, false);
                WriteBoundedString(writer, frame.IdempotencyKey, MaximumIdempotencyBytes, true);
                byte[] payload = frame.Payload ?? Array.Empty<byte>();
                writer.Write(payload.Length);
                writer.Write(payload);
                writer.Flush();
                return ToLowerHex(algorithm.ComputeHash(stream.ToArray()));
            }
        }

        internal static string ToLowerHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            for (int index = 0; index < bytes.Length; index++)
                builder.Append(bytes[index].ToString("x2"));
            return builder.ToString();
        }

        private static void ValidateFrame(RpcWireFrame frame)
        {
            if (!Enum.IsDefined(typeof(RpcFrameKind), frame.Kind))
                throw new ArgumentOutOfRangeException(nameof(frame.Kind));
            if (frame.Sequence <= 0) throw new ArgumentOutOfRangeException(nameof(frame.Sequence));
            if (frame.TimeoutMilliseconds < 0 || frame.TimeoutMilliseconds > 60000)
                throw new ArgumentOutOfRangeException(nameof(frame.TimeoutMilliseconds));
            byte[] payload = frame.Payload ?? Array.Empty<byte>();
            int payloadMaximum = frame.Kind == RpcFrameKind.Challenge || frame.Kind == RpcFrameKind.Offer
                ? MaximumHandshakeBytes
                : RpcEndpointDescriptor.HardMaximumPayloadBytes;
            if (payload.Length > payloadMaximum) throw new ArgumentOutOfRangeException(nameof(frame.Payload));

            if (frame.Kind == RpcFrameKind.Request || frame.Kind == RpcFrameKind.Response ||
                frame.Kind == RpcFrameKind.Cancel)
            {
                RequireCanonicalGuid(frame.CorrelationId, false, nameof(frame.CorrelationId));
                Runic.Foundation.Core.RunicIdentifier.Require(frame.ModuleId, nameof(frame.ModuleId));
                Runic.Foundation.Core.RunicIdentifier.Require(frame.EndpointId, nameof(frame.EndpointId));
            }
            else if (!string.IsNullOrEmpty(frame.CorrelationId) ||
                     !string.IsNullOrEmpty(frame.ModuleId) ||
                     !string.IsNullOrEmpty(frame.EndpointId))
            {
                throw new ArgumentException("Handshake frames cannot carry request identity fields.");
            }

            if (frame.Kind == RpcFrameKind.Request)
            {
                if (!TryValidateIdempotencyKey(
                        frame.IdempotencyKey,
                        false,
                        out _,
                        out string failure))
                    throw new ArgumentException(failure, nameof(frame.IdempotencyKey));
            }
            else if (!string.IsNullOrEmpty(frame.IdempotencyKey))
            {
                throw new ArgumentException(
                    "Only request frames can carry an idempotency key.",
                    nameof(frame.IdempotencyKey));
            }

            bool carriesReason = frame.Kind == RpcFrameKind.Accept ||
                                 frame.Kind == RpcFrameKind.Reject ||
                                 frame.Kind == RpcFrameKind.Response;
            if (carriesReason)
                Runic.Foundation.Core.RunicIdentifier.Require(frame.ReasonCode, nameof(frame.ReasonCode));
            else if (!string.IsNullOrEmpty(frame.ReasonCode))
                throw new ArgumentException("This frame kind cannot carry a reason code.", nameof(frame.ReasonCode));
            RequireCanonicalGuid(frame.SessionId, false, nameof(frame.SessionId));
        }

        internal static bool TryValidateIdempotencyKey(
            string value,
            bool required,
            out string exact,
            out string reasonCode)
        {
            exact = value ?? string.Empty;
            reasonCode = string.Empty;
            if (required && exact.Length == 0)
            {
                reasonCode = "idempotency-key-required";
                return false;
            }
            if (exact.Length > 0 &&
                (char.IsWhiteSpace(exact[0]) || char.IsWhiteSpace(exact[exact.Length - 1])))
            {
                reasonCode = "idempotency-key-invalid";
                return false;
            }
            try
            {
                for (int index = 0; index < exact.Length; index++)
                {
                    if (char.IsControl(exact[index]))
                    {
                        reasonCode = "idempotency-key-invalid";
                        return false;
                    }
                }
                if (StrictUtf8.GetByteCount(exact) > MaximumIdempotencyBytes)
                {
                    reasonCode = "idempotency-key-too-long";
                    return false;
                }
            }
            catch (EncoderFallbackException)
            {
                reasonCode = "idempotency-key-invalid";
                return false;
            }
            return true;
        }

        private static void WriteGuid(BinaryWriter writer, string value, bool allowEmpty, string name)
        {
            if (allowEmpty && string.IsNullOrEmpty(value))
            {
                writer.Write(new byte[16]);
                return;
            }
            RequireCanonicalGuid(value, false, name);
            writer.Write(Guid.ParseExact(value, "N").ToByteArray());
        }

        private static string ReadGuid(BinaryReader reader, bool allowEmpty)
        {
            byte[] bytes = reader.ReadBytes(16);
            if (bytes.Length != 16) throw new EndOfStreamException();
            bool empty = bytes.All(value => value == 0);
            if (empty && allowEmpty) return string.Empty;
            if (empty) throw new InvalidDataException("Required GUID is empty.");
            return new Guid(bytes).ToString("N");
        }

        private static void RequireCanonicalGuid(string value, bool allowEmpty, string name)
        {
            if (allowEmpty && string.IsNullOrEmpty(value)) return;
            if (value == null || value.Length != 32 || !Guid.TryParseExact(value, "N", out Guid parsed) ||
                !string.Equals(parsed.ToString("N"), value, StringComparison.Ordinal))
                throw new ArgumentException("Value is not a canonical lowercase GUID.", name);
        }

        internal static void WriteBoundedString(
            BinaryWriter writer,
            string value,
            int maximumBytes,
            bool allowEmpty)
        {
            value = value ?? string.Empty;
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            if ((!allowEmpty && bytes.Length == 0) || bytes.Length > maximumBytes)
                throw new ArgumentOutOfRangeException(nameof(value));
            writer.Write((ushort)bytes.Length);
            writer.Write(bytes);
        }

        internal static string ReadBoundedString(
            BinaryReader reader,
            int maximumBytes,
            bool allowEmpty)
        {
            int length = reader.ReadUInt16();
            if ((!allowEmpty && length == 0) || length > maximumBytes)
                throw new InvalidDataException("String length exceeds its bound.");
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            string value = new UTF8Encoding(false, true).GetString(bytes);
            if (Encoding.UTF8.GetByteCount(value) != length)
                throw new InvalidDataException("String encoding is not canonical UTF-8.");
            for (int index = 0; index < value.Length; index++)
                if (char.IsControl(value[index]))
                    throw new InvalidDataException("String contains a control character.");
            return value;
        }
    }

    internal sealed class RpcHandshakeProfile
    {
        internal byte[] Nonce = Array.Empty<byte>();
        internal byte[] EchoNonce = Array.Empty<byte>();
        internal ProtocolHello Hello;
    }

    internal static class RpcHandshakeCodec
    {
        internal const int NonceBytes = 32;

        internal static byte[] Encode(RpcHandshakeProfile profile)
        {
            if (profile == null || profile.Hello == null) throw new ArgumentNullException(nameof(profile));
            RequireNonce(profile.Nonce, nameof(profile.Nonce));
            if (profile.EchoNonce != null && profile.EchoNonce.Length != 0)
                RequireNonce(profile.EchoNonce, nameof(profile.EchoNonce));
            if (!string.Equals(
                    profile.Hello.Nonce,
                    RpcWireCodec.ToLowerHex(profile.Nonce),
                    StringComparison.Ordinal))
                throw new ArgumentException(
                    "The public hello nonce must be the canonical form of the binary session nonce.",
                    nameof(profile));

            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write((byte)profile.Nonce.Length);
                writer.Write(profile.Nonce);
                byte[] echo = profile.EchoNonce ?? Array.Empty<byte>();
                writer.Write((byte)echo.Length);
                writer.Write(echo);
                RpcWireCodec.WriteBoundedString(writer, profile.Hello.Nonce, 128, false);
                writer.Write((ushort)profile.Hello.Modules.Count);
                foreach (ModuleProtocolState module in profile.Hello.Modules.OrderBy(value => value.ModuleId, StringComparer.Ordinal))
                {
                    RpcWireCodec.WriteBoundedString(writer, module.ModuleId, 128, false);
                    RpcWireCodec.WriteBoundedString(writer, module.SemanticVersion, 128, false);
                    writer.Write(module.ProtocolVersion);
                    writer.Write((ushort)module.Capabilities.Count);
                    foreach (string capability in module.Capabilities)
                        RpcWireCodec.WriteBoundedString(writer, capability, 128, false);
                }
                writer.Write((ushort)profile.Hello.Claims.Count);
                foreach (KeyValuePair<string, string> claim in
                         profile.Hello.Claims.Values.OrderBy(value => value.Key, StringComparer.Ordinal))
                {
                    RpcWireCodec.WriteBoundedString(
                        writer,
                        claim.Key,
                        RpcHandshakeClaim.MaximumKeyBytes,
                        false);
                    RpcWireCodec.WriteBoundedString(
                        writer,
                        claim.Value,
                        RpcHandshakeClaim.MaximumValueBytes,
                        true);
                }
                writer.Flush();
                if (stream.Length > RpcWireCodec.MaximumHandshakeBytes)
                    throw new ArgumentOutOfRangeException(nameof(profile));
                return stream.ToArray();
            }
        }

        internal static bool TryDecode(byte[] payload, out RpcHandshakeProfile profile)
        {
            profile = null;
            if (payload == null || payload.Length == 0 || payload.Length > RpcWireCodec.MaximumHandshakeBytes)
                return false;
            try
            {
                using (var stream = new MemoryStream(payload, false))
                using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
                {
                    int nonceLength = reader.ReadByte();
                    if (nonceLength != NonceBytes) return false;
                    byte[] nonce = reader.ReadBytes(nonceLength);
                    int echoLength = reader.ReadByte();
                    if (echoLength != 0 && echoLength != NonceBytes) return false;
                    byte[] echo = reader.ReadBytes(echoLength);
                    string helloNonce = RpcWireCodec.ReadBoundedString(reader, 128, false);
                    if (!string.Equals(
                            helloNonce,
                            RpcWireCodec.ToLowerHex(nonce),
                            StringComparison.Ordinal)) return false;
                    int moduleCount = reader.ReadUInt16();
                    if (moduleCount > ProtocolHello.MaximumModules) return false;
                    var modules = new List<ModuleProtocolState>(moduleCount);
                    for (int moduleIndex = 0; moduleIndex < moduleCount; moduleIndex++)
                    {
                        string moduleId = RpcWireCodec.ReadBoundedString(reader, 128, false);
                        string semanticVersion = RpcWireCodec.ReadBoundedString(reader, 128, false);
                        int protocolVersion = reader.ReadInt32();
                        int capabilityCount = reader.ReadUInt16();
                        if (capabilityCount > ModuleProtocolState.MaximumCapabilities) return false;
                        var capabilities = new List<string>(capabilityCount);
                        for (int capabilityIndex = 0; capabilityIndex < capabilityCount; capabilityIndex++)
                            capabilities.Add(RpcWireCodec.ReadBoundedString(reader, 128, false));
                        modules.Add(new ModuleProtocolState(
                            moduleId,
                            semanticVersion,
                            protocolVersion,
                            capabilities));
                    }
                    int claimCount = reader.ReadUInt16();
                    if (claimCount > RpcHandshakeClaims.MaximumClaims) return false;
                    var claims = new List<RpcHandshakeClaim>(claimCount);
                    for (int claimIndex = 0; claimIndex < claimCount; claimIndex++)
                    {
                        claims.Add(new RpcHandshakeClaim(
                            RpcWireCodec.ReadBoundedString(
                                reader,
                                RpcHandshakeClaim.MaximumKeyBytes,
                                false),
                            RpcWireCodec.ReadBoundedString(
                                reader,
                                RpcHandshakeClaim.MaximumValueBytes,
                                true)));
                    }
                    if (stream.Position != stream.Length) return false;
                    profile = new RpcHandshakeProfile
                    {
                        Nonce = nonce,
                        EchoNonce = echo,
                        Hello = new ProtocolHello(helloNonce, modules, claims)
                    };
                    return true;
                }
            }
            catch (Exception exception) when (
                exception is IOException || exception is InvalidDataException ||
                exception is ArgumentException ||
                exception is FormatException ||
                exception is InvalidOperationException)
            {
                return false;
            }
        }

        internal static byte[] CreateNonce()
        {
            byte[] nonce = new byte[NonceBytes];
            using (RandomNumberGenerator generator = RandomNumberGenerator.Create())
                generator.GetBytes(nonce);
            return nonce;
        }

        internal static bool FixedEquals(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            int difference = 0;
            for (int index = 0; index < left.Length; index++) difference |= left[index] ^ right[index];
            return difference == 0;
        }

        private static void RequireNonce(byte[] nonce, string name)
        {
            if (nonce == null || nonce.Length != NonceBytes)
                throw new ArgumentOutOfRangeException(name);
        }
    }
}

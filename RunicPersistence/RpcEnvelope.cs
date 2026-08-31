using System;
using System.Collections.Generic;

namespace Runic.Foundation.Persistence
{
    public sealed class RpcEnvelope
    {
        public const int MaximumIdentifierLength = 128;
        public const int MaximumPayloadBytes = 1024 * 1024;

        private readonly byte[] _payload;

        public RpcEnvelope(
            int protocolVersion,
            string moduleId,
            string messageType,
            string correlationId,
            string idempotencyKey,
            byte[] payload,
            long createdUtcTicks)
        {
            if (protocolVersion < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(protocolVersion));
            }

            RequireIdentifier(moduleId, nameof(moduleId));
            RequireIdentifier(messageType, nameof(messageType));
            if (!string.IsNullOrEmpty(correlationId))
                RequireIdentifier(correlationId, nameof(correlationId));
            if (!string.IsNullOrEmpty(idempotencyKey))
                RequireIdentifier(idempotencyKey, nameof(idempotencyKey));
            if (payload != null && payload.Length > MaximumPayloadBytes)
                throw new ArgumentOutOfRangeException(nameof(payload), "RPC payload exceeds the bounded envelope limit.");
            if (createdUtcTicks < 0 || createdUtcTicks > DateTime.MaxValue.Ticks)
                throw new ArgumentOutOfRangeException(nameof(createdUtcTicks));

            ProtocolVersion = protocolVersion;
            ModuleId = moduleId;
            MessageType = messageType;
            CorrelationId = string.IsNullOrEmpty(correlationId) ? Guid.NewGuid().ToString("N") : correlationId;
            IdempotencyKey = idempotencyKey ?? string.Empty;
            _payload = payload == null ? Array.Empty<byte>() : (byte[])payload.Clone();
            CreatedUtcTicks = createdUtcTicks;
        }

        public int ProtocolVersion { get; }

        public string ModuleId { get; }

        public string MessageType { get; }

        public string CorrelationId { get; }

        public string IdempotencyKey { get; }

        public byte[] Payload => (byte[])_payload.Clone();

        public long CreatedUtcTicks { get; }

        private static void RequireIdentifier(string value, string parameterName)
        {
            if (string.IsNullOrEmpty(value))
                throw new ArgumentException("RPC identifiers cannot be empty.", parameterName);
            if (value.Length > MaximumIdentifierLength)
                throw new ArgumentOutOfRangeException(parameterName, "RPC identifier exceeds the bounded length limit.");
            if (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[value.Length - 1]))
                throw new ArgumentException("RPC identifiers cannot have leading or trailing whitespace.", parameterName);
            for (int index = 0; index < value.Length; index++)
                if (char.IsControl(value[index]))
                    throw new ArgumentException("RPC identifiers cannot contain control characters.", parameterName);
        }
    }

    public sealed class IdempotencyWindow
    {
        public const int DefaultMaximumEntries = 10000;
        public const int MaximumKeyLength = 256;

        private readonly object _gate = new object();
        private readonly Dictionary<string, long> _seen =
            new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly int _maximumEntries;

        public IdempotencyWindow(int maximumEntries = DefaultMaximumEntries)
        {
            if (maximumEntries < 1 || maximumEntries > 1000000)
                throw new ArgumentOutOfRangeException(nameof(maximumEntries));
            _maximumEntries = maximumEntries;
        }

        public int Count
        {
            get
            {
                lock (_gate) return _seen.Count;
            }
        }

        public bool TryAccept(string idempotencyKey, long nowUtcTicks, long retentionTicks)
        {
            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                return true;
            }
            if (idempotencyKey.Length > MaximumKeyLength)
                throw new ArgumentOutOfRangeException(nameof(idempotencyKey));
            if (nowUtcTicks < 0 || nowUtcTicks > DateTime.MaxValue.Ticks)
                throw new ArgumentOutOfRangeException(nameof(nowUtcTicks));
            if (retentionTicks <= 0)
                throw new ArgumentOutOfRangeException(nameof(retentionTicks));

            lock (_gate)
            {
                var expired = new List<string>();
                foreach (KeyValuePair<string, long> entry in _seen)
                {
                    if (entry.Value <= nowUtcTicks)
                    {
                        expired.Add(entry.Key);
                    }
                }

                foreach (string key in expired)
                {
                    _seen.Remove(key);
                }

                if (_seen.ContainsKey(idempotencyKey))
                {
                    return false;
                }

                // Capacity exhaustion denies admission rather than evicting a still-live key and
                // accidentally permitting a duplicate mutation.
                if (_seen.Count >= _maximumEntries)
                    return false;

                _seen.Add(idempotencyKey, checked(nowUtcTicks + retentionTicks));
                return true;
            }
        }
    }
}

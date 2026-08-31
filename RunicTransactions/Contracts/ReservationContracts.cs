using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace RunicTransactions.Contracts
{
    public readonly struct ResourceBalance : IEquatable<ResourceBalance>
    {
        public ResourceBalance(ResourceId resource, long amount)
        {
            if (!resource.IsValid) throw new ArgumentException("Resource identifier is invalid.", nameof(resource));
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            Resource = resource;
            Amount = amount;
        }

        public ResourceId Resource { get; }
        public long Amount { get; }
        public bool Equals(ResourceBalance other) => Resource.Equals(other.Resource) && Amount == other.Amount;
        public override bool Equals(object obj) => obj is ResourceBalance other && Equals(other);
        public override int GetHashCode() => unchecked((Resource.GetHashCode() * 397) ^ Amount.GetHashCode());
    }

    public readonly struct ReservationLine : IEquatable<ReservationLine>
    {
        public ReservationLine(EndpointId endpoint, ResourceId resource, long amount)
        {
            if (!endpoint.IsValid) throw new ArgumentException("Endpoint identifier is invalid.", nameof(endpoint));
            if (!resource.IsValid) throw new ArgumentException("Resource identifier is invalid.", nameof(resource));
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
            Endpoint = endpoint;
            Resource = resource;
            Amount = amount;
        }

        public EndpointId Endpoint { get; }
        public ResourceId Resource { get; }
        public long Amount { get; }
        public bool Equals(ReservationLine other) =>
            Endpoint.Equals(other.Endpoint) && Resource.Equals(other.Resource) && Amount == other.Amount;
        public override bool Equals(object obj) => obj is ReservationLine other && Equals(other);
        public override int GetHashCode() => unchecked(((Endpoint.GetHashCode() * 397) ^ Resource.GetHashCode()) * 397 ^ Amount.GetHashCode());
        public override string ToString() => $"{Endpoint}/{Resource}:{Amount.ToString(CultureInfo.InvariantCulture)}";
    }

    public sealed class ReservationRequest
    {
        public const int MaximumLines = 4096;
        private readonly ReservationLine[] _lines;

        public ReservationRequest(
            TransactionId transactionId,
            CorrelationId correlationId,
            IdempotencyKey idempotencyKey,
            PrincipalId principal,
            IEnumerable<ReservationLine> lines)
        {
            if (!transactionId.IsValid) throw new ArgumentException("Transaction identifier is invalid.", nameof(transactionId));
            if (!correlationId.IsValid) throw new ArgumentException("Correlation identifier is invalid.", nameof(correlationId));
            if (!idempotencyKey.IsValid) throw new ArgumentException("Idempotency key is invalid.", nameof(idempotencyKey));
            if (!principal.IsValid) throw new ArgumentException("Principal identifier is invalid.", nameof(principal));
            if (lines == null) throw new ArgumentNullException(nameof(lines));

            TransactionId = transactionId;
            CorrelationId = correlationId;
            IdempotencyKey = idempotencyKey;
            Principal = principal;
            _lines = Canonicalize(lines);
            if (_lines.Length == 0) throw new ArgumentException("At least one reservation line is required.", nameof(lines));
            Fingerprint = ComputeFingerprint();
        }

        public TransactionId TransactionId { get; }
        public CorrelationId CorrelationId { get; }
        public IdempotencyKey IdempotencyKey { get; }
        public PrincipalId Principal { get; }
        public IReadOnlyList<ReservationLine> Lines => _lines;
        public string Fingerprint { get; }

        private static ReservationLine[] Canonicalize(IEnumerable<ReservationLine> lines)
        {
            var totals = new Dictionary<LineKey, long>();
            int inputCount = 0;
            foreach (ReservationLine line in lines)
            {
                if (++inputCount > MaximumLines) throw new ArgumentOutOfRangeException(nameof(lines), "Too many reservation lines.");
                if (!line.Endpoint.IsValid || !line.Resource.IsValid || line.Amount <= 0)
                    throw new ArgumentException("Every reservation line must have valid identifiers and a positive amount.", nameof(lines));

                var key = new LineKey(line.Endpoint, line.Resource);
                long existing;
                totals.TryGetValue(key, out existing);
                try
                {
                    totals[key] = checked(existing + line.Amount);
                }
                catch (OverflowException)
                {
                    throw new ArgumentOutOfRangeException(nameof(lines), "Reservation amount overflowed Int64.");
                }
            }

            return totals
                .Select(pair => new ReservationLine(pair.Key.Endpoint, pair.Key.Resource, pair.Value))
                .OrderBy(line => line.Endpoint)
                .ThenBy(line => line.Resource)
                .ToArray();
        }

        private string ComputeFingerprint()
        {
            var builder = new StringBuilder();
            Append(builder, Principal.Value);
            foreach (ReservationLine line in _lines)
            {
                Append(builder, line.Endpoint.Value);
                Append(builder, line.Resource.Value);
                builder.Append(line.Amount.ToString(CultureInfo.InvariantCulture)).Append(';');
            }

            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()));
                var hex = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) hex.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                return hex.ToString();
            }
        }

        private static void Append(StringBuilder builder, string value) =>
            builder.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append(';');

        private readonly struct LineKey : IEquatable<LineKey>
        {
            internal LineKey(EndpointId endpoint, ResourceId resource)
            {
                Endpoint = endpoint;
                Resource = resource;
            }

            internal EndpointId Endpoint { get; }
            internal ResourceId Resource { get; }
            public bool Equals(LineKey other) => Endpoint.Equals(other.Endpoint) && Resource.Equals(other.Resource);
            public override bool Equals(object obj) => obj is LineKey other && Equals(other);
            public override int GetHashCode() => unchecked((Endpoint.GetHashCode() * 397) ^ Resource.GetHashCode());
        }
    }
}

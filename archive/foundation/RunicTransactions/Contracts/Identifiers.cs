using System;

namespace RunicTransactions.Contracts
{
    internal static class StableIdentifier
    {
        internal static string Require(string value, string parameterName, int maximumLength)
        {
            if (value == null) throw new ArgumentNullException(parameterName);
            if (value.Length == 0) throw new ArgumentException("Identifier cannot be empty.", parameterName);
            if (value.Length > maximumLength)
                throw new ArgumentOutOfRangeException(parameterName, $"Identifier cannot exceed {maximumLength} characters.");
            if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
                throw new ArgumentException("Identifier cannot have leading or trailing whitespace.", parameterName);

            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if (char.IsControl(character) || char.IsSurrogate(character))
                    throw new ArgumentException("Identifier contains a control or surrogate character.", parameterName);
            }

            return value;
        }

        internal static int Compare(string left, string right) =>
            string.Compare(left ?? string.Empty, right ?? string.Empty, StringComparison.Ordinal);

        internal static int Hash(string value) => value == null ? 0 : StringComparer.Ordinal.GetHashCode(value);
    }

    public readonly struct EndpointId : IEquatable<EndpointId>, IComparable<EndpointId>
    {
        public EndpointId(string value) => Value = StableIdentifier.Require(value, nameof(value), 160);

        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public int CompareTo(EndpointId other) => StableIdentifier.Compare(Value, other.Value);
        public bool Equals(EndpointId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is EndpointId other && Equals(other);
        public override int GetHashCode() => StableIdentifier.Hash(Value);
        public override string ToString() => Value ?? string.Empty;
        public static bool operator ==(EndpointId left, EndpointId right) => left.Equals(right);
        public static bool operator !=(EndpointId left, EndpointId right) => !left.Equals(right);
    }

    public readonly struct ResourceId : IEquatable<ResourceId>, IComparable<ResourceId>
    {
        public ResourceId(string value) => Value = StableIdentifier.Require(value, nameof(value), 160);

        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public int CompareTo(ResourceId other) => StableIdentifier.Compare(Value, other.Value);
        public bool Equals(ResourceId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is ResourceId other && Equals(other);
        public override int GetHashCode() => StableIdentifier.Hash(Value);
        public override string ToString() => Value ?? string.Empty;
        public static bool operator ==(ResourceId left, ResourceId right) => left.Equals(right);
        public static bool operator !=(ResourceId left, ResourceId right) => !left.Equals(right);
    }

    public readonly struct PrincipalId : IEquatable<PrincipalId>, IComparable<PrincipalId>
    {
        public PrincipalId(string value) => Value = StableIdentifier.Require(value, nameof(value), 160);

        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public int CompareTo(PrincipalId other) => StableIdentifier.Compare(Value, other.Value);
        public bool Equals(PrincipalId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is PrincipalId other && Equals(other);
        public override int GetHashCode() => StableIdentifier.Hash(Value);
        public override string ToString() => Value ?? string.Empty;
        public static bool operator ==(PrincipalId left, PrincipalId right) => left.Equals(right);
        public static bool operator !=(PrincipalId left, PrincipalId right) => !left.Equals(right);
    }

    public readonly struct TransactionId : IEquatable<TransactionId>, IComparable<TransactionId>
    {
        public TransactionId(string value) => Value = StableIdentifier.Require(value, nameof(value), 128);

        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public static TransactionId New() => new TransactionId(Guid.NewGuid().ToString("N"));
        public int CompareTo(TransactionId other) => StableIdentifier.Compare(Value, other.Value);
        public bool Equals(TransactionId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is TransactionId other && Equals(other);
        public override int GetHashCode() => StableIdentifier.Hash(Value);
        public override string ToString() => Value ?? string.Empty;
        public static bool operator ==(TransactionId left, TransactionId right) => left.Equals(right);
        public static bool operator !=(TransactionId left, TransactionId right) => !left.Equals(right);
    }

    public readonly struct CorrelationId : IEquatable<CorrelationId>, IComparable<CorrelationId>
    {
        public CorrelationId(string value) => Value = StableIdentifier.Require(value, nameof(value), 128);

        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public static CorrelationId New() => new CorrelationId(Guid.NewGuid().ToString("N"));
        public int CompareTo(CorrelationId other) => StableIdentifier.Compare(Value, other.Value);
        public bool Equals(CorrelationId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is CorrelationId other && Equals(other);
        public override int GetHashCode() => StableIdentifier.Hash(Value);
        public override string ToString() => Value ?? string.Empty;
        public static bool operator ==(CorrelationId left, CorrelationId right) => left.Equals(right);
        public static bool operator !=(CorrelationId left, CorrelationId right) => !left.Equals(right);
    }

    public readonly struct IdempotencyKey : IEquatable<IdempotencyKey>, IComparable<IdempotencyKey>
    {
        public IdempotencyKey(string value) => Value = StableIdentifier.Require(value, nameof(value), 200);

        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public static IdempotencyKey New() => new IdempotencyKey(Guid.NewGuid().ToString("N"));
        public int CompareTo(IdempotencyKey other) => StableIdentifier.Compare(Value, other.Value);
        public bool Equals(IdempotencyKey other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is IdempotencyKey other && Equals(other);
        public override int GetHashCode() => StableIdentifier.Hash(Value);
        public override string ToString() => Value ?? string.Empty;
        public static bool operator ==(IdempotencyKey left, IdempotencyKey right) => left.Equals(right);
        public static bool operator !=(IdempotencyKey left, IdempotencyKey right) => !left.Equals(right);
    }
}

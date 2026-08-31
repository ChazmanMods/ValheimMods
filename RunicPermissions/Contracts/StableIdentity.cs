using System;

namespace RunicPermissions.Contracts
{
    /// <summary>
    /// A platform/provider-qualified player identifier. Display names are     /// excluded from equality and authorization.
    /// </summary>
    public sealed class StableIdentity : IEquatable<StableIdentity>, IComparable<StableIdentity>
    {
        public const int MaximumAuthorityLength = 64;
        public const int MaximumSubjectIdLength = 256;

        public StableIdentity(string authority, string subjectId)
        {
            string normalizedAuthority = NormalizeAuthority(authority);
            string normalizedSubject = NormalizeSubject(subjectId);
            if (!IsValidAuthority(normalizedAuthority))
                throw new ArgumentException("Identity authority must contain only ASCII letters, digits, '.', '_', or '-'.", nameof(authority));
            if (!IsValidSubject(normalizedSubject))
                throw new ArgumentException("Identity subject ID is empty, too long, or contains control characters.", nameof(subjectId));

            Authority = normalizedAuthority;
            SubjectId = normalizedSubject;
            CanonicalKey = Authority + ":" + Uri.EscapeDataString(SubjectId);
        }

        public string Authority { get; }

        public string SubjectId { get; }

        public string CanonicalKey { get; }

        public static bool TryCreate(string authority, string subjectId, out StableIdentity identity)
        {
            try
            {
                identity = new StableIdentity(authority, subjectId);
                return true;
            }
            catch (ArgumentException)
            {
                identity = null;
                return false;
            }
        }

        public bool Equals(StableIdentity other) =>
            other != null &&
            string.Equals(Authority, other.Authority, StringComparison.Ordinal) &&
            string.Equals(SubjectId, other.SubjectId, StringComparison.Ordinal);

        public override bool Equals(object obj) => Equals(obj as StableIdentity);

        public override int GetHashCode()
        {
            unchecked
            {
                return (StringComparer.Ordinal.GetHashCode(Authority) * 397) ^
                       StringComparer.Ordinal.GetHashCode(SubjectId);
            }
        }

        public int CompareTo(StableIdentity other)
        {
            if (other == null) return 1;
            int authority = string.Compare(Authority, other.Authority, StringComparison.Ordinal);
            return authority != 0
                ? authority
                : string.Compare(SubjectId, other.SubjectId, StringComparison.Ordinal);
        }

        public override string ToString() => CanonicalKey;

        private static string NormalizeAuthority(string value) =>
            (value ?? string.Empty).Trim().ToLowerInvariant();

        private static string NormalizeSubject(string value) => (value ?? string.Empty).Trim();

        private static bool IsValidAuthority(string value)
        {
            if (value.Length == 0 || value.Length > MaximumAuthorityLength) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                bool valid = character >= 'a' && character <= 'z' ||
                             character >= '0' && character <= '9' ||
                             character == '.' || character == '_' || character == '-';
                if (!valid) return false;
            }
            return true;
        }

        private static bool IsValidSubject(string value)
        {
            if (value.Length == 0 || value.Length > MaximumSubjectIdLength) return false;
            for (int i = 0; i < value.Length; i++)
                if (char.IsControl(value[i])) return false;
            return true;
        }
    }

    public enum IdentityResolutionStatus
    {
        Verified = 0,
        Missing = 1,
        Ambiguous = 2,
        Stale = 3
    }

    /// <summary>
    /// The current server-side resolution of a player identity. DisplayNameSnapshot is
    /// informational only and is never consulted by the evaluator.
    /// </summary>
    public sealed class IdentityClaim
    {
        public IdentityClaim(
            StableIdentity identity,
            string displayNameSnapshot,
            IdentityResolutionStatus status)
        {
            Identity = identity;
            DisplayNameSnapshot = displayNameSnapshot ?? string.Empty;
            Status = status;
        }

        public StableIdentity Identity { get; }

        public string DisplayNameSnapshot { get; }

        public IdentityResolutionStatus Status { get; }

        public bool IsVerified => Status == IdentityResolutionStatus.Verified && Identity != null;

        public static IdentityClaim Verified(StableIdentity identity, string displayNameSnapshot = "") =>
            new IdentityClaim(identity, displayNameSnapshot, IdentityResolutionStatus.Verified);
    }
}

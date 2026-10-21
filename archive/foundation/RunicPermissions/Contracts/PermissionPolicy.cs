using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using RunicPermissions.Groups;

namespace RunicPermissions.Contracts
{
    public enum PermissionPolicyKind
    {
        Everyone = 1,
        Approved = 2,
        Owner = 3,
        Nobody = 4,
        Ward = 5,
        WardWithExceptions = 6,
        Group = 7
    }

    /// <summary>Stable persistence/wire names for policy kinds.</summary>
    public static class PermissionPolicyCodec
    {
        private static readonly PermissionPolicyKind[] Kinds =
        {
            PermissionPolicyKind.Everyone,
            PermissionPolicyKind.Approved,
            PermissionPolicyKind.Owner,
            PermissionPolicyKind.Nobody,
            PermissionPolicyKind.Ward,
            PermissionPolicyKind.WardWithExceptions,
            PermissionPolicyKind.Group
        };

        private static readonly string[] Names =
        {
            "everyone",
            "approved",
            "owner",
            "nobody",
            "ward",
            "ward.exceptions",
            "group"
        };

        public static string ToWireName(PermissionPolicyKind kind)
        {
            for (int i = 0; i < Kinds.Length; i++)
                if (Kinds[i] == kind) return Names[i];
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown permission policy kind.");
        }

        public static bool TryParse(string wireName, out PermissionPolicyKind kind)
        {
            for (int i = 0; i < Names.Length; i++)
            {
                if (!string.Equals(Names[i], wireName, StringComparison.Ordinal)) continue;
                kind = Kinds[i];
                return true;
            }
            kind = default;
            return false;
        }
    }

    /// <summary>
    /// Object-local explicit allow and deny identities for one action. If an identity
    /// occurs in both lists, deny wins.
    /// </summary>
    public sealed class AccessControlList
    {
        private readonly StableIdentity[] _allowed;
        private readonly StableIdentity[] _denied;
        private readonly ReadOnlyCollection<StableIdentity> _allowedView;
        private readonly ReadOnlyCollection<StableIdentity> _deniedView;

        public AccessControlList(
            IEnumerable<StableIdentity> allowed = null,
            IEnumerable<StableIdentity> denied = null)
        {
            _allowed = Copy(allowed, out bool invalidAllowed);
            _denied = Copy(denied, out bool invalidDenied);
            _allowedView = Array.AsReadOnly(_allowed);
            _deniedView = Array.AsReadOnly(_denied);
            IsWellFormed = !invalidAllowed && !invalidDenied;
        }

        public IReadOnlyList<StableIdentity> Allowed => _allowedView;

        public IReadOnlyList<StableIdentity> Denied => _deniedView;

        public bool IsWellFormed { get; }

        public AccessOverride Resolve(StableIdentity identity)
        {
            if (identity == null) return AccessOverride.None;
            return new AccessOverride(Contains(_allowed, identity), Contains(_denied, identity));
        }

        private static StableIdentity[] Copy(IEnumerable<StableIdentity> source, out bool invalid)
        {
            invalid = false;
            if (source == null) return Array.Empty<StableIdentity>();
            var values = new List<StableIdentity>();
            foreach (StableIdentity identity in source)
            {
                if (identity == null)
                {
                    invalid = true;
                    continue;
                }
                if (!Contains(values, identity)) values.Add(identity);
            }
            values.Sort();
            return values.ToArray();
        }

        private static bool Contains(IReadOnlyList<StableIdentity> values, StableIdentity identity)
        {
            for (int i = 0; i < values.Count; i++)
                if (values[i].Equals(identity)) return true;
            return false;
        }
    }

    public readonly struct AccessOverride
    {
        public AccessOverride(bool allow, bool deny)
        {
            Allow = allow;
            Deny = deny;
        }

        public bool Allow { get; }

        public bool Deny { get; }

        public static AccessOverride None => new AccessOverride(false, false);

        public static AccessOverride ExplicitAllow => new AccessOverride(true, false);

        public static AccessOverride ExplicitDeny => new AccessOverride(false, true);

        public static AccessOverride Conflicting => new AccessOverride(true, true);
    }

    /// <summary>A single action's object-local policy.</summary>
    public sealed class PermissionPolicy
    {
        public PermissionPolicy(
            PermissionPolicyKind kind,
            AccessControlList accessList = null,
            string groupId = "")
        {
            Kind = kind;
            AccessList = accessList ?? new AccessControlList();
            GroupId = groupId ?? string.Empty;
        }

        public PermissionPolicyKind Kind { get; }

        public AccessControlList AccessList { get; }

        public string GroupId { get; }

        public bool IsWellFormed =>
            Enum.IsDefined(typeof(PermissionPolicyKind), Kind) &&
            AccessList != null && AccessList.IsWellFormed &&
            (Kind != PermissionPolicyKind.Group || GroupIdentity.IsCanonicalId(GroupId));
    }

    /// <summary>
    /// Immutable mapping of independent action scopes to policies. Missing scopes deny.
    /// </summary>
    public sealed class PermissionProfile
    {
        private readonly Dictionary<PermissionAction, PermissionPolicy> _policies;
        private readonly ReadOnlyDictionary<PermissionAction, PermissionPolicy> _view;

        public PermissionProfile(
            string profileId,
            int schemaVersion,
            long revision,
            IEnumerable<KeyValuePair<PermissionAction, PermissionPolicy>> policies,
            RecordTrust trust = RecordTrust.Current)
        {
            ProfileId = (profileId ?? string.Empty).Trim();
            SchemaVersion = schemaVersion;
            Revision = revision;
            Trust = trust;
            _policies = new Dictionary<PermissionAction, PermissionPolicy>();
            bool invalid = false;
            if (policies == null)
            {
                invalid = true;
            }
            else
            {
                foreach (KeyValuePair<PermissionAction, PermissionPolicy> pair in policies)
                {
                    if (!Enum.IsDefined(typeof(PermissionAction), pair.Key) || pair.Value == null)
                    {
                        invalid = true;
                        continue;
                    }
                    if (_policies.ContainsKey(pair.Key))
                    {
                        invalid = true;
                        continue;
                    }
                    _policies.Add(pair.Key, pair.Value);
                }
            }
            _view = new ReadOnlyDictionary<PermissionAction, PermissionPolicy>(_policies);
            IsWellFormed = !invalid && ProfileId.Length > 0 && SchemaVersion > 0 && Revision >= 0 &&
                           Enum.IsDefined(typeof(RecordTrust), Trust);
        }

        public string ProfileId { get; }

        public int SchemaVersion { get; }

        public long Revision { get; }

        public RecordTrust Trust { get; }

        public bool IsWellFormed { get; }

        public IReadOnlyDictionary<PermissionAction, PermissionPolicy> Policies => _view;

        public bool TryGetPolicy(PermissionAction action, out PermissionPolicy policy) =>
            _policies.TryGetValue(action, out policy);
    }
}

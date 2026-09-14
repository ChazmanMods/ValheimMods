using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using RunicPortals.Api;

namespace RunicPortals.Core
{
    /// <summary>
    /// Exact bounded metadata evidence used on both sides of confirmation. All data exported to
    /// the Core contract is reduced to opaque fixed-size hashes.
    /// </summary>
    internal readonly struct PortalEditEvidence
    {
        internal PortalEditEvidence(
            int schema,
            int mode,
            int revision,
            string network,
            string name,
            long owner,
            int direction)
            : this(
                schema,
                mode,
                revision,
                network,
                name,
                owner,
                mode == (int)PortalMode.Network
                    ? PortalPermissionAdapter.Identity(owner)
                    : string.Empty,
                mode == (int)PortalMode.Network
                    ? PortalNetworkKind.Public
                    : PortalNetworkKind.Custom,
                string.Empty,
                direction)
        {
        }

        internal PortalEditEvidence(
            int schema,
            int mode,
            int revision,
            string network,
            string name,
            long owner,
            string ownerIdentity,
            PortalNetworkKind networkKind,
            string groupId,
            int direction)
        {
            Schema = schema;
            Mode = mode;
            Revision = revision;
            Network = network ?? string.Empty;
            Name = name ?? string.Empty;
            Owner = owner;
            OwnerIdentity = ownerIdentity ?? string.Empty;
            NetworkKind = networkKind;
            GroupId = groupId ?? string.Empty;
            Direction = direction;
        }

        internal int Schema { get; }
        internal int Mode { get; }
        internal int Revision { get; }
        internal string Network { get; }
        internal string Name { get; }
        internal long Owner { get; }
        internal string OwnerIdentity { get; }
        internal PortalNetworkKind NetworkKind { get; }
        internal string GroupId { get; }
        internal int Direction { get; }

        internal bool IsCanonicalRecord()
        {
            bool standard = Mode == (int)PortalMode.StandardPair;
            bool network = Mode == (int)PortalMode.Network;
            if ((!standard && !network) || Revision < 0 ||
                Schema != Integration.PortalZdoCodec.SchemaVersion)
                return false;
            if (standard)
                return Network.Length == 0 && Name.Length == 0 && Owner == 0L &&
                       OwnerIdentity.Length == 0 && NetworkKind == PortalNetworkKind.Custom &&
                       GroupId.Length == 0 && Direction == 0;
            return BoundedText(Network, PortalContractLimits.MaximumNetworkIdLength) &&
                   BoundedText(Name, PortalContractLimits.MaximumNameLength) &&
                   Owner != 0L && Direction >= 1 && Direction <= 3 &&
                   PortalPermissionAdapter.TryParseIdentity(OwnerIdentity, out _) &&
                   (NetworkKind == PortalNetworkKind.Public ||
                    NetworkKind == PortalNetworkKind.Personal ||
                    NetworkKind == PortalNetworkKind.Group) &&
                   (NetworkKind == PortalNetworkKind.Group
                       ? RunicPermissions.Groups.GroupIdentity.IsCanonicalId(GroupId)
                       : GroupId.Length == 0);
        }

        internal bool Matches(PortalEditEvidence other) =>
            Schema == other.Schema && Mode == other.Mode && Revision == other.Revision &&
            Owner == other.Owner && Direction == other.Direction &&
            NetworkKind == other.NetworkKind &&
            string.Equals(Network, other.Network, StringComparison.Ordinal) &&
            string.Equals(Name, other.Name, StringComparison.Ordinal) &&
            string.Equals(OwnerIdentity, other.OwnerIdentity, StringComparison.Ordinal) &&
            string.Equals(GroupId, other.GroupId, StringComparison.Ordinal);

        /// <summary>
        /// Canonical consumer-state evidence for the shared Transactions WAL. Runic identity,
        /// claim, owner-revision, and epoch/sequence marker fields are excluded.
        /// </summary>
        internal string StateFingerprint()
        {
            var builder = new StringBuilder(256);
            Append(builder, "runic.portals.metadata.v2");
            Append(builder, Schema);
            Append(builder, Mode);
            Append(builder, Revision);
            Append(builder, Network);
            Append(builder, Name);
            Append(builder, Owner);
            Append(builder, OwnerIdentity);
            Append(builder, (int)NetworkKind);
            Append(builder, GroupId);
            Append(builder, Direction);
            return Hash(builder.ToString());
        }

        internal string SemanticRevision =>
            "portal.metadata.v2." + Revision.ToString(CultureInfo.InvariantCulture);

        internal static bool TryProject(
            PortalEditEvidence before,
            PortalEditCommand command,
            long creator,
            string ownerStableIdentity,
            out PortalEditEvidence after)
        {
            after = default;
            if (command == null || command.Kind == PortalEditKind.Invalid || creator == 0L ||
                before.Revision == int.MaxValue ||
                !PortalPermissionAdapter.TryParseIdentity(ownerStableIdentity, out _))
                return false;
            int revision = Math.Max(0, before.Revision) + 1;
            if (command.Kind == PortalEditKind.StandardPair)
            {
                after = new PortalEditEvidence(
                    Integration.PortalZdoCodec.SchemaVersion,
                    (int)PortalMode.StandardPair,
                    revision,
                    string.Empty,
                    string.Empty,
                    0L,
                    string.Empty,
                    PortalNetworkKind.Custom,
                    string.Empty,
                    0);
                return true;
            }
            int direction = (command.AcceptsArrival ? 1 : 0) |
                            (command.PermitsDeparture ? 2 : 0);
            after = new PortalEditEvidence(
                Integration.PortalZdoCodec.SchemaVersion,
                (int)PortalMode.Network,
                revision,
                command.NetworkId,
                command.DisplayName,
                creator,
                ownerStableIdentity,
                command.NetworkKind,
                command.GroupId,
                direction);
            return after.Matches(command, creator, ownerStableIdentity);
        }

        internal bool Matches(PortalEditCommand command, long creator)
            => Matches(command, creator, PortalPermissionAdapter.Identity(creator));

        internal bool Matches(
            PortalEditCommand command,
            long creator,
            string ownerStableIdentity)
        {
            if (command == null || command.Kind == PortalEditKind.Invalid) return false;
            if (command.Kind == PortalEditKind.StandardPair)
                return Mode == (int)PortalMode.StandardPair;
            int direction = (command.AcceptsArrival ? 1 : 0) |
                            (command.PermitsDeparture ? 2 : 0);
            return Mode == (int)PortalMode.Network && Owner == creator && Direction == direction &&
                   NetworkKind == command.NetworkKind &&
                   string.Equals(Network, command.NetworkId, StringComparison.Ordinal) &&
                   string.Equals(Name, command.DisplayName, StringComparison.Ordinal) &&
                   string.Equals(OwnerIdentity, ownerStableIdentity, StringComparison.Ordinal) &&
                   string.Equals(GroupId, command.GroupId, StringComparison.Ordinal);
        }

        internal bool RequiresConfirmation(PortalEditCommand command, long creator) =>
            Mode == (int)PortalMode.Network && !Matches(command, creator);

        internal string Fingerprint(PortalEditCommand command, long creator)
            => Fingerprint(command, creator, PortalPermissionAdapter.Identity(creator));

        internal string Fingerprint(
            PortalEditCommand command,
            long creator,
            string ownerStableIdentity)
        {
            var builder = new StringBuilder(384);
            Append(builder, Schema);
            Append(builder, Mode);
            Append(builder, Revision);
            Append(builder, Network);
            Append(builder, Name);
            Append(builder, Owner);
            Append(builder, OwnerIdentity);
            Append(builder, (int)NetworkKind);
            Append(builder, GroupId);
            Append(builder, Direction);
            Append(builder, (int)(command?.Kind ?? PortalEditKind.Invalid));
            Append(builder, command?.NetworkId ?? string.Empty);
            Append(builder, command?.DisplayName ?? string.Empty);
            Append(builder, (int)(command?.NetworkKind ?? PortalNetworkKind.Custom));
            Append(builder, command?.GroupId ?? string.Empty);
            Append(builder, command != null && command.AcceptsArrival ? 1 : 0);
            Append(builder, command != null && command.PermitsDeparture ? 1 : 0);
            Append(builder, command != null && command.HasVanillaTag ? 1 : 0);
            Append(builder, command?.VanillaTag ?? string.Empty);
            Append(builder, creator);
            Append(builder, ownerStableIdentity ?? string.Empty);
            return "portal-v2:" + Hash(builder.ToString());
        }

        internal string LegacyFingerprint(PortalEditCommand command, long creator)
        {
            var builder = new StringBuilder(384);
            Append(builder, Schema);
            Append(builder, Mode);
            Append(builder, Revision);
            Append(builder, Network);
            Append(builder, Name);
            Append(builder, Owner);
            Append(builder, Direction);
            Append(builder, (int)(command?.Kind ?? PortalEditKind.Invalid));
            Append(builder, command?.NetworkId ?? string.Empty);
            Append(builder, command?.DisplayName ?? string.Empty);
            Append(builder, command != null && command.AcceptsArrival ? 1 : 0);
            Append(builder, command != null && command.PermitsDeparture ? 1 : 0);
            Append(builder, command != null && command.HasVanillaTag ? 1 : 0);
            Append(builder, command?.VanillaTag ?? string.Empty);
            Append(builder, creator);
            return "portal-v1:" + Hash(builder.ToString());
        }

        internal static string OpaqueTarget(string portalId)
        {
            string value = portalId ?? string.Empty;
            if (value.Length == 0 || value.Length > 96)
                throw new ArgumentOutOfRangeException(nameof(portalId));
            return "zdo-sha256:" + Hash(value);
        }

        internal static bool TryCapture(ZDO zdo, out PortalEditEvidence evidence)
        {
            evidence = default;
            if (zdo == null || !zdo.IsValid() || zdo.m_uid.IsNone()) return false;
            if (Integration.PortalZdoCodec.TryReadAuthoritativeRecord(
                    zdo,
                    out PortalEditEvidence authoritative,
                    out _,
                    out _,
                    out bool recordPresent,
                    out _))
            {
                if (recordPresent)
                {
                    evidence = authoritative;
                    return evidence.IsCanonicalRecord();
                }
            }
            else if (recordPresent)
            {
                return false;
            }
            int schema = zdo.GetInt(Integration.PortalZdoCodec.SchemaKey, 0);
            int mode = zdo.GetInt(Integration.PortalZdoCodec.ModeKey, 0);
            int revision = zdo.GetInt(Integration.PortalZdoCodec.RevisionKey, 0);
            bool standard = mode == (int)PortalMode.StandardPair;
            bool network = mode == (int)PortalMode.Network;
            if ((!standard && !network) || revision < 0) return false;
            if (standard && schema != 0 &&
                schema != Integration.PortalZdoCodec.LegacySchemaVersion &&
                schema != Integration.PortalZdoCodec.SchemaVersion)
                return false;
            if (network && schema != Integration.PortalZdoCodec.LegacySchemaVersion &&
                schema != Integration.PortalZdoCodec.SchemaVersion) return false;

            string networkId = string.Empty;
            string name = string.Empty;
            long owner = 0L;
            string ownerIdentity = string.Empty;
            PortalNetworkKind networkKind = PortalNetworkKind.Custom;
            string groupId = string.Empty;
            int direction = 0;
            if (network)
            {
                networkId = zdo.GetString(Integration.PortalZdoCodec.NetworkKey, string.Empty);
                name = zdo.GetString(Integration.PortalZdoCodec.NameKey, string.Empty);
                owner = zdo.GetLong(Integration.PortalZdoCodec.OwnerKey, 0L);
                ownerIdentity = schema == Integration.PortalZdoCodec.LegacySchemaVersion
                    ? PortalPermissionAdapter.Identity(owner)
                    : zdo.GetString(Integration.PortalZdoCodec.OwnerIdentityKey, string.Empty);
                networkKind = schema == Integration.PortalZdoCodec.LegacySchemaVersion
                    ? PortalNetworkKind.Public
                    : (PortalNetworkKind)zdo.GetInt(
                        Integration.PortalZdoCodec.NetworkKindKey,
                        (int)PortalNetworkKind.Custom);
                groupId = schema == Integration.PortalZdoCodec.LegacySchemaVersion
                    ? string.Empty
                    : zdo.GetString(Integration.PortalZdoCodec.GroupKey, string.Empty);
                direction = zdo.GetInt(Integration.PortalZdoCodec.DirectionKey, 0);
                if (!BoundedText(networkId, PortalContractLimits.MaximumNetworkIdLength) ||
                    !BoundedText(name, PortalContractLimits.MaximumNameLength) ||
                    owner == 0L || direction < 1 || direction > 3 ||
                    !PortalPermissionAdapter.TryParseIdentity(ownerIdentity, out _) ||
                    (networkKind != PortalNetworkKind.Public &&
                     networkKind != PortalNetworkKind.Personal &&
                     networkKind != PortalNetworkKind.Group) ||
                    (networkKind == PortalNetworkKind.Group
                        ? !RunicPermissions.Groups.GroupIdentity.IsCanonicalId(groupId)
                        : groupId.Length != 0))
                    return false;
            }
            evidence = new PortalEditEvidence(
                schema, mode, revision, networkId, name, owner, ownerIdentity,
                networkKind, groupId, direction);
            return true;
        }

        private static bool BoundedText(string value, int maximum)
        {
            if (string.IsNullOrEmpty(value) || value.Length > maximum) return false;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (char.IsControl(character)) return false;
                if (!char.IsSurrogate(character)) continue;
                if (!char.IsHighSurrogate(character) || index + 1 >= value.Length ||
                    !char.IsLowSurrogate(value[index + 1])) return false;
                index++;
            }
            return true;
        }

        private static void Append(StringBuilder builder, int value) => Append(builder,
            value.ToString(CultureInfo.InvariantCulture));

        private static void Append(StringBuilder builder, long value) => Append(builder,
            value.ToString(CultureInfo.InvariantCulture));

        private static void Append(StringBuilder builder, string value)
        {
            string safe = value ?? string.Empty;
            builder.Append(safe.Length.ToString(CultureInfo.InvariantCulture))
                .Append(':').Append(safe).Append(';');
        }

        private static string Hash(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            byte[] digest;
            using (SHA256 sha = SHA256.Create()) digest = sha.ComputeHash(bytes);
            var builder = new StringBuilder(64);
            for (int index = 0; index < digest.Length; index++)
                builder.Append(digest[index].ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }
    }
}

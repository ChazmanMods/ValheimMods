using System;
using System.IO;
using System.Text;
using RunicPermissions.Groups;
using RunicPortals.Api;
using RunicPortals.Core;

namespace RunicPortals.Integration
{
    internal static class PortalZdoCodec
    {
        internal const int SchemaVersion = 2;
        internal const int LegacySchemaVersion = 1;
        internal const string SchemaKey = "runic.portals.schema";
        internal const string ModeKey = "runic.portals.mode";
        internal const string NetworkKey = "runic.portals.network";
        internal const string NameKey = "runic.portals.name";
        internal const string OwnerKey = "runic.portals.owner";
        internal const string OwnerIdentityKey = "runic.portals.ownerIdentity";
        internal const string NetworkKindKey = "runic.portals.networkKind";
        internal const string GroupKey = "runic.portals.group";
        internal const string DirectionKey = "runic.portals.direction";
        internal const string RevisionKey = "runic.portals.revision";
        internal const string AuthoritativeRecordKey = "runic.portals.record";
        internal const string PublicationPulseKey = "runic.portals.publicationPulse";

        private const uint RecordMagic = 0x31525052; // RPR1
        private const ushort RecordSchema = 1;
        private const int MaximumRecordBytes = 2048;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        internal const int ArrivalFlag = 1;
        internal const int DepartureFlag = 2;

        // Called only for objects obtained from the native portal registry.
        // Keep TryRead network-only so vanilla interaction and tag pairing stay untouched.
        internal static bool TryReadDestination(ZDO zdo, out PortalEndpoint endpoint, out string failure)
        {
            endpoint = null;
            failure = string.Empty;
            if (zdo == null || !zdo.IsValid() || zdo.m_uid.IsNone()) return false;
            if (GetMode(zdo) != (int)PortalMode.StandardPair)
                return TryRead(zdo, out endpoint, out failure);
            int schema = GetSchema(zdo);
            if (schema != 0 && schema != LegacySchemaVersion && schema != SchemaVersion) return false;
            string tag = zdo.GetString(ZDOVars.s_tag, string.Empty).Trim();
            if (tag.Length == 0) tag = "Unnamed Standard Pair";
            if (tag.Length > PortalContractLimits.MaximumNameLength)
                tag = tag.Substring(0, PortalContractLimits.MaximumNameLength);
            endpoint = new PortalEndpoint(zdo.m_uid.ToString(), PortalMode.StandardPair,
                tag, string.Empty, PortalNetworkKind.Public, string.Empty, string.Empty,
                PortalOnlineState.Online, true, false, PortalAccessProfile.PublicNetwork,
                Math.Max(0, GetRevision(zdo)));
            return true;
        }

        internal static bool TryRead(ZDO zdo, out PortalEndpoint endpoint, out string failure)
        {
            endpoint = null;
            failure = string.Empty;
            if (zdo == null || !zdo.IsValid() || zdo.m_uid.IsNone())
            {
                failure = "zdo-missing";
                return false;
            }
            if (!PortalEditEvidence.TryCapture(zdo, out PortalEditEvidence evidence))
            {
                int rawSchema = zdo.GetInt(SchemaKey, 0);
                int rawMode = zdo.GetInt(ModeKey, 0);
                PortalNetworkKind rawKind = (PortalNetworkKind)zdo.GetInt(
                    NetworkKindKey, (int)PortalNetworkKind.Custom);
                string rawGroup = zdo.GetString(GroupKey, string.Empty);
                failure = rawSchema == SchemaVersion && rawMode == (int)PortalMode.Network &&
                          rawKind == PortalNetworkKind.Group &&
                          !GroupIdentity.IsCanonicalId(rawGroup)
                    ? "record-group-invalid"
                    : "record-invalid";
                return false;
            }
            int mode = evidence.Mode;
            if (mode == (int)PortalMode.StandardPair) return false;
            if (mode != (int)PortalMode.Network)
            {
                failure = "mode-unsupported";
                return false;
            }
            int schema = evidence.Schema;
            if (schema != LegacySchemaVersion && schema != SchemaVersion)
            {
                failure = "schema-unsupported";
                return false;
            }
            int revision = evidence.Revision;
            long owner = evidence.Owner;
            int directions = evidence.Direction;
            if (revision < 0 || owner == 0L)
            {
                failure = "record-invalid";
                return false;
            }
            try
            {
                PortalNetworkKind networkKind = evidence.NetworkKind;
                string ownerIdentity = evidence.OwnerIdentity;
                string groupId = evidence.GroupId;
                PortalAccessProfile access = PortalAccessProfile.PublicNetwork;
                if (schema == SchemaVersion)
                {
                    if ((networkKind != PortalNetworkKind.Public &&
                         networkKind != PortalNetworkKind.Personal &&
                         networkKind != PortalNetworkKind.Group) ||
                        !PortalPermissionAdapter.TryParseIdentity(ownerIdentity, out _))
                    {
                        failure = "record-policy-invalid";
                        return false;
                    }
                    if (networkKind == PortalNetworkKind.Group)
                    {
                        if (!GroupIdentity.IsCanonicalId(groupId))
                        {
                            failure = "record-group-invalid";
                            return false;
                        }
                        access = PortalAccessProfile.ForGroup(groupId);
                    }
                    else
                    {
                        if (groupId.Length != 0)
                        {
                            failure = "record-group-invalid";
                            return false;
                        }
                        access = networkKind == PortalNetworkKind.Personal
                            ? PortalAccessProfile.PrivateNetwork
                            : PortalAccessProfile.PublicNetwork;
                    }
                }
                endpoint = new PortalEndpoint(
                    zdo.m_uid.ToString(),
                    PortalMode.Network,
                    evidence.Name,
                    evidence.Network,
                    networkKind,
                    ownerIdentity,
                    string.Empty,
                    PortalOnlineState.Online,
                    (directions & ArrivalFlag) != 0,
                    (directions & DepartureFlag) != 0,
                    access,
                    revision,
                    string.Empty);
                return true;
            }
            catch (ArgumentException)
            {
                failure = "record-invalid";
                return false;
            }
        }

        internal static bool TryWrite(
            ZDO zdo,
            PortalEditCommand command,
            long owner,
            out int committedRevision,
            out string failure) => TryWrite(
                zdo,
                command,
                owner,
                PortalPermissionAdapter.Identity(owner),
                out committedRevision,
                out failure);

        internal static bool TryWrite(
            ZDO zdo,
            PortalEditCommand command,
            long owner,
            string ownerStableIdentity,
            out int committedRevision,
            out string failure)
        {
            committedRevision = -1;
            failure = string.Empty;
            if (zdo == null || !zdo.IsValid() || zdo.m_uid.IsNone() || command == null || owner == 0L ||
                !PortalPermissionAdapter.TryParseIdentity(ownerStableIdentity, out _))
            {
                failure = "write-context-invalid";
                return false;
            }
            if (command.Kind == PortalEditKind.PublicNetwork &&
                ((command.NetworkKind != PortalNetworkKind.Public &&
                  command.NetworkKind != PortalNetworkKind.Personal &&
                  command.NetworkKind != PortalNetworkKind.Group) ||
                 command.NetworkKind == PortalNetworkKind.Group !=
                 GroupIdentity.IsCanonicalId(command.GroupId)))
            {
                failure = "command-policy-invalid";
                return false;
            }
            if (!PortalEditEvidence.TryCapture(zdo, out PortalEditEvidence beforeEvidence))
            {
                failure = "record-invalid";
                return false;
            }
            int existingSchema = beforeEvidence.Schema;
            int existingMode = beforeEvidence.Mode;
            if (existingMode != (int)PortalMode.StandardPair &&
                existingMode != (int)PortalMode.Network)
            {
                failure = "mode-unsupported";
                return false;
            }
            if (existingSchema != 0 && existingSchema != LegacySchemaVersion &&
                existingSchema != SchemaVersion)
            {
                failure = "schema-unsupported";
                return false;
            }
            if (beforeEvidence.Revision == int.MaxValue)
            {
                failure = "revision-exhausted";
                return false;
            }
            if (!PortalEditEvidence.TryProject(
                    beforeEvidence,
                    command,
                    owner,
                    ownerStableIdentity,
                    out PortalEditEvidence after))
            {
                failure = "command-invalid";
                return false;
            }
            return TryPublishAuthoritativeRecord(
                zdo, beforeEvidence, after, string.Empty, 0L, out committedRevision, out failure);
        }

        internal static int GetSchema(ZDO zdo) =>
            PortalEditEvidence.TryCapture(zdo, out PortalEditEvidence evidence)
                ? evidence.Schema
                : -1;

        internal static int GetMode(ZDO zdo) =>
            PortalEditEvidence.TryCapture(zdo, out PortalEditEvidence evidence)
                ? evidence.Mode
                : -1;

        internal static int GetRevision(ZDO zdo) =>
            PortalEditEvidence.TryCapture(zdo, out PortalEditEvidence evidence)
                ? evidence.Revision
                : -1;

        internal static bool TryPublishAuthoritativeRecord(
            ZDO zdo,
            PortalEditEvidence expectedBefore,
            PortalEditEvidence after,
            string worldEpoch,
            long commitSequence,
            out int committedRevision,
            out string failure)
        {
            committedRevision = -1;
            failure = string.Empty;
            if (zdo == null || !zdo.IsValid() || zdo.m_uid.IsNone() ||
                (!expectedBefore.IsCanonicalRecord() &&
                 expectedBefore.Schema != 0 &&
                 expectedBefore.Schema != LegacySchemaVersion) ||
                !after.IsCanonicalRecord() ||
                commitSequence < 0 ||
                commitSequence == 0 != string.IsNullOrEmpty(worldEpoch) ||
                commitSequence > 0 &&
                (!Guid.TryParseExact(worldEpoch, "N", out Guid epoch) || epoch == Guid.Empty ||
                 !string.Equals(epoch.ToString("N"), worldEpoch, StringComparison.Ordinal)))
            {
                failure = "record-publication-context-invalid";
                return false;
            }
            if (!PortalEditEvidence.TryCapture(zdo, out PortalEditEvidence current) ||
                !current.Matches(expectedBefore))
            {
                failure = "record-publication-before-changed";
                return false;
            }
            string encoded;
            try { encoded = EncodeRecord(after, worldEpoch, commitSequence); }
            catch
            {
                failure = "record-publication-encode";
                return false;
            }
            try
            {
                zdo.Set(AuthoritativeRecordKey, encoded);
                if (!string.Equals(
                        zdo.GetString(AuthoritativeRecordKey, string.Empty),
                        encoded,
                        StringComparison.Ordinal) ||
                    !TryReadAuthoritativeRecord(
                        zdo, out PortalEditEvidence verified, out string verifiedEpoch,
                        out long verifiedSequence, out bool present, out _) ||
                    !present || !verified.Matches(after) || verifiedSequence != commitSequence ||
                    !string.Equals(verifiedEpoch, worldEpoch, StringComparison.Ordinal))
                {
                    failure = "record-publication-readback";
                    return false;
                }

                // The single authoritative record above is the atomic logical commit. These
                // legacy fields are a compatibility projection only; a crash while projecting
                // cannot create an ambiguous Runic state because all readers prefer the record.
                TryProjectLegacy(zdo, after);
                committedRevision = after.Revision;
                return true;
            }
            catch
            {
                failure = "record-publication-fault";
                return false;
            }
        }

        /// <summary>
        /// Advances only a transport-publication pulse. The authoritative record remains the
        /// sole logical portal commit. A new pulse increments the owning ZDO's DataRevision so
        /// an exact owner-command replay can requeue the complete record even after the native
        /// peer send cache has already observed the prior revision.
        /// </summary>
        internal static bool TryAdvancePublicationPulse(ZDO zdo, out string failure)
        {
            failure = "publication-pulse-context-invalid";
            if (zdo == null || !zdo.IsValid() || zdo.m_uid.IsNone()) return false;
            long current = zdo.GetLong(PublicationPulseKey, 0L);
            if (current < 0L || current == long.MaxValue)
            {
                failure = "publication-pulse-exhausted";
                return false;
            }
            try
            {
                long next = current + 1L;
                zdo.Set(PublicationPulseKey, next);
                if (zdo.GetLong(PublicationPulseKey, 0L) != next)
                {
                    failure = "publication-pulse-readback";
                    return false;
                }
                failure = string.Empty;
                return true;
            }
            catch
            {
                failure = "publication-pulse-fault";
                return false;
            }
        }

        internal static bool TryReadAuthoritativeRecord(
            ZDO zdo,
            out PortalEditEvidence evidence,
            out string worldEpoch,
            out long commitSequence,
            out bool recordPresent,
            out string failure)
        {
            evidence = default;
            worldEpoch = string.Empty;
            commitSequence = 0L;
            recordPresent = false;
            failure = string.Empty;
            if (zdo == null || !zdo.IsValid())
            {
                failure = "record-zdo-invalid";
                return false;
            }
            string encoded = zdo.GetString(AuthoritativeRecordKey, string.Empty);
            if (encoded.Length == 0) return true;
            recordPresent = true;
            try
            {
                byte[] bytes = Convert.FromBase64String(encoded);
                if (bytes.Length == 0 || bytes.Length > MaximumRecordBytes)
                    throw new InvalidDataException();
                using (var stream = new MemoryStream(bytes, false))
                using (var reader = new BinaryReader(stream, StrictUtf8, true))
                {
                    if (reader.ReadUInt32() != RecordMagic ||
                        reader.ReadUInt16() != RecordSchema || reader.ReadUInt16() != 0)
                        throw new InvalidDataException();
                    evidence = ReadEvidence(reader);
                    worldEpoch = ReadText(reader, 32);
                    commitSequence = reader.ReadInt64();
                    if (stream.Position != stream.Length || !evidence.IsCanonicalRecord() ||
                        commitSequence < 0 ||
                        commitSequence == 0 != (worldEpoch.Length == 0) ||
                        commitSequence > 0 &&
                        (!Guid.TryParseExact(worldEpoch, "N", out Guid epoch) || epoch == Guid.Empty ||
                         !string.Equals(epoch.ToString("N"), worldEpoch, StringComparison.Ordinal)) ||
                        !string.Equals(EncodeRecord(evidence, worldEpoch, commitSequence), encoded,
                            StringComparison.Ordinal))
                        throw new InvalidDataException();
                }
                return true;
            }
            catch
            {
                evidence = default;
                worldEpoch = string.Empty;
                commitSequence = 0L;
                failure = "record-corrupt";
                return false;
            }
        }

        private static string EncodeRecord(
            PortalEditEvidence evidence,
            string worldEpoch,
            long commitSequence)
        {
            if (!evidence.IsCanonicalRecord()) throw new ArgumentException(nameof(evidence));
            using (var stream = new MemoryStream(512))
            using (var writer = new BinaryWriter(stream, StrictUtf8, true))
            {
                writer.Write(RecordMagic);
                writer.Write(RecordSchema);
                writer.Write((ushort)0);
                WriteEvidence(writer, evidence);
                WriteText(writer, worldEpoch ?? string.Empty, 32);
                writer.Write(commitSequence);
                writer.Flush();
                if (stream.Length > MaximumRecordBytes) throw new InvalidDataException();
                return Convert.ToBase64String(stream.ToArray());
            }
        }

        private static void WriteEvidence(BinaryWriter writer, PortalEditEvidence value)
        {
            writer.Write(value.Schema);
            writer.Write(value.Mode);
            writer.Write(value.Revision);
            WriteText(writer, value.Network, PortalContractLimits.MaximumNetworkIdLength);
            WriteText(writer, value.Name, PortalContractLimits.MaximumNameLength);
            writer.Write(value.Owner);
            WriteText(writer, value.OwnerIdentity, 96);
            writer.Write((byte)value.NetworkKind);
            WriteText(writer, value.GroupId, 64);
            writer.Write(value.Direction);
        }

        private static PortalEditEvidence ReadEvidence(BinaryReader reader) =>
            new PortalEditEvidence(
                reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(),
                ReadText(reader, PortalContractLimits.MaximumNetworkIdLength),
                ReadText(reader, PortalContractLimits.MaximumNameLength),
                reader.ReadInt64(), ReadText(reader, 96),
                (PortalNetworkKind)reader.ReadByte(), ReadText(reader, 64), reader.ReadInt32());

        private static void WriteText(BinaryWriter writer, string value, int maximum)
        {
            string exact = OptionalText(value, maximum);
            byte[] bytes = StrictUtf8.GetBytes(exact);
            writer.Write((ushort)bytes.Length);
            writer.Write(bytes);
        }

        private static string ReadText(BinaryReader reader, int maximum)
        {
            int count = reader.ReadUInt16();
            if (count > MaximumRecordBytes) throw new InvalidDataException();
            byte[] bytes = reader.ReadBytes(count);
            if (bytes.Length != count) throw new EndOfStreamException();
            string value = StrictUtf8.GetString(bytes);
            if (!string.Equals(
                    value,
                    OptionalText(value, maximum),
                    StringComparison.Ordinal))
                throw new InvalidDataException();
            return value;
        }

        private static string OptionalText(string value, int maximum) =>
            PortalText.NormalizeOptional(value, maximum, nameof(value));

        private static void TryProjectLegacy(ZDO zdo, PortalEditEvidence value)
        {
            try
            {
                zdo.Set(ModeKey, (int)PortalMode.StandardPair);
                zdo.Set(SchemaKey, value.Schema);
                zdo.Set(RevisionKey, value.Revision);
                zdo.Set(NetworkKey, value.Network);
                zdo.Set(NameKey, value.Name);
                zdo.Set(OwnerKey, value.Owner);
                zdo.Set(OwnerIdentityKey, value.OwnerIdentity);
                zdo.Set(NetworkKindKey, (int)value.NetworkKind);
                zdo.Set(GroupKey, value.GroupId);
                zdo.Set(DirectionKey, value.Direction);
                zdo.Set(ModeKey, value.Mode);
            }
            catch
            {
                // The authoritative single-record publication already succeeded. Recovery and
                // all Runic readers use that record; projection repair is idempotent on retry.
            }
        }

        private sealed class PortalZdoSnapshot
        {
            internal int Schema;
            internal int Mode;
            internal string Network;
            internal string Name;
            internal long Owner;
            internal string OwnerIdentity;
            internal int NetworkKind;
            internal string Group;
            internal int Direction;
            internal int Revision;

            internal static PortalZdoSnapshot Capture(ZDO zdo) => new PortalZdoSnapshot
            {
                Schema = zdo.GetInt(SchemaKey, 0),
                Mode = zdo.GetInt(ModeKey, 0),
                Network = zdo.GetString(NetworkKey, string.Empty),
                Name = zdo.GetString(NameKey, string.Empty),
                Owner = zdo.GetLong(OwnerKey, 0L),
                OwnerIdentity = zdo.GetString(OwnerIdentityKey, string.Empty),
                NetworkKind = zdo.GetInt(NetworkKindKey, 0),
                Group = zdo.GetString(GroupKey, string.Empty),
                Direction = zdo.GetInt(DirectionKey, 0),
                Revision = zdo.GetInt(RevisionKey, 0)
            };

            internal void Restore(ZDO zdo)
            {
                zdo.Set(ModeKey, (int)PortalMode.StandardPair);
                zdo.Set(SchemaKey, Schema);
                zdo.Set(NetworkKey, Network ?? string.Empty);
                zdo.Set(NameKey, Name ?? string.Empty);
                zdo.Set(OwnerKey, Owner);
                zdo.Set(OwnerIdentityKey, OwnerIdentity ?? string.Empty);
                zdo.Set(NetworkKindKey, NetworkKind);
                zdo.Set(GroupKey, Group ?? string.Empty);
                zdo.Set(DirectionKey, Direction);
                zdo.Set(RevisionKey, Revision);
                zdo.Set(ModeKey, Mode);
            }
        }
    }
}

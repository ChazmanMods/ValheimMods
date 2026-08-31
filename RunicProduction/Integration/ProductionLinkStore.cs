using System;
using System.Security.Cryptography;
using RunicProduction.Contracts;
using UnityEngine;

namespace RunicProduction.Integration
{
    internal sealed class StoredProductionLink
    {
        internal string LinkId;
        internal ProductionLinkRole Role;
        /// <summary>
        /// Runic-owned world-stable identity for the target.  Valheim rewrites ZDOIDs while
        /// loading version-37 worlds, so <see cref="Target"/> is only a current-session cache.
        /// </summary>
        internal string TargetToken;
        internal int TargetPrefabHash;
        internal ZDOID Target;
        internal Vector3 ExpectedPosition;
        internal long OwnerId;
        internal long StationOwnerId;
        internal long TargetOwnerId;
        internal int Revision;
    }

    internal static class ProductionLinkStore
    {
        internal const int LegacySchemaVersion = 1;
        internal const int SchemaVersion = 2;
        private const int AtomicRecordSchemaVersion = 1;
        private const int MaximumAtomicRecordCharacters = 2048;
        private const int MaximumAtomicRecordBytes = 1536;
        internal const string AtomicTombstone = "!";
        private const string InvalidAtomicLinkId = "invalid-atomic-record";
        private static readonly string SchemaKey = Key("schema");
        private static readonly string StationLinkIdKey = Key("station.link-id");
        private static readonly string StationOwnerKey = Key("station.owner");

        internal static StoredProductionLink Load(ZDO stationZdo, ProductionLinkRole role)
        {
            if (stationZdo == null) return null;
            string atomic = stationZdo.GetString(RoleRecordKey(role), string.Empty);
            if (string.Equals(atomic, AtomicTombstone, StringComparison.Ordinal)) return null;
            if (!string.IsNullOrEmpty(atomic))
                return TryParseAtomicRecord(atomic, role, out StoredProductionLink persisted)
                    ? persisted
                    : InvalidAtomicRecord(role);

            int schema = stationZdo.GetInt(SchemaKey, 0);
            if (schema != LegacySchemaVersion && schema != SchemaVersion) return null;
            string prefix = RolePrefix(role);
            ZDOID target = stationZdo.GetZDOID(prefix + ".target");
            string targetToken = schema >= SchemaVersion
                ? stationZdo.GetString(prefix + ".target-token", string.Empty)
                : string.Empty;
            string linkId = stationZdo.GetString(prefix + ".id", string.Empty);
            // A malformed/unknown token is retained in the record so live
            // validation can fail closed.  Treating it as no link would silently re-enable
            // vanilla processing around managed output and recovery state.
            if ((target.IsNone() && string.IsNullOrEmpty(targetToken)) ||
                string.IsNullOrEmpty(linkId)) return null;
            return new StoredProductionLink
            {
                LinkId = linkId,
                Role = role,
                TargetToken = targetToken,
                TargetPrefabHash = schema >= SchemaVersion
                    ? stationZdo.GetInt(prefix + ".target-prefab", 0)
                    : 0,
                Target = target,
                ExpectedPosition = stationZdo.GetVec3(prefix + ".position", Vector3.zero),
                OwnerId = stationZdo.GetLong(prefix + ".owner", 0L),
                StationOwnerId = stationZdo.GetLong(prefix + ".station-owner", 0L),
                TargetOwnerId = stationZdo.GetLong(prefix + ".target-owner", 0L),
                Revision = Math.Max(1, stationZdo.GetInt(prefix + ".revision", 1))
            };
        }

        /// <summary>
        /// Captures the selected role as one canonical consumer-domain publication. The value is
        /// independent of ZDO DataRevision and unrelated ZDO metadata
        /// metadata. Legacy fields are accepted only when they can be upgraded in memory to the
        /// exact schema-2 atomic descriptor; malformed or tokenless legacy evidence fails closed.
        /// </summary>
        internal static bool TryCaptureCanonicalPublication(
            ZDO stationZdo,
            ProductionLinkRole role,
            out string publication,
            out StoredProductionLink link,
            out string failure)
        {
            publication = string.Empty;
            link = null;
            failure = "production-link-record-unavailable";
            if (stationZdo == null || !stationZdo.IsValid() ||
                !Enum.IsDefined(typeof(ProductionLinkRole), role)) return false;

            string raw = stationZdo.GetString(RoleRecordKey(role), string.Empty);
            if (string.Equals(raw, AtomicTombstone, StringComparison.Ordinal))
            {
                publication = AtomicTombstone;
                failure = "ok";
                return true;
            }
            if (!string.IsNullOrEmpty(raw))
            {
                if (!TryParseAtomicRecord(raw, role, out link))
                {
                    failure = "production-link-record-corrupt";
                    return false;
                }
                publication = raw;
                failure = "ok";
                return true;
            }

            StoredProductionLink legacy = Load(stationZdo, role);
            if (legacy == null)
            {
                publication = AtomicTombstone;
                failure = "ok";
                return true;
            }
            try
            {
                publication = SerializeAtomicRecord(legacy);
                link = legacy;
                failure = "ok";
                return true;
            }
            catch
            {
                publication = string.Empty;
                link = null;
                failure = "production-link-legacy-identity-incomplete";
                return false;
            }
        }

        /// <summary>
        /// Publishes only the requested role after an exact role-local CAS. Other link roles are
        /// never read, cleared, or rewritten by this method. The caller publishes the persistent
        /// epoch/sequence marker only after this exact role readback succeeds.
        /// </summary>
        internal static bool TryPublishCanonicalPublication(
            ZDO stationZdo,
            ProductionLinkRole role,
            string expectedPublication,
            string desiredPublication,
            out string failure)
        {
            failure = "production-link-publication-invalid";
            if (stationZdo == null || !stationZdo.IsValid() ||
                !IsCanonicalPublication(role, expectedPublication) ||
                !IsCanonicalPublication(role, desiredPublication)) return false;
            if (!TryCaptureCanonicalPublication(
                    stationZdo, role, out string current, out _, out failure) ||
                !string.Equals(current, expectedPublication, StringComparison.Ordinal))
            {
                failure = "production-link-publication-before-changed";
                return false;
            }

            try
            {
                PublishAtomicRecord(stationZdo, role, desiredPublication);
                if (!TryCaptureCanonicalPublication(
                        stationZdo, role, out string readback, out _, out failure) ||
                    !string.Equals(readback, desiredPublication, StringComparison.Ordinal))
                {
                    failure = "production-link-publication-readback";
                    return false;
                }
                ClearLegacyFields(stationZdo, role);
                if (!TryCaptureCanonicalPublication(
                        stationZdo, role, out readback, out _, out failure) ||
                    !string.Equals(readback, desiredPublication, StringComparison.Ordinal))
                {
                    failure = "production-link-publication-cleanup-changed";
                    return false;
                }
                failure = "ok";
                return true;
            }
            catch
            {
                failure = "production-link-publication-fault";
                return false;
            }
        }

        internal static string CreateCanonicalPublication(StoredProductionLink link) =>
            link == null ? AtomicTombstone : SerializeAtomicRecord(link);

        internal static bool IsCanonicalPublication(
            ProductionLinkRole role,
            string publication)
        {
            if (string.Equals(publication, AtomicTombstone, StringComparison.Ordinal)) return true;
            return TryParseAtomicRecord(publication, role, out _);
        }

        internal static StoredProductionLink Save(
            ZDO stationZdo,
            ProductionLinkRole role,
            ZDOID target,
            Vector3 targetPosition,
            long actorId,
            long stationOwnerId,
            long targetOwnerId)
        {
            if (stationZdo == null) throw new ArgumentNullException(nameof(stationZdo));
            if (target.IsNone()) throw new ArgumentException("A target ZDO is required.", nameof(target));
            StoredProductionLink previous = Load(stationZdo, role);
            StoredProductionLink next = CreateDetached(
                role,
                target,
                targetPosition,
                actorId,
                stationOwnerId,
                targetOwnerId,
                previous);
            string encoded = SerializeAtomicRecord(next);

            // Every write before the record publication is ancillary. A crash leaves the
            // previously published record (or legacy record) authoritative. The one bounded
            // descriptor Set below is the only A -> B identity switch.
            stationZdo.Set(SchemaKey, SchemaVersion);
            if (string.IsNullOrEmpty(stationZdo.GetString(StationLinkIdKey, string.Empty)))
                stationZdo.Set(StationLinkIdKey, Guid.NewGuid().ToString("N"));
            stationZdo.Set(StationOwnerKey, actorId);
            PublishAtomicRecord(stationZdo, role, encoded);
            StoredProductionLink published = Load(stationZdo, role);
            if (!SameCompleteRecord(published, next))
                throw new InvalidOperationException(
                    "The atomic production link did not round-trip exactly.");

            // Old singleton fields are compatibility input only. Once the descriptor is
            // visible they are inert, so cleanup can be interrupted without changing Load.
            ClearLegacyFields(stationZdo, role);
            published = Load(stationZdo, role);
            if (!SameCompleteRecord(published, next))
                throw new InvalidOperationException(
                    "The atomic production link changed during cleanup.");
            return published;
        }

        /// <summary>
        /// Creates one exact link identity without publishing it into the legacy singleton role
        /// keys. Multi-destination catalogs use this for a new relation; refresh keeps the
        /// existing catalog link unchanged and advances only its signed plan revision.
        /// </summary>
        internal static StoredProductionLink CreateDetached(
            ProductionLinkRole role,
            ZDOID target,
            Vector3 targetPosition,
            long actorId,
            long stationOwnerId,
            long targetOwnerId,
            StoredProductionLink previous = null)
        {
            if (target.IsNone())
                throw new ArgumentException("A target ZDO is required.", nameof(target));
            if (role != ProductionLinkRole.Input && role != ProductionLinkRole.Fuel &&
                role != ProductionLinkRole.Output && role != ProductionLinkRole.Replenishment)
                throw new ArgumentOutOfRangeException(nameof(role));
            if (!ProductionEndpointIdentity.TryCaptureCurrentTarget(
                    target,
                    out string targetToken,
                    out int targetPrefabHash,
                    out Vector3 rootPosition,
                    out string identityFailure))
                throw new InvalidOperationException(
                    "The selected container has no persistent world identity: " + identityFailure);
            int revision = previous == null ? 1 : checked(previous.Revision + 1);
            string linkId = previous?.LinkId;
            if (string.Equals(linkId, InvalidAtomicLinkId, StringComparison.Ordinal))
                linkId = string.Empty;
            if (string.IsNullOrEmpty(linkId)) linkId = Guid.NewGuid().ToString("N");
            return new StoredProductionLink
            {
                LinkId = linkId,
                Role = role,
                TargetToken = targetToken,
                TargetPrefabHash = targetPrefabHash,
                Target = target,
                // The component transform can be a prefab child. Persist the authoritative
                // ZDO root so validation survives prefab hierarchy changes and reloads.
                ExpectedPosition = rootPosition,
                OwnerId = actorId,
                StationOwnerId = stationOwnerId,
                TargetOwnerId = targetOwnerId,
                Revision = revision
            };
        }

        /// <summary>
        /// Refreshes only the restart-unstable ZDOID cache after a stable token resolves. The
        /// signed link identity and all authorization evidence remain unchanged.
        /// </summary>
        internal static bool TryRefreshResolvedTargetCache(
            ZDO stationZdo,
            StoredProductionLink expected,
            ZDO targetZdo,
            out StoredProductionLink refreshed)
        {
            refreshed = null;
            if (stationZdo == null || expected == null || targetZdo == null ||
                targetZdo.m_uid.IsNone() ||
                !ProductionEndpointIdentity.IsCanonicalToken(expected.TargetToken)) return false;
            StoredProductionLink current = Load(stationZdo, expected.Role);
            if (!SameStableIdentity(current, expected) ||
                current.TargetPrefabHash == 0 ||
                current.TargetPrefabHash != targetZdo.GetPrefab()) return false;
            if (current.Target != targetZdo.m_uid)
            {
                var next = Copy(current);
                next.Target = targetZdo.m_uid;
                PublishAtomicRecord(
                    stationZdo, expected.Role, SerializeAtomicRecord(next));
                ClearLegacyFields(stationZdo, expected.Role);
            }
            refreshed = Load(stationZdo, expected.Role);
            return SameStableIdentity(refreshed, expected) &&
                   refreshed.Target == targetZdo.m_uid;
        }

        internal static void Clear(ZDO stationZdo, ProductionLinkRole role)
        {
            if (stationZdo == null) return;
            // The tombstone is a complete, atomic publication of absence. It             // remains after cleanup so stale legacy proof can never be resurrected by a later
            // partially completed fresh Save.
            PublishAtomicRecord(stationZdo, role, AtomicTombstone);
            ClearLegacyFields(stationZdo, role);
        }

        /// <summary>
        /// Completes a schema-1 link after the runtime has proved one unique legacy target.
        /// This is an identity migration, not a relink: the signed link ID, revision, expected
        /// position, actors, and fault evidence remain unchanged.
        /// </summary>
        internal static bool TryUpgradeResolvedTarget(
            ZDO stationZdo,
            StoredProductionLink expected,
            ZDO targetZdo,
            string targetToken,
            int targetPrefabHash,
            out StoredProductionLink upgraded)
        {
            upgraded = null;
            if (stationZdo == null || expected == null || targetZdo == null ||
                targetZdo.m_uid.IsNone() || targetPrefabHash == 0 ||
                !ProductionEndpointIdentity.IsCanonicalToken(targetToken)) return false;
            StoredProductionLink current = Load(stationZdo, expected.Role);
            if (!SameStoredIdentity(current, expected) ||
                !string.IsNullOrEmpty(current.TargetToken)) return false;

            var next = Copy(current);
            next.TargetToken = targetToken;
            next.TargetPrefabHash = targetPrefabHash;
            next.Target = targetZdo.m_uid;
            string encoded = SerializeAtomicRecord(next);
            stationZdo.Set(SchemaKey, SchemaVersion);
            // The complete descriptor is the authoritative identity switch. A crash before
            // this Set leaves the retryable legacy record intact; a crash after it sees the
            // token, prefab, cache, position, owners, and revision together.
            PublishAtomicRecord(stationZdo, expected.Role, encoded);
            upgraded = Load(stationZdo, expected.Role);
            if (!SameCompleteRecord(upgraded, next)) return false;
            ClearLegacyFields(stationZdo, expected.Role);
            upgraded = Load(stationZdo, expected.Role);
            return SameCompleteRecord(upgraded, next);
        }

        internal static string StationLinkId(ZDO zdo)
        {
            if (zdo == null) return string.Empty;
            string value = zdo.GetString(StationLinkIdKey, string.Empty);
            if (!string.IsNullOrEmpty(value)) return value;
            value = Guid.NewGuid().ToString("N");
            zdo.Set(StationLinkIdKey, value);
            zdo.Set(SchemaKey, SchemaVersion);
            return value;
        }

        private static string RolePrefix(ProductionLinkRole role)
        {
            switch (role)
            {
                case ProductionLinkRole.Input: return Key("input");
                case ProductionLinkRole.Fuel: return Key("fuel");
                case ProductionLinkRole.Output: return Key("output");
                case ProductionLinkRole.Replenishment: return Key("replenishment");
                default: throw new ArgumentOutOfRangeException(nameof(role));
            }
        }

        internal static string RoleRecordKey(ProductionLinkRole role) =>
            RolePrefix(role) + ".record";

        private static void PublishAtomicRecord(
            ZDO stationZdo,
            ProductionLinkRole role,
            string encoded) =>
            stationZdo.Set(RoleRecordKey(role), encoded);

        private static void ClearLegacyFields(ZDO stationZdo, ProductionLinkRole role)
        {
            string prefix = RolePrefix(role);
            stationZdo.Set(prefix + ".id", string.Empty);
            stationZdo.Set(prefix + ".target-token", string.Empty);
            stationZdo.RemoveInt(prefix + ".target-prefab");
            stationZdo.Set(prefix + ".target", ZDOID.None);
            stationZdo.RemoveVec3(prefix + ".position");
            stationZdo.Set(prefix + ".owner", 0L);
            stationZdo.Set(prefix + ".station-owner", 0L);
            stationZdo.Set(prefix + ".target-owner", 0L);
            stationZdo.RemoveInt(prefix + ".revision");
        }

        internal static string SerializeAtomicRecord(StoredProductionLink link)
        {
            if (!IsValidAtomicRecord(link))
                throw new InvalidOperationException(
                    "A complete stable production link is required for atomic publication.");
            var payload = new ZPackage();
            payload.Write(AtomicRecordSchemaVersion);
            payload.Write((int)link.Role);
            payload.Write(link.LinkId);
            payload.Write(link.TargetToken);
            payload.Write(link.TargetPrefabHash);
            payload.Write(link.Target);
            payload.Write(link.ExpectedPosition);
            payload.Write(link.OwnerId);
            payload.Write(link.StationOwnerId);
            payload.Write(link.TargetOwnerId);
            payload.Write(link.Revision);
            byte[] bytes = payload.GetArray();
            if (bytes.Length == 0 || bytes.Length > MaximumAtomicRecordBytes)
                throw new InvalidOperationException("The production link record exceeds its bound.");
            var envelope = new ZPackage();
            envelope.Write(bytes);
            envelope.Write(ComputeDigest(bytes));
            string encoded = Convert.ToBase64String(envelope.GetArray());
            if (encoded.Length > MaximumAtomicRecordCharacters)
                throw new InvalidOperationException("The production link record exceeds its encoded bound.");
            return encoded;
        }

        internal static bool TryParseAtomicRecord(
            string encoded,
            ProductionLinkRole expectedRole,
            out StoredProductionLink link)
        {
            link = null;
            if (string.IsNullOrEmpty(encoded) ||
                string.Equals(encoded, AtomicTombstone, StringComparison.Ordinal) ||
                encoded.Length > MaximumAtomicRecordCharacters) return false;
            try
            {
                byte[] envelopeBytes = Convert.FromBase64String(encoded);
                if (envelopeBytes.Length == 0 || envelopeBytes.Length > MaximumAtomicRecordBytes)
                    return false;
                var envelope = new ZPackage(envelopeBytes);
                byte[] payloadBytes = envelope.ReadByteArray();
                byte[] digest = envelope.ReadByteArray();
                if (envelope.GetPos() != envelope.Size() || payloadBytes == null ||
                    payloadBytes.Length == 0 || payloadBytes.Length > MaximumAtomicRecordBytes ||
                    !DigestEquals(digest, ComputeDigest(payloadBytes))) return false;
                var payload = new ZPackage(payloadBytes);
                if (payload.ReadInt() != AtomicRecordSchemaVersion) return false;
                var parsed = new StoredProductionLink
                {
                    Role = (ProductionLinkRole)payload.ReadInt(),
                    LinkId = payload.ReadString(),
                    TargetToken = payload.ReadString(),
                    TargetPrefabHash = payload.ReadInt(),
                    Target = payload.ReadZDOID(),
                    ExpectedPosition = payload.ReadVector3(),
                    OwnerId = payload.ReadLong(),
                    StationOwnerId = payload.ReadLong(),
                    TargetOwnerId = payload.ReadLong(),
                    Revision = payload.ReadInt()
                };
                if (payload.GetPos() != payload.Size() || parsed.Role != expectedRole ||
                    !IsValidAtomicRecord(parsed) ||
                    !string.Equals(SerializeAtomicRecord(parsed), encoded,
                        StringComparison.Ordinal)) return false;
                link = parsed;
                return true;
            }
            catch
            {
                link = null;
                return false;
            }
        }

        private static bool IsValidAtomicRecord(StoredProductionLink link) =>
            link != null &&
            (link.Role == ProductionLinkRole.Input || link.Role == ProductionLinkRole.Fuel ||
             link.Role == ProductionLinkRole.Output ||
             link.Role == ProductionLinkRole.Replenishment) &&
            !string.IsNullOrEmpty(link.LinkId) && link.LinkId.Length <= 128 &&
            ProductionEndpointIdentity.IsCanonicalToken(link.TargetToken) &&
            link.TargetPrefabHash != 0 && !link.Target.IsNone() &&
            ValheimAccess.IsFinite(link.ExpectedPosition) && link.Revision >= 1;

        private static StoredProductionLink InvalidAtomicRecord(ProductionLinkRole role) =>
            new StoredProductionLink
            {
                LinkId = InvalidAtomicLinkId,
                Role = role,
                TargetToken = "invalid",
                TargetPrefabHash = 0,
                Target = ZDOID.None,
                ExpectedPosition = Vector3.zero,
                Revision = 1
            };

        private static StoredProductionLink Copy(StoredProductionLink link) =>
            link == null
                ? null
                : new StoredProductionLink
                {
                    LinkId = link.LinkId,
                    Role = link.Role,
                    TargetToken = link.TargetToken,
                    TargetPrefabHash = link.TargetPrefabHash,
                    Target = link.Target,
                    ExpectedPosition = link.ExpectedPosition,
                    OwnerId = link.OwnerId,
                    StationOwnerId = link.StationOwnerId,
                    TargetOwnerId = link.TargetOwnerId,
                    Revision = link.Revision
                };

        private static byte[] ComputeDigest(byte[] bytes)
        {
            using SHA256 algorithm = SHA256.Create();
            return algorithm.ComputeHash(bytes);
        }

        private static bool DigestEquals(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            int difference = 0;
            for (int index = 0; index < left.Length; index++)
                difference |= left[index] ^ right[index];
            return difference == 0;
        }

        private static bool SameStoredIdentity(
            StoredProductionLink left,
            StoredProductionLink right) =>
            left != null && right != null &&
            left.Role == right.Role &&
            left.Revision == right.Revision &&
            left.Target == right.Target &&
            string.Equals(left.LinkId, right.LinkId, StringComparison.Ordinal);

        private static bool SameStableIdentity(
            StoredProductionLink left,
            StoredProductionLink right) =>
            left != null && right != null &&
            left.Role == right.Role &&
            left.Revision == right.Revision &&
            left.TargetPrefabHash == right.TargetPrefabHash &&
            ProductionEndpointIdentity.IsCanonicalToken(left.TargetToken) &&
            string.Equals(left.TargetToken, right.TargetToken, StringComparison.Ordinal) &&
            string.Equals(left.LinkId, right.LinkId, StringComparison.Ordinal);

        private static bool SameCompleteRecord(
            StoredProductionLink left,
            StoredProductionLink right) =>
            SameStableIdentity(left, right) &&
            left.Target == right.Target &&
            left.ExpectedPosition == right.ExpectedPosition &&
            left.OwnerId == right.OwnerId &&
            left.StationOwnerId == right.StationOwnerId &&
            left.TargetOwnerId == right.TargetOwnerId;

        private static string Key(string localName) => Plugin.ModuleId + "." + localName;
    }
}

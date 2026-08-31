using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using RunicProduction.Contracts;
using RunicProduction.Core;

namespace RunicProduction.Integration
{
    /// <summary>One bounded atomic publication per station and role.</summary>
    internal static class ProductionRoleLinkCatalogStore
    {
        private const int SchemaVersion = 1;
        private const int MaximumEncodedCharacters = 49152;
        private const int MaximumPayloadBytes = 32768;

        internal static StoredRecordState Read(
            ZDO stationZdo,
            ProductionLinkRole role,
            out ProductionRoleLinkCatalog catalog)
        {
            catalog = null;
            if (stationZdo == null || !Supported(role)) return StoredRecordState.Invalid;
            string encoded = stationZdo.GetString(Key(role), string.Empty);
            if (string.IsNullOrEmpty(encoded)) return StoredRecordState.Absent;
            if (encoded.Length > MaximumEncodedCharacters) return StoredRecordState.Invalid;
            try
            {
                byte[] envelopeBytes = Convert.FromBase64String(encoded);
                if (envelopeBytes.Length == 0 || envelopeBytes.Length > MaximumPayloadBytes)
                    return StoredRecordState.Invalid;
                var envelope = new ZPackage(envelopeBytes);
                byte[] payloadBytes = envelope.ReadByteArray();
                byte[] digest = envelope.ReadByteArray();
                if (envelope.GetPos() != envelope.Size() || payloadBytes == null ||
                    payloadBytes.Length == 0 || payloadBytes.Length > MaximumPayloadBytes ||
                    !DigestEquals(digest, Digest(payloadBytes))) return StoredRecordState.Invalid;
                var payload = new ZPackage(payloadBytes);
                if (payload.ReadInt() != SchemaVersion ||
                    payload.ReadInt() != (int)role) return StoredRecordState.Invalid;
                int revision = payload.ReadInt();
                int count = payload.ReadInt();
                if (revision < 1 || count < 0 ||
                    count > ProductionRoleLinkCatalog.HardMaximumLinks)
                    return StoredRecordState.Invalid;
                var links = new List<StoredProductionLink>(count);
                for (int index = 0; index < count; index++)
                {
                    string publication = payload.ReadString();
                    if (!ProductionLinkStore.TryParseAtomicRecord(
                            publication, role, out StoredProductionLink link))
                        return StoredRecordState.Invalid;
                    links.Add(link);
                }
                if (payload.GetPos() != payload.Size()) return StoredRecordState.Invalid;
                catalog = new ProductionRoleLinkCatalog(role, revision, links);
                return string.Equals(Serialize(catalog), encoded, StringComparison.Ordinal)
                    ? StoredRecordState.Valid
                    : StoredRecordState.Invalid;
            }
            catch
            {
                catalog = null;
                return StoredRecordState.Invalid;
            }
        }

        internal static StoredRecordState ReadWithLegacy(
            ZDO stationZdo,
            ProductionLinkRole role,
            out ProductionRoleLinkCatalog catalog)
        {
            StoredRecordState state = Read(stationZdo, role, out catalog);
            if (state != StoredRecordState.Absent) return state;
            StoredProductionLink legacy = ProductionLinkStore.Load(stationZdo, role);
            try
            {
                catalog = legacy == null
                    ? ProductionRoleLinkCatalogPolicy.Empty(role)
                    : new ProductionRoleLinkCatalog(role, 1, new[] { legacy });
                return StoredRecordState.Valid;
            }
            catch
            {
                catalog = null;
                return StoredRecordState.Invalid;
            }
        }

        internal static bool Publish(
            ZDO stationZdo,
            ProductionRoleLinkCatalog expected,
            ProductionRoleLinkCatalog catalog)
        {
            if (stationZdo == null || expected == null || catalog == null ||
                expected.Role != catalog.Role) return false;
            if (ReadWithLegacy(
                    stationZdo,
                    catalog.Role,
                    out ProductionRoleLinkCatalog current) != StoredRecordState.Valid ||
                !string.Equals(
                    Serialize(current),
                    Serialize(expected),
                    StringComparison.Ordinal)) return false;
            string priorCanonical = Serialize(expected);
            string encoded = Serialize(catalog);
            stationZdo.Set(Key(catalog.Role), encoded);
            if (Read(stationZdo, catalog.Role, out ProductionRoleLinkCatalog exact) !=
                    StoredRecordState.Valid ||
                !string.Equals(Serialize(exact), encoded, StringComparison.Ordinal))
            {
                stationZdo.Set(Key(catalog.Role), priorCanonical);
                if (Read(stationZdo, catalog.Role, out exact) != StoredRecordState.Valid ||
                    !string.Equals(
                        Serialize(exact), priorCanonical, StringComparison.Ordinal))
                    ProductionDiagnostics.Warning(
                        "Role-link catalog rollback could not be proven exactly; the " +
                        "operation was not reported as successful.");
                return false;
            }
            // The catalog is now authoritative. Retire the old singleton only after readback.
            ProductionLinkStore.Clear(stationZdo, catalog.Role);
            if (Read(stationZdo, catalog.Role, out exact) == StoredRecordState.Valid &&
                string.Equals(Serialize(exact), encoded, StringComparison.Ordinal)) return true;
            stationZdo.Set(Key(catalog.Role), priorCanonical);
            // The caller never reports success on this path. Exact readback distinguishes a
            // proven semantic rollback from an indeterminate external write race for diagnostics
            // and future retries, while the public result remains fail closed.
            bool rollbackProven =
                Read(stationZdo, catalog.Role, out exact) == StoredRecordState.Valid &&
                string.Equals(Serialize(exact), priorCanonical, StringComparison.Ordinal);
            if (!rollbackProven)
                ProductionDiagnostics.Warning(
                    "Role-link catalog rollback could not be proven exactly; the operation " +
                    "was not reported as successful.");
            return false;
        }

        internal static string Serialize(ProductionRoleLinkCatalog catalog)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            var payload = new ZPackage();
            payload.Write(SchemaVersion);
            payload.Write((int)catalog.Role);
            payload.Write(catalog.Revision);
            payload.Write(catalog.Links.Count);
            foreach (StoredProductionLink link in catalog.Links)
                payload.Write(ProductionLinkStore.CreateCanonicalPublication(link));
            byte[] payloadBytes = payload.GetArray();
            if (payloadBytes.Length == 0 || payloadBytes.Length > MaximumPayloadBytes)
                throw new InvalidOperationException("The role-link publication exceeds its bound.");
            var envelope = new ZPackage();
            envelope.Write(payloadBytes);
            envelope.Write(Digest(payloadBytes));
            string encoded = Convert.ToBase64String(envelope.GetArray());
            if (encoded.Length > MaximumEncodedCharacters)
                throw new InvalidOperationException("The role-link publication exceeds its encoded bound.");
            return encoded;
        }

        internal static string StorageKey(ProductionLinkRole role) => Key(role);

        private static bool Supported(ProductionLinkRole role) =>
            role == ProductionLinkRole.Input || role == ProductionLinkRole.Fuel ||
            role == ProductionLinkRole.Output || role == ProductionLinkRole.Replenishment;

        private static string Key(ProductionLinkRole role) =>
            Plugin.ModuleId + "." + role.ToString().ToLowerInvariant() + ".links.record";

        private static byte[] Digest(byte[] bytes)
        {
            using SHA256 algorithm = SHA256.Create();
            return algorithm.ComputeHash(bytes);
        }

        private static bool DigestEquals(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            int difference = 0;
            for (int index = 0; index < left.Length; index++) difference |= left[index] ^ right[index];
            return difference == 0;
        }
    }
}

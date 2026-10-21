using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using Runic.Foundation.Transactions;
using RunicTransactions.Contracts;

namespace RunicTransactions.Valheim
{
    /// <summary>
    /// Canonical shared-container endpoint schema used by every Foundation consumer. Mutations are
    /// exact complete Inventory.Save payloads derived by an authoritative server consumer. The
    /// semantic revision is the same complete-payload SHA-256 and therefore excludes ZDO claim
    /// metadata while detecting every inventory-domain change.
    /// </summary>
    public static class DurableValheimContainerComposite
    {
        public const string EndpointDomainId = "valheim.container.inventory";
        public const int EndpointDomainSchemaVersion = 1;
        public const string StableEndpointPrefix = ValheimIdentityIds.WorldObjectEndpointPrefix;

        public static DurableCompositeEndpointDomainDescriptor DomainDescriptor { get; } =
            new DurableCompositeEndpointDomainDescriptor(
                EndpointDomainId,
                EndpointDomainSchemaVersion,
                StableEndpointPrefix);

        public static bool TryCapture(
            Container container,
            out EndpointId endpointId,
            out byte[] exactInventory,
            out string fingerprint,
            out string semanticRevision,
            out string failureCode)
        {
            endpointId = default;
            exactInventory = Array.Empty<byte>();
            fingerprint = string.Empty;
            semanticRevision = string.Empty;
            failureCode = "composite.container.unavailable";
            try
            {
                if (!ValheimContainerCompositeEndpointStore.TryResolve(
                        container, out WorldObjectToken token, out Inventory inventory, out _))
                    return false;
                byte[] payload = ValheimContainerCompositeEndpointStore.Save(inventory);
                if (!ValheimContainerCompositeEndpointStore.TryRoundTrip(inventory, payload))
                {
                    failureCode = "composite.container.non-roundtrip";
                    return false;
                }
                endpointId = ValheimIdentityIds.WorldObjectEndpoint(token.Value);
                exactInventory = payload;
                fingerprint = DurableCompositeCodec.Sha256Hex(payload);
                semanticRevision = fingerprint;
                failureCode = "composite.container.ready";
                return true;
            }
            catch
            {
                endpointId = default;
                exactInventory = Array.Empty<byte>();
                fingerprint = string.Empty;
                semanticRevision = string.Empty;
                failureCode = "composite.container.capture-failed";
                return false;
            }
        }

        public static bool TryCreateEndpointIntent(
            Container container,
            byte[] exactAfterInventory,
            out DurableCompositeEndpointIntent endpointIntent,
            out string failureCode)
        {
            endpointIntent = null;
            if (!TryCapture(
                    container,
                    out EndpointId endpoint,
                    out _,
                    out string before,
                    out string semanticRevision,
                    out failureCode)) return false;
            try
            {
                Inventory inventory = container.GetInventory();
                byte[] after = exactAfterInventory == null
                    ? Array.Empty<byte>()
                    : (byte[])exactAfterInventory.Clone();
                if (after.Length < 1 || after.Length > DurableCompositeLimits.MaximumIntentBytes ||
                    !ValheimContainerCompositeEndpointStore.TryRoundTrip(inventory, after))
                {
                    failureCode = "composite.container.after-noncanonical";
                    return false;
                }
                string afterHash = DurableCompositeCodec.Sha256Hex(after);
                endpointIntent = new DurableCompositeEndpointIntent(
                    endpoint,
                    before,
                    afterHash,
                    semanticRevision,
                    after);
                failureCode = "composite.container.intent-ready";
                return true;
            }
            catch
            {
                endpointIntent = null;
                failureCode = "composite.container.intent-failed";
                return false;
            }
        }
    }

    internal sealed class ValheimContainerCompositeEndpointStore :
        IDurableCompositeEndpointStore
    {
        private const string ClaimStorageKey =
            "runic.transactions.composite.container-claim.v1";
        private const string AppliedMarkerStorageKey =
            "runic.transactions.composite.container-applied.v1";
        private const int ClaimMagic = 0x52444343; // RDCC
        private const int MaximumClaimBytes = 4096;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private static readonly MethodInfo InventoryChangedMethod =
            AccessTools.Method(typeof(Inventory), "Changed");

        public DurableCompositeEndpointReadState Read(
            EndpointId stableEndpointId,
            out DurableCompositeEndpointSnapshot snapshot,
            out string failureCode)
        {
            snapshot = null;
            failureCode = "composite.container.unavailable";
            if (!TryResolve(
                    stableEndpointId,
                    out Container container,
                    out _,
                    out Inventory inventory,
                    out ZDO zdo))
                return DurableCompositeEndpointReadState.Missing;
            try
            {
                if (!TryReadClaim(zdo, out DurableCompositeEndpointClaim claim, out bool corrupt))
                {
                    failureCode = "composite.container.claim-corrupt";
                    return corrupt
                        ? DurableCompositeEndpointReadState.Corrupt
                        : DurableCompositeEndpointReadState.Unavailable;
                }
                if (!TryReadAppliedMarker(
                        zdo,
                        out string appliedWorldEpoch,
                        out long appliedCommitSequence))
                {
                    failureCode = "composite.container.applied-marker-corrupt";
                    return DurableCompositeEndpointReadState.Corrupt;
                }
                byte[] payload = Save(inventory);
                if (!TryRoundTrip(inventory, payload))
                {
                    failureCode = "composite.container.non-roundtrip";
                    return DurableCompositeEndpointReadState.Corrupt;
                }
                string fingerprint = DurableCompositeCodec.Sha256Hex(payload);
                ZNetView view = WorldObjectIdentity.View(container);
                bool server = ZNet.instance != null && ZNet.instance.IsServer();
                bool owner = server && view != null && view.IsValid() && view.IsOwner() &&
                             zdo.GetOwner() == ZNet.GetUID();
                bool stable = WorldObjectIdentity.Resolve(
                                  stableEndpointId.Value.Substring(
                                      DurableValheimContainerComposite.StableEndpointPrefix.Length),
                                  out ZDO resolved) == WorldObjectIdentityStatus.Ready &&
                              ReferenceEquals(resolved, zdo);
                snapshot = new DurableCompositeEndpointSnapshot(
                    stableEndpointId,
                    fingerprint,
                    fingerprint,
                    stable,
                    inventory != null,
                    owner,
                    !zdo.IsValid() || view == null || !view.IsValid(),
                    claim,
                    appliedWorldEpoch,
                    appliedCommitSequence);
                failureCode = "composite.container.ready";
                return DurableCompositeEndpointReadState.Ready;
            }
            catch
            {
                snapshot = null;
                failureCode = "composite.container.read-failed";
                return DurableCompositeEndpointReadState.Unavailable;
            }
        }

        public bool TryAcquireClaim(
            DurableCompositeEndpointClaim claim,
            out string failureCode)
        {
            failureCode = "composite.container.claim-failed";
            if (claim == null ||
                !string.Equals(
                    claim.EndpointDomainId,
                    DurableValheimContainerComposite.EndpointDomainId,
                    StringComparison.Ordinal) ||
                !TryResolve(
                    claim.StableEndpointId,
                    out Container container,
                    out _,
                    out _,
                    out ZDO zdo)) return false;
            try
            {
                ZNetView view = WorldObjectIdentity.View(container);
                if (ZNet.instance == null || !ZNet.instance.IsServer() ||
                    view == null || !view.IsValid() || !view.IsOwner() ||
                    zdo.GetOwner() != ZNet.GetUID())
                {
                    failureCode = "composite.container.authority-changed";
                    return false;
                }
                if (!TryReadClaim(zdo, out DurableCompositeEndpointClaim existing, out bool corrupt) ||
                    corrupt)
                {
                    failureCode = "composite.container.claim-corrupt";
                    return false;
                }
                if (existing != null)
                {
                    bool exact = claim.MatchesExact(existing);
                    failureCode = exact
                        ? "composite.container.claim-replay"
                        : "composite.container.claim-conflict";
                    return exact;
                }
                string encoded = EncodeClaim(claim);
                zdo.Set(ClaimStorageKey, encoded);
                if (!string.Equals(
                        zdo.GetString(ClaimStorageKey, string.Empty),
                        encoded,
                        StringComparison.Ordinal) ||
                    !TryReadClaim(zdo, out DurableCompositeEndpointClaim readback, out corrupt) ||
                    corrupt || readback == null || !claim.MatchesExact(readback))
                {
                    failureCode = "composite.container.claim-readback-failed";
                    return false;
                }
                failureCode = "composite.container.claimed";
                return true;
            }
            catch
            {
                failureCode = "composite.container.claim-exception";
                return false;
            }
        }

        public bool TryApplyAfter(
            DurableCompositeMutationContext operation,
            DurableCompositeEndpointIntent endpoint,
            DurableCompositeEndpointClaim exactClaim,
            out string failureCode)
        {
            failureCode = "composite.container.apply-failed";
            if (operation == null || endpoint == null || exactClaim == null ||
                !string.Equals(
                    operation.EndpointDomainId,
                    DurableValheimContainerComposite.EndpointDomainId,
                    StringComparison.Ordinal) ||
                operation.EndpointDomainSchemaVersion !=
                    DurableValheimContainerComposite.EndpointDomainSchemaVersion ||
                !string.Equals(
                    operation.OwnerModuleId, exactClaim.OwnerModuleId, StringComparison.Ordinal) ||
                operation.OperationId != exactClaim.OperationId ||
                !CompositeValidation.SameIdentity(
                    operation.ActorIdentity, exactClaim.ActorIdentity) ||
                !string.Equals(
                    operation.IntentHash, exactClaim.IntentHash, StringComparison.Ordinal) ||
                endpoint.StableEndpointId != exactClaim.StableEndpointId ||
                !TryResolve(
                    endpoint.StableEndpointId,
                    out Container container,
                    out _,
                    out Inventory inventory,
                    out ZDO zdo)) return false;
            byte[] after = endpoint.ExactMutation;
            if (after.Length < 1 ||
                !string.Equals(
                    DurableCompositeCodec.Sha256Hex(after),
                    endpoint.AfterFingerprint,
                    StringComparison.Ordinal) ||
                !TryRoundTrip(inventory, after))
            {
                failureCode = "composite.container.after-noncanonical";
                return false;
            }
            if (Read(
                    endpoint.StableEndpointId,
                    out DurableCompositeEndpointSnapshot before,
                    out _) != DurableCompositeEndpointReadState.Ready ||
                !before.StableIdentityCurrent || !before.Synchronized || !before.ServerOwned ||
                before.DestroyPending || before.Claim == null ||
                !exactClaim.MatchesExact(before.Claim) ||
                !string.Equals(before.Fingerprint, endpoint.BeforeFingerprint, StringComparison.Ordinal) ||
                !string.Equals(
                    before.SemanticRevision,
                    endpoint.PreparedSemanticRevision,
                    StringComparison.Ordinal))
            {
                failureCode = "composite.container.before-changed";
                return false;
            }

            byte[] rollback;
            string rollbackMarker;
            try { rollback = Save(inventory); }
            catch
            {
                failureCode = "composite.container.rollback-unavailable";
                return false;
            }
            try { rollbackMarker = zdo.GetString(AppliedMarkerStorageKey, string.Empty); }
            catch
            {
                failureCode = "composite.container.marker-rollback-unavailable";
                return false;
            }
            string appliedMarker = EncodeAppliedMarker(
                operation.WorldEpoch, operation.CommitSequence);
            Action changed = inventory.m_onChanged;
            inventory.m_onChanged = null;
            try
            {
                inventory.Load(new ZPackage(after));
                zdo.Set(AppliedMarkerStorageKey, appliedMarker);
                if (!string.Equals(
                        DurableCompositeCodec.Sha256Hex(Save(inventory)),
                        endpoint.AfterFingerprint,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        zdo.GetString(AppliedMarkerStorageKey, string.Empty),
                        appliedMarker,
                        StringComparison.Ordinal))
                    throw new InvalidOperationException("The exact after image did not read back.");
            }
            catch
            {
                try
                {
                    inventory.Load(new ZPackage(rollback));
                    zdo.Set(AppliedMarkerStorageKey, rollbackMarker);
                    if (!Exact(Save(inventory), rollback))
                        throw new InvalidOperationException("Rollback did not read back exactly.");
                    if (!string.Equals(
                            zdo.GetString(AppliedMarkerStorageKey, string.Empty),
                            rollbackMarker,
                            StringComparison.Ordinal))
                        throw new InvalidOperationException("Marker rollback did not read back exactly.");
                }
                catch
                {
                    failureCode = "composite.container.rollback-failed";
                    return false;
                }
                failureCode = "composite.container.apply-rejected";
                return false;
            }
            finally
            {
                inventory.m_onChanged = changed;
            }
            try
            {
                InventoryChangedMethod?.Invoke(inventory, Array.Empty<object>());
                if (Read(
                        endpoint.StableEndpointId,
                        out DurableCompositeEndpointSnapshot readback,
                        out _) != DurableCompositeEndpointReadState.Ready ||
                    readback.Claim == null || !exactClaim.MatchesExact(readback.Claim) ||
                    !string.Equals(
                        readback.Fingerprint,
                        endpoint.AfterFingerprint,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        readback.SemanticRevision,
                        endpoint.AfterSemanticRevision,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        readback.AppliedWorldEpoch,
                        operation.WorldEpoch,
                        StringComparison.Ordinal) ||
                    readback.AppliedCommitSequence != operation.CommitSequence)
                {
                    failureCode = "composite.container.after-publication-changed";
                    return false;
                }
                failureCode = "composite.container.applied";
                return true;
            }
            catch
            {
                failureCode = "composite.container.publication-failed";
                return false;
            }
        }

        public bool TryReleaseClaim(
            DurableCompositeEndpointClaim exactClaim,
            out string failureCode)
        {
            failureCode = "composite.container.release-failed";
            if (exactClaim == null ||
                !TryResolve(
                    exactClaim.StableEndpointId,
                    out _,
                    out _,
                    out _,
                    out ZDO zdo)) return false;
            try
            {
                if (!TryReadClaim(zdo, out DurableCompositeEndpointClaim current, out bool corrupt) ||
                    corrupt)
                {
                    failureCode = "composite.container.claim-corrupt";
                    return false;
                }
                if (current == null)
                {
                    failureCode = "composite.container.release-replay";
                    return true;
                }
                if (!exactClaim.MatchesExact(current))
                {
                    failureCode = "composite.container.claim-conflict";
                    return false;
                }
                zdo.Set(ClaimStorageKey, string.Empty);
                if (!TryReadClaim(zdo, out current, out corrupt) || corrupt || current != null)
                {
                    failureCode = "composite.container.release-readback-failed";
                    return false;
                }
                failureCode = "composite.container.released";
                return true;
            }
            catch
            {
                failureCode = "composite.container.release-exception";
                return false;
            }
        }

        internal static bool TryResolve(
            Container container,
            out WorldObjectToken token,
            out Inventory inventory,
            out ZDO zdo)
        {
            token = default;
            inventory = null;
            zdo = null;
            if (container == null || ZNet.instance == null || !ZNet.instance.IsServer()) return false;
            ZNetView view = WorldObjectIdentity.View(container);
            if (view == null || !view.IsValid()) return false;
            if (!view.IsOwner()) view.ClaimOwnership();
            if (!view.IsValid() || !view.IsOwner()) return false;
            zdo = view.GetZDO();
            if (zdo == null || !zdo.IsValid() || zdo.GetOwner() != ZNet.GetUID() ||
                WorldObjectIdentity.Read(zdo, out token) != WorldObjectIdentityStatus.Ready ||
                WorldObjectIdentity.Resolve(token, out ZDO exact) != WorldObjectIdentityStatus.Ready ||
                !ReferenceEquals(exact, zdo))
                return false;
            inventory = container.GetInventory();
            return inventory != null;
        }

        internal static bool TryResolve(
            EndpointId endpointId,
            out Container container,
            out WorldObjectToken token,
            out Inventory inventory,
            out ZDO zdo)
        {
            container = null;
            token = default;
            inventory = null;
            zdo = null;
            string value = endpointId.Value ?? string.Empty;
            if (!value.StartsWith(
                    DurableValheimContainerComposite.StableEndpointPrefix,
                    StringComparison.Ordinal) ||
                !WorldObjectToken.TryParse(
                    value.Substring(
                        DurableValheimContainerComposite.StableEndpointPrefix.Length),
                    out token) ||
                WorldObjectIdentity.ResolveContainer(
                    token.Value, out container, out zdo) != WorldObjectIdentityStatus.Ready)
                return false;
            return TryResolve(container, out WorldObjectToken actual, out inventory, out zdo) &&
                   actual == token;
        }

        internal static byte[] Save(Inventory inventory)
        {
            var package = new ZPackage();
            inventory.Save(package);
            byte[] result = package.GetArray();
            if (result.Length < 1 || result.Length > DurableCompositeLimits.MaximumIntentBytes)
                throw new InvalidDataException("The inventory image exceeds the composite bound.");
            return result;
        }

        internal static bool TryRoundTrip(Inventory shape, byte[] payload)
        {
            if (shape == null || payload == null || payload.Length < 1 ||
                payload.Length > DurableCompositeLimits.MaximumIntentBytes) return false;
            try
            {
                var shadow = new Inventory(
                    shape.GetName(), null, shape.GetWidth(), shape.GetHeight());
                shadow.Load(new ZPackage(payload));
                return Exact(Save(shadow), payload);
            }
            catch { return false; }
        }

        private static string EncodeClaim(DurableCompositeEndpointClaim claim)
        {
            using (var body = new MemoryStream())
            using (var writer = new BinaryWriter(body, StrictUtf8, true))
            {
                writer.Write(ClaimMagic);
                WriteText(writer, claim.OwnerModuleId, 160);
                WriteText(writer, claim.EndpointDomainId, 160);
                writer.Write(claim.OperationId.ToByteArray());
                WriteText(writer, claim.ActorIdentity.Authority, 64);
                WriteText(writer, claim.ActorIdentity.SubjectId, 1024);
                writer.Write((int)claim.ActorIdentity.Assurance);
                WriteText(writer, claim.IntentHash, 64);
                WriteText(writer, claim.StableEndpointId.Value, 160);
                WriteText(writer, claim.BeforeFingerprint, 64);
                WriteText(writer, claim.AfterFingerprint, 64);
                WriteText(writer, claim.PreparedSemanticRevision, 128);
                writer.Flush();
                byte[] content = body.ToArray();
                using (SHA256 sha = SHA256.Create())
                {
                    byte[] digest = sha.ComputeHash(content);
                    using (var envelope = new MemoryStream())
                    {
                        envelope.Write(content, 0, content.Length);
                        envelope.Write(digest, 0, digest.Length);
                        byte[] result = envelope.ToArray();
                        if (result.Length > MaximumClaimBytes)
                            throw new InvalidDataException("The claim exceeds its bound.");
                        return Convert.ToBase64String(result);
                    }
                }
            }
        }

        private static string EncodeAppliedMarker(string worldEpoch, long commitSequence)
        {
            if (!Guid.TryParseExact(worldEpoch, "N", out Guid epoch) || epoch == Guid.Empty ||
                !string.Equals(epoch.ToString("N"), worldEpoch, StringComparison.Ordinal) ||
                commitSequence < 1)
                throw new ArgumentException("The applied marker is not canonical.");
            return worldEpoch + ":" + commitSequence.ToString(
                "x16", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static bool TryReadAppliedMarker(
            ZDO zdo,
            out string worldEpoch,
            out long commitSequence)
        {
            worldEpoch = string.Empty;
            commitSequence = 0;
            string value = zdo.GetString(AppliedMarkerStorageKey, string.Empty);
            if (string.IsNullOrEmpty(value)) return true;
            string[] parts = value.Split(':');
            if (parts.Length != 2 || parts[1].Length != 16 ||
                !Guid.TryParseExact(parts[0], "N", out Guid epoch) || epoch == Guid.Empty ||
                !long.TryParse(
                    parts[1],
                    System.Globalization.NumberStyles.AllowHexSpecifier,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out long sequence) || sequence < 1 ||
                !string.Equals(
                    parts[0] + ":" + sequence.ToString(
                        "x16", System.Globalization.CultureInfo.InvariantCulture),
                    value,
                    StringComparison.Ordinal))
                return false;
            worldEpoch = parts[0];
            commitSequence = sequence;
            return true;
        }

        private static bool TryReadClaim(
            ZDO zdo,
            out DurableCompositeEndpointClaim claim,
            out bool corrupt)
        {
            claim = null;
            corrupt = false;
            string encoded = zdo.GetString(ClaimStorageKey, string.Empty);
            if (string.IsNullOrEmpty(encoded)) return true;
            try
            {
                byte[] envelope = Convert.FromBase64String(encoded);
                if (envelope.Length < 64 || envelope.Length > MaximumClaimBytes)
                    throw new InvalidDataException();
                int contentLength = envelope.Length - 32;
                byte[] content = new byte[contentLength];
                byte[] digest = new byte[32];
                Buffer.BlockCopy(envelope, 0, content, 0, contentLength);
                Buffer.BlockCopy(envelope, contentLength, digest, 0, 32);
                using (SHA256 sha = SHA256.Create())
                    if (!Exact(sha.ComputeHash(content), digest))
                        throw new InvalidDataException();
                using (var stream = new MemoryStream(content, false))
                using (var reader = new BinaryReader(stream, StrictUtf8, true))
                {
                    if (reader.ReadInt32() != ClaimMagic) throw new InvalidDataException();
                    string owner = ReadText(reader, 160);
                    string domain = ReadText(reader, 160);
                    Guid operation = new Guid(ReadExact(reader, 16));
                    var identity = new Runic.Foundation.Persistence.RpcPeerIdentity(
                        ReadText(reader, 64),
                        ReadText(reader, 1024),
                        (Runic.Foundation.Persistence.RpcIdentityAssurance)reader.ReadInt32());
                    string hash = ReadText(reader, 64);
                    var endpoint = new DurableCompositeEndpointIntent(
                        new EndpointId(ReadText(reader, 160)),
                        ReadText(reader, 64),
                        ReadText(reader, 64),
                        ReadText(reader, 128),
                        Array.Empty<byte>());
                    if (stream.Position != stream.Length) throw new InvalidDataException();
                    claim = new DurableCompositeEndpointClaim(
                        owner, domain, operation, identity, hash, endpoint);
                    return true;
                }
            }
            catch
            {
                claim = null;
                corrupt = true;
                return false;
            }
        }

        private static void WriteText(BinaryWriter writer, string value, int maximum)
        {
            string exact = CompositeValidation.RequireText(value, nameof(value), maximum);
            byte[] bytes = StrictUtf8.GetBytes(exact);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        private static string ReadText(BinaryReader reader, int maximum)
        {
            int length = reader.ReadInt32();
            if (length < 1 || length > maximum) throw new InvalidDataException();
            string result = StrictUtf8.GetString(ReadExact(reader, length));
            return CompositeValidation.RequireText(result, nameof(result), maximum);
        }

        private static byte[] ReadExact(BinaryReader reader, int count)
        {
            byte[] result = reader.ReadBytes(count);
            if (result.Length != count) throw new EndOfStreamException();
            return result;
        }

        private static bool Exact(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            int difference = 0;
            for (int index = 0; index < left.Length; index++)
                difference |= left[index] ^ right[index];
            return difference == 0;
        }
    }
}

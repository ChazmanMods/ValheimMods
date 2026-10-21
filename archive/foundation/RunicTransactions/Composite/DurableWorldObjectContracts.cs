using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Runic.Foundation.Core;
using Runic.Foundation.Persistence;
using RunicTransactions.Contracts;

namespace Runic.Foundation.Transactions
{
    /// <summary>
    /// Canonical create-from-absence endpoint domain. Its stable identity is a Runic UUID derived
    /// from a durably server-issued operation token, never a client-selected or preallocated ZDOID.
    /// </summary>
    public static class DurableWorldObjectDomain
    {
        public const string DomainId = "valheim.world-object.desired-state";
        public const int SchemaVersion = 1;
        public const string StableEndpointPrefix = "valheim.desired-world-object:";
        public const string AbsentSemanticRevision = "absent.v1";
        public static readonly string AbsentFingerprint = DurableCompositeCodec.Sha256Hex(
            Encoding.UTF8.GetBytes("runic.transactions.world-object.absent.v1"));
        public static DurableCompositeEndpointDomainDescriptor Descriptor { get; } =
            new DurableCompositeEndpointDomainDescriptor(
                DomainId,
                SchemaVersion,
                StableEndpointPrefix,
                DurableCompositeLimits.MaximumEndpoints);
    }

    public sealed class DurableWorldObjectCreationId
    {
        private DurableWorldObjectCreationId(string mutationKind, Guid value)
        {
            MutationKind = RunicIdentifier.Require(mutationKind, nameof(mutationKind));
            if (value == Guid.Empty) throw new ArgumentException(
                "A nonempty creation UUID is required.", nameof(value));
            Value = value;
        }

        public string MutationKind { get; }
        public Guid Value { get; }
        public string ValueText => Value.ToString("N");
        public EndpointId EndpointId => new EndpointId(
            DurableWorldObjectDomain.StableEndpointPrefix + MutationKind + "." + ValueText);

        public static DurableWorldObjectCreationId Derive(
            DurableCompositeOperationToken operationToken,
            string mutationKind,
            int endpointOrdinal)
        {
            if (operationToken == null) throw new ArgumentNullException(nameof(operationToken));
            string kind = RunicIdentifier.Require(mutationKind, nameof(mutationKind));
            if (endpointOrdinal < 0 || endpointOrdinal >= DurableCompositeLimits.MaximumEndpoints)
                throw new ArgumentOutOfRangeException(nameof(endpointOrdinal));
            byte[] input = Encoding.UTF8.GetBytes(
                "runic.transactions.world-object.creation.v1\n" +
                operationToken.CanonicalValue + "\n" + kind + "\n" +
                endpointOrdinal.ToString(CultureInfo.InvariantCulture));
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(input);
                var uuidBytes = new byte[16];
                Buffer.BlockCopy(digest, 0, uuidBytes, 0, uuidBytes.Length);
                // Stable RFC-4122 variant/version bits; this is an identifier, not randomness.
                uuidBytes[7] = (byte)((uuidBytes[7] & 0x0f) | 0x50);
                uuidBytes[8] = (byte)((uuidBytes[8] & 0x3f) | 0x80);
                Guid value = new Guid(uuidBytes);
                if (value == Guid.Empty) throw new CryptographicException(
                    "The derived creation identity was empty.");
                return new DurableWorldObjectCreationId(kind, value);
            }
        }

        /// <summary>
        /// Builds the stable endpoint identity for an already-existing object from its exact
        /// server-published Runic world-object token. Callers must resolve and prove that token
        /// through the authoritative provider; this method never treats payload text as proof.
        /// </summary>
        public static DurableWorldObjectCreationId FromExisting(
            string mutationKind,
            string canonicalWorldObjectToken)
        {
            string kind = RunicIdentifier.Require(mutationKind, nameof(mutationKind));
            if (!Guid.TryParseExact(
                    canonicalWorldObjectToken, "N", out Guid value) || value == Guid.Empty ||
                !string.Equals(
                    value.ToString("N"), canonicalWorldObjectToken, StringComparison.Ordinal))
                throw new ArgumentException(
                    "A canonical nonempty server world-object token is required.",
                    nameof(canonicalWorldObjectToken));
            return new DurableWorldObjectCreationId(kind, value);
        }

        public static bool TryParseEndpoint(
            EndpointId endpointId,
            out DurableWorldObjectCreationId creationId)
        {
            creationId = null;
            string value = endpointId.Value ?? string.Empty;
            if (!value.StartsWith(
                    DurableWorldObjectDomain.StableEndpointPrefix,
                    StringComparison.Ordinal)) return false;
            string suffix = value.Substring(DurableWorldObjectDomain.StableEndpointPrefix.Length);
            int separator = suffix.LastIndexOf('.');
            if (separator < 1 || separator + 33 != suffix.Length ||
                !Guid.TryParseExact(suffix.Substring(separator + 1), "N", out Guid uuid) ||
                uuid == Guid.Empty) return false;
            try
            {
                creationId = new DurableWorldObjectCreationId(
                    suffix.Substring(0, separator), uuid);
                return creationId.EndpointId == endpointId;
            }
            catch { return false; }
        }
    }

    public sealed class DurableWorldObjectPose
    {
        public DurableWorldObjectPose(
            float x,
            float y,
            float z,
            float rotationX,
            float rotationY,
            float rotationZ,
            float rotationW)
        {
            float[] values =
            {
                x, y, z, rotationX, rotationY, rotationZ, rotationW
            };
            foreach (float value in values)
                if (float.IsNaN(value) || float.IsInfinity(value))
                    throw new ArgumentOutOfRangeException(nameof(x));
            double magnitude = rotationX * rotationX + rotationY * rotationY +
                               rotationZ * rotationZ + rotationW * rotationW;
            if (magnitude < 0.999 || magnitude > 1.001)
                throw new ArgumentException("The exact rotation must already be normalized.");
            X = x;
            Y = y;
            Z = z;
            RotationX = rotationX;
            RotationY = rotationY;
            RotationZ = rotationZ;
            RotationW = rotationW;
        }

        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public float RotationX { get; }
        public float RotationY { get; }
        public float RotationZ { get; }
        public float RotationW { get; }
    }

    public sealed class DurableWorldObjectMutationProviderDescriptor
    {
        public DurableWorldObjectMutationProviderDescriptor(
            string mutationKind,
            int schemaVersion)
        {
            MutationKind = RunicIdentifier.Require(mutationKind, nameof(mutationKind));
            if (schemaVersion < 1 || schemaVersion > 65535)
                throw new ArgumentOutOfRangeException(nameof(schemaVersion));
            SchemaVersion = schemaVersion;
        }

        public string MutationKind { get; }
        public int SchemaVersion { get; }
    }

    public enum DurableWorldObjectTransitionKind
    {
        CreateFromAbsence = 1,
        RemoveToAbsence = 2,
        UpdateExisting = 3
    }

    /// <summary>
    /// One canonical typed transition in the desired-state world-object domain. Implementations
    /// are immutable codec-backed values supplied by Transactions, not arbitrary payload IDs.
    /// </summary>
    public interface IDurableWorldObjectEndpointIntent
    {
        DurableWorldObjectTransitionKind TransitionKind { get; }
        DurableCompositeOperationToken OperationToken { get; }
        int EndpointOrdinal { get; }
        DurableWorldObjectMutationProviderDescriptor Provider { get; }
        DurableWorldObjectCreationId ObjectId { get; }
        RpcPeerIdentity ActorIdentity { get; }
        string BeforeFingerprint { get; }
        string BeforeSemanticRevision { get; }
        string AfterFingerprint { get; }
        string AfterSemanticRevision { get; }
        string InventoryReceiptHash { get; }
        string InventoryNativeTagValue { get; }
        bool RequiresIdentityEnrollment { get; }
        byte[] ExactProviderPayload { get; }
        DurableCompositeEndpointIntent ToCompositeEndpointIntent();
    }

    public sealed class DurableWorldObjectCreateIntent : IDurableWorldObjectEndpointIntent
    {
        private readonly byte[] _providerPayload;

        public DurableWorldObjectCreateIntent(
            DurableCompositeOperationToken operationToken,
            int endpointOrdinal,
            DurableWorldObjectMutationProviderDescriptor provider,
            string prefabId,
            DurableWorldObjectPose pose,
            RpcPeerIdentity creatorIdentity,
            string wardEvidenceHash,
            string terrainEvidenceHash,
            string costReceiptHash,
            string inventoryReceiptHash,
            string inventoryNativeTagValue,
            byte[] exactProviderPayload)
        {
            OperationToken = operationToken ?? throw new ArgumentNullException(nameof(operationToken));
            Provider = provider ?? throw new ArgumentNullException(nameof(provider));
            CreationId = DurableWorldObjectCreationId.Derive(
                operationToken, provider.MutationKind, endpointOrdinal);
            EndpointOrdinal = endpointOrdinal;
            PrefabId = CompositeValidation.RequireText(prefabId, nameof(prefabId), 128);
            Pose = pose ?? throw new ArgumentNullException(nameof(pose));
            CreatorIdentity = CompositeValidation.RequireBackendIdentity(creatorIdentity);
            WardEvidenceHash = CompositeValidation.RequireSha256(
                wardEvidenceHash, nameof(wardEvidenceHash));
            TerrainEvidenceHash = CompositeValidation.RequireSha256(
                terrainEvidenceHash, nameof(terrainEvidenceHash));
            CostReceiptHash = CompositeValidation.RequireSha256(
                costReceiptHash, nameof(costReceiptHash));
            InventoryReceiptHash = CompositeValidation.RequireSha256(
                inventoryReceiptHash, nameof(inventoryReceiptHash));
            if (!string.Equals(
                    inventoryNativeTagValue,
                    operationToken.CanonicalValue,
                    StringComparison.Ordinal))
                throw new ArgumentException(
                    "The client inventory tag must be the exact server-issued token.",
                    nameof(inventoryNativeTagValue));
            InventoryNativeTagValue = inventoryNativeTagValue;
            exactProviderPayload = exactProviderPayload ?? Array.Empty<byte>();
            if (exactProviderPayload.Length > DurableCompositeLimits.MaximumWorldObjectPayloadBytes)
                throw new ArgumentOutOfRangeException(nameof(exactProviderPayload));
            _providerPayload = (byte[])exactProviderPayload.Clone();
            byte[] mutation = DurableWorldObjectCodec.Encode(this);
            AfterFingerprint = DurableWorldObjectCodec.ComputeDesiredFingerprint(mutation);
            ExactCompositeMutation = mutation;
        }

        public DurableCompositeOperationToken OperationToken { get; }
        public DurableWorldObjectTransitionKind TransitionKind =>
            DurableWorldObjectTransitionKind.CreateFromAbsence;
        public int EndpointOrdinal { get; }
        public DurableWorldObjectMutationProviderDescriptor Provider { get; }
        public DurableWorldObjectCreationId CreationId { get; }
        public DurableWorldObjectCreationId ObjectId => CreationId;
        public string PrefabId { get; }
        public DurableWorldObjectPose Pose { get; }
        public RpcPeerIdentity CreatorIdentity { get; }
        public RpcPeerIdentity ActorIdentity => CreatorIdentity;
        public string WardEvidenceHash { get; }
        public string TerrainEvidenceHash { get; }
        public string CostReceiptHash { get; }
        public string InventoryReceiptHash { get; }
        public string InventoryNativeTagValue { get; }
        public bool RequiresIdentityEnrollment => false;
        public byte[] ExactProviderPayload => (byte[])_providerPayload.Clone();
        public string BeforeFingerprint => DurableWorldObjectDomain.AbsentFingerprint;
        public string BeforeSemanticRevision => DurableWorldObjectDomain.AbsentSemanticRevision;
        public string AfterFingerprint { get; }
        public string AfterSemanticRevision => "present." + AfterFingerprint;
        internal byte[] ExactCompositeMutation { get; }
        internal byte[] ExactProviderPayloadUnsafe => _providerPayload;

        public DurableCompositeEndpointIntent ToCompositeEndpointIntent() =>
            new DurableCompositeEndpointIntent(
                CreationId.EndpointId,
                DurableWorldObjectDomain.AbsentFingerprint,
                AfterFingerprint,
                DurableWorldObjectDomain.AbsentSemanticRevision,
                AfterSemanticRevision,
                ExactCompositeMutation);
    }

    /// <summary>
    /// Typed removal of one exact existing Runic-tokened object. The before evidence and provider
    /// payload must be sufficient for authoritative reconstruction if a prepared native mutation
    /// has to be compensated. It shares the same operation token/inventory receipt as a paired
    /// create intent, allowing one mixed replant root to order both endpoints atomically.
    /// </summary>
    public sealed class DurableWorldObjectRemoveIntent : IDurableWorldObjectEndpointIntent
    {
        private readonly byte[] _providerPayload;

        public DurableWorldObjectRemoveIntent(
            DurableCompositeOperationToken operationToken,
            int endpointOrdinal,
            DurableWorldObjectMutationProviderDescriptor provider,
            string canonicalExistingWorldObjectToken,
            string prefabId,
            DurableWorldObjectPose pose,
            RpcPeerIdentity actorIdentity,
            string beforeFingerprint,
            string beforeSemanticRevision,
            string wardEvidenceHash,
            string terrainEvidenceHash,
            string costReceiptHash,
            string inventoryReceiptHash,
            string inventoryNativeTagValue,
            byte[] exactProviderPayload)
        {
            OperationToken = operationToken ?? throw new ArgumentNullException(nameof(operationToken));
            if (endpointOrdinal < 0 || endpointOrdinal >= DurableCompositeLimits.MaximumEndpoints)
                throw new ArgumentOutOfRangeException(nameof(endpointOrdinal));
            EndpointOrdinal = endpointOrdinal;
            Provider = provider ?? throw new ArgumentNullException(nameof(provider));
            ObjectId = DurableWorldObjectCreationId.FromExisting(
                provider.MutationKind, canonicalExistingWorldObjectToken);
            PrefabId = CompositeValidation.RequireText(prefabId, nameof(prefabId), 128);
            Pose = pose ?? throw new ArgumentNullException(nameof(pose));
            ActorIdentity = CompositeValidation.RequireBackendIdentity(actorIdentity);
            BeforeFingerprint = CompositeValidation.RequireSha256(
                beforeFingerprint, nameof(beforeFingerprint));
            if (string.Equals(
                    BeforeFingerprint,
                    DurableWorldObjectDomain.AbsentFingerprint,
                    StringComparison.Ordinal))
                throw new ArgumentException(
                    "A removal requires exact Present before evidence.", nameof(beforeFingerprint));
            BeforeSemanticRevision = CompositeValidation.RequireText(
                beforeSemanticRevision,
                nameof(beforeSemanticRevision),
                DurableCompositeLimits.MaximumSemanticRevisionBytes);
            WardEvidenceHash = CompositeValidation.RequireSha256(
                wardEvidenceHash, nameof(wardEvidenceHash));
            TerrainEvidenceHash = CompositeValidation.RequireSha256(
                terrainEvidenceHash, nameof(terrainEvidenceHash));
            CostReceiptHash = CompositeValidation.RequireSha256(
                costReceiptHash, nameof(costReceiptHash));
            InventoryReceiptHash = CompositeValidation.RequireSha256(
                inventoryReceiptHash, nameof(inventoryReceiptHash));
            if (!string.Equals(
                    inventoryNativeTagValue,
                    operationToken.CanonicalValue,
                    StringComparison.Ordinal))
                throw new ArgumentException(
                    "The client inventory tag must be the exact server-issued token.",
                    nameof(inventoryNativeTagValue));
            InventoryNativeTagValue = inventoryNativeTagValue;
            exactProviderPayload = exactProviderPayload ?? Array.Empty<byte>();
            if (exactProviderPayload.Length > DurableCompositeLimits.MaximumWorldObjectPayloadBytes)
                throw new ArgumentOutOfRangeException(nameof(exactProviderPayload));
            _providerPayload = (byte[])exactProviderPayload.Clone();
            ExactCompositeMutation = DurableWorldObjectCodec.Encode(this);
        }

        /// <summary>
        /// Reserves a stable server identity for an exact ordinary/vanilla object that does not
        /// yet carry a Runic world-object token. Prepare durably journals this intent before the
        /// provider may publish the derived token. Gameplay removal remains forbidden until that
        /// token is read back uniquely and every endpoint claim is Prepared.
        /// </summary>
        public static DurableWorldObjectRemoveIntent EnrollExisting(
            DurableCompositeOperationToken operationToken,
            int endpointOrdinal,
            DurableWorldObjectMutationProviderDescriptor provider,
            string prefabId,
            DurableWorldObjectPose pose,
            RpcPeerIdentity actorIdentity,
            string beforeFingerprint,
            string beforeSemanticRevision,
            string wardEvidenceHash,
            string terrainEvidenceHash,
            string costReceiptHash,
            string inventoryReceiptHash,
            string inventoryNativeTagValue,
            byte[] exactProviderPayload)
        {
            if (operationToken == null) throw new ArgumentNullException(nameof(operationToken));
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            string token = DurableWorldObjectCreationId.Derive(
                operationToken, provider.MutationKind, endpointOrdinal).ValueText;
            return new DurableWorldObjectRemoveIntent(
                operationToken,
                endpointOrdinal,
                provider,
                token,
                prefabId,
                pose,
                actorIdentity,
                beforeFingerprint,
                beforeSemanticRevision,
                wardEvidenceHash,
                terrainEvidenceHash,
                costReceiptHash,
                inventoryReceiptHash,
                inventoryNativeTagValue,
                exactProviderPayload);
        }

        public DurableWorldObjectTransitionKind TransitionKind =>
            DurableWorldObjectTransitionKind.RemoveToAbsence;
        public DurableCompositeOperationToken OperationToken { get; }
        public int EndpointOrdinal { get; }
        public DurableWorldObjectMutationProviderDescriptor Provider { get; }
        public DurableWorldObjectCreationId ObjectId { get; }
        public string PrefabId { get; }
        public DurableWorldObjectPose Pose { get; }
        public RpcPeerIdentity ActorIdentity { get; }
        public string BeforeFingerprint { get; }
        public string BeforeSemanticRevision { get; }
        public string AfterFingerprint => DurableWorldObjectDomain.AbsentFingerprint;
        public string AfterSemanticRevision => DurableWorldObjectDomain.AbsentSemanticRevision;
        public string WardEvidenceHash { get; }
        public string TerrainEvidenceHash { get; }
        public string CostReceiptHash { get; }
        public string InventoryReceiptHash { get; }
        public string InventoryNativeTagValue { get; }
        public bool RequiresIdentityEnrollment => ObjectId.Value ==
            DurableWorldObjectCreationId.Derive(
                OperationToken, Provider.MutationKind, EndpointOrdinal).Value;
        public byte[] ExactProviderPayload => (byte[])_providerPayload.Clone();
        internal byte[] ExactProviderPayloadUnsafe => _providerPayload;
        internal byte[] ExactCompositeMutation { get; }

        public DurableCompositeEndpointIntent ToCompositeEndpointIntent() =>
            new DurableCompositeEndpointIntent(
                ObjectId.EndpointId,
                BeforeFingerprint,
                AfterFingerprint,
                BeforeSemanticRevision,
                AfterSemanticRevision,
                ExactCompositeMutation);
    }

    /// <summary>
    /// Typed Present-to-Present mutation of one exact server-tokened object. Both semantic
    /// states are explicit consumer-domain evidence and must exclude Transactions marker/claim
    /// metadata. This supports bounded multi-piece repair without pretending that an existing
    /// WearNTear object was destroyed and recreated.
    /// </summary>
    public sealed class DurableWorldObjectUpdateIntent : IDurableWorldObjectEndpointIntent
    {
        private readonly byte[] _providerPayload;

        public DurableWorldObjectUpdateIntent(
            DurableCompositeOperationToken operationToken,
            int endpointOrdinal,
            DurableWorldObjectMutationProviderDescriptor provider,
            string canonicalExistingWorldObjectToken,
            string prefabId,
            DurableWorldObjectPose pose,
            RpcPeerIdentity actorIdentity,
            string beforeFingerprint,
            string beforeSemanticRevision,
            string afterFingerprint,
            string afterSemanticRevision,
            string wardEvidenceHash,
            string terrainEvidenceHash,
            string costReceiptHash,
            string inventoryReceiptHash,
            string inventoryNativeTagValue,
            byte[] exactProviderPayload)
        {
            OperationToken = operationToken ?? throw new ArgumentNullException(nameof(operationToken));
            if (endpointOrdinal < 0 || endpointOrdinal >= DurableCompositeLimits.MaximumEndpoints)
                throw new ArgumentOutOfRangeException(nameof(endpointOrdinal));
            EndpointOrdinal = endpointOrdinal;
            Provider = provider ?? throw new ArgumentNullException(nameof(provider));
            ObjectId = DurableWorldObjectCreationId.FromExisting(
                provider.MutationKind, canonicalExistingWorldObjectToken);
            PrefabId = CompositeValidation.RequireText(prefabId, nameof(prefabId), 128);
            Pose = pose ?? throw new ArgumentNullException(nameof(pose));
            ActorIdentity = CompositeValidation.RequireBackendIdentity(actorIdentity);
            BeforeFingerprint = RequirePresentFingerprint(
                beforeFingerprint, nameof(beforeFingerprint));
            BeforeSemanticRevision = CompositeValidation.RequireText(
                beforeSemanticRevision,
                nameof(beforeSemanticRevision),
                DurableCompositeLimits.MaximumSemanticRevisionBytes);
            AfterFingerprint = RequirePresentFingerprint(
                afterFingerprint, nameof(afterFingerprint));
            AfterSemanticRevision = CompositeValidation.RequireText(
                afterSemanticRevision,
                nameof(afterSemanticRevision),
                DurableCompositeLimits.MaximumSemanticRevisionBytes);
            if (string.Equals(BeforeFingerprint, AfterFingerprint, StringComparison.Ordinal) &&
                string.Equals(
                    BeforeSemanticRevision,
                    AfterSemanticRevision,
                    StringComparison.Ordinal))
                throw new ArgumentException("A Present-to-Present mutation cannot be a no-op.");
            WardEvidenceHash = CompositeValidation.RequireSha256(
                wardEvidenceHash, nameof(wardEvidenceHash));
            TerrainEvidenceHash = CompositeValidation.RequireSha256(
                terrainEvidenceHash, nameof(terrainEvidenceHash));
            CostReceiptHash = CompositeValidation.RequireSha256(
                costReceiptHash, nameof(costReceiptHash));
            InventoryReceiptHash = CompositeValidation.RequireSha256(
                inventoryReceiptHash, nameof(inventoryReceiptHash));
            if (!string.Equals(
                    inventoryNativeTagValue,
                    operationToken.CanonicalValue,
                    StringComparison.Ordinal))
                throw new ArgumentException(
                    "The client inventory tag must be the exact server-issued token.",
                    nameof(inventoryNativeTagValue));
            InventoryNativeTagValue = inventoryNativeTagValue;
            exactProviderPayload = exactProviderPayload ?? Array.Empty<byte>();
            if (exactProviderPayload.Length > DurableCompositeLimits.MaximumWorldObjectPayloadBytes)
                throw new ArgumentOutOfRangeException(nameof(exactProviderPayload));
            _providerPayload = (byte[])exactProviderPayload.Clone();
            ExactCompositeMutation = DurableWorldObjectCodec.Encode(this);
        }

        /// <summary>
        /// Reserves a stable server identity for an exact ordinary/vanilla object that does not
        /// yet carry a Runic world-object token. The exact target evidence is provider payload;
        /// it is journaled before identity publication and is never replaced by a raw network id.
        /// </summary>
        public static DurableWorldObjectUpdateIntent EnrollExisting(
            DurableCompositeOperationToken operationToken,
            int endpointOrdinal,
            DurableWorldObjectMutationProviderDescriptor provider,
            string prefabId,
            DurableWorldObjectPose pose,
            RpcPeerIdentity actorIdentity,
            string beforeFingerprint,
            string beforeSemanticRevision,
            string afterFingerprint,
            string afterSemanticRevision,
            string wardEvidenceHash,
            string terrainEvidenceHash,
            string costReceiptHash,
            string inventoryReceiptHash,
            string inventoryNativeTagValue,
            byte[] exactProviderPayload)
        {
            if (operationToken == null) throw new ArgumentNullException(nameof(operationToken));
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            string token = DurableWorldObjectCreationId.Derive(
                operationToken, provider.MutationKind, endpointOrdinal).ValueText;
            return new DurableWorldObjectUpdateIntent(
                operationToken,
                endpointOrdinal,
                provider,
                token,
                prefabId,
                pose,
                actorIdentity,
                beforeFingerprint,
                beforeSemanticRevision,
                afterFingerprint,
                afterSemanticRevision,
                wardEvidenceHash,
                terrainEvidenceHash,
                costReceiptHash,
                inventoryReceiptHash,
                inventoryNativeTagValue,
                exactProviderPayload);
        }

        public DurableWorldObjectTransitionKind TransitionKind =>
            DurableWorldObjectTransitionKind.UpdateExisting;
        public DurableCompositeOperationToken OperationToken { get; }
        public int EndpointOrdinal { get; }
        public DurableWorldObjectMutationProviderDescriptor Provider { get; }
        public DurableWorldObjectCreationId ObjectId { get; }
        public string PrefabId { get; }
        public DurableWorldObjectPose Pose { get; }
        public RpcPeerIdentity ActorIdentity { get; }
        public string BeforeFingerprint { get; }
        public string BeforeSemanticRevision { get; }
        public string AfterFingerprint { get; }
        public string AfterSemanticRevision { get; }
        public string WardEvidenceHash { get; }
        public string TerrainEvidenceHash { get; }
        public string CostReceiptHash { get; }
        public string InventoryReceiptHash { get; }
        public string InventoryNativeTagValue { get; }
        public bool RequiresIdentityEnrollment => ObjectId.Value ==
            DurableWorldObjectCreationId.Derive(
                OperationToken, Provider.MutationKind, EndpointOrdinal).Value;
        public byte[] ExactProviderPayload => (byte[])_providerPayload.Clone();
        internal byte[] ExactProviderPayloadUnsafe => _providerPayload;
        internal byte[] ExactCompositeMutation { get; }

        public DurableCompositeEndpointIntent ToCompositeEndpointIntent() =>
            new DurableCompositeEndpointIntent(
                ObjectId.EndpointId,
                BeforeFingerprint,
                AfterFingerprint,
                BeforeSemanticRevision,
                AfterSemanticRevision,
                ExactCompositeMutation);

        private static string RequirePresentFingerprint(string value, string parameter)
        {
            string exact = CompositeValidation.RequireSha256(value, parameter);
            if (string.Equals(
                    exact,
                    DurableWorldObjectDomain.AbsentFingerprint,
                    StringComparison.Ordinal))
                throw new ArgumentException(
                    "A Present-to-Present mutation requires Present evidence.", parameter);
            return exact;
        }
    }

    public enum DurableWorldObjectReadState
    {
        Absent = 0,
        Present = 1,
        Duplicate = 2,
        Corrupt = 3,
        Unavailable = 4
    }

    public sealed class DurableWorldObjectAuthorityEvidence
    {
        public DurableWorldObjectAuthorityEvidence(
            DurableWorldObjectReadState state,
            string fingerprint,
            string semanticRevision,
            bool stableIdentityCurrent,
            bool synchronized,
            bool serverOwned,
            bool destroyPending,
            string appliedWorldEpoch,
            long appliedCommitSequence)
        {
            if (!Enum.IsDefined(typeof(DurableWorldObjectReadState), state))
                throw new ArgumentOutOfRangeException(nameof(state));
            State = state;
            Fingerprint = CompositeValidation.RequireSha256(fingerprint, nameof(fingerprint));
            SemanticRevision = CompositeValidation.RequireText(
                semanticRevision,
                nameof(semanticRevision),
                DurableCompositeLimits.MaximumSemanticRevisionBytes);
            StableIdentityCurrent = stableIdentityCurrent;
            Synchronized = synchronized;
            ServerOwned = serverOwned;
            DestroyPending = destroyPending;
            if (appliedCommitSequence == 0)
            {
                if (!string.IsNullOrEmpty(appliedWorldEpoch))
                    throw new ArgumentException("An unapplied object cannot carry an epoch.");
                AppliedWorldEpoch = string.Empty;
            }
            else
            {
                if (appliedCommitSequence < 0 ||
                    !Guid.TryParseExact(appliedWorldEpoch, "N", out Guid epoch) ||
                    epoch == Guid.Empty ||
                    !string.Equals(
                        epoch.ToString("N"), appliedWorldEpoch, StringComparison.Ordinal))
                    throw new ArgumentException("The applied object marker is not canonical.");
                AppliedWorldEpoch = appliedWorldEpoch;
            }
            AppliedCommitSequence = appliedCommitSequence;
        }

        public DurableWorldObjectReadState State { get; }
        public string Fingerprint { get; }
        public string SemanticRevision { get; }
        public bool StableIdentityCurrent { get; }
        public bool Synchronized { get; }
        public bool ServerOwned { get; }
        public bool DestroyPending { get; }
        public string AppliedWorldEpoch { get; }
        public long AppliedCommitSequence { get; }
    }

    /// <summary>
    /// A provider owns one bounded mutation kind. Read must resolve zero or exactly one object by
    /// the Runic creation token and must never select by client ZDOID. Apply validates prefab,
    /// finite pose, transport-bound creator, ward, terrain, and exact cost/inventory receipts;
    /// it publishes the endpoint epoch/commit marker only with the desired Present evidence.
    /// Any partial apply must roll back internally before returning false.
    /// </summary>
    public interface IDurableWorldObjectMutationProvider
    {
        DurableWorldObjectReadState Read(
            DurableWorldObjectCreationId creationId,
            out DurableWorldObjectAuthorityEvidence evidence,
            out string failureCode);

        bool TryApplyDesiredState(
            DurableCompositeMutationContext operation,
            IDurableWorldObjectEndpointIntent intent,
            out string failureCode);

        bool TryCompensateToBefore(
            DurableCompositeRollbackContext operation,
            IDurableWorldObjectEndpointIntent intent,
            out string failureCode);
    }

    public enum DurableWorldObjectIdentityEnrollmentState
    {
        Enrolled = 0,
        Pending = 1,
        FailedClosed = 2
    }

    /// <summary>
    /// Optional provider surface for an exact existing object that predates Runic identity
    /// metadata. ReadExactUnenrolledTarget must resolve exactly one current object from immutable
    /// provider evidence persisted in the root (for example exact ZDO identity, prefab, creator,
    /// finite pose, and consumer semantic fingerprint). It must reject reuse, ambiguity, or stale
    /// evidence. Begin/resume publishes only the coordinator-derived ObjectId token using an exact
    /// missing-or-same CAS; it may dispatch to the current direct-session owner and return Pending.
    /// It may not perform the requested gameplay mutation. Enrolled is valid only after the server
    /// can uniquely read the same object through IDurableWorldObjectMutationProvider.Read(ObjectId).
    /// </summary>
    public interface IDurableWorldObjectIdentityEnrollmentProvider
    {
        DurableWorldObjectReadState ReadExactUnenrolledTarget(
            DurableCompositeRollbackContext operation,
            IDurableWorldObjectEndpointIntent intent,
            out DurableWorldObjectAuthorityEvidence evidence,
            out string failureCode);

        DurableWorldObjectIdentityEnrollmentState TryBeginOrResumeIdentityEnrollment(
            DurableCompositeRollbackContext operation,
            IDurableWorldObjectEndpointIntent intent,
            DurableWorldObjectAuthorityEvidence exactUnenrolledEvidence,
            out string failureCode);
    }

    /// <summary>
    /// Optional provider surface for native mutations that can only be performed by an exact
    /// connected object owner. Authority checks must resolve the current owner from server-read
    /// endpoint state and bind it to the direct Persistence session. Begin/resume is invoked only
    /// after the root is durably Committing and therefore receives the immutable commit sequence.
    /// Returning Pending must be side-effect idempotent for that exact operation token/sequence.
    /// </summary>
    public interface IDurableWorldObjectDeferredMutationProvider
    {
        bool IsExactCurrentOwnerMutationAuthority(
            DurableCompositeRollbackContext operation,
            long expectedCommitSequence,
            IDurableWorldObjectEndpointIntent intent,
            DurableWorldObjectAuthorityEvidence evidence,
            out string failureCode);

        DurableCompositeDeferredApplyState TryBeginOrResumeDesiredState(
            DurableCompositeMutationContext operation,
            IDurableWorldObjectEndpointIntent intent,
            out string failureCode);
    }

    public interface IDurableWorldObjectCompositeService
    {
        IDisposable RegisterMutationProvider(
            ModuleRegistration providerModule,
            DurableWorldObjectMutationProviderDescriptor descriptor,
            IDurableWorldObjectMutationProvider provider);

        IDurableCompositeOperationCoordinator CreateCoordinator(ModuleRegistration ownerModule);

        DurableCompositeOperationIntent CreateOperationIntent(
            ModuleRegistration ownerModule,
            DurableCompositeOperationToken operationToken,
            RpcPeerIdentity actorIdentity,
            string requestHash,
            byte[] exactIntent,
            IEnumerable<IDurableWorldObjectEndpointIntent> endpoints,
            DurableCompositeReconciliationRequirement reconciliationRequirement = null);
    }

    internal static class DurableWorldObjectCodec
    {
        private const int Magic = 0x5244574f; // RDWO
        // Schema 2 is the already-persisted Create/Remove envelope. It must remain byte-for-byte
        // stable because ExactMutation is part of durable roots and recovery readback. Schema 3
        // adds the explicit Present after evidence required by UpdateExisting.
        private const int CreateRemoveSchema = 2;
        private const int UpdateSchema = 3;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        internal static byte[] Encode(DurableWorldObjectCreateIntent value) => EncodeCore(
            CreateRemoveSchema,
            value.TransitionKind,
            value.OperationToken,
            value.EndpointOrdinal,
            value.Provider,
            value.ObjectId,
            value.PrefabId,
            value.Pose,
            value.ActorIdentity,
            value.BeforeFingerprint,
            value.BeforeSemanticRevision,
            null,
            null,
            value.WardEvidenceHash,
            value.TerrainEvidenceHash,
            value.CostReceiptHash,
            value.InventoryReceiptHash,
            value.InventoryNativeTagValue,
            value.ExactProviderPayloadUnsafe);

        internal static byte[] Encode(DurableWorldObjectRemoveIntent value) => EncodeCore(
            CreateRemoveSchema,
            value.TransitionKind,
            value.OperationToken,
            value.EndpointOrdinal,
            value.Provider,
            value.ObjectId,
            value.PrefabId,
            value.Pose,
            value.ActorIdentity,
            value.BeforeFingerprint,
            value.BeforeSemanticRevision,
            null,
            null,
            value.WardEvidenceHash,
            value.TerrainEvidenceHash,
            value.CostReceiptHash,
            value.InventoryReceiptHash,
            value.InventoryNativeTagValue,
            value.ExactProviderPayloadUnsafe);

        internal static byte[] Encode(DurableWorldObjectUpdateIntent value) => EncodeCore(
            UpdateSchema,
            value.TransitionKind,
            value.OperationToken,
            value.EndpointOrdinal,
            value.Provider,
            value.ObjectId,
            value.PrefabId,
            value.Pose,
            value.ActorIdentity,
            value.BeforeFingerprint,
            value.BeforeSemanticRevision,
            value.AfterFingerprint,
            value.AfterSemanticRevision,
            value.WardEvidenceHash,
            value.TerrainEvidenceHash,
            value.CostReceiptHash,
            value.InventoryReceiptHash,
            value.InventoryNativeTagValue,
            value.ExactProviderPayloadUnsafe);

        private static byte[] EncodeCore(
            int schema,
            DurableWorldObjectTransitionKind transitionKind,
            DurableCompositeOperationToken operationToken,
            int endpointOrdinal,
            DurableWorldObjectMutationProviderDescriptor provider,
            DurableWorldObjectCreationId objectId,
            string prefabId,
            DurableWorldObjectPose pose,
            RpcPeerIdentity actorIdentity,
            string beforeFingerprint,
            string beforeSemanticRevision,
            string afterFingerprint,
            string afterSemanticRevision,
            string wardEvidenceHash,
            string terrainEvidenceHash,
            string costReceiptHash,
            string inventoryReceiptHash,
            string inventoryNativeTagValue,
            byte[] providerPayload)
        {
            bool isUpdate = transitionKind == DurableWorldObjectTransitionKind.UpdateExisting;
            if ((isUpdate && schema != UpdateSchema) ||
                (!isUpdate && schema != CreateRemoveSchema))
                throw new InvalidDataException("The transition/schema pairing is not canonical.");
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Utf8, true))
            {
                writer.Write(Magic);
                writer.Write(schema);
                writer.Write((int)transitionKind);
                Write(writer, operationToken.CanonicalValue, 192);
                writer.Write(endpointOrdinal);
                Write(writer, provider.MutationKind, 160);
                writer.Write(provider.SchemaVersion);
                Write(writer, objectId.ValueText, 32);
                Write(writer, prefabId, 128);
                writer.Write(pose.X);
                writer.Write(pose.Y);
                writer.Write(pose.Z);
                writer.Write(pose.RotationX);
                writer.Write(pose.RotationY);
                writer.Write(pose.RotationZ);
                writer.Write(pose.RotationW);
                Write(writer, actorIdentity.Authority, 64);
                Write(writer, actorIdentity.SubjectId, 1024);
                writer.Write((int)actorIdentity.Assurance);
                Write(writer, beforeFingerprint, 64);
                Write(
                    writer,
                    beforeSemanticRevision,
                    DurableCompositeLimits.MaximumSemanticRevisionBytes);
                if (isUpdate)
                {
                    Write(writer, afterFingerprint, 64);
                    Write(
                        writer,
                        afterSemanticRevision,
                        DurableCompositeLimits.MaximumSemanticRevisionBytes);
                }
                Write(writer, wardEvidenceHash, 64);
                Write(writer, terrainEvidenceHash, 64);
                Write(writer, costReceiptHash, 64);
                Write(writer, inventoryReceiptHash, 64);
                Write(writer, inventoryNativeTagValue, 192);
                writer.Write(providerPayload.Length);
                writer.Write(providerPayload);
                writer.Flush();
                byte[] result = stream.ToArray();
                if (result.Length > DurableCompositeLimits.MaximumIntentBytes)
                    throw new InvalidDataException("The world-object intent exceeds 64 KiB.");
                return result;
            }
        }

        internal static bool TryDecode(
            byte[] bytes,
            out IDurableWorldObjectEndpointIntent intent)
        {
            intent = null;
            if (bytes == null || bytes.Length < 64 ||
                bytes.Length > DurableCompositeLimits.MaximumIntentBytes) return false;
            try
            {
                using (var stream = new MemoryStream(bytes, false))
                using (var reader = new BinaryReader(stream, Utf8, true))
                {
                    if (reader.ReadInt32() != Magic) return false;
                    int schema = reader.ReadInt32();
                    if (schema != CreateRemoveSchema && schema != UpdateSchema) return false;
                    var transition = (DurableWorldObjectTransitionKind)reader.ReadInt32();
                    if (!Enum.IsDefined(typeof(DurableWorldObjectTransitionKind), transition))
                        return false;
                    bool isUpdate = transition == DurableWorldObjectTransitionKind.UpdateExisting;
                    if ((isUpdate && schema != UpdateSchema) ||
                        (!isUpdate && schema != CreateRemoveSchema)) return false;
                    var token = DurableCompositeOperationToken.Parse(Read(reader, 192));
                    int ordinal = reader.ReadInt32();
                    var provider = new DurableWorldObjectMutationProviderDescriptor(
                        Read(reader, 160), reader.ReadInt32());
                    string objectToken = Read(reader, 32);
                    string prefab = Read(reader, 128);
                    var pose = new DurableWorldObjectPose(
                        reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                        reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                        reader.ReadSingle());
                    var creator = new RpcPeerIdentity(
                        Read(reader, 64),
                        Read(reader, 1024),
                        (RpcIdentityAssurance)reader.ReadInt32());
                    string beforeFingerprint = Read(reader, 64);
                    string beforeSemanticRevision = Read(
                        reader, DurableCompositeLimits.MaximumSemanticRevisionBytes);
                    string afterFingerprint = isUpdate ? Read(reader, 64) : null;
                    string afterSemanticRevision = isUpdate
                        ? Read(reader, DurableCompositeLimits.MaximumSemanticRevisionBytes)
                        : null;
                    string ward = Read(reader, 64);
                    string terrain = Read(reader, 64);
                    string cost = Read(reader, 64);
                    string inventory = Read(reader, 64);
                    string nativeTag = Read(reader, 192);
                    int payloadLength = reader.ReadInt32();
                    if (payloadLength < 0 ||
                        payloadLength > DurableCompositeLimits.MaximumWorldObjectPayloadBytes)
                        return false;
                    byte[] payload = reader.ReadBytes(payloadLength);
                    if (payload.Length != payloadLength || stream.Position != stream.Length)
                        return false;
                    if (transition == DurableWorldObjectTransitionKind.CreateFromAbsence)
                    {
                        var create = new DurableWorldObjectCreateIntent(
                            token,
                            ordinal,
                            provider,
                            prefab,
                            pose,
                            creator,
                            ward,
                            terrain,
                            cost,
                            inventory,
                            nativeTag,
                            payload);
                        if (!string.Equals(
                                create.ObjectId.ValueText, objectToken, StringComparison.Ordinal) ||
                            !string.Equals(
                                create.BeforeFingerprint,
                                beforeFingerprint,
                                StringComparison.Ordinal) ||
                            !string.Equals(
                                create.BeforeSemanticRevision,
                                beforeSemanticRevision,
                                StringComparison.Ordinal) ||
                            !Exact(Encode(create), bytes)) return false;
                        intent = create;
                    }
                    else if (transition == DurableWorldObjectTransitionKind.RemoveToAbsence)
                    {
                        var remove = new DurableWorldObjectRemoveIntent(
                            token,
                            ordinal,
                            provider,
                            objectToken,
                            prefab,
                            pose,
                            creator,
                            beforeFingerprint,
                            beforeSemanticRevision,
                            ward,
                            terrain,
                            cost,
                            inventory,
                            nativeTag,
                            payload);
                        if (!Exact(Encode(remove), bytes)) return false;
                        intent = remove;
                    }
                    else
                    {
                        var update = new DurableWorldObjectUpdateIntent(
                            token,
                            ordinal,
                            provider,
                            objectToken,
                            prefab,
                            pose,
                            creator,
                            beforeFingerprint,
                            beforeSemanticRevision,
                            afterFingerprint,
                            afterSemanticRevision,
                            ward,
                            terrain,
                            cost,
                            inventory,
                            nativeTag,
                            payload);
                        if (!Exact(Encode(update), bytes)) return false;
                        intent = update;
                    }
                    return true;
                }
            }
            catch { return false; }
        }

        internal static bool TryDecode(
            byte[] bytes,
            out DurableWorldObjectCreateIntent intent)
        {
            bool decoded = TryDecode(
                bytes, out IDurableWorldObjectEndpointIntent value);
            intent = value as DurableWorldObjectCreateIntent;
            return decoded && intent != null;
        }

        internal static string ComputeDesiredFingerprint(byte[] canonicalIntent)
        {
            byte[] domain = Utf8.GetBytes("runic.transactions.world-object.present.v1\n");
            byte[] input = new byte[domain.Length + canonicalIntent.Length];
            Buffer.BlockCopy(domain, 0, input, 0, domain.Length);
            Buffer.BlockCopy(canonicalIntent, 0, input, domain.Length, canonicalIntent.Length);
            return DurableCompositeCodec.Sha256Hex(input);
        }

        private static void Write(BinaryWriter writer, string value, int maximum)
        {
            string exact = CompositeValidation.RequireText(value, nameof(value), maximum);
            byte[] bytes = Utf8.GetBytes(exact);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        private static string Read(BinaryReader reader, int maximum)
        {
            int length = reader.ReadInt32();
            if (length < 1 || length > maximum) throw new InvalidDataException();
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            return CompositeValidation.RequireText(
                Utf8.GetString(bytes), "value", maximum);
        }

        private static bool Exact(byte[] left, byte[] right)
        {
            if (left.Length != right.Length) return false;
            int difference = 0;
            for (int index = 0; index < left.Length; index++)
                difference |= left[index] ^ right[index];
            return difference == 0;
        }
    }
}

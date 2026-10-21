using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Runic.Foundation.Core;
using Runic.Foundation.Persistence;
using RunicTransactions.Contracts;

namespace Runic.Foundation.Transactions
{
    internal sealed class CompositeRootRecord
    {
        private readonly byte[] _exactIntent;
        private readonly IReadOnlyList<DurableCompositeEndpointIntent> _endpoints;
        private readonly IReadOnlyList<DurableCompositeEndpointPredecessor> _predecessors;
        private readonly IReadOnlyList<DurableCompositeEndpointReceipt> _terminalEndpoints;

        internal CompositeRootRecord(
            DurableCompositeOperationIntent intent,
            int endpointDomainSchemaVersion)
            : this(
                intent.OwnerModuleId,
                intent.OwnerModule.Descriptor.SemanticVersion.ToString(),
                intent.OwnerModule.Descriptor.ProtocolVersion.Major,
                intent.EndpointDomainId,
                endpointDomainSchemaVersion,
                intent.OperationToken,
                intent.ActorIdentity,
                intent.RequestHash,
                intent.IntentHash,
                intent.ExactIntentUnsafe,
                intent.Endpoints,
                intent.ReconciliationRequirement,
                false,
                DurableCompositeOperationPhase.Claiming,
                0,
                Array.Empty<DurableCompositeEndpointPredecessor>(),
                string.Empty,
                Array.Empty<DurableCompositeEndpointReceipt>(),
                string.Empty)
        {
        }

        internal CompositeRootRecord(
            string ownerModuleId,
            string ownerModuleVersion,
            int ownerProtocolMajor,
            string endpointDomainId,
            int endpointDomainSchemaVersion,
            DurableCompositeOperationToken operationToken,
            RpcPeerIdentity actorIdentity,
            string requestHash,
            string intentHash,
            byte[] exactIntent,
            IEnumerable<DurableCompositeEndpointIntent> endpoints,
            DurableCompositeReconciliationRequirement reconciliationRequirement,
            bool compactedAtCheckpoint,
            DurableCompositeOperationPhase phase,
            long commitSequence,
            IEnumerable<DurableCompositeEndpointPredecessor> predecessors,
            string receiptHash,
            IEnumerable<DurableCompositeEndpointReceipt> terminalEndpoints,
            string acknowledgementHash)
        {
            OwnerModuleId = RunicIdentifier.Require(ownerModuleId, nameof(ownerModuleId));
            OwnerModuleVersion = CompositeValidation.RequireText(
                ownerModuleVersion, nameof(ownerModuleVersion), 64);
            if (ownerProtocolMajor < 1 || ownerProtocolMajor > 65535)
                throw new ArgumentOutOfRangeException(nameof(ownerProtocolMajor));
            OwnerProtocolMajor = ownerProtocolMajor;
            EndpointDomainId = RunicIdentifier.Require(endpointDomainId, nameof(endpointDomainId));
            if (endpointDomainSchemaVersion < 1 || endpointDomainSchemaVersion > 65535)
                throw new ArgumentOutOfRangeException(nameof(endpointDomainSchemaVersion));
            EndpointDomainSchemaVersion = endpointDomainSchemaVersion;
            OperationToken = operationToken ?? throw new ArgumentNullException(nameof(operationToken));
            ActorIdentity = CompositeValidation.RequireBackendIdentity(actorIdentity);
            RequestHash = CompositeValidation.RequireSha256(requestHash, nameof(requestHash));
            IntentHash = CompositeValidation.RequireSha256(intentHash, nameof(intentHash));
            CompactedAtCheckpoint = compactedAtCheckpoint;
            exactIntent = exactIntent ?? Array.Empty<byte>();
            if (exactIntent.Length < (compactedAtCheckpoint ? 0 : 1) ||
                exactIntent.Length > DurableCompositeLimits.MaximumIntentBytes)
                throw new ArgumentOutOfRangeException(nameof(exactIntent));
            _exactIntent = (byte[])exactIntent.Clone();

            var endpointCopy = (endpoints ?? throw new ArgumentNullException(nameof(endpoints))).ToArray();
            if (endpointCopy.Length < 1 || endpointCopy.Length > DurableCompositeLimits.MaximumEndpoints ||
                endpointCopy.Any(value => value == null))
                throw new ArgumentOutOfRangeException(nameof(endpoints));
            for (int index = 1; index < endpointCopy.Length; index++)
                if (endpointCopy[index - 1].StableEndpointId.CompareTo(
                        endpointCopy[index].StableEndpointId) >= 0)
                    throw new ArgumentException(
                        "Root endpoints must be strictly sorted and unique.", nameof(endpoints));
            _endpoints = new ReadOnlyCollection<DurableCompositeEndpointIntent>(endpointCopy);
            if (!compactedAtCheckpoint && !string.Equals(
                    DurableCompositeCodec.ComputeIntentHash(
                        EndpointDomainId, _exactIntent, _endpoints),
                    IntentHash,
                    StringComparison.Ordinal))
                throw new ArgumentException("The root intent hash is not exact.", nameof(intentHash));
            ReconciliationRequirement = reconciliationRequirement;
            if (compactedAtCheckpoint && phase != DurableCompositeOperationPhase.Committed)
                throw new ArgumentException(
                    "Only a checkpoint-grounded committed root can be compacted.",
                    nameof(compactedAtCheckpoint));
            if (!Enum.IsDefined(typeof(DurableCompositeOperationPhase), phase))
                throw new ArgumentOutOfRangeException(nameof(phase));
            Phase = phase;

            var predecessorCopy = (predecessors ??
                throw new ArgumentNullException(nameof(predecessors))).ToArray();
            if (phase == DurableCompositeOperationPhase.Committing ||
                phase == DurableCompositeOperationPhase.Committed)
            {
                if (commitSequence < 1) throw new ArgumentOutOfRangeException(nameof(commitSequence));
            }
            else if (commitSequence != 0 || predecessorCopy.Length != 0)
                throw new ArgumentException(
                    "Only committing and committed roots carry commit order.");
            CommitSequence = commitSequence;
            if (predecessorCopy.Length > endpointCopy.Length)
                throw new ArgumentOutOfRangeException(nameof(predecessors));
            Array.Sort(predecessorCopy, (left, right) => left.EndpointId.CompareTo(right.EndpointId));
            for (int index = 0; index < predecessorCopy.Length; index++)
            {
                if (predecessorCopy[index] == null ||
                    predecessorCopy[index].CommitSequence >= commitSequence ||
                    !endpointCopy.Any(endpoint =>
                        endpoint.StableEndpointId == predecessorCopy[index].EndpointId) ||
                    index > 0 && predecessorCopy[index - 1].EndpointId ==
                                 predecessorCopy[index].EndpointId)
                    throw new ArgumentException("Commit predecessors are not canonical.", nameof(predecessors));
            }
            _predecessors = new ReadOnlyCollection<DurableCompositeEndpointPredecessor>(predecessorCopy);

            var terminalCopy = (terminalEndpoints ??
                throw new ArgumentNullException(nameof(terminalEndpoints))).ToArray();
            bool terminal = phase == DurableCompositeOperationPhase.Committed ||
                            phase == DurableCompositeOperationPhase.Aborted;
            if (terminal)
            {
                ReceiptHash = CompositeValidation.RequireSha256(receiptHash, nameof(receiptHash));
                if (terminalCopy.Length != endpointCopy.Length)
                    throw new ArgumentException(
                        "A terminal root requires exact evidence for every endpoint.",
                        nameof(terminalEndpoints));
                for (int index = 0; index < terminalCopy.Length; index++)
                    if (terminalCopy[index] == null ||
                        terminalCopy[index].EndpointId != endpointCopy[index].StableEndpointId ||
                        phase == DurableCompositeOperationPhase.Committed &&
                            (!string.Equals(
                                terminalCopy[index].AppliedWorldEpoch,
                                OperationToken.WorldEpoch,
                                StringComparison.Ordinal) ||
                             terminalCopy[index].AppliedCommitSequence != commitSequence ||
                             !string.Equals(
                                 terminalCopy[index].Fingerprint,
                                 endpointCopy[index].AfterFingerprint,
                                 StringComparison.Ordinal) ||
                             !string.Equals(
                                 terminalCopy[index].SemanticRevision,
                                 endpointCopy[index].AfterSemanticRevision,
                                 StringComparison.Ordinal)) ||
                        phase == DurableCompositeOperationPhase.Aborted &&
                            (!string.Equals(
                                 terminalCopy[index].Fingerprint,
                                 endpointCopy[index].BeforeFingerprint,
                                 StringComparison.Ordinal) ||
                             !string.Equals(
                                 terminalCopy[index].SemanticRevision,
                                 endpointCopy[index].PreparedSemanticRevision,
                                 StringComparison.Ordinal)))
                        throw new ArgumentException(
                            "Terminal endpoint evidence is not canonical.",
                            nameof(terminalEndpoints));
                string computed = DurableCompositeCodec.ComputeReceiptHash(
                    this, phase, terminalCopy);
                if (!string.Equals(computed, ReceiptHash, StringComparison.Ordinal))
                    throw new ArgumentException("The terminal receipt hash is not exact.", nameof(receiptHash));
            }
            else if (!string.IsNullOrEmpty(receiptHash) || terminalCopy.Length != 0)
                throw new ArgumentException("A nonterminal root cannot carry a receipt.");
            else ReceiptHash = string.Empty;
            _terminalEndpoints = new ReadOnlyCollection<DurableCompositeEndpointReceipt>(terminalCopy);

            if (!string.IsNullOrEmpty(acknowledgementHash))
            {
                if ((phase != DurableCompositeOperationPhase.Committed &&
                     phase != DurableCompositeOperationPhase.Aborted) ||
                    reconciliationRequirement == null)
                    throw new ArgumentException(
                        "Only a reconciliation-required terminal root can be acknowledged.",
                        nameof(acknowledgementHash));
                AcknowledgementHash = CompositeValidation.RequireSha256(
                    acknowledgementHash, nameof(acknowledgementHash));
            }
            else AcknowledgementHash = string.Empty;
        }

        internal string OwnerModuleId { get; }
        internal string OwnerModuleVersion { get; }
        internal int OwnerProtocolMajor { get; }
        internal string EndpointDomainId { get; }
        internal int EndpointDomainSchemaVersion { get; }
        internal DurableCompositeOperationToken OperationToken { get; }
        internal Guid OperationId => OperationToken.OperationId;
        internal string OperationIdText => OperationToken.OperationIdText;
        internal RpcPeerIdentity ActorIdentity { get; }
        internal string RequestHash { get; }
        internal string IntentHash { get; }
        internal IReadOnlyList<DurableCompositeEndpointIntent> Endpoints => _endpoints;
        internal DurableCompositeReconciliationRequirement ReconciliationRequirement { get; }
        internal bool CompactedAtCheckpoint { get; }
        internal DurableCompositeOperationPhase Phase { get; }
        internal long CommitSequence { get; }
        internal IReadOnlyList<DurableCompositeEndpointPredecessor> Predecessors => _predecessors;
        internal string ReceiptHash { get; }
        internal IReadOnlyList<DurableCompositeEndpointReceipt> TerminalEndpoints => _terminalEndpoints;
        internal string AcknowledgementHash { get; }
        internal bool ReconciliationPending =>
            (Phase == DurableCompositeOperationPhase.Committed ||
             Phase == DurableCompositeOperationPhase.Aborted) &&
            ReconciliationRequirement != null &&
            string.IsNullOrEmpty(AcknowledgementHash);
        internal byte[] ExactIntentUnsafe => _exactIntent;

        internal DurableCompositeOperationIntent ToIntent(ModuleRegistration ownerModule)
        {
            if (CompactedAtCheckpoint)
                throw new InvalidOperationException(
                    "A checkpoint-compacted receipt no longer carries mutation bytes.");
            RequireOwner(ownerModule);
            var intent = new DurableCompositeOperationIntent(
                ownerModule,
                OperationToken,
                ActorIdentity,
                EndpointDomainId,
                RequestHash,
                _exactIntent,
                _endpoints,
                ReconciliationRequirement);
            if (!string.Equals(intent.IntentHash, IntentHash, StringComparison.Ordinal))
                throw new InvalidDataException("The reconstructed root intent hash changed.");
            return intent;
        }

        internal DurableCompositeOperationReference ToReference(ModuleRegistration ownerModule)
        {
            RequireOwner(ownerModule);
            return new DurableCompositeOperationReference(
                ownerModule, OperationToken, ActorIdentity, RequestHash, IntentHash);
        }

        internal DurableCompositeMutationContext ToMutationContext() =>
            CompactedAtCheckpoint
                ? throw new InvalidOperationException(
                    "A checkpoint-compacted root cannot be replayed as a mutation.")
                :
            new DurableCompositeMutationContext(
                OwnerModuleId,
                EndpointDomainId,
                EndpointDomainSchemaVersion,
                OperationToken,
                ActorIdentity,
                IntentHash,
                _exactIntent,
                CommitSequence);

        internal DurableCompositeRollbackContext ToRollbackContext() =>
            CompactedAtCheckpoint
                ? throw new InvalidOperationException(
                    "A checkpoint-compacted root cannot be compensated.")
                : new DurableCompositeRollbackContext(
                    OwnerModuleId,
                    EndpointDomainId,
                    EndpointDomainSchemaVersion,
                     OperationToken,
                     ActorIdentity,
                     IntentHash,
                     _exactIntent,
                     Phase,
                     CommitSequence);

        internal DurableCompositeEndpointClaim ClaimFor(DurableCompositeEndpointIntent endpoint) =>
            new DurableCompositeEndpointClaim(
                OwnerModuleId,
                EndpointDomainId,
                OperationId,
                ActorIdentity,
                IntentHash,
                endpoint);

        internal bool Matches(DurableCompositeOperationReference operation) =>
            operation != null &&
            string.Equals(operation.OwnerModuleId, OwnerModuleId, StringComparison.Ordinal) &&
            operation.OperationId == OperationId &&
            string.Equals(
                operation.OperationToken.CanonicalValue,
                OperationToken.CanonicalValue,
                StringComparison.Ordinal) &&
            CompositeValidation.SameIdentity(operation.ActorIdentity, ActorIdentity) &&
            string.Equals(operation.RequestHash, RequestHash, StringComparison.Ordinal) &&
            string.Equals(operation.IntentHash, IntentHash, StringComparison.Ordinal);

        internal CompositeRootRecord WithPhase(DurableCompositeOperationPhase phase) =>
            Copy(phase, 0, Array.Empty<DurableCompositeEndpointPredecessor>(),
                string.Empty, Array.Empty<DurableCompositeEndpointReceipt>(), string.Empty);

        internal CompositeRootRecord WithCommitOrder(DurableCompositeCommitOrder order)
        {
            if (order == null) throw new ArgumentNullException(nameof(order));
            return Copy(
                DurableCompositeOperationPhase.Committing,
                order.CommitSequence,
                order.Predecessors,
                string.Empty,
                Array.Empty<DurableCompositeEndpointReceipt>(),
                string.Empty);
        }

        internal CompositeRootRecord WithTerminal(
            DurableCompositeOperationPhase phase,
            IEnumerable<DurableCompositeEndpointReceipt> endpoints)
        {
            var copy = endpoints.ToArray();
            string hash = DurableCompositeCodec.ComputeReceiptHash(this, phase, copy);
            return Copy(
                phase,
                phase == DurableCompositeOperationPhase.Committed ? CommitSequence : 0,
                phase == DurableCompositeOperationPhase.Committed
                    ? _predecessors
                    : Array.Empty<DurableCompositeEndpointPredecessor>(),
                hash,
                copy,
                string.Empty);
        }

        internal CompositeRootRecord WithAcknowledgement(string acknowledgementHash) =>
            Copy(
                Phase,
                CommitSequence,
                _predecessors,
                ReceiptHash,
                _terminalEndpoints,
                CompositeValidation.RequireSha256(
                    acknowledgementHash, nameof(acknowledgementHash)));

        internal CompositeRootRecord CompactAtCheckpoint()
        {
            if (Phase != DurableCompositeOperationPhase.Committed)
                throw new InvalidOperationException("Only a committed root can be compacted.");
            if (CompactedAtCheckpoint) return this;
            var compactEndpoints = _endpoints.Select(endpoint =>
                new DurableCompositeEndpointIntent(
                    endpoint.StableEndpointId,
                    endpoint.BeforeFingerprint,
                    endpoint.AfterFingerprint,
                    endpoint.PreparedSemanticRevision,
                    endpoint.AfterSemanticRevision,
                    Array.Empty<byte>())).ToArray();
            return new CompositeRootRecord(
                OwnerModuleId,
                OwnerModuleVersion,
                OwnerProtocolMajor,
                EndpointDomainId,
                EndpointDomainSchemaVersion,
                OperationToken,
                ActorIdentity,
                RequestHash,
                IntentHash,
                Array.Empty<byte>(),
                compactEndpoints,
                ReconciliationRequirement,
                true,
                Phase,
                CommitSequence,
                _predecessors,
                ReceiptHash,
                _terminalEndpoints,
                AcknowledgementHash);
        }

        internal DurableCompositeOperationSnapshot ToSnapshot(ModuleRegistration ownerModule)
        {
            DurableCompositeOperationReference reference = ToReference(ownerModule);
            DurableCompositeReceipt receipt = null;
            if (Phase == DurableCompositeOperationPhase.Committed ||
                Phase == DurableCompositeOperationPhase.Aborted)
                receipt = new DurableCompositeReceipt(
                    reference, Phase, ReceiptHash, _terminalEndpoints);
            return new DurableCompositeOperationSnapshot(
                reference,
                Phase,
                receipt,
                CommitSequence,
                !string.IsNullOrEmpty(AcknowledgementHash));
        }

        private CompositeRootRecord Copy(
            DurableCompositeOperationPhase phase,
            long commitSequence,
            IEnumerable<DurableCompositeEndpointPredecessor> predecessors,
            string receiptHash,
            IEnumerable<DurableCompositeEndpointReceipt> terminalEndpoints,
            string acknowledgementHash) =>
            new CompositeRootRecord(
                OwnerModuleId,
                OwnerModuleVersion,
                OwnerProtocolMajor,
                EndpointDomainId,
                EndpointDomainSchemaVersion,
                OperationToken,
                ActorIdentity,
                RequestHash,
                IntentHash,
                _exactIntent,
                _endpoints,
                ReconciliationRequirement,
                CompactedAtCheckpoint,
                phase,
                commitSequence,
                predecessors,
                receiptHash,
                terminalEndpoints,
                acknowledgementHash);

        private void RequireOwner(ModuleRegistration ownerModule)
        {
            if (ownerModule == null ||
                !string.Equals(
                    ownerModule.Descriptor.ModuleId, OwnerModuleId, StringComparison.Ordinal))
                throw new InvalidOperationException("The active owner module does not own this root.");
        }
    }

    internal static class DurableCompositeCodec
    {
        private const int Magic = 0x52444352; // RDCR
        private const int Schema = 6;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        internal static string ComputeIntentHash(
            string endpointDomainId,
            byte[] exactIntent,
            IReadOnlyList<DurableCompositeEndpointIntent> endpoints)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, StrictUtf8, true))
            {
                WriteText(writer, "runic.transactions.composite.intent.v2", 64);
                WriteText(writer, endpointDomainId, 160);
                WriteBytes(writer, exactIntent, DurableCompositeLimits.MaximumIntentBytes);
                writer.Write(endpoints.Count);
                foreach (DurableCompositeEndpointIntent endpoint in endpoints)
                {
                    WriteText(writer, endpoint.StableEndpointId.Value, 160);
                    WriteText(writer, endpoint.BeforeFingerprint, 64);
                    WriteText(writer, endpoint.AfterFingerprint, 64);
                    WriteText(
                        writer,
                        endpoint.PreparedSemanticRevision,
                        DurableCompositeLimits.MaximumSemanticRevisionBytes);
                    WriteText(
                        writer,
                        endpoint.AfterSemanticRevision,
                        DurableCompositeLimits.MaximumSemanticRevisionBytes);
                    WriteBytes(
                        writer,
                        endpoint.ExactMutationUnsafe,
                        DurableCompositeLimits.MaximumIntentBytes);
                }
                writer.Flush();
                return Sha256Hex(stream.ToArray());
            }
        }

        internal static string ComputeReceiptHash(
            CompositeRootRecord root,
            DurableCompositeOperationPhase phase,
            IReadOnlyList<DurableCompositeEndpointReceipt> endpoints)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, StrictUtf8, true))
            {
                WriteText(writer, "runic.transactions.composite.receipt.v2", 64);
                WriteText(writer, root.OwnerModuleId, 160);
                WriteText(writer, root.EndpointDomainId, 160);
                WriteText(writer, root.OperationToken.CanonicalValue, 192);
                WriteIdentity(writer, root.ActorIdentity);
                WriteText(writer, root.RequestHash, 64);
                WriteText(writer, root.IntentHash, 64);
                writer.Write((int)phase);
                writer.Write(root.CommitSequence);
                writer.Write(endpoints.Count);
                foreach (DurableCompositeEndpointReceipt endpoint in endpoints)
                {
                    WriteText(writer, endpoint.EndpointId.Value, 160);
                    WriteText(writer, endpoint.Fingerprint, 64);
                    WriteText(
                        writer,
                        endpoint.SemanticRevision,
                        DurableCompositeLimits.MaximumSemanticRevisionBytes);
                    WriteTextOrEmpty(writer, endpoint.AppliedWorldEpoch, 32);
                    writer.Write(endpoint.AppliedCommitSequence);
                }
                writer.Flush();
                return Sha256Hex(stream.ToArray());
            }
        }

        internal static byte[] Encode(CompositeRootRecord root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, StrictUtf8, true))
            {
                writer.Write(Magic);
                writer.Write(Schema);
                WriteText(writer, root.OwnerModuleId, 160);
                WriteText(writer, root.OwnerModuleVersion, 64);
                writer.Write(root.OwnerProtocolMajor);
                WriteText(writer, root.EndpointDomainId, 160);
                writer.Write(root.EndpointDomainSchemaVersion);
                WriteText(writer, root.OperationToken.CanonicalValue, 192);
                WriteIdentity(writer, root.ActorIdentity);
                WriteText(writer, root.RequestHash, 64);
                WriteText(writer, root.IntentHash, 64);
                writer.Write(root.CompactedAtCheckpoint);
                WriteBytes(writer, root.ExactIntentUnsafe, DurableCompositeLimits.MaximumIntentBytes);
                WriteReconciliation(writer, root.ReconciliationRequirement);
                writer.Write(root.Endpoints.Count);
                foreach (DurableCompositeEndpointIntent endpoint in root.Endpoints)
                {
                    WriteText(writer, endpoint.StableEndpointId.Value, 160);
                    WriteText(writer, endpoint.BeforeFingerprint, 64);
                    WriteText(writer, endpoint.AfterFingerprint, 64);
                    WriteText(
                        writer,
                        endpoint.PreparedSemanticRevision,
                        DurableCompositeLimits.MaximumSemanticRevisionBytes);
                    WriteText(
                        writer,
                        endpoint.AfterSemanticRevision,
                        DurableCompositeLimits.MaximumSemanticRevisionBytes);
                    WriteBytes(
                        writer,
                        endpoint.ExactMutationUnsafe,
                        DurableCompositeLimits.MaximumIntentBytes);
                }
                writer.Write((int)root.Phase);
                writer.Write(root.CommitSequence);
                writer.Write(root.Predecessors.Count);
                foreach (DurableCompositeEndpointPredecessor predecessor in root.Predecessors)
                {
                    WriteText(writer, predecessor.EndpointId.Value, 160);
                    writer.Write(predecessor.CommitSequence);
                    WriteText(writer, predecessor.OperationId, 32);
                }
                WriteTextOrEmpty(writer, root.ReceiptHash, 64);
                writer.Write(root.TerminalEndpoints.Count);
                foreach (DurableCompositeEndpointReceipt endpoint in root.TerminalEndpoints)
                {
                    WriteText(writer, endpoint.EndpointId.Value, 160);
                    WriteText(writer, endpoint.Fingerprint, 64);
                    WriteText(
                        writer,
                        endpoint.SemanticRevision,
                        DurableCompositeLimits.MaximumSemanticRevisionBytes);
                    WriteTextOrEmpty(writer, endpoint.AppliedWorldEpoch, 32);
                    writer.Write(endpoint.AppliedCommitSequence);
                }
                WriteTextOrEmpty(writer, root.AcknowledgementHash, 64);
                writer.Flush();
                byte[] result = stream.ToArray();
                if (result.Length > DurableCompositeLimits.MaximumRootRecordBytes)
                    throw new InvalidDataException("The composite root exceeds 128 KiB.");
                return result;
            }
        }

        internal static bool TryDecode(
            byte[] exactRecord,
            out CompositeRootRecord root,
            out string failureCode)
        {
            root = null;
            failureCode = "composite.root.corrupt";
            if (exactRecord == null || exactRecord.Length < 32 ||
                exactRecord.Length > DurableCompositeLimits.MaximumRootRecordBytes)
                return false;
            try
            {
                using (var stream = new MemoryStream(exactRecord, false))
                using (var reader = new BinaryReader(stream, StrictUtf8, true))
                {
                    if (reader.ReadInt32() != Magic || reader.ReadInt32() != Schema) return false;
                    string moduleId = ReadText(reader, 160, false);
                    string moduleVersion = ReadText(reader, 64, false);
                    int protocol = reader.ReadInt32();
                    string domainId = ReadText(reader, 160, false);
                    int domainSchema = reader.ReadInt32();
                    DurableCompositeOperationToken operationToken =
                        DurableCompositeOperationToken.Parse(ReadText(reader, 192, false));
                    RpcPeerIdentity actor = ReadIdentity(reader);
                    string requestHash = ReadText(reader, 64, false);
                    string intentHash = ReadText(reader, 64, false);
                    bool compacted = reader.ReadBoolean();
                    byte[] intent = ReadBytes(
                        reader, DurableCompositeLimits.MaximumIntentBytes, compacted);
                    DurableCompositeReconciliationRequirement reconciliation =
                        ReadReconciliation(reader);
                    int count = reader.ReadInt32();
                    if (count < 1 || count > DurableCompositeLimits.MaximumEndpoints) return false;
                    var endpoints = new DurableCompositeEndpointIntent[count];
                    for (int index = 0; index < count; index++)
                        endpoints[index] = new DurableCompositeEndpointIntent(
                            new EndpointId(ReadText(reader, 160, false)),
                            ReadText(reader, 64, false),
                            ReadText(reader, 64, false),
                            ReadText(
                                reader,
                                DurableCompositeLimits.MaximumSemanticRevisionBytes,
                                false),
                            ReadText(
                                reader,
                                DurableCompositeLimits.MaximumSemanticRevisionBytes,
                                false),
                            ReadBytes(reader, DurableCompositeLimits.MaximumIntentBytes, true));
                    var phase = (DurableCompositeOperationPhase)reader.ReadInt32();
                    long commitSequence = reader.ReadInt64();
                    int predecessorCount = reader.ReadInt32();
                    if (predecessorCount < 0 || predecessorCount > count) return false;
                    var predecessors = new DurableCompositeEndpointPredecessor[predecessorCount];
                    for (int index = 0; index < predecessorCount; index++)
                        predecessors[index] = new DurableCompositeEndpointPredecessor(
                            new EndpointId(ReadText(reader, 160, false)),
                            reader.ReadInt64(),
                            ReadText(reader, 32, false));
                    string receiptHash = ReadText(reader, 64, true);
                    int terminalCount = reader.ReadInt32();
                    if (terminalCount < 0 || terminalCount > count) return false;
                    var terminal = new DurableCompositeEndpointReceipt[terminalCount];
                    for (int index = 0; index < terminalCount; index++)
                        terminal[index] = new DurableCompositeEndpointReceipt(
                            new EndpointId(ReadText(reader, 160, false)),
                            ReadText(reader, 64, false),
                            ReadText(
                                reader,
                                DurableCompositeLimits.MaximumSemanticRevisionBytes,
                                false),
                            ReadText(reader, 32, true),
                            reader.ReadInt64());
                    string acknowledgementHash = ReadText(reader, 64, true);
                    if (stream.Position != stream.Length) return false;
                    root = new CompositeRootRecord(
                        moduleId,
                        moduleVersion,
                        protocol,
                        domainId,
                        domainSchema,
                        operationToken,
                        actor,
                        requestHash,
                        intentHash,
                        intent,
                        endpoints,
                        reconciliation,
                        compacted,
                        phase,
                        commitSequence,
                        predecessors,
                        receiptHash,
                        terminal,
                        acknowledgementHash);
                    failureCode = string.Empty;
                    return true;
                }
            }
            catch
            {
                root = null;
                failureCode = "composite.root.corrupt";
                return false;
            }
        }

        internal static string Sha256Hex(byte[] value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(value);
                var result = new StringBuilder(digest.Length * 2);
                for (int index = 0; index < digest.Length; index++)
                    result.Append(digest[index].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                return result.ToString();
            }
        }

        private static void WriteReconciliation(
            BinaryWriter writer,
            DurableCompositeReconciliationRequirement requirement)
        {
            writer.Write(requirement != null);
            if (requirement == null) return;
            WriteText(writer, requirement.ConsumerModuleId, 160);
            writer.Write(requirement.ConsumerProtocolMajor);
            WriteText(writer, requirement.RequiredProviderCapability, 160);
        }

        private static DurableCompositeReconciliationRequirement ReadReconciliation(
            BinaryReader reader)
        {
            if (!reader.ReadBoolean()) return null;
            return new DurableCompositeReconciliationRequirement(
                ReadText(reader, 160, false),
                reader.ReadInt32(),
                ReadText(reader, 160, false));
        }

        private static void WriteIdentity(BinaryWriter writer, RpcPeerIdentity identity)
        {
            WriteText(writer, identity.Authority, RpcPeerIdentity.MaximumAuthorityLength);
            WriteText(writer, identity.SubjectId, RpcPeerIdentity.MaximumSubjectLength * 4);
            writer.Write((int)identity.Assurance);
        }

        private static RpcPeerIdentity ReadIdentity(BinaryReader reader) =>
            new RpcPeerIdentity(
                ReadText(reader, RpcPeerIdentity.MaximumAuthorityLength, false),
                ReadText(reader, RpcPeerIdentity.MaximumSubjectLength * 4, false),
                (RpcIdentityAssurance)reader.ReadInt32());

        private static void WriteText(BinaryWriter writer, string value, int maximumBytes)
        {
            string exact = CompositeValidation.RequireText(value, nameof(value), maximumBytes);
            byte[] bytes = StrictUtf8.GetBytes(exact);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        private static void WriteTextOrEmpty(BinaryWriter writer, string value, int maximumBytes)
        {
            if (string.IsNullOrEmpty(value))
            {
                writer.Write(0);
                return;
            }
            WriteText(writer, value, maximumBytes);
        }

        private static string ReadText(BinaryReader reader, int maximumBytes, bool allowEmpty)
        {
            int length = reader.ReadInt32();
            if (length < (allowEmpty ? 0 : 1) || length > maximumBytes)
                throw new InvalidDataException("Text length is outside the canonical bound.");
            if (length == 0) return string.Empty;
            string result = StrictUtf8.GetString(ReadExact(reader, length));
            CompositeValidation.RequireText(result, nameof(result), maximumBytes);
            return result;
        }

        private static void WriteBytes(BinaryWriter writer, byte[] value, int maximumBytes)
        {
            value = value ?? Array.Empty<byte>();
            if (value.Length > maximumBytes) throw new ArgumentOutOfRangeException(nameof(value));
            writer.Write(value.Length);
            writer.Write(value);
        }

        private static byte[] ReadBytes(BinaryReader reader, int maximumBytes, bool allowEmpty)
        {
            int length = reader.ReadInt32();
            if (length < (allowEmpty ? 0 : 1) || length > maximumBytes)
                throw new InvalidDataException("Byte payload length is outside the canonical bound.");
            return ReadExact(reader, length);
        }

        private static byte[] ReadExact(BinaryReader reader, int length)
        {
            byte[] result = reader.ReadBytes(length);
            if (result.Length != length) throw new EndOfStreamException();
            return result;
        }
    }
}

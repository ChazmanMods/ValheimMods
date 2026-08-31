using System;
using Runic.Foundation.Core;
using Runic.Foundation.Persistence;

namespace Runic.Foundation.Transactions
{
    /// <summary>
    /// Account-scoped pre-PeerInfo gate for unfinished durable work. The account always comes
    /// from Persistence's exact direct-session context; claims and module text cannot select a
    /// different ledger partition.
    /// </summary>
    internal static class DurableCompositePeerAdmissionPolicy
    {
        internal static RpcHandshakeClaimEvaluation Evaluate(
            IDurableCompositeOutstandingOperationSource source,
            RpcHandshakePeerContext context)
        {
            try
            {
                if (context == null)
                    return RpcHandshakeClaimEvaluation.Deny(
                        "transactions-reconciliation-context-required");
                // The evaluator is registered symmetrically because both peers publish the
                // Transactions module. Only the server is admitting a player/account. A client
                // sees the server as valheim.server/ConnectionBound and must never try to select
                // an account WAL partition from that non-account identity.
                if (!context.ReceiverIsServer)
                    return RpcHandshakeClaimEvaluation.Allow();
                if (!context.ConnectionCurrent || context.Identity == null ||
                    context.Identity.Assurance != RpcIdentityAssurance.BackendAccount)
                    return RpcHandshakeClaimEvaluation.Deny(
                        "transactions-reconciliation-identity-required");
                if (source == null)
                    return RpcHandshakeClaimEvaluation.Deny(
                        "transactions-reconciliation-ledger-unavailable");

                DurableCompositeOutstandingQueryResult outstanding =
                    source.QueryOutstanding(context.Identity);
                if (outstanding == null ||
                    outstanding.State != DurableCompositeOutstandingQueryState.Ready)
                    return RpcHandshakeClaimEvaluation.Deny(
                        "transactions-reconciliation-ledger-unavailable");

                foreach (DurableCompositeOutstandingOperation operation in outstanding.Operations)
                {
                    ModuleProtocolState consumer = FindModule(
                        context.RemoteHello,
                        operation.Requirement.ConsumerModuleId);
                    if (consumer == null)
                        return RpcHandshakeClaimEvaluation.Deny(
                            "transactions-reconciliation-consumer-missing");
                    if (consumer.ProtocolVersion !=
                            operation.Requirement.ConsumerProtocolMajor ||
                        !string.Equals(
                            consumer.SemanticVersion,
                            operation.OwnerModuleVersion,
                            StringComparison.Ordinal))
                        return RpcHandshakeClaimEvaluation.Deny(
                            "transactions-reconciliation-consumer-mismatch");
                    if (!HasCapability(
                            context.RemoteHello,
                            operation.Requirement.RequiredProviderCapability))
                        return RpcHandshakeClaimEvaluation.Deny(
                            "transactions-reconciliation-provider-missing");
                }

                foreach (DurableCompositeIssuedOperation issued in outstanding.IssuedOperations)
                {
                    ModuleProtocolState consumer = FindModule(
                        context.RemoteHello,
                        issued.OwnerModuleId);
                    if (consumer == null)
                        return RpcHandshakeClaimEvaluation.Deny(
                            "transactions-issued-consumer-missing");
                    if (consumer.ProtocolVersion != issued.OwnerProtocolMajor ||
                        !string.Equals(
                            consumer.SemanticVersion,
                            issued.OwnerModuleVersion,
                            StringComparison.Ordinal))
                        return RpcHandshakeClaimEvaluation.Deny(
                            "transactions-issued-consumer-mismatch");
                    if (!HasCapability(
                            context.RemoteHello,
                            RunicCapabilityIds.DurableCompositeOperations))
                        return RpcHandshakeClaimEvaluation.Deny(
                            "transactions-issued-provider-missing");
                }

                foreach (DurableCompositeJournaledOutstandingOperation journaled in
                         outstanding.JournaledOperations)
                {
                    ModuleProtocolState consumer = FindModule(
                        context.RemoteHello,
                        journaled.OwnerModuleId);
                    if (consumer == null ||
                        consumer.ProtocolVersion != journaled.OwnerProtocolMajor ||
                        !string.Equals(
                            consumer.SemanticVersion,
                            journaled.OwnerModuleVersion,
                            StringComparison.Ordinal))
                        return RpcHandshakeClaimEvaluation.Deny(
                            "transactions-journaled-consumer-mismatch");
                    if (!HasCapability(
                            context.RemoteHello,
                            RunicCapabilityIds.DurableCompositeOperations))
                        return RpcHandshakeClaimEvaluation.Deny(
                            "transactions-journaled-provider-missing");
                }

                return RpcHandshakeClaimEvaluation.Allow();
            }
            catch
            {
                return RpcHandshakeClaimEvaluation.Deny(
                    "transactions-reconciliation-evaluator-failed");
            }
        }

        private static ModuleProtocolState FindModule(
            ProtocolHello hello,
            string moduleId)
        {
            if (hello == null) return null;
            foreach (ModuleProtocolState module in hello.Modules)
                if (string.Equals(module.ModuleId, moduleId, StringComparison.Ordinal))
                    return module;
            return null;
        }

        private static bool HasCapability(ProtocolHello hello, string capabilityId)
        {
            if (hello == null) return false;
            foreach (ModuleProtocolState module in hello.Modules)
                foreach (string capability in module.Capabilities)
                    if (string.Equals(capability, capabilityId, StringComparison.Ordinal))
                        return true;
            return false;
        }
    }
}

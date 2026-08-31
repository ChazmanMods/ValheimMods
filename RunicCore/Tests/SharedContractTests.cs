using System;
using System.Collections.Generic;
using System.Linq;

namespace Runic.Foundation.Core.Tests
{
    internal static class SharedContractTests
    {
        internal static void Register()
        {
            TestRunner.Run("Item protection contract distinguishes domain from uncertainty", ItemProtectionIsBounded);
            TestRunner.Run("High-impact confirmation context is metadata-free and bounded", ConfirmationIsBounded);
            TestRunner.Run("High-impact confirmation calls require module lease provenance", ConfirmationRequiresLease);
            TestRunner.Run("World declarations stop duplicate floods after the inspection cap", WorldInputsAreBounded);
            TestRunner.Run("Durable inventory intent and custody are immutable and bounded", DurableInventoryContractsAreBounded);
            TestRunner.Run("Durable inventory service contract contains no Valheim types", DurableInventoryContractIsProviderNeutral);
        }

        private static void ItemProtectionIsBounded()
        {
            TestAssert.Equal(0, (int)ItemProtectionState.Unknown);
            TestAssert.Equal(1, (int)ItemProtectionState.Unlocked);
            TestAssert.Equal(2, (int)ItemProtectionState.Locked);
            IItemProtectionQuery outside = new OutOfDomainProtectionQuery();
            TestAssert.False(outside.TryGetProtection(new object(), out ItemProtectionState state));
            TestAssert.Equal(ItemProtectionState.Unknown, state);
            IItemProtectionQuery uncertain = new InDomainUnknownProtectionQuery();
            TestAssert.True(uncertain.TryGetProtection(new object(), out state));
            TestAssert.Equal(ItemProtectionState.Unknown, state);
        }

        private static void ConfirmationIsBounded()
        {
            var context = new ConfirmationContext(
                "runic.portals",
                HighImpactOperation.PortalOverwrite,
                "zdo:1/2",
                new string('A', 64));
            TestAssert.Equal("runic.portals", context.RequesterModuleId);
            TestAssert.Equal(HighImpactOperation.PortalOverwrite, context.Operation);
            TestAssert.Throws<ArgumentOutOfRangeException>(() => new ConfirmationContext(
                "runic.portals",
                HighImpactOperation.Unknown,
                "zdo:1/2",
                "AA"));
            TestAssert.Throws<ArgumentException>(() => new ConfirmationContext(
                "runic.portals",
                HighImpactOperation.PortalOverwrite,
                "raw\nmetadata",
                "AA"));
            TestAssert.Throws<ArgumentOutOfRangeException>(() => new ConfirmationContext(
                "runic.portals",
                HighImpactOperation.PortalOverwrite,
                "target",
                new string('A', 129)));
        }

        private static void ConfirmationRequiresLease()
        {
            Type[] requestParameters = Array.ConvertAll(
                typeof(IHighImpactConfirmation).GetMethod("TryRequest").GetParameters(),
                value => value.ParameterType);
            TestAssert.Equal(typeof(ModuleRegistration), requestParameters[0]);
            Type[] cancelParameters = Array.ConvertAll(
                typeof(IHighImpactConfirmation).GetMethod("Cancel").GetParameters(),
                value => value.ParameterType);
            TestAssert.Equal(typeof(ModuleRegistration), cancelParameters[0]);
        }

        private static void WorldInputsAreBounded()
        {
            TestAssert.Throws<ArgumentOutOfRangeException>(() => new WorldDataOwnershipDeclaration(
                "runic.test",
                "1",
                Enumerable.Repeat(7, 10_000),
                null,
                null));
            var declaration = new WorldDataOwnershipDeclaration(
                "runic.test",
                "1",
                new[] { 2, 1, 2 },
                new[] { "runic.test.key" },
                null);
            TestAssert.Equal(2, declaration.PrefabHashes.Count);
            TestAssert.Throws<NotSupportedException>(() =>
                ((IList<int>)declaration.PrefabHashes).Add(3));
        }

        private static void DurableInventoryContractsAreBounded()
        {
            Guid operation = Guid.ParseExact("11111111111111111111111111111111", "N");
            byte[] manifest = { 1, 2, 3 };
            var intent = new InventoryDurableOperationIntent(
                operation,
                "runic.storage",
                "storage.quick-stack",
                InventoryDurableMutationKind.ItemDebit,
                "runic.storage.operation",
                manifest);
            manifest[0] = 99;
            TestAssert.Equal((byte)1, intent.ExactManifest[0]);
            byte[] copy = intent.ExactManifest;
            copy[0] = 88;
            TestAssert.Equal((byte)1, intent.ExactManifest[0]);
            TestAssert.Equal(64, intent.ManifestSha256.Length);
            string serverToken = new string('a', 32) + ":0000000000000001:" +
                                 operation.ToString("N") + ":" + new string('b', 64);
            var remoteIntent = new InventoryDurableOperationIntent(
                operation,
                "runic.storage",
                "storage.quick-stack",
                InventoryDurableMutationKind.ItemDebit,
                "runic.storage.operation",
                serverToken,
                new byte[] { 1 });
            TestAssert.Equal(serverToken, remoteIntent.NativeTagValue);
            TestAssert.Throws<ArgumentException>(() => new InventoryDurableOperationIntent(
                operation, "runic.storage", "storage.quick-stack",
                InventoryDurableMutationKind.ItemDebit, "runic.storage.operation",
                "token with whitespace", new byte[] { 1 }));
            TestAssert.Throws<ArgumentException>(() => new InventoryDurableOperationIntent(
                operation, "runic.storage", "storage.quick-stack",
                InventoryDurableMutationKind.ItemDebit, "runic.storage.operation",
                "token-\u2603", new byte[] { 1 }));
            TestAssert.Throws<ArgumentOutOfRangeException>(() => new InventoryDurableOperationIntent(
                operation, "runic.storage", "storage.quick-stack",
                InventoryDurableMutationKind.ItemDebit, "runic.storage.operation",
                new string('x', 193), new byte[] { 1 }));
            TestAssert.Throws<ArgumentException>(() => new InventoryDurableOperationIntent(
                operation,
                "runic.storage",
                "storage.quick-stack",
                InventoryDurableMutationKind.ItemDebit,
                "runic.other.operation",
                new byte[] { 1 }));
            TestAssert.Throws<ArgumentOutOfRangeException>(() =>
                new InventoryDurableOperationIntent(
                    operation,
                    "runic.storage",
                    "storage.quick-stack",
                    InventoryDurableMutationKind.ItemDebit,
                    "runic.storage.operation",
                    new byte[InventoryDurableOperationIntent.MaximumManifestBytes + 1]));

            var custody = new InventoryDurableCustodySnapshot(
                InventoryDurableCustodyKind.Tombstone,
                123L,
                7U,
                456L,
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                new string('b', 64),
                2,
                17);
            TestAssert.True(custody.IsPresent);
            TestAssert.False(custody.IsResolved);
            TestAssert.Equal(17, custody.TaggedQuantity);
            TestAssert.Equal(
                InventoryDurableCustodyEvidenceSource.LocalVanillaCapture,
                custody.EvidenceSource);

            var custodyResolution = new InventoryDurableCustodyResolution(
                InventoryDurableCustodyResolutionOutcome.RemoteCommittedApplied,
                serverToken,
                new string('b', 64),
                2,
                17,
                new string('e', 64),
                0,
                0,
                new string('f', 64));
            var resolvedCustody = new InventoryDurableCustodySnapshot(
                InventoryDurableCustodyKind.Tombstone,
                123L,
                7U,
                456L,
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                new string('b', 64),
                2,
                17,
                custodyResolution);
            TestAssert.True(resolvedCustody.IsResolved);
            TestAssert.Equal(
                InventoryDurableCustodyResolutionOutcome.RemoteCommittedApplied,
                resolvedCustody.Resolution.Outcome);
            TestAssert.Equal(new string('f', 64),
                resolvedCustody.Resolution.DurableServerReceiptSha256);

            var serverBinding = new InventoryDurableServerCustodyBinding(
                operation,
                serverToken,
                new string('1', 64),
                new string('2', 64),
                1,
                new string('3', 64),
                new string('4', 64),
                new string('5', 64),
                new string('6', 64));
            var recoveredCustody = new InventoryDurableCustodySnapshot(
                InventoryDurableCustodyKind.Tombstone,
                123L,
                7U,
                456L,
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                new string('b', 64),
                2,
                17,
                InventoryDurableCustodyEvidenceSource.DurableServerRecovery,
                custodyResolution,
                serverBinding);
            TestAssert.Equal(
                InventoryDurableCustodyEvidenceSource.DurableServerRecovery,
                recoveredCustody.EvidenceSource);
            TestAssert.Equal(operation, recoveredCustody.ServerRecoveryBinding.OperationId);
            TestAssert.Equal(1,
                recoveredCustody.ServerRecoveryBinding.ExactTaggedGraveCandidateCount);
            TestAssert.Throws<ArgumentOutOfRangeException>(() =>
                new InventoryDurableServerCustodyBinding(
                    operation, serverToken, new string('1', 64), new string('2', 64),
                    2, new string('3', 64), new string('4', 64),
                    new string('5', 64), new string('6', 64)));
            TestAssert.Throws<ArgumentOutOfRangeException>(() =>
                new InventoryDurableServerCustodyBinding(
                    operation, serverToken, new string('1', 64), new string('2', 64),
                    0, new string('3', 64), new string('4', 64),
                    new string('5', 64), new string('6', 64)));
            TestAssert.Throws<ArgumentException>(() =>
                new InventoryDurableServerCustodyBinding(
                    operation, serverToken, new string('1', 64), new string('2', 64),
                    1, new string('3', 64), new string('4', 64),
                    new string('5', 64), new string('5', 64)));
            TestAssert.Throws<ArgumentException>(() => new InventoryDurableCustodySnapshot(
                InventoryDurableCustodyKind.Tombstone,
                123L,
                7U,
                456L,
                string.Empty,
                new string('b', 64),
                2,
                17,
                InventoryDurableCustodyEvidenceSource.DurableServerRecovery,
                custodyResolution,
                serverBinding));

            var outstanding = new InventoryDurableOutstandingOperation(
                remoteIntent,
                InventoryDurableOutstandingOutcome.RemoteCommitted,
                InventoryDurableProfileSource.Cloud,
                new string('1', 64),
                new string('2', 64),
                new string('3', 64),
                new string('4', 64),
                new string('5', 64),
                new string('6', 64),
                new string('a', 64),
                new string('c', 64));
            InventoryDurableOutstandingSnapshot outstandingSnapshot =
                outstanding.CreateSnapshot(
                    InventoryDurableOutstandingObservation.Prepared);
            TestAssert.Equal(operation, outstandingSnapshot.OperationId);
            TestAssert.Equal(remoteIntent.ManifestSha256, outstandingSnapshot.ManifestSha256);
            TestAssert.Equal(
                InventoryDurableOutstandingObservation.Prepared,
                outstandingSnapshot.Observation);
            TestAssert.Equal(string.Empty,
                outstandingSnapshot.FreshSessionObservationSha256);
            InventoryDurableOutstandingSnapshot observed =
                outstandingSnapshot.WithFreshSessionObservation(new string('d', 64));
            TestAssert.Equal(new string('d', 64),
                observed.FreshSessionObservationSha256);
            TestAssert.Equal(string.Empty,
                outstandingSnapshot.FreshSessionObservationSha256);
            var preparedIsTerminal = new InventoryDurableOutstandingOperation(
                remoteIntent,
                InventoryDurableOutstandingOutcome.RemoteCommitted,
                InventoryDurableProfileSource.Cloud,
                new string('1', 64),
                new string('2', 64),
                new string('2', 64),
                new string('4', 64),
                new string('5', 64),
                new string('6', 64),
                new string('a', 64),
                new string('c', 64));
            TestAssert.Equal(
                preparedIsTerminal.PreparedInventorySha256,
                preparedIsTerminal.ExpectedAfterInventorySha256,
                "A reversible local preparation may already be the terminal committed state.");
            TestAssert.Throws<ArgumentException>(() =>
                new InventoryDurableOutstandingOperation(
                    remoteIntent,
                    InventoryDurableOutstandingOutcome.RemoteCommitted,
                    InventoryDurableProfileSource.Cloud,
                    new string('1', 64),
                    new string('1', 64),
                    new string('3', 64),
                    new string('4', 64),
                    new string('5', 64),
                    new string('6', 64),
                    new string('a', 64),
                    new string('c', 64)));
            TestAssert.Throws<ArgumentOutOfRangeException>(() =>
                new InventoryDurableOutstandingOperation(
                    remoteIntent,
                    InventoryDurableOutstandingOutcome.RemoteCommitted,
                    InventoryDurableProfileSource.Unresolved,
                    new string('1', 64),
                    new string('2', 64),
                    new string('3', 64),
                    new string('4', 64),
                    new string('5', 64),
                    new string('6', 64),
                    new string('a', 64),
                    new string('c', 64)));
            var freshProof = new InventoryDurableFreshSessionProof(
                operation,
                serverToken,
                new string('4', 64),
                new string('5', 64),
                new string('a', 64),
                new string('c', 64),
                new string('6', 64),
                new string('d', 64),
                new string('3', 64),
                true,
                false);
            TestAssert.True(freshProof.ObservedBeforeAnyMutationThisSession);
            TestAssert.False(freshProof.MutationIssuedThisSession);
            TestAssert.Throws<ArgumentException>(() => new InventoryDurableCustodySnapshot(
                InventoryDurableCustodyKind.Tombstone,
                123L,
                7U,
                456L,
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                new string('0', 64),
                2,
                17,
                custodyResolution));
            TestAssert.Throws<ArgumentException>(() => new InventoryDurableCustodySnapshot(
                InventoryDurableCustodyKind.None,
                1L,
                0U,
                0L,
                string.Empty,
                string.Empty,
                0,
                0));
            TestAssert.Throws<ArgumentException>(() => new InventoryDurableCustodySnapshot(
                InventoryDurableCustodyKind.Tombstone,
                1L,
                2U,
                3L,
                string.Empty,
                new string('A', 64),
                0,
                0));

            var snapshot = new InventoryDurableOperationSnapshot(
                operation,
                "runic.storage",
                "storage.quick-stack",
                InventoryDurableMutationKind.ItemDebit,
                InventoryDurableOperationPhase.Journaled,
                "runic.storage.operation",
                new string('c', 64),
                new string('d', 64),
                string.Empty,
                InventoryProfileReadbackState.NotAttempted,
                string.Empty,
                true,
                custody,
                serverToken,
                preparedInventorySha256: new string('e', 64));
            TestAssert.Equal(operation, snapshot.OperationId);
            TestAssert.Equal(custody, snapshot.Custody);
            TestAssert.Equal(serverToken, snapshot.NativeTagValue);
            TestAssert.Equal(new string('e', 64), snapshot.PreparedInventorySha256);
        }

        private static void DurableInventoryContractIsProviderNeutral()
        {
            Type contract = typeof(IInventoryDurableOperationService);
            TestAssert.Equal("Runic.Foundation.Core", contract.Namespace);
            foreach (System.Reflection.MethodInfo method in contract.GetMethods())
            {
                TestAssert.False(IsValheimType(method.ReturnType), method.Name + " returns a Valheim type.");
                foreach (System.Reflection.ParameterInfo parameter in method.GetParameters())
                    TestAssert.False(IsValheimType(parameter.ParameterType),
                        method.Name + " accepts a Valheim type.");
            }
            TestAssert.Equal(
                "inventory.durable-operations",
                RunicCapabilityIds.InventoryDurableOperations);
            TestAssert.Equal(
                "runic.core.custody-schema",
                InventoryDurableCustodyMetadata.CustodySchemaKey);
            TestAssert.Equal(1, InventoryDurableCustodyMetadata.CustodySchemaVersion);
            TestAssert.Equal(
                "runic.core.custody-token",
                InventoryDurableCustodyMetadata.CustodyTokenKey);
            TestAssert.Equal(147,
                InventoryDurableCustodyMetadata.CanonicalOperationTokenCharacters);
            TestAssert.Equal(192,
                InventoryDurableCustodyMetadata.MaximumOpaqueTokenCharacters);
            System.Reflection.MethodInfo manifest = contract.GetMethod("TryReadExactManifest");
            TestAssert.True(manifest != null);
            System.Reflection.ParameterInfo[] parameters = manifest.GetParameters();
            TestAssert.Equal(typeof(byte[]).MakeByRefType(), parameters[2].ParameterType);
            TestAssert.True(parameters[2].IsOut,
                "Exact manifest disclosure must use an explicit cloned out value.");
            System.Reflection.MethodInfo custody = contract.GetMethod("TryResolveCustody");
            TestAssert.True(custody != null);
            TestAssert.Equal(
                typeof(InventoryDurableCustodyResolution),
                custody.GetParameters()[2].ParameterType);
            System.Reflection.MethodInfo recovery = contract.GetMethod("TryRecoverCustody");
            TestAssert.True(recovery != null);
            TestAssert.Equal(
                typeof(InventoryDurableCustodySnapshot),
                recovery.GetParameters()[2].ParameterType);
            TestAssert.True(contract.GetMethod("TryGetProfileSource") != null);
            System.Reflection.MethodInfo adopt = contract.GetMethod("TryAdoptOutstanding");
            TestAssert.True(adopt != null);
            TestAssert.Equal(
                typeof(InventoryDurableOutstandingOperation),
                adopt.GetParameters()[0].ParameterType);
            System.Reflection.MethodInfo fresh = contract.GetMethod("TryProveFreshSession");
            TestAssert.True(fresh != null);
            TestAssert.Equal(
                typeof(InventoryDurableFreshSessionProof),
                fresh.GetParameters()[2].ParameterType);
            System.Reflection.MethodInfo prepared = contract.GetMethod("TryCaptureLocalPrepared");
            TestAssert.True(prepared != null);
            TestAssert.Equal(4, prepared.GetParameters().Length);
            System.Reflection.MethodInfo forecast = contract.GetMethod("TryForecastOwnedMutation");
            TestAssert.True(forecast != null);
            System.Reflection.ParameterInfo[] forecastParameters = forecast.GetParameters();
            TestAssert.Equal(5, forecastParameters.Length);
            TestAssert.Equal(typeof(Func<byte[], byte[]>), forecastParameters[2].ParameterType);
            TestAssert.Equal(typeof(string).MakeByRefType(), forecastParameters[3].ParameterType);
            TestAssert.True(forecastParameters[3].IsOut);
        }

        private static bool IsValheimType(Type type)
        {
            Type exact = type.IsByRef ? type.GetElementType() : type;
            return exact != null && string.Equals(
                exact.Assembly.GetName().Name,
                "assembly_valheim",
                StringComparison.Ordinal);
        }

        private sealed class OutOfDomainProtectionQuery : IItemProtectionQuery
        {
            public bool TryGetProtection(object nativeItem, out ItemProtectionState state)
            {
                state = ItemProtectionState.Unknown;
                return false;
            }
        }

        private sealed class InDomainUnknownProtectionQuery : IItemProtectionQuery
        {
            public bool TryGetProtection(object nativeItem, out ItemProtectionState state)
            {
                state = ItemProtectionState.Unknown;
                return true;
            }
        }
    }
}

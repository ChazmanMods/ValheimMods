using System;
using RunicSafety.Api;
using RunicSafety.Services;

namespace RunicSafety.Tests
{
    internal static class ProtectedPolicyTests
    {
        internal static void Register()
        {
            TestRunner.Run("protected policy null item denies", NullItemDenies);
            TestRunner.Run("protected policy equipped item denies", EquippedDenies);
            TestRunner.Run("protected policy quest item denies", QuestDenies);
            TestRunner.Run("protected policy locked item denies", LockedDenies);
            TestRunner.Run("protected policy rare item requests confirmation", RareConfirms);
            TestRunner.Run("protected policy rare gate can be disabled", RareGateDisabled);
            TestRunner.Run("protected policy ordinary vanilla item allows", OrdinaryAllows);
            TestRunner.Run("protected policy advertised out-of-domain item still applies own rules", AdvertisedOutOfDomainUsesOwnRules);
            TestRunner.Run("protected policy missing advertised provider denies", MissingProviderDenies);
            TestRunner.Run("legacy provider cannot replace missing typed lock query", ProviderMismatchDenies);
            TestRunner.Run("protected policy unadvertised unknown lock does not invent provider requirement", UnadvertisedAllows);
            TestRunner.Run("protected policy provider deny wins", ProviderDenyWins);
            TestRunner.Run("protected policy provider confirmation propagates", ProviderConfirmationPropagates);
            TestRunner.Run("protected policy provider exception denies", ProviderExceptionDenies);
            TestRunner.Run("protected policy provider null response denies", ProviderNullDenies);
            TestRunner.Run("protected policy admin bypass off still denies", AdminBypassOff);
            TestRunner.Run("protected policy admin bypass on allows and records", AdminBypassOn);
            TestRunner.Run("protected policy duplicate provider ID rejected", DuplicateProviderRejected);
            TestRunner.Run("protected policy registration dispose detaches", RegistrationDispose);
            TestRunner.Run("protected policy provider limit is bounded", ProviderLimitBounded);
            TestRunner.Run("protected policy invalid provider ID rejected", InvalidProviderIdRejected);
            TestRunner.Run("protected request never requires native handle", NativeHandleOptional);
        }

        private static ProtectedItemPolicy Policy(
            bool rare = true,
            bool adminBypass = false,
            CorrelatedDiagnosticBuffer diagnostics = null) =>
            new ProtectedItemPolicy(
                diagnostics ?? new CorrelatedDiagnosticBuffer(),
                () => rare,
                () => adminBypass);

        private static ItemProtectionRequest Request(
            ProtectedItemDescriptor item,
            bool admin = false,
            bool advertised = false,
            object handle = null) =>
            new ItemProtectionRequest(
                item,
                ProtectionDestination.Obliterator,
                "protect-test",
                admin,
                advertised,
                handle);

        private static ProtectedItemDescriptor Item(
            bool equipped = false,
            bool quest = false,
            ItemLockState lockState = ItemLockState.NotApplicable,
            bool rare = false) =>
            new ProtectedItemDescriptor("Iron", equipped, quest, lockState, rare);

        private static void NullItemDenies() => TestAssert.Equal(
            ProtectionReason.InvalidRequest,
            Policy().Evaluate(Request(null)).Reason);

        private static void EquippedDenies() => TestAssert.Equal(
            ProtectionReason.Equipped,
            Policy().Evaluate(Request(Item(equipped: true))).Reason);

        private static void QuestDenies() => TestAssert.Equal(
            ProtectionReason.QuestItem,
            Policy().Evaluate(Request(Item(quest: true))).Reason);

        private static void LockedDenies() => TestAssert.Equal(
            ProtectionReason.Locked,
            Policy().Evaluate(Request(Item(lockState: ItemLockState.Locked))).Reason);

        private static void RareConfirms() => TestAssert.Equal(
            ProtectionOutcome.RequireConfirmation,
            Policy().Evaluate(Request(Item(rare: true))).Outcome);

        private static void RareGateDisabled() => TestAssert.Equal(
            ProtectionOutcome.Allow,
            Policy(rare: false).Evaluate(Request(Item(rare: true))).Outcome);

        private static void OrdinaryAllows() => TestAssert.Equal(
            ProtectionOutcome.Allow,
            Policy().Evaluate(Request(Item())).Outcome);

        private static void AdvertisedOutOfDomainUsesOwnRules()
        {
            TestAssert.Equal(
                ProtectionOutcome.Allow,
                Policy().Evaluate(Request(
                    Item(lockState: ItemLockState.NotApplicable),
                    advertised: true)).Outcome);
            TestAssert.Equal(
                ProtectionOutcome.RequireConfirmation,
                Policy().Evaluate(Request(
                    Item(lockState: ItemLockState.NotApplicable, rare: true),
                    advertised: true)).Outcome);
            TestAssert.Equal(
                ProtectionReason.QuestItem,
                Policy().Evaluate(Request(
                    Item(quest: true, lockState: ItemLockState.NotApplicable),
                    advertised: true)).Reason);
        }

        private static void MissingProviderDenies() => TestAssert.Equal(
            ProtectionReason.ProviderUnavailable,
            Policy().Evaluate(Request(Item(lockState: ItemLockState.Unknown), advertised: true)).Reason);

        private static void UnadvertisedAllows() => TestAssert.Equal(
            ProtectionOutcome.Allow,
            Policy().Evaluate(Request(Item(lockState: ItemLockState.Unknown), advertised: false)).Outcome);

        private static void ProviderMismatchDenies()
        {
            ProtectedItemPolicy policy = Policy();
            policy.RegisterProvider(new FixedProvider("test.allow", ProtectionOutcome.Allow), 100);
            ItemProtectionDecision decision = policy.Evaluate(
                Request(Item(lockState: ItemLockState.Unknown), advertised: true));
            TestAssert.Equal(ProtectionOutcome.Deny, decision.Outcome);
            TestAssert.Equal(ProtectionReason.ProviderUnavailable, decision.Reason);
        }

        private static void ProviderDenyWins()
        {
            ProtectedItemPolicy policy = Policy();
            policy.RegisterProvider(new FixedProvider("test.allow", ProtectionOutcome.Allow), 100);
            policy.RegisterProvider(new FixedProvider("test.deny", ProtectionOutcome.Deny), 0);
            TestAssert.Equal(ProtectionOutcome.Deny,
                policy.Evaluate(Request(Item(lockState: ItemLockState.Unlocked), advertised: true)).Outcome);
        }

        private static void ProviderConfirmationPropagates()
        {
            ProtectedItemPolicy policy = Policy();
            policy.RegisterProvider(new FixedProvider("test.confirm", ProtectionOutcome.RequireConfirmation));
            TestAssert.Equal(ProtectionOutcome.RequireConfirmation,
                policy.Evaluate(Request(Item(lockState: ItemLockState.Unlocked), advertised: true)).Outcome);
        }

        private static void ProviderExceptionDenies()
        {
            ProtectedItemPolicy policy = Policy();
            policy.RegisterProvider(new ThrowingProvider());
            TestAssert.Equal(ProtectionReason.ProviderFailure,
                policy.Evaluate(Request(Item(lockState: ItemLockState.Unlocked), advertised: true)).Reason);
        }

        private static void ProviderNullDenies()
        {
            ProtectedItemPolicy policy = Policy();
            policy.RegisterProvider(new NullProvider());
            TestAssert.Equal(ProtectionReason.ProviderFailure,
                policy.Evaluate(Request(Item(lockState: ItemLockState.Unlocked), advertised: true)).Reason);
        }

        private static void AdminBypassOff() => TestAssert.Equal(
            ProtectionOutcome.Deny,
            Policy(adminBypass: false).Evaluate(Request(Item(equipped: true), admin: true)).Outcome);

        private static void AdminBypassOn()
        {
            var diagnostics = new CorrelatedDiagnosticBuffer();
            ItemProtectionDecision decision = Policy(adminBypass: true, diagnostics: diagnostics)
                .Evaluate(Request(Item(equipped: true), admin: true));
            TestAssert.Equal(ProtectionReason.AdministratorBypass, decision.Reason);
            TestAssert.True(diagnostics.Count > 0);
        }

        private static void DuplicateProviderRejected()
        {
            ProtectedItemPolicy policy = Policy();
            policy.RegisterProvider(new FixedProvider("test.same", ProtectionOutcome.Allow));
            TestAssert.Throws<InvalidOperationException>(() =>
                policy.RegisterProvider(new FixedProvider("test.same", ProtectionOutcome.Allow)));
        }

        private static void RegistrationDispose()
        {
            ProtectedItemPolicy policy = Policy();
            IDisposable registration = policy.RegisterProvider(
                new FixedProvider("test.dispose", ProtectionOutcome.Allow));
            TestAssert.Equal(1, policy.ProviderCount);
            registration.Dispose();
            registration.Dispose();
            TestAssert.Equal(0, policy.ProviderCount);
        }

        private static void ProviderLimitBounded()
        {
            ProtectedItemPolicy policy = Policy();
            for (int index = 0; index < 16; index++)
                policy.RegisterProvider(new FixedProvider("test.p" + index, ProtectionOutcome.Allow));
            TestAssert.Throws<InvalidOperationException>(() =>
                policy.RegisterProvider(new FixedProvider("test.overflow", ProtectionOutcome.Allow)));
        }

        private static void InvalidProviderIdRejected() =>
            TestAssert.Throws<ArgumentException>(() =>
                Policy().RegisterProvider(new FixedProvider("Bad Provider", ProtectionOutcome.Allow)));

        private static void NativeHandleOptional()
        {
            ItemProtectionRequest request = Request(Item(), handle: null);
            TestAssert.Equal(null, request.NativeItemHandle);
            TestAssert.Equal(ProtectionOutcome.Allow, Policy().Evaluate(request).Outcome);
        }

        private sealed class FixedProvider : IItemProtectionProvider
        {
            private readonly ProtectionOutcome _outcome;
            internal FixedProvider(string id, ProtectionOutcome outcome)
            {
                ProviderId = id;
                _outcome = outcome;
            }
            public string ProviderId { get; }
            public ItemProtectionDecision Evaluate(ItemProtectionRequest request) =>
                new ItemProtectionDecision(_outcome, ProtectionReason.ExternalPolicy, ProviderId);
        }

        private sealed class ThrowingProvider : IItemProtectionProvider
        {
            public string ProviderId => "test.throw";
            public ItemProtectionDecision Evaluate(ItemProtectionRequest request) =>
                throw new InvalidOperationException("fault injection");
        }

        private sealed class NullProvider : IItemProtectionProvider
        {
            public string ProviderId => "test.null";
            public ItemProtectionDecision Evaluate(ItemProtectionRequest request) => null;
        }
    }
}

using System;
using RunicSafety.Api;
using RunicSafety.Services;

namespace RunicSafety.Tests
{
    internal static class ConfirmationPolicyTests
    {
        private static readonly DateTime Now = new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);

        internal static void Register()
        {
            TestRunner.Run("confirmation first identical action requires repeat", FirstRequiresRepeat);
            TestRunner.Run("confirmation second identical action proceeds", SecondProceeds);
            TestRunner.Run("confirmation is one-shot", ConfirmationIsOneShot);
            TestRunner.Run("confirmation changed fingerprint resets", ChangedFingerprintResets);
            TestRunner.Run("confirmation changed context is independent", ChangedContextIndependent);
            TestRunner.Run("confirmation expiration resets", ExpirationResets);
            TestRunner.Run("disabled confirmation proceeds without state", DisabledProceeds);
            TestRunner.Run("empty context fails closed", EmptyContextInvalid);
            TestRunner.Run("oversized context fails closed", OversizedContextInvalid);
            TestRunner.Run("short window fails closed", ShortWindowInvalid);
            TestRunner.Run("long window fails closed", LongWindowInvalid);
            TestRunner.Run("confirmation cancellation removes all action variants", CancellationWorks);
            TestRunner.Run("confirmation clear removes pending state", ClearWorks);
            TestRunner.Run("confirmation cache stays bounded", CacheIsBounded);
            TestRunner.Run("confirmation correlations are non-empty and distinct", CorrelationsDistinct);
        }

        private static ContextualConfirmationService Service(int capacity = 8) =>
            new ContextualConfirmationService(new CorrelatedDiagnosticBuffer(64, () => Now), capacity);

        private static ConfirmationRequest Request(
            string context = "zdo:1",
            string fingerprint = "state-a",
            bool enabled = true,
            TimeSpan? window = null) =>
            new ConfirmationRequest(
                SafetyActionKind.PortalOverwrite,
                context,
                fingerprint,
                window ?? TimeSpan.FromSeconds(4),
                enabled);

        private static void FirstRequiresRepeat()
        {
            ConfirmationDecision decision = Service().Evaluate(Request(), Now);
            TestAssert.Equal(ConfirmationOutcome.ConfirmAgain, decision.Outcome);
        }

        private static void SecondProceeds()
        {
            ContextualConfirmationService service = Service();
            service.Evaluate(Request(), Now);
            TestAssert.True(service.Evaluate(Request(), Now.AddSeconds(1)).MayProceed);
        }

        private static void ConfirmationIsOneShot()
        {
            ContextualConfirmationService service = Service();
            service.Evaluate(Request(), Now);
            service.Evaluate(Request(), Now.AddSeconds(1));
            TestAssert.Equal(ConfirmationOutcome.ConfirmAgain,
                service.Evaluate(Request(), Now.AddSeconds(2)).Outcome);
        }

        private static void ChangedFingerprintResets()
        {
            ContextualConfirmationService service = Service();
            service.Evaluate(Request(), Now);
            TestAssert.Equal(ConfirmationOutcome.ConfirmAgain,
                service.Evaluate(Request(fingerprint: "state-b"), Now.AddSeconds(1)).Outcome);
        }

        private static void ChangedContextIndependent()
        {
            ContextualConfirmationService service = Service();
            service.Evaluate(Request(), Now);
            TestAssert.Equal(ConfirmationOutcome.ConfirmAgain,
                service.Evaluate(Request(context: "zdo:2"), Now.AddSeconds(1)).Outcome);
            TestAssert.Equal(2, service.PendingCount);
        }

        private static void ExpirationResets()
        {
            ContextualConfirmationService service = Service();
            service.Evaluate(Request(), Now);
            TestAssert.Equal(ConfirmationOutcome.ConfirmAgain,
                service.Evaluate(Request(), Now.AddSeconds(5)).Outcome);
        }

        private static void DisabledProceeds()
        {
            ContextualConfirmationService service = Service();
            TestAssert.True(service.Evaluate(Request(enabled: false), Now).MayProceed);
            TestAssert.Equal(0, service.PendingCount);
        }

        private static void EmptyContextInvalid() => TestAssert.Equal(
            ConfirmationOutcome.Invalid,
            Service().Evaluate(Request(context: ""), Now).Outcome);

        private static void OversizedContextInvalid() => TestAssert.Equal(
            ConfirmationOutcome.Invalid,
            Service().Evaluate(Request(context: new string('x', 161)), Now).Outcome);

        private static void ShortWindowInvalid() => TestAssert.Equal(
            ConfirmationOutcome.Invalid,
            Service().Evaluate(Request(window: TimeSpan.FromMilliseconds(249)), Now).Outcome);

        private static void LongWindowInvalid() => TestAssert.Equal(
            ConfirmationOutcome.Invalid,
            Service().Evaluate(Request(window: TimeSpan.FromSeconds(31)), Now).Outcome);

        private static void CancellationWorks()
        {
            ContextualConfirmationService service = Service();
            service.Evaluate(Request(), Now);
            service.Evaluate(new ConfirmationRequest(
                SafetyActionKind.VehicleDestruction, "zdo:1", "a", TimeSpan.FromSeconds(4)), Now);
            service.Cancel("zdo:1");
            TestAssert.Equal(0, service.PendingCount);
        }

        private static void ClearWorks()
        {
            ContextualConfirmationService service = Service();
            service.Evaluate(Request(), Now);
            service.Clear();
            TestAssert.Equal(0, service.PendingCount);
        }

        private static void CacheIsBounded()
        {
            ContextualConfirmationService service = Service(8);
            for (int index = 0; index < 40; index++)
                service.Evaluate(Request("zdo:" + index), Now.AddMilliseconds(index));
            TestAssert.Equal(8, service.PendingCount);
            TestAssert.Equal(8, service.Capacity);
        }

        private static void CorrelationsDistinct()
        {
            ContextualConfirmationService service = Service();
            string first = service.Evaluate(Request(), Now).CorrelationId;
            string second = service.Evaluate(Request("zdo:2"), Now).CorrelationId;
            TestAssert.False(string.IsNullOrEmpty(first));
            TestAssert.NotEqual(first, second);
        }
    }
}

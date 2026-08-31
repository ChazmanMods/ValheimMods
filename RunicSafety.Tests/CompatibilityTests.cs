using System;
using System.Collections.Generic;
using RunicSafety.Api;
using RunicSafety.Services;

namespace RunicSafety.Tests
{
    internal static class CompatibilityTests
    {
        internal static void Register()
        {
            TestRunner.Run("compatibility identical identities allow", IdenticalAllows);
            TestRunner.Run("compatibility invalid local identity blocks", InvalidLocalBlocks);
            TestRunner.Run("compatibility invalid remote semantic version blocks", InvalidRemoteVersionBlocks);
            TestRunner.Run("compatibility game mismatch blocks", GameMismatchBlocks);
            TestRunner.Run("compatibility protocol major mismatch blocks", ProtocolMismatchBlocks);
            TestRunner.Run("compatibility protocol minor difference allows", ProtocolMinorAllows);
            TestRunner.Run("compatibility topology mismatch blocks", TopologyMismatchBlocks);
            TestRunner.Run("compatibility rules mismatch blocks", RulesMismatchBlocks);
            TestRunner.Run("compatibility exact known unsafe pair blocks", KnownUnsafeBlocks);
            TestRunner.Run("compatibility reversed known unsafe pair blocks", KnownUnsafeReverseBlocks);
            TestRunner.Run("compatibility near known pair does not wildcard", KnownUnsafeIsExact);
            TestRunner.Run("compatibility known list is bounded", KnownListBounded);
            TestRunner.Run("compatibility remediation is precise", RemediationPrecise);
            TestRunner.Run("compatibility remote admission is truthfully unavailable", RemoteAdmissionUnavailable);
        }

        private static CompatibilityGate Gate() => new CompatibilityGate(new CorrelatedDiagnosticBuffer());

        private static CompatibilityIdentity Identity(
            string module = "runic.safety",
            string version = "0.1.0",
            string protocol = "1.0",
            string game = "0.221.12",
            string topology = "vanilla",
            string rules = "rules-a") =>
            new CompatibilityIdentity(module, version, protocol, game, topology, rules);

        private static void IdenticalAllows() => TestAssert.True(
            Gate().Evaluate(Identity(), Identity()).MayEnter);

        private static void InvalidLocalBlocks() => TestAssert.Equal(
            CompatibilityOutcome.BlockedInvalidIdentity,
            Gate().Evaluate(Identity(module: ""), Identity()).Outcome);

        private static void InvalidRemoteVersionBlocks() => TestAssert.Equal(
            CompatibilityOutcome.BlockedInvalidIdentity,
            Gate().Evaluate(Identity(), Identity(version: "banana")).Outcome);

        private static void GameMismatchBlocks() => TestAssert.Equal(
            CompatibilityOutcome.BlockedGameVersion,
            Gate().Evaluate(Identity(), Identity(game: "0.222.0")).Outcome);

        private static void ProtocolMismatchBlocks() => TestAssert.Equal(
            CompatibilityOutcome.BlockedProtocol,
            Gate().Evaluate(Identity(), Identity(protocol: "2.0")).Outcome);

        private static void ProtocolMinorAllows() => TestAssert.True(
            Gate().Evaluate(Identity(protocol: "1.0"), Identity(protocol: "1.9")).MayEnter);

        private static void TopologyMismatchBlocks() => TestAssert.Equal(
            CompatibilityOutcome.BlockedTopology,
            Gate().Evaluate(Identity(), Identity(topology: "expanded")).Outcome);

        private static void RulesMismatchBlocks() => TestAssert.Equal(
            CompatibilityOutcome.BlockedSynchronizedRules,
            Gate().Evaluate(Identity(), Identity(rules: "rules-b")).Outcome);

        private static KnownUnsafeCombination Unsafe() => new KnownUnsafeCombination(
            "runic.safety", "0.1.0", "runic.inventory", "0.2.0", "upgrade-inventory");

        private static void KnownUnsafeBlocks()
        {
            CompatibilityIdentity local = Identity();
            CompatibilityIdentity remote = Identity(module: "runic.inventory", version: "0.2.0");
            TestAssert.Equal(CompatibilityOutcome.BlockedKnownCombination,
                Gate().Evaluate(local, remote, new[] { Unsafe() }).Outcome);
        }

        private static void KnownUnsafeReverseBlocks()
        {
            CompatibilityIdentity local = Identity(module: "runic.inventory", version: "0.2.0");
            CompatibilityIdentity remote = Identity();
            TestAssert.Equal(CompatibilityOutcome.BlockedKnownCombination,
                Gate().Evaluate(local, remote, new[] { Unsafe() }).Outcome);
        }

        private static void KnownUnsafeIsExact()
        {
            CompatibilityIdentity local = Identity();
            CompatibilityIdentity remote = Identity(module: "runic.inventory", version: "0.2.1");
            TestAssert.True(Gate().Evaluate(local, remote, new[] { Unsafe() }).MayEnter);
        }

        private static void KnownListBounded()
        {
            var list = new List<KnownUnsafeCombination>();
            for (int index = 0; index < 129; index++) list.Add(null);
            TestAssert.Equal(CompatibilityOutcome.BlockedInvalidIdentity,
                Gate().Evaluate(Identity(), Identity(), list).Outcome);
        }

        private static void RemediationPrecise()
        {
            CompatibilityDecision decision = Gate().Evaluate(Identity(), Identity(topology: "other"));
            TestAssert.Equal("match-inventory-slot-topology", decision.RemediationCode);
            TestAssert.False(string.IsNullOrEmpty(decision.CorrelationId));
        }

        private static void RemoteAdmissionUnavailable() => TestAssert.False(Gate().RemoteAdmissionHookAvailable);
    }
}

using System;
using System.Collections.Generic;

namespace Runic.Foundation.Core.Tests
{
    internal static class VersionTests
    {
        internal static void Register()
        {
            TestRunner.Run("Semantic versions parse and round-trip", ParseAndRoundTrip);
            TestRunner.Run("Semantic prerelease precedence follows SemVer", PreReleasePrecedence);
            TestRunner.Run("Semantic versions reject malformed values", RejectMalformedVersions);
            TestRunner.Run("Protocol compatibility uses the major generation", ProtocolCompatibility);
            TestRunner.Run("Module descriptors validate and freeze discovery metadata", DescriptorMetadata);
            TestRunner.Run("Canonical capability IDs remain valid Runic identifiers", CapabilityIdentifiers);
        }

        private static void ParseAndRoundTrip()
        {
            SemanticVersion version = SemanticVersion.Parse("1.2.3-rc.1+valheim.220");
            TestAssert.Equal(1, version.Major);
            TestAssert.Equal(2, version.Minor);
            TestAssert.Equal(3, version.Patch);
            TestAssert.Equal("rc.1", version.PreRelease);
            TestAssert.Equal("valheim.220", version.BuildMetadata);
            TestAssert.Equal("1.2.3-rc.1+valheim.220", version.ToString());
        }

        private static void PreReleasePrecedence()
        {
            string[] ordered =
            {
                "1.0.0-alpha",
                "1.0.0-alpha.1",
                "1.0.0-alpha.beta",
                "1.0.0-beta",
                "1.0.0-beta.2",
                "1.0.0-beta.11",
                "1.0.0-rc.1",
                "1.0.0"
            };
            for (int index = 0; index < ordered.Length - 1; index++)
            {
                TestAssert.True(
                    SemanticVersion.Parse(ordered[index]) < SemanticVersion.Parse(ordered[index + 1]),
                    ordered[index] + " should precede " + ordered[index + 1] + ".");
            }
        }

        private static void RejectMalformedVersions()
        {
            string[] invalid = { "1", "1.2", "01.2.3", "1.2.3-01", "1.2.3+", "v1.2.3" };
            foreach (string value in invalid)
                TestAssert.False(SemanticVersion.TryParse(value, out _), value + " should be invalid.");
        }

        private static void ProtocolCompatibility()
        {
            TestAssert.Equal("1.0.0", RunicCoreMetadata.SemanticVersionText);
            ProtocolVersion one = ProtocolVersion.Parse("1.0");
            TestAssert.True(one.IsCompatibleWith(ProtocolVersion.Parse("1.9")));
            TestAssert.False(one.IsCompatibleWith(ProtocolVersion.Parse("2.0")));
            TestAssert.True(ProtocolVersion.Parse("1.2").CompareTo(one) > 0);
        }

        private static void DescriptorMetadata()
        {
            ModuleDescriptor descriptor = new ModuleDescriptor(
                "test.module",
                "Test Module",
                "0.1.0",
                "1.0",
                new[] { "zdo.observe", "container.query" },
                new[] { "test.extension" });
            TestAssert.Equal("container.query", descriptor.OwnedCapabilities[0]);
            TestAssert.True(descriptor.HasCapability("zdo.observe"));
            TestAssert.Equal("0.1.0", descriptor.SemanticVersion.ToString());
            TestAssert.Throws<NotSupportedException>(() =>
                ((IList<string>)descriptor.OwnedCapabilities).Add("network.protocol"));
            TestAssert.Throws<ArgumentException>(() => new ModuleDescriptor(
                "Test.Module",
                "Bad",
                "0.1.0",
                "1.0"));
        }

        private static void CapabilityIdentifiers()
        {
            string[] values =
            {
                RunicCapabilityIds.PermissionsEvaluate,
                RunicCapabilityIds.ContainerQuery,
                RunicCapabilityIds.ContainerTransfer,
                RunicCapabilityIds.MaterialsReserve,
                RunicCapabilityIds.MaterialsConsume,
                RunicCapabilityIds.PersistenceMigrate,
                RunicCapabilityIds.NetworkProtocol,
                RunicCapabilityIds.NotificationPublish,
                RunicCapabilityIds.KeybindingsRegistry,
                RunicCapabilityIds.StartupMeasure,
                RunicCapabilityIds.StartupCache,
                RunicCapabilityIds.SecurityAttest,
                RunicCapabilityIds.SecurityEvidence,
                RunicCapabilityIds.SecurityAdmission,
                RunicCapabilityIds.InventoryItemLocks,
                RunicCapabilityIds.InventoryDurableOperations,
                RunicCapabilityIds.SafetyConfirmation,
                RunicCapabilityIds.ZdoObserve,
                RunicCapabilityIds.ZdoOwnership
            };
            foreach (string value in values)
                TestAssert.True(RunicIdentifier.IsValid(value), value + " should be canonical.");
        }
    }
}

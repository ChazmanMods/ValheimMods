using System;
using RunicInventory.Integration;

namespace RunicInventory.Tests
{
    internal static class PlatformVersionTests
    {
        internal static void Register()
        {
            TestRunner.Run("version gate ignores platform display prefixes", DisplayPrefixesDoNotReject);
            TestRunner.Run("version gate still rejects unsupported or unknown builds", UnsupportedBuildsReject);
            TestRunner.Run("version gate requires CurrentVersion and never falls back to display text", MissingContractRejects);
        }

        private static void DisplayPrefixesDoNotReject()
        {
            FakeVersion.DisplayReads = 0;
            foreach (string build in new[] { "1.0.7", "1.0.12" })
            foreach (string label in new[] { build, "l-" + build, "platform-" + build })
            {
                FakeVersion.Value = build;
                FakeVersion.Display = label;
                ValheimContracts.ValidateGameVersion(typeof(FakeVersion));
            }
            TestAssert.Equal(0, FakeVersion.DisplayReads);
            ValheimContracts.ValidateGameVersion(typeof(Player).Assembly.GetType("Version", true));
        }

        private static void UnsupportedBuildsReject()
        {
            FakeVersion.Display = "1.0.7";
            foreach (string build in new[] { "1.0.6", "1.0.8", "1.0.13", "1.0.70", "1.0.120", "l-1.0.7", "l-1.0.12", "1.0.12-preview", "", null })
            {
                FakeVersion.Value = build;
                ExpectRejected(() => ValheimContracts.ValidateGameVersion(typeof(FakeVersion)));
            }
        }

        private static void MissingContractRejects()
        {
            ExpectRejected(() => ValheimContracts.ValidateGameVersion(typeof(DisplayOnlyVersion)));
            ExpectRejected(() => ValheimContracts.ValidateGameVersion(null));
        }

        private static void ExpectRejected(Action action)
        {
            try { action(); }
            catch (MissingMemberException) { return; }
            throw new InvalidOperationException("An unsupported version contract was accepted.");
        }

        private static class FakeVersion
        {
            internal static string Value;
            internal static string Display;
            internal static int DisplayReads;
            public static object CurrentVersion => Value == null ? null : new BuildValue(Value);
            public static string GetVersionString(bool includeNetwork)
            {
                DisplayReads++;
                return Display;
            }
        }

        private sealed class BuildValue
        {
            private readonly string _value;
            internal BuildValue(string value) => _value = value;
            public override string ToString() => _value;
        }

        private static class DisplayOnlyVersion
        {
            public static string GetVersionString(bool includeNetwork) =>
                throw new InvalidOperationException("Display version must never be queried.");
        }
    }
}

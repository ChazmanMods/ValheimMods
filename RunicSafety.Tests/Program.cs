using System;

namespace RunicSafety.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            ConfirmationPolicyTests.Register();
            ProtectedPolicyTests.Register();
            DiagnosticTests.Register();
            RecoveryPlanningTests.Register();
            CompatibilityTests.Register();
            MigrationBackupTests.Register();
            PluginContractTests.Register();
            HarmonyContractTests.Register();
            InstalledValheimContractTests.Register();
            DocumentationContractTests.Register();

            System.Console.WriteLine();
            System.Console.WriteLine(
                TestRunner.FailureCount == 0
                    ? "PASS: " + TestRunner.PassCount + "/" + TestRunner.TotalCount + " Runic Safety tests"
                    : "FAIL: " + TestRunner.FailureCount + "/" + TestRunner.TotalCount +
                      " Runic Safety tests failed");
            return TestRunner.FailureCount == 0 ? 0 : 1;
        }
    }

    internal static class TestRunner
    {
        private static readonly string Filter =
            Environment.GetEnvironmentVariable("RUNIC_TEST_FILTER") ?? string.Empty;

        internal static int PassCount { get; private set; }
        internal static int FailureCount { get; private set; }
        internal static int TotalCount => PassCount + FailureCount;

        internal static void Run(string name, Action test)
        {
            if (Filter.Length != 0 &&
                name.IndexOf(Filter, StringComparison.OrdinalIgnoreCase) < 0) return;
            try
            {
                test();
                PassCount++;
                System.Console.WriteLine("PASS " + name);
            }
            catch (Exception exception)
            {
                FailureCount++;
                System.Console.Error.WriteLine("FAIL " + name);
                System.Console.Error.WriteLine("     " + exception);
            }
        }
    }
}

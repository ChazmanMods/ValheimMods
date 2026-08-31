using System;

namespace RunicInteraction.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            CorePolicyTests.Register();
            PluginContractTests.Register();
            HarmonyContractTests.Register();
            InstalledValheimContractTests.Register();
            RuntimeSafetyTests.Register();
            DocumentationContractTests.Register();

            System.Console.WriteLine();
            System.Console.WriteLine(
                TestRunner.FailureCount == 0
                    ? "PASS: " + TestRunner.PassCount + "/" + TestRunner.TotalCount +
                      " Runic Interaction tests"
                    : "FAIL: " + TestRunner.FailureCount + "/" + TestRunner.TotalCount +
                      " Runic Interaction tests failed");
            return TestRunner.FailureCount == 0 ? 0 : 1;
        }
    }

    internal static class TestRunner
    {
        internal static int PassCount { get; private set; }
        internal static int FailureCount { get; private set; }
        internal static int TotalCount => PassCount + FailureCount;

        internal static void Run(string name, Action test)
        {
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

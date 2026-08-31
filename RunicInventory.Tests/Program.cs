using System;

namespace RunicInventory.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            TopologyTests.Register();
            PersistenceTests.Register();
            CharacterProfileReadbackTests.Register();
            SortPlannerTests.Register();
            AtomicPositionTests.Register();
            PickupPolicyTests.Register();
            ApiContractTests.Register();
            ItemProtectionDomainTests.Register();
            ControllerBindingPolicyTests.Register();
            InstalledValheimTests.Register();
            DedicatedInstalledTests.Register();
            IndependenceTests.Register();
            PerformanceTests.Register();
            DocumentationTests.Register();

            System.Console.WriteLine();
            System.Console.WriteLine(TestRunner.FailureCount == 0
                ? "PASS: " + TestRunner.PassCount + "/" + TestRunner.TotalCount + " Runic Inventory tests"
                : "FAIL: " + TestRunner.FailureCount + "/" + TestRunner.TotalCount + " Runic Inventory tests failed");
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

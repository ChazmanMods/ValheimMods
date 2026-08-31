using System;

namespace QuietBuildRotation.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            TestRunner.RunAll();
            System.Console.WriteLine();
            System.Console.WriteLine(
                TestRunner.FailureCount == 0
                    ? $"PASS: {TestRunner.PassCount} tests"
                    : $"FAIL: {TestRunner.FailureCount} of {TestRunner.TotalCount} tests failed");
            return TestRunner.FailureCount == 0 ? 0 : 1;
        }
    }

    internal static class TestRunner
    {
        internal static int PassCount { get; private set; }

        internal static int FailureCount { get; private set; }

        internal static int TotalCount => PassCount + FailureCount;

        internal static void RunAll()
        {
            PoseControllerTests.Register();
            PlacementTransformTests.Register();
            BuildCatalogTests.Register();
            MutationPolicyTests.Register();
            TargetSelectionPolicyTests.Register();
            AxisGuideRadialSuppressionPolicyTests.Register();
            PlacementSessionTests.Register();
            OrientationAnglesTests.Register();
            InputRouterTests.Register();
            ConfigurationTests.Register();
            ExtendedInputRouterTests.Register();
            ValheimInputSourceTests.Register();
            AllocationTests.Register();
            HarmonyCleanupContractTests.Register();
            InstalledValheimContractTests.Register();
            ReleaseContractTests.Register();
        }

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
                System.Console.WriteLine("FAIL " + name);
                System.Console.WriteLine("     " + exception.Message);
            }
        }
    }
}

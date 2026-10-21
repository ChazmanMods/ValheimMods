using System;

namespace Runic.Foundation.Core.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            VersionTests.Register();
            RegistryTests.Register();
            KeybindingTests.Register();
            NotificationTests.Register();
            SharedContractTests.Register();

            Console.WriteLine();
            Console.WriteLine(
                TestRunner.FailureCount == 0
                    ? "PASS: " + TestRunner.PassCount + " tests"
                    : "FAIL: " + TestRunner.FailureCount + " of " +
                      TestRunner.TotalCount + " tests failed");
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
                Console.WriteLine("PASS " + name);
            }
            catch (Exception exception)
            {
                FailureCount++;
                Console.WriteLine("FAIL " + name);
                Console.WriteLine("     " + exception.GetType().Name + ": " + exception.Message);
            }
        }
    }
}

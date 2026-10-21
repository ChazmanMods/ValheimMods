using System;

namespace RunicPermissions.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            ContractTests.Register();
            PermissionEvaluatorTests.Register();
            GroupDomainTests.Register();
            GroupWorldStoreTests.Register();
            GroupMembershipServiceTests.Register();
            GroupActiveSelectionTests.Register();
            GroupFriendlyProtocolTests.Register();
            GroupCommandTests.Register();
            GroupRuntimeContractTests.Register();
            Console.WriteLine();
            Console.WriteLine(
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
                Console.WriteLine("     " + exception.Message);
            }
        }
    }

    internal static class TestAssert
    {
        internal static void True(bool condition, string message = "Expected true.")
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        internal static void False(bool condition, string message = "Expected false.") =>
            True(!condition, message);

        internal static void Equal<T>(T expected, T actual, string message = null)
        {
            if (!Equals(expected, actual))
                throw new InvalidOperationException(message ?? $"Expected {expected}, got {actual}.");
        }

        internal static TException Throws<TException>(Action action)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException exception)
            {
                return exception;
            }
            throw new InvalidOperationException("Expected " + typeof(TException).Name + ".");
        }
    }
}

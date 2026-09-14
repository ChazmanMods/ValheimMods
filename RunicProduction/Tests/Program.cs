using System;
using System.Collections.Generic;

namespace RunicProduction.Tests
{
    internal static class Program
    {
        private static int _passed;
        private static int _expected;

        private static void Main()
        {
            RunAll(NativeCheatChecksTests.Cases());
            RunAll(ProductionLinkPressTests.Cases());
            RunAll(ProductionSetupOwnershipTests.Cases());
            RunAll(StandaloneProductionTests.Cases());
            RunAll(NearbyIngredientContainerIndexTests.Cases());
            RunAll(MultiReplenishmentCatalogTests.Cases());
            System.Console.WriteLine(
                $"PASS: {_passed}/{_expected} Runic Production tests");
        }

        private static void RunAll(
            IReadOnlyList<KeyValuePair<string, Action>> tests)
        {
            foreach (KeyValuePair<string, Action> test in tests)
                Run(test.Key, test.Value);
        }

        private static void Run(string name, Action test)
        {
            _expected++;
            try
            {
                test();
                _passed++;
                System.Console.WriteLine("PASS " + name);
            }
            catch (Exception exception)
            {
                System.Console.Error.WriteLine(
                    "FAIL " + name + ": " + exception.Message);
                Environment.ExitCode = 1;
            }
        }
    }
}

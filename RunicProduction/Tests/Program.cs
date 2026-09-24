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
            Run("blocked stations back off and removed stations lose schedule state", () =>
            {
                var schedule = new RunicProduction.Core.StationSchedule();
                if (!schedule.IsDue(1, 0)) throw new Exception("new station not due");
                schedule.Observe(1, 0, false, 2);
                if (schedule.IsDue(1, 3) || !schedule.IsDue(1, 4)) throw new Exception("backoff failed");
                schedule.Observe(1, 4, true, 2);
                if (!schedule.IsDue(1, 6)) throw new Exception("success did not reset backoff");
                schedule.Remove(1);
                if (!schedule.IsDue(1, 0)) throw new Exception("removed station retained state");
            });
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

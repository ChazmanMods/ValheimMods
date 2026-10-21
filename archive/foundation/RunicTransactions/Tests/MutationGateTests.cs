using System;
using RunicTransactions.Coordination;

namespace RunicTransactions.Tests
{
    internal static class MutationGateTests
    {
        internal static void CrossModuleGateRejectsReentrancyAndStaleDisposal()
        {
            if (!RunicMutationGate.TryEnter("runic.storage/transfer", out IDisposable first))
                throw new InvalidOperationException("An idle gate must admit its first mutation.");
            try
            {
                Equal(true, RunicMutationGate.IsHeld, "The live lease must be observable.");
                Equal("runic.storage/transfer", RunicMutationGate.CurrentOwnerId,
                    "The current owner is diagnostic and stable.");
                Equal(false, RunicMutationGate.TryEnter("runic.production/output", out IDisposable nested),
                    "Same-thread cross-module reentrancy must fail closed.");
                nested?.Dispose();
            }
            finally { first.Dispose(); }

            Equal(false, RunicMutationGate.IsHeld, "Disposal must release the gate.");
            Equal(true, RunicMutationGate.TryEnter("runic.production/output", out IDisposable second),
                "A later independent mutation must be admitted.");
            first.Dispose();
            Equal(true, RunicMutationGate.IsHeld,
                "Disposing an already-released lease must not release its replacement.");
            second.Dispose();
            Equal(false, RunicMutationGate.IsHeld, "The replacement lease must release normally.");
        }

        private static void Equal<T>(T expected, T actual, string message)
        {
            if (!object.Equals(expected, actual))
                throw new InvalidOperationException(
                    message + " Expected " + expected + "; actual " + actual + ".");
        }
    }
}

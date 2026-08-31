using System;
using System.Collections.Generic;

namespace RunicCrafting.Tests
{
    internal static class TestAssert
    {
        internal static void True(bool value, string message = "Expected true.")
        {
            if (!value) throw new InvalidOperationException(message);
        }

        internal static void False(bool value, string message = "Expected false.") => True(!value, message);

        internal static void Equal<T>(T expected, T actual, string message = null)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException(message ?? $"Expected {expected}; got {actual}.");
        }
    }
}

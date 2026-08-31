using System;
using System.Collections.Generic;

namespace RunicAwareness.Tests
{
    internal static class TestAssert
    {
        internal static void True(bool condition, string message = null)
        {
            if (!condition) throw new InvalidOperationException(message ?? "Expected true.");
        }

        internal static void False(bool condition, string message = null) =>
            True(!condition, message ?? "Expected false.");

        internal static void Equal<T>(T expected, T actual, string message = null)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException(
                    (message == null ? string.Empty : message + " ") +
                    "Expected <" + expected + ">, actual <" + actual + ">.");
        }

        internal static T NotNull<T>(T value, string message = null) where T : class =>
            value ?? throw new InvalidOperationException(message ?? "Expected a non-null value.");

        internal static void Contains(string expected, string actual, string message = null)
        {
            if (actual == null || !actual.Contains(expected, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    message ?? "Expected text to contain <" + expected + ">.");
        }

        internal static void DoesNotContain(string expected, string actual, string message = null)
        {
            if (actual != null && actual.Contains(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    message ?? "Expected text not to contain <" + expected + ">.");
        }
    }
}

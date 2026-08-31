using System;
using System.Collections.Generic;

namespace RunicAgriculture.Tests
{
    internal static class TestAssert
    {
        internal static void True(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }

        internal static void False(bool value, string message) => True(!value, message);

        internal static void Equal<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException(
                    message + " Expected: " + expected + "; actual: " + actual + ".");
        }

        internal static void Near(double expected, double actual, double tolerance, string message)
        {
            if (Math.Abs(expected - actual) > tolerance)
                throw new InvalidOperationException(
                    message + " Expected: " + expected + "; actual: " + actual + ".");
        }

        internal static T NotNull<T>(T value, string message) where T : class
        {
            if (value == null) throw new InvalidOperationException(message);
            return value;
        }

        internal static void Throws<TException>(Action action, string message)
            where TException : Exception
        {
            try { action(); }
            catch (TException) { return; }
            throw new InvalidOperationException(message);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace RunicSafety.Tests
{
    internal static class TestAssert
    {
        internal static void True(bool condition, string message = "Expected true.")
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        internal static void False(bool condition, string message = "Expected false.") => True(!condition, message);

        internal static void Equal<T>(T expected, T actual, string message = null)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException(message ?? "Expected " + expected + ", got " + actual + ".");
        }

        internal static void NotEqual<T>(T unexpected, T actual, string message = null)
        {
            if (EqualityComparer<T>.Default.Equals(unexpected, actual))
                throw new InvalidOperationException(message ?? "Did not expect " + unexpected + ".");
        }

        internal static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string message = null)
        {
            if (!expected.SequenceEqual(actual))
                throw new InvalidOperationException(message ?? "Sequences differ.");
        }

        internal static T NotNull<T>(T value, string message = "Expected non-null.") where T : class
        {
            if (value == null) throw new InvalidOperationException(message);
            return value;
        }

        internal static T Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T exception) { return exception; }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "Expected " + typeof(T).Name + ", got " + exception.GetType().Name + ".", exception);
            }
            throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
        }

        internal static void Contains(string expected, string actual, string message = null)
        {
            if (actual == null || actual.IndexOf(expected, StringComparison.Ordinal) < 0)
                throw new InvalidOperationException(message ?? "Expected text containing: " + expected);
        }
    }
}

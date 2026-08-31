using System;
using System.Collections.Generic;

namespace RunicInventory.Tests
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
                throw new InvalidOperationException(message ?? "Expected <" + expected + "> but was <" + actual + ">.");
        }

        internal static void NotEqual<T>(T left, T right, string message = null)
        {
            if (EqualityComparer<T>.Default.Equals(left, right))
                throw new InvalidOperationException(message ?? "Expected unequal values.");
        }

        internal static T NotNull<T>(T value, string message = "Expected non-null.") where T : class =>
            value ?? throw new InvalidOperationException(message);

        internal static T Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T exception) { return exception; }
            catch (Exception exception) { throw new InvalidOperationException("Expected " + typeof(T).Name + " but got " + exception.GetType().Name + ".", exception); }
            throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
        }

        internal static void Contains(string text, string expected, string message = null)
        {
            if (text == null || text.IndexOf(expected, StringComparison.Ordinal) < 0)
                throw new InvalidOperationException(message ?? "Expected text containing '" + expected + "'.");
        }
    }
}

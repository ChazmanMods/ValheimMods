using System;
using System.Collections.Generic;

namespace Runic.Foundation.Core.Tests
{
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
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException(message ?? "Expected " + expected + ", got " + actual + ".");
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
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "Expected " + typeof(TException).Name + ", got " + exception.GetType().Name + ".");
            }
            throw new InvalidOperationException("Expected " + typeof(TException).Name + ".");
        }
    }
}

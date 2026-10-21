using System;
using System.Collections.Generic;

namespace RunicTransactions.Tests
{
    internal static class TestAssert
    {
        internal static void True(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        internal static void False(bool condition, string message) => True(!condition, message);

        internal static void Equal<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException($"{message} Expected: {expected}; actual: {actual}.");
        }

        internal static void NotNull(object value, string message) => True(value != null, message);

        internal static void Throws<TException>(Action action, string message)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"{message} Expected {typeof(TException).Name}, received {exception.GetType().Name}.",
                    exception);
            }

            throw new InvalidOperationException($"{message} Expected {typeof(TException).Name}.");
        }
    }
}

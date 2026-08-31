using System;
using System.Collections.Generic;
using System.IO;

namespace RunicExploration.Tests
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
            value ?? throw new InvalidOperationException(message ?? "Expected non-null.");

        internal static void Contains(string expected, string actual, string message = null)
        {
            if (actual == null || !actual.Contains(expected, StringComparison.Ordinal))
                throw new InvalidOperationException(message ??
                    "Expected text to contain <" + expected + ">.");
        }

        internal static void DoesNotContain(string expected, string actual, string message = null)
        {
            if (actual != null && actual.Contains(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(message ??
                    "Expected text not to contain <" + expected + ">.");
        }
    }

    internal static class TestPaths
    {
        private static string _root;
        internal static string Root => _root ??= FindRoot();
        internal static string Plugin(string path) => Path.Combine(Root, "RunicExploration", path);

        private static string FindRoot()
        {
            string[] starts = { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
            foreach (string start in starts)
            {
                DirectoryInfo directory = new DirectoryInfo(start);
                while (directory != null)
                {
                    if (File.Exists(Path.Combine(directory.FullName,
                            "RunicExploration", "RunicExploration.csproj")))
                        return directory.FullName;
                    directory = directory.Parent;
                }
            }
            throw new DirectoryNotFoundException("RunicExploration.csproj not found.");
        }
    }
}

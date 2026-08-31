using System;
using System.IO;

namespace RunicAwareness.Tests
{
    internal static class TestPaths
    {
        private static string _repositoryRoot;

        internal static string RepositoryRoot => _repositoryRoot ??= FindRepositoryRoot();

        internal static string PluginFile(string relativePath) =>
            Path.Combine(RepositoryRoot, "RunicAwareness", relativePath);

        private static string FindRepositoryRoot()
        {
            string[] starts = { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
            foreach (string start in starts)
            {
                DirectoryInfo current = new DirectoryInfo(start);
                while (current != null)
                {
                    if (File.Exists(Path.Combine(
                            current.FullName, "RunicAwareness", "RunicAwareness.csproj")))
                        return current.FullName;
                    current = current.Parent;
                }
            }
            throw new DirectoryNotFoundException("Could not locate RunicAwareness.csproj.");
        }
    }
}

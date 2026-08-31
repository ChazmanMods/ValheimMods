using System;
using System.IO;

namespace RunicInteraction.Tests
{
    internal static class TestPaths
    {
        private static string _root;
        internal static string RepositoryRoot => _root ??= FindRoot();
        internal static string PluginFile(string relative) =>
            Path.Combine(RepositoryRoot, "RunicInteraction", relative);

        private static string FindRoot()
        {
            foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                DirectoryInfo current = new DirectoryInfo(start);
                while (current != null)
                {
                    if (File.Exists(Path.Combine(current.FullName, "RunicInteraction", "RunicInteraction.csproj")))
                        return current.FullName;
                    current = current.Parent;
                }
            }
            throw new DirectoryNotFoundException("Could not locate RunicInteraction.csproj.");
        }
    }
}

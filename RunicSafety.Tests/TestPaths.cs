using System;
using System.IO;

namespace RunicSafety.Tests
{
    internal static class TestPaths
    {
        internal static string RepositoryRoot
        {
            get
            {
                string directory = AppContext.BaseDirectory;
                while (directory != null)
                {
                    if (Directory.Exists(Path.Combine(directory, "RunicSafety")) &&
                        File.Exists(Path.Combine(directory, "README.md"))) return directory;
                    directory = Directory.GetParent(directory)?.FullName;
                }
                throw new DirectoryNotFoundException("Repository root not found.");
            }
        }

        internal static string Safety(params string[] parts)
        {
            string path = Path.Combine(RepositoryRoot, "RunicSafety");
            foreach (string part in parts) path = Path.Combine(path, part);
            return path;
        }
    }
}

using System;
using System.IO;

namespace RunicInventory.Tests
{
    internal static class TestPaths
    {
        internal static readonly string Repository = FindRepository();
        internal static string Module(string name) => Path.Combine(Repository, "RunicInventory", name);
        internal static string Module(params string[] parts)
        {
            string path = Path.Combine(Repository, "RunicInventory");
            foreach (string part in parts) path = Path.Combine(path, part);
            return path;
        }
        internal static string BuiltPlugin => Module(
            "bin", "Release", "netstandard2.1", "RunicInventory.dll");
        internal static string InstalledValheim =>
            Path.Combine("E:\\SteamLibrary\\steamapps\\common\\Valheim", "valheim_Data", "Managed", "assembly_valheim.dll");
        internal static string InstalledUtils =>
            Path.Combine("E:\\SteamLibrary\\steamapps\\common\\Valheim", "valheim_Data", "Managed", "assembly_utils.dll");
        internal static string InstalledDedicatedValheim =>
            Path.Combine("E:\\SteamLibrary\\steamapps\\common\\Valheim dedicated server",
                "valheim_server_Data", "Managed", "assembly_valheim.dll");

        private static string FindRepository()
        {
            DirectoryInfo current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "RunicInventory", "RunicInventory.csproj")))
                    return current.FullName;
                current = current.Parent;
            }
            throw new DirectoryNotFoundException("Could not locate ChazmanModsRepo.");
        }
    }
}

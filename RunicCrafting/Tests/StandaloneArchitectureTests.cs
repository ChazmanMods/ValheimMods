using System;
using System.IO;

namespace RunicCrafting.Tests
{
    internal static class StandaloneArchitectureTests
    {
        private static readonly string ProjectRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", ".."));

        internal static void PackageHasNoFoundationDependencies()
        {
            string project = Read("RunicCrafting.csproj");
            string manifest = Read("manifest.json");
            string plugin = Read("Plugin.cs");
            Reject(project + manifest + plugin,
                "RunicCore", "RunicPersistence", "RunicPermissions", "RunicTransactions",
                "BepInDependency", "RunicRegistry");
        }

        internal static void RuntimeHasNoDurableOrGlobalMutationLayer()
        {
            string source = Read(Path.Combine("Integration", "CraftingRuntime.cs")) +
                            Read(Path.Combine("Integration", "ContainerQueryRuntime.cs"));
            Reject(source,
                "RemoteCrafting", "RunicMutationGate", "Durable", "Journal",
                "ITransactionCoordinator", "RunicRegistry");
        }

        internal static void ContainerMutationRequiresNativeOwnership()
        {
            string query = Read(Path.Combine("Integration", "ContainerQueryRuntime.cs"));
            TestAssert.True(query.Contains("CanMutateLocalPlayer(player)", StringComparison.Ordinal));
            TestAssert.True(query.Contains("container.IsOwner()", StringComparison.Ordinal));
            TestAssert.True(query.Contains("container.IsInUse()", StringComparison.Ordinal));
        }

        private static string Read(string relative) =>
            File.ReadAllText(Path.Combine(ProjectRoot, relative));

        private static void Reject(string source, params string[] forbidden)
        {
            foreach (string value in forbidden)
                TestAssert.False(source.Contains(value, StringComparison.Ordinal),
                    "Unexpected standalone dependency token: " + value);
        }
    }
}

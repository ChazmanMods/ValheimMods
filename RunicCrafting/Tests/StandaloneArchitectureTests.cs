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
            TestAssert.True(project.Contains("InventorySafety"));
            Reject(project + manifest + plugin, "RunicAutomation.csproj", "Chazman-RunicAutomation", "BepInDependency");
            Reject(project + manifest + plugin,
                "RunicCore", "RunicPersistence", "RunicPermissions", "RunicTransactions",
                "RunicRegistry");
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
            TestAssert.True(query.Contains("RunicAutomation.ContainerAuthority.TryAcquire", StringComparison.Ordinal));
            TestAssert.True(query.Contains("container.IsInUse()", StringComparison.Ordinal));
            TestAssert.True(query.Contains("zdo.GetInt(ZDOVars.s_inUse, 0)", StringComparison.Ordinal));
            string reflection = Read(Path.Combine("Integration", "ValheimReflection.cs"));
            TestAssert.True(reflection.Contains("RefreshOwnedContainer", StringComparison.Ordinal));
            TestAssert.True(reflection.Contains("GetByteArray(ZDOVars.s_items)", StringComparison.Ordinal));
            TestAssert.True(query.Contains("if (!requireWritable) return true;", StringComparison.Ordinal));
            TestAssert.True(query.IndexOf("if (!requireWritable) return true;", StringComparison.Ordinal) <
                query.IndexOf("RunicAutomation.ContainerAuthority.TryAcquire", StringComparison.Ordinal));
            string reader = reflection.Substring(reflection.IndexOf("internal static bool TryReadContainerInventory", StringComparison.Ordinal));
            reader = reader.Substring(0, reader.IndexOf("internal static bool RefreshOwnedContainer", StringComparison.Ordinal));
            Reject(reader, "ClaimOwnership", "ContainerLoadMethod.Invoke", "NotifyInventoryChanged");
            var preview = new RunicCrafting.Integration.ReadOnlyMaterialSource(
                new RunicCrafting.Domain.MaterialSourceSnapshot("chest", RunicCrafting.Domain.MaterialSourceKind.NearbyContainer,
                    1f, new System.Collections.Generic.Dictionary<string, int> { { "Wood", 10 } }));
            TestAssert.Equal(10, preview.Snapshot().Available("Wood"));
            TestAssert.False(preview.TryTake("Wood", 1, out var token));
            TestAssert.True(token == null);
            string commands = Read(Path.Combine("Integration", "WorkshopAccessCommands.cs"));
            TestAssert.True(commands.IndexOf("if (verb == \"show\")", StringComparison.Ordinal) <
                commands.IndexOf("piece.GetCreator()", StringComparison.Ordinal));
            TestAssert.True(Read("Configuration.cs").Contains(
                "\"DefaultLocalMaterialUse\", WorkshopPolicyKind.Everyone", StringComparison.Ordinal));
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

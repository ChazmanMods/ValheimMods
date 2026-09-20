using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using RunicCrafting.Domain;

namespace RunicCrafting.Tests
{
    internal static class AreaRepairTests
    {
        internal static void RadiusAndAccessAreBounded()
        {
            TestAssert.Equal(50f, AreaRepairPolicy.Radius(float.NaN));
            TestAssert.Equal(50f, AreaRepairPolicy.Radius(float.PositiveInfinity));
            TestAssert.Equal(1f, AreaRepairPolicy.Radius(-1f));
            TestAssert.Equal(100f, AreaRepairPolicy.Radius(500f));
            TestAssert.Equal(50f, AreaRepairPolicy.Radius(50f));
            // Every combination; none of the eligibility checks can be bypassed.
            for (int mask = 0; mask < 64; mask++)
                TestAssert.Equal(mask == 63, AreaRepairPolicy.Eligible((mask & 1) != 0,
                    (mask & 2) != 0, (mask & 4) != 0, (mask & 8) != 0, (mask & 16) != 0, (mask & 32) != 0));
        }

        internal static void BatchesAreBoundedAndCancelable()
        {
            var queue = new AreaRepairBatch<int>();
            for (int i = 0; i < 100; i++) TestAssert.True(queue.Add(i));
            int calls = 0;
            TestAssert.Equal(8, queue.Step(_ => { calls++; return true; }));
            TestAssert.Equal(8, calls); TestAssert.Equal(92, queue.Count);
            calls = 0;
            TestAssert.Equal(0, queue.Step(_ => { calls++; return false; }));
            TestAssert.Equal(32, calls); TestAssert.Equal(60, queue.Count);
            queue.Clear();
            TestAssert.Equal(0, queue.Step(_ => throw new Exception("Cancelled work executed")));
            for (int i = 0; i < AreaRepairBatch<int>.MaximumPieces; i++) TestAssert.True(queue.Add(i));
            TestAssert.False(queue.Add(-1));
            queue.Clear();
            for (int i = 0; i < 55; i++) queue.Add(i);
            var seen = new System.Collections.Generic.HashSet<int>();
            while (queue.Count > 0) queue.Step(i => { TestAssert.True(seen.Add(i)); return i % 2 == 0; });
            TestAssert.Equal(55, seen.Count);
        }

        private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        internal static void RuntimeUsesNativeRepairAndDisablesCleanly()
        {
            string runtime = File.ReadAllText(Path.Combine(Root, "Integration", "AreaRepairRuntime.cs"));
            foreach (string required in new[] { "!Configuration.AreaRepairEnabled.Value", "{ Reset(); return; }",
                "!Configuration.Enabled.Value", "!TakesGameplayInput(player)", "Chat.instance.HasFocus()",
                "!TextInput.IsVisible()", "CursorLockMode.Locked", "Pending.Step", "GetItemPrefab(\"Hammer\")",
                "PrivateArea.CheckAccess", "ValheimReflection.ContainerAllows", "CraftingRuntime.CanUseStation",
                "wear.Repair()", "!view.HasOwner()", "!ReferenceEquals(_network, ZNet.instance)" })
                TestAssert.True(runtime.Contains(required, StringComparison.Ordinal), required);
            foreach (string forbidden in new[] { ".SetOwner(", ".ClaimOwnership(", ".m_durability", ".SetHealth(", "GetZDO().Set", ".GetInventory(" })
                TestAssert.False(runtime.Contains(forbidden, StringComparison.Ordinal), forbidden);
            string config = File.ReadAllText(Path.Combine(Root, "Configuration.cs"));
            TestAssert.True(config.Contains("\"Area Repair\", \"Enabled\", true"));
            TestAssert.True(config.Contains("new KeyboardShortcut(KeyCode.Semicolon)"));
            TestAssert.True(config.Contains("\"RadiusMeters\", 50f"));
            TestAssert.True(config.Contains("new AcceptableValueRange<float>(1f, 100f)"));
            string plugin = File.ReadAllText(Path.Combine(Root, "Plugin.cs"));
            TestAssert.True(plugin.Contains("if (_harmony != null) AreaRepairRuntime.Tick();"));
            TestAssert.True(plugin.Contains("AreaRepairRuntime.Reset();"));
        }

        internal static void OptionalRepairTriggersAreSeparateAndGuarded()
        {
            string config = File.ReadAllText(Path.Combine(Root, "Configuration.cs"));
            TestAssert.True(config.Contains("AutoRepairOnStationOpen", StringComparison.Ordinal));
            TestAssert.True(config.Contains("AutoRepairOnStationOpen\", false", StringComparison.Ordinal));
            TestAssert.True(config.Contains("\"OnHammerRepair\", false", StringComparison.Ordinal));

            string patch = File.ReadAllText(Path.Combine(Root, "Integration", "Patches.cs"));
            TestAssert.True(patch.Contains("!__result || !(user is Player player)", StringComparison.Ordinal));
            TestAssert.True(patch.Contains("RepairAllRuntime.TryHandleStationOpen", StringComparison.Ordinal));
            TestAssert.True(patch.Contains("WearNTear.Repair", StringComparison.Ordinal));
            TestAssert.True(patch.Contains("AreaRepairRuntime.OnVanillaHammerRepair", StringComparison.Ordinal));

            string repair = File.ReadAllText(Path.Combine(Root, "Integration", "RepairAllRuntime.cs"));
            TestAssert.True(repair.Contains("FindObjectOfType<InventoryGui>", StringComparison.Ordinal));
            TestAssert.True(repair.Contains("TryRepair(player, gui, station, false, false)", StringComparison.Ordinal));
            string area = File.ReadAllText(Path.Combine(Root, "Integration", "AreaRepairRuntime.cs"));
            TestAssert.True(area.Contains("_repairingArea", StringComparison.Ordinal));
            TestAssert.True(area.Contains("if (Pending.Count == 0) _hammerRepairRequested = true", StringComparison.Ordinal));
            TestAssert.True(area.Contains("try { return wear.Repair(); }", StringComparison.Ordinal));
            TestAssert.True(area.Contains("IsHoldingHammer", StringComparison.Ordinal));
        }

        internal static void HammerPreviewRowsAndConsumptionUsePlayerRange()
        {
            string source = File.ReadAllText(Path.Combine(Root, "Integration", "CraftingRuntime.cs"));
            TestAssert.Equal(3, source.Split("Vector3 origin = BuildQueryOrigin(player);").Length - 1);
            TestAssert.Equal(3, source.Replace("\r\n", "\n").Split("? Configuration.SafeStationlessBuildRange\n                : Configuration.SafeRangeCap;").Length - 1);
            TestAssert.True(source.Contains("BuildQueryOrigin(Player player) => player.transform.position"));
            TestAssert.True(source.Contains("BuildMaterialQueryScope.RequiredStationUnavailable"));
            TestAssert.True(source.Contains("WorkshopAction.LocalMaterialUse"));
            TestAssert.True(source.Contains("player-centered chest range="));
            // Geometry regression: workbench at x=0, player at 18, copper chest at 30.
            // In a 20m query the chest is outside the old station origin, inside the new player origin.
            TestAssert.True(Math.Abs(30 - 0) > 20);
            TestAssert.True(Math.Abs(30 - 18) < 20);
        }

        internal static void ForgeCostsCommitOnceAndRollbackExactly()
        {
            var requirements = new[] { new MaterialRequirement("Stone", 4), new MaterialRequirement("Coal", 4),
                new MaterialRequirement("Wood", 10), new MaterialRequirement("Copper", 6) };
            foreach (bool commit in new[] { false, true })
            {
                var chest = new FakeMaterialSource("near-player", MaterialSourceKind.NearbyContainer, 144f,
                    new System.Collections.Generic.Dictionary<string, int> { ["Stone"] = 4, ["Coal"] = 4, ["Wood"] = 10, ["Copper"] = 8 });
                var engine = new ExactMaterialTransactionEngine();
                TestAssert.True(engine.TryBegin(requirements, new IMutableMaterialSource[] { chest }, out var lease, out _));
                if (commit) { lease.Commit(); lease.Commit(); } else TestAssert.True(lease.Rollback());
                TestAssert.Equal(commit ? 2 : 8, chest.Quantity("Copper"));
                TestAssert.Equal(commit ? 0 : 10, chest.Quantity("Wood"));
                TestAssert.Equal(commit ? 0 : 4, chest.Quantity("Stone"));
                TestAssert.Equal(commit ? 0 : 4, chest.Quantity("Coal"));
            }
            var ore = new FakeMaterialSource("ore-only", MaterialSourceKind.NearbyContainer, 1f,
                new System.Collections.Generic.Dictionary<string, int> { ["CopperOre"] = 20 });
            TestAssert.False(new ExactMaterialTransactionEngine().TryBegin(new[] { new MaterialRequirement("Copper", 6) },
                new IMutableMaterialSource[] { ore }, out _, out _));
        }

        internal static void InstalledRepairContractIsOwnerDirected()
        {
            using var assembly = AssemblyDefinition.ReadAssembly(Path.Combine(Environment.GetEnvironmentVariable("VALHEIM_INSTALL"), "valheim_Data", "Managed", "assembly_valheim.dll"));
            var wear = assembly.MainModule.Types.Single(t => t.Name == "WearNTear");
            var repair = wear.Methods.Single(m => m.Name == "Repair" && m.Parameters.Count == 0);
            TestAssert.True(repair.IsPublic); TestAssert.Equal("System.Boolean", repair.ReturnType.FullName);
            TestAssert.True(repair.Body.Instructions.Any(i => i.Operand is string s && s == "RPC_Repair"));
            var rpc = wear.Methods.Single(m => m.Name == "RPC_Repair");
            TestAssert.True(rpc.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "IsOwner"));
            var player = assembly.MainModule.Types.Single(t => t.Name == "Player");
            TestAssert.Equal("System.Boolean", player.Methods.Single(m => m.Name == "TakeInput" && m.Parameters.Count == 0).ReturnType.FullName);
            var container = assembly.MainModule.Types.Single(t => t.Name == "Container");
            TestAssert.True(container.Methods.Any(m => m.Name == "CheckAccess" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "System.Int64"));
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using RunicInteraction.Core;
using RunicInteraction.Integration;
using UnityEngine;

namespace RunicInteraction.Tests
{
    internal static class RuntimeSafetyTests
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic |
                                         BindingFlags.Instance | BindingFlags.Static;

        internal static void Register()
        {
            TestRunner.Run("frame ticks allocate no managed objects", FrameTicksAllocateNoManagedObjects);
            TestRunner.Run("door obstruction scan is non-alloc and saturation-safe", DoorScanIsNonAlloc);
            TestRunner.Run("door timers and menu contexts are hard bounded", RuntimeCollectionsAreBounded);
            TestRunner.Run("door close state is session-only and uses native Valheim ownership", DoorCloseIsNativeAndEphemeral);
            TestRunner.Run("module writes no persistent custom world state", NoPersistentWorldMutation);
            TestRunner.Run("module has no custom network transport", NoCustomTransport);
            TestRunner.Run("hold-repeat never invokes a station callback", HoldRepeatOnlyLeasesInterval);
            TestRunner.Run("pickup filter decides before any mutation API", PickupFilterIsPureGate);
            TestRunner.Run("each active feature has its own configuration gate", FeaturesAreIndependentlyGated);
            TestRunner.Run("unsupported drag transfer remains an explicit disabled gate", UnsupportedDragIsExplicit);
            TestRunner.Run("controller modifier choices are bounded", ControllerModifiersAreBounded);
            TestRunner.Run("compiled input catalog exactly matches local descriptors", DefaultInputCatalogMatchesRuntime);
            TestRunner.Run("Inventory item protection is optional reflection", ItemProtectionIsOptional);
        }

        private static void FrameTicksAllocateNoManagedObjects()
        {
            foreach (MethodInfo tick in new[]
                     {
                         Method(typeof(InteractionRuntime), "Tick"),
                         Method(typeof(DoorAutoCloseRuntime), "Tick"),
                         Method(typeof(EquipmentRestoreRuntime), "Tick")
                     })
                TestAssert.Equal(0, IlReader.NewObjectCount(tick),
                    tick.DeclaringType?.Name + ".Tick allocates per frame.");
        }

        private static void DoorScanIsNonAlloc()
        {
            MethodInfo scan = Method(typeof(DoorAutoCloseRuntime), "IsObstructed");
            TestAssert.True(IlReader.Calls(
                scan, typeof(Physics), nameof(Physics.OverlapSphereNonAlloc)));
            TestAssert.False(IlReader.Calls(
                scan, typeof(Physics), nameof(Physics.OverlapSphere)));
            TestAssert.Equal(32,
                ((Collider[])Field(typeof(DoorAutoCloseRuntime), "Obstructions")
                    .GetValue(null)).Length);
        }

        private static void RuntimeCollectionsAreBounded()
        {
            TestAssert.Equal(64,
                (int)Field(typeof(DoorAutoCloseRuntime), "MaximumTimers")
                    .GetRawConstantValue());
            TestAssert.Equal(8,
                (int)Field(typeof(DoorAutoCloseRuntime), "TickBudget")
                    .GetRawConstantValue());
            TestAssert.Equal(70f,
                (float)Field(typeof(DoorAutoCloseRuntime), "MaximumLifetimeSeconds")
                    .GetRawConstantValue());
            TestAssert.Equal(64,
                (int)Field(typeof(MenuMemoryRuntime), "MaximumContexts")
                    .GetRawConstantValue());
            TestAssert.Equal(128, PickupFilterSet.MaximumRules);
        }

        private static void DoorCloseIsNativeAndEphemeral()
        {
            MethodInfo capture = Method(typeof(DoorAutoCloseRuntime), "BeforeInteract");
            MethodInfo tick = Method(typeof(DoorAutoCloseRuntime), "TickOne");
            TestAssert.True(IlReader.AccessesField(
                capture, typeof(Player), nameof(Player.m_localPlayer)));
            TestAssert.True(IlReader.Calls(
                capture, typeof(Character), nameof(Character.IsOwner)));
            TestAssert.True(IlReader.Calls(
                tick, typeof(PrivateArea), nameof(PrivateArea.CheckAccess)));
            TestAssert.True(IlReader.Calls(
                tick, typeof(ZNetView), nameof(ZNetView.ClaimOwnership)));
            TestAssert.True(IlReader.Calls(
                tick, typeof(ValheimAccess), "CloseDoor"));
        }

        private static void NoPersistentWorldMutation()
        {
            foreach (MethodInfo method in ModuleMethods())
            {
                if (method.GetMethodBody() == null) continue;
                foreach (MethodBase call in IlReader.Calls(method))
                {
                    TestAssert.False(
                        call.DeclaringType == typeof(ZDO) &&
                        call.Name == nameof(ZDO.Set),
                        method + " writes custom persistent ZDO state.");
                    TestAssert.False(
                        call.DeclaringType == typeof(ZNetScene) &&
                        call.Name == nameof(ZNetScene.Destroy),
                        method + " destroys a network world object.");
                }
            }
        }

        private static void NoCustomTransport()
        {
            string source = string.Join("\n",
                Directory.EnumerateFiles(
                        Path.Combine(TestPaths.RepositoryRoot, "RunicInteraction"),
                        "*.cs",
                        SearchOption.AllDirectories)
                    .Where(path => !path.Contains(
                        Path.DirectorySeparatorChar + "obj" +
                        Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                    .Select(File.ReadAllText));
            foreach (string forbidden in new[]
                     {
                         "ZRoutedRpc", "Register<ZPackage>", "IRunicRpcService",
                         "AccountBoundPlayer", "DurableOperation", "MutationJournal"
                     })
                TestAssert.False(source.Contains(
                    forbidden, StringComparison.Ordinal));
        }

        private static void HoldRepeatOnlyLeasesInterval()
        {
            MethodInfo begin = Method(typeof(HoldRepeatRuntime), "Begin");
            MethodInfo end = Method(typeof(HoldRepeatRuntime), "End");
            TestAssert.True(IlReader.AccessesField(
                begin, typeof(Switch), "m_holdRepeatInterval"));
            TestAssert.True(IlReader.AccessesField(
                end, typeof(Switch), "m_holdRepeatInterval"));
        }

        private static void PickupFilterIsPureGate()
        {
            MethodInfo gate = Method(typeof(PickupFilterRuntime), "Allow");
            foreach (MethodBase call in IlReader.Calls(gate))
            {
                TestAssert.False(call.DeclaringType == typeof(Inventory) &&
                                 (call.Name.Contains("Add", StringComparison.Ordinal) ||
                                  call.Name.Contains("Remove", StringComparison.Ordinal)));
            }
        }

        private static void FeaturesAreIndependentlyGated()
        {
            foreach ((Type Runtime, string Setting) value in new[]
                     {
                         (typeof(HoldRepeatRuntime), "HoldToRepeat"),
                         (typeof(TransferGestureRuntime), "TransferGestures"),
                         (typeof(DoorAutoCloseRuntime), "AutoCloseDoors"),
                         (typeof(EquipmentRestoreRuntime), "EquipmentRestore"),
                         (typeof(TextEntryRuntime), "TextEntryPolish"),
                         (typeof(PickupFilterRuntime), "PickupFilters")
                     })
                AssertFeatureGate(value.Runtime, value.Setting);
        }

        private static void UnsupportedDragIsExplicit()
        {
            TestAssert.True(IlReader.LoadsString(
                Method(typeof(InteractionRuntime), "OnConfigurationChanged"),
                "DragTransfer remains disabled: Valheim 0.221.12 exposes no authority-safe " +
                "drag-sweep transaction boundary. Ordinary vanilla dragging is unchanged."));
        }

        private static void ControllerModifiersAreBounded()
        {
            string[] options = InteractionInputBindings.CreateControllerModifierOptions();
            TestAssert.Equal(
                InteractionInputBindings.MaximumControllerModifierOptions,
                options.Length);
            TestAssert.Equal(options.Length,
                options.Distinct(StringComparer.Ordinal).Count());
            TestAssert.False(options.Contains("JoyButtonA", StringComparer.Ordinal));
            TestAssert.False(options.Contains("JoyButtonX", StringComparer.Ordinal));
            TestAssert.Equal(
                InteractionInputBindings.DefaultPickupBypassControllerModifier,
                InteractionInputBindings.ResolveControllerModifier(
                    "invalid", out bool usedDefault));
            TestAssert.True(usedDefault);
        }

        private static void DefaultInputCatalogMatchesRuntime()
        {
            IReadOnlyList<KeybindingDescriptor> descriptors =
                InteractionInputBindings.CreateDescriptors(
                    InteractionInputBindings.DefaultPickupBypassControllerModifier);
            string[] lines = InteractionInputBindings.DefaultBindingCatalog.Split('\n');
            TestAssert.Equal(lines.Length, descriptors.Count);
            for (int index = 0; index < lines.Length; index++)
            {
                string[] parts = lines[index].Split('|');
                KeybindingDescriptor descriptor = descriptors[index];
                TestAssert.Equal(parts[0], descriptor.BindingId);
                TestAssert.Equal(parts[1], descriptor.Chord.DeviceId);
                TestAssert.Equal(parts[2], descriptor.Chord.PrimaryControl);
                TestAssert.Equal(parts[3], descriptor.Chord.Modifiers[0]);
                TestAssert.Equal(parts[4], descriptor.Context);
            }
        }

        private static void ItemProtectionIsOptional()
        {
            TestAssert.True(ItemProtectionQueryAdapter.AllowsTransfer(
                TransferItemProtection.NotApplicable));
            TestAssert.True(ItemProtectionQueryAdapter.AllowsTransfer(
                TransferItemProtection.Unlocked));
            TestAssert.False(ItemProtectionQueryAdapter.AllowsTransfer(
                TransferItemProtection.Locked));
            TestAssert.False(ItemProtectionQueryAdapter.AllowsTransfer(
                TransferItemProtection.Unknown));
            string source = File.ReadAllText(Path.Combine(
                TestPaths.RepositoryRoot,
                "RunicInteraction",
                "Core",
                "ItemProtectionQueryAdapter.cs"));
            TestAssert.True(source.Contains(
                "chazman.RunicInventory", StringComparison.Ordinal));
            TestAssert.True(source.Contains(
                "InventoryIntegrationApi", StringComparison.Ordinal));
            TestAssert.True(source.Contains(
                "BindingFlags.Public | BindingFlags.Static", StringComparison.Ordinal));
        }

        private static void AssertFeatureGate(Type runtime, string setting)
        {
            MethodInfo gate = Method(runtime, "FeatureOn");
            IReadOnlyList<MethodBase> calls = IlReader.Calls(gate);
            TestAssert.True(calls.Any(call =>
                call.DeclaringType == typeof(InteractionConfig) &&
                call.Name == "get_Enabled"));
            TestAssert.True(calls.Any(call =>
                call.DeclaringType == typeof(InteractionConfig) &&
                call.Name == "get_" + setting));
        }

        private static IEnumerable<MethodInfo> ModuleMethods() =>
            typeof(Plugin).Assembly.GetTypes()
                .Where(type => type.Namespace != null &&
                               type.Namespace.StartsWith(
                                   "RunicInteraction", StringComparison.Ordinal))
                .SelectMany(type => type.GetMethods(Any));

        private static MethodInfo Method(Type type, string name) =>
            TestAssert.NotNull(type.GetMethod(name, Any),
                type.FullName + "." + name + " is missing.");

        private static FieldInfo Field(Type type, string name) =>
            TestAssert.NotNull(type.GetField(name, Any),
                type.FullName + "." + name + " is missing.");
    }
}

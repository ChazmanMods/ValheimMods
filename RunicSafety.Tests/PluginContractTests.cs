using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using RunicSafety.Api;
using RunicSafety.Integration;
using RunicSafety.Services;
using UnityEngine;

namespace RunicSafety.Tests
{
    internal static class PluginContractTests
    {
        internal static void Register()
        {
            TestRunner.Run("plugin identity and version are exact", IdentityExact);
            TestRunner.Run("plugin has no hard dependency attributes", NoHardDependencies);
            TestRunner.Run("startup audits installed targets before patching", AuditBeforePatch);
            TestRunner.Run("startup attaches the standalone API before patching", ApiBeforePatch);
            TestRunner.Run("shutdown detaches API patches and runtime", ShutdownComplete);
            TestRunner.Run("Safety binary has no Runic assembly references", NoRunicReferences);
            TestRunner.Run("Safety plugin has no per-frame callback", NoPerFrameCallback);
            TestRunner.Run("Safety never creates or destroys world objects", NoWorldObjectMutation);
            TestRunner.Run("Safety never directly writes ZDO or removes inventory", NoDirectStateMutation);
            TestRunner.Run("public standalone contracts are public", ContractsPublic);
            TestRunner.Run("Inventory integration is optional reflection", ProtectionIntegrationIsOptional);
            TestRunner.Run("routed sender cannot grant remote administrator bypass", RoutedSenderCannotGrantAdministratorBypass);
            TestRunner.Run("durable custody and remote bridges are absent", RemovedArchitectureAbsent);
        }

        private static void IdentityExact()
        {
            BepInPlugin identity = TestAssert.NotNull(typeof(Plugin).GetCustomAttribute<BepInPlugin>());
            TestAssert.Equal("chazman.RunicSafety", Plugin.Guid);
            TestAssert.Equal("Runic Safety", Plugin.Name);
            TestAssert.Equal("1.0.0", Plugin.Version);
            TestAssert.Equal("runic.safety", Plugin.ModuleId);
            TestAssert.Equal("1.0", Plugin.ProtocolVersion);
            TestAssert.Equal(Plugin.Guid, identity.GUID);
            TestAssert.Equal(Plugin.Version, identity.Version.ToString());
            TestAssert.Equal(new System.Version(1, 0, 0, 0), typeof(Plugin).Assembly.GetName().Version);
        }

        private static void NoHardDependencies() =>
            TestAssert.Equal(0, typeof(Plugin).GetCustomAttributes<BepInDependency>().Count());

        private static void AuditBeforePatch()
        {
            IReadOnlyList<MethodBase> calls = IlReader.Calls(Method(typeof(Plugin), "Awake"));
            int audit = IlReader.CallIndex(calls, typeof(ValheimContracts), "Initialize");
            int patch = IlReader.CallIndex(calls, typeof(Harmony), nameof(Harmony.PatchAll));
            TestAssert.True(audit >= 0 && patch > audit);
        }

        private static void ApiBeforePatch()
        {
            IReadOnlyList<MethodBase> calls = IlReader.Calls(Method(typeof(Plugin), "Awake"));
            int attach = IlReader.CallIndex(calls, typeof(SafetyIntegrationApi), "Attach");
            int patch = IlReader.CallIndex(calls, typeof(Harmony), nameof(Harmony.PatchAll));
            TestAssert.True(attach >= 0 && patch > attach);
        }

        private static void ShutdownComplete()
        {
            MethodInfo method = Method(typeof(Plugin), "ShutdownRuntime");
            TestAssert.True(IlReader.Calls(method, typeof(Harmony), nameof(Harmony.UnpatchSelf)));
            TestAssert.True(IlReader.Calls(method, typeof(SafetyIntegrationApi), "Detach"));
            TestAssert.True(IlReader.Calls(method, typeof(SafetyRuntime), "Shutdown"));
        }

        private static void NoRunicReferences()
        {
            string[] references = typeof(Plugin).Assembly.GetReferencedAssemblies()
                .Select(item => item.Name)
                .ToArray();
            foreach (string forbidden in new[]
                     {
                         "RunicCore", "RunicPersistence", "RunicPermissions", "RunicTransactions",
                         "RunicInventory", "RunicStorage", "RunicProduction", "RunicCrafting",
                         "RunicAgriculture", "RunicPortals", "RunicInteraction"
                     })
                TestAssert.False(references.Contains(forbidden, StringComparer.OrdinalIgnoreCase), forbidden);
        }

        private static void NoPerFrameCallback()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public |
                                       BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            TestAssert.Equal(null, typeof(Plugin).GetMethod("Update", flags));
            TestAssert.Equal(null, typeof(Plugin).GetMethod("FixedUpdate", flags));
            TestAssert.Equal(null, typeof(Plugin).GetMethod("LateUpdate", flags));
        }

        private static void NoWorldObjectMutation()
        {
            foreach (MethodInfo method in AllMethods())
            {
                TestAssert.False(IlReader.Calls(
                    method, typeof(UnityEngine.Object), nameof(UnityEngine.Object.Instantiate)),
                    method.DeclaringType?.FullName + "." + method.Name + " instantiates a world object.");
                TestAssert.False(IlReader.Calls(method, typeof(ZNetScene), nameof(ZNetScene.Destroy)),
                    method.DeclaringType?.FullName + "." + method.Name + " destroys a world object.");
            }
        }

        private static void NoDirectStateMutation()
        {
            foreach (MethodInfo method in AllMethods())
            {
                TestAssert.False(IlReader.Calls(method, typeof(ZDO), nameof(ZDO.Set)),
                    method.DeclaringType?.FullName + "." + method.Name + " writes a ZDO.");
                TestAssert.False(IlReader.Calls(method, typeof(Inventory), nameof(Inventory.RemoveItem)),
                    method.DeclaringType?.FullName + "." + method.Name + " removes inventory state.");
                TestAssert.False(IlReader.Calls(method, typeof(Inventory), nameof(Inventory.RemoveOneItem)),
                    method.DeclaringType?.FullName + "." + method.Name + " removes inventory state.");
            }
        }

        private static void ContractsPublic()
        {
            foreach (Type type in new[]
                     {
                         typeof(SafetyIntegrationApi), typeof(IContextualConfirmationService),
                         typeof(IProtectedItemPolicy), typeof(IRecoveryPlanningService),
                         typeof(IMigrationBackupService), typeof(ICompatibilityGate),
                         typeof(ISafetyDiagnosticService), typeof(ISafetyStatusService)
                     })
                TestAssert.True(type.IsPublic, type.FullName + " is not public.");
        }

        private static void ProtectionIntegrationIsOptional()
        {
            Type adapter = typeof(Plugin).Assembly.GetType(
                "RunicSafety.Services.InventoryProtectionAdapter", true);
            MethodInfo method = Method(adapter, "TryResolveMethod");
            TestAssert.True(IlReader.Calls(method).Any(call =>
                call.DeclaringType?.FullName == "BepInEx.Bootstrap.Chainloader" &&
                call.Name == "get_PluginInfos"));
            TestAssert.False(typeof(Plugin).Assembly.GetReferencedAssemblies().Any(
                reference => string.Equals(reference.Name, "RunicInventory", StringComparison.OrdinalIgnoreCase)));
        }

        private static void RoutedSenderCannotGrantAdministratorBypass()
        {
            MethodInfo method = Method(typeof(SafetyRuntime), "IsSenderAdministrator");
            TestAssert.True(IlReader.Calls(method, typeof(ZNet), nameof(ZNet.IsServer)));
            TestAssert.True(IlReader.Calls(method, typeof(ZNet), nameof(ZNet.GetUID)));
            TestAssert.True(IlReader.Calls(method, typeof(ZNet), nameof(ZNet.LocalPlayerIsAdminOrHost)));
            TestAssert.False(IlReader.Calls(method, typeof(ZNet), nameof(ZNet.GetPeer)));
            TestAssert.False(IlReader.Calls(method, typeof(ZNet), nameof(ZNet.IsAdmin)));
        }

        private static void RemovedArchitectureAbsent()
        {
            Type[] types = typeof(Plugin).Assembly.GetTypes();
            foreach (string fragment in new[]
                     {
                         "DurableCustody", "CustodyDisposition", "SafetyRemoteCompatibility",
                         "HighImpactConfirmationAdapter"
                     })
                TestAssert.False(types.Any(type =>
                    type.FullName?.Contains(fragment, StringComparison.Ordinal) == true), fragment);
        }

        private static MethodInfo[] AllMethods() => typeof(Plugin).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Static |
                                                BindingFlags.Public | BindingFlags.NonPublic |
                                                BindingFlags.DeclaredOnly))
            .Where(method => method.GetMethodBody() != null)
            .ToArray();

        private static MethodInfo Method(Type type, string name) => TestAssert.NotNull(
            type.GetMethod(name, BindingFlags.Instance | BindingFlags.Static |
                                 BindingFlags.Public | BindingFlags.NonPublic),
            type.FullName + "." + name + " missing.");
    }
}

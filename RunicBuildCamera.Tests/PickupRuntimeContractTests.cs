using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RunicBuildCamera.Core;
using RunicBuildCamera.Integration;
using UnityEngine;

namespace RunicBuildCamera.Tests
{
    internal static class PickupRuntimeContractTests
    {
        internal static void Register()
        {
            TestRunner.Run("pickup buffers and mutation budget are hard-bounded", PickupWorkIsHardBounded);
            TestRunner.Run("pickup uses installed private auto-pickup fields exactly", AutoPickupFieldsResolveExactly);
            TestRunner.Run("pickup discovers from the detached camera", PickupDiscoversFromCamera);
            TestRunner.Run("pickup validates each item and ward independently", PickupValidatesEveryTarget);
            TestRunner.Run("ownership request precedes eligibility commit and pickup", OwnershipPrecedesCommit);
            TestRunner.Run("pickup cooldown storage is bounded and resettable", CooldownsAreBoundedAndResettable);
            TestRunner.Run("pickup handles only loose ItemDrop rigidbodies", PickupHandlesOnlyLooseDrops);
            TestRunner.Run("pickup runtime never accesses containers", PickupNeverAccessesContainers);
            TestRunner.Run("pickup faults reset transient state", PickupFaultsResetState);
        }

        private static void PickupWorkIsHardBounded()
        {
            Type runtime = typeof(RemotePickupRuntime);
            FieldInfo buffer = runtime.GetField(
                "ColliderBuffer", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.Equal(typeof(Collider[]), TestAssert.NotNull(
                buffer, "Pickup collider buffer is missing.").FieldType);
            MethodInfo scan = runtime.GetMethod("Scan", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.True(IlReader.LoadsInt32(scan, PickupPolicy.MaximumAttemptsPerScan),
                "Per-scan mutation limit is no longer compiled into the scan loop.");
            TestAssert.True(IlReader.Calls(
                scan, typeof(Physics), nameof(Physics.OverlapSphereNonAlloc)),
                "Pickup discovery allocates instead of using the bounded non-allocating overlap API.");
            TestAssert.False(IlReader.Calls(scan, typeof(Physics), nameof(Physics.OverlapSphere)),
                "Pickup discovery uses the allocating overlap API.");

            MethodBase initializer = TestAssert.NotNull(
                runtime.TypeInitializer, "Pickup runtime static initializer is missing.");
            TestAssert.True(IlReader.LoadsInt32(initializer, PickupPolicy.ColliderCapacity),
                "Collider buffer capacity drifted from the policy contract.");
        }

        private static void AutoPickupFieldsResolveExactly()
        {
            const BindingFlags AllFields = BindingFlags.Public | BindingFlags.NonPublic |
                                           BindingFlags.Instance | BindingFlags.Static;
            FieldInfo enabled = typeof(Player).GetField("m_enableAutoPickup", AllFields);
            FieldInfo mask = typeof(Player).GetField("m_autoPickupMask", AllFields);
            TestAssert.NotNull(enabled, "Installed Player.m_enableAutoPickup is missing.");
            TestAssert.NotNull(mask, "Installed Player.m_autoPickupMask is missing.");
            TestAssert.Equal(typeof(bool), enabled.FieldType);
            TestAssert.True(enabled.IsStatic, "Installed auto-pickup enable flag changed ownership.");
            TestAssert.Equal(typeof(int), mask.FieldType);
            TestAssert.False(mask.IsStatic, "Installed auto-pickup layer mask changed ownership.");

            MethodBase initializer = typeof(RemotePickupRuntime).TypeInitializer;
            TestAssert.True(IlReader.LoadsString(initializer, "m_enableAutoPickup"));
            TestAssert.True(IlReader.LoadsString(initializer, "m_autoPickupMask"));
            TestAssert.True(IlReader.Calls(initializer, typeof(AccessTools), nameof(AccessTools.Field)));

            MethodInfo reader = typeof(RemotePickupRuntime).GetMethod(
                "TryReadAutoPickupState", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.True(IlReader.Calls(reader, typeof(FieldInfo), nameof(FieldInfo.GetValue)));
        }

        private static void PickupDiscoversFromCamera()
        {
            MethodInfo tick = typeof(RemotePickupRuntime).GetMethod(
                "Tick", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.True(IlReader.Calls(
                tick, typeof(BuildCameraRuntime), "TryGetActiveContext"));
            TestAssert.True(IlReader.Calls(tick, typeof(RemotePickupRuntime), "Scan"));

            MethodInfo scan = typeof(RemotePickupRuntime).GetMethod(
                "Scan", BindingFlags.Static | BindingFlags.NonPublic);
            ParameterInfo camera = scan.GetParameters().Single(parameter =>
                string.Equals(parameter.Name, "cameraPosition", StringComparison.Ordinal));
            TestAssert.Equal(typeof(Vector3), camera.ParameterType);
            TestAssert.True(IlReader.Calls(
                scan, typeof(Physics), nameof(Physics.OverlapSphereNonAlloc)));
        }

        private static void PickupValidatesEveryTarget()
        {
            MethodInfo evaluate = typeof(RemotePickupRuntime).GetMethod(
                "TryEvaluate", BindingFlags.Static | BindingFlags.NonPublic);
            IReadOnlyList<MethodBase> calls = IlReader.Calls(evaluate);
            int range = IlReader.CallIndex(calls, typeof(PickupPolicy), "IsWithinRange");
            int ward = IlReader.CallIndex(calls, typeof(PrivateArea), nameof(PrivateArea.CheckAccess));
            int unique = FindCall(calls, nameof(Player.HaveUniqueKey));
            int capacity = FindCall(calls, nameof(Inventory.CanAddItem));
            int weight = FindCall(calls, nameof(Inventory.GetTotalWeight));
            int maxWeight = FindCall(calls, nameof(Player.GetMaxCarryWeight));
            int piece = IlReader.CallIndex(calls, typeof(ItemDrop), nameof(ItemDrop.IsPiece));
            int tar = IlReader.CallIndex(calls, typeof(ItemDrop), nameof(ItemDrop.InTar));
            int policy = IlReader.CallIndex(calls, typeof(PickupPolicy), "Evaluate");
            TestAssert.True(range >= 0 && ward > range,
                "Ward access must be checked at each finite in-range target position.");
            TestAssert.True(unique >= 0 && capacity > unique && weight > capacity && maxWeight > weight,
                "Unique, inventory capacity, and carry-weight checks drifted.");
            TestAssert.True(piece >= 0 && tar > piece && policy > tar,
                "Loose-item exclusions are not represented in the final policy facts.");
            TestAssert.True(IlReader.AccessesField(
                evaluate, typeof(ItemDrop), nameof(ItemDrop.m_autoPickup)));
            TestAssert.True(IlReader.AccessesField(
                evaluate, typeof(ItemDrop.ItemData.SharedData), "m_questItem"));
        }

        private static void OwnershipPrecedesCommit()
        {
            MethodInfo scan = typeof(RemotePickupRuntime).GetMethod(
                "Scan", BindingFlags.Static | BindingFlags.NonPublic);
            IReadOnlyList<MethodBase> calls = IlReader.Calls(scan);
            int firstEvaluate = IlReader.CallIndex(calls, typeof(RemotePickupRuntime), "TryEvaluate");
            int requestOwn = IlReader.CallIndex(calls, typeof(ItemDrop), nameof(ItemDrop.RequestOwn));
            int canPickup = IlReader.CallIndex(calls, typeof(ItemDrop), nameof(ItemDrop.CanPickup));
            int secondEvaluate = IlReader.CallIndex(
                calls, typeof(RemotePickupRuntime), "TryEvaluate", firstEvaluate + 1);
            int pickup = IlReader.CallIndex(calls, typeof(Humanoid), nameof(Humanoid.Pickup));
            TestAssert.True(
                firstEvaluate >= 0 && requestOwn > firstEvaluate && canPickup > requestOwn &&
                secondEvaluate > canPickup && pickup > secondEvaluate,
                "Expected evaluate -> RequestOwn -> CanPickup -> re-evaluate -> vanilla Pickup order.");
            TestAssert.True(calls.Count(method =>
                method.DeclaringType == typeof(ItemDrop) && method.Name == nameof(ItemDrop.Load)) >= 2,
                "Item state must be loaded before both evaluation boundaries.");
        }

        private static void CooldownsAreBoundedAndResettable()
        {
            Type runtime = typeof(RemotePickupRuntime);
            FieldInfo cooldowns = runtime.GetField(
                "Cooldowns", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.True(cooldowns != null && cooldowns.FieldType.IsGenericType &&
                            cooldowns.FieldType.GetGenericTypeDefinition() == typeof(Dictionary<,>));

            MethodInfo set = runtime.GetMethod(
                "SetCooldown", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.True(IlReader.LoadsInt32(set, PickupPolicy.MaximumCooldownEntries),
                "Cooldown table capacity is no longer hard-bounded.");
            TestAssert.True(IlReader.Calls(set, runtime, "RemoveOldestCooldown"));

            MethodInfo reset = runtime.GetMethod(
                "ResetTransientState", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.True(IlReader.Calls(reset, typeof(Array), nameof(Array.Clear)));
            TestAssert.True(IlReader.Calls(reset, typeof(HashSet<ZDOID>), nameof(HashSet<ZDOID>.Clear)));
            TestAssert.True(IlReader.Calls(
                reset,
                cooldowns.FieldType,
                nameof(Dictionary<ZDOID, object>.Clear)));
        }

        private static void PickupHandlesOnlyLooseDrops()
        {
            MethodInfo resolve = typeof(RemotePickupRuntime).GetMethod(
                "ResolveLooseItem", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.True(IlReader.Calls(resolve, typeof(Collider), "get_attachedRigidbody"));
            TestAssert.True(IlReader.Calls(resolve, typeof(Component), "GetComponent"),
                "Loose pickup does not resolve ItemDrop from the physics body.");
            TestAssert.True(IlReader.ReferencesType(resolve, typeof(FloatingTerrainDummy)),
                "Floating terrain proxy items no longer resolve through their parent drop.");
        }

        private static void PickupNeverAccessesContainers()
        {
            foreach (MethodInfo method in typeof(RemotePickupRuntime).GetMethods(
                         BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (method.GetMethodBody() == null) continue;
                TestAssert.False(IlReader.ReferencesType(method, typeof(Container)),
                    method.Name + " accesses Container; remote pickup must stay loose-drop only.");
            }
        }

        private static void PickupFaultsResetState()
        {
            MethodInfo postfix = typeof(GameCameraRemoteEffectsPatch).GetMethod(
                "Postfix", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.True(IlReader.Calls(postfix, typeof(RemotePickupRuntime), "Tick"));
            TestAssert.True(IlReader.Calls(postfix, typeof(RemotePickupRuntime), "Reset"));

            MethodInfo shutdown = typeof(RemotePickupRuntime).GetMethod(
                "Shutdown", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.True(IlReader.Calls(
                shutdown, typeof(RemotePickupRuntime), "ResetTransientState"));
        }

        private static int FindCall(IReadOnlyList<MethodBase> calls, string name)
        {
            for (int index = 0; index < calls.Count; index++)
            {
                if (string.Equals(calls[index].Name, name, StringComparison.Ordinal)) return index;
            }
            return -1;
        }
    }
}

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
            TestRunner.Run("pickup validates loose items without building-only ward rules", PickupValidatesEveryTarget);
            TestRunner.Run("ownership request precedes eligibility commit and pickup", OwnershipPrecedesCommit);
            TestRunner.Run("pickup cooldown storage is bounded and resettable", CooldownsAreBoundedAndResettable);
            TestRunner.Run("pickup registry includes drops without physics colliders", PickupUsesLoadedDrops);
            TestRunner.Run("pickup runtime never accesses containers", PickupNeverAccessesContainers);
            TestRunner.Run("pickup faults reset transient state", PickupFaultsResetState);
            TestRunner.Run("player ward pickup adapter matches installed game", PlayerWardAdapterMatchesGame);
        }

        private static void PlayerWardAdapterMatchesGame()
        {
            FieldInfo areas = typeof(PrivateArea).GetField("m_allAreas", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.NotNull(areas, "PrivateArea registry missing.");
            TestAssert.True(areas.IsStatic);
            TestAssert.Equal(typeof(List<PrivateArea>), areas.FieldType);
            TestAssert.NotNull(typeof(PrivateArea).GetMethod("IsEnabled", BindingFlags.Instance | BindingFlags.NonPublic).CreateDelegate(typeof(Func<PrivateArea, bool>)), "Enabled delegate failed.");
            TestAssert.NotNull(typeof(PrivateArea).GetMethod("IsInside", BindingFlags.Instance | BindingFlags.NonPublic).CreateDelegate(typeof(Func<PrivateArea, Vector3, float, bool>)), "Inside delegate failed.");
            TestAssert.NotNull(typeof(PrivateArea).GetMethod("HaveLocalAccess", BindingFlags.Instance | BindingFlags.NonPublic).CreateDelegate(typeof(Func<PrivateArea, bool>)), "Access delegate failed.");
            MethodInfo allows = typeof(PlayerWardPickupAccess).GetMethod("Allows",
                BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.True(IlReader.Calls(allows, typeof(Piece), nameof(Piece.IsPlacedByPlayer)));
            MethodInfo evaluate = typeof(RemotePickupRuntime).GetMethod("TryEvaluate",
                BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.True(IlReader.Calls(evaluate, typeof(PlayerWardPickupAccess), "Allows"));
        }

        private static void PickupWorkIsHardBounded()
        {
            Type runtime = typeof(RemotePickupRuntime);
            FieldInfo buffer = runtime.GetField(
                "ItemBuffer", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.Equal(typeof(ItemDrop[]), TestAssert.NotNull(
                buffer, "Pickup candidate buffer is missing.").FieldType);
            MethodInfo scan = runtime.GetMethod("Scan", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.True(IlReader.LoadsInt32(scan, PickupPolicy.MaximumAttemptsPerScan),
                "Per-scan mutation limit is no longer compiled into the scan loop.");
            TestAssert.True(IlReader.Calls(scan, typeof(PickupScanWindow), "CopyNext"));
            TestAssert.False(IlReader.Calls(scan, typeof(Physics), nameof(Physics.OverlapSphereNonAlloc)),
                "Collider-only discovery can miss drops and starve a saturated buffer.");

            MethodBase initializer = TestAssert.NotNull(
                runtime.TypeInitializer, "Pickup runtime static initializer is missing.");
            TestAssert.True(IlReader.LoadsInt32(initializer, PickupPolicy.CandidateCapacity),
                "Candidate buffer capacity drifted from the policy contract.");
        }

        private static void AutoPickupFieldsResolveExactly()
        {
            const BindingFlags AllFields = BindingFlags.Public | BindingFlags.NonPublic |
                                           BindingFlags.Instance | BindingFlags.Static;
            FieldInfo enabled = typeof(Player).GetField("m_enableAutoPickup", AllFields);
            TestAssert.NotNull(enabled, "Installed Player.m_enableAutoPickup is missing.");
            TestAssert.Equal(typeof(bool), enabled.FieldType);
            TestAssert.True(enabled.IsStatic);
            FieldInfo registry = typeof(ItemDrop).GetField("s_instances", AllFields);
            TestAssert.NotNull(registry, "Installed item registry is missing.");
            TestAssert.True(registry.IsStatic);
            TestAssert.Equal(typeof(List<ItemDrop>), registry.FieldType);

            MethodBase initializer = typeof(RemotePickupRuntime).TypeInitializer;
            TestAssert.True(IlReader.LoadsString(initializer, "m_enableAutoPickup"));
            TestAssert.True(IlReader.LoadsString(initializer, "s_instances"));
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
                scan, typeof(PickupScanWindow), "CopyNext"));
        }

        private static void PickupValidatesEveryTarget()
        {
            MethodInfo evaluate = typeof(RemotePickupRuntime).GetMethod(
                "TryEvaluate", BindingFlags.Static | BindingFlags.NonPublic);
            IReadOnlyList<MethodBase> calls = IlReader.Calls(evaluate);
            int range = IlReader.CallIndex(calls, typeof(PickupPolicy), "IsWithinRange");

            int unique = FindCall(calls, nameof(Player.HaveUniqueKey));
            int capacity = FindCall(calls, nameof(Inventory.CanAddItem));
            int weight = FindCall(calls, nameof(Inventory.GetTotalWeight));
            int maxWeight = FindCall(calls, nameof(Player.GetMaxCarryWeight));
            int piece = IlReader.CallIndex(calls, typeof(ItemDrop), nameof(ItemDrop.IsPiece));
            int tar = IlReader.CallIndex(calls, typeof(ItemDrop), nameof(ItemDrop.InTar));
            int policy = IlReader.CallIndex(calls, typeof(PickupPolicy), "Evaluate");
            TestAssert.True(range >= 0);
            TestAssert.False(IlReader.Calls(evaluate, typeof(PrivateArea), nameof(PrivateArea.CheckAccess)),
                "Loose pickup must not add the building-only ward restriction.");
            TestAssert.True(calls.OfType<MethodInfo>().Any(method =>
                method.Name == "GetComponent" && method.IsGenericMethod &&
                method.GetGenericArguments().Contains(typeof(Fish))),
                "Fish actors must not bypass fishing rules through ItemDrop pickup.");
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

        private static void PickupUsesLoadedDrops()
        {
            MethodInfo scan = typeof(RemotePickupRuntime).GetMethod(
                "Scan", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.True(IlReader.Calls(scan, typeof(Behaviour), "get_isActiveAndEnabled"));
            TestAssert.True(IlReader.Calls(scan, typeof(GameObject), "get_activeInHierarchy"));
            TestAssert.False(IlReader.Calls(scan, typeof(Collider), "get_attachedRigidbody"));
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RunicBuildCamera.Integration;
using UnityEngine;

namespace RunicBuildCamera.Tests
{
    internal static class HarmonyContractTests
    {
        private const BindingFlags AllMethods = BindingFlags.Public | BindingFlags.NonPublic |
                                                BindingFlags.Instance | BindingFlags.Static |
                                                BindingFlags.FlattenHierarchy;

        internal static void Register()
        {
            TestRunner.Run("Valheim adapter resolves exact installed signatures", AdapterResolvesExactInstalledSignatures);
            TestRunner.Run("all Harmony targets resolve exactly", AllHarmonyTargetsResolveExactly);
            TestRunner.Run("Player.Update remains live while SetControls freezes the avatar", FreezePatchesSetControlsOnly);
            TestRunner.Run("SetControls prefix freezes every avatar action input", SetControlsPrefixFreezesEveryInput);
            TestRunner.Run("mouse look is isolated without skipping Player.Update", MouseLookIsIsolated);
            TestRunner.Run("placement input restoration runs before Precision", PlacementInputRunsBeforePrecision);
            TestRunner.Run("GameCamera prefix owns detached transform fail-safely", GameCameraPrefixOwnsDetachedTransform);
            TestRunner.Run("placement range leases cover ghost and commit paths", PlacementRangeLeasesCoverBothPaths);
            TestRunner.Run("range lease restoration is nested and fail-safe", RangeLeaseRestorationIsNestedAndFailSafe);
            TestRunner.Run("required station range follows the scoped lease", StationRangeUsesScopedLease);
            TestRunner.Run("PieceRayTest postfix preserves the exact remote cap", PieceRayPostfixPreservesCap);
            TestRunner.Run("controller run input enables fast camera movement", ControllerRunEnablesFastMovement);
            TestRunner.Run("camera runtime has complete exit gates", CameraRuntimeHasCompleteExitGates);
            TestRunner.Run("build mode gate requires an equipped build-piece table", BuildModeGateRequiresBuildPieces);
        }

        private static void AdapterResolvesExactInstalledSignatures()
        {
            AssertMethod(
                InstalledMethod(typeof(Player), "UpdatePlacement", typeof(bool), typeof(float)),
                typeof(Player), "UpdatePlacement", typeof(void), typeof(bool), typeof(float));
            AssertMethod(
                InstalledMethod(typeof(Player), "UpdatePlacementGhost", typeof(bool)),
                typeof(Player), "UpdatePlacementGhost", typeof(void), typeof(bool));
            AssertMethod(
                InstalledMethod(typeof(GameCamera), "UpdateCamera", typeof(float)),
                typeof(GameCamera), "UpdateCamera", typeof(void), typeof(float));
            AssertMethod(
                InstalledMethod(
                    typeof(Player),
                    "SetControls",
                    typeof(Vector3),
                    typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool),
                    typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool),
                    typeof(bool)),
                typeof(Player),
                "SetControls",
                typeof(void),
                typeof(Vector3),
                typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool),
                typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool),
                typeof(bool));

            MethodInfo initialize = typeof(ValheimAdapter).GetMethod(
                "Initialize", BindingFlags.Static | BindingFlags.NonPublic);
            foreach (string member in new[]
                     {
                         "m_maxPlaceDistance", "m_rightItem", "UpdatePlacement",
                         "UpdatePlacementGhost", "UpdateCamera", "SetControls", "TakeInput"
                     })
                TestAssert.True(IlReader.LoadsString(initialize, member),
                    "Adapter no longer verifies installed member " + member + ".");
        }

        private static void AllHarmonyTargetsResolveExactly()
        {
            SeedAdapterTargetProperties();
            try
            {
                var targets = new List<MethodBase>();
                Type[] patchTypes = typeof(Plugin).Assembly.GetTypes()
                    .Where(type => type.GetCustomAttributes(typeof(HarmonyPatch), false).Length != 0)
                    .ToArray();
                TestAssert.True(patchTypes.Length >= 13,
                    "Expected camera, input-isolation, range, pickup, and demister patches.");
                foreach (Type patchType in patchTypes)
                {
                    MethodInfo dynamicTarget = patchType.GetMethod(
                        "TargetMethod", BindingFlags.Static | BindingFlags.NonPublic);
                    MethodBase target = dynamicTarget != null
                        ? dynamicTarget.Invoke(null, null) as MethodBase
                        : ResolveAttributeTarget(patchType);
                    TestAssert.NotNull(target, patchType.FullName + " did not resolve a target.");
                    TestAssert.True(target.Module.Assembly == typeof(Player).Assembly,
                        patchType.FullName + " did not resolve into installed assembly_valheim.");
                    targets.Add(target);
                }

                TestAssert.True(targets.Any(target =>
                        target.DeclaringType == typeof(Player) && target.Name == "Update"),
                    "Player.Update input-isolation scope is missing.");
                TestAssert.True(targets.Any(target =>
                        target.DeclaringType == typeof(CraftingStation) &&
                        target.Name == nameof(CraftingStation.GetStationBuildRange)),
                    "Scoped required-station range patch is missing.");
            }
            finally
            {
                ClearAdapterTargetProperties();
            }
        }

        private static void FreezePatchesSetControlsOnly()
        {
            Type patch = typeof(PlayerSetControlsPatch);
            MethodInfo targetMethod = patch.GetMethod(
                "TargetMethod", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.True(IlReader.Calls(
                targetMethod, typeof(ValheimAdapter), "get_SetControlsMethod"));

            MethodInfo updatePrefix = typeof(PlayerUpdateInputScopePatch).GetMethod(
                "Prefix", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo updateFinalizer = typeof(PlayerUpdateInputScopePatch).GetMethod(
                "Finalizer", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.Equal(typeof(void), updatePrefix.ReturnType,
                "Player.Update prefix must never skip the original update.");
            TestAssert.True(IlReader.Calls(
                updatePrefix, typeof(PlayerUpdateInputIsolation), "Enter"));
            TestAssert.Equal(typeof(Exception), updateFinalizer.ReturnType);
            TestAssert.True(IlReader.Calls(
                updateFinalizer, typeof(PlayerUpdateInputIsolation), "Exit"));

            MethodInfo takeInputPostfix = typeof(PlayerTakeInputIsolationPatch).GetMethod(
                "Postfix", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.Equal(
                typeof(bool).MakeByRefType(),
                takeInputPostfix.GetParameters().Single(parameter => parameter.Name == "__result")
                    .ParameterType);
            TestAssert.True(IlReader.Calls(
                takeInputPostfix, typeof(PlayerUpdateInputIsolation), "ShouldSuppress"));
            TestAssert.True(IlReader.Calls(
                takeInputPostfix, typeof(PlayerUpdateInputIsolation), "CaptureTakeInput"));

            MethodInfo placementPrefix = typeof(PlayerUpdatePlacementRangePatch).GetMethod(
                "Prefix", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.True(IlReader.Calls(
                placementPrefix, typeof(PlayerUpdateInputIsolation), "PlacementInput"),
                "Build actions do not recover the original TakeInput decision inside the scope.");
        }

        private static void SetControlsPrefixFreezesEveryInput()
        {
            MethodInfo prefix = typeof(PlayerSetControlsPatch).GetMethod(
                "Prefix", BindingFlags.Static | BindingFlags.NonPublic);
            ParameterInfo[] parameters = prefix.GetParameters();
            TestAssert.Equal(13, parameters.Length);
            TestAssert.Equal(typeof(Player), parameters[0].ParameterType);
            TestAssert.Equal(typeof(Vector3).MakeByRefType(), parameters[1].ParameterType);
            TestAssert.Equal(11, parameters.Skip(2).Count(parameter =>
                parameter.ParameterType == typeof(bool).MakeByRefType()));
            TestAssert.SequenceEqual(
                new[]
                {
                    "__instance", "movedir", "attack", "attackHold", "secondaryAttack",
                    "secondaryAttackHold", "block", "blockHold", "jump", "crouch", "run",
                    "autoRun", "dodge"
                },
                parameters.Select(parameter => parameter.Name));
            TestAssert.True(IlReader.Calls(
                prefix, typeof(BuildCameraRuntime), "ShouldFreezePlayer"));
            TestAssert.True(IlReader.Calls(prefix, typeof(Vector3), "get_zero"));
            int indirectStores = IlReader.Read(prefix).Count(instruction =>
                instruction.OpCode == System.Reflection.Emit.OpCodes.Stind_I1);
            TestAssert.Equal(11, indirectStores,
                "Every mutable Boolean SetControls argument must be cleared.");
        }

        private static void MouseLookIsIsolated()
        {
            MethodInfo prefix = typeof(PlayerSetMouseLookIsolationPatch).GetMethod(
                "Prefix", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.Equal(typeof(void), prefix.ReturnType);
            TestAssert.Equal(
                typeof(Vector2).MakeByRefType(),
                prefix.GetParameters().Single(parameter => parameter.Name == "mouseLook")
                    .ParameterType);
            TestAssert.True(IlReader.Calls(
                prefix, typeof(BuildCameraRuntime), "ShouldFreezePlayer"));
            TestAssert.True(IlReader.Calls(prefix, typeof(Vector2), "get_zero"));
        }

        private static void PlacementInputRunsBeforePrecision()
        {
            MethodInfo prefix = typeof(PlayerUpdatePlacementRangePatch).GetMethod(
                "Prefix", BindingFlags.Static | BindingFlags.NonPublic);
            HarmonyPriority priority = TestAssert.NotNull(
                prefix.GetCustomAttribute<HarmonyPriority>(),
                "UpdatePlacement input recovery lacks an explicit Harmony priority.");
            HarmonyBefore before = TestAssert.NotNull(
                prefix.GetCustomAttribute<HarmonyBefore>(),
                "UpdatePlacement input recovery lacks an explicit Precision ordering rule.");
            TestAssert.Equal(Priority.First, priority.info.priority);
            TestAssert.True(before.info.before.Contains(
                "chazman.RunicPrecisionBuildTool", StringComparer.Ordinal));
            TestAssert.Equal(
                typeof(bool).MakeByRefType(),
                prefix.GetParameters().Single(parameter => parameter.Name == "takeInput")
                    .ParameterType);
        }

        private static void GameCameraPrefixOwnsDetachedTransform()
        {
            MethodInfo prefix = typeof(GameCameraUpdateCameraPatch).GetMethod(
                "Prefix", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.Equal(typeof(bool), prefix.ReturnType);
            TestAssert.True(IlReader.Calls(prefix, typeof(BuildCameraRuntime), "TryUpdateCamera"));
            TestAssert.True(IlReader.Calls(prefix, typeof(BuildCameraRuntime), "ForceStop"));

            MethodInfo update = typeof(BuildCameraRuntime).GetMethod(
                "TryUpdateCamera", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.True(IlReader.Calls(
                update, typeof(Transform), nameof(Transform.SetPositionAndRotation)),
                "Detached runtime never commits its complete position and rotation.");
            TestAssert.True(IlReader.Calls(
                update, typeof(ValheimAdapter), "CanTakeInput"));
            TestAssert.True(IlReader.Calls(
                update, typeof(RunicBuildCamera.Core.BuildCameraSession), "SetPose"));

            MethodInfo step = typeof(BuildCameraRuntime).GetMethod(
                "StepCamera", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.True(IlReader.Calls(step, typeof(ZoneSystem), "GetGroundHeight"),
                "Camera motion no longer clamps below terrain.");
        }

        private static void PlacementRangeLeasesCoverBothPaths()
        {
            AssertRangePatch(typeof(PlayerUpdatePlacementRangePatch), "UpdatePlacementMethod");
            AssertRangePatch(
                typeof(PlayerUpdatePlacementGhostRangePatch), "UpdatePlacementGhostMethod");
        }

        private static void RangeLeaseRestorationIsNestedAndFailSafe()
        {
            Type lease = typeof(ValheimAdapter.RangeLease);
            MethodInfo dispose = lease.GetMethod(nameof(IDisposable.Dispose));
            TestAssert.True(IlReader.Calls(dispose, typeof(ValheimAdapter), "ExitRange"));

            Type state = typeof(ValheimAdapter.RangeState);
            TestAssert.NotNull(state.GetField("Original", BindingFlags.Instance | BindingFlags.NonPublic),
                "Range state does not retain the exact original value.");
            TestAssert.NotNull(state.GetField("Depth", BindingFlags.Instance | BindingFlags.NonPublic),
                "Range state lacks nested lease depth.");

            MethodInfo enter = typeof(ValheimAdapter).GetMethod(
                "EnterRemoteActionRange", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.True(IlReader.AccessesField(enter, state, "Original"));
            TestAssert.True(IlReader.AccessesField(enter, state, "Depth"));
            MethodInfo exit = typeof(ValheimAdapter).GetMethod(
                "ExitRange", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.True(IlReader.AccessesField(exit, state, "Original"));
            TestAssert.True(IlReader.AccessesField(exit, state, "Depth"));

            MethodInfo stop = typeof(BuildCameraRuntime).GetMethod(
                "Stop", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.True(IlReader.Calls(
                stop, typeof(ValheimAdapter), "ForceRestoreRanges"),
                "Camera exit does not force exact range restoration.");
            MethodInfo shutdown = typeof(ValheimAdapter).GetMethod(
                "Shutdown", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.True(IlReader.Calls(shutdown, typeof(ValheimAdapter), "ForceRestoreRanges"));
        }

        private static void StationRangeUsesScopedLease()
        {
            Type state = typeof(ValheimAdapter.RangeState);
            TestAssert.NotNull(state.GetField(
                "Requested", BindingFlags.Instance | BindingFlags.NonPublic),
                "Scoped action range is not retained for required-station checks.");
            MethodInfo enter = typeof(ValheimAdapter).GetMethod(
                "EnterRemoteActionRange", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo scoped = typeof(ValheimAdapter).GetMethod(
                "TryGetScopedStationRange", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.True(IlReader.AccessesField(enter, state, "Requested"));
            TestAssert.True(IlReader.AccessesField(scoped, state, "Requested"));
            TestAssert.True(IlReader.AccessesField(scoped, state, "Depth"));

            MethodInfo postfix = typeof(CraftingStationScopedBuildRangePatch).GetMethod(
                "Postfix", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.True(IlReader.Calls(
                postfix, typeof(ValheimAdapter), "TryGetScopedStationRange"));
            TestAssert.Equal(
                typeof(float).MakeByRefType(),
                postfix.GetParameters().Single().ParameterType);
        }

        private static void PieceRayPostfixPreservesCap()
        {
            MethodInfo enter = typeof(BuildCameraRuntime).GetMethod(
                "EnterRemoteActionRange", BindingFlags.Static | BindingFlags.NonPublic);
            MethodBase adapterCall = IlReader.Calls(enter).Single(method =>
                method.DeclaringType == typeof(ValheimAdapter) &&
                method.Name == "EnterRemoteActionRange");
            TestAssert.SequenceEqual(
                new[] { typeof(Player), typeof(float), typeof(float) },
                adapterCall.GetParameters().Select(parameter => parameter.ParameterType));
            TestAssert.SequenceEqual(
                new[] { "player", "requestedPlayerRange", "requestedStationRange" },
                adapterCall.GetParameters().Select(parameter => parameter.Name));

            MethodInfo limit = typeof(BuildCameraRuntime).GetMethod(
                "IsWithinRemoteActionLimit", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.True(IlReader.Calls(
                limit, typeof(BuildCameraRuntime), "ShouldFreezePlayer"));
            TestAssert.True(IlReader.AccessesField(limit, typeof(Character), "m_eye") ||
                            IlReader.AccessesFieldNamed(limit, "m_eye"));
            TestAssert.True(IlReader.Read(limit).Any(instruction =>
                    instruction.OpCode == System.Reflection.Emit.OpCodes.Mul),
                "Remote cap no longer compares squared distance to squared configured range.");

            MethodInfo postfix = typeof(PlayerPieceRayRemoteLimitPatch).GetMethod(
                "Postfix", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.Equal(typeof(void), postfix.ReturnType);
            TestAssert.Equal(
                typeof(bool).MakeByRefType(),
                postfix.GetParameters().Single(parameter => parameter.Name == "__result")
                    .ParameterType);
            TestAssert.True(IlReader.Calls(
                postfix, typeof(BuildCameraRuntime), "ShouldFreezePlayer"));
            TestAssert.True(IlReader.Calls(
                postfix, typeof(BuildCameraRuntime), "IsWithinRemoteActionLimit"));
            TestAssert.True(typeof(PlayerPieceRayRemoteLimitPatch).GetMethod(
                "Prefix", BindingFlags.Static | BindingFlags.NonPublic) == null,
                "Remote limit must only narrow a successful vanilla PieceRayTest result.");
        }

        private static void ControllerRunEnablesFastMovement()
        {
            MethodInfo step = typeof(BuildCameraRuntime).GetMethod(
                "StepCamera", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.True(IlReader.LoadsString(step, "Run"));
            TestAssert.True(IlReader.LoadsString(step, "JoyRun"));
        }

        private static void CameraRuntimeHasCompleteExitGates()
        {
            MethodInfo tick = typeof(BuildCameraRuntime).GetMethod(
                "Tick", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.True(IlReader.LoadsString(tick, "Hide"));
            TestAssert.True(IlReader.LoadsString(tick, "JoyHide"));
            TestAssert.True(IlReader.Calls(tick, typeof(ValheimAdapter), "IsBuildToolEquipped"));
            TestAssert.True(IlReader.Calls(tick, typeof(BuildCameraRuntime), "Stop"));

            string[] exits =
            {
                "Shutdown", "OnApplicationFocus", "OnConfigurationChanged",
                "OnLocalPlayerAssigned", "ForceStop"
            };
            foreach (string exit in exits)
            {
                MethodInfo method = typeof(BuildCameraRuntime).GetMethod(
                    exit, BindingFlags.Static | BindingFlags.NonPublic);
                TestAssert.True(IlReader.Calls(method, typeof(BuildCameraRuntime), "Stop"),
                    exit + " no longer routes through centralized exit cleanup.");
            }

            MethodInfo tryUpdate = typeof(BuildCameraRuntime).GetMethod(
                "TryUpdateCamera", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.True(IlReader.Calls(
                tryUpdate, typeof(BuildCameraRuntime), "SessionBelongsToUsablePlayer"));
            TestAssert.True(IlReader.Calls(tryUpdate, typeof(BuildCameraRuntime), "Stop"));
        }

        private static void BuildModeGateRequiresBuildPieces()
        {
            MethodInfo equipped = typeof(ValheimAdapter).GetMethod(
                "IsBuildToolEquipped", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.True(IlReader.Calls(equipped, typeof(ValheimAdapter), "IsUsableLocalPlayer"));
            TestAssert.True(IlReader.Calls(equipped).Any(method =>
                    string.Equals(method.Name, nameof(Player.InPlaceMode), StringComparison.Ordinal)),
                "Build mode does not require Player.InPlaceMode().");
            TestAssert.True(IlReader.AccessesFieldNamed(equipped, "m_buildPieces"),
                "Equipped item does not require a non-null PieceTable.");
        }

        private static void AssertRangePatch(Type patchType, string adapterProperty)
        {
            MethodInfo target = patchType.GetMethod(
                "TargetMethod", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.True(IlReader.Calls(
                target, typeof(ValheimAdapter), "get_" + adapterProperty));
            MethodInfo prefix = patchType.GetMethod(
                "Prefix", BindingFlags.Static | BindingFlags.NonPublic);
            ParameterInfo state = prefix.GetParameters().Single(parameter =>
                string.Equals(parameter.Name, "__state", StringComparison.Ordinal));
            TestAssert.Equal(typeof(ValheimAdapter.RangeLease).MakeByRefType(), state.ParameterType);
            TestAssert.True(IlReader.Calls(
                prefix, typeof(BuildCameraRuntime), "EnterRemoteActionRange"));

            MethodInfo finalizer = patchType.GetMethod(
                "Finalizer", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.Equal(typeof(Exception), finalizer.ReturnType);
            TestAssert.Equal(typeof(Exception), finalizer.GetParameters()[0].ParameterType);
            TestAssert.Equal(typeof(ValheimAdapter.RangeLease), finalizer.GetParameters()[1].ParameterType);
            TestAssert.True(IlReader.Calls(
                finalizer, typeof(ValheimAdapter.RangeLease), nameof(IDisposable.Dispose)),
                patchType.Name + " does not restore its lease from a Harmony finalizer.");
        }

        private static MethodBase ResolveAttributeTarget(Type patchType)
        {
            Type declaringType = null;
            string methodName = null;
            Type[] argumentTypes = null;
            MethodType methodType = MethodType.Normal;
            foreach (HarmonyPatch attribute in patchType.GetCustomAttributes<HarmonyPatch>())
            {
                HarmonyMethod info = attribute.info;
                if (info.declaringType != null) declaringType = info.declaringType;
                if (!string.IsNullOrEmpty(info.methodName)) methodName = info.methodName;
                if (info.argumentTypes != null) argumentTypes = info.argumentTypes;
                if (info.methodType.HasValue) methodType = info.methodType.Value;
            }

            TestAssert.Equal(MethodType.Normal, methodType,
                patchType.FullName + " uses an unsupported target kind.");
            TestAssert.NotNull(declaringType, patchType.FullName + " lacks a declaring type.");
            TestAssert.True(!string.IsNullOrEmpty(methodName),
                patchType.FullName + " lacks a method name.");
            MethodInfo[] candidates = declaringType.GetMethods(AllMethods)
                .Where(method => string.Equals(method.Name, methodName, StringComparison.Ordinal))
                .Where(method => argumentTypes == null || method.GetParameters()
                    .Select(parameter => parameter.ParameterType)
                    .SequenceEqual(argumentTypes))
                .ToArray();
            TestAssert.Equal(1, candidates.Length,
                patchType.FullName + " target is missing or ambiguous.");
            return candidates[0];
        }

        private static MethodInfo InstalledMethod(Type owner, string name, params Type[] parameters)
        {
            return TestAssert.NotNull(
                owner.GetMethod(name, AllMethods, null, parameters, null),
                owner.FullName + "." + name + " does not match the installed signature.");
        }

        private static void SeedAdapterTargetProperties()
        {
            SetAdapterTarget("UpdatePlacementMethod",
                InstalledMethod(typeof(Player), "UpdatePlacement", typeof(bool), typeof(float)));
            SetAdapterTarget("UpdatePlacementGhostMethod",
                InstalledMethod(typeof(Player), "UpdatePlacementGhost", typeof(bool)));
            SetAdapterTarget("UpdateCameraMethod",
                InstalledMethod(typeof(GameCamera), "UpdateCamera", typeof(float)));
            SetAdapterTarget("SetControlsMethod",
                InstalledMethod(
                    typeof(Player), "SetControls", typeof(Vector3),
                    typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool),
                    typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool),
                    typeof(bool)));
        }

        private static void ClearAdapterTargetProperties()
        {
            foreach (string property in new[]
                     {
                         "UpdatePlacementMethod", "UpdatePlacementGhostMethod",
                         "UpdateCameraMethod", "SetControlsMethod"
                     })
                SetAdapterTarget(property, null);
        }

        private static void SetAdapterTarget(string property, MethodInfo value)
        {
            FieldInfo backing = typeof(ValheimAdapter).GetField(
                "<" + property + ">k__BackingField",
                BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.NotNull(backing, "Adapter target backing field is missing: " + property)
                .SetValue(null, value);
        }

        private static void AssertMethod(
            MethodInfo method,
            Type owner,
            string name,
            Type returnType,
            params Type[] parameters)
        {
            TestAssert.NotNull(method, owner.FullName + "." + name + " did not resolve.");
            TestAssert.Equal(owner, method.DeclaringType);
            TestAssert.Equal(name, method.Name);
            TestAssert.Equal(returnType, method.ReturnType);
            TestAssert.SequenceEqual(
                parameters,
                method.GetParameters().Select(parameter => parameter.ParameterType));
        }
    }
}

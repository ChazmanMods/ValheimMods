using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RunicBuildCamera.Integration;
using UnityEngine;

namespace RunicBuildCamera.Tests
{
    internal static class InstalledValheimContractTests
    {
        private const BindingFlags InstanceMethods = BindingFlags.Public | BindingFlags.NonPublic |
                                                     BindingFlags.Instance;

        internal static void Register()
        {
            TestRunner.Run("tests target installed Valheim 1.0.15", InstalledVersionIsExact);
            TestRunner.Run("placement ray derives from GameCamera transform", PlacementRayUsesGameCamera);
            TestRunner.Run("placement commit refreshes the camera-derived ghost", PlacementCommitRefreshesGhost);
            TestRunner.Run("repair hover ray derives from GameCamera transform", RepairRayUsesGameCamera);
            TestRunner.Run("remove ray derives directly from GameCamera transform", RemoveRayUsesGameCamera);
            TestRunner.Run("remote actions retain installed range and ward checks", RemoteActionsRetainChecks);
            TestRunner.Run("vanilla actions remain intact with a postfix-only ray validator", VanillaActionMethodsRemainUnpatched);
        }

        private static void InstalledVersionIsExact()
        {
            Type versionType = typeof(Player).Assembly.GetType("Version", throwOnError: true);
            MethodInfo getVersion = versionType.GetMethod(
                "GetVersionString",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(bool) },
                null);
            TestAssert.NotNull(getVersion, "Installed Valheim version API is missing.");
            TestAssert.Equal("1.0.15", (string)getVersion.Invoke(null, new object[] { false }));
        }

        private static void PlacementRayUsesGameCamera()
        {
            MethodInfo ray = GetPlayerMethod(
                "PieceRayTest",
                typeof(Vector3).MakeByRefType(),
                typeof(Vector3).MakeByRefType(),
                typeof(Piece).MakeByRefType(),
                typeof(Heightmap).MakeByRefType(),
                typeof(Collider).MakeByRefType(),
                typeof(bool));
            AssertCameraRay(ray, "m_placeRayMask");
            TestAssert.True(IlReader.AccessesField(ray, typeof(Player), "m_maxPlaceDistance"));

            MethodInfo ghost = GetPlayerMethod("UpdatePlacementGhost", typeof(bool));
            TestAssert.True(IlReader.Calls(ghost, typeof(Player), "PieceRayTest"),
                "Placement ghost no longer originates at PieceRayTest.");
        }

        private static void PlacementCommitRefreshesGhost()
        {
            MethodInfo tryPlace = GetPlayerMethod("TryPlacePiece", typeof(Piece));
            IReadOnlyList<MethodBase> calls = IlReader.Calls(tryPlace);
            int ghost = IlReader.CallIndex(calls, typeof(Player), "UpdatePlacementGhost");
            int place = IlReader.CallIndex(calls, typeof(Player), "PlacePiece");
            TestAssert.True(ghost >= 0 && place > ghost,
                "Placement commit must refresh and validate the camera-derived ghost first.");
            TestAssert.True(IlReader.AccessesField(tryPlace, typeof(Player), "m_placementGhost"));

            MethodInfo update = GetPlayerMethod(
                "UpdatePlacement", typeof(bool), typeof(float));
            TestAssert.True(IlReader.Calls(update, typeof(Player), "TryPlacePiece"));
        }

        private static void RepairRayUsesGameCamera()
        {
            MethodInfo updateHover = GetPlayerMethod("UpdateWearNTearHover");
            AssertCameraRay(updateHover, "m_removeRayMask");
            TestAssert.True(IlReader.AccessesField(
                updateHover, typeof(Player), "m_hoveringPiece"));
            TestAssert.True(IlReader.AccessesField(
                updateHover, typeof(Player), "m_maxPlaceDistance"));

            MethodInfo update = GetPlayerMethod(
                "UpdatePlacement", typeof(bool), typeof(float));
            IReadOnlyList<MethodBase> updateCalls = IlReader.Calls(update);
            int hover = IlReader.CallIndex(updateCalls, typeof(Player), "UpdateWearNTearHover");
            int repair = IlReader.CallIndex(updateCalls, typeof(Player), "Repair");
            TestAssert.True(hover >= 0 && repair > hover,
                "Repair must consume the hover target refreshed from GameCamera.");

            MethodInfo repairMethod = GetPlayerMethod(
                "Repair", typeof(ItemDrop.ItemData), typeof(Piece));
            TestAssert.True(IlReader.Calls(
                repairMethod, typeof(Player), "GetHoveringPiece"));
        }

        private static void RemoveRayUsesGameCamera()
        {
            MethodInfo remove = GetPlayerMethod("RemovePiece");
            AssertCameraRay(remove, "m_removeRayMask");
            TestAssert.True(IlReader.AccessesField(remove, typeof(Player), "m_maxPlaceDistance"));
            TestAssert.True(IlReader.Calls(remove, typeof(WearNTear), nameof(WearNTear.Remove)));

            MethodInfo update = GetPlayerMethod(
                "UpdatePlacement", typeof(bool), typeof(float));
            TestAssert.True(IlReader.Calls(update, typeof(Player), "RemovePiece"));
        }

        private static void RemoteActionsRetainChecks()
        {
            MethodInfo placementGhost = GetPlayerMethod("UpdatePlacementGhost", typeof(bool));
            MethodInfo repair = GetPlayerMethod(
                "Repair", typeof(ItemDrop.ItemData), typeof(Piece));
            MethodInfo remove = GetPlayerMethod("RemovePiece");
            TestAssert.True(IlReader.Calls(
                placementGhost, typeof(PrivateArea), nameof(PrivateArea.CheckAccess)),
                "Placement no longer retains vanilla ward access checks.");
            TestAssert.True(IlReader.Calls(
                repair, typeof(PrivateArea), nameof(PrivateArea.CheckAccess)),
                "Repair no longer retains vanilla ward access checks.");
            TestAssert.True(IlReader.Calls(
                remove, typeof(PrivateArea), nameof(PrivateArea.CheckAccess)),
                "Removal no longer retains vanilla ward access checks.");
            TestAssert.True(IlReader.AccessesField(
                remove, typeof(Player), "m_maxPlaceDistance"));
            TestAssert.True(IlReader.AccessesField(
                GetPlayerMethod("PieceRayTest",
                    typeof(Vector3).MakeByRefType(),
                    typeof(Vector3).MakeByRefType(),
                    typeof(Piece).MakeByRefType(),
                    typeof(Heightmap).MakeByRefType(),
                    typeof(Collider).MakeByRefType(),
                    typeof(bool)),
                typeof(Player),
                "m_maxPlaceDistance"));
        }

        private static void VanillaActionMethodsRemainUnpatched()
        {
            string[] unpatchedActionMethods =
            {
                "UpdateWearNTearHover", "TryPlacePiece", "PlacePiece",
                "RemovePiece", "Repair"
            };
            Type[] patchTypes = typeof(Plugin).Assembly.GetTypes()
                .Where(type => type.GetCustomAttributes(
                    typeof(HarmonyLib.HarmonyPatch), false).Length != 0)
                .ToArray();
            foreach (Type patchType in patchTypes)
            {
                foreach (HarmonyLib.HarmonyPatch attribute in
                         patchType.GetCustomAttributes<HarmonyLib.HarmonyPatch>())
                {
                    TestAssert.False(
                        attribute.info.declaringType == typeof(Player) &&
                        unpatchedActionMethods.Contains(attribute.info.methodName, StringComparer.Ordinal),
                        patchType.Name + " replaces a vanilla build action boundary.");
                }
            }

            Type boundedRay = typeof(PlayerPieceRayRemoteLimitPatch);
            TestAssert.NotNull(boundedRay.GetMethod(
                "Postfix", BindingFlags.Static | BindingFlags.NonPublic),
                "PieceRayTest lacks the bounded-result postfix.");
            TestAssert.True(boundedRay.GetMethod(
                "Prefix", BindingFlags.Static | BindingFlags.NonPublic) == null);
            TestAssert.True(boundedRay.GetMethod(
                "Transpiler", BindingFlags.Static | BindingFlags.NonPublic) == null);
        }

        private static void AssertCameraRay(MethodInfo method, string maskField)
        {
            IReadOnlyList<MethodBase> calls = IlReader.Calls(method);
            int firstCamera = IlReader.CallIndex(calls, typeof(GameCamera), "get_instance");
            int firstPosition = IlReader.CallIndex(calls, typeof(Transform), "get_position");
            int secondCamera = IlReader.CallIndex(
                calls, typeof(GameCamera), "get_instance", firstCamera + 1);
            int forward = IlReader.CallIndex(calls, typeof(Transform), "get_forward");
            int raycast = IlReader.CallIndex(calls, typeof(Physics), nameof(Physics.Raycast));
            TestAssert.True(
                firstCamera >= 0 && firstPosition > firstCamera &&
                secondCamera > firstPosition && forward > secondCamera && raycast > forward,
                method.Name + " no longer builds its ray from GameCamera position and forward.");
            TestAssert.True(IlReader.AccessesField(method, typeof(Player), maskField),
                method.Name + " no longer uses its exact installed build ray mask.");
        }

        private static MethodInfo GetPlayerMethod(string name, params Type[] parameters)
        {
            MethodInfo method = typeof(Player).GetMethod(
                name, InstanceMethods, null, parameters, null);
            return TestAssert.NotNull(
                method,
                "Installed Player." + name + "(" +
                string.Join(",", parameters.Select(type => type.Name)) + ") is missing.");
        }
    }
}

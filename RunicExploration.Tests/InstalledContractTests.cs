using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RunicExploration.Integration;
using UnityEngine;

namespace RunicExploration.Tests
{
    internal static class InstalledContractTests
    {
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic |
                                                 BindingFlags.Instance | BindingFlags.Static;

        internal static void Register()
        {
            TestRunner.Run("tests target installed Valheim 1.0.7", ExactInstalledVersion);
            TestRunner.Run("startup verifier accepts exact installed signatures", VerifierAccepts);
            TestRunner.Run("Minimap pin and coverage fields are exact", MinimapFieldsAreExact);
            TestRunner.Run("Minimap world-to-pixel signature and formula are exact", WorldToPixelIsExact);
            TestRunner.Run("Minimap explored formula is exact", ExploredFormulaIsExact);
            TestRunner.Run("map centering is UI-only in installed game", ShowPointIsViewOnly);
            TestRunner.Run("player ship wind and gamepad signatures are exact", LocalReadoutsAreExact);
            TestRunner.Run("known-cell math matches installed map semantics", KnownCellMath);
        }

        private static void ExactInstalledVersion()
        {
            Type version = typeof(Player).Assembly.GetType("Version", true);
            MethodInfo method = version.GetMethod("GetVersionString",
                BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(bool) }, null);
            TestAssert.NotNull(method);
            TestAssert.Equal("1.0.7", (string)method.Invoke(null, new object[] { false }));
        }

        private static void VerifierAccepts() => ValheimContracts.VerifyInstalledSignatures();

        private static void MinimapFieldsAreExact()
        {
            ExactField(typeof(Minimap), "m_pins", typeof(List<Minimap.PinData>));
            ExactField(typeof(Minimap), "m_explored", typeof(BitArray));
            ExactField(typeof(Minimap), "m_exploredOthers", typeof(BitArray));
            ExactField(typeof(Minimap), "m_textureSize", typeof(int));
            ExactField(typeof(Minimap), "m_pixelSize", typeof(float));
            ExactPublicField(typeof(Minimap.PinData), nameof(Minimap.PinData.m_name), typeof(string));
            ExactPublicField(typeof(Minimap.PinData), nameof(Minimap.PinData.m_type),
                typeof(Minimap.PinType));
            ExactPublicField(typeof(Minimap.PinData), nameof(Minimap.PinData.m_pos), typeof(Vector3));
            ExactPublicField(typeof(Minimap.PinData), nameof(Minimap.PinData.m_save), typeof(bool));
            ExactPublicField(typeof(Minimap.PinData), nameof(Minimap.PinData.m_ownerID), typeof(long));
            ExactPublicField(typeof(Minimap.PinData), nameof(Minimap.PinData.m_checked), typeof(bool));
            TestAssert.Equal(4, (int)Minimap.PinType.Death);
            TestAssert.Equal(5, (int)Minimap.PinType.Bed);
            TestAssert.Equal(9, (int)Minimap.PinType.Boss);
        }

        private static void WorldToPixelIsExact()
        {
            MethodInfo method = Exact(typeof(Minimap), "WorldToPixel", typeof(void), false,
                typeof(Vector3), typeof(int).MakeByRefType(), typeof(int).MakeByRefType());
            TestAssert.True(IlReader.AccessesField(method, typeof(Minimap), "m_textureSize"));
            TestAssert.True(IlReader.AccessesField(method, typeof(Minimap), "m_pixelSize"));
            TestAssert.True(IlReader.Calls(method, typeof(Utils), nameof(Utils.RoundToInt)));
        }

        private static void ExploredFormulaIsExact()
        {
            MethodInfo method = Exact(typeof(Minimap), "IsExplored", typeof(bool), false,
                typeof(Vector3));
            TestAssert.True(IlReader.Calls(method, typeof(Minimap), "WorldToPixel"));
            TestAssert.True(IlReader.AccessesField(method, typeof(Minimap), "m_explored"));
            TestAssert.True(IlReader.AccessesField(method, typeof(Minimap), "m_exploredOthers"));
            TestAssert.True(IlReader.AccessesField(method, typeof(Minimap), "m_textureSize"));
        }

        private static void ShowPointIsViewOnly()
        {
            MethodInfo method = Exact(typeof(Minimap), nameof(Minimap.ShowPointOnMap),
                typeof(void), false, typeof(Vector3));
            string[] mutators =
            {
                "AddPin", "RemovePin", "RemovePinByName", "SetMapData", "AddSharedMapData",
                "Explore", "ExploreAll", "ClearPins"
            };
            foreach (string mutator in mutators)
                TestAssert.False(IlReader.Calls(method, typeof(Minimap), mutator),
                    "Installed ShowPointOnMap unexpectedly calls " + mutator + ".");
            TestAssert.True(IlReader.Calls(method, typeof(Minimap), "SetMapMode"));
            TestAssert.True(IlReader.AccessesField(method, typeof(Minimap), "m_mapOffset"));
        }

        private static void LocalReadoutsAreExact()
        {
            Exact(typeof(Minimap), nameof(Minimap.IsOpen), typeof(bool), true);
            Exact(typeof(Minimap), "InTextInput", typeof(bool), true);
            Exact(typeof(Minimap), "OnMapLeftDown", typeof(void), false, typeof(UIInputHandler));
            Exact(typeof(Minimap), "OnMapLeftUp", typeof(void), false, typeof(UIInputHandler));
            Exact(typeof(Minimap), "OnMapLeftClick", typeof(void), false);
            Exact(typeof(Minimap), "OnMapDblClick", typeof(void), false);
            Exact(typeof(Minimap), "OnMapMiddleClick", typeof(void), false,
                typeof(UIInputHandler));
            Exact(typeof(Minimap), "RemovePinUnderPointer", typeof(void), false);
            Exact(typeof(Player), nameof(Player.GetCurrentBiome), typeof(Heightmap.Biome), false);
            Exact(typeof(Player), nameof(Player.GetControlledShip), typeof(Ship), false);
            Exact(typeof(Player), nameof(Player.GetPlayerID), typeof(long), false);
            Exact(typeof(Ship), nameof(Ship.GetWindAngleFactor), typeof(float), false);
            Exact(typeof(Ship), nameof(Ship.IsSailUp), typeof(bool), false);
            Exact(typeof(Ship), nameof(Ship.GetSpeedSetting), typeof(Ship.Speed), false);
            Exact(typeof(EnvMan), nameof(EnvMan.GetWindIntensity), typeof(float), false);
            Exact(typeof(ZInput), nameof(ZInput.IsGamepadActive), typeof(bool), true);
            FieldInfo local = typeof(Player).GetField(nameof(Player.m_localPlayer),
                BindingFlags.Public | BindingFlags.Static);
            TestAssert.NotNull(local);
            TestAssert.Equal(typeof(Player), local.FieldType);
            ExactProperty(typeof(Minimap), nameof(Minimap.instance), typeof(Minimap), true);
            ExactProperty(typeof(EnvMan), nameof(EnvMan.instance), typeof(EnvMan), true);
        }

        private static void KnownCellMath()
        {
            var local = new BitArray(16);
            var shared = new BitArray(16);
            local[2 * 4 + 2] = true;
            shared[1 * 4 + 3] = true;
            var view = new MinimapReadView(null, local, shared, 4, 10f);
            TestAssert.True(KnownPinSource.IsKnown(view, new Vector3(0, 0, 0), false));
            TestAssert.False(KnownPinSource.IsKnown(view, new Vector3(10, 0, -10), false));
            TestAssert.True(KnownPinSource.IsKnown(view, new Vector3(10, 0, -10), true));
            TestAssert.False(KnownPinSource.IsKnown(view, new Vector3(500, 0, 0), true));
            TestAssert.False(KnownPinSource.IsKnown(view,
                new Vector3(float.NaN, 0, 0), true));
        }

        private static FieldInfo ExactField(Type type, string name, Type fieldType)
        {
            FieldInfo field = type.GetField(name, All);
            TestAssert.NotNull(field);
            TestAssert.Equal(fieldType, field.FieldType);
            return field;
        }

        private static FieldInfo ExactPublicField(Type type, string name, Type fieldType)
        {
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public);
            TestAssert.NotNull(field);
            TestAssert.Equal(fieldType, field.FieldType);
            return field;
        }

        private static MethodInfo Exact(Type type, string name, Type result, bool isStatic,
            params Type[] parameters)
        {
            MethodInfo method = type.GetMethod(name, All, null, parameters, null);
            TestAssert.NotNull(method, "Missing " + type.FullName + "." + name + ".");
            TestAssert.Equal(result, method.ReturnType);
            TestAssert.Equal(isStatic, method.IsStatic);
            return method;
        }

        private static PropertyInfo ExactProperty(Type type, string name, Type result,
            bool isStatic)
        {
            PropertyInfo property = type.GetProperty(name, All);
            TestAssert.NotNull(property);
            TestAssert.Equal(result, property.PropertyType);
            TestAssert.NotNull(property.GetMethod);
            TestAssert.Equal(isStatic, property.GetMethod.IsStatic);
            TestAssert.Equal(0, property.GetIndexParameters().Length);
            return property;
        }
    }
}

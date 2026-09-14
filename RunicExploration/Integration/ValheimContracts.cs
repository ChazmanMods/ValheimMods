using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace RunicExploration.Integration
{
    internal readonly struct MinimapReadView
    {
        internal MinimapReadView(
            List<Minimap.PinData> pins,
            BitArray explored,
            BitArray exploredOthers,
            int textureSize,
            float pixelSize)
        {
            Pins = pins;
            Explored = explored;
            ExploredOthers = exploredOthers;
            TextureSize = textureSize;
            PixelSize = pixelSize;
        }

        internal List<Minimap.PinData> Pins { get; }
        internal BitArray Explored { get; }
        internal BitArray ExploredOthers { get; }
        internal int TextureSize { get; }
        internal float PixelSize { get; }
    }

    internal static class ValheimContracts
    {
        private const BindingFlags InstanceFields =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static FieldInfo _pins;
        private static FieldInfo _explored;
        private static FieldInfo _exploredOthers;
        private static FieldInfo _textureSize;
        private static FieldInfo _pixelSize;
        private static bool _verified;

        internal static void VerifyInstalledSignatures()
        {
            if (_verified) return;
            RequireMethod(typeof(Minimap), nameof(Minimap.IsOpen), typeof(bool), true);
            RequireMethod(typeof(Minimap), nameof(Minimap.ShowPointOnMap), typeof(void), false,
                typeof(Vector3));
            RequireMethod(typeof(Minimap), "WorldToPixel", typeof(void), false,
                typeof(Vector3), typeof(int).MakeByRefType(), typeof(int).MakeByRefType());
            RequireMethod(typeof(Minimap), "IsExplored", typeof(bool), false, typeof(Vector3));
            RequireMethod(typeof(Minimap), "InTextInput", typeof(bool), true);
            RequireMethod(typeof(Minimap), "OnMapLeftDown", typeof(void), false,
                typeof(UIInputHandler));
            RequireMethod(typeof(Minimap), "OnMapLeftUp", typeof(void), false,
                typeof(UIInputHandler));
            RequireMethod(typeof(Minimap), "OnMapLeftClick", typeof(void), false);
            RequireMethod(typeof(Minimap), "OnMapDblClick", typeof(void), false);
            RequireMethod(typeof(Minimap), "OnMapMiddleClick", typeof(void), false,
                typeof(UIInputHandler));
            RequireMethod(typeof(Minimap), "RemovePinUnderPointer", typeof(void), false);
            RequireMethod(typeof(ZInput), nameof(ZInput.IsGamepadActive), typeof(bool), true);
            RequireMethod(typeof(Player), nameof(Player.GetCurrentBiome), typeof(Heightmap.Biome), false);
            RequireMethod(typeof(Player), nameof(Player.GetControlledShip), typeof(Ship), false);
            RequireMethod(typeof(Player), nameof(Player.GetPlayerID), typeof(long), false);
            RequireMethod(typeof(Ship), nameof(Ship.GetWindAngleFactor), typeof(float), false);
            RequireMethod(typeof(Ship), nameof(Ship.IsSailUp), typeof(bool), false);
            RequireMethod(typeof(Ship), nameof(Ship.GetSpeedSetting), typeof(Ship.Speed), false);
            RequireMethod(typeof(EnvMan), nameof(EnvMan.GetWindIntensity), typeof(float), false);
            RequireField(typeof(Player), nameof(Player.m_localPlayer), typeof(Player),
                BindingFlags.Static | BindingFlags.Public);
            RequireProperty(typeof(Minimap), nameof(Minimap.instance), typeof(Minimap), true);
            RequireProperty(typeof(EnvMan), nameof(EnvMan.instance), typeof(EnvMan), true);
            RequireField(typeof(Game), nameof(Game.m_noMap), typeof(bool),
                BindingFlags.Static | BindingFlags.Public);
            _pins = RequireField(
                typeof(Minimap), "m_pins", typeof(List<Minimap.PinData>), InstanceFields);
            _explored = RequireField(typeof(Minimap), "m_explored", typeof(BitArray), InstanceFields);
            _exploredOthers = RequireField(
                typeof(Minimap), "m_exploredOthers", typeof(BitArray), InstanceFields);
            _textureSize = RequireField(typeof(Minimap), "m_textureSize", typeof(int), InstanceFields);
            _pixelSize = RequireField(typeof(Minimap), "m_pixelSize", typeof(float), InstanceFields);
            RequirePublicPinField(nameof(Minimap.PinData.m_name), typeof(string));
            RequirePublicPinField(nameof(Minimap.PinData.m_type), typeof(Minimap.PinType));
            RequirePublicPinField(nameof(Minimap.PinData.m_pos), typeof(Vector3));
            RequirePublicPinField(nameof(Minimap.PinData.m_save), typeof(bool));
            RequirePublicPinField(nameof(Minimap.PinData.m_ownerID), typeof(long));
            RequirePublicPinField(nameof(Minimap.PinData.m_checked), typeof(bool));
            if ((int)Minimap.PinType.Death != 4 || (int)Minimap.PinType.Bed != 5 ||
                (int)Minimap.PinType.Boss != 9)
                throw new InvalidOperationException("Installed Minimap pin categories changed.");
            _verified = true;
        }

        internal static bool TryRead(Minimap minimap, out MinimapReadView view)
        {
            view = default;
            if (!_verified || minimap == null) return false;
            try
            {
                var pins = _pins.GetValue(minimap) as List<Minimap.PinData>;
                var explored = _explored.GetValue(minimap) as BitArray;
                var exploredOthers = _exploredOthers.GetValue(minimap) as BitArray;
                if (pins == null || explored == null) return false;
                view = new MinimapReadView(
                    pins,
                    explored,
                    exploredOthers,
                    (int)_textureSize.GetValue(minimap),
                    (float)_pixelSize.GetValue(minimap));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void RequirePublicPinField(string name, Type type)
        {
            FieldInfo field = typeof(Minimap.PinData).GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public);
            if (field == null || field.FieldType != type)
                throw new MissingFieldException(typeof(Minimap.PinData).FullName, name);
        }

        private static MethodInfo RequireMethod(
            Type type,
            string name,
            Type returnType,
            bool isStatic,
            params Type[] parameters)
        {
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic |
                                     BindingFlags.Instance | BindingFlags.Static;
            MethodInfo method = type.GetMethod(name, all, null, parameters ?? Type.EmptyTypes, null);
            if (method == null || method.ReturnType != returnType || method.IsStatic != isStatic)
                throw new MissingMethodException(type.FullName, name);
            return method;
        }

        private static FieldInfo RequireField(
            Type type,
            string name,
            Type fieldType,
            BindingFlags flags)
        {
            FieldInfo field = type.GetField(name, flags);
            if (field == null || field.FieldType != fieldType)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static PropertyInfo RequireProperty(
            Type type,
            string name,
            Type propertyType,
            bool isStatic)
        {
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic |
                                     BindingFlags.Instance | BindingFlags.Static;
            PropertyInfo property = type.GetProperty(name, all);
            MethodInfo getter = property?.GetMethod;
            if (property == null || property.PropertyType != propertyType || getter == null ||
                getter.IsStatic != isStatic || property.GetIndexParameters().Length != 0)
                throw new MissingMemberException(type.FullName, name);
            return property;
        }
    }
}

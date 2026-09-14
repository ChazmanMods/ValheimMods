using System;
using System.Reflection;
using BepInEx.Bootstrap;
using RunicSafety.Api;

namespace RunicSafety.Services
{
    internal static class InventoryProtectionAdapter
    {
        private const string PluginGuid = "chazman.RunicInventory";
        private const string ApiTypeName = "RunicInventory.Api.InventoryIntegrationApi";
        private static MethodInfo _method;
        private static MethodInfo _useMethod;
        private static object[] _arguments;

        internal static ItemLockState Resolve(object nativeItem, out bool integrationPresent)
            => Resolve(nativeItem, ProtectionDestination.UnsupportedDestructivePath, out integrationPresent);

        internal static ItemLockState Resolve(object nativeItem, ProtectionDestination destination, out bool integrationPresent)
        {
            integrationPresent = Chainloader.PluginInfos.ContainsKey(PluginGuid);
            if (!integrationPresent) return ItemLockState.NotApplicable;
            if (nativeItem == null || !TryResolveMethod()) return ItemLockState.Unknown;
            try
            {
                _arguments[0] = nativeItem;
                _arguments[1] = 0;
                MethodInfo method = AllowsNativeUse(destination) && _useMethod != null ? _useMethod : _method;
                bool governed = method.Invoke(null, _arguments) is bool result && result;
                int state = _arguments[1] is int value ? value : 0;
                if (!governed) return ItemLockState.NotApplicable;
                if (state == 1) return ItemLockState.Unlocked;
                return state == 2 ? ItemLockState.Locked : ItemLockState.Unknown;
            }
            catch
            {
                _method = null;
                _useMethod = null;
                _arguments = null;
                return ItemLockState.Unknown;
            }
            finally
            {
                if (_arguments != null)
                {
                    _arguments[0] = null;
                    _arguments[1] = 0;
                }
            }
        }

        private static bool TryResolveMethod()
        {
            // A missing plugin instance during startup is not a permanent missing API.
            if (_method != null) return true;
            if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out var information)) return false;
            Type api = information?.Instance?.GetType().Assembly.GetType(ApiTypeName, false);
            _method = ResolveMethod(api);
            _useMethod = ResolveUseMethod(api);
            if (_method == null) return false;
            _arguments = new object[] { null, 0 };
            return true;
        }

        internal static MethodInfo ResolveMethod(Type api)
            => ResolveNamedMethod(api, "TryGetProtection");

        internal static MethodInfo ResolveUseMethod(Type api)
            => ResolveNamedMethod(api, "TryGetUseProtection");

        internal static bool AllowsNativeUse(ProtectionDestination destination) =>
            destination == ProtectionDestination.SmelterInput ||
            destination == ProtectionDestination.SmelterFuel ||
            destination == ProtectionDestination.CookingStation ||
            destination == ProtectionDestination.CookingFuel ||
            destination == ProtectionDestination.Fermenter;

        private static MethodInfo ResolveNamedMethod(Type api, string name)
        {
            MethodInfo method = api?.GetMethod(
                name,
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(object), typeof(int).MakeByRefType() },
                null);
            return method?.ReturnType == typeof(bool) ? method : null;
        }
    }
}

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
        private static object[] _arguments;
        private static bool _resolved;

        internal static ItemLockState Resolve(object nativeItem, out bool integrationPresent)
        {
            integrationPresent = Chainloader.PluginInfos.ContainsKey(PluginGuid);
            if (!integrationPresent) return ItemLockState.NotApplicable;
            if (nativeItem == null || !TryResolveMethod()) return ItemLockState.Unknown;
            try
            {
                _arguments[0] = nativeItem;
                _arguments[1] = 0;
                bool governed = _method.Invoke(null, _arguments) is bool result && result;
                int state = _arguments[1] is int value ? value : 0;
                if (!governed) return ItemLockState.NotApplicable;
                if (state == 1) return ItemLockState.Unlocked;
                return state == 2 ? ItemLockState.Locked : ItemLockState.Unknown;
            }
            catch
            {
                _resolved = false;
                _method = null;
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
            if (_resolved) return _method != null;
            _resolved = true;
            if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out var information)) return false;
            Type api = information?.Instance?.GetType().Assembly.GetType(ApiTypeName, false);
            _method = api?.GetMethod(
                "TryGetProtection",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(object), typeof(int).MakeByRefType() },
                null);
            if (_method == null || _method.ReturnType != typeof(bool)) return false;
            _arguments = new object[] { null, 0 };
            return true;
        }
    }
}

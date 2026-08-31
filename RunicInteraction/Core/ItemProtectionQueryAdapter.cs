using System;
using System.Reflection;
using BepInEx.Bootstrap;

namespace RunicInteraction.Core
{
    internal enum TransferItemProtection
    {
        NotApplicable = 0,
        Unlocked = 1,
        Locked = 2,
        Unknown = 3
    }

    internal static class ItemProtectionQueryAdapter
    {
        private const string PluginGuid = "chazman.RunicInventory";
        private const string ApiTypeName = "RunicInventory.Api.InventoryIntegrationApi";
        private static MethodInfo _method;
        private static object[] _arguments;
        private static bool _resolved;

        internal static TransferItemProtection Resolve(object nativeItem)
        {
            if (!Chainloader.PluginInfos.ContainsKey(PluginGuid))
                return TransferItemProtection.NotApplicable;
            if (nativeItem == null || !TryResolveMethod())
                return TransferItemProtection.Unknown;
            try
            {
                _arguments[0] = nativeItem;
                _arguments[1] = 0;
                bool applies = _method.Invoke(null, _arguments) is bool result && result;
                int state = _arguments[1] is int value ? value : 0;
                return !applies
                    ? TransferItemProtection.NotApplicable
                    : state == 1
                        ? TransferItemProtection.Unlocked
                        : state == 2
                            ? TransferItemProtection.Locked
                            : TransferItemProtection.Unknown;
            }
            catch
            {
                _resolved = false;
                _method = null;
                _arguments = null;
                return TransferItemProtection.Unknown;
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

        internal static bool AllowsTransfer(TransferItemProtection protection) =>
            protection == TransferItemProtection.NotApplicable ||
            protection == TransferItemProtection.Unlocked;

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

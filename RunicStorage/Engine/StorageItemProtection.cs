using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace RunicStorage.Engine
{
    internal enum StorageProtectionState : byte
    {
        NotApplicable = 0,
        Unlocked = 1,
        Locked = 2
    }

    internal readonly struct StorageProtectionSnapshot
    {
        private readonly StorageProtectionState[] _states;

        internal StorageProtectionSnapshot(StorageProtectionState[] states) => _states = states;
        internal int Count => _states?.Length ?? 0;
        internal StorageProtectionState StateAt(int index) =>
            _states != null && index >= 0 && index < _states.Length
                ? _states[index]
                : StorageProtectionState.NotApplicable;
    }

    internal static class StorageItemProtection
    {
        internal const int MaximumCarriedStacks = 128;

        internal static bool TryCapture<T>(
            IReadOnlyList<T> items,
            out StorageProtectionSnapshot snapshot,
            out string failureCode)
            where T : class
        {
            snapshot = default;
            if (items == null || items.Count > MaximumCarriedStacks)
            {
                failureCode = items == null
                    ? "protection.items-unavailable"
                    : "protection.stack-bound-exceeded";
                return false;
            }

            Type api = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(
                    "RunicInventory.Api.InventoryIntegrationApi", throwOnError: false))
                .FirstOrDefault(type => type != null);
            if (api == null)
            {
                snapshot = new StorageProtectionSnapshot(
                    new StorageProtectionState[items.Count]);
                failureCode = "ok:inventory-absent";
                return true;
            }

            MethodInfo method = api.GetMethod(
                "TryGetProtection",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(object), typeof(int).MakeByRefType() },
                modifiers: null);
            if (method == null)
            {
                failureCode = "protection.optional-api-malformed";
                return false;
            }

            var states = new StorageProtectionState[items.Count];
            try
            {
                for (int index = 0; index < items.Count; index++)
                {
                    if (items[index] == null)
                    {
                        failureCode = "protection.item-null";
                        return false;
                    }
                    object[] arguments = { items[index], 0 };
                    bool applicable = method.Invoke(null, arguments) is bool result && result;
                    if (!applicable)
                    {
                        states[index] = StorageProtectionState.NotApplicable;
                        continue;
                    }
                    int state = arguments[1] is int value ? value : 0;
                    if (state == 1) states[index] = StorageProtectionState.Unlocked;
                    else if (state == 2) states[index] = StorageProtectionState.Locked;
                    else
                    {
                        failureCode = "protection.in-domain-unknown";
                        return false;
                    }
                }
            }
            catch
            {
                failureCode = "protection.provider-exception";
                return false;
            }

            snapshot = new StorageProtectionSnapshot(states);
            failureCode = "ok";
            return true;
        }
    }
}

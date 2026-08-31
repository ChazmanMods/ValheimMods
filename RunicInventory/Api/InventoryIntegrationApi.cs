using System;
using RunicInventory.Integration;

namespace RunicInventory.Api
{
    public enum ItemProtectionState
    {
        Unknown = 0,
        Unlocked = 1,
        Locked = 2
    }

    internal interface IItemProtectionQuery
    {
        bool TryGetProtection(object nativeItem, out ItemProtectionState state);
    }

    /// <summary>
    /// Optional reflection seam for independently installed mods. Callers must not retain the
    /// supplied native item. State is 0 unknown, 1 unlocked, or 2 locked.
    /// </summary>
    public static class InventoryIntegrationApi
    {
        private static readonly object Gate = new object();
        private static InventoryRuntime _runtime;

        public static bool TryGetProtection(object nativeItem, out int state)
        {
            state = (int)ItemProtectionState.Unknown;
            InventoryRuntime runtime;
            lock (Gate) runtime = _runtime;
            if (!(nativeItem is ItemDrop.ItemData)) return false;
            if (runtime == null) return true;
            try
            {
                bool governed = runtime.TryGetProtection(
                    nativeItem, out ItemProtectionState protection);
                state = (int)protection;
                return governed;
            }
            catch (Exception)
            {
                state = (int)ItemProtectionState.Unknown;
                return true;
            }
        }

        internal static void Attach(InventoryRuntime runtime)
        {
            lock (Gate) _runtime = runtime;
        }

        internal static void Detach(InventoryRuntime runtime)
        {
            lock (Gate)
                if (ReferenceEquals(_runtime, runtime)) _runtime = null;
        }
    }
}

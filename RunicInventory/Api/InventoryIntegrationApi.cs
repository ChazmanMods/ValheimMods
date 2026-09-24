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
        bool TryGetProtection(object nativeItem, out ItemProtectionState state, bool quickStack = false);
    }

    /// <summary>
    /// Optional reflection seam for independently installed mods. Callers must not retain the
    /// supplied native item. State is 0 unknown, 1 unlocked, or 2 locked. False means the item is
    /// outside Inventory's active protection domain, including stable vanilla-fallback modes.
    /// </summary>
    public static class InventoryIntegrationApi
    {
        private static readonly object Gate = new object();
        private static IItemProtectionQuery _runtime;
        private static bool _hasActivated;
        private static bool _startupFailed;

        /// <summary>
        /// Native gameplay-use view (cooking/refueling/processing), not a storage-transfer
        /// permission. Retains the exact domain and Unknown behavior of TryGetProtection.
        /// Quest, equipped, rare-item and destination rules remain the caller's responsibility.
        /// </summary>
        public static bool TryGetUseProtection(object nativeItem, out int state)
        {
            bool governed = TryGetProtection(nativeItem, out state);
            if (governed && state == (int)ItemProtectionState.Locked)
                state = (int)ItemProtectionState.Unlocked;
            return governed;
        }

        public static bool TryGetProtection(object nativeItem, out int state)
            => TryQuery(nativeItem, out state, quickStack: false);

        /// <summary>Includes player slot exclusions specifically for Runic Quick Stack.</summary>
        public static bool TryGetQuickStackProtection(object nativeItem, out int state)
            => TryQuery(nativeItem, out state, quickStack: true);

        private static bool TryQuery(object nativeItem, out int state, bool quickStack)
        {
            state = (int)ItemProtectionState.Unknown;
            IItemProtectionQuery runtime;
            bool startupFailed;
            lock (Gate) { runtime = _runtime; startupFailed = _startupFailed; }
            if (!(nativeItem is ItemDrop.ItemData)) return false;
            // A plugin which never activated cannot enforce any locks. Do not advertise an
            // unavailable protection service forever after a failed compatibility check.
            if (runtime == null) return !startupFailed;
            try
            {
                bool governed = runtime.TryGetProtection(
                    nativeItem, out ItemProtectionState protection, quickStack);
                state = (int)protection;
                return governed;
            }
            catch (Exception)
            {
                state = (int)ItemProtectionState.Unknown;
                return true;
            }
        }

        internal static void BeginInitialization()
        {
            lock (Gate)
                if (!_hasActivated) _startupFailed = false;
        }

        internal static void MarkStartupFailed()
        {
            lock (Gate)
                if (!_hasActivated && _runtime == null) _startupFailed = true;
        }

        internal static void Attach(IItemProtectionQuery runtime)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            lock (Gate) { _runtime = runtime; _hasActivated = true; _startupFailed = false; }
        }

        internal static void Detach(IItemProtectionQuery runtime)
        {
            lock (Gate)
                if (ReferenceEquals(_runtime, runtime)) _runtime = null;
        }
    }
}

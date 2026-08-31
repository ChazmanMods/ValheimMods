using System;

namespace RunicInventory.Core
{
    /// <summary>
    /// Pure, allocation-free-after-resolution policy for Inventory's controller routes.  The
    /// legacy mapping is exact: a player-authored custom set is never rewritten or
    /// silently substituted.
    /// </summary>
    internal static class ControllerBindingPolicy
    {
        internal const string ModifierAction = "JoyAltKeys";
        internal const string LegacyQuick1Action = "JoyLBumper";
        internal const string Quick1Action = "JoyMap";
        internal const string Quick2Action = "JoyButtonY";
        internal const string Quick3Action = "JoyRBumper";
        internal const string SortAction = "JoyButtonA";
        internal const string ToggleLockAction = "JoyButtonB";

        internal const int RouteCount = 5;
        internal const int AllRoutesMask = (1 << RouteCount) - 1;

        internal static string EffectiveQuick1Action(
            string modifier,
            string quick1,
            string quick2,
            string quick3,
            string sort,
            string toggleLock,
            out bool legacyMapped)
        {
            legacyMapped =
                SameAction(modifier, ModifierAction) &&
                SameAction(quick1, LegacyQuick1Action) &&
                SameAction(quick2, Quick2Action) &&
                SameAction(quick3, Quick3Action) &&
                SameAction(sort, SortAction) &&
                SameAction(toggleLock, ToggleLockAction);
            return legacyMapped ? Quick1Action : quick1;
        }

        /// <summary>
        /// Returns a bit for each independently safe primary.  A malformed modifier denies every
        /// route.  A primary that aliases the modifier is denied alone; two primaries that alias
        /// one another are both denied, leaving unrelated routes available.
        /// </summary>
        internal static int ValidRouteMask(
            string modifierAction,
            string modifierPath,
            string[] primaryActions,
            string[] primaryPaths)
        {
            if (primaryActions == null || primaryPaths == null ||
                primaryActions.Length != RouteCount || primaryPaths.Length != RouteCount ||
                string.IsNullOrEmpty(modifierAction) || string.IsNullOrWhiteSpace(modifierPath))
            {
                return 0;
            }

            int valid = AllRoutesMask;
            for (int index = 0; index < RouteCount; index++)
            {
                if (string.IsNullOrEmpty(primaryActions[index]) ||
                    string.IsNullOrWhiteSpace(primaryPaths[index]) ||
                    SameAction(primaryActions[index], modifierAction) ||
                    SamePath(primaryPaths[index], modifierPath))
                {
                    valid &= ~(1 << index);
                }
            }

            for (int left = 0; left < RouteCount; left++)
            {
                if ((valid & (1 << left)) == 0) continue;
                for (int right = left + 1; right < RouteCount; right++)
                {
                    if ((valid & (1 << right)) == 0 ||
                        !(SameAction(primaryActions[left], primaryActions[right]) ||
                          SamePath(primaryPaths[left], primaryPaths[right])))
                    {
                        continue;
                    }
                    valid &= ~(1 << left);
                    valid &= ~(1 << right);
                }
            }
            return valid;
        }

        internal static bool RouteIsValid(int mask, int route) =>
            route >= 0 && route < RouteCount && (mask & (1 << route)) != 0;

        private static bool SameAction(string left, string right) =>
            string.Equals(left, right, StringComparison.Ordinal);

        private static bool SamePath(string left, string right) =>
            string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}

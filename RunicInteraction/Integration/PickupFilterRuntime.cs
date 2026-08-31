using RunicInteraction.Core;
using UnityEngine;

namespace RunicInteraction.Integration
{
    internal static class PickupFilterRuntime
    {
        private static PickupFilterSet _filters = PickupFilterSet.Parse(string.Empty);
        private static string _controllerModifierAction =
            InteractionInputBindings.DefaultPickupBypassControllerModifier;

        internal static void Refresh()
        {
            _filters = PickupFilterSet.Parse(InteractionConfig.FilteredPickupItems.Value);
            _controllerModifierAction = InteractionInputBindings.ResolveControllerModifier(
                InteractionConfig.PickupBypassControllerModifier.Value,
                out bool usedDefault);
            if (_filters.Truncated)
                Diagnostics.Warn("Pickup filter input exceeded 128 unique rules; extra rules were ignored.");
            if (usedDefault)
                Diagnostics.Warn(
                    "Pickup bypass controller modifier was outside the bounded installed-action set; " +
                    InteractionInputBindings.DefaultPickupBypassControllerModifier + " remains active.");
            Diagnostics.Trace("Pickup filter compiled " + _filters.Count + " exact rules.");
        }

        internal static bool Allow(Humanoid actor, GameObject worldObject)
        {
            if (!FeatureOn() || !actor || actor != Player.m_localPlayer || !worldObject) return true;
            if (BypassHeld()) return true;
            ItemDrop drop = worldObject.GetComponent<ItemDrop>();
            if (!drop || drop.m_itemData == null) return true;
            if (drop.m_itemData.m_shared.m_questItem && !InteractionConfig.FilterQuestItems.Value) return true;
            string prefab = drop.m_itemData.m_dropPrefab ? drop.m_itemData.m_dropPrefab.name : worldObject.name;
            if (!_filters.Matches(prefab, drop.m_itemData.m_shared.m_name)) return true;
            // Prefix exits before Load, ownership request, inventory mutation, or ZNetScene.Destroy.
            return false;
        }

        internal static void Shutdown() => _filters = PickupFilterSet.Parse(string.Empty);

        private static bool BypassHeld()
        {
            return ZInput.GetKey(KeyCode.LeftAlt) || ZInput.GetKey(KeyCode.RightAlt) ||
                   ZInput.GetButton(_controllerModifierAction);
        }

        private static bool FeatureOn() =>
            InteractionConfig.Enabled.Value && InteractionConfig.PickupFilters.Value && _filters.Count > 0;
    }
}

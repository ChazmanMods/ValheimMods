using System;
using System.Collections;
using RunicInteraction.Core;
using UnityEngine;

namespace RunicInteraction.Integration
{
    internal static class MenuMemoryRuntime
    {
        private const int MaximumContexts = 64;
        private static readonly BoundedContextMemory<string, MenuSelection> Memory =
            new BoundedContextMemory<string, MenuSelection>(MaximumContexts, StringComparer.Ordinal);
        private static string _activeContext;
        private static string _pendingContext;
        private static MenuSelection _pendingSelection;
        private static bool _hasPending;

        internal static void BeginSetup(InventoryGui gui)
        {
            if (!FeatureOn()) return;
            Capture(gui, _activeContext);
            _pendingContext = CurrentContext();
            _hasPending = Memory.TryGet(_pendingContext, out _pendingSelection);
        }

        internal static void CompleteSetup(InventoryGui gui)
        {
            if (!FeatureOn()) return;
            _activeContext = _pendingContext ?? CurrentContext();
            if (_hasPending) Restore(gui, _pendingSelection);
            _pendingContext = null;
            _hasPending = false;
        }

        internal static void BeforeContextChange(InventoryGui gui) =>
            Capture(gui, _activeContext ?? CurrentContext());

        internal static void AfterContextChange(InventoryGui gui)
        {
            if (!FeatureOn()) return;
            string context = CurrentContext();
            _activeContext = context;
            if (Memory.TryGet(context, out MenuSelection selection)) Restore(gui, selection);
        }

        internal static void BeforeHide(InventoryGui gui)
        {
            if (FeatureOn()) Capture(gui, _activeContext ?? CurrentContext());
        }

        internal static void OnConfigurationChanged()
        {
            if (FeatureOn()) return;
            Memory.Clear();
            _activeContext = null;
            _pendingContext = null;
            _hasPending = false;
        }

        internal static void Shutdown()
        {
            Memory.Clear();
            _activeContext = null;
            _pendingContext = null;
            _hasPending = false;
        }

        private static void Capture(InventoryGui gui, string context)
        {
            if (gui == null || string.IsNullOrEmpty(context)) return;
            object pair = ValheimAccess.GetSelectedRecipePair(gui);
            Recipe recipe = ValheimAccess.GetPairRecipe(pair);
            ItemDrop.ItemData item = ValheimAccess.GetPairItem(pair);
            string recipeName = recipe ? recipe.name : string.Empty;
            Memory.Put(context, new MenuSelection(recipeName, item != null, gui.ActiveGroup));
        }

        private static void Restore(InventoryGui gui, MenuSelection selection)
        {
            if (gui == null) return;
            if (selection.ActiveGroup >= 0)
                ValheimAccess.SetActiveGroup(gui, selection.ActiveGroup, sound: false);
            if (!selection.HasRecipe) return;
            IList pairs = ValheimAccess.GetAvailableRecipePairs(gui);
            if (pairs == null) return;
            for (int index = 0; index < pairs.Count; index++)
            {
                object pair = pairs[index];
                Recipe recipe = ValheimAccess.GetPairRecipe(pair);
                bool upgrade = ValheimAccess.GetPairItem(pair) != null;
                if (!recipe || upgrade != selection.Upgrade ||
                    !string.Equals(recipe.name, selection.RecipePrefab, StringComparison.Ordinal))
                    continue;
                ValheimAccess.SetRecipe(gui, index, center: false);
                return;
            }
        }

        private static string CurrentContext()
        {
            Player player = Player.m_localPlayer;
            CraftingStation station = player ? player.GetCurrentCraftingStation() : null;
            string stationName = station ? Utils.GetPrefabName(station.gameObject) : "handcraft";
            InventoryGui gui = InventoryGui.instance;
            string mode = gui != null && gui.InUpradeTab() ? "upgrade" : "craft";
            return stationName + ":" + mode;
        }

        private static bool FeatureOn() =>
            InteractionConfig.Enabled.Value && InteractionConfig.MenuMemory.Value;
    }
}

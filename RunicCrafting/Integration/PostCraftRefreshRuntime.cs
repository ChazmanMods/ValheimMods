using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RunicCrafting.Integration
{
    // Run after the material lease and the native craft timer have both unwound.
    internal static class PostCraftRefreshRuntime
    {
        private static readonly MethodInfo UpdatePanel = AccessTools.Method(
            typeof(InventoryGui), "UpdateCraftingPanel", new[] { typeof(bool) });
        private static Player _player;
        private static CraftingStation _station;
        private static InventoryGui _gui;
        private static ZNet _network;
        private static int _dueFrame;
        internal static bool Refreshing { get; private set; }

        internal static void Schedule(Player player)
        {
            if (Refreshing || player == null) return;
            _player = player;
            _station = player.GetCurrentCraftingStation();
            _gui = InventoryGui.instance;
            _network = ZNet.instance;
            _dueFrame = Time.frameCount + 2;
        }

        internal static void Tick()
        {
            if (ReferenceEquals(_player, null) || Refreshing) return;
            if (_player == null || _gui == null || !Configuration.Enabled.Value ||
                !ReferenceEquals(_player, Player.m_localPlayer) ||
                !ReferenceEquals(_gui, InventoryGui.instance) ||
                !ReferenceEquals(_network, ZNet.instance) ||
                !ReferenceEquals(_station, _player.GetCurrentCraftingStation()) ||
                !InventoryGui.IsVisible())
            {
                Reset();
                return;
            }
            if (Time.frameCount < _dueFrame || PreviewRefreshRuntime.InAction ||
                CraftingRuntime.HasMaterialOperation) return;

            InventoryGui gui = _gui;
            Reset(); // Coalesce requests and consume before invoking any UI callbacks.
            PreviewRefreshRuntime.Invalidate();
            UiPreviewCache.Invalidate();
            long scope = PreviewRefreshRuntime.Begin();
            Refreshing = true;
            try
            {
                // Native panel refresh preserves selected recipe/upgrade and rebuilds sorting.
                // Do not call UpdateRecipe: it advances the crafting timer.
                if (UpdatePanel == null) throw new MissingMethodException("InventoryGui.UpdateCraftingPanel");
                UpdatePanel.Invoke(gui, new object[] { false });
            }
            catch (Exception exception)
            {
                Plugin.Log?.LogWarning("Could not refresh crafting recipes: " +
                    (exception.InnerException ?? exception).Message);
            }
            finally
            {
                Refreshing = false;
                PreviewRefreshRuntime.End(scope);
            }
        }

        internal static void Reset()
        {
            _player = null; _station = null; _gui = null; _network = null;
            _dueFrame = 0;
        }
    }
}

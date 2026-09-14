using System;
using System.Reflection;
using HarmonyLib;
using RunicInventory.Core;
using UnityEngine;

namespace RunicInventory.Integration
{
    internal static class CompactQuiverPanel
    {
        private static readonly RectTransform[] Cells = new RectTransform[8];
        private static FieldInfo _baseHeight, _rowHeight;
        private static RectTransform _panel;
        private static float _originalY, _appliedY, _originalHeight, _appliedHeight;
        private static bool _warned;

        internal static void Apply(InventoryGrid grid, Func<int, int, Component> element, bool equipped)
        {
            var inventory = grid.GetInventory();
            var gui = InventoryGui.instance;
            if (inventory == null || gui == null || gui.m_player == null) { Restore(); return; }
            var top = element(0, 0)?.transform as RectTransform;
            if (top == null) { Restore(); return; }
            int roleRow = inventory.GetHeight() - 1;
            float? lowestQuiverY = null;
            if (equipped)
            {
                for (int x = 0; x < 3; x++)
                {
                    var arrow = element(x, roleRow - 1)?.transform as RectTransform;
                    if (arrow == null || !arrow.gameObject.activeSelf || arrow.parent != top.parent)
                    { Restore(); return; }
                    lowestQuiverY = Math.Min(lowestQuiverY ?? arrow.anchoredPosition.y, arrow.anchoredPosition.y);
                }
            }
            if (!QuiverPanelLayout.TryPlan(roleRow - 2, grid.m_elementSpace, top.anchoredPosition.y,
                    lowestQuiverY, InventoryConfig.CompactQuiverLayout.Value, out float y, out float removed))
            { Restore(); return; }
            _baseHeight = _baseHeight ?? AccessTools.Field(typeof(InventoryGui), "m_playerHeight");
            _rowHeight = _rowHeight ?? AccessTools.Field(typeof(InventoryGui), "m_invGridHeight");
            float fullHeight = (float)_baseHeight.GetValue(gui) + (inventory.GetHeight() - 4) * (float)_rowHeight.GetValue(gui);
            if (fullHeight <= removed || float.IsNaN(fullHeight) || float.IsInfinity(fullHeight)) { Restore(); return; }
            // Validate all cells before making any visual change. Button identities/indexes stay intact.
            for (int x = 0; x < 8; x++)
            {
                var cell = element(x, roleRow)?.transform as RectTransform;
                if (cell == null || cell.parent != top.parent) { Restore(); return; }
            }
            _originalY = top.anchoredPosition.y - roleRow * grid.m_elementSpace;
            _appliedY = y;
            for (int x = 0; x < 8; x++)
            {
                Cells[x] = element(x, roleRow).transform as RectTransform;
                Vector2 position = Cells[x].anchoredPosition;
                position.y = y;
                if (Cells[x].anchoredPosition != position) Cells[x].anchoredPosition = position;
            }
            _panel = gui.m_player;
            _originalHeight = fullHeight;
            _appliedHeight = fullHeight - removed;
            Vector2 size = _panel.sizeDelta;
            size.y = _appliedHeight;
            if (_panel.sizeDelta != size) _panel.sizeDelta = size;
        }

        internal static void Fault(Exception exception)
        {
            Restore();
            if (_warned) return;
            _warned = true;
            Diagnostics.Error(exception, "Compact quiver panel unavailable; ordinary display retained. Inventory data was not changed.");
        }

        internal static void Restore()
        {
            foreach (var cell in Cells)
                if (cell != null && Mathf.Approximately(cell.anchoredPosition.y, _appliedY))
                {
                    Vector2 position = cell.anchoredPosition;
                    position.y = _originalY;
                    cell.anchoredPosition = position;
                }
            if (_panel != null && Mathf.Approximately(_panel.sizeDelta.y, _appliedHeight))
            {
                Vector2 size = _panel.sizeDelta;
                size.y = _originalHeight;
                _panel.sizeDelta = size;
            }
            Array.Clear(Cells, 0, Cells.Length);
            _panel = null;
        }
    }
}

using System;
using System.Linq;
using Mono.Cecil;
using RunicInventory.Core;

namespace RunicInventory.Tests
{
    internal static class QuiverPanelLayoutTests
    {
        internal static void Register()
        {
            TestRunner.Run("compact quiver display removes spacer while keeping label clearance", Equipped);
            TestRunner.Run("compact quiver display collapses both hidden rows without equipped quiver", Unequipped);
            TestRunner.Run("compact display respects custom quiver offsets and toggle-off", Offsets);
            TestRunner.Run("compact display is stable across native pocket sizes and UI scaling", Sizes);
            TestRunner.Run("compact display rejects invalid geometry", Invalid);
            TestRunner.Run("compact renderer cannot change inventory dimensions or item positions", VisualOnly);
            TestRunner.Run("native inventory click mapping uses element identity rather than visual row", ClickMapping);
        }
        private static void Equipped()
        {
            TestAssert.True(QuiverPanelLayout.TryPlan(4, 70f, 0f, -280f, true, out float y, out float removed));
            TestAssert.Equal(-362f, y);
            TestAssert.Equal(58f, removed);
        }
        private static void Unequipped()
        {
            TestAssert.True(QuiverPanelLayout.TryPlan(4, 70f, 0f, null, true, out float y, out float removed));
            TestAssert.Equal(-292f, y);
            TestAssert.Equal(128f, removed);
        }
        private static void Offsets()
        {
            TestAssert.True(QuiverPanelLayout.TryPlan(4, 70f, 0f, -310f, true, out float y, out float removed));
            TestAssert.Equal(-392f, y);
            TestAssert.Equal(28f, removed);
            TestAssert.True(QuiverPanelLayout.TryPlan(4, 70f, 0f, -450f, true, out y, out removed));
            TestAssert.Equal(-420f, y); // No compaction when the custom position leaves no removable gap.
            TestAssert.Equal(0f, removed);
            TestAssert.True(QuiverPanelLayout.TryPlan(4, 70f, 0f, -280f, false, out y, out removed));
            TestAssert.Equal(-420f, y);
            TestAssert.Equal(0f, removed);
        }
        private static void Sizes()
        {
            for (int rows = 4; rows <= 9; rows++)
                foreach (float pitch in new[] { 40f, 70f, 96f, 140f })
                {
                    float arrow = 15f - rows * pitch;
                    TestAssert.True(QuiverPanelLayout.TryPlan(rows, pitch, 15f, arrow, true, out float y, out float removed));
                    TestAssert.True(y < arrow - pitch);
                    TestAssert.True(removed > 0f && removed < pitch);
                    TestAssert.True(QuiverPanelLayout.TryPlan(rows, pitch, 15f, arrow, true, out float second, out float secondRemoved));
                    TestAssert.Equal(y, second);
                    TestAssert.Equal(removed, secondRemoved);
                }
        }
        private static void Invalid()
        {
            foreach (float pitch in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, 4096f })
                TestAssert.False(QuiverPanelLayout.TryPlan(4, pitch, 0f, null, true, out _, out _));
            TestAssert.False(QuiverPanelLayout.TryPlan(10, 70f, 0f, null, true, out _, out _));
            TestAssert.False(QuiverPanelLayout.TryPlan(4, 70f, 0f, float.NaN, true, out _, out _));
        }
        private static void VisualOnly()
        {
            using var assembly = AssemblyDefinition.ReadAssembly(typeof(QuiverPanelLayout).Assembly.Location);
            foreach (string name in new[] { "RunicInventory.Core.QuiverPanelLayout", "RunicInventory.Integration.CompactQuiverPanel" })
                foreach (var method in assembly.MainModule.GetType(name).Methods.Where(m => m.HasBody))
                    foreach (var instruction in method.Body.Instructions)
                    {
                        if (instruction.Operand is MethodReference call && call.DeclaringType.FullName == "Inventory")
                            TestAssert.True(new[] { "GetHeight", "GetWidth" }.Contains(call.Name), call.FullName);
                        if (instruction.Operand is FieldReference field)
                            TestAssert.False(field.Name == "m_gridPos" || field.Name == "m_inventory" || field.Name == "m_height");
                    }
        }
        private static void ClickMapping()
        {
            using var assembly = AssemblyDefinition.ReadAssembly(typeof(InventoryGrid).Assembly.Location);
            var method = assembly.MainModule.GetType("InventoryGrid").Methods.Single(m => m.Name == "GetButtonPos");
            TestAssert.True(method.Body.Instructions.Any(i => i.Operand is FieldReference field && field.Name == "m_elements"));
            TestAssert.True(method.Body.Instructions.Any(i => i.Operand is MethodReference call && call.Name == "GetPositionFromIndex"));
            TestAssert.False(method.Body.Instructions.Any(i => i.Operand is MethodReference call && call.Name == "get_anchoredPosition"));
        }
    }
}

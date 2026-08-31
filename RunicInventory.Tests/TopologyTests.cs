using System;
using System.Collections.Generic;
using RunicInventory.Api;
using RunicInventory.Core;

namespace RunicInventory.Tests
{
    internal static class TopologyTests
    {
        internal static void Register()
        {
            TestRunner.Run("topology accepts exact native 8x4 layout", ExactNativeLayout);
            TestRunner.Run("topology rejects every non-eight width", RejectsOtherWidths);
            TestRunner.Run("topology rejects short and unbounded heights", RejectsUnsafeHeights);
            TestRunner.Run("all eight roles are unique and occupy the bottom row", RolesAreUniqueBottomRow);
            TestRunner.Run("a proven topology rejects every runtime dimension drift", RuntimeDimensionDriftFails);
            TestRunner.Run("equipment roles accept only exact installed categories", EquipmentCategoriesAreExact);
            TestRunner.Run("every known equipped category maps to one canonical role", EquipmentCategoryMapIsExact);
            TestRunner.Run("quick roles accept consumables tools and utility only", QuickCategoriesAreExact);
            TestRunner.Run("modded or unknown categories fall back out of special roles", UnknownCategoriesFallBack);
            TestRunner.Run("selected row policy always rejects hotbar and special row", UnsafeRowsRejected);
            TestRunner.Run("empty selected rows expand only to proven general rows", EmptyRowsExpandSafely);
            TestRunner.Run("oversized selected-row config fails before splitting", OversizedRowsFailClosed);
        }

        private static void ExactNativeLayout()
        {
            TestAssert.True(TopologyLayout.TryCreate(8, 4, out TopologyLayout layout, out string code));
            TestAssert.Equal("ok", code);
            TestAssert.Equal(3, layout.SpecialRow);
            TestAssert.Equal(32, layout.TotalSlots);
        }

        private static void RejectsOtherWidths()
        {
            foreach (int width in new[] { 1, 7, 9, 16 })
            {
                TestAssert.False(TopologyLayout.TryCreate(width, 4, out _, out string code));
                TestAssert.Equal("topology.width-not-eight", code);
            }
        }

        private static void RejectsUnsafeHeights()
        {
            TestAssert.False(TopologyLayout.TryCreate(8, 3, out _, out string shortCode));
            TestAssert.Equal("topology.height-too-small", shortCode);
            TestAssert.True(TopologyLayout.TryCreate(8, 16, out _, out _));
            TestAssert.False(TopologyLayout.TryCreate(8, 17, out _, out string largeCode));
            TestAssert.Equal("topology.slot-bound-exceeded", largeCode);
        }

        private static void RolesAreUniqueBottomRow()
        {
            foreach (int height in new[] { 4, 7, 16 })
            {
                TestAssert.True(TopologyLayout.TryCreate(8, height, out TopologyLayout layout, out _));
                var coordinates = new HashSet<InventorySlotCoordinate>();
                foreach (InventoryRoleKind role in Enum.GetValues(typeof(InventoryRoleKind)))
                {
                    InventorySlotCoordinate coordinate = layout.Coordinate(role);
                    TestAssert.True(coordinates.Add(coordinate));
                    TestAssert.Equal((int)role - 1, coordinate.X);
                    TestAssert.Equal(height - 1, coordinate.Y);
                    TestAssert.True(layout.TryRoleAt(coordinate.X, coordinate.Y, out InventoryRoleKind roundTrip));
                    TestAssert.Equal(role, roundTrip);
                }
                TestAssert.Equal(8, coordinates.Count);
            }
        }

        private static void RuntimeDimensionDriftFails()
        {
            TestAssert.True(TopologyLayout.TryCreate(8, 4, out TopologyLayout layout, out _));
            TestAssert.True(layout.MatchesNativeDimensions(8, 4));
            TestAssert.False(layout.MatchesNativeDimensions(8, 5));
            TestAssert.False(layout.MatchesNativeDimensions(7, 4));
            TestAssert.False(layout.MatchesNativeDimensions(9, 4));
            TestAssert.False(layout.MatchesNativeDimensions(8, 3));
            TestAssert.False(layout.MatchesNativeDimensions(8, 17));
        }

        private static void EquipmentCategoriesAreExact()
        {
            TestAssert.True(TopologyLayout.Accepts(InventoryRoleKind.Head, InventoryItemCategory.Helmet));
            TestAssert.True(TopologyLayout.Accepts(InventoryRoleKind.Chest, InventoryItemCategory.Chest));
            TestAssert.True(TopologyLayout.Accepts(InventoryRoleKind.Legs, InventoryItemCategory.Legs));
            TestAssert.True(TopologyLayout.Accepts(InventoryRoleKind.Cape, InventoryItemCategory.Cape));
            TestAssert.True(TopologyLayout.Accepts(InventoryRoleKind.Utility, InventoryItemCategory.Utility));
            TestAssert.False(TopologyLayout.Accepts(InventoryRoleKind.Head, InventoryItemCategory.Chest));
            TestAssert.False(TopologyLayout.Accepts(InventoryRoleKind.Utility, InventoryItemCategory.Tool));
        }

        private static void EquipmentCategoryMapIsExact()
        {
            var expected = new[]
            {
                (InventoryItemCategory.Helmet, InventoryRoleKind.Head),
                (InventoryItemCategory.Chest, InventoryRoleKind.Chest),
                (InventoryItemCategory.Legs, InventoryRoleKind.Legs),
                (InventoryItemCategory.Cape, InventoryRoleKind.Cape),
                (InventoryItemCategory.Utility, InventoryRoleKind.Utility)
            };
            foreach ((InventoryItemCategory category, InventoryRoleKind role) in expected)
            {
                TestAssert.True(TopologyLayout.TryEquipmentRole(category, out InventoryRoleKind actual));
                TestAssert.Equal(role, actual);
            }
            foreach (InventoryItemCategory category in new[]
                     {
                         InventoryItemCategory.Unknown, InventoryItemCategory.Material,
                         InventoryItemCategory.Consumable, InventoryItemCategory.Tool, InventoryItemCategory.Other
                     })
                TestAssert.False(TopologyLayout.TryEquipmentRole(category, out _));
        }

        private static void QuickCategoriesAreExact()
        {
            foreach (InventoryRoleKind role in new[] { InventoryRoleKind.Quick1, InventoryRoleKind.Quick2, InventoryRoleKind.Quick3 })
            {
                TestAssert.True(TopologyLayout.Accepts(role, InventoryItemCategory.Consumable));
                TestAssert.True(TopologyLayout.Accepts(role, InventoryItemCategory.Tool));
                TestAssert.True(TopologyLayout.Accepts(role, InventoryItemCategory.Utility));
                TestAssert.False(TopologyLayout.Accepts(role, InventoryItemCategory.Material));
                TestAssert.False(TopologyLayout.Accepts(role, InventoryItemCategory.Other));
            }
        }

        private static void UnknownCategoriesFallBack()
        {
            foreach (InventoryRoleKind role in Enum.GetValues(typeof(InventoryRoleKind)))
            {
                TestAssert.False(TopologyLayout.Accepts(role, InventoryItemCategory.Unknown));
                TestAssert.False(TopologyLayout.Accepts(role, InventoryItemCategory.Other));
            }
        }

        private static void UnsafeRowsRejected()
        {
            TopologyLayout.TryCreate(8, 4, out TopologyLayout layout, out _);
            TestAssert.False(SelectedRowPolicy.TryParse("0", layout, out _, out string hotbar));
            TestAssert.Equal("sort.region-unsafe", hotbar);
            TestAssert.False(SelectedRowPolicy.TryParse("3", layout, out _, out string special));
            TestAssert.Equal("sort.region-unsafe", special);
        }

        private static void EmptyRowsExpandSafely()
        {
            TopologyLayout.TryCreate(8, 7, out TopologyLayout layout, out _);
            TestAssert.True(SelectedRowPolicy.TryParse(string.Empty, layout, out IReadOnlyList<int> rows, out _));
            TestAssert.Equal(5, rows.Count);
            for (int index = 0; index < rows.Count; index++) TestAssert.Equal(index + 1, rows[index]);
        }

        private static void OversizedRowsFailClosed()
        {
            TopologyLayout.TryCreate(8, 4, out TopologyLayout layout, out _);
            TestAssert.False(SelectedRowPolicy.TryParse(new string('1', 1000000), layout, out _, out string code));
            TestAssert.Equal("sort.region-character-bound", code);
        }
    }
}

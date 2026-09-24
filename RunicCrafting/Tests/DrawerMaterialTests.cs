using Runic.Compatibility;
using RunicCrafting.Domain;
using RunicCrafting.Integration;

namespace RunicCrafting.Tests
{
    internal static class DrawerMaterialTests
    {
        internal static void DrawerCraftingConsumptionAndRefund()
        {
            var drawer = DrawerFixture.Create(75);
            var source = new ValheimMaterialSource("drawer", drawer.Inventory,
                MaterialSourceKind.NearbyContainer, 0, new[] { "1" },
                () => ModdedContainerCompatibility.TryRefresh(drawer, out _));
            TestAssert.True(source.TryTake("1", 75, out var refund));
            TestAssert.True(ModdedContainerCompatibility.TryPreview(drawer, out var after));
            TestAssert.Equal(0, after.GetAllItems()[0].m_stack);
            TestAssert.True(refund.Restore());
            TestAssert.True(ModdedContainerCompatibility.TryPreview(drawer, out after));
            TestAssert.Equal(75, after.GetAllItems()[0].m_stack);
            TestAssert.False(source.TryTake("1", 76, out _));
        }

        internal static void DrawerManualStagingUsesNativeStackLimit()
        {
            var drawer = DrawerFixture.Create(500);
            var destination = new Inventory("backpack", null, 2, 1);
            TestAssert.True(ManualItemTransfer.TryMoveOne(drawer.Inventory, destination, "1",
                () => ModdedContainerCompatibility.TryRefresh(drawer, out _), _ => true, out _));
            TestAssert.Equal(1, destination.GetAllItems()[0].m_stack);
            TestAssert.Equal(50, destination.GetAllItems()[0].m_shared.m_maxStackSize);
            TestAssert.True(ModdedContainerCompatibility.TryPreview(drawer, out var after));
            TestAssert.Equal(499, after.GetAllItems()[0].m_stack);
        }
    }
}

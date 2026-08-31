using System;
using System.Collections.Generic;

namespace RunicInventory.Capabilities
{
    public static class InventoryCapabilityIds
    {
        public const string Topology = "inventory.topology";
        public const string ItemLocks = "inventory.item-locks";
        public const string QuickSlots = "inventory.quick-slots";
        public const string PickupPreview = "inventory.pickup-preview";
        public const string Status = "inventory.status";

        private static readonly IReadOnlyList<string> Values = Array.AsReadOnly(new[]
        {
            Topology,
            ItemLocks,
            QuickSlots,
            PickupPreview,
            Status
        });

        public static IReadOnlyList<string> Published => Values;
    }
}

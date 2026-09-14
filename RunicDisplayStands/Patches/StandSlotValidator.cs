namespace RunicDisplayStands
{
    internal static class StandSlotValidator
    {
        public static bool IsValidForItemStand(ItemDrop.ItemData item) => item != null;
        public static bool IsValidForArmorSlot(ItemDrop.ItemData item, int slotIndex) => ArmorStandSlots.Accepts(slotIndex, item);
    }
}

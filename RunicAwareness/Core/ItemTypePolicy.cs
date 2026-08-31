using System;

namespace RunicAwareness.Core
{
    internal enum ItemComparisonDisposition
    {
        HideKnownNonEquipment = 0,
        CompareKnownEquipment = 1,
        ShowRawUnsupported = 2
    }

    internal static class ItemTypePolicy
    {
        internal static ItemComparisonDisposition Classify(string type)
        {
            switch (type)
            {
                case "Helmet":
                case "Chest":
                case "Legs":
                case "Hands":
                case "Shoulder":
                case "Utility":
                case "Trinket":
                case "Ammo":
                case "OneHandedWeapon":
                case "TwoHandedWeapon":
                case "TwoHandedWeaponLeft":
                case "Attach_Atgeir":
                case "Bow":
                case "Shield":
                case "Tool":
                case "Torch":
                    return ItemComparisonDisposition.CompareKnownEquipment;
                case "None":
                case "Material":
                case "Consumable":
                case "Customization":
                case "Trophy":
                case "Misc":
                case "Fish":
                case "AmmoNonEquipable":
                    return ItemComparisonDisposition.HideKnownNonEquipment;
                default:
                    return string.IsNullOrEmpty(type)
                        ? ItemComparisonDisposition.HideKnownNonEquipment
                        : ItemComparisonDisposition.ShowRawUnsupported;
            }
        }
    }
}

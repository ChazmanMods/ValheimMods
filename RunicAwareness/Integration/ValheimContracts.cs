using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RunicAwareness.Integration
{
    internal static class ValheimContracts
    {
        private const BindingFlags InstanceFields =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static FieldInfo _rightItem;
        private static FieldInfo _leftItem;
        private static FieldInfo _helmetItem;
        private static FieldInfo _chestItem;
        private static FieldInfo _legItem;
        private static FieldInfo _shoulderItem;
        private static FieldInfo _utilityItem;
        private static FieldInfo _trinketItem;
        private static FieldInfo _ammoItem;
        private static FieldInfo _comfortPieces;
        private static bool _verified;
        private static Func<Player, bool> _takeInput;

        // Calls the patched game method, so optional mod panels participate too.
        internal static bool GameplayInputBlocked(Player player) =>
            player == null || _takeInput == null || !_takeInput(player);

        internal static FieldInfo ComfortPiecesField =>
            _comfortPieces ?? throw new InvalidOperationException("Valheim contracts are not initialized.");

        internal static void VerifyInstalledSignatures()
        {
            if (_verified) return;
            _takeInput = AccessTools.MethodDelegate<Func<Player, bool>>(
                RequireMethod(typeof(Player), "TakeInput", typeof(bool), false));
            RequireMethod(typeof(Player), nameof(Player.GetFoods), typeof(List<Player.Food>), false);
            RequireMethod(typeof(Player), nameof(Player.GetComfortLevel), typeof(int), false);
            RequireMethod(typeof(Player), nameof(Player.GetHoverObject), typeof(GameObject), false);
            RequireMethod(typeof(Player), nameof(Player.GetHoveringPiece), typeof(Piece), false);
            RequireMethod(typeof(Player), nameof(Player.GetSEMan), typeof(SEMan), false);
            RequireMethod(typeof(Player), nameof(Player.InShelter), typeof(bool), false);
            RequireMethod(typeof(Minimap), nameof(Minimap.IsOpen), typeof(bool), true);
            RequireMethod(typeof(Hud), nameof(Hud.IsPieceSelectionVisible), typeof(bool), true);
            RequireMethod(typeof(Chat), nameof(Chat.HasFocus), typeof(bool), false);
            RequireMethod(
                typeof(Chat), nameof(Chat.IsChatDialogWindowVisible), typeof(bool), false);
            RequireMethod(
                typeof(PlayerCustomizaton),
                nameof(PlayerCustomizaton.IsBarberGuiVisible),
                typeof(bool),
                true);
            RequireMethod(typeof(Feedback), nameof(Feedback.IsVisible), typeof(bool), true);
            RequireMethod(
                typeof(UnifiedPopup), nameof(UnifiedPopup.IsVisible), typeof(bool), true);
            RequireMethod(typeof(ConnectPanel), nameof(ConnectPanel.IsVisible), typeof(bool), true);
            RequireMethod(typeof(TextViewer), nameof(TextViewer.IsVisible), typeof(bool), false);
            RequireMethod(typeof(SEMan), nameof(SEMan.GetStatusEffects), typeof(List<StatusEffect>), false);
            RequireMethod(typeof(SEMan), nameof(SEMan.GetStatusEffect), typeof(StatusEffect), false,
                typeof(int));
            RequireMethod(typeof(StatusEffect), nameof(StatusEffect.GetRemaningTime), typeof(float), false);
            RequireField(
                typeof(Localization),
                "m_translations",
                typeof(Dictionary<string, string>));
            if (!BoundedLocalization.IsSupported)
                throw new MissingFieldException(typeof(Localization).FullName, "m_translations");
            RequireMethod(typeof(InventoryGrid), "CreateItemTooltip", typeof(void), false,
                typeof(ItemDrop.ItemData), typeof(UITooltip));
            RequireMethod(typeof(SE_Rested), nameof(SE_Rested.CalculateComfortLevel), typeof(int), true,
                typeof(Player));
            RequireMethod(typeof(CraftingStation), nameof(CraftingStation.GetHoverText), typeof(string), false);
            RequireMethod(typeof(CookingStation), nameof(CookingStation.GetHoverText), typeof(string), false);
            RequireMethod(typeof(Fermenter), nameof(Fermenter.GetHoverText), typeof(string), false);
            RequireMethod(typeof(Plant), nameof(Plant.GetHoverText), typeof(string), false);
            RequireMethod(typeof(Beehive), nameof(Beehive.GetHoverText), typeof(string), false);
            RequireMethod(typeof(Tameable), nameof(Tameable.GetHoverText), typeof(string), false);
            RequireMethod(typeof(Switch), nameof(Switch.GetHoverText), typeof(string), false);
            RequireMethod(typeof(WearNTear), nameof(WearNTear.GetHealthPercentage), typeof(float), false);
            RequireMethod(typeof(CraftingStation), nameof(CraftingStation.GetLevel), typeof(int), false,
                typeof(bool));
            RequireField(
                typeof(PrivateArea),
                "m_allAreas",
                typeof(List<PrivateArea>),
                BindingFlags.Static | BindingFlags.NonPublic);
            RequireMethod(typeof(PrivateArea), "IsEnabled", typeof(bool), false);
            RequireMethod(typeof(PrivateArea), "HaveLocalAccess", typeof(bool), false);
            RequireMethod(typeof(PrivateArea), "IsInside", typeof(bool), false,
                typeof(Vector3), typeof(float));
            if (!StrictWardDisclosure.IsSupported)
                throw new MissingMethodException(
                    typeof(PrivateArea).FullName,
                    "strict disclosure delegates");

            _rightItem = RequireField(typeof(Humanoid), "m_rightItem", typeof(ItemDrop.ItemData));
            _leftItem = RequireField(typeof(Humanoid), "m_leftItem", typeof(ItemDrop.ItemData));
            _helmetItem = RequireField(typeof(Humanoid), "m_helmetItem", typeof(ItemDrop.ItemData));
            _chestItem = RequireField(typeof(Humanoid), "m_chestItem", typeof(ItemDrop.ItemData));
            _legItem = RequireField(typeof(Humanoid), "m_legItem", typeof(ItemDrop.ItemData));
            _shoulderItem = RequireField(typeof(Humanoid), "m_shoulderItem", typeof(ItemDrop.ItemData));
            _utilityItem = RequireField(typeof(Humanoid), "m_utilityItem", typeof(ItemDrop.ItemData));
            _trinketItem = RequireField(typeof(Humanoid), "m_trinketItem", typeof(ItemDrop.ItemData));
            _ammoItem = RequireField(typeof(Humanoid), "m_ammoItem", typeof(ItemDrop.ItemData));
            _comfortPieces = RequireField(
                typeof(SE_Rested),
                "s_tempPieces",
                typeof(List<Piece>),
                BindingFlags.Static | BindingFlags.NonPublic);
            _verified = true;
        }

        internal static ItemDrop.ItemData EquippedFor(Player player, ItemDrop.ItemData selected)
        {
            if (!_verified || player == null || selected?.m_shared == null) return null;
            string type = selected.m_shared.m_itemType.ToString();
            switch (type)
            {
                case "Helmet": return ReadItem(_helmetItem, player);
                case "Chest": return ReadItem(_chestItem, player);
                case "Legs": return ReadItem(_legItem, player);
                case "Shoulder": return ReadItem(_shoulderItem, player);
                case "Utility": return ReadItem(_utilityItem, player);
                case "Trinket": return ReadItem(_trinketItem, player);
                case "Ammo": return ReadItem(_ammoItem, player);
                case "Shield": return ReadItem(_leftItem, player);
                case "TwoHandedWeaponLeft": return ReadItem(_leftItem, player);
                default:
                    ItemDrop.ItemData right = ReadItem(_rightItem, player);
                    if (right != null && right.m_shared != null &&
                        string.Equals(
                            right.m_shared.m_itemType.ToString(),
                            type,
                            StringComparison.Ordinal))
                        return right;
                    ItemDrop.ItemData left = ReadItem(_leftItem, player);
                    return left != null && left.m_shared != null &&
                           string.Equals(left.m_shared.m_itemType.ToString(), type, StringComparison.Ordinal)
                        ? left
                        : null;
            }
        }

        private static ItemDrop.ItemData ReadItem(FieldInfo field, Humanoid player) =>
            field?.GetValue(player) as ItemDrop.ItemData;

        private static MethodInfo RequireMethod(
            Type type,
            string name,
            Type returnType,
            bool isStatic,
            params Type[] parameters)
        {
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic |
                                     BindingFlags.Instance | BindingFlags.Static;
            MethodInfo method = type.GetMethod(name, all, null, parameters ?? Type.EmptyTypes, null);
            if (method == null || method.ReturnType != returnType || method.IsStatic != isStatic)
                throw new MissingMethodException(type.FullName, name);
            return method;
        }

        private static FieldInfo RequireField(Type type, string name, Type fieldType) =>
            RequireField(type, name, fieldType, InstanceFields);

        private static FieldInfo RequireField(
            Type type,
            string name,
            Type fieldType,
            BindingFlags flags)
        {
            FieldInfo field = type.GetField(name, flags);
            if (field == null || field.FieldType != fieldType)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }
    }
}

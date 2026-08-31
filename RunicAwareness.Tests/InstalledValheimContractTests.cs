using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RunicAwareness.Integration;
using UnityEngine;

namespace RunicAwareness.Tests
{
    internal static class InstalledValheimContractTests
    {
        private const BindingFlags AllMethods = BindingFlags.Public | BindingFlags.NonPublic |
                                                       BindingFlags.Instance | BindingFlags.Static;

        internal static void Register()
        {
            TestRunner.Run("tests target installed Valheim 0.221.12", InstalledVersionIsExact);
            TestRunner.Run("startup verifier accepts exact installed signatures", VerifierAcceptsInstalledGame);
            TestRunner.Run("food and effect readers expose existing local lists", LocalListsAreExact);
            TestRunner.Run("timer and comfort APIs are exact", TimerAndComfortApisAreExact);
            TestRunner.Run("vanilla tooltip builder covers mouse and controller selection", TooltipBuilderCoversBothInputs);
            TestRunner.Run("comfort capture reuses vanilla completed world scan", ComfortCaptureReusesVanillaScan);
            TestRunner.Run("hover context signatures are exact and parameterless", HoverSignaturesAreExact);
            TestRunner.Run("building status readers are exact", BuildingReadersAreExact);
            TestRunner.Run("direct localization dictionary signature is exact", LocalizationLookupIsExact);
            TestRunner.Run("strict ward disclosure signatures are exact", WardDisclosureSignaturesAreExact);
        }

        private static void InstalledVersionIsExact()
        {
            Type versionType = typeof(Player).Assembly.GetType("Version", true);
            MethodInfo getVersion = versionType.GetMethod(
                "GetVersionString",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(bool) },
                null);
            TestAssert.NotNull(getVersion);
            TestAssert.Equal("0.221.12", (string)getVersion.Invoke(null, new object[] { false }));
        }

        private static void VerifierAcceptsInstalledGame() =>
            ValheimContracts.VerifyInstalledSignatures();

        private static void LocalListsAreExact()
        {
            MethodInfo foods = Exact(typeof(Player), nameof(Player.GetFoods),
                typeof(List<Player.Food>), false);
            MethodInfo effects = Exact(typeof(SEMan), nameof(SEMan.GetStatusEffects),
                typeof(List<StatusEffect>), false);
            TestAssert.True(IlReader.AccessesField(foods, typeof(Player), "m_foods"));
            TestAssert.True(IlReader.AccessesField(effects, typeof(SEMan), "m_statusEffects"));
            TestAssert.False(IlReader.HasNewArray(foods));
            TestAssert.False(IlReader.HasNewArray(effects));
        }

        private static void TimerAndComfortApisAreExact()
        {
            Exact(typeof(Player), nameof(Player.GetComfortLevel), typeof(int), false);
            Exact(typeof(Player), nameof(Player.InShelter), typeof(bool), false);
            Exact(typeof(Player), nameof(Player.GetSEMan), typeof(SEMan), false);
            Exact(typeof(SEMan), nameof(SEMan.GetStatusEffect), typeof(StatusEffect), false,
                typeof(int));
            Exact(typeof(StatusEffect), nameof(StatusEffect.GetRemaningTime), typeof(float), false);
            TestAssert.True(typeof(SEMan).GetField(
                nameof(SEMan.s_statusEffectRested),
                BindingFlags.Public | BindingFlags.Static) != null);
        }

        private static void TooltipBuilderCoversBothInputs()
        {
            MethodInfo create = Exact(
                typeof(InventoryGrid), "CreateItemTooltip", typeof(void), false,
                typeof(ItemDrop.ItemData), typeof(UITooltip));
            MethodInfo update = Exact(
                typeof(InventoryGrid), "UpdateGui", typeof(void), false,
                typeof(Player), typeof(ItemDrop.ItemData));
            TestAssert.True(IlReader.Calls(update, typeof(InventoryGrid), create.Name));
            TestAssert.True(IlReader.Calls(update, typeof(InventoryGrid), "GetHoveredElement"));
            TestAssert.True(IlReader.Calls(update, typeof(ZInput), nameof(ZInput.IsGamepadActive)));
            Exact(typeof(InventoryGrid), nameof(InventoryGrid.GetGamepadSelectedItem),
                typeof(ItemDrop.ItemData), false);
        }

        private static void ComfortCaptureReusesVanillaScan()
        {
            MethodInfo playerOverload = Exact(
                typeof(SE_Rested), nameof(SE_Rested.CalculateComfortLevel), typeof(int), true,
                typeof(Player));
            MethodInfo positionOverload = Exact(
                typeof(SE_Rested), nameof(SE_Rested.CalculateComfortLevel), typeof(int), true,
                typeof(bool), typeof(Vector3));
            MethodInfo nearby = Exact(
                typeof(SE_Rested), "GetNearbyComfortPieces", typeof(List<Piece>), true,
                typeof(Vector3));
            TestAssert.True(IlReader.Calls(playerOverload, typeof(SE_Rested),
                nameof(SE_Rested.CalculateComfortLevel)));
            TestAssert.True(IlReader.Calls(positionOverload, typeof(SE_Rested),
                "GetNearbyComfortPieces"));
            TestAssert.True(IlReader.Calls(nearby, typeof(Piece),
                nameof(Piece.GetAllComfortPiecesInRadius)));
            FieldInfo scratch = typeof(SE_Rested).GetField(
                "s_tempPieces", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.NotNull(scratch);
            TestAssert.Equal(typeof(List<Piece>), scratch.FieldType);
        }

        private static void HoverSignaturesAreExact()
        {
            Type[] types =
            {
                typeof(CraftingStation), typeof(CookingStation), typeof(Fermenter), typeof(Plant),
                typeof(Beehive), typeof(Tameable), typeof(Switch)
            };
            foreach (Type type in types)
                Exact(type, "GetHoverText", typeof(string), false);
            Exact(typeof(Player), nameof(Player.GetHoverObject), typeof(GameObject), false);
            Exact(typeof(Player), nameof(Player.GetHoveringPiece), typeof(Piece), false);
            Exact(typeof(Minimap), nameof(Minimap.IsOpen), typeof(bool), true);
            Exact(typeof(Hud), nameof(Hud.IsPieceSelectionVisible), typeof(bool), true);
            Exact(typeof(Chat), nameof(Chat.HasFocus), typeof(bool), false);
            Exact(typeof(Chat), nameof(Chat.IsChatDialogWindowVisible), typeof(bool), false);
            Exact(
                typeof(PlayerCustomizaton),
                nameof(PlayerCustomizaton.IsBarberGuiVisible),
                typeof(bool),
                true);
            Exact(typeof(Feedback), nameof(Feedback.IsVisible), typeof(bool), true);
            Exact(typeof(UnifiedPopup), nameof(UnifiedPopup.IsVisible), typeof(bool), true);
            Exact(typeof(ConnectPanel), nameof(ConnectPanel.IsVisible), typeof(bool), true);
            Exact(typeof(TextViewer), nameof(TextViewer.IsVisible), typeof(bool), false);
            PropertyInfo virtualKeyboard = typeof(ZInput).GetProperty(
                nameof(ZInput.VirtualKeyboardOpen),
                BindingFlags.Public | BindingFlags.Static);
            TestAssert.NotNull(virtualKeyboard);
            TestAssert.Equal(typeof(bool), virtualKeyboard.PropertyType);
        }

        private static void BuildingReadersAreExact()
        {
            Exact(typeof(WearNTear), nameof(WearNTear.GetHealthPercentage), typeof(float), false);
            Exact(typeof(CraftingStation), nameof(CraftingStation.GetLevel), typeof(int), false,
                typeof(bool));
            FieldInfo station = typeof(Piece).GetField(
                nameof(Piece.m_craftingStation), BindingFlags.Instance | BindingFlags.Public);
            TestAssert.NotNull(station);
            TestAssert.Equal(typeof(CraftingStation), station.FieldType);
        }

        private static void LocalizationLookupIsExact()
        {
            FieldInfo translations = typeof(Localization).GetField(
                "m_translations",
                BindingFlags.Instance | BindingFlags.NonPublic);
            TestAssert.NotNull(translations);
            TestAssert.Equal(typeof(Dictionary<string, string>), translations.FieldType);
            TestAssert.True(BoundedLocalization.IsSupported);
        }

        private static void WardDisclosureSignaturesAreExact()
        {
            FieldInfo areas = typeof(PrivateArea).GetField(
                "m_allAreas",
                BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.NotNull(areas);
            TestAssert.Equal(typeof(List<PrivateArea>), areas.FieldType);
            Exact(typeof(PrivateArea), "IsEnabled", typeof(bool), false);
            Exact(typeof(PrivateArea), "HaveLocalAccess", typeof(bool), false);
            Exact(typeof(PrivateArea), "IsInside", typeof(bool), false,
                typeof(Vector3), typeof(float));
            TestAssert.True(StrictWardDisclosure.IsSupported);
        }

        private static MethodInfo Exact(
            Type type,
            string name,
            Type returnType,
            bool isStatic,
            params Type[] parameters)
        {
            MethodInfo method = type.GetMethod(
                name, AllMethods, null, parameters ?? Type.EmptyTypes, null);
            TestAssert.NotNull(method,
                "Missing " + type.FullName + "." + name + "(" +
                string.Join(",", (parameters ?? Type.EmptyTypes).Select(item => item.Name)) + ").");
            TestAssert.Equal(returnType, method.ReturnType);
            TestAssert.Equal(isStatic, method.IsStatic);
            return method;
        }
    }
}

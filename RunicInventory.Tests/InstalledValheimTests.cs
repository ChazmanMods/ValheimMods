using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using UnityEngine;

namespace RunicInventory.Tests
{
    internal static class InstalledValheimTests
    {
        private const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        internal static void Register()
        {
            TestRunner.Run("installed Valheim target is exactly 0.221.12", VersionIsExact);
            TestRunner.Run("installed Valheim assembly hash is the audited binary", AssemblyHashIsExact);
            TestRunner.Run("installed input assembly hash is the audited binary", InputAssemblyHashIsExact);
            TestRunner.Run("native Inventory exposes exact 8x4-safe coordinate APIs", InventorySignaturesAreExact);
            TestRunner.Run("native automatic stacking exposes the exact lock-routing seam", StackRoutingSignatureIsExact);
            TestRunner.Run("native item capacity exposes the exact reserved-row adjustment seam", CapacitySignatureIsExact);
            TestRunner.Run("installed inventory grid exposes distinct mouse-hover and gamepad focus contracts", FocusContractsAreExact);
            TestRunner.Run("Inventory.Save serializes every item metadata field and grid position", SavePreservesMetadata);
            TestRunner.Run("Inventory.Load restores through the installed bounded coordinate add path", LoadUsesBoundedAdd);
            TestRunner.Run("player logout and reload preserve inventory plus Runic custom-data state", PlayerSaveLoadRemainVanilla);
            TestRunner.Run("installed profile save exposes no trustworthy durable acknowledgement", ProfileSaveHasNoDurableAcknowledgement);
            TestRunner.Run("installed profile loader ignores hash and cloud commit acknowledgement", ProfileReadbackNeedsIndependentProof);
            TestRunner.Run("vanilla tombstone creation moves the complete native inventory", TombstoneUsesNativeInventoryMove);
            TestRunner.Run("native grave move has no Runic-only topology dependency", GraveMoveIsVanilla);
            TestRunner.Run("vanilla equipment weight reads equipped item weight", EquipmentWeightRemainsNative);
            TestRunner.Run("vanilla UseItem retains inventory membership validation", UseItemValidatesMembership);
            TestRunner.Run("installed upgrade commit identifies and removes the exact captured item", UpgradeCommitUsesExactItem);
            TestRunner.Run("pickup mutates inventory before destroying its world item", PickupOrderingIsExact);
            TestRunner.Run("installed client drives Player state from network ownership, not server role", AuthoritySignaturesAreExact);
            TestRunner.Run("installed ItemType retains exact armor utility and new-category identities", ItemTypesAreExact);
            TestRunner.Run("installed controller layouts expose the audited Inventory paths", ControllerLayoutsAreExact);
            TestRunner.Run("installed Classic Alternative1 and Alternative2 keep Inventory defaults unique", ControllerDefaultsAreUnique);
        }

        private static void VersionIsExact()
        {
            Type version = typeof(Player).Assembly.GetType("Version", true);
            MethodInfo method = version.GetMethod("GetVersionString", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(bool) }, null);
            TestAssert.NotNull(method);
            TestAssert.Equal("0.221.12", (string)method.Invoke(null, new object[] { false }));
        }

        private static void AssemblyHashIsExact()
        {
            using SHA256 sha = SHA256.Create();
            string hash = Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(TestPaths.InstalledValheim)));
            TestAssert.Equal("3B26C8512778F6E0664B5AF2A26F3C30993A00F584C1E76D9123A742B67E2004", hash);
        }

        private static void InputAssemblyHashIsExact()
        {
            using SHA256 sha = SHA256.Create();
            string hash = Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(TestPaths.InstalledUtils)));
            TestAssert.Equal("41C2D53EC0351E974BB962B88A122C9E915E3FA0B8B1E6ACE59786BAE6405993", hash);
        }

        private static void InventorySignaturesAreExact()
        {
            TestAssert.Equal(typeof(int), Exact(typeof(Inventory), nameof(Inventory.GetWidth)).ReturnType);
            TestAssert.Equal(typeof(int), Exact(typeof(Inventory), nameof(Inventory.GetHeight)).ReturnType);
            TestAssert.Equal(typeof(Vector2i), Exact(typeof(Inventory), "FindEmptySlot", typeof(bool)).ReturnType);
            TestAssert.Equal(typeof(bool), Exact(typeof(InventoryGrid), nameof(InventoryGrid.DropItem),
                typeof(Inventory), typeof(ItemDrop.ItemData), typeof(int), typeof(Vector2i)).ReturnType);
            TestAssert.Equal(typeof(bool), Exact(typeof(Humanoid), nameof(Humanoid.DropItem),
                typeof(Inventory), typeof(ItemDrop.ItemData), typeof(int)).ReturnType);
            TestAssert.Equal(typeof(void), Exact(typeof(Humanoid), nameof(Humanoid.UseItem),
                typeof(Inventory), typeof(ItemDrop.ItemData), typeof(bool)).ReturnType);
        }

        private static void StackRoutingSignatureIsExact()
        {
            MethodInfo method = Exact(typeof(Inventory), "FindFreeStackItem",
                typeof(string), typeof(int), typeof(float));
            TestAssert.Equal(typeof(ItemDrop.ItemData), method.ReturnType);
            TestAssert.True(IlReader.Accesses(method, typeof(Inventory), "m_inventory"));
        }

        private static void CapacitySignatureIsExact()
        {
            MethodInfo capacity = Exact(typeof(Inventory), nameof(Inventory.CanAddItem),
                typeof(ItemDrop.ItemData), typeof(int));
            TestAssert.Equal(typeof(bool), capacity.ReturnType);
            TestAssert.True(IlReader.Calls(capacity, typeof(Inventory), "FindFreeStackSpace"));
            TestAssert.True(IlReader.Accesses(capacity, typeof(Inventory), "m_width"));
            TestAssert.True(IlReader.Accesses(capacity, typeof(Inventory), "m_height"));
        }

        private static void FocusContractsAreExact()
        {
            MethodInfo hover = Exact(typeof(InventoryGrid), "GetHoveredElement");
            TestAssert.True(hover.ReturnType.IsNested && hover.ReturnType.DeclaringType == typeof(InventoryGrid));
            TestAssert.NotNull(hover.ReturnType.GetField("m_go", Instance));
            TestAssert.Equal(typeof(bool), typeof(ZInput).GetMethod(nameof(ZInput.IsGamepadActive),
                BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null)?.ReturnType);
        }

        private static void SavePreservesMetadata()
        {
            MethodInfo save = Exact(typeof(Inventory), nameof(Inventory.Save), typeof(ZPackage));
            foreach (string field in new[]
                     {
                         "m_gridPos", "m_stack", "m_durability", "m_equipped", "m_quality", "m_variant",
                         "m_crafterID", "m_crafterName", "m_customData", "m_worldLevel", "m_pickedUp", "m_dropPrefab"
                     })
                TestAssert.True(IlReader.Accesses(save, typeof(ItemDrop.ItemData), field), "Inventory.Save no longer accesses " + field + ".");
            TestAssert.False(IlReader.Calls(save, typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.Clone)),
                "Native save unexpectedly clones live item state.");
        }

        private static void LoadUsesBoundedAdd()
        {
            MethodInfo load = Exact(typeof(Inventory), nameof(Inventory.Load), typeof(ZPackage));
            IReadOnlyList<MethodBase> calls = IlReader.Calls(load);
            TestAssert.True(calls.Any(call => call.DeclaringType == typeof(Inventory) && call.Name == "AddItem" &&
                                                call.GetParameters().Length >= 4),
                "Inventory.Load no longer restores through coordinate AddItem.");
            MethodInfo add = typeof(Inventory).GetMethods(Instance)
                .Single(method => method.Name == "AddItem" && method.IsPrivate &&
                                  method.GetParameters().Select(parameter => parameter.ParameterType)
                                      .SequenceEqual(new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int) }));
            TestAssert.True(IlReader.Accesses(add, typeof(Inventory), "m_width"));
            TestAssert.True(IlReader.Accesses(add, typeof(Inventory), "m_height"));
        }

        private static void PlayerSaveLoadRemainVanilla()
        {
            MethodInfo save = Exact(typeof(Player), nameof(Player.Save), typeof(ZPackage));
            MethodInfo load = Exact(typeof(Player), nameof(Player.Load), typeof(ZPackage));
            TestAssert.True(IlReader.Calls(save, typeof(Inventory), nameof(Inventory.Save)));
            TestAssert.True(IlReader.Calls(load, typeof(Inventory), nameof(Inventory.Load)));
            TestAssert.True(IlReader.Accesses(save, typeof(Player), "m_customData"));
            TestAssert.True(IlReader.Accesses(load, typeof(Player), "m_customData"));
            TestAssert.False(IlReader.Calls(save, typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.Clone)));
            TestAssert.False(IlReader.Calls(load, typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.Clone)));
        }

        private static void ProfileSaveHasNoDurableAcknowledgement()
        {
            MethodInfo gameSave = Exact(typeof(Game), nameof(Game.SavePlayerProfile), typeof(bool));
            MethodInfo profileSave = Exact(typeof(PlayerProfile), nameof(PlayerProfile.Save));
            MethodInfo saveToDisk = Exact(typeof(PlayerProfile), "SavePlayerToDisk");
            TestAssert.Equal(typeof(void), gameSave.ReturnType);
            TestAssert.Equal(typeof(bool), profileSave.ReturnType);
            TestAssert.True(IlReader.Calls(gameSave, typeof(PlayerProfile), nameof(PlayerProfile.SavePlayerData)));
            TestAssert.True(IlReader.Calls(gameSave, typeof(PlayerProfile), nameof(PlayerProfile.Save)));

            IReadOnlyList<IlInstruction> gameInstructions = IlReader.Read(gameSave);
            bool discarded = false;
            for (int index = 0; index + 1 < gameInstructions.Count; index++)
            {
                if (gameInstructions[index].Operand is MethodBase call &&
                    call.DeclaringType == typeof(PlayerProfile) && call.Name == nameof(PlayerProfile.Save) &&
                    gameInstructions[index + 1].Code == OpCodes.Pop)
                {
                    discarded = true;
                    break;
                }
            }
            TestAssert.True(discarded, "Game.SavePlayerProfile no longer discards PlayerProfile.Save's result; re-audit the seam.");

            TestAssert.True(IlReader.Calls(saveToDisk, typeof(FileWriter), nameof(FileWriter.Finish)));
            TestAssert.True(IlReader.Calls(saveToDisk, typeof(FileWriter), "get_Status"));
            IReadOnlyList<IlInstruction> diskInstructions = IlReader.Read(saveToDisk);
            int returnIndex = diskInstructions.Count - 1;
            while (returnIndex >= 0 && diskInstructions[returnIndex].Code != OpCodes.Ret) returnIndex--;
            int valueIndex = returnIndex - 1;
            while (valueIndex >= 0 && diskInstructions[valueIndex].Code == OpCodes.Nop) valueIndex--;
            TestAssert.True(valueIndex >= 0 && diskInstructions[valueIndex].Code == OpCodes.Ldc_I4_1,
                "PlayerProfile.SavePlayerToDisk no longer returns an unconditional true; re-audit durable acknowledgement.");
        }

        private static void ProfileReadbackNeedsIndependentProof()
        {
            MethodInfo loadFromDisk = Exact(typeof(PlayerProfile), "LoadPlayerDataFromDisk");
            TestAssert.True(IlReader.Calls(loadFromDisk, typeof(FileReader), ".ctor"));
            TestAssert.False(IlReader.Calls(loadFromDisk, typeof(ZPackage), nameof(ZPackage.GenerateHash)),
                "Vanilla unexpectedly began validating the stored outer profile hash; re-audit the custom verifier.");

            const BindingFlags publicStatic = BindingFlags.Public | BindingFlags.Static;
            MethodInfo replace = TestAssert.NotNull(typeof(FileHelpers).GetMethod(
                nameof(FileHelpers.ReplaceOldFile), publicStatic));
            MethodInfo unmount = TestAssert.NotNull(typeof(FileHelpers).GetMethod(
                nameof(FileHelpers.Unmount), publicStatic, null, Type.EmptyTypes, null));
            ConstructorInfo reader = TestAssert.NotNull(typeof(FileReader).GetConstructor(
                new[] { typeof(string), typeof(FileHelpers.FileSource), typeof(FileHelpers.FileHelperType) }));
            TestAssert.Equal(typeof(void), replace.ReturnType);
            TestAssert.Equal(typeof(void), unmount.ReturnType);
            TestAssert.True(IlReader.CallsNamed(replace, "Splatform.IFileAccess", "MoveFile"),
                "The installed cloud replace seam no longer uses IFileAccess.MoveFile; re-audit its result handling.");
            TestAssert.True(IlReader.Calls(reader, typeof(FileHelpers), nameof(FileHelpers.Unmount)),
                "Cloud FileReader construction no longer reaches the commit/unmount seam.");
            TestAssert.NotNull(typeof(FileHelpers).GetField("m_mountedDepot",
                BindingFlags.NonPublic | BindingFlags.Static));
            TestAssert.NotNull(typeof(FileHelpers).GetField("m_depotReferenceCounter",
                BindingFlags.NonPublic | BindingFlags.Static));
        }

        private static void TombstoneUsesNativeInventoryMove()
        {
            MethodInfo tombstone = Exact(typeof(Player), nameof(Player.CreateTombStone));
            TestAssert.True(IlReader.Calls(tombstone, typeof(Inventory), nameof(Inventory.MoveInventoryToGrave)),
                "Player.CreateTombStone no longer delegates every carried native item to the grave inventory.");
            IReadOnlyList<MethodBase> calls = IlReader.Calls(tombstone);
            int unequip = IlReader.CallIndex(calls, typeof(Humanoid), nameof(Humanoid.UnequipAllItems));
            int move = IlReader.CallIndex(calls, typeof(Inventory), nameof(Inventory.MoveInventoryToGrave));
            TestAssert.True(unequip >= 0 && move > unequip,
                "Normal death no longer unequips native equipment before the grave move.");
        }

        private static void GraveMoveIsVanilla()
        {
            MethodInfo move = Exact(typeof(Inventory), nameof(Inventory.MoveInventoryToGrave), typeof(Inventory));
            TestAssert.True(IlReader.Calls(move, typeof(Inventory), nameof(Inventory.GetAllItems)) ||
                            IlReader.Accesses(move, typeof(Inventory), "m_inventory"));
            TestAssert.True(IlReader.Calls(move, typeof(Inventory), "Changed") ||
                            IlReader.Accesses(move, typeof(Inventory), "m_onChanged"));
            TestAssert.True(IlReader.Accesses(move, typeof(Inventory), "m_width"));
            TestAssert.True(IlReader.Accesses(move, typeof(Inventory), "m_height"));
            TestAssert.False(IlReader.Calls(move, typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.Clone)));
        }

        private static void EquipmentWeightRemainsNative()
        {
            MethodInfo method = Exact(typeof(Humanoid), nameof(Humanoid.GetEquipmentWeight));
            foreach (string field in new[] { "m_chestItem", "m_legItem", "m_helmetItem", "m_shoulderItem", "m_utilityItem" })
                TestAssert.True(IlReader.Accesses(method, typeof(Humanoid), field), "Missing equipment weight field " + field + ".");
            bool readsWeight = IlReader.Calls(method).Any(call => call.Name == nameof(ItemDrop.ItemData.GetWeight)) ||
                               IlReader.Read(method).Any(instruction =>
                                   instruction.Operand is FieldInfo field && field.Name == "m_weight");
            TestAssert.True(readsWeight,
                "Installed equipment-weight path no longer reads either ItemData.GetWeight or the native shared weight field.");
        }

        private static void UseItemValidatesMembership()
        {
            MethodInfo method = Exact(typeof(Humanoid), nameof(Humanoid.UseItem),
                typeof(Inventory), typeof(ItemDrop.ItemData), typeof(bool));
            TestAssert.True(IlReader.Calls(method, typeof(Inventory), nameof(Inventory.ContainsItem)));
        }

        private static void UpgradeCommitUsesExactItem()
        {
            MethodInfo crafting = Exact(typeof(InventoryGui), "DoCrafting", typeof(Player));
            TestAssert.True(IlReader.Accesses(crafting, typeof(InventoryGui), "m_craftUpgradeItem"));
            TestAssert.True(IlReader.Calls(crafting, typeof(Inventory), nameof(Inventory.ContainsItem)));
            TestAssert.True(IlReader.Calls(crafting, typeof(Inventory), nameof(Inventory.RemoveItem)));
            MethodInfo guard = typeof(RunicInventory.Integration.ValheimContracts).GetMethod(
                "CraftingCommitItem", BindingFlags.NonPublic | BindingFlags.Static);
            TestAssert.NotNull(guard);
            TestAssert.True(IlReader.Calls(guard).Any(call => call.Name == nameof(FieldInfo.GetValue)));
            TestAssert.Contains(
                File.ReadAllText(TestPaths.Module(Path.Combine("Integration", "ValheimContracts.cs"))),
                "ExactField(typeof(InventoryGui), \"m_craftUpgradeItem\", typeof(ItemDrop.ItemData))");
        }

        private static void PickupOrderingIsExact()
        {
            MethodInfo pickup = Exact(typeof(Humanoid), nameof(Humanoid.Pickup),
                typeof(GameObject), typeof(bool), typeof(bool));
            IReadOnlyList<MethodBase> calls = IlReader.Calls(pickup);
            int load = IlReader.CallIndex(calls, typeof(ItemDrop), nameof(ItemDrop.Load));
            int add = IlReader.CallIndex(calls, typeof(Inventory), nameof(Inventory.AddItem));
            int destroy = IlReader.CallIndex(calls, typeof(ZNetScene), nameof(ZNetScene.Destroy));
            TestAssert.True(load >= 0 && add > load && destroy > add,
                "Installed pickup ordering changed; prefix denial must be re-audited.");
        }

        private static void AuthoritySignaturesAreExact()
        {
            TestAssert.Equal(typeof(bool), Exact(typeof(ZNet), nameof(ZNet.IsServer)).ReturnType);
            TestAssert.Equal(typeof(bool), Exact(typeof(Player), nameof(Player.IsOwner)).ReturnType);
            FieldInfo localPlayer = typeof(Player).GetField("m_localPlayer",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            TestAssert.NotNull(localPlayer);
            TestAssert.Equal(typeof(Player), localPlayer.FieldType);
            foreach (string methodName in new[] { "Update", "FixedUpdate" })
            {
                MethodInfo playerLoop = Exact(typeof(Player), methodName);
                TestAssert.True(IlReader.Calls(playerLoop).Any(call =>
                        call.Name == nameof(Player.IsOwner) ||
                        (call.DeclaringType == typeof(ZNetView) && call.Name == nameof(ZNetView.IsOwner))),
                    "Installed Player." + methodName + " no longer gates its owner-local loop through IsOwner.");
            }
            FieldInfo custom = typeof(Player).GetField("m_customData", Instance);
            TestAssert.NotNull(custom);
            TestAssert.Equal(typeof(Dictionary<string, string>), custom.FieldType);
        }

        private static void ItemTypesAreExact()
        {
            TestAssert.Equal(6, (int)ItemDrop.ItemData.ItemType.Helmet);
            TestAssert.Equal(7, (int)ItemDrop.ItemData.ItemType.Chest);
            TestAssert.Equal(11, (int)ItemDrop.ItemData.ItemType.Legs);
            TestAssert.Equal(17, (int)ItemDrop.ItemData.ItemType.Shoulder);
            TestAssert.Equal(18, (int)ItemDrop.ItemData.ItemType.Utility);
            TestAssert.Equal(24, (int)ItemDrop.ItemData.ItemType.Trinket);
        }

        private static void ControllerLayoutsAreExact()
        {
            TestAssert.Equal(0, (int)InputLayout.Default);
            TestAssert.Equal(1, (int)InputLayout.Alternative1);
            TestAssert.Equal(2, (int)InputLayout.Alternative2);

            MethodInfo generic = Exact(typeof(ZInput), "ResetGamepadButtonsGeneric");
            TestAssert.Equal(GamepadInput.Select, MappedInput(generic, "JoyMap"));
            TestAssert.Equal(GamepadInput.Select, MappedInput(generic, "JoyChat"));
            TestAssert.Equal(GamepadInput.BumperL, MappedInput(generic, "JoyLBumper"));
            TestAssert.Equal(GamepadInput.FaceButtonY, MappedInput(generic, "JoyButtonY"));
            TestAssert.Equal(GamepadInput.BumperR, MappedInput(generic, "JoyRBumper"));
            TestAssert.Equal(GamepadInput.FaceButtonA, MappedInput(generic, "JoyButtonA"));
            TestAssert.Equal(GamepadInput.FaceButtonB, MappedInput(generic, "JoyButtonB"));

            TestAssert.Equal(GamepadInput.TriggerL,
                MappedInput(Exact(typeof(ZInput), "ResetGamepadToClassic"), "JoyAltKeys"));
            TestAssert.Equal(GamepadInput.BumperL,
                MappedInput(Exact(typeof(ZInput), "ResetGamepadToAlt1"), "JoyAltKeys"));
            TestAssert.Equal(GamepadInput.TriggerL,
                MappedInput(Exact(typeof(ZInput), "ResetGamepadToAlt2"), "JoyAltKeys"));

            FieldInfo mapField = TestAssert.NotNull(typeof(ZInput).GetField(
                "s_gamepadInputPathMap", BindingFlags.NonPublic | BindingFlags.Static));
            IDictionary paths = TestAssert.NotNull(mapField.GetValue(null) as IDictionary);
            TestAssert.Equal("<Gamepad>/select", paths[GamepadInput.Select] as string);
            TestAssert.Equal("<Gamepad>/leftShoulder", paths[GamepadInput.BumperL] as string);
            TestAssert.Equal("<Gamepad>/leftTrigger", paths[GamepadInput.TriggerL] as string);
        }

        private static void ControllerDefaultsAreUnique()
        {
            MethodInfo generic = Exact(typeof(ZInput), "ResetGamepadButtonsGeneric");
            GamepadInput[] primaries =
            {
                MappedInput(generic, "JoyMap"),
                MappedInput(generic, "JoyButtonY"),
                MappedInput(generic, "JoyRBumper"),
                MappedInput(generic, "JoyButtonA"),
                MappedInput(generic, "JoyButtonB")
            };
            foreach (string layout in new[]
                     {
                         "ResetGamepadToClassic", "ResetGamepadToAlt1", "ResetGamepadToAlt2"
                     })
            {
                var unique = new HashSet<GamepadInput>(primaries)
                {
                    MappedInput(Exact(typeof(ZInput), layout), "JoyAltKeys")
                };
                TestAssert.Equal(6, unique.Count, layout);
            }

            GamepadInput altOneModifier = MappedInput(
                Exact(typeof(ZInput), "ResetGamepadToAlt1"), "JoyAltKeys");
            TestAssert.Equal(MappedInput(generic, "JoyLBumper"), altOneModifier,
                "The installed legacy Alt1 collision must remain an explicit regression premise.");
        }

        private static GamepadInput MappedInput(MethodInfo method, string action)
        {
            IReadOnlyList<IlInstruction> instructions = IlReader.Read(method);
            for (int index = 0; index < instructions.Count; index++)
            {
                if (instructions[index].Code != OpCodes.Ldstr ||
                    !string.Equals(instructions[index].Operand as string, action, StringComparison.Ordinal))
                {
                    continue;
                }
                int limit = Math.Min(instructions.Count, index + 7);
                for (int call = index + 2; call < limit; call++)
                {
                    if (!(instructions[call].Operand is MethodBase target) ||
                        target.Name != "get_Item" || call == 0 ||
                        !TryReadInt(instructions[call - 1], out int value))
                    {
                        continue;
                    }
                    return (GamepadInput)value;
                }
            }
            throw new InvalidOperationException(method.Name + " has no audited mapping for " + action + ".");
        }

        private static bool TryReadInt(IlInstruction instruction, out int value)
        {
            if (instruction.Code == OpCodes.Ldc_I4_M1) value = -1;
            else if (instruction.Code == OpCodes.Ldc_I4_0) value = 0;
            else if (instruction.Code == OpCodes.Ldc_I4_1) value = 1;
            else if (instruction.Code == OpCodes.Ldc_I4_2) value = 2;
            else if (instruction.Code == OpCodes.Ldc_I4_3) value = 3;
            else if (instruction.Code == OpCodes.Ldc_I4_4) value = 4;
            else if (instruction.Code == OpCodes.Ldc_I4_5) value = 5;
            else if (instruction.Code == OpCodes.Ldc_I4_6) value = 6;
            else if (instruction.Code == OpCodes.Ldc_I4_7) value = 7;
            else if (instruction.Code == OpCodes.Ldc_I4_8) value = 8;
            else if (instruction.Code == OpCodes.Ldc_I4_S && instruction.Operand is sbyte shortValue)
                value = shortValue;
            else if (instruction.Code == OpCodes.Ldc_I4 && instruction.Operand is int intValue)
                value = intValue;
            else
            {
                value = 0;
                return false;
            }
            return true;
        }

        private static MethodInfo Exact(Type type, string name, params Type[] parameters) =>
            TestAssert.NotNull(type.GetMethod(name, Instance, null, parameters, null),
                type.FullName + "." + name + " exact signature is missing.");
    }
}

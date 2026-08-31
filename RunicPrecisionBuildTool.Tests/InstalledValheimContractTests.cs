using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using HarmonyLib;
using QuietBuildRotation.Integration;
using UnityEngine;

namespace QuietBuildRotation.Tests
{
    internal static class InstalledValheimContractTests
    {
        private const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic |
                                                      BindingFlags.Instance;
        private const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic |
                                                    BindingFlags.Static;

        internal static void Register()
        {
            TestRunner.Run("tests target installed Valheim 0.221.12", InstalledVersionIsExact);
            TestRunner.Run("installed Valheim assembly is the audited 0.221.12 binary", InstalledHashIsExact);
            TestRunner.Run("installed build-menu visibility remains owned by the native build HUD", BuildMenuVisibilityContractIsExact);
            TestRunner.Run("installed placement IL retains one exact rotation and pre-snap hook seam", PlacementHookShapeIsExact);
            TestRunner.Run("all vanilla placement validation remains after the pre-snap seam", PlacementValidationRemainsNative);
            TestRunner.Run("installed placement creation assigns the native creator after instantiate", CreatorAssignmentOrderingIsExact);
            TestRunner.Run("installed undo seams expose exact owner removal and policy APIs", UndoContractsAreExact);
            TestRunner.Run("installed repair seams expose exact native repair and durability APIs", RepairContractsAreExact);
            TestRunner.Run("installed structural-support proof fields retain exact types", SupportContractsAreExact);
            TestRunner.Run("installed native support publication advances ZDO state after placement", NativeSupportPublicationIsPostPlacement);
            TestRunner.Run("installed PieceTable catalog APIs retain exact unlocked-list contracts", CatalogContractsAreExact);
            TestRunner.Run("bounded catalog grid reader initializes under the net8 harness", CatalogGridReaderInitializes);
            TestRunner.Run("catalog grid reader rejects nonliteral and out-of-range fields", CatalogGridReaderRejectsUnsafeFields);
            TestRunner.Run("dedicated PieceTable retains the exact literal grid-width contract", DedicatedCatalogContractIsExact);
            TestRunner.Run("installed localization exposes exact bounded Translate seam", LocalizationContractIsExact);
            TestRunner.Run("dedicated authority boundary exposes exact server and object-owner APIs", AuthorityContractsAreExact);
            TestRunner.Run("installed unloaded-zone authority catalogues retain exact persistent seams", PersistentAuthorityCataloguesAreExact);
        }

        private static void InstalledVersionIsExact()
        {
            Type version = typeof(Player).Assembly.GetType("Version", true);
            MethodInfo method = version.GetMethod(
                "GetVersionString",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(bool) },
                null);
            TestAssert.True(method != null);
            TestAssert.Equal("0.221.12", (string)method.Invoke(null, new object[] { false }));
        }

        private static void InstalledHashIsExact()
        {
            using SHA256 sha = SHA256.Create();
            string path = typeof(Player).Assembly.Location;
            string hash = Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(path)));
            TestAssert.Equal(
                "3B26C8512778F6E0664B5AF2A26F3C30993A00F584C1E76D9123A742B67E2004",
                hash);
        }

        private static void BuildMenuVisibilityContractIsExact()
        {
            MethodInfo updateInput = Exact(typeof(Player), "UpdateBuildGuiInput");
            MethodInfo toggle = Exact(typeof(Hud), nameof(Hud.TogglePieceSelection));
            MethodInfo visible = Exact(typeof(Hud), nameof(Hud.IsPieceSelectionVisible));
            TestAssert.True(IlReader.Calls(updateInput).Any(call => Equals(call, toggle)),
                "BuildMenu input no longer opens through Hud.TogglePieceSelection.");
            TestAssert.True(IlReader.AccessesField(visible, typeof(Hud), "m_buildHud"),
                "Piece-menu visibility no longer depends on the native build HUD root.");
            TestAssert.True(IlReader.AccessesField(visible, typeof(Hud), "m_pieceSelectionWindow"),
                "Piece-menu visibility no longer depends on the native selection window.");
        }

        private static void PersistentAuthorityCataloguesAreExact()
        {
            MethodInfo loadedOnly = Exact(
                typeof(Location),
                nameof(Location.IsInsideNoBuildLocation),
                typeof(Vector3));
            FieldInfo loadedLocations = typeof(Location).GetField("s_allLocations", Static);
            TestAssert.True(loadedLocations != null);
            IReadOnlyList<IlInstruction> loadedCode = IlReader.Read(loadedOnly);
            TestAssert.True(loadedCode.Any(value =>
                value.Operand as FieldInfo == loadedLocations));
            TestAssert.True(IlReader.Calls(loadedOnly).Any(value =>
                value.DeclaringType == typeof(Location) && value.Name == "IsInside"));

            MethodInfo inside = Exact(
                typeof(Location), "IsInside", typeof(Vector3), typeof(float), typeof(bool));
            TestAssert.True(IlReader.Calls(inside).Any(value =>
                value.DeclaringType == typeof(Utils) && value.Name == "DistanceXZ"));
            TestAssert.True(IlReader.Calls(inside).Any(value =>
                value.DeclaringType == typeof(Location) && value.Name == "GetMaxRadius"));

            MethodInfo list = Exact(typeof(ZoneSystem), "GetLocationList");
            TestAssert.True(typeof(ICollection<ZoneSystem.LocationInstance>)
                .IsAssignableFrom(list.ReturnType));
            foreach (string mutation in new[]
                     {
                         "Load", "RegisterLocation", "PlaceLocations",
                         "ClearNonPlacedLocations", "RemoveUnplacedLocations"
                     })
                TestAssert.True(typeof(ZoneSystem).GetMethods(Instance).Any(
                    value => string.Equals(value.Name, mutation, StringComparison.Ordinal)));

            Type descriptor = typeof(ZoneSystem.ZoneLocation);
            TestAssert.True(descriptor.GetField("m_prefabName", Instance) != null);
            TestAssert.True(descriptor.GetField("m_interiorRadius", Instance) != null);
            TestAssert.True(descriptor.GetField("m_exteriorRadius", Instance) != null);
            TestAssert.False(descriptor.GetFields(Instance).Any(field =>
                field.Name.IndexOf("nobuild", StringComparison.OrdinalIgnoreCase) >= 0),
                "ZoneLocation unexpectedly gained direct no-build metadata; re-audit the authority source.");

            Type softReference = typeof(SoftReferenceableAssets.SoftReference<GameObject>);
            Type loadedHandler = typeof(SoftReferenceableAssets.LoadedHandler);
            MethodInfo loadAsync = Exact(softReference, "LoadAsync", loadedHandler);
            MethodInfo release = Exact(softReference, "Release");
            Type loadData = typeof(ZoneSystem).GetNestedType(
                "LocationPrefabLoadData", BindingFlags.NonPublic);
            TestAssert.True(loadData != null);
            ConstructorInfo loadDataConstructor = loadData.GetConstructor(
                Instance, null, new[] { softReference, typeof(bool) }, null);
            TestAssert.True(loadDataConstructor != null);
            TestAssert.True(IlReader.Calls(loadDataConstructor).Any(call =>
                Equals(call, loadAsync)),
                "Native location metadata no longer enters through bounded async prefab loading.");
            MethodInfo releaseData = loadData.GetMethod("Release", Instance);
            TestAssert.True(releaseData != null);
            TestAssert.True(IlReader.Calls(releaseData).Any(call => Equals(call, release)),
                "Native location prefab loading no longer balances the SoftReference lease.");
            MethodInfo poke = Exact(
                typeof(ZoneSystem), "PokeCanSpawnLocation", descriptor, typeof(bool));
            TestAssert.True(IlReader.Calls(poke).Any(call =>
                Equals(call, loadDataConstructor)));
            TestAssert.True(IlReader.Calls(poke).Any(call =>
                call.DeclaringType == loadData && call.Name == "get_IsLoaded"));
            MethodInfo forceRelease = Exact(typeof(ZoneSystem), "ForceReleaseLoadedPrefabs");
            TestAssert.True(IlReader.Calls(forceRelease).Any(call =>
                call.DeclaringType == loadData && call.Name == "Release"));

            MethodInfo setup = Exact(typeof(ZoneSystem), "SetupLocations");
            IReadOnlyList<MethodBase> setupCalls = IlReader.Calls(setup);
            int syncLoad = setupCalls.ToList().FindIndex(call =>
                call.DeclaringType == softReference && call.Name == "Load");
            int hold = setupCalls.ToList().FindIndex(call =>
                call.DeclaringType == typeof(SoftReferenceableAssets.ReferenceHolder) &&
                call.Name == "HoldReferenceTo");
            int syncRelease = setupCalls.ToList().FindIndex(call =>
                call.DeclaringType == softReference && call.Name == "Release");
            TestAssert.True(syncLoad >= 0 && hold > syncLoad && syncRelease > hold,
                "Native eager location loading no longer transfers then balances its reference.");

            MethodInfo wardScan = typeof(ZDOMan).GetMethods(Instance).Single(value =>
                string.Equals(
                    value.Name,
                    "GetAllZDOsWithPrefabIterative",
                    StringComparison.Ordinal));
            ParameterInfo[] parameters = wardScan.GetParameters();
            TestAssert.Equal(typeof(bool), wardScan.ReturnType);
            TestAssert.Equal(3, parameters.Length);
            TestAssert.Equal(typeof(string), parameters[0].ParameterType);
            TestAssert.Equal(typeof(List<ZDO>), parameters[1].ParameterType);
            TestAssert.Equal(typeof(int).MakeByRefType(), parameters[2].ParameterType);
        }

        private static void PlacementHookShapeIsExact()
        {
            MethodInfo ghost = Exact(typeof(Player), "UpdatePlacementGhost", typeof(bool));
            IReadOnlyList<IlInstruction> code = IlReader.Read(ghost);
            FieldInfo rotation = typeof(Player).GetField("m_placeRotation", Instance);
            FieldInfo rotationDegrees = typeof(Player).GetField("m_placeRotationDegrees", Instance);
            FieldInfo tempPieces = typeof(Player).GetField("m_tempPieces", Instance);
            MethodInfo euler = typeof(Quaternion).GetMethod(
                nameof(Quaternion.Euler),
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(float), typeof(float), typeof(float) },
                null);
            int rotationMatches = 0;
            int translationMatches = 0;
            for (int index = 8; index < code.Count; index++)
            {
                if (IsCall(code[index], euler) && IsFloatZero(code[index - 8]) &&
                    code[index - 7].OpCode == System.Reflection.Emit.OpCodes.Ldarg_0 &&
                    code[index - 6].Operand as FieldInfo == rotationDegrees &&
                    code[index - 5].OpCode == System.Reflection.Emit.OpCodes.Ldarg_0 &&
                    code[index - 4].Operand as FieldInfo == rotation &&
                    code[index - 3].OpCode == System.Reflection.Emit.OpCodes.Conv_R4 &&
                    code[index - 2].OpCode == System.Reflection.Emit.OpCodes.Mul &&
                    IsFloatZero(code[index - 1]))
                    rotationMatches++;
            }
            for (int index = 0; index + 4 < code.Count; index++)
            {
                MethodInfo clear = code[index + 4].Operand as MethodInfo;
                if (LoadsLocal(code[index]) && IsBranchTrue(code[index + 1]) &&
                    code[index + 2].OpCode == System.Reflection.Emit.OpCodes.Ldarg_0 &&
                    code[index + 3].Operand as FieldInfo == tempPieces &&
                    clear?.Name == nameof(List<Piece>.Clear) &&
                    clear.DeclaringType == typeof(List<Piece>))
                    translationMatches++;
            }
            TestAssert.Equal(1, rotationMatches);
            TestAssert.Equal(1, translationMatches);

            string adapter = File.ReadAllText(ModuleSource(
                Path.Combine("Integration", "PlacementAdapter.cs")));
            TestAssert.True(adapter.Contains(
                "rotationAnchors.Count == 1 && translationAnchors.Count == 1",
                StringComparison.Ordinal));
            TestAssert.True(adapter.Contains(
                "nameof(PlacementRuntime.ApplyCandidateTranslation)",
                StringComparison.Ordinal));
        }

        private static void PlacementValidationRemainsNative()
        {
            MethodInfo ghost = Exact(typeof(Player), "UpdatePlacementGhost", typeof(bool));
            IReadOnlyList<MethodBase> calls = IlReader.Calls(ghost);
            int clear = IlReader.CallIndex(calls, typeof(List<Piece>), nameof(List<Piece>.Clear));
            int snap = IlReader.CallIndex(calls, typeof(Player), "FindClosestSnapPoints");
            int noBuild = IlReader.CallIndex(calls, typeof(Location), nameof(Location.IsInsideNoBuildLocation));
            int ward = IlReader.CallIndex(calls, typeof(PrivateArea), nameof(PrivateArea.CheckAccess));
            int valid = calls.ToList().FindIndex(call => call.Name == "SetPlacementGhostValid");
            TestAssert.True(clear >= 0 && snap > clear,
                "Automatic snap no longer follows the audited AltPlace branch.");
            TestAssert.True(noBuild > clear && ward > clear && valid > clear,
                "A native no-build, ward, or placement-validity check moved before the Runic seam.");
            TestAssert.True(IlReader.AccessesField(ghost, typeof(Piece), "m_clipEverything"));
            TestAssert.True(IlReader.AccessesField(ghost, typeof(Piece), "m_noClipping"));
        }

        private static void CreatorAssignmentOrderingIsExact()
        {
            MethodInfo place = Exact(
                typeof(Player),
                "PlacePiece",
                typeof(Piece),
                typeof(Vector3),
                typeof(Quaternion),
                typeof(bool));
            IReadOnlyList<MethodBase> calls = IlReader.Calls(place);
            int instantiate = calls.ToList().FindIndex(call =>
                call.DeclaringType == typeof(UnityEngine.Object) && call.Name == nameof(UnityEngine.Object.Instantiate));
            int creator = IlReader.CallIndex(calls, typeof(Piece), nameof(Piece.SetCreator));
            TestAssert.True(instantiate >= 0 && creator > instantiate);
            TestAssert.Equal(1, IlReader.CountCalls(place, typeof(Piece), nameof(Piece.SetCreator)));
        }

        private static void UndoContractsAreExact()
        {
            MethodInfo check = Exact(typeof(Player), "CheckCanRemovePiece", typeof(Piece));
            TestAssert.Equal(typeof(bool), check.ReturnType);
            TestAssert.True(IlReader.AccessesField(check, typeof(Player), "m_noPlacementCost"));
            TestAssert.True(IlReader.AccessesField(check, typeof(Piece), "m_craftingStation"));
            TestAssert.True(IlReader.Calls(
                check, typeof(CraftingStation), nameof(CraftingStation.HaveBuildStationInRange)));
            TestAssert.True(IlReader.Calls(check, typeof(ZoneSystem), nameof(ZoneSystem.GetGlobalKey)));
            MethodInfo remove = Exact(typeof(WearNTear), nameof(WearNTear.Remove), typeof(bool));
            TestAssert.Equal(typeof(void), remove.ReturnType);
            MethodInfo nativeRemove = Exact(typeof(Player), "RemovePiece");
            TestAssert.True(IlReader.Calls(nativeRemove, typeof(Player), "CheckCanRemovePiece"));
            TestAssert.True(IlReader.Calls(nativeRemove, typeof(PrivateArea), nameof(PrivateArea.CheckAccess)));
            TestAssert.True(IlReader.Calls(nativeRemove, typeof(WearNTear), nameof(WearNTear.Remove)));
            TestAssert.True(IlReader.AccessesField(nativeRemove, typeof(Player), "m_maxPlaceDistance"));
        }

        private static void RepairContractsAreExact()
        {
            MethodInfo wearRepair = Exact(typeof(WearNTear), nameof(WearNTear.Repair));
            TestAssert.Equal(typeof(bool), wearRepair.ReturnType);
            MethodInfo repair = Exact(
                typeof(Player), "Repair", typeof(ItemDrop.ItemData), typeof(Piece));
            TestAssert.True(IlReader.Calls(repair, typeof(Player), "CheckCanRemovePiece"));
            TestAssert.True(IlReader.Calls(repair, typeof(PrivateArea), nameof(PrivateArea.CheckAccess)));
            TestAssert.True(IlReader.Calls(repair, typeof(WearNTear), nameof(WearNTear.Repair)));
            MethodInfo durability = Exact(
                typeof(Player), "GetPlaceDurability", typeof(ItemDrop.ItemData));
            TestAssert.Equal(typeof(float), durability.ReturnType);
            MethodInfo update = Exact(typeof(Player), "UpdatePlacement", typeof(bool), typeof(float));
            TestAssert.True(IlReader.Calls(update, typeof(Player), "GetPlaceDurability"),
                "Native build/remove durability no longer uses the audited player-adjusted cost.");
        }

        private static void SupportContractsAreExact()
        {
            FieldInfo supports = typeof(WearNTear).GetField("m_supportColliders", Instance);
            FieldInfo dirty = typeof(WearNTear).GetField("m_clearCachedSupport", Instance);
            TestAssert.True(supports != null);
            TestAssert.Equal(typeof(List<Collider>), supports.FieldType);
            TestAssert.True(dirty != null);
            TestAssert.Equal(typeof(bool), dirty.FieldType);
            MethodInfo all = typeof(WearNTear).GetMethod(
                nameof(WearNTear.GetAllInstances),
                BindingFlags.Public | BindingFlags.Static,
                null,
                Type.EmptyTypes,
                null);
            TestAssert.True(all != null);
            TestAssert.Equal(typeof(List<WearNTear>), all.ReturnType);
        }

        private static void NativeSupportPublicationIsPostPlacement()
        {
            MethodInfo placed = Exact(typeof(WearNTear), nameof(WearNTear.OnPlaced));
            MethodInfo update = typeof(WearNTear).GetMethod(
                "UpdateSupport", Instance, null, Type.EmptyTypes, null);
            TestAssert.True(placed != null && update != null);
            TestAssert.True(IlReader.Calls(update, typeof(ZDO), nameof(ZDO.Set)),
                "Installed UpdateSupport no longer publishes native support through ZDO.Set.");
            TestAssert.False(IlReader.Calls(placed, typeof(ZDO), nameof(ZDO.Set)),
                "Installed OnPlaced now publishes support synchronously; re-audit capture timing.");
        }

        private static void CatalogContractsAreExact()
        {
            FieldInfo available = typeof(PieceTable).GetField("m_availablePieces", Instance);
            FieldInfo gridWidth = typeof(PieceTable).GetField("m_gridWidth", Static);
            TestAssert.True(available != null, "PieceTable.m_availablePieces is missing.");
            TestAssert.Equal(typeof(List<List<Piece>>), available.FieldType);
            TestAssert.True(gridWidth != null, "PieceTable.m_gridWidth is missing.");
            TestAssert.Equal(typeof(int), gridWidth.FieldType);
            TestAssert.True(gridWidth.IsStatic);
            TestAssert.True(gridWidth.IsPublic);
            TestAssert.True(gridWidth.IsLiteral,
                "PieceTable.m_gridWidth is a const; a by-reference Harmony accessor is invalid.");
            TestAssert.Equal(15, (int)gridWidth.GetRawConstantValue());
            TestAssert.Equal(typeof(void), Exact(typeof(PieceTable), "SetCategory", typeof(int)).ReturnType);
            TestAssert.Equal(typeof(void), Exact(typeof(PieceTable), "SetSelected", typeof(Vector2Int)).ReturnType);
            TestAssert.Equal(typeof(Piece), Exact(typeof(PieceTable), "GetSelectedPiece").ReturnType);
        }

        private static void CatalogGridReaderInitializes()
        {
            BuildCatalogRuntime.Shutdown();
            try
            {
                bool initialized = BuildCatalogRuntime.InitializeGridWidthReader(out string error);
                TestAssert.True(initialized, error);
            }
            finally
            {
                BuildCatalogRuntime.Shutdown();
            }

            string source = File.ReadAllText(ModuleSource(
                Path.Combine("Integration", "BuildCatalogRuntime.cs")));
            TestAssert.False(source.Contains("StaticFieldRefAccess", StringComparison.Ordinal),
                "Literal grid width regressed to Harmony by-reference access.");
            TestAssert.True(source.Contains(
                "gridWidth != entry.GridWidth",
                StringComparison.Ordinal),
                "Fresh selection no longer revalidates the catalog grid width.");
        }

        private static void DedicatedCatalogContractIsExact()
        {
            const string path =
                @"E:\SteamLibrary\steamapps\common\Valheim dedicated server\valheim_server_Data\Managed\assembly_valheim.dll";
            using (SHA256 sha = SHA256.Create())
            {
                string hash = Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(path)));
                TestAssert.Equal(
                    "84A1B34F95774D36BE328390578D7B07C5CFFBC8CBB15119541900F055D486A3",
                    hash);
            }

            using FileStream stream = File.OpenRead(path);
            using PEReader pe = new PEReader(stream);
            MetadataReader reader = pe.GetMetadataReader();
            FieldDefinitionHandle match = default;
            foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
            {
                TypeDefinition type = reader.GetTypeDefinition(typeHandle);
                if (reader.GetString(type.Name) != nameof(PieceTable)) continue;
                foreach (FieldDefinitionHandle fieldHandle in type.GetFields())
                {
                    FieldDefinition candidate = reader.GetFieldDefinition(fieldHandle);
                    if (reader.GetString(candidate.Name) != "m_gridWidth") continue;
                    TestAssert.True(match.IsNil, "Dedicated PieceTable has duplicate m_gridWidth fields.");
                    match = fieldHandle;
                }
            }

            TestAssert.False(match.IsNil, "Dedicated PieceTable.m_gridWidth is missing.");
            FieldDefinition field = reader.GetFieldDefinition(match);
            FieldAttributes attributes = field.Attributes;
            TestAssert.Equal(FieldAttributes.Public, attributes & FieldAttributes.FieldAccessMask);
            TestAssert.True((attributes & FieldAttributes.Static) != 0);
            TestAssert.True((attributes & FieldAttributes.Literal) != 0);
            TestAssert.True((attributes & FieldAttributes.HasDefault) != 0);
            ConstantHandle constantHandle = field.GetDefaultValue();
            TestAssert.False(constantHandle.IsNil);
            Constant constant = reader.GetConstant(constantHandle);
            TestAssert.Equal(ConstantTypeCode.Int32, constant.TypeCode);
            BlobReader value = reader.GetBlobReader(constant.Value);
            TestAssert.Equal(15, value.ReadInt32());
        }

        private static void CatalogGridReaderRejectsUnsafeFields()
        {
            TestAssert.False(BuildCatalogRuntime.TryReadLiteralGridWidth(
                typeof(GridWidthFixtures).GetField(nameof(GridWidthFixtures.Mutable), Static),
                out _));
            TestAssert.False(BuildCatalogRuntime.TryReadLiteralGridWidth(
                typeof(GridWidthFixtures).GetField(nameof(GridWidthFixtures.Zero), Static),
                out _));
            TestAssert.False(BuildCatalogRuntime.TryReadLiteralGridWidth(
                typeof(GridWidthFixtures).GetField(nameof(GridWidthFixtures.TooWide), Static),
                out _));
            TestAssert.False(BuildCatalogRuntime.TryReadLiteralGridWidth(
                typeof(GridWidthFixtures).GetField(nameof(GridWidthFixtures.WrongType), Static),
                out _));
        }

        private static void LocalizationContractIsExact()
        {
            MethodInfo translate = Exact(typeof(Localization), "Translate", typeof(string));
            TestAssert.False(translate.IsStatic);
            TestAssert.Equal(typeof(string), translate.ReturnType);
        }

        private static void AuthorityContractsAreExact()
        {
            TestAssert.Equal(typeof(bool), Exact(typeof(ZNet), nameof(ZNet.IsServer)).ReturnType);
            TestAssert.Equal(typeof(long), Exact(typeof(ZNet), nameof(ZNet.GetWorldUID)).ReturnType);
            TestAssert.Equal(typeof(long), Exact(typeof(ZNet), nameof(ZNet.GetUID)).ReturnType);
            TestAssert.Equal(typeof(ZNetPeer), Exact(
                typeof(ZNet), nameof(ZNet.GetPeer), typeof(long)).ReturnType);
            TestAssert.Equal(typeof(bool), Exact(typeof(ZNetView), nameof(ZNetView.IsOwner)).ReturnType);
            TestAssert.Equal(typeof(bool), Exact(typeof(ZNetView), nameof(ZNetView.IsValid)).ReturnType);
            TestAssert.Equal(typeof(ZDO), Exact(typeof(ZNetView), nameof(ZNetView.GetZDO)).ReturnType);
            TestAssert.Equal(typeof(long), Exact(typeof(Piece), nameof(Piece.GetCreator)).ReturnType);
            TestAssert.Equal(typeof(List<Player>), Exact(
                typeof(Player), nameof(Player.GetAllPlayers)).ReturnType);
            TestAssert.Equal(typeof(ZDO), Exact(
                typeof(ZDOMan), nameof(ZDOMan.GetZDO), typeof(ZDOID)).ReturnType);
            TestAssert.Equal(typeof(ZNetView), Exact(
                typeof(ZNetScene), nameof(ZNetScene.FindInstance), typeof(ZDO)).ReturnType);
            TestAssert.Equal(typeof(bool), Exact(
                typeof(ZoneSystem), nameof(ZoneSystem.IsZoneLoaded), typeof(UnityEngine.Vector3)).ReturnType);
            TestAssert.Equal(typeof(bool), Exact(
                typeof(ZoneSystem), nameof(ZoneSystem.IsZoneLoaded), typeof(Vector2i)).ReturnType);
            TestAssert.Equal(typeof(Vector2i), Exact(
                typeof(ZoneSystem), nameof(ZoneSystem.GetZone), typeof(UnityEngine.Vector3)).ReturnType);
            TestAssert.Equal(typeof(long), Exact(typeof(ZDO), nameof(ZDO.GetOwner)).ReturnType);
            TestAssert.Equal(typeof(void), Exact(
                typeof(ZDO), nameof(ZDO.SetOwner), typeof(long)).ReturnType);
            TestAssert.Equal(typeof(UnityEngine.Vector3), Exact(
                typeof(ZDO), nameof(ZDO.GetPosition)).ReturnType);
            TestAssert.Equal(typeof(UnityEngine.Quaternion), Exact(
                typeof(ZDO), nameof(ZDO.GetRotation)).ReturnType);
            PropertyInfo revision = typeof(ZDO).GetProperty("DataRevision", Instance);
            TestAssert.True(revision != null);
            TestAssert.Equal(typeof(uint), revision.PropertyType);
            FieldInfo character = typeof(ZNetPeer).GetField("m_characterID", Instance);
            TestAssert.True(character != null);
            TestAssert.Equal(typeof(ZDOID), character.FieldType);
            FieldInfo areas = typeof(PrivateArea).GetField("m_allAreas", Static);
            TestAssert.True(areas != null);
            TestAssert.Equal(typeof(List<PrivateArea>), areas.FieldType);
            TestAssert.Equal(typeof(bool), Exact(typeof(PrivateArea), "IsEnabled").ReturnType);
            TestAssert.Equal(typeof(bool), Exact(
                typeof(PrivateArea), "IsInside", typeof(UnityEngine.Vector3), typeof(float)).ReturnType);
            TestAssert.Equal(typeof(bool), Exact(
                typeof(PrivateArea), "IsPermitted", typeof(long)).ReturnType);
        }

        private static MethodInfo Exact(Type type, string name, params Type[] parameters)
        {
            MethodInfo method = type.GetMethod(name, Instance | Static, null, parameters, null);
            TestAssert.True(method != null,
                type.FullName + "." + name + "(" +
                string.Join(",", parameters.Select(parameter => parameter.Name)) + ") is missing.");
            return method;
        }

        private static bool IsCall(IlInstruction instruction, MethodInfo method) =>
            (instruction.OpCode == System.Reflection.Emit.OpCodes.Call ||
             instruction.OpCode == System.Reflection.Emit.OpCodes.Callvirt) &&
            Equals(instruction.Operand, method);

        private static bool IsFloatZero(IlInstruction instruction) =>
            instruction.OpCode == System.Reflection.Emit.OpCodes.Ldc_R4 &&
            instruction.Operand is float value && value == 0f;

        private static bool LoadsLocal(IlInstruction instruction)
        {
            System.Reflection.Emit.OpCode code = instruction.OpCode;
            return code == System.Reflection.Emit.OpCodes.Ldloc ||
                   code == System.Reflection.Emit.OpCodes.Ldloc_S ||
                   code == System.Reflection.Emit.OpCodes.Ldloc_0 ||
                   code == System.Reflection.Emit.OpCodes.Ldloc_1 ||
                   code == System.Reflection.Emit.OpCodes.Ldloc_2 ||
                   code == System.Reflection.Emit.OpCodes.Ldloc_3;
        }

        private static bool IsBranchTrue(IlInstruction instruction) =>
            instruction.OpCode == System.Reflection.Emit.OpCodes.Brtrue ||
            instruction.OpCode == System.Reflection.Emit.OpCodes.Brtrue_S;

        private static string ModuleSource(string relative)
        {
            string root = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", ".."));
            return Path.Combine(root, "RunicPrecisionBuildTool", relative);
        }

        private static class GridWidthFixtures
        {
            public static int Mutable = 15;
            public const int Zero = 0;
            public const int TooWide = BuildCatalogRuntime.MaximumGridWidth + 1;
            public const long WrongType = 15L;
        }
    }
}

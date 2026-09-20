using System;
using System.IO;
using System.Linq;
using Mono.Cecil;

namespace RunicCrafting.Tests
{
    internal static class InstalledValheimContractTests
    {
        internal static void PreviewRefreshAndInvalidationHooksMatchInstalledValheim()
        {
            using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(ValheimAssemblyPath());
            AssertMethod(assembly, "Player", "UpdateAvailablePiecesList");
            AssertMethod(assembly, "Player", "UpdatePlacement", "System.Boolean", "System.Single");
            AssertMethod(assembly, "InventoryGui", "DoCrafting", "Player");
            TestAssert.Equal(1, FindType(assembly, "Player").Methods.Count(m => m.Name == "TryPlacePiece"));
            AssertMethod(assembly, "PieceTable", "UpdateAvailable", "System.Collections.Generic.HashSet`1<System.String>", "Player", "System.Boolean", "System.Boolean");
            AssertMethod(assembly, "Hud", "UpdateBuild", "Player", "System.Boolean");
            AssertMethod(assembly, "Hud", "UpdatePieceBuildStatus", "System.Collections.Generic.List`1<Piece>", "Player");
            AssertMethod(assembly, "Hud", "UpdatePieceBuildStatusAll", "System.Collections.Generic.List`1<Piece>", "Player");
            AssertMethod(assembly, "Hud", "SetupPieceInfo", "Piece");
            AssertMethod(assembly, "InventoryGui", "UpdateCraftingPanel", "System.Boolean");
            AssertMethod(assembly, "InventoryGui", "UpdateRecipeList", "System.Collections.Generic.List`1<Recipe>");
            AssertMethod(assembly, "InventoryGui", "UpdateRecipe", "Player", "System.Single");
            AssertMethod(assembly, "InventoryGui", "SetupRequirementList", "System.Int32", "Player", "System.Boolean", "System.Int32");
            AssertMethod(assembly, "Inventory", "Changed", "System.Boolean", "System.Boolean");
            AssertMethod(assembly, "ZDO", "IncreaseDataRevision");
            AssertMethod(assembly, "ZDO", "IncreaseOwnerRevision");
            AssertMethod(assembly, "ZDO", "set_DataRevision", "System.UInt32");
            AssertMethod(assembly, "ZDO", "set_OwnerRevision", "System.UInt16");
            AssertMethod(assembly, "ZDO", "Deserialize", "ZPackage");
            AssertMethod(assembly, "PrivateArea", "Awake");
            AssertMethod(assembly, "PrivateArea", "OnDestroy");
            string patch = ReadSource("PreviewRefreshPatches.cs");
            TestAssert.True(patch.Contains("Finalizer(long __state)", StringComparison.Ordinal));
            TestAssert.True(patch.Contains("PreviewRefreshRuntime.End(__state)", StringComparison.Ordinal));
            TestAssert.True(patch.Contains("!ReferenceEquals(__instance, PreviewRefreshRuntime.LoadingPreview)", StringComparison.Ordinal));
            TestAssert.True(patch.Contains("PreviewRefreshRuntime.Supported = false", StringComparison.Ordinal));
            string query = ReadSource("ContainerQueryRuntime.cs");
            TestAssert.True(query.Contains("if (!requireWritable && allowRefreshCache && !PreviewRefreshRuntime.InAction &&", StringComparison.Ordinal));
            TestAssert.True(query.Contains("PreviewRefreshRuntime.Cache.Active && ValheimReflection.CanMutateLocalPlayer(player)", StringComparison.Ordinal));
            string writableGuard = query.Substring(query.IndexOf("if (requireWritable)", StringComparison.Ordinal));
            writableGuard = writableGuard.Substring(0, writableGuard.IndexOf("if (!requireWritable", StringComparison.Ordinal));
            TestAssert.True(writableGuard.Contains("PreviewRefreshRuntime.Invalidate();", StringComparison.Ordinal));
            TestAssert.True(writableGuard.Contains("UiPreviewCache.Invalidate();", StringComparison.Ordinal));
            TestAssert.True(query.Contains("allPreviewResources: true", StringComparison.Ordinal));
            TestAssert.True(ReadSource("CraftingRuntime.cs").Contains("PreviewRefreshRuntime.Invalidate();", StringComparison.Ordinal));
            TestAssert.True(ReadSource("ContainerSpatialIndex.cs").Contains("PreviewRefreshRuntime.Invalidate();", StringComparison.Ordinal));
            TestAssert.True(patch.Contains("PreviewRefreshRuntime.EnterAction();", StringComparison.Ordinal));
            TestAssert.True(patch.Contains("PreviewRefreshRuntime.ExitAction();", StringComparison.Ordinal));
            TestAssert.True(patch.Contains("UiPreviewCache.Changed(__instance);", StringComparison.Ordinal));
            TestAssert.True(patch.Contains("if (instance is ZDO) UiPreviewCache.Changed(instance);", StringComparison.Ordinal));
        }

        private static readonly string ProjectRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", ".."));

        internal static void InventoryChangedUsesValheim10Signature()
        {
            AssertMethod("Inventory", "Changed", "System.Boolean", "System.Boolean");

            string source = ReadSource("ValheimReflection.cs");
            TestAssert.True(source.Contains(
                "new[] { typeof(bool), typeof(bool) }",
                StringComparison.Ordinal));
            TestAssert.True(source.Contains(
                "new object[] { false, false }",
                StringComparison.Ordinal));
        }

        internal static void ManualInteractionHooksMatchInstalledValheim()
        {
            AssertMethod("CookingStation", "OnInteract", "Humanoid");
            AssertMethod("CookingStation", "HaveDoneItem");
            AssertMethod("CookingStation", "GetFreeSlot");
            AssertMethod("CookingStation", "IsFireLit");
            AssertMethod("CookingStation", "FindCookableItem", "Inventory");
            AssertMethod("CookingStation", "GetFuel");
            AssertMethod("CookingStation", "OnAddFuelSwitch", "Switch", "Humanoid", "ItemDrop/ItemData");
            AssertMethod("Smelter", "OnAddFuel", "Switch", "Humanoid", "ItemDrop/ItemData");
            AssertMethod("Smelter", "GetFuel");
            AssertMethod("Fireplace", "Interact", "Humanoid", "System.Boolean", "System.Boolean");
            AssertMethod("WearNTear", "Repair");
            AssertMethod("Inventory", "CanAddItem", "ItemDrop/ItemData", "System.Int32");
            string patch = ReadSource("Patches.cs");
            TestAssert.True(patch.Contains("WearNTear.Repair", StringComparison.Ordinal));
            TestAssert.True(patch.Contains("bool __result", StringComparison.Ordinal));
            AssertMethod("Inventory", "AddItem", "ItemDrop/ItemData");
            using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(ValheimAssemblyPath());
            AssertField(assembly,"Fireplace","m_lastUseTime","System.Single");
            string source=ReadSource("ManualInteractionRuntime.cs");
            TestAssert.True(source.Contains("bool __runOriginal",StringComparison.Ordinal));
            TestAssert.True(source.Contains("PrivateArea.CheckAccess",StringComparison.Ordinal));
            TestAssert.True(source.Contains("requireWritable: true",StringComparison.Ordinal));
            TestAssert.True(source.Contains("MaterialSourceKind.NearbyContainer",StringComparison.Ordinal));
            TestAssert.False(source.Contains("InvokeRPC(",StringComparison.Ordinal));
            TestAssert.False(source.Contains("SetOwner(",StringComparison.Ordinal));
        }

        internal static void CraftOutputAddItemUsesValheim10Signature()
        {
            AssertMethod(
                "Inventory",
                "AddItem",
                "System.String",
                "System.Int32",
                "System.Int32",
                "System.Int32",
                "System.Int64",
                "System.String",
                "Vector2i",
                "System.Boolean",
                "System.Boolean",
                "System.Boolean");

            string source = ReadSource("Patches.cs");
            TestAssert.True(source.Contains(
                "typeof(Vector2i), typeof(bool), typeof(bool), typeof(bool)",
                StringComparison.Ordinal));
        }

        internal static void PlacePieceUsesValheim10Signature()
        {
            AssertMethod(
                "Player",
                "PlacePiece",
                "Piece",
                "UnityEngine.Vector3",
                "UnityEngine.Quaternion",
                "System.Boolean",
                "System.Boolean");

            string source = ReadSource("Patches.cs");
            TestAssert.True(source.Contains(
                "typeof(Quaternion), typeof(bool), typeof(bool)",
                StringComparison.Ordinal));
        }

        internal static void PrivateReflectionBridgeMatchesValheim10()
        {
            using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(ValheimAssemblyPath());
            AssertField(assembly, "CraftingStation", "m_upgrader", "System.Boolean");
            AssertField(assembly, "Piece/Requirement", "m_upgraderResource", "System.Boolean");
            AssertField(assembly, "Container", "m_nview", "ZNetView");
            AssertField(assembly, "CraftingStation", "m_nview", "ZNetView");
            AssertField(
                assembly,
                "PrivateArea",
                "m_allAreas",
                "System.Collections.Generic.List`1<PrivateArea>");
            AssertField(
                assembly,
                "InventoryGui",
                "m_selectedRecipe",
                "InventoryGui/RecipeDataPair");
            AssertField(
                assembly,
                "InventoryGui/RecipeDataPair",
                "<Recipe>k__BackingField",
                "Recipe");
            AssertMethod(assembly, "Container", "CheckAccess", "System.Int64");
            AssertMethod(assembly, "PrivateArea", "IsEnabled");
            AssertMethod(
                assembly,
                "PrivateArea",
                "IsInside",
                "UnityEngine.Vector3",
                "System.Single");
        }

        private static void AssertMethod(
            string declaringType,
            string methodName,
            params string[] parameterTypes)
        {
            using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(ValheimAssemblyPath());
            AssertMethod(assembly, declaringType, methodName, parameterTypes);
        }

        private static void AssertMethod(
            AssemblyDefinition assembly,
            string declaringType,
            string methodName,
            params string[] parameterTypes)
        {
            TypeDefinition type = FindType(assembly, declaringType);
            bool found = type.Methods.Any(method =>
                string.Equals(method.Name, methodName, StringComparison.Ordinal) &&
                method.Parameters.Select(parameter => parameter.ParameterType.FullName)
                    .SequenceEqual(parameterTypes));
            TestAssert.True(found,
                $"Valheim 1.0 contract missing: {declaringType}.{methodName}(" +
                string.Join(", ", parameterTypes) + ").");
        }

        private static void AssertField(
            AssemblyDefinition assembly,
            string declaringType,
            string fieldName,
            string fieldType)
        {
            TypeDefinition type = FindType(assembly, declaringType);
            FieldDefinition field = type.Fields.SingleOrDefault(candidate =>
                string.Equals(candidate.Name, fieldName, StringComparison.Ordinal));
            TestAssert.True(field != null,
                $"Valheim 1.0 contract missing: {declaringType}.{fieldName}.");
            TestAssert.Equal(fieldType, field.FieldType.FullName,
                $"Valheim 1.0 field type changed: {declaringType}.{fieldName}.");
        }

        private static TypeDefinition FindType(AssemblyDefinition assembly, string fullName)
        {
            TypeDefinition type = assembly.MainModule.Types
                .SelectMany(SelfAndNestedTypes)
                .SingleOrDefault(candidate =>
                    string.Equals(candidate.FullName, fullName, StringComparison.Ordinal));
            TestAssert.True(type != null, "Valheim 1.0 type missing: " + fullName + ".");
            return type;
        }

        private static System.Collections.Generic.IEnumerable<TypeDefinition> SelfAndNestedTypes(
            TypeDefinition type)
        {
            yield return type;
            foreach (TypeDefinition nested in type.NestedTypes.SelectMany(SelfAndNestedTypes))
                yield return nested;
        }

        private static string ValheimAssemblyPath()
        {
            string install = Environment.GetEnvironmentVariable("VALHEIM_INSTALL");
            TestAssert.True(!string.IsNullOrWhiteSpace(install),
                "VALHEIM_INSTALL must identify the Valheim 1.0 installation.");
            string path = Path.Combine(install, "valheim_Data", "Managed", "assembly_valheim.dll");
            TestAssert.True(File.Exists(path), "Installed assembly_valheim.dll was not found: " + path);
            return path;
        }

        private static string ReadSource(string fileName) =>
            File.ReadAllText(Path.Combine(ProjectRoot, "Integration", fileName));
    }
}

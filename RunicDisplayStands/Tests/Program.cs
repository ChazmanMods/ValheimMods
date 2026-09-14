using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Mono.Cecil;
using RunicDisplayStands;

internal static class Program
{
    private static readonly List<(string Name, Action Body)> Tests = new();

    private static int Main(string[] args)
    {
        string sourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string clientAssembly = GetPath(
            args,
            0,
            "VALHEIM_CLIENT_ASSEMBLY",
            @"E:\SteamLibrary\steamapps\common\Valheim\valheim_Data\Managed\assembly_valheim.dll");
        string serverAssembly = GetPath(
            args,
            1,
            "VALHEIM_SERVER_ASSEMBLY",
            @"E:\SteamLibrary\steamapps\common\Valheim dedicated server\valheim_server_Data\Managed\assembly_valheim.dll");
        string pluginAssembly = GetPath(
            args,
            2,
            "RUNIC_DISPLAY_STANDS_ASSEMBLY",
            Path.Combine(sourceRoot, "bin", "Release", "netstandard2.1", "RunicDisplayStands.dll"));

        RequireFile(clientAssembly, "Valheim client assembly");
        RequireFile(serverAssembly, "Valheim dedicated-server assembly");
        RequireFile(pluginAssembly, "RunicDisplayStands assembly");

        AddMigrationTests();
        Tests.AddRange(TransferTests.Cases());
        Tests.AddRange(LoadoutTests.Cases());
        Tests.Add(("plugin and package release identity is 1.3.8", () =>
            VerifyReleaseIdentity(pluginAssembly, sourceRoot)));
        Tests.Add(("client Valheim 1.0 API contract", () => VerifyGameContract(clientAssembly)));
        Tests.Add(("server Valheim 1.0 API contract", () => VerifyGameContract(serverAssembly)));
        Tests.Add(("plugin references resolve on client", () => VerifyPluginReferences(pluginAssembly, clientAssembly)));
        Tests.Add(("plugin references resolve on dedicated server", () => VerifyPluginReferences(pluginAssembly, serverAssembly)));
        Tests.Add(("stable-hash migration source invariants", () => VerifyMigrationSource(sourceRoot)));
        Tests.Add(("Harmony target and UI safety invariants", () => VerifyPatchSource(sourceRoot)));

        int failed = 0;
        foreach ((string name, Action body) in Tests)
        {
            try
            {
                body();
                Console.WriteLine($"PASS  {name}");
            }
            catch (Exception error)
            {
                failed++;
                Console.WriteLine($"FAIL  {name}: {error.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Client assembly SHA256: {Sha256(clientAssembly)}");
        Console.WriteLine($"Server assembly SHA256: {Sha256(serverAssembly)}");
        Console.WriteLine($"RunicDisplayStands SHA256: {Sha256(pluginAssembly)}");
        Console.WriteLine($"Result: {Tests.Count - failed}/{Tests.Count} passed");
        return failed == 0 ? 0 : 1;
    }

    private static void AddMigrationTests()
    {
        Tests.Add(("current integer identity wins without legacy access", () =>
        {
            int reads = 0;
            int hashes = 0;
            int writes = 0;
            int result = AttachedItemIdentity.Resolve(
                12345,
                isOwner: true,
                () => { reads++; return "legacy"; },
                _ => { hashes++; return 99; },
                _ => writes++);

            Equal(12345, result, "current identity");
            Equal(0, reads, "legacy reads");
            Equal(0, hashes, "legacy hashes");
            Equal(0, writes, "migration writes");
        }));

        Tests.Add(("empty legacy identity stays empty", () =>
        {
            int writes = 0;
            int result = AttachedItemIdentity.Resolve(0, true, () => string.Empty, _ => 99, _ => writes++);
            Equal(0, result, "resolved identity");
            Equal(0, writes, "migration writes");
        }));

        Tests.Add(("non-owner can read legacy identity without mutating ZDO", () =>
        {
            int writes = 0;
            int result = AttachedItemIdentity.Resolve(0, false, () => "SwordIron", _ => 6789, _ => writes++);
            Equal(6789, result, "resolved identity");
            Equal(0, writes, "migration writes");
        }));

        Tests.Add(("owner migrates legacy identity exactly once", () =>
        {
            var writes = new List<int>();
            int result = AttachedItemIdentity.Resolve(0, true, () => "SwordIron", _ => 6789, writes.Add);
            Equal(6789, result, "resolved identity");
            Equal(1, writes.Count, "migration write count");
            Equal(6789, writes[0], "migrated identity");
        }));

        Tests.Add(("zero legacy hash is never persisted", () =>
        {
            int writes = 0;
            int result = AttachedItemIdentity.Resolve(0, true, () => "collision", _ => 0, _ => writes++);
            Equal(0, result, "resolved identity");
            Equal(0, writes, "migration writes");
        }));
    }

    private static void VerifyGameContract(string assemblyPath)
    {
        using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
        ModuleDefinition module = assembly.MainModule;

        ExactMethod(module, "ItemStand", "GetAttachedItem", "System.Int32");
        ExactMethod(module, "ItemStand", "GetOrientation", "System.Int32");
        ExactMethod(module, "ItemStand", "Interact", "System.Boolean", "Humanoid", "System.Boolean", "System.Boolean");
        ExactMethod(module, "Switch", "Interact", "System.Boolean", "Humanoid", "System.Boolean", "System.Boolean");
        ExactMethod(module, "ArmorStand", "GetAttachedItem", "System.Int32", "System.Int32");
        ExactMethod(module, "ArmorStand", "CanAttach", "System.Boolean", "ArmorStand/ArmorStandSlot", "ItemDrop/ItemData");
        ExactField(module, "ArmorStand", "m_slots", "System.Collections.Generic.List`1<ArmorStand/ArmorStandSlot>");
        foreach (string field in new[] { "m_leftItem", "m_rightItem", "m_chestItem", "m_legItem", "m_helmetItem",
                     "m_shoulderItem", "m_utilityItem", "m_trinketItem", "m_ammoItem", "m_hiddenLeftItem", "m_hiddenRightItem" })
            ExactField(module, "Humanoid", field, "ItemDrop/ItemData");
        ExactMethod(module, "Humanoid", "SetupEquipment", "System.Void");
        ExactMethod(module, "InventoryGrid", "UpdateGui", "System.Void", "Player", "ItemDrop/ItemData");
        ExactMethod(module, "InventoryGrid", "DropItem", "System.Boolean", "Inventory", "ItemDrop/ItemData", "System.Int32", "Vector2i");

        ExactMethod(module, "ObjectDB", "GetItemPrefab", "UnityEngine.GameObject", "System.Int32");
        ExactMethod(module, "ItemDrop", "LoadFromZDO", "System.Void", "ItemDrop/ItemData", "ZDO", "System.Int32");
        ExactMethod(module, "ItemDrop", "SaveToZDO", "System.Void", "ItemDrop/ItemData", "ZDO", "System.Int32");
        ExactMethod(module, "ZDO", "GetInt", "System.Int32", "System.Int32", "System.Int32");
        ExactMethod(module, "ZDO", "GetString", "System.String", "System.Int32", "System.String");
        ExactMethod(module, "ZDO", "RemoveString", "System.Boolean", "System.Int32");
        ExactMethod(module, "ZDO", "Set", "System.Void", "System.Int32", "System.Int32", "System.Boolean");

        ExactMethod(module, "ZRoutedRpc", "AddPeer", "System.Void", "ZNetPeer");
        ExactMethod(module, "ZNet", "Awake", "System.Void");
        ExactMethod(module, "InventoryGui", "Show", "System.Void", "Container", "System.Int32");
        ExactMethod(module, "InventoryGui", "UpdateContainer", "System.Void", "Player");
        ExactMethod(module, "InventoryGui", "OnStackAll", "System.Void");
        ExactMethod(module, "InventoryGui", "Hide", "System.Void");
        ExactMethod(module, "InventoryGui", "CloseContainer", "System.Void");
        ExactMethod(module, "Inventory", "Changed", "System.Void", "System.Boolean", "System.Boolean");
        ExactMethod(module, "Container", "IsOwner", "System.Boolean");
        ExactMethod(module, "Container", "SetInUse", "System.Void", "System.Boolean");
        ExactMethod(module, "Container", "StackAll", "System.Void");
        ExactField(module, "Container", "m_inventory", "Inventory");
        ExactMethod(module, "ZNetView", "ClaimOwnership", "System.Void");
        ExactMethod(module, "ZNetView", "IsOwner", "System.Boolean");
        ExactMethod(module, "PrivateArea", "CheckAccess", "System.Boolean", "UnityEngine.Vector3", "System.Single", "System.Boolean", "System.Boolean");
        ExactMethod(module, "InventoryGrid", "GetElement", "InventoryElement", "System.Int32", "System.Int32", "System.Int32");
        ExactField(module, "InventoryGrid", "m_elements", "System.Collections.Generic.List`1<InventoryElement>");

        UniqueMethodName(module, "ItemStand", "Interact");
        UniqueMethodName(module, "Switch", "Interact");
        UniqueMethodName(module, "InventoryGrid", "GetElement");
        UniqueMethodName(module, "InventoryGui", "UpdateContainer");
        UniqueMethodName(module, "InventoryGui", "OnStackAll");
        UniqueMethodName(module, "InventoryGui", "Hide");
        UniqueMethodName(module, "InventoryGui", "CloseContainer");
        UniqueMethodName(module, "Container", "IsOwner");
        UniqueMethodName(module, "Container", "SetInUse");
        UniqueMethodName(module, "Container", "StackAll");
        UniqueMethodName(module, "ZNet", "Awake");
        UniqueMethodName(module, "ZRoutedRpc", "AddPeer");
    }

    private static void VerifyPluginReferences(string pluginPath, string gamePath)
    {
        using AssemblyDefinition plugin = AssemblyDefinition.ReadAssembly(pluginPath);
        using AssemblyDefinition game = AssemblyDefinition.ReadAssembly(gamePath);
        AssemblyNameReference[] bepinExReferences = plugin.MainModule.AssemblyReferences
            .Where(reference => reference.Name == "BepInEx")
            .ToArray();
        Equal(1, bepinExReferences.Length, "BepInEx reference count");
        Equal("5.4.23.5", bepinExReferences[0].Version.ToString(), "BepInEx compile baseline");

        var methods = new HashSet<string>(AllTypes(game.MainModule).SelectMany(type => type.Methods).Select(CanonicalMethod));
        var fields = new HashSet<string>(AllTypes(game.MainModule).SelectMany(type => type.Fields).Select(field => field.FullName));
        var missing = new SortedSet<string>(StringComparer.Ordinal);

        foreach (MethodDefinition method in AllTypes(plugin.MainModule).SelectMany(type => type.Methods))
        {
            if (!method.HasBody) continue;
            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.Operand is MethodReference methodReference && IsGameReference(methodReference.DeclaringType))
                {
                    string signature = CanonicalMethod(methodReference.GetElementMethod());
                    if (!methods.Contains(signature)) missing.Add(signature);
                }
                else if (instruction.Operand is FieldReference fieldReference && IsGameReference(fieldReference.DeclaringType))
                {
                    if (!fields.Contains(fieldReference.FullName)) missing.Add(fieldReference.FullName);
                }
            }
        }

        if (missing.Count != 0)
            throw new InvalidOperationException("unresolved game members: " + string.Join("; ", missing));
    }

    private static void VerifyReleaseIdentity(string pluginPath, string sourceRoot)
    {
        using AssemblyDefinition plugin = AssemblyDefinition.ReadAssembly(pluginPath);
        Equal("RunicDisplayStands", plugin.Name.Name, "assembly name");
        Equal("1.3.8.0", plugin.Name.Version.ToString(), "assembly version");

        TypeDefinition pluginType = AllTypes(plugin.MainModule).Single(type =>
            type.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == "BepInEx.BepInPlugin"));
        CustomAttribute identity = pluginType.CustomAttributes.Single(attribute =>
            attribute.AttributeType.FullName == "BepInEx.BepInPlugin");
        Equal("chazman.RunicDisplayStands", identity.ConstructorArguments[0].Value?.ToString() ?? string.Empty, "plugin GUID");
        Equal("RunicDisplayStands", identity.ConstructorArguments[1].Value?.ToString() ?? string.Empty, "plugin name");
        Equal("1.3.8", identity.ConstructorArguments[2].Value?.ToString() ?? string.Empty, "plugin version");

        using System.Text.Json.JsonDocument manifest = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(sourceRoot, "manifest.json")));
        Equal("RunicDisplayStands", manifest.RootElement.GetProperty("name").GetString() ?? string.Empty, "package name");
        Equal("1.3.8", manifest.RootElement.GetProperty("version_number").GetString() ?? string.Empty, "package version");
        string[] dependencies = manifest.RootElement.GetProperty("dependencies").EnumerateArray()
            .Select(value => value.GetString() ?? string.Empty).ToArray();
        Equal(1, dependencies.Length, "package dependency count");
        Equal("denikson-BepInExPack_Valheim-5.4.2350", dependencies[0], "BepInEx package dependency");
    }

    private static bool IsGameReference(TypeReference type)
    {
        TypeReference elementType = type.GetElementType();
        return string.Equals(elementType.Scope?.Name, "assembly_valheim", StringComparison.OrdinalIgnoreCase);
    }

    private static string CanonicalMethod(MethodReference method)
    {
        string parameters = string.Join(",", method.Parameters.Select(parameter => CanonicalType(parameter.ParameterType)));
        return $"{CanonicalType(method.ReturnType)} {CanonicalType(method.DeclaringType)}::{method.Name}``{method.GenericParameters.Count}({parameters})";
    }

    private static string CanonicalType(TypeReference type)
    {
        if (type is GenericParameter generic)
            return (generic.Type == GenericParameterType.Method ? "!!" : "!") + generic.Position;
        if (type is GenericInstanceType instance)
            return CanonicalType(instance.ElementType) + "<" + string.Join(",", instance.GenericArguments.Select(CanonicalType)) + ">";
        if (type is ArrayType array)
            return CanonicalType(array.ElementType) + "[" + new string(',', array.Rank - 1) + "]";
        if (type is ByReferenceType byReference)
            return CanonicalType(byReference.ElementType) + "&";
        if (type is PointerType pointer)
            return CanonicalType(pointer.ElementType) + "*";
        return type.FullName;
    }

    private static void VerifyMigrationSource(string sourceRoot)
    {
        string itemSource = File.ReadAllText(Path.Combine(sourceRoot, "Patches", "ItemStandAdapter.cs"));
        string armorSource = File.ReadAllText(Path.Combine(sourceRoot, "Patches", "ArmorStandAdapter.cs"));

        Contains(itemSource, "stand.GetAttachedItem()", "ItemStand integer identity read");
        Contains(itemSource, "ObjectDB.instance.GetItemPrefab(prefabHash)", "ItemStand integer prefab lookup");
        Contains(itemSource, "zdo.GetString(ZDOVars.s_item", "ItemStand legacy string read");
        AtLeast(itemSource, "RemoveString(ZDOVars.s_item)", 1, "ItemStand legacy identity migration");
        string batchSource = File.ReadAllText(Path.Combine(sourceRoot, "Transfers", "StandWriteBatch.cs"));
        Contains(batchSource, "ClearLegacy(identityKey)", "prepared identity writes clear stale strings");
        Contains(batchSource, "_zdo.RemoveString(key)", "batch legacy cleanup and rollback");
        DoesNotMatch(itemSource, @"SetVisualItem""\s*,\s*prefabName", "ItemStand string visual RPC");

        Contains(armorSource, "stand.GetAttachedItem(i)", "ArmorStand integer identity read");
        Contains(armorSource, "ObjectDB.instance.GetItemPrefab(prefabHash)", "ArmorStand integer prefab lookup");
        Contains(armorSource, "zdo.GetString(itemKey", "ArmorStand legacy string read");
        Contains(armorSource, "zdo.RemoveString(itemKey)", "ArmorStand migration cleanup");
        Contains(armorSource, "batch.Item(mapped[i], i)", "ArmorStand uses prepared set/clear batch for every slot");
        DoesNotMatch(armorSource, @"RPC_SetVisualItem""\s*,\s*i\s*,\s*prefabName", "ArmorStand string visual RPC");
    }

    private static void VerifyPatchSource(string sourceRoot)
    {
        string configSource = File.ReadAllText(Path.Combine(sourceRoot, "Network", "ConfigSync.cs"));
        string gridSource = File.ReadAllText(Path.Combine(sourceRoot, "Patches", "InventoryGridCompatibility.cs"));
        string bridgeSource = File.ReadAllText(Path.Combine(sourceRoot, "ContainerBridge.cs"));
        string interactionSource = File.ReadAllText(Path.Combine(sourceRoot, "Patches", "InteractionPatches.cs"));

        Contains(configSource, "typeof(ZRoutedRpc), nameof(ZRoutedRpc.AddPeer)", "route-ready config-sync hook");
        DoesNotMatch(configSource, @"HarmonyPatch\(typeof\(ZNet\),\s*""OnNewConnection""", "premature config-sync hook");
        Contains(configSource, "ReferenceEquals(_registeredRpc, routedRpc)", "per-session RPC registration");
        Contains(configSource, "MaximumPayloadBytes = 4096", "config payload byte bound");
        Contains(configSource, "MaximumEntries = 16", "config entry bound");
        Contains(configSource, "MaximumKeyBytes = 128", "config key byte bound");
        Contains(configSource, "MaximumValueBytes = 2048", "config value byte bound");
        Contains(configSource, "Encoding.UTF8.GetByteCount(value)", "UTF-8 byte accounting");
        Contains(configSource, "ZNetPeer server = network.GetServerPeer()", "current server-peer lookup");
        Contains(configSource, "server.m_uid != sender", "exact server-sender gate");
        Contains(configSource, "!values.TryAdd(key, value)", "duplicate-key rejection");
        int senderGate = configSource.IndexOf("server.m_uid != sender", StringComparison.Ordinal);
        int decode = configSource.IndexOf("if (!TryDeserialize(payload))", StringComparison.Ordinal);
        if (senderGate < 0 || decode <= senderGate)
            throw new InvalidOperationException("config payload is parsed before exact server authentication");
        Contains(gridSource, "x < width", "grid horizontal bound");
        Contains(gridSource, "ref InventoryElement __result", "explicit skipped-method result");
        Contains(bridgeSource, "_stackAllButtonWasActive", "vanilla button-state preservation");
        Contains(bridgeSource, "_stackAllButtonText", "vanilla button-label preservation");
        DoesNotMatch(bridgeSource, @"AccessTools\.Method\(typeof\(Inventory\),\s*""Changed""", "stale Inventory.Changed reflection");
        AtLeast(bridgeSource, "TryClaimOwnership()", 2, "ownership gates for stand mutation");
        Contains(bridgeSource, "_baseline != StandContents(ReadStand())", "revalidate saved stand roles before writes");
        Contains(bridgeSource, "TransferBoundary.Complete", "GUI and explicit moves share commit boundary");
        DoesNotMatch(bridgeSource, @"ItemDrop\.DropItem\(", "failed moves must not spawn world items");
        DoesNotMatch(bridgeSource, @"PushInventoryToStand", "no callback or close-time stale flush");
        AtLeast(interactionSource, "PrivateArea.CheckAccess", 2, "ward access enforcement");
        Contains(
            interactionSource,
            "[HarmonyBefore(\"chazman.RunicInteraction\")]",
            "managed armor-stand interception ordering");
    }

    private static void ExactMethod(
        ModuleDefinition module,
        string typeName,
        string methodName,
        string returnType,
        params string[] parameterTypes)
    {
        TypeDefinition type = ExactType(module, typeName);
        MethodDefinition[] matches = type.Methods.Where(method =>
                method.Name == methodName &&
                method.ReturnType.FullName == returnType &&
                method.Parameters.Select(parameter => parameter.ParameterType.FullName).SequenceEqual(parameterTypes))
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException($"expected one {typeName}.{methodName}({string.Join(", ", parameterTypes)}) -> {returnType}; found {matches.Length}");
    }

    private static void ExactField(ModuleDefinition module, string typeName, string fieldName, string fieldType)
    {
        TypeDefinition type = ExactType(module, typeName);
        FieldDefinition[] matches = type.Fields.Where(field => field.Name == fieldName && field.FieldType.FullName == fieldType).ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException($"expected one {fieldType} {typeName}.{fieldName}; found {matches.Length}");
    }

    private static void UniqueMethodName(ModuleDefinition module, string typeName, string methodName)
    {
        int count = ExactType(module, typeName).Methods.Count(method => method.Name == methodName);
        if (count != 1)
            throw new InvalidOperationException($"Harmony target {typeName}.{methodName} is ambiguous or absent; found {count}");
    }

    private static TypeDefinition ExactType(ModuleDefinition module, string typeName)
    {
        TypeDefinition[] matches = AllTypes(module).Where(type => type.FullName == typeName).ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException($"expected one type {typeName}; found {matches.Length}");
        return matches[0];
    }

    private static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition module) => module.Types.SelectMany(Flatten);

    private static IEnumerable<TypeDefinition> Flatten(TypeDefinition type)
    {
        yield return type;
        foreach (TypeDefinition nested in type.NestedTypes.SelectMany(Flatten)) yield return nested;
    }

    private static string GetPath(string[] args, int index, string environmentName, string fallback)
    {
        string? value = args.Length > index ? args[index] : Environment.GetEnvironmentVariable(environmentName);
        return Path.GetFullPath(string.IsNullOrWhiteSpace(value) ? fallback : value);
    }

    private static void RequireFile(string path, string label)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"{label} not found", path);
    }

    private static void Contains(string source, string expected, string label)
    {
        if (!source.Contains(expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"missing {label}");
    }

    private static void DoesNotMatch(string source, string pattern, string label)
    {
        if (Regex.IsMatch(source, pattern, RegexOptions.CultureInvariant))
            throw new InvalidOperationException($"found forbidden {label}");
    }

    private static void AtLeast(string source, string value, int minimum, string label)
    {
        int count = Regex.Matches(source, Regex.Escape(value), RegexOptions.CultureInvariant).Count;
        if (count < minimum)
            throw new InvalidOperationException($"expected at least {minimum} occurrences of {label}; found {count}");
    }

    private static void Equal<T>(T expected, T actual, string label) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}

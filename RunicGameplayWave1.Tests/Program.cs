using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using System.Text.RegularExpressions;
using BepInEx;
using HarmonyLib;
using Runic.Foundation.Core;
using RunicAgriculture.Core;
using RunicCrafting.Capabilities;
using RunicProduction.Contracts;
using RunicStorage.Capabilities;
using RunicTransactions.Contracts;

namespace RunicGameplayWave1.Tests
{
    internal static class Program
    {
        private const string TestProviderVersion = "1.0.0";

        private static readonly string[] GameplayAssemblyNames =
        {
            "RunicStorage", "RunicCrafting", "RunicAgriculture", "RunicProduction"
        };

        private static readonly IReadOnlyDictionary<string, string> ExpectedPluginGuids =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["RunicStorage"] = "chazman.RunicStorage",
                ["RunicCrafting"] = "chazman.RunicCrafting",
                ["RunicAgriculture"] = "chazman.RunicAgriculture",
                ["RunicProduction"] = "chazman.RunicProduction"
            };

        private static readonly IReadOnlyDictionary<string, string> ExpectedGameplayVersions =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["RunicStorage"] = "1.0.0",
                ["RunicCrafting"] = "1.0.0",
                ["RunicAgriculture"] = "1.0.0",
                ["RunicProduction"] = "1.0.0"
            };

        private static readonly IReadOnlyDictionary<string, string[]> ExpectedFoundationDependencies =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["RunicStorage"] = new[]
                {
                    "chazman.RunicCore@1.0.0", "chazman.RunicPersistence@1.0.0",
                    "chazman.RunicTransactions@1.0.0", "chazman.RunicInventory@1.0.0"
                },
                ["RunicCrafting"] = new[]
                {
                    "chazman.RunicCore@1.0.0", "chazman.RunicPersistence@1.0.0",
                    "chazman.RunicPermissions@1.0.0",
                    "chazman.RunicTransactions@1.0.0"
                },
                ["RunicAgriculture"] = new[]
                {
                    "chazman.RunicCore@1.0.0", "chazman.RunicPersistence@1.0.0",
                    "chazman.RunicTransactions@1.0.0"
                },
                ["RunicProduction"] = new[]
                {
                    "chazman.RunicCore@1.0.0", "chazman.RunicPermissions@1.0.0",
                    "chazman.RunicPersistence@1.0.0", "chazman.RunicTransactions@1.0.0"
                }
            };

        private static readonly IReadOnlyDictionary<string, string[]> ExpectedManifestDependencies =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["RunicStorage"] = new[]
                {
                    "denikson-BepInExPack_Valheim-5.4.2333",
                    "Chazman-RunicCore-1.0.0", "Chazman-RunicPersistence-1.0.0",
                    "Chazman-RunicTransactions-1.0.0", "Chazman-RunicInventory-1.0.0"
                },
                ["RunicCrafting"] = new[]
                {
                    "denikson-BepInExPack_Valheim-5.4.2333",
                    "Chazman-RunicCore-1.0.0", "Chazman-RunicPersistence-1.0.0",
                    "Chazman-RunicPermissions-1.0.0",
                    "Chazman-RunicTransactions-1.0.0"
                },
                ["RunicAgriculture"] = new[]
                {
                    "denikson-BepInExPack_Valheim-5.4.2333",
                    "Chazman-RunicCore-1.0.0", "Chazman-RunicTransactions-1.0.0",
                    "Chazman-RunicPersistence-1.0.0"
                },
                ["RunicProduction"] = new[]
                {
                    "denikson-BepInExPack_Valheim-5.4.2333",
                    "Chazman-RunicCore-1.0.0", "Chazman-RunicPermissions-1.0.0",
                    "Chazman-RunicTransactions-1.0.0", "Chazman-RunicPersistence-1.0.0"
                }
            };

        private static int Main()
        {
            var tests = new (string Name, Action Run)[]
            {
                ("all four gameplay production assemblies load", AssembliesLoad),
                ("all Harmony targets resolve against the installed Valheim build", HarmonyTargetsResolve),
                ("gameplay modules have no hard gameplay-module references", NoGameplayReferenceCycles),
                ("plugin identities are unique and version aligned", PluginIdentities),
                ("release metadata layers are exactly version aligned", ReleaseMetadataLayers),
                ("gameplay modules require their exact compatible Transactions floor", TransactionDependencyFloor),
                ("plugin foundation dependencies are exact", PluginFoundationDependencies),
                ("manifests have no gameplay hard dependencies", ManifestDependencyGraph),
                ("Wave1 builder archives superseded packages before guarded promotion", WaveBuilderPromotionContract),
                ("typed optional container provider resolves across binaries", TypedContainerProviderResolves),
                ("canonical Valheim IDs are shared", CanonicalValheimIds),
                ("gameplay capability contracts are semantically distinct", CapabilityContractsAreDistinct),
                ("Production advertises the stable-identity capability protocol", ProductionCapabilityProtocol),
                ("Storage publishes query but defers crash-unsafe transfer", StorageCapabilityBoundary),
                ("default keyboard chords are unique", DefaultKeyboardChordsAreUnique),
                ("controller routes use audited Valheim actions and distinct chords", ControllerContracts),
                ("Storage controller sessions consume chains without owning Agriculture controls", StorageControllerSessionBoundary),
                ("Storage reports radius-clipped query coverage as incomplete", StorageQueryCoverageBoundary),
                ("Crafting requirement rows use exact recipe and build UI contexts", CraftingRequirementUiContextBoundary),
                ("Crafting stationless scope never replaces a missing required station", CraftingStationlessScopeBoundary),
                ("Crafting commits exceptional placement only after the exact Instantiate boundary", CraftingPlacementInstantiationBoundary)
            };
            int failures = 0;
            foreach ((string name, Action run) in tests)
            {
                try { run(); System.Console.WriteLine("PASS " + name); }
                catch (Exception exception) { failures++; System.Console.Error.WriteLine("FAIL " + name + ": " + exception); }
            }
            System.Console.WriteLine(failures == 0 ? $"PASS: {tests.Length}/{tests.Length} integrated gameplay tests" : $"FAIL: {failures}/{tests.Length}");
            return failures == 0 ? 0 : 1;
        }

        private static Assembly[] Assemblies()
        {
            return new[]
            {
                typeof(RunicStorage.Plugin).Assembly,
                typeof(RunicCrafting.Plugin).Assembly,
                typeof(RunicAgriculture.Plugin).Assembly,
                typeof(RunicProduction.Plugin).Assembly
            };
        }

        private static void AssembliesLoad()
        {
            foreach (Assembly assembly in Assemblies())
            {
                Require(GameplayAssemblyNames.Contains(assembly.GetName().Name, StringComparer.Ordinal), "Unexpected gameplay assembly " + assembly.FullName);
                Require(assembly.GetTypes().Length > 0, assembly.GetName().Name + " contains no loadable types.");
            }
        }

        private static void HarmonyTargetsResolve()
        {
            const BindingFlags AllMethods = BindingFlags.Public | BindingFlags.NonPublic |
                                            BindingFlags.Instance | BindingFlags.Static |
                                            BindingFlags.FlattenHierarchy;
            int resolvedClasses = 0;
            foreach (Assembly assembly in Assemblies())
            {
                foreach (Type patchType in assembly.GetTypes())
                {
                    object[] attributes = patchType.GetCustomAttributes(typeof(HarmonyPatch), inherit: false);
                    if (attributes.Length == 0) continue;
                    Type declaringType = null;
                    string methodName = null;
                    Type[] argumentTypes = null;
                    MethodType methodType = MethodType.Normal;
                    foreach (HarmonyPatch attribute in attributes.Cast<HarmonyPatch>())
                    {
                        HarmonyMethod info = attribute.info;
                        if (info.declaringType != null) declaringType = info.declaringType;
                        if (!string.IsNullOrEmpty(info.methodName)) methodName = info.methodName;
                        if (info.argumentTypes != null) argumentTypes = info.argumentTypes;
                        if (info.methodType.HasValue) methodType = info.methodType.Value;
                    }

                    Require(declaringType != null, patchType.FullName + " has no declaring target type.");
                    Require(methodType == MethodType.Normal,
                        patchType.FullName + " uses an unsupported target method kind " + methodType + ".");
                    Require(!string.IsNullOrEmpty(methodName), patchType.FullName + " has no target method name.");
                    MethodInfo[] candidates = declaringType.GetMethods(AllMethods)
                        .Where(method => string.Equals(method.Name, methodName, StringComparison.Ordinal))
                        .Where(method => argumentTypes == null || method.GetParameters()
                            .Select(parameter => parameter.ParameterType)
                            .SequenceEqual(argumentTypes))
                        .ToArray();
                    Require(candidates.Length != 0,
                        patchType.FullName + " does not resolve " + declaringType.FullName + "." + methodName +
                        " against the installed Valheim assemblies.");
                    Require(candidates.Length == 1,
                        patchType.FullName + " resolves an ambiguous target signature; declare exact argument types.");
                    resolvedClasses++;
                }
            }
            Require(resolvedClasses >= 20,
                "Too few Harmony patch classes were discovered; the runtime signature audit is incomplete.");
        }

        private static void NoGameplayReferenceCycles()
        {
            foreach (Assembly assembly in Assemblies())
            {
                foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
                {
                    if (!GameplayAssemblyNames.Contains(reference.Name, StringComparer.Ordinal)) continue;
                    throw new InvalidOperationException(assembly.GetName().Name + " hard-references gameplay module " + reference.Name + ".");
                }
            }
        }

        private static void PluginIdentities()
        {
            var guids = new HashSet<string>(StringComparer.Ordinal);
            var moduleIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (Assembly assembly in Assemblies())
            {
                Type pluginType = assembly.GetTypes().Single(type => type.GetCustomAttribute<BepInPlugin>() != null);
                BepInPlugin attribute = pluginType.GetCustomAttribute<BepInPlugin>();
                string assemblyName = assembly.GetName().Name;
                Require(guids.Add(attribute.GUID), "Duplicate BepInEx GUID " + attribute.GUID);
                Require(ExpectedPluginGuids.TryGetValue(assemblyName, out string expectedGuid) &&
                        string.Equals(attribute.GUID, expectedGuid, StringComparison.Ordinal),
                    assemblyName + " BepInEx GUID drifted.");
                Require(ExpectedGameplayVersions.TryGetValue(assemblyName, out string expectedVersion) &&
                        string.Equals(attribute.Version.ToString(), expectedVersion, StringComparison.Ordinal),
                    pluginType.Name + " version drifted.");
                FieldInfo moduleField = pluginType.GetField("ModuleId", BindingFlags.Public | BindingFlags.Static);
                Require(moduleField != null, pluginType.FullName + " does not publish ModuleId.");
                Require(moduleIds.Add((string)moduleField.GetValue(null)), "Duplicate Runic module ID.");
            }
        }

        private static void TransactionDependencyFloor()
        {
            foreach (Assembly assembly in Assemblies())
            {
                string assemblyName = assembly.GetName().Name;
                Type pluginType = assembly.GetTypes().Single(type => type.GetCustomAttribute<BepInPlugin>() != null);
                BepInDependency dependency = pluginType.GetCustomAttributes<BepInDependency>()
                    .SingleOrDefault(item => string.Equals(
                        item.DependencyGUID,
                        "chazman.RunicTransactions",
                        StringComparison.Ordinal));
                Require(dependency != null, assemblyName + " has no hard Transactions dependency.");
                const string expected = "1.0.0";
                Require(string.Equals(
                        dependency.MinimumVersion.ToString(), expected, StringComparison.Ordinal),
                    assemblyName + " Transactions floor drifted; expected " + expected + ".");
            }
        }

        private static void PluginFoundationDependencies()
        {
            foreach (Assembly assembly in Assemblies())
            {
                string assemblyName = assembly.GetName().Name;
                Type pluginType = assembly.GetTypes().Single(type => type.GetCustomAttribute<BepInPlugin>() != null);
                string[] actual = pluginType.GetCustomAttributes<BepInDependency>()
                    .Select(item => item.DependencyGUID + "@" + item.MinimumVersion)
                    .OrderBy(item => item, StringComparer.Ordinal)
                    .ToArray();
                string[] wanted = ExpectedFoundationDependencies[assemblyName]
                    .OrderBy(item => item, StringComparer.Ordinal)
                    .ToArray();
                Require(actual.SequenceEqual(wanted, StringComparer.Ordinal),
                    assemblyName + " foundation dependency attributes drifted: " +
                    string.Join(",", actual));
            }
        }

        private static void ManifestDependencyGraph()
        {
            string root = FindRepositoryRoot();
            foreach (string module in GameplayAssemblyNames)
            {
                string manifestPath = Path.Combine(root, module, "manifest.json");
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifestPath));
                JsonElement rootElement = document.RootElement;
                Require(string.Equals(rootElement.GetProperty("name").GetString(), module, StringComparison.Ordinal), module + " manifest name drifted.");
                Require(string.Equals(
                        rootElement.GetProperty("version_number").GetString(),
                        ExpectedGameplayVersions[module],
                        StringComparison.Ordinal),
                    module + " manifest version drifted.");
                string[] dependencies = rootElement.GetProperty("dependencies")
                    .EnumerateArray()
                    .Select(dependency => dependency.GetString() ?? string.Empty)
                    .ToArray();
                string[] expectedDependencies = ExpectedManifestDependencies[module];
                Require(dependencies.SequenceEqual(expectedDependencies, StringComparer.Ordinal),
                    module + " manifest dependency set/order drifted: " +
                    string.Join(",", dependencies));
                foreach (string value in dependencies)
                {
                    foreach (string gameplay in GameplayAssemblyNames)
                        Require(value.IndexOf(gameplay, StringComparison.OrdinalIgnoreCase) < 0, module + " hard-depends on gameplay module " + gameplay + ".");
                }
            }
        }

        private static void ReleaseMetadataLayers()
        {
            foreach (Assembly assembly in Assemblies())
            {
                string assemblyName = assembly.GetName().Name;
                string releaseVersion = ExpectedGameplayVersions[assemblyName];
                Require(assembly.GetName().Version?.ToString() == releaseVersion + ".0",
                    assemblyName + " AssemblyVersion drifted.");
                AssemblyFileVersionAttribute fileVersion =
                    assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
                Require(fileVersion != null &&
                        string.Equals(fileVersion.Version, releaseVersion + ".0", StringComparison.Ordinal),
                    assemblyName + " AssemblyFileVersion drifted.");
                AssemblyInformationalVersionAttribute informational =
                    assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                Require(informational != null &&
                        string.Equals(informational.InformationalVersion, releaseVersion, StringComparison.Ordinal),
                    assemblyName + " AssemblyInformationalVersion drifted.");
            }
        }

        private static void WaveBuilderPromotionContract()
        {
            string script = File.ReadAllText(Path.Combine(
                FindRepositoryRoot(), "Build-RunicGameplayWave1.ps1"));
            int promotionGuard = script.LastIndexOf(
                "if (-not $SkipTests) {", StringComparison.Ordinal);
            int archive = script.IndexOf(
                "Move-Item -LiteralPath $oldPackage.Original -Destination $archivePath",
                StringComparison.Ordinal);
            int promote = script.IndexOf(
                "Copy-Item -LiteralPath $package.Source -Destination $package.Destination",
                StringComparison.Ordinal);
            Require(promotionGuard >= 0 && archive > promotionGuard && promote > archive,
                "Wave1 promotion must archive under the non-SkipTests guard before copying new ZIPs.");
            Require(script.IndexOf("Get-CollisionSafeObsoletePath", StringComparison.Ordinal) >= 0 &&
                    script.IndexOf("[Guid]::NewGuid()", StringComparison.Ordinal) >= 0 &&
                    script.IndexOf("Test-Path -LiteralPath $candidate", StringComparison.Ordinal) >= 0,
                "Wave1 obsolete-package naming is not collision-safe.");
            Require(script.IndexOf(
                        "(Get-FileSha256Hex -Path $archivePath) -cne $oldPackage.Hash",
                        StringComparison.Ordinal) > archive,
                "Wave1 archive move is not byte-verified before promotion.");
            Require(script.IndexOf(
                        "Copy-Item -LiteralPath $oldPackage.Archive -Destination $oldPackage.Original -Force",
                        StringComparison.Ordinal) > promote,
                "Wave1 promotion failure no longer restores archived root packages.");
            Require(script.IndexOf("[System.IO.Path]::GetTempPath()", StringComparison.Ordinal) >= 0 &&
                    script.IndexOf(
                        "Validated all gameplay packages without promotion because -SkipTests was requested.",
                        StringComparison.Ordinal) > promote,
                "-SkipTests must validate from a temporary stage without artifact promotion.");
        }

        private static void TypedContainerProviderResolves()
        {
            var registry = new RunicRegistry();
            using ModuleRegistration module = registry.RegisterModule(new ModuleDescriptor(
                "runic.storage", "Storage Test Provider", TestProviderVersion, "1.0",
                new[] { RunicCapabilityIds.ContainerQuery }));
            var service = new FakeQueryService();
            using ServiceRegistration registration = registry.RegisterService<IContainerQueryService>(
                RunicCapabilityIds.ContainerQuery, "runic.storage", service);
            Require(registry.TryGetService<IContainerQueryService>(RunicCapabilityIds.ContainerQuery, out IContainerQueryService resolved), "Typed provider did not resolve.");
            Require(ReferenceEquals(service, resolved), "Registry returned a different provider instance.");
        }

        private static void CanonicalValheimIds()
        {
            PrincipalId player = ValheimIdentityIds.Player(42);
            Require(string.Equals(player.Value, "valheim.player:42", StringComparison.Ordinal), "Canonical player prefix drifted.");
            Require(ValheimIdentityIds.TryGetPlayerId(player, out long value) && value == 42, "Canonical player ID did not round-trip.");
            Require(string.Equals(ValheimIdentityIds.ZdoEndpoint("1:2").Value, "valheim.zdo:1:2", StringComparison.Ordinal), "Canonical ZDO prefix drifted.");
        }

        private static void CapabilityContractsAreDistinct()
        {
            Require(RunicIdentifier.IsValid(CraftingCapabilityIds.MaterialConsumeExact),
                "Crafting's exact-consume capability ID is invalid.");
            Require(!string.Equals(
                    CraftingCapabilityIds.MaterialConsumeExact,
                    RunicCapabilityIds.MaterialsReserve,
                    StringComparison.Ordinal),
                "Crafting must not advertise a final consume API as the foundation reservation contract.");
            Require(!string.Equals(
                    CraftingCapabilityIds.MaterialConsumeExact,
                    RunicCapabilityIds.MaterialsConsume,
                    StringComparison.Ordinal),
                "Crafting's typed final-consume service must not collide with the coordinator factory contract.");
        }

        private static void ProductionCapabilityProtocol()
        {
            Require(string.Equals(
                    ProductionCapabilityIds.ExplicitLinks,
                    "production.explicit-links",
                    StringComparison.Ordinal),
                "Production's explicit-link capability ID drifted.");
            Require(string.Equals(
                    ProductionCapabilityIds.ProtocolVersion,
                    "1.3",
                    StringComparison.Ordinal),
                "Production must advertise protocol 1.3 for stable world-object identities.");
        }

        private static void StorageCapabilityBoundary()
        {
            Require(StorageCapabilityIds.Published.Contains(
                    RunicCapabilityIds.ContainerQuery,
                    StringComparer.Ordinal),
                "Storage must publish its bounded query provider.");
            Require(!StorageCapabilityIds.Published.Contains(
                    RunicCapabilityIds.ContainerTransfer,
                    StringComparer.Ordinal),
                "Storage must not publish container.transfer before durable crash reconciliation exists.");
            Require(typeof(RunicStorage.Plugin).GetProperty(
                    "TransferService",
                    BindingFlags.Public | BindingFlags.Static) == null,
                "Storage's deferred transfer adapter must not remain a public bypass around the registry boundary.");
        }

        private static void DefaultKeyboardChordsAreUnique()
        {
            string root = FindRepositoryRoot();
            var owners = new Dictionary<string, string>(StringComparer.Ordinal);
            var expression = new Regex(
                @"new\s+KeyboardShortcut\s*\(\s*KeyCode\.(\w+)\s*,\s*KeyCode\.(\w+)\s*\)",
                RegexOptions.CultureInvariant);
            foreach (string module in GameplayAssemblyNames)
            {
                foreach (string file in Directory.EnumerateFiles(
                             Path.Combine(root, module), "*.cs", SearchOption.AllDirectories))
                {
                    if (file.IndexOf(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    string source = File.ReadAllText(file);
                    foreach (Match match in expression.Matches(source))
                    {
                        string chord = match.Groups[2].Value + "+" + match.Groups[1].Value;
                        string owner = module + "/" + Path.GetFileName(file);
                        Require(!owners.TryGetValue(chord, out string existing),
                            "Default chord " + chord + " is shared by " + existing + " and " + owner + ".");
                        owners.Add(chord, owner);
                    }
                }
            }
            Require(owners.Count >= 9, "The default-key audit found too few gameplay bindings.");
        }

        private static void ControllerContracts()
        {
            const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;
            Require(typeof(ZInput).GetMethod("GetButton", PublicStatic, null,
                        new[] { typeof(string) }, null) != null,
                "Valheim's controller held-action API drifted.");
            Require(typeof(ZInput).GetMethod("GetButtonDown", PublicStatic, null,
                        new[] { typeof(string) }, null) != null,
                "Valheim's controller edge-action API drifted.");
            Require(typeof(ZInput).GetMethod("ResetButtonStatus", PublicStatic, null,
                        new[] { typeof(string) }, null) != null,
                "Valheim's controller input-consumption API drifted.");
            Require(typeof(ZInput).GetMethod("GetButtonDef", BindingFlags.Public | BindingFlags.Instance,
                        null, new[] { typeof(string) }, null) != null,
                "Valheim's controller action-discovery API drifted.");

            var agriculture = new AgricultureControllerBindings(
                ValheimControllerAction.JoyAltKeys,
                ValheimControllerAction.JoyPlace,
                ValheimControllerAction.JoyPrevSnap,
                ValheimControllerAction.JoyUse);
            Require(agriculture.TryValidate(out string problem),
                "Agriculture's default controller bindings are invalid: " + problem);

            string storageConfig = File.ReadAllText(Path.Combine(
                FindRepositoryRoot(), "RunicStorage", "PluginConfig.cs"));
            string[] storageActions =
            {
                "JoyDPadDown", "JoyDPadUp", "JoyRStick", "JoyDPadLeft", "JoyDPadRight"
            };
            foreach (string action in storageActions)
                Require(storageConfig.IndexOf('"' + action + '"', StringComparison.Ordinal) >= 0,
                    "Storage's audited default controller action is missing: " + action);
            Require(storageConfig.IndexOf("\"JoyAltKeys\"", StringComparison.Ordinal) >= 0,
                "Storage's audited controller modifier is missing.");

            var chords = new HashSet<string>(StringComparer.Ordinal);
            foreach (string action in storageActions)
                Require(chords.Add("JoyAltKeys+" + action),
                    "Storage defines a duplicate default controller chord.");
            Require(chords.Add("JoyAltKeys+JoyPlace"),
                "Agriculture confirm collides with a Storage controller chord.");
            Require(chords.Add("JoyAltKeys+JoyPrevSnap"),
                "Agriculture cycle collides with a Storage controller chord.");
            Require(chords.Add("JoyAltKeys+JoyUse"),
                "Agriculture harvest collides with a Storage controller chord.");
        }

        private static void StorageControllerSessionBoundary()
        {
            Assembly storage = typeof(RunicStorage.Plugin).Assembly;
            Type outcomeType = storage.GetType("RunicStorage.Engine.StorageRouteOutcome", throwOnError: true);
            Type policyType = storage.GetType("RunicStorage.Engine.ControllerChordSessionPolicy", throwOnError: true);
            MethodInfo decide = policyType.GetMethod(
                "Decide",
                BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(bool), outcomeType },
                null);
            Require(decide != null, "Storage controller-session policy API is missing.");

            object execute = Enum.Parse(outcomeType, "Execute");
            object blocked = Enum.Parse(outcomeType, "Blocked");
            Require(decide.Invoke(null, new[] { (object)false, execute }).ToString() == "ExecuteAndConsume",
                "Storage must consume an executable first controller chord.");
            Require(decide.Invoke(null, new[] { (object)true, execute }).ToString() == "ExecuteAndConsume",
                "Storage must consume an executable chained controller chord.");
            Require(decide.Invoke(null, new[] { (object)true, blocked }).ToString() == "ReportAndConsume",
                "Storage must fail closed for a blocked chord inside an owned modifier session.");
            Require(decide.Invoke(null, new[] { (object)false, blocked }).ToString() == "ReportWithoutConsume",
                "Storage must leave a first blocked controller chord vanilla-owned.");

            Type pathsType = storage.GetType("RunicStorage.Engine.ControllerSessionPaths", throwOnError: true);
            ConstructorInfo constructor = pathsType.GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                Enumerable.Repeat(typeof(string), 6).ToArray(),
                null);
            MethodInfo contains = pathsType.GetMethod(
                "Contains",
                BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new[] { typeof(string) },
                null);
            Require(constructor != null && contains != null,
                "Storage controller-session path boundary is missing.");
            object paths = constructor.Invoke(new object[]
            {
                "JoyAltKeys", "JoyDPadDown", "JoyDPadUp", "JoyRStick", "JoyDPadLeft", "JoyDPadRight"
            });
            foreach (string owned in new[]
                     {
                         "JoyAltKeys", "JoyDPadDown", "JoyDPadUp", "JoyRStick", "JoyDPadLeft", "JoyDPadRight"
                     })
                Require((bool)contains.Invoke(paths, new object[] { owned }),
                    "Storage controller session failed to own configured path " + owned + ".");
            foreach (string agriculturePath in new[] { "JoyPlace", "JoyPrevSnap", "JoyUse" })
                Require(!(bool)contains.Invoke(paths, new object[] { agriculturePath }),
                    "Storage controller session incorrectly owns Agriculture path " + agriculturePath + ".");
        }

        private static void CraftingRequirementUiContextBoundary()
        {
            const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo selectedRecipe = typeof(InventoryGui).GetField("m_selectedRecipe", InstanceFields);
            Require(selectedRecipe != null,
                "Valheim's selected crafting-recipe field drifted.");
            FieldInfo recipeValue = selectedRecipe.FieldType.GetField(
                "<Recipe>k__BackingField",
                InstanceFields);
            Require(recipeValue != null && recipeValue.FieldType == typeof(Recipe),
                "Valheim's selected RecipeDataPair contract drifted.");

            Assembly crafting = typeof(RunicCrafting.Plugin).Assembly;
            Type listPatch = crafting.GetType(
                "RunicCrafting.Integration.InventoryGuiSetupRequirementListPatch",
                throwOnError: true);
            Require(listPatch.GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic) != null,
                "Crafting no longer captures selected-recipe context before requirement rendering.");
            Require(listPatch.GetMethod("Finalizer", BindingFlags.Static | BindingFlags.NonPublic) != null,
                "Crafting no longer clears selected-recipe context after requirement rendering.");

            Type rowPatch = crafting.GetType(
                "RunicCrafting.Integration.InventoryGuiSetupRequirementPatch",
                throwOnError: true);
            MethodInfo postfix = rowPatch.GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic);
            Require(postfix != null, "Crafting requirement-row postfix is missing.");
            Require(postfix.GetParameters().All(parameter =>
                    !string.Equals(parameter.Name, "___m_craftRecipe", StringComparison.Ordinal)),
                "Crafting requirement rows regressed to Valheim's transient active-craft field.");

            Type piecePatch = crafting.GetType(
                "RunicCrafting.Integration.HudSetupPieceInfoPatch",
                throwOnError: true);
            MethodInfo piecePrefix = piecePatch.GetMethod(
                "Prefix",
                BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo pieceFinalizer = piecePatch.GetMethod(
                "Finalizer",
                BindingFlags.Static | BindingFlags.NonPublic);
            Require(piecePrefix != null && pieceFinalizer != null,
                "Crafting no longer scopes hammer build-cost rows to Hud.SetupPieceInfo.");
            Require(piecePrefix.GetParameters().Any(parameter => parameter.ParameterType == typeof(Piece)),
                "Crafting's hammer-row context no longer captures the selected Piece.");
        }

        private static void StorageQueryCoverageBoundary()
        {
            Assembly storage = typeof(RunicStorage.Plugin).Assembly;
            Type coverageType = storage.GetType(
                "RunicStorage.Engine.ContainerQueryCoverage",
                throwOnError: true);
            MethodInfo isTruncated = coverageType.GetMethod(
                "IsTruncated",
                BindingFlags.Static | BindingFlags.NonPublic,
                null,
                new[] { typeof(float), typeof(float), typeof(bool) },
                null);
            Require(isTruncated != null,
                "Storage's query-coverage boundary is missing.");
            Require((bool)isTruncated.Invoke(null, new object[] { 35f, 20f, false }),
                "Storage must report an effective-radius clamp as truncated.");
            Require(!(bool)isTruncated.Invoke(null, new object[] { 20f, 20f, false }),
                "Storage must not report exact complete radius coverage as truncated.");
        }

        private static void CraftingStationlessScopeBoundary()
        {
            Assembly crafting = typeof(RunicCrafting.Plugin).Assembly;
            Type scopeType = crafting.GetType(
                "RunicCrafting.Domain.BuildMaterialQueryScope",
                throwOnError: true);
            Type policyType = crafting.GetType(
                "RunicCrafting.Domain.BuildMaterialQueryScopePolicy",
                throwOnError: true);
            MethodInfo resolve = policyType.GetMethod(
                "Resolve",
                BindingFlags.Static | BindingFlags.NonPublic,
                null,
                new[] { typeof(bool), typeof(bool) },
                null);
            Require(resolve != null, "Crafting's stationless query-scope policy is missing.");
            Require(resolve.Invoke(null, new object[] { false, false }).ToString() == "PlayerLocalStationless",
                "A genuinely stationless Piece must use the bounded player-local source scope.");
            Require(resolve.Invoke(null, new object[] { true, false }).ToString() == "RequiredStationUnavailable",
                "A Piece whose required station is missing must never fall into stationless nearby building.");
            Require(resolve.Invoke(null, new object[] { true, true }).ToString() == "StationLocal",
                "A resolved required station must retain station-local source and permission semantics.");

            Type stationlessPolicy = crafting.GetType(
                "RunicCrafting.Domain.StationlessPiecePolicy",
                throwOnError: true);
            MethodInfo defaults = stationlessPolicy.GetMethod(
                "VanillaDefaults",
                BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo evaluate = stationlessPolicy.GetMethod(
                "Evaluate",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(string) },
                null);
            Require(defaults != null && evaluate != null,
                "Crafting's configurable stationless prefab policy is missing.");
            object policy = defaults.Invoke(null, null);
            object firePit = evaluate.Invoke(policy, new object[] { "fire_pit(Clone)" });
            object cookingRack = evaluate.Invoke(policy, new object[] { "PIECE_COOKINGSTATION" });
            object feast = evaluate.Invoke(policy, new object[] { "FeastMistlands" });
            PropertyInfo allowed = firePit.GetType().GetProperty(
                "Allowed",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Require(allowed != null && (bool)allowed.GetValue(firePit),
                "The default stationless policy must include the vanilla fire pit.");
            Require((bool)allowed.GetValue(cookingRack),
                "The default stationless policy must include the vanilla basic cooking station.");
            Require(!(bool)allowed.GetValue(feast),
                "Special Feast placement must remain explicit opt-in rather than a default progression bypass.");
        }

        private static void CraftingPlacementInstantiationBoundary()
        {
            MethodInfo placePiece = typeof(Player).GetMethod(
                "PlacePiece",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(Piece), typeof(UnityEngine.Vector3), typeof(UnityEngine.Quaternion), typeof(bool) },
                null);
            Require(placePiece != null,
                "Valheim's exact Player.PlacePiece placement-output boundary drifted.");
            byte[] installedIl = placePiece.GetMethodBody()?.GetILAsByteArray();
            Require(installedIl != null && HasExactPlacementInstantiateBoundary(placePiece.Module, installedIl),
                "Valheim's exact Player.PlacePiece Instantiate -> stloc.0 boundary drifted.");

            Assembly crafting = typeof(RunicCrafting.Plugin).Assembly;
            Type patch = crafting.GetType(
                "RunicCrafting.Integration.PlayerPlacePieceInstantiationPatch",
                throwOnError: true);
            MethodInfo transpiler = patch.GetMethod(
                "Transpiler",
                BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo callback = crafting.GetType(
                    "RunicCrafting.Integration.CraftingRuntime",
                    throwOnError: true)
                .GetMethod(
                    "PlacementObjectInstantiated",
                    BindingFlags.Static | BindingFlags.NonPublic);
            Require(transpiler != null && callback != null,
                "Crafting's exact placement-instantiation hook is missing.");
            MethodInfo instantiate = typeof(UnityEngine.Object)
                .GetMethods(BindingFlags.Static | BindingFlags.Public)
                .Where(method => method.IsGenericMethodDefinition &&
                                 method.Name == nameof(UnityEngine.Object.Instantiate))
                .Single(method => method.GetParameters().Select(parameter => parameter.ParameterType)
                    .SequenceEqual(new[] { method.GetGenericArguments()[0], typeof(UnityEngine.Vector3), typeof(UnityEngine.Quaternion) }))
                .MakeGenericMethod(typeof(UnityEngine.GameObject));
            var original = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Call, instantiate),
                new CodeInstruction(OpCodes.Stloc_0)
            };
            var rewritten = ((IEnumerable<CodeInstruction>)transpiler.Invoke(
                    null,
                    new object[] { original }))
                .ToList();
            int callbackIndex = rewritten.FindIndex(instruction =>
                instruction.opcode == OpCodes.Call && Equals(instruction.operand, callback));
            Require(callbackIndex >= 3,
                "Crafting did not inject its output-created callback after Valheim's placement Instantiate.");
            Require(rewritten[callbackIndex - 3].opcode == OpCodes.Dup &&
                    rewritten[callbackIndex - 2].opcode == OpCodes.Ldarg_0 &&
                    rewritten[callbackIndex - 1].opcode == OpCodes.Ldarg_1 &&
                    callbackIndex + 1 < rewritten.Count &&
                    rewritten[callbackIndex + 1].opcode == OpCodes.Stloc_0,
                "Crafting's placement callback no longer preserves the exact Instantiate -> local boundary.");
            Require(rewritten.Count(instruction =>
                    instruction.opcode == OpCodes.Call && Equals(instruction.operand, callback)) == 1,
                "Crafting's placement output hook must be injected exactly once.");
        }

        private static bool HasExactPlacementInstantiateBoundary(Module module, byte[] il)
        {
            for (int index = 0; index + 5 < il.Length; index++)
            {
                if (il[index] != unchecked((byte)OpCodes.Call.Value) ||
                    il[index + 5] != unchecked((byte)OpCodes.Stloc_0.Value)) continue;
                try
                {
                    MethodInfo method = module.ResolveMethod(BitConverter.ToInt32(il, index + 1)) as MethodInfo;
                    if (method == null || !method.IsGenericMethod ||
                        method.DeclaringType != typeof(UnityEngine.Object) ||
                        method.Name != nameof(UnityEngine.Object.Instantiate) ||
                        method.ReturnType != typeof(UnityEngine.GameObject) ||
                        method.GetGenericArguments().Length != 1 ||
                        method.GetGenericArguments()[0] != typeof(UnityEngine.GameObject)) continue;
                    Type[] parameters = method.GetParameters()
                        .Select(parameter => parameter.ParameterType)
                        .ToArray();
                    if (parameters.SequenceEqual(new[]
                            { typeof(UnityEngine.GameObject), typeof(UnityEngine.Vector3), typeof(UnityEngine.Quaternion) }))
                        return true;
                }
                catch (ArgumentException)
                {
                    // An operand byte can resemble a call opcode; only valid metadata tokens count.
                }
            }
            return false;
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "RunicStorage")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "RunicCrafting"))) return directory.FullName;
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException("Could not locate ChazmanModsRepo from test output.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class FakeQueryService : IContainerQueryService
        {
            public ContainerQueryResult Query(ContainerQueryRequest request) =>
                new ContainerQueryResult(ContainerQueryStatus.Succeeded, null, 0, false, "ok");
        }
    }
}

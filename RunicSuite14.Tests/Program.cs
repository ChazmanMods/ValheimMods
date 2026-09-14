using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mono.Cecil;

namespace RunicSuite14.Tests
{
    internal static class Program
    {
        private static readonly ModuleSpec[] Modules =
        {
            new ModuleSpec("Storage", "RunicStorage", "RunicStorage", "chazman.RunicStorage", "icon.png"),
            new ModuleSpec("Crafting", "RunicCrafting", "RunicCrafting", "chazman.RunicCrafting", "icon.png"),
            new ModuleSpec("Agriculture", "RunicAgriculture", "RunicAgriculture", "chazman.RunicAgriculture", "icon.png"),
            new ModuleSpec("Production", "RunicProduction", "RunicProduction", "chazman.RunicProduction", "icon.png"),
            new ModuleSpec("Building", "RunicPrecisionBuildTool", "RunicPrecisionBuildTool", "chazman.RunicPrecisionBuildTool", "media/icon.png"),
            new ModuleSpec("Inventory", "RunicInventory", "RunicInventory", "chazman.RunicInventory", "icon.png"),
            new ModuleSpec("Portals", "RunicPortals", "RunicPortals", "chazman.RunicPortals", "icon.png"),
            new ModuleSpec("Exploration", "RunicExploration", "RunicExploration", "chazman.RunicExploration", "icon.png"),
            new ModuleSpec("Awareness", "RunicAwareness", "RunicAwareness", "chazman.RunicAwareness", "icon.png"),
            new ModuleSpec("Interaction", "RunicInteraction", "RunicInteraction", "chazman.RunicInteraction", "icon.png"),
            new ModuleSpec("Safety", "RunicSafety", "RunicSafety", "chazman.RunicSafety", "icon.png"),
            new ModuleSpec("Velocity", "RunicVelocity", "RunicVelocity", "chazman.RunicVelocity", "icon.png"),
            new ModuleSpec("Sentinel", "RunicSentinel", "RunicSentinel", "chazman.RunicSentinel", "icon.png"),
            new ModuleSpec("World Engine", "RunicWorldEngine", "RunicWorldEngine", "chazman.RunicWorldEngine", "icon.png"),
            new ModuleSpec("Build Camera", "RunicBuildCamera", "RunicBuildCamera", "chazman.RunicBuildCamera", "icon.png"),
            new ModuleSpec(
                "Display Stands",
                @"..\StandaloneItemStands",
                "RunicDisplayStands",
                "chazman.RunicDisplayStands",
                "icon.png",
                requiresConfigExample: false)
        };

        private static readonly ModuleSpec[] CompatibilityParticipants = Modules;

        private static readonly Dictionary<string, DynamicTargetSpec> ApprovedDynamicTargets =
            new Dictionary<string, DynamicTargetSpec>(StringComparer.Ordinal)
            {
                ["RunicBuildCamera.Integration.GameCameraRemoteEffectsPatch"] =
                    new DynamicTargetSpec("GameCamera", "assembly_valheim", "UpdateCamera", "System.Single"),
                ["RunicBuildCamera.Integration.GameCameraUpdateCameraPatch"] =
                    new DynamicTargetSpec("GameCamera", "assembly_valheim", "UpdateCamera", "System.Single"),
                ["RunicBuildCamera.Integration.PlayerSetControlsPatch"] =
                    new DynamicTargetSpec(
                        "Player", "assembly_valheim", "SetControls", "UnityEngine.Vector3",
                        "System.Boolean", "System.Boolean", "System.Boolean", "System.Boolean", "System.Boolean",
                        "System.Boolean", "System.Boolean", "System.Boolean", "System.Boolean", "System.Boolean",
                        "System.Boolean"),
                ["RunicProduction.Integration.ProductionSetControlsInputPatch"] =
                    new DynamicTargetSpec(
                        "Player", "assembly_valheim", "SetControls", "UnityEngine.Vector3",
                        "System.Boolean", "System.Boolean", "System.Boolean", "System.Boolean", "System.Boolean",
                        "System.Boolean", "System.Boolean", "System.Boolean", "System.Boolean", "System.Boolean",
                        "System.Boolean"),
                ["RunicBuildCamera.Integration.PlayerUpdatePlacementRangePatch"] =
                    new DynamicTargetSpec(
                        "Player", "assembly_valheim", "UpdatePlacement", "System.Boolean", "System.Single"),
                ["RunicBuildCamera.Integration.PlayerUpdatePlacementGhostRangePatch"] =
                    new DynamicTargetSpec("Player", "assembly_valheim", "UpdatePlacementGhost", "System.Boolean"),
                ["RunicInventory.Integration.DurableAttackUseAmmoPatch"] =
                    new DynamicTargetSpec("Attack", "assembly_valheim", "UseAmmo", "ItemDrop/ItemData&")
            };

        private const string ExactDynamicTargetAttribute =
            "RunicInventory.Integration.DurableMutationTargetAttribute";

        private static readonly Dictionary<string, string> ApprovedOverlaps =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Container::Awake"] = "additive event-index registration",
                ["Container::OnDestroyed"] =
                    "the four gameplay indices clean up only when the original destruction remains eligible",
                ["Container::CheckForChanges"] =
                    "the four bounded standalone indices observe completed eligible native inventory changes",
                ["Smelter::Awake"] = "additive station registration",
                ["CookingStation::Awake"] = "additive station registration",
                ["CraftingStation::GetHoverText"] = "bounded hover composition/capture",
                ["CookingStation::GetHoverText"] = "bounded hover composition/capture",
                ["Fermenter::GetHoverText"] = "bounded hover composition/capture",
                ["GameCamera::UpdateMouseCapture"] =
                    "Sentinel alone may suppress native mouse recapture for its admin modal; Sentinel, Storage, and Portals then renew only the cursor lease owned by their currently open panel",
                ["Plant::GetHoverText"] = "bounded hover composition/capture",
                ["Beehive::GetHoverText"] = "bounded hover composition/capture",
                ["Player::UpdatePlacement"] =
                    "Sentinel gates its admin modal first, then Build Camera range, Precision transforms, and Crafting scope compose in declared order",
                ["Player::UpdatePlacementGhost"] = "Agriculture preview and Building final pose are independently gated",
                ["Player::Interact"] = "Production link selection and Agriculture harvest use exact disjoint targets",
                ["Player::SetLocalPlayer"] =
                    "Inventory topology rebinding and Build Camera session/effect cleanup are independent and idempotent",
                ["Player::SetControls"] =
                    "Build Camera isolates its remote-camera controls while Production zeroes only combat arguments owned by an accepted link gesture",
                ["Player::Update"] =
                    "Agriculture and Build Camera keep their existing scopes while Production samples only an exact station-targeted Alt link or Shift+Alt unlink gesture and never skips Player.Update",
                ["CookingStation::OnAddFuelSwitch"] =
                    "Safety protection precedes Inventory's independent locked-item denial",
                ["Fermenter::AddItem"] =
                    "Safety protection precedes Inventory's independent locked-item denial",
                ["Humanoid::EquipItem"] =
                    "Inventory lock denial, Interaction temporary-equipment bookkeeping, then Inventory role relocation",
                ["Humanoid::Pickup"] =
                    "Inventory topology/filter denial precedes Interaction's independent pickup-filter denial",
                ["Incinerator::OnIncinerate"] =
                    "Safety confirmation/protection precedes Inventory's independent locked-item denial",
                ["InventoryGui::DoCrafting"] =
                    "Inventory locked-upgrade denial precedes Crafting's exact material reservation/commit",
                ["InventoryGui::Hide"] =
                    "Interaction captures menu memory while Display Stands independently closes only its transient virtual-container bridge",
                ["InventoryGui::OnSelectedItem"] =
                    "Inventory locked-slot denial precedes Interaction's guarded transfer gesture",
                ["InventoryGui::RepairOneItem"] =
                    "Inventory establishes its bounded repair allowance before Crafting may replace one native repair action with repair-all",
                ["ItemStand::UseItem"] =
                    "Safety protection precedes Inventory's independent locked-item denial",
                ["Minimap::OnMapLeftClick"] =
                    "Exploration first vetoes clicks inside its owned panel; Portals then selects only an exact picker-owned marker while its modal session is active",
                ["Minimap::OnMapDblClick"] =
                    "Portals suppresses double-click map mutation only during its modal picker; Exploration independently vetoes input inside its owned panel",
                ["Minimap::OnMapMiddleClick"] =
                    "Portals suppresses middle-click map mutation only during its modal picker; Exploration independently vetoes input inside its owned panel",
                ["Minimap::RemovePinUnderPointer"] =
                    "Exploration first vetoes its panel region, then Portals suppresses native pin removal only for its active picker",
                ["Smelter::OnAddFuel"] =
                    "Safety protection precedes Inventory's independent locked-item denial",
                ["Smelter::OnAddOre"] =
                    "Safety protection precedes Inventory's independent locked-item denial",
                ["Switch::Interact"] =
                    "Display Stands makes its managed armor-stand interception decision before Interaction may open a hold-repeat scope; unmanaged switches remain native",
                ["Player::TakeInput"] =
                    "Sentinel may deny local gameplay input for its admin modal; Build Camera first captures its scoped native result, then Storage and Portals monotonically force false only for their own open panels",
                ["ZInput::GetKeyDown"] =
                    "Storage and Portals suppress Escape only while their own modal owns it, with Portals ordered after Storage for deterministic composition",
                ["ZInput::GetButton"] =
                    "Storage, Production, Portals, Agriculture, and Inventory run in a fixed chain; Portals additionally suppresses only the editor-owned attack or cancel action and its release latch",
                ["ZInput::GetButtonDown"] =
                    "Storage, Production, Portals, Agriculture, and Inventory run in a fixed chain; Portals additionally suppresses only the editor-owned attack or cancel edge and its release latch",
                ["ZInput::GetButtonUp"] =
                    "Storage, Portals, Agriculture, and Inventory run in a fixed chain and monotonically deny only their audited owned controller or modal paths",
                ["ZInput::GetButtonPressedTimer"] =
                    "Storage, Agriculture, and Inventory monotonically deny only their audited owned controller paths",
                ["ZInput::GetButtonLastPressedTimer"] =
                    "Storage, Agriculture, and Inventory monotonically deny only their audited owned controller paths"
            };

        // A rationale alone can silently bless a newly arriving participant. Pin the exact
        // module set for every accepted overlap so any new patch owner forces a fresh review.
        private static readonly Dictionary<string, string[]> ApprovedOverlapModules =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["Beehive::GetHoverText"] = new[] { "RunicAgriculture", "RunicAwareness" },
                ["Container::Awake"] = new[] { "RunicAgriculture", "RunicCrafting", "RunicProduction", "RunicStorage" },
                ["Container::CheckForChanges"] = new[] { "RunicAgriculture", "RunicCrafting", "RunicProduction", "RunicStorage" },
                ["Container::OnDestroyed"] = new[] { "RunicAgriculture", "RunicCrafting", "RunicProduction", "RunicStorage" },
                ["CookingStation::Awake"] = new[] { "RunicInteraction", "RunicProduction" },
                ["CookingStation::GetHoverText"] = new[] { "RunicAwareness", "RunicProduction" },
                ["CookingStation::OnAddFuelSwitch"] = new[] { "RunicInventory", "RunicSafety" },
                ["CraftingStation::GetHoverText"] = new[] { "RunicAwareness", "RunicProduction" },
                ["Fermenter::AddItem"] = new[] { "RunicInventory", "RunicSafety" },
                ["Fermenter::GetHoverText"] = new[] { "RunicAwareness", "RunicProduction" },
                ["GameCamera::UpdateMouseCapture"] = new[] { "RunicPortals", "RunicSentinel", "RunicStorage" },
                ["Humanoid::EquipItem"] = new[] { "RunicInteraction", "RunicInventory" },
                ["Humanoid::Pickup"] = new[] { "RunicInteraction", "RunicInventory" },
                ["Incinerator::OnIncinerate"] = new[] { "RunicInventory", "RunicSafety" },
                ["InventoryGui::DoCrafting"] = new[] { "RunicCrafting", "RunicInventory" },
                ["InventoryGui::Hide"] = new[] { "RunicDisplayStands", "RunicInteraction" },
                ["InventoryGui::OnSelectedItem"] = new[] { "RunicInteraction", "RunicInventory" },
                ["InventoryGui::RepairOneItem"] = new[] { "RunicCrafting", "RunicInventory" },
                ["ItemStand::UseItem"] = new[] { "RunicInventory", "RunicSafety" },
                ["Minimap::OnMapDblClick"] = new[] { "RunicExploration", "RunicPortals" },
                ["Minimap::OnMapLeftClick"] = new[] { "RunicExploration", "RunicPortals" },
                ["Minimap::OnMapMiddleClick"] = new[] { "RunicExploration", "RunicPortals" },
                ["Minimap::RemovePinUnderPointer"] = new[] { "RunicExploration", "RunicPortals" },
                ["Plant::GetHoverText"] = new[] { "RunicAgriculture", "RunicAwareness" },
                ["Player::Interact"] = new[] { "RunicAgriculture", "RunicProduction" },
                ["Player::SetLocalPlayer"] = new[] { "RunicBuildCamera", "RunicInventory" },
                ["Player::SetControls"] = new[] { "RunicBuildCamera", "RunicProduction" },
                ["Player::Update"] = new[] { "RunicAgriculture", "RunicBuildCamera", "RunicProduction" },
                ["Player::UpdatePlacement"] = new[] { "RunicBuildCamera", "RunicCrafting", "RunicPrecisionBuildTool", "RunicSentinel" },
                ["Player::UpdatePlacementGhost"] = new[] { "RunicAgriculture", "RunicBuildCamera", "RunicPrecisionBuildTool" },
                ["Smelter::Awake"] = new[] { "RunicInteraction", "RunicProduction" },
                ["Smelter::OnAddFuel"] = new[] { "RunicInventory", "RunicSafety" },
                ["Smelter::OnAddOre"] = new[] { "RunicInventory", "RunicSafety" },
                ["Switch::Interact"] = new[] { "RunicDisplayStands", "RunicInteraction" },
                ["Player::TakeInput"] = new[] { "RunicBuildCamera", "RunicPortals", "RunicSentinel", "RunicStorage" },
                ["ZInput::GetButton"] = new[] { "RunicAgriculture", "RunicInventory", "RunicPortals", "RunicProduction", "RunicStorage" },
                ["ZInput::GetButtonDown"] = new[] { "RunicAgriculture", "RunicInventory", "RunicPortals", "RunicProduction", "RunicStorage" },
                ["ZInput::GetButtonLastPressedTimer"] = new[] { "RunicAgriculture", "RunicInventory", "RunicStorage" },
                ["ZInput::GetButtonPressedTimer"] = new[] { "RunicAgriculture", "RunicInventory", "RunicStorage" },
                ["ZInput::GetButtonUp"] = new[] { "RunicAgriculture", "RunicInventory", "RunicPortals", "RunicStorage" },
                ["ZInput::GetKeyDown"] = new[] { "RunicPortals", "RunicStorage" }
            };

        private static readonly Dictionary<string, string> ApprovedInputOverlaps =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> PersistentWorldStateOwners =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "RunicCrafting",
                "RunicProduction",
                "RunicPortals",
                "RunicDisplayStands"
            };

        private static readonly Dictionary<string, HashSet<string>> ApprovedPersistentWriteMethods =
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
            {
                ["RunicStorage"] = new HashSet<string>(StringComparer.Ordinal)
                {
                    "RunicStorage.Runtime.RemoteStorageZdoMutationCodec::Publish"
                },
                ["RunicInventory"] = new HashSet<string>(StringComparer.Ordinal)
                {
                    "RunicInventory.Integration.InventoryDurableOperationRuntime::PrepareTombstoneCustodyMarker"
                },
                ["RunicAgriculture"] = new HashSet<string>(StringComparer.Ordinal)
                {
                    "RunicAgriculture.Integration.AgricultureWorldObjectMetadata::TryPublishExact"
                },
                ["RunicPrecisionBuildTool"] = new HashSet<string>(StringComparer.Ordinal)
                {
                    "QuietBuildRotation.Integration.PrecisionPersistentZdoPublisher::TryPublish"
                }
            };

        private static int Main()
        {
            var tests = new (string Name, Action Run)[]
            {
                ("the exact sixteen canonical Runic release identities exist", ExactSixteenExist),
                ("every module has a test project and complete release surface", ReleaseSurfacesAreComplete),
                ("client server BepInEx Harmony and Cecil binaries match the audited environment", EnvironmentBinariesArePinned),
                ("plugin manifest assembly and package versions align", IdentitiesAlign),
                ("all sixteen GUIDs assemblies and package names are unique", IdentitiesAreUnique),
                ("active hard module references are restricted to Sentinel Safety", NoGameplayHardReferences),
                ("optional peer discovery references only public contract namespaces", PrivatePeerReflectionIsAbsent),
                ("gameplay manifest hard dependencies are explicit and acyclic", GameplayManifestDependenciesAreExplicit),
                ("compiled and packaged active dependency versions align", ActiveDependencyVersionsAlign),
                ("retired Foundation package references are absent", RetiredFoundationReferencesAreAbsent),
                ("standalone integration contracts have exact public owners", StandaloneIntegrationContractsAlign),
                ("standalone RPC surfaces are sender-bound bounded and unique", StandaloneRpcContractsAreBounded),
                ("direct persistent ZDO writes stay with declared single-purpose owners", PersistentWritesHaveOwners),
                ("all sixteen package icons are unique exact 256x256 PNG", IconsAreExact),
                ("every static Harmony target resolves exactly on installed Valheim", HarmonyTargetsResolveExactly),
                ("gameplay modules do not manually patch private peer implementations", ManualPeerPatchesAreAbsent),
                ("cross-module Harmony overlaps are explicitly classified", PatchOverlapsAreClassified),
                ("overlapping prefixes that can skip vanilla are explicitly ordered", SkippingPrefixOverlapsAreOrdered),
                ("cross-module result observers run after peer hover composers", ResultObserversAreOrdered),
                ("no shared target has multiple transpilers", TranspilersDoNotCompete),
                ("default keyboard and controller action chords do not collide", DefaultInputChordsDoNotCollide),
                ("every module documents bounded behavior and authority limits", DocumentationHasBoundaries)
            };
            int failed = 0;
            foreach ((string name, Action run) in tests)
            {
                try { run(); Console.WriteLine("PASS " + name); }
                catch (Exception exception) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + exception.Message); }
            }
            Console.WriteLine($"{tests.Length - failed}/{tests.Length} canonical-suite audits passed.");
            return failed == 0 ? 0 : 1;
        }

        private static void ExactSixteenExist()
        {
            Equal(16, Modules.Length);
            foreach (ModuleSpec module in Modules)
            {
                string directory = PathOf(module.Directory);
                True(Directory.Exists(directory), "Missing module directory: " + module.Directory);
                True(File.Exists(Path.Combine(directory, module.Assembly + ".csproj")), "Missing project: " + module.Assembly);
                True(File.Exists(Path.Combine(directory, "manifest.json")), "Missing manifest: " + module.Directory);
                True(File.Exists(Path.Combine(directory, "README.md")), "Missing README: " + module.Directory);
            }
        }

        private static void IdentitiesAlign()
        {
            foreach (ModuleSpec module in Modules)
            {
                string assemblyPath = AssemblyPath(module);
                True(File.Exists(assemblyPath), "Build Release first: " + module.Assembly);
                using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
                TypeDefinition plugin = assembly.MainModule.Types.Single(type =>
                    type.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == "BepInEx.BepInPlugin"));
                CustomAttribute identity = plugin.CustomAttributes.Single(attribute =>
                    attribute.AttributeType.FullName == "BepInEx.BepInPlugin");
                string guid = (string)identity.ConstructorArguments[0].Value;
                string version = (string)identity.ConstructorArguments[2].Value;
                Equal(module.Guid, guid);
                using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(PathOf(module.Directory, "manifest.json")));
                Equal(module.Assembly, manifest.RootElement.GetProperty("name").GetString());
                Equal(version, manifest.RootElement.GetProperty("version_number").GetString());
                Version semantic = new Version(version);
                Version binary = assembly.Name.Version;
                Equal(semantic.Major, binary.Major);
                Equal(semantic.Minor, binary.Minor);
                Equal(Math.Max(0, semantic.Build), binary.Build);
                Equal(0, binary.Revision);

                string fileVersion = AssemblyAttribute(
                    assembly,
                    "System.Reflection.AssemblyFileVersionAttribute");
                Equal(version + ".0", fileVersion);
                Equal(
                    version,
                    AssemblyAttribute(
                        assembly,
                        "System.Reflection.AssemblyInformationalVersionAttribute"));
                string product = AssemblyAttribute(
                    assembly,
                    "System.Reflection.AssemblyProductAttribute");
                True(!string.IsNullOrWhiteSpace(product),
                    module.Assembly + " has no assembly product identity.");
            }
        }

        private static void ReleaseSurfacesAreComplete()
        {
            foreach (ModuleSpec module in Modules)
            {
                string directory = PathOf(module.Directory);
                string siblingTests = PathOf(module.Assembly + ".Tests", module.Assembly + ".Tests.csproj");
                string nestedTestsDirectory = Path.Combine(directory, "Tests");
                bool hasNestedTests = Directory.Exists(nestedTestsDirectory) &&
                                      Directory.GetFiles(nestedTestsDirectory, "*.csproj", SearchOption.TopDirectoryOnly)
                                          .Length == 1;
                True(File.Exists(siblingTests) || hasNestedTests,
                    "Missing focused test project: " + module.Assembly + ".Tests");
                True(File.Exists(Path.Combine(directory, "CHANGELOG.md")),
                    "Missing changelog: " + module.Assembly);
                string[] examples = Directory.GetFiles(directory, "*.cfg.example", SearchOption.TopDirectoryOnly);
                Equal(module.RequiresConfigExample ? 1 : 0, examples.Length);

                using JsonDocument manifest = JsonDocument.Parse(
                    File.ReadAllText(Path.Combine(directory, "manifest.json")));
                string[] dependencies = manifest.RootElement.GetProperty("dependencies")
                    .EnumerateArray().Select(value => value.GetString() ?? string.Empty).ToArray();
                True(dependencies.Length >= 1 && dependencies.Length <= 8,
                    module.Assembly + " dependency list is missing or unbounded.");
                Equal(dependencies.Length,
                    dependencies.Distinct(StringComparer.OrdinalIgnoreCase).Count());
                Equal(1, dependencies.Count(value => string.Equals(
                    value, "denikson-BepInExPack_Valheim-5.4.2350", StringComparison.Ordinal)));
            }
        }

        private static void EnvironmentBinariesArePinned()
        {
            string managed = FindManagedDirectory();
            string clientAssembly = Path.Combine(managed, "assembly_valheim.dll");
            Equal("A5130F5A957AB51CB6538F5412CBE57B43F927F4A679918BFF199B5C905D01BC",
                Sha256Hex(clientAssembly));
            using (AssemblyDefinition game = AssemblyDefinition.ReadAssembly(clientAssembly))
            {
                TypeDefinition version = game.MainModule.Types.Single(value => value.FullName == "Version");
                MethodDefinition initializer = version.Methods.Single(value => value.Name == ".cctor");
                IList<Mono.Cecil.Cil.Instruction> instructions = initializer.Body.Instructions;
                int currentStore = instructions.ToList().FindIndex(instruction =>
                    instruction.OpCode.Code == Mono.Cecil.Cil.Code.Stsfld &&
                    instruction.Operand is FieldReference field &&
                    string.Equals(field.Name, "<CurrentVersion>k__BackingField", StringComparison.Ordinal));
                True(currentStore >= 4 &&
                     ReadInt32Constant(instructions[currentStore - 4]) == 1 &&
                     ReadInt32Constant(instructions[currentStore - 3]) == 0 &&
                     ReadInt32Constant(instructions[currentStore - 2]) == 7 &&
                     instructions[currentStore - 1].OpCode.Code == Mono.Cecil.Cil.Code.Newobj &&
                     string.Equals(((MethodReference)instructions[currentStore - 1].Operand).DeclaringType.FullName,
                         "GameVersion", StringComparison.Ordinal),
                    "Installed client assembly does not identify audited Valheim 1.0.7.");
            }

            string core = FindBepInExCoreDirectory();
            string bepInEx = Path.Combine(core, "BepInEx.dll");
            string harmony = Path.Combine(core, "0Harmony.dll");
            string cecil = Path.Combine(core, "Mono.Cecil.dll");
            Equal("F09821B2A7B990C6F50C5EF23229635303CE675374B70EDC6F5A9B960CB818E3", Sha256Hex(bepInEx));
            Equal("1A21CC03424FC82C3DD1346905D16494536B9595AE4162228D99FB7C285C1031", Sha256Hex(harmony));
            Equal("7AE470288FFF4A402899C254D0A76CEFEF55877F5C54F96E83C797CC5BB6E2F6", Sha256Hex(cecil));
            Equal("5.4.23.5", System.Diagnostics.FileVersionInfo.GetVersionInfo(bepInEx).FileVersion);
            Equal("2.9.0.0", System.Diagnostics.FileVersionInfo.GetVersionInfo(harmony).FileVersion);

            string dedicatedRoot = Environment.GetEnvironmentVariable("VALHEIM_DEDICATED_INSTALL");
            if (string.IsNullOrWhiteSpace(dedicatedRoot))
                dedicatedRoot = @"E:\SteamLibrary\steamapps\common\Valheim dedicated server";
            string serverAssembly = Path.Combine(
                dedicatedRoot, "valheim_server_Data", "Managed", "assembly_valheim.dll");
            string serverExecutable = Path.Combine(dedicatedRoot, "valheim_server.exe");
            True(File.Exists(serverAssembly) && File.Exists(serverExecutable),
                "Audited dedicated-server install is unavailable.");
            Equal("9DF99B0011B4CA0A448E6D935C77368B4E3B98EEE7B0AC8D1B43B34E267471B2",
                Sha256Hex(serverAssembly));
            Equal("E01757027E08D35C5FC926ADFEEC164344B73EADB4D4C61196945642B787E4FD",
                Sha256Hex(serverExecutable));
        }

        private static void IdentitiesAreUnique()
        {
            Equal(16, Modules.Select(value => value.Guid).Distinct(StringComparer.Ordinal).Count());
            Equal(16, Modules.Select(value => value.Assembly).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            var packageNames = Modules.Select(module =>
            {
                using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(PathOf(module.Directory, "manifest.json")));
                return manifest.RootElement.GetProperty("name").GetString();
            }).ToArray();
            Equal(16, packageNames.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        private static void NoGameplayHardReferences()
        {
            var gameplay = new HashSet<string>(Modules.Select(value => value.Assembly), StringComparer.OrdinalIgnoreCase);
            foreach (ModuleSpec module in Modules)
            {
                using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(AssemblyPath(module));
                string[] forbidden = assembly.MainModule.AssemblyReferences.Select(value => value.Name)
                    .Where(value => gameplay.Contains(value))
                    .Where(value => !(string.Equals(module.Assembly, "RunicSentinel", StringComparison.Ordinal) &&
                                      string.Equals(value, "RunicSafety", StringComparison.Ordinal)))
                    .ToArray();
                True(forbidden.Length == 0,
                    module.Assembly + " has an unapproved hard reference to active peer(s): " +
                    string.Join(",", forbidden));
            }

            ModuleSpec sentinel = Modules.Single(value => value.Assembly == "RunicSentinel");
            using AssemblyDefinition sentinelAssembly = AssemblyDefinition.ReadAssembly(AssemblyPath(sentinel));
            Equal(1, sentinelAssembly.MainModule.AssemblyReferences.Count(value =>
                string.Equals(value.Name, "RunicSafety", StringComparison.Ordinal)));
        }

        private static void PrivatePeerReflectionIsAbsent()
        {
            string[] peerRoots = Modules.Select(module => module.Assembly + ".")
                .Distinct(StringComparer.Ordinal).ToArray();
            string[] privateSegments = { ".Runtime.", ".Integration.", ".Core.", ".Plugin", ".Configuration", ".Diagnostics" };
            foreach (ModuleSpec module in Modules)
            {
                using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(AssemblyPath(module));
                string[] forbidden = AllTypes(assembly.MainModule.Types)
                    .SelectMany(type => type.Methods)
                    .Where(method => method.HasBody)
                    .SelectMany(method => method.Body.Instructions)
                    .Where(instruction => instruction.OpCode.Code == Mono.Cecil.Cil.Code.Ldstr)
                    .Select(instruction => instruction.Operand as string ?? string.Empty)
                    .Where(value => peerRoots.Any(root =>
                        !value.StartsWith(module.Assembly + ".", StringComparison.Ordinal) &&
                        value.StartsWith(root, StringComparison.Ordinal)))
                    .Where(value => privateSegments.Any(segment =>
                        value.IndexOf(segment, StringComparison.Ordinal) >= 0))
                    .Distinct(StringComparer.Ordinal).ToArray();
                True(forbidden.Length == 0,
                    module.Assembly + " reflects against private gameplay peer implementation(s): " +
                    string.Join(",", forbidden));
            }
        }

        private static void GameplayManifestDependenciesAreExplicit()
        {
            var gameplayPackages = new HashSet<string>(Modules.Select(value => "Chazman-" + value.Assembly), StringComparer.OrdinalIgnoreCase);
            const string sentinelSafety = "Chazman-RunicSafety-1.0.2";
            int sentinelSafetyCount = 0;
            foreach (ModuleSpec module in Modules)
            {
                using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(PathOf(module.Directory, "manifest.json")));
                foreach (JsonElement dependency in manifest.RootElement.GetProperty("dependencies").EnumerateArray())
                {
                    string value = dependency.GetString() ?? string.Empty;
                    if (!gameplayPackages.Any(package => value.StartsWith(package + "-", StringComparison.OrdinalIgnoreCase)))
                        continue;
                    bool exactSentinelSafety = string.Equals(module.Assembly, "RunicSentinel", StringComparison.Ordinal) &&
                                               string.Equals(value, sentinelSafety, StringComparison.Ordinal);
                    True(exactSentinelSafety,
                        module.Assembly + " has an undeclared gameplay dependency " + value + ".");
                    sentinelSafetyCount++;
                }
            }
            Equal(1, sentinelSafetyCount);

            using JsonDocument inventoryManifest = JsonDocument.Parse(
                File.ReadAllText(PathOf("RunicInventory", "manifest.json")));
            True(!inventoryManifest.RootElement.GetProperty("dependencies").EnumerateArray()
                    .Select(value => value.GetString() ?? string.Empty)
                    .Any(value => value.StartsWith("Chazman-RunicStorage-", StringComparison.OrdinalIgnoreCase)),
                "Storage -> Inventory must remain acyclic; Inventory may not depend on Storage.");
        }

        private static void ActiveDependencyVersionsAlign()
        {
            var byAssembly = Modules.ToDictionary(value => value.Assembly, StringComparer.OrdinalIgnoreCase);
            var byGuid = Modules.ToDictionary(value => value.Guid, StringComparer.Ordinal);
            var versions = Modules.ToDictionary(
                value => value.Assembly,
                value =>
                {
                    using JsonDocument manifest = JsonDocument.Parse(
                        File.ReadAllText(PathOf(value.Directory, "manifest.json")));
                    return manifest.RootElement.GetProperty("version_number").GetString() ?? string.Empty;
                },
                StringComparer.OrdinalIgnoreCase);
            using var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(FindBepInExCoreDirectory());
            resolver.AddSearchDirectory(FindManagedDirectory());
            foreach (ModuleSpec module in Modules)
                resolver.AddSearchDirectory(Path.GetDirectoryName(AssemblyPath(module)) ?? string.Empty);

            foreach (ModuleSpec module in Modules)
            {
                using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(
                    AssemblyPath(module),
                    new ReaderParameters { AssemblyResolver = resolver });
                TypeDefinition plugin = assembly.MainModule.Types.Single(type =>
                    type.CustomAttributes.Any(attribute =>
                        attribute.AttributeType.FullName == "BepInEx.BepInPlugin"));
                string[] referencedPeers = assembly.MainModule.AssemblyReferences
                    .Select(value => value.Name)
                    .Where(byAssembly.ContainsKey)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                using JsonDocument manifest = JsonDocument.Parse(
                    File.ReadAllText(PathOf(module.Directory, "manifest.json")));
                string[] packaged = manifest.RootElement.GetProperty("dependencies")
                    .EnumerateArray().Select(value => value.GetString() ?? string.Empty).ToArray();
                foreach (string peerAssembly in referencedPeers)
                {
                    ModuleSpec peer = byAssembly[peerAssembly];
                    string exactPackage = "Chazman-" + peer.Assembly + "-" + versions[peer.Assembly];
                    Equal(1, packaged.Count(value =>
                        string.Equals(value, exactPackage, StringComparison.Ordinal)));
                    Equal(1, plugin.CustomAttributes.Count(attribute =>
                        attribute.AttributeType.FullName == "BepInEx.BepInDependency" &&
                        attribute.ConstructorArguments.Count >= 1 &&
                        string.Equals(
                            attribute.ConstructorArguments[0].Value as string,
                            peer.Guid,
                            StringComparison.Ordinal)));
                }

                foreach (CustomAttribute dependency in plugin.CustomAttributes.Where(attribute =>
                             attribute.AttributeType.FullName == "BepInEx.BepInDependency" &&
                             attribute.ConstructorArguments.Count >= 1 &&
                             attribute.ConstructorArguments[0].Value is string guid &&
                             byGuid.ContainsKey(guid)))
                {
                    ModuleSpec peer = byGuid[(string)dependency.ConstructorArguments[0].Value];
                    True(referencedPeers.Contains(peer.Assembly, StringComparer.OrdinalIgnoreCase),
                        module.Assembly + " declares an active BepInDependency without a matching " +
                        "compiled reference: " + peer.Guid);
                }
            }
        }

        private static void RetiredFoundationReferencesAreAbsent()
        {
            string[] retiredAssemblies =
            {
                "RunicCore", "RunicPermissions", "RunicPersistence", "RunicTransactions", "RunicIntegrity"
            };
            string[] retiredPackages = retiredAssemblies.Select(value => "Chazman-" + value + "-").ToArray();
            foreach (ModuleSpec module in Modules)
            {
                using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(AssemblyPath(module));
                string[] references = assembly.MainModule.AssemblyReferences.Select(value => value.Name)
                    .Where(value => retiredAssemblies.Contains(value, StringComparer.OrdinalIgnoreCase))
                    .ToArray();
                True(references.Length == 0,
                    module.Assembly + " still references retired assembly package(s): " +
                    string.Join(",", references));
                using JsonDocument manifest = JsonDocument.Parse(
                    File.ReadAllText(PathOf(module.Directory, "manifest.json")));
                string[] packaged = manifest.RootElement.GetProperty("dependencies")
                    .EnumerateArray().Select(value => value.GetString() ?? string.Empty)
                    .Where(value => retiredPackages.Any(prefix =>
                        value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    .ToArray();
                True(packaged.Length == 0,
                    module.Assembly + " still packages retired dependency entry/entries: " +
                    string.Join(",", packaged));
            }
        }

        private static void StandaloneIntegrationContractsAlign()
        {
            using AssemblyDefinition inventory = AssemblyDefinition.ReadAssembly(
                AssemblyPath(Modules.Single(value => value.Assembly == "RunicInventory")));
            using AssemblyDefinition safety = AssemblyDefinition.ReadAssembly(
                AssemblyPath(Modules.Single(value => value.Assembly == "RunicSafety")));
            using AssemblyDefinition storage = AssemblyDefinition.ReadAssembly(
                AssemblyPath(Modules.Single(value => value.Assembly == "RunicStorage")));
            using AssemblyDefinition interaction = AssemblyDefinition.ReadAssembly(
                AssemblyPath(Modules.Single(value => value.Assembly == "RunicInteraction")));
            using AssemblyDefinition sentinel = AssemblyDefinition.ReadAssembly(
                AssemblyPath(Modules.Single(value => value.Assembly == "RunicSentinel")));
            using AssemblyDefinition portals = AssemblyDefinition.ReadAssembly(
                AssemblyPath(Modules.Single(value => value.Assembly == "RunicPortals")));

            Equal("inventory.topology", Constant(
                inventory, "RunicInventory.Capabilities.InventoryCapabilityIds", "Topology"));
            Equal("inventory.item-locks", Constant(
                inventory, "RunicInventory.Capabilities.InventoryCapabilityIds", "ItemLocks"));
            RequirePublicStaticMethod(
                inventory,
                "RunicInventory.Api.InventoryIntegrationApi",
                "TryGetProtection",
                "System.Boolean",
                "System.Object",
                "System.Int32&");

            foreach (string contract in new[]
                     {
                         "RunicSafety.Api.IProtectedItemPolicy",
                         "RunicSafety.Api.IMigrationBackupService",
                         "RunicSafety.Api.IContextualConfirmationService",
                         "RunicSafety.Api.ISafetyStatusService"
                     })
                RequirePublicContract(safety, contract);
            foreach (string getter in new[]
                     { "get_Protection", "get_Backups", "get_Confirmations", "get_Status" })
                RequirePublicStaticMethod(
                    safety,
                    "RunicSafety.Api.SafetyIntegrationApi",
                    getter,
                    null);

            const string inventoryApi = "RunicInventory.Api.InventoryIntegrationApi";
            foreach ((AssemblyDefinition Assembly, string Name) consumer in new[]
                     { (safety, "Safety"), (storage, "Storage"), (interaction, "Interaction") })
            {
                True(HasStringLiteral(consumer.Assembly, inventoryApi),
                    consumer.Name + " does not resolve the exact standalone Inventory API type.");
                True(HasStringLiteral(consumer.Assembly, "TryGetProtection"),
                    consumer.Name + " does not resolve the exact standalone protection method.");
                True(!consumer.Assembly.MainModule.AssemblyReferences.Any(reference =>
                        string.Equals(reference.Name, "RunicInventory", StringComparison.Ordinal)),
                    consumer.Name + " must keep Inventory integration optional.");
            }

            Equal("3.0", Constant(
                sentinel, "RunicSentinel.Contracts.SentinelCapabilityIds", "ProtocolVersion"));
            Equal("security.admission", Constant(
                sentinel, "RunicSentinel.Contracts.SentinelCapabilityIds", "Admission"));
            Equal("security.attest", Constant(
                sentinel, "RunicSentinel.Contracts.SentinelCapabilityIds", "Attestation"));
            RequirePublicStaticMethod(
                sentinel,
                "RunicSentinel.Api.SentinelIntegrationApi",
                "ReportRejectedServerRequest",
                "System.Boolean",
                "System.String",
                "System.Int64",
                "System.String",
                "System.String",
                "System.String",
                "System.Int32",
                "System.String");
            RequirePublicStaticMethod(
                portals,
                "RunicPortals.Api.GroupIntegrationApi",
                "TryIsMember",
                "System.Boolean",
                "System.String",
                "System.Int64",
                "System.Boolean&");

            foreach (ModuleSpec module in Modules)
            {
                using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(AssemblyPath(module));
                if (module.Assembly != "RunicInventory")
                    True(!AllTypes(assembly.MainModule.Types).Any(type =>
                            type.FullName == inventoryApi),
                        module.Assembly + " duplicates Inventory's integration API.");
                if (module.Assembly != "RunicSafety")
                    True(!AllTypes(assembly.MainModule.Types).Any(type =>
                            type.FullName == "RunicSafety.Api.SafetyIntegrationApi"),
                        module.Assembly + " duplicates Safety's integration API.");
            }
        }

        private static void StandaloneRpcContractsAreBounded()
        {
            var routedRegistrars = new List<string>();
            var directRegistrars = new List<string>();
            foreach (ModuleSpec module in Modules)
            {
                using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(AssemblyPath(module));
                if (ReferencesMethod(assembly, "ZRoutedRpc", "Register"))
                    routedRegistrars.Add(module.Assembly);
                if (ReferencesMethod(assembly, "ZRpc", "Register"))
                    directRegistrars.Add(module.Assembly);
            }
            Equal(
                "RunicDisplayStands,RunicPortals",
                string.Join(",", routedRegistrars.OrderBy(value => value, StringComparer.Ordinal)));
            Equal("RunicSentinel", string.Join(",", directRegistrars));

            using AssemblyDefinition portals = AssemblyDefinition.ReadAssembly(
                AssemblyPath(Modules.Single(value => value.Assembly == "RunicPortals")));
            using AssemblyDefinition sentinel = AssemblyDefinition.ReadAssembly(
                AssemblyPath(Modules.Single(value => value.Assembly == "RunicSentinel")));
            using AssemblyDefinition stands = AssemblyDefinition.ReadAssembly(
                AssemblyPath(Modules.Single(value => value.Assembly == "RunicDisplayStands")));
            var rpcNames = new HashSet<string>(StringComparer.Ordinal);

            foreach (string name in new[]
                     {
                         Constant(portals, "RunicPortals.Integration.PortalRuntime", "DirectoryRequestRpc"),
                         Constant(portals, "RunicPortals.Integration.PortalRuntime", "DirectoryResponseRpc"),
                         Constant(portals, "RunicPortals.Integration.PortalRuntime", "MapDirectoryRequestRpc"),
                         Constant(portals, "RunicPortals.Integration.PortalRuntime", "MapDirectoryResponseRpc"),
                         Constant(portals, "RunicPortals.Integration.PortalGroupRuntime", "RequestRpc"),
                         Constant(portals, "RunicPortals.Integration.PortalGroupRuntime", "ResponseRpc"),
                         Constant(sentinel, "RunicSentinel.Admission.AdmissionProtocolV2", "DirectRpcName"),
                         Constant(sentinel, "RunicSentinel.Runtime.SentinelAdminControl", "RequestRpc"),
                         Constant(sentinel, "RunicSentinel.Runtime.SentinelAdminControl", "ResponseRpc"),
                         Constant(stands, "RunicDisplayStands.ConfigSync", "RpcName")
                     })
            {
                True(!string.IsNullOrWhiteSpace(name), "An RPC contract name is empty.");
                True(rpcNames.Add(name), "Duplicate active RPC contract name: " + name);
            }

            Equal(2048, IntConstant(
                portals, "RunicPortals.Integration.PortalRuntime", "MaximumDirectoryEnvelopeBytes"));
            Equal(512, IntConstant(
                portals, "RunicPortals.Integration.PortalRuntime", "MaximumDirectoryEndpointsSent"));
            Equal(32768, IntConstant(
                portals, "RunicPortals.Integration.PortalGroupRuntime", "MaximumEnvelopeBytes"));
            Equal(32, IntConstant(
                portals, "RunicPortals.Integration.PortalGroupRuntime", "MaximumPending"));
            Equal(512, IntConstant(
                sentinel, "RunicSentinel.Admission.AdmissionProtocolV2", "MaximumPlugins"));
            Equal(256 * 1024, IntConstant(
                sentinel, "RunicSentinel.Admission.AdmissionProtocolV2", "MaximumFrameBytes"));
            Equal(64, IntConstant(
                sentinel, "RunicSentinel.Core.SentinelNetworkCompatibility", "MaximumTrackedConnections"));
            Equal(180 * 1024, IntConstant(
                sentinel, "RunicSentinel.Runtime.SentinelAdminControl", "MaximumEnvelopeBytes"));
            True(ReferencesMethod(portals, "ZNet", "GetServerPeer") &&
                 ReferencesMethod(portals, "ZNet", "GetPeer") &&
                 ReferencesMethod(portals, "ZPackage", "Size"),
                "Portals RPC transport no longer proves current peers and bounded envelopes.");

            int standPayloadBytes = IntConstant(
                stands, "RunicDisplayStands.ConfigSync", "MaximumPayloadBytes");
            int standEntries = IntConstant(
                stands, "RunicDisplayStands.ConfigSync", "MaximumEntries");
            int standKeyBytes = IntConstant(
                stands, "RunicDisplayStands.ConfigSync", "MaximumKeyBytes");
            int standValueBytes = IntConstant(
                stands, "RunicDisplayStands.ConfigSync", "MaximumValueBytes");
            True(standPayloadBytes > 0 && standPayloadBytes <= 16 * 1024 &&
                 standEntries > 0 && standEntries <= 64 &&
                 standKeyBytes > 0 && standKeyBytes <= 256 &&
                 standValueBytes > 0 && standValueBytes <= 8 * 1024,
                "Display Stands config RPC limits are missing or unreasonably broad.");
            True(ReferencesMethod(stands, "ZNet", "GetServerPeer") &&
                 ReferencesMethod(stands, "System.Text.Encoding", "GetByteCount"),
                "Display Stands config RPC is not exact-server-bound and UTF-8 bounded.");
        }

        private static void IconsAreExact()
        {
            var hashes = new HashSet<string>(StringComparer.Ordinal);
            foreach (ModuleSpec module in Modules)
            {
                string path = PathOf(module.Directory, module.IconPath.Replace('/', Path.DirectorySeparatorChar));
                byte[] bytes = File.ReadAllBytes(path);
                True(bytes.Length >= 24 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4e && bytes[3] == 0x47,
                    module.Assembly + " icon is not PNG");
                Equal(256, ReadBigEndianInt32(bytes, 16));
                Equal(256, ReadBigEndianInt32(bytes, 20));
                hashes.Add(Convert.ToHexString(SHA256.HashData(bytes)));
            }
            Equal(Modules.Length, hashes.Count);
        }

        private static void PersistentWritesHaveOwners()
        {
            foreach (ModuleSpec module in CompatibilityParticipants)
            {
                using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(AssemblyPath(module));
                var writes = new List<string>();
                foreach (TypeDefinition type in AllTypes(assembly.MainModule.Types))
                foreach (MethodDefinition method in type.Methods)
                {
                    if (!method.HasBody) continue;
                    foreach (Mono.Cecil.Cil.Instruction instruction in method.Body.Instructions)
                    {
                        if (!(instruction.Operand is MethodReference called) ||
                            !string.Equals(called.DeclaringType.FullName, "ZDO", StringComparison.Ordinal) ||
                            !string.Equals(called.Name, "Set", StringComparison.Ordinal)) continue;
                        writes.Add(type.FullName + "::" + method.Name);
                    }
                }
                if (writes.Count == 0) continue;
                bool declaredOwner = PersistentWorldStateOwners.Contains(module.Assembly);
                bool exactException = ApprovedPersistentWriteMethods.TryGetValue(
                    module.Assembly, out HashSet<string> approved) &&
                    writes.All(approved.Contains) && approved.All(writes.Contains);
                True(declaredOwner || exactException,
                    module.Assembly + " directly writes persistent ZDO state outside an exact " +
                    "single-purpose owner at " + string.Join(",", writes.Take(4)));
                string readme = File.ReadAllText(PathOf(module.Directory, "README.md"));
                True(readme.IndexOf("persist", StringComparison.OrdinalIgnoreCase) >= 0,
                    module.Assembly + " writes ZDO state without documenting persistence ownership.");
            }
        }

        private static void HarmonyTargetsResolveExactly()
        {
            IReadOnlyList<PatchRecord> patches = ReadPatches();
            True(patches.Count > 0, "No Harmony patch records were discovered.");
            string managed = FindManagedDirectory();
            var assemblies = new Dictionary<string, AssemblyDefinition>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (PatchRecord patch in patches)
                {
                    string ownerType = patch.Owner.Split(new[] { "::" }, StringSplitOptions.None)[0];
                    True(!patch.Dynamic || patch.ExactDynamicDescriptor ||
                         ApprovedDynamicTargets.ContainsKey(ownerType),
                        patch.Module + " uses an unclassified dynamic Harmony target: " + patch.Owner);
                    string scope = patch.TargetAssembly;
                    True(!string.IsNullOrWhiteSpace(scope), patch.Module + " has no target assembly scope: " + patch.Owner);
                    if (!assemblies.TryGetValue(scope, out AssemblyDefinition targetAssembly))
                    {
                        string path = Path.Combine(managed, scope + ".dll");
                        True(File.Exists(path), "Installed target assembly is missing: " + path);
                        targetAssembly = AssemblyDefinition.ReadAssembly(path);
                        assemblies.Add(scope, targetAssembly);
                    }

                    TypeDefinition targetType = AllTypes(targetAssembly.MainModule.Types)
                        .SingleOrDefault(type => string.Equals(type.FullName, patch.TargetType, StringComparison.Ordinal));
                    True(targetType != null, patch.Module + " target type is absent: " + patch.TargetType);
                    MethodDefinition[] candidates = targetType.Methods
                        .Where(method => string.Equals(method.Name, patch.TargetMethod, StringComparison.Ordinal))
                        .ToArray();
                    True(candidates.Length > 0, patch.Module + " target method is absent: " + patch.Target);
                    if (patch.ArgumentTypesSpecified)
                    {
                        MethodDefinition[] exact = candidates.Where(method =>
                            method.Parameters.Count == patch.ArgumentTypes.Count &&
                            method.Parameters.Select(value => NormalizeTypeName(value.ParameterType.FullName))
                                .SequenceEqual(patch.ArgumentTypes.Select(NormalizeTypeName), StringComparer.Ordinal))
                            .ToArray();
                        True(exact.Length == 1,
                            patch.Module + " exact target signature resolved " + exact.Length +
                            " candidates: " + patch.CollisionKey);
                    }
                    else
                    {
                        Equal(1, candidates.Length);
                    }
                }
            }
            finally
            {
                foreach (AssemblyDefinition assembly in assemblies.Values) assembly.Dispose();
            }
        }

        private static void ManualPeerPatchesAreAbsent()
        {
            foreach (ModuleSpec module in Modules)
            {
                using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(AssemblyPath(module));
                string[] manual = AllTypes(assembly.MainModule.Types)
                    .SelectMany(type => type.Methods)
                    .Where(method => method.HasBody)
                    .Where(method => method.Body.Instructions.Any(instruction =>
                        instruction.Operand is MethodReference called &&
                        string.Equals(called.DeclaringType.FullName, "HarmonyLib.Harmony", StringComparison.Ordinal) &&
                        string.Equals(called.Name, "Patch", StringComparison.Ordinal)))
                    .Select(method => method.DeclaringType.FullName + "::" + method.Name)
                    .ToArray();
                True(manual.Length == 0,
                    module.Assembly + " manually patches a runtime method outside the auditable " +
                    "attribute target inventory: " + string.Join(",", manual));
            }
        }

        private static void PatchOverlapsAreClassified()
        {
            var failures = new List<string>();
            IReadOnlyList<PatchRecord> patches = ReadPatches();
            var targets = GroupPatchCollisions(patches)
                .Where(group => group.Select(value => value.Module).Distinct(StringComparer.Ordinal).Count() > 1)
                .OrderBy(group => group.Key, StringComparer.Ordinal).ToArray();
            foreach (IGrouping<string, PatchRecord> overlap in targets)
            {
                string target = overlap.First().Target;
                if (!ApprovedOverlaps.TryGetValue(target, out string rationale) ||
                    string.IsNullOrWhiteSpace(rationale))
                {
                    failures.Add(overlap.Key + " (" +
                                 string.Join(",", overlap.Select(value => value.Module).Distinct()) + ")");
                    continue;
                }
                if (!ApprovedOverlapModules.TryGetValue(target, out string[] expectedModules))
                {
                    failures.Add(target + " has no exact participant allowlist");
                    continue;
                }
                string[] actualModules = overlap.Select(value => value.Module)
                    .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
                string[] orderedExpected = expectedModules.Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal).ToArray();
                if (!actualModules.SequenceEqual(orderedExpected, StringComparer.Ordinal))
                {
                    failures.Add(overlap.Key + " expected [" + string.Join(",", orderedExpected) +
                                 "] but found [" + string.Join(",", actualModules) + "]");
                    continue;
                }
                if (target == "Container::CheckForChanges")
                {
                    bool observationalPostfixes = overlap.All(value =>
                        value.HasPostfix && !value.HasPrefix && !value.HasTranspiler);
                    if (!observationalPostfixes)
                    {
                        failures.Add(overlap.Key +
                                     " must remain postfix-only standalone index observation");
                        continue;
                    }
                }
                if (target == "Container::OnDestroyed")
                {
                    bool observational = overlap.All(value => value.HasPostfix &&
                        !value.HasPrefix && !value.HasTranspiler &&
                        PatchAcceptsRunOriginal(value));
                    if (!observational)
                    {
                        failures.Add(overlap.Key +
                                     " must keep every index observer postfix-only and require __runOriginal");
                        continue;
                    }
                }
                if (target == "Player::UpdatePlacement")
                {
                    PatchRecord sentinel = overlap.Single(value =>
                        value.Module == "RunicSentinel" && value.HasPrefix);
                    PatchRecord camera = overlap.Single(value =>
                        value.Module == "RunicBuildCamera" && value.HasPrefix);
                    PatchRecord precision = overlap.Single(value =>
                        value.Module == "RunicPrecisionBuildTool" && value.HasPrefix);
                    bool exactOrder = sentinel.HasSkippingPrefix && sentinel.Priority == 800 &&
                                      sentinel.Before.Contains("chazman.RunicBuildCamera", StringComparer.Ordinal) &&
                                      camera.Priority == 800 &&
                                      camera.Before.Contains("chazman.RunicPrecisionBuildTool", StringComparer.Ordinal) &&
                                      precision.Priority == 400 &&
                                      precision.Before.Contains("chazman.RunicCrafting", StringComparer.Ordinal);
                    if (!exactOrder)
                    {
                        failures.Add(overlap.Key +
                                     " must order Sentinel, Build Camera, Precision, then Crafting");
                        continue;
                    }
                }
                if (target == "Switch::Interact")
                {
                    PatchRecord stands = overlap.Single(value =>
                        value.Module == "RunicDisplayStands" && value.HasPrefix);
                    PatchRecord interaction = overlap.Single(value =>
                        value.Module == "RunicInteraction" && value.HasPrefix);
                    bool exactOrder = stands.HasSkippingPrefix &&
                                      stands.Before.Contains("chazman.RunicInteraction", StringComparer.Ordinal) &&
                                      !interaction.HasSkippingPrefix &&
                                      !stands.HasTranspiler && !interaction.HasTranspiler;
                    if (!exactOrder)
                    {
                        failures.Add(overlap.Key +
                                     " must let Display Stands decide its managed switch before Interaction");
                        continue;
                    }
                }
                Console.WriteLine("  OVERLAP " + overlap.Key + " => " + rationale + " [" +
                                  string.Join(", ", overlap.Select(value =>
                                      value.Module + ":" + value.Owner)) + "]");
            }
            var actualTargets = new HashSet<string>(
                targets.Select(value => value.First().Target), StringComparer.Ordinal);
            foreach (string stale in ApprovedOverlaps.Keys.Where(value => !actualTargets.Contains(value)))
                failures.Add("stale rationale approval " + stale);
            foreach (string stale in ApprovedOverlapModules.Keys.Where(value => !actualTargets.Contains(value)))
                failures.Add("stale participant approval " + stale);
            True(failures.Count == 0,
                "Unclassified cross-module patch target(s): " + string.Join("; ", failures));
        }

        private static void TranspilersDoNotCompete()
        {
            IReadOnlyList<PatchRecord> patches = ReadPatches();
            foreach (IGrouping<string, PatchRecord> target in GroupPatchCollisions(patches))
            {
                int modules = target.Where(value => value.HasTranspiler).Select(value => value.Module)
                    .Distinct(StringComparer.Ordinal).Count();
                True(modules <= 1, "Multiple modules transpile " + target.Key);
            }
        }

        private static bool PatchAcceptsRunOriginal(PatchRecord patch)
        {
            ModuleSpec module = CompatibilityParticipants.Single(value =>
                string.Equals(value.Assembly, patch.Module, StringComparison.Ordinal));
            string[] owner = patch.Owner.Split(new[] { "::" }, StringSplitOptions.None);
            if (owner.Length != 2) return false;
            using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(AssemblyPath(module));
            TypeDefinition type = AllTypes(assembly.MainModule.Types).SingleOrDefault(value =>
                string.Equals(value.FullName, owner[0], StringComparison.Ordinal));
            if (type == null) return false;
            return type.Methods.Where(method =>
                    string.Equals(method.Name, owner[1], StringComparison.Ordinal) &&
                    IsPatchKind(method, "HarmonyPostfix", "Postfix"))
                .Any(method => method.Parameters.Any(parameter =>
                    string.Equals(parameter.Name, "__runOriginal", StringComparison.Ordinal) &&
                    string.Equals(parameter.ParameterType.FullName, "System.Boolean", StringComparison.Ordinal)));
        }

        private static void SkippingPrefixOverlapsAreOrdered()
        {
            var failures = new List<string>();
            IReadOnlyList<PatchRecord> patches = ReadPatches();
            foreach (IGrouping<string, PatchRecord> target in GroupPatchCollisions(patches))
            {
                PatchRecord[] prefixes = target.Where(value => value.HasPrefix).ToArray();
                if (prefixes.Select(value => value.Module).Distinct(StringComparer.Ordinal).Count() <= 1 ||
                    !prefixes.Any(value => value.HasSkippingPrefix)) continue;
                for (int leftIndex = 0; leftIndex < prefixes.Length; leftIndex++)
                for (int rightIndex = leftIndex + 1; rightIndex < prefixes.Length; rightIndex++)
                {
                    PatchRecord left = prefixes[leftIndex];
                    PatchRecord right = prefixes[rightIndex];
                    if (left.Module == right.Module ||
                        (!left.HasSkippingPrefix && !right.HasSkippingPrefix)) continue;
                    bool leftBefore = DeclaresBefore(left, right);
                    bool rightBefore = DeclaresBefore(right, left);
                    if (leftBefore && rightBefore)
                    {
                        failures.Add(target.Key + ": contradictory order between " +
                                     left.Module + " and " + right.Module);
                        continue;
                    }
                    int leftPriority = left.Priority ?? 400;
                    int rightPriority = right.Priority ?? 400;
                    if (!leftBefore && !rightBefore && leftPriority == rightPriority)
                        failures.Add(target.Key + ": no effective order between " +
                                     left.Module + " and " + right.Module);
                }
            }
            True(failures.Count == 0,
                "Cross-module prefixes lack Harmony ordering: " + string.Join("; ", failures));
        }

        private static void ResultObserversAreOrdered()
        {
            var failures = new List<string>();
            IReadOnlyList<PatchRecord> patches = ReadPatches();
            foreach (IGrouping<string, PatchRecord> target in GroupPatchCollisions(patches))
            {
                PatchRecord[] postfixes = target.Where(value => value.HasPostfix).ToArray();
                if (postfixes.Select(value => value.Module)
                        .Distinct(StringComparer.Ordinal).Count() <= 1) continue;
                for (int leftIndex = 0; leftIndex < postfixes.Length; leftIndex++)
                for (int rightIndex = leftIndex + 1; rightIndex < postfixes.Length; rightIndex++)
                {
                    PatchRecord left = postfixes[leftIndex];
                    PatchRecord right = postfixes[rightIndex];
                    if (left.Module == right.Module ||
                        (!left.ReadsResult && !right.ReadsResult)) continue;
                    bool leftBefore = DeclaresBefore(left, right);
                    bool rightBefore = DeclaresBefore(right, left);
                    if (leftBefore && rightBefore)
                    {
                        failures.Add(target.Key + ": contradictory postfix order between " +
                                     left.Module + " and " + right.Module);
                        continue;
                    }
                    int leftPriority = left.Priority ?? 400;
                    int rightPriority = right.Priority ?? 400;
                    if (!leftBefore && !rightBefore && leftPriority == rightPriority)
                        failures.Add(target.Key + ": no effective postfix order between result observers " +
                                     left.Module + " and " + right.Module);
                }
            }
            True(failures.Count == 0,
                "Cross-module result observers lack Harmony ordering: " + string.Join("; ", failures));
        }

        private static bool DeclaresBefore(PatchRecord first, PatchRecord second)
        {
            string firstGuid = CompatibilityParticipants.Single(value => value.Assembly == first.Module).Guid;
            string secondGuid = CompatibilityParticipants.Single(value => value.Assembly == second.Module).Guid;
            return first.Before.Contains(secondGuid, StringComparer.Ordinal) ||
                   second.After.Contains(firstGuid, StringComparer.Ordinal);
        }

        private static IEnumerable<IGrouping<string, PatchRecord>> GroupPatchCollisions(
            IReadOnlyList<PatchRecord> patches)
        {
            var wildcardTargets = new HashSet<string>(patches
                .Where(value => !value.ArgumentTypesSpecified)
                .Select(value => value.Target), StringComparer.Ordinal);
            return patches.GroupBy(
                value => wildcardTargets.Contains(value.Target)
                    ? value.Target + "(*)"
                    : value.CollisionKey,
                StringComparer.Ordinal).ToArray();
        }

        private static void DocumentationHasBoundaries()
        {
            foreach (ModuleSpec module in Modules)
            {
                string readme = File.ReadAllText(PathOf(module.Directory, "README.md"));
                bool boundedHeading = Regex.IsMatch(
                    readme,
                    @"^##[^\r\n]*(?:bound|limit|performance|safety|lossless|production-readiness|included features|what .* does)",
                    RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);
                bool authorityHeading = Regex.IsMatch(
                    readme,
                    @"^##[^\r\n]*(?:authority|multiplayer|ownership|compatibility|boundary)",
                    RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);
                bool authorityStatement = Regex.IsMatch(
                    readme,
                    @"\b(?:authority|multiplayer|server|client|host|owner|single-player|dedicated)\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                True(boundedHeading,
                    module.Assembly + " README has no explicit bounded-work/performance heading.");
                True(authorityHeading && authorityStatement,
                    module.Assembly + " README has no explicit authority/multiplayer heading.");
            }
        }

        private static void DefaultInputChordsDoNotCollide()
        {
            InputBinding[] bindings = Modules.SelectMany(ReadDefaultBindings)
                .Concat(ReadInteractionRuntimeBindings())
                .ToArray();
            InputBinding[] interaction = bindings.Where(value =>
                string.Equals(value.Module, "RunicInteraction", StringComparison.Ordinal)).ToArray();
            Equal(InteractionRuntimeBindingCount(), interaction.Length);
            InputBinding pickupBypass = interaction.Single(value =>
                string.Equals(value.Name, "pickup-bypass-controller", StringComparison.Ordinal));
            Equal(CanonicalChord("JoyRStick+JoyUse"), pickupBypass.Canonical);
            InputBinding agricultureHarvest = bindings.Single(value =>
                string.Equals(value.Module, "RunicAgriculture", StringComparison.Ordinal) &&
                string.Equals(value.Name, "AreaHarvestAction", StringComparison.Ordinal));
            Equal(CanonicalChord("JoyAltKeys+JoyUse"), agricultureHarvest.Canonical);
            foreach (IGrouping<string, InputBinding> collision in bindings
                         .GroupBy(value => value.Kind + ":" + value.Canonical, StringComparer.OrdinalIgnoreCase)
                         .Where(group => group.Select(value => value.Module)
                             .Distinct(StringComparer.Ordinal).Count() > 1))
            {
                True(ApprovedInputOverlaps.TryGetValue(collision.Key, out string rationale),
                    "Unclassified default input collision " + collision.Key + ": " +
                    string.Join(", ", collision.Select(value => value.Module + "." + value.Name)));
                True(!string.IsNullOrWhiteSpace(rationale));
            }
        }

        private static IEnumerable<InputBinding> ReadDefaultBindings(ModuleSpec module)
        {
            string directory = PathOf(module.Directory);
            string[] files = Directory.Exists(directory)
                ? Directory.GetFiles(directory, "*.cfg.example", SearchOption.TopDirectoryOnly)
                : Array.Empty<string>();
            if (files.Length == 0) yield break;
            Equal(1, files.Length);
            var settings = new List<ConfigInputSetting>();
            string section = string.Empty;
            bool keyboardShortcut = false;
            foreach (string raw in File.ReadLines(files[0]))
            {
                string line = raw.Trim();
                Match sectionMatch = Regex.Match(line, @"^\[(?<name>[^]]+)\]$");
                if (sectionMatch.Success)
                {
                    section = sectionMatch.Groups["name"].Value;
                    keyboardShortcut = false;
                    continue;
                }
                if (line.IndexOf("Setting type: KeyboardShortcut", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    keyboardShortcut = true;
                    continue;
                }
                Match setting = Regex.Match(line, @"^(?<name>[A-Za-z0-9_.-]+)\s*=\s*(?<value>.*)$");
                if (!setting.Success)
                {
                    if (!line.StartsWith("#", StringComparison.Ordinal)) keyboardShortcut = false;
                    continue;
                }

                settings.Add(new ConfigInputSetting(
                    section,
                    setting.Groups["name"].Value,
                    setting.Groups["value"].Value.Trim(),
                    keyboardShortcut));
                keyboardShortcut = false;
            }

            foreach (ConfigInputSetting setting in settings)
            {
                bool controller = setting.Section.IndexOf(
                                      "Controller", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                  setting.Name.EndsWith("Action", StringComparison.OrdinalIgnoreCase) &&
                                  !setting.Name.EndsWith("ModifierAction", StringComparison.OrdinalIgnoreCase);
                bool wheelModifier = setting.Name.EndsWith(
                    "WheelChord", StringComparison.OrdinalIgnoreCase);
                bool keyboard = !wheelModifier && (setting.KeyboardShortcut ||
                                 ((setting.Section.Equals("Keys", StringComparison.OrdinalIgnoreCase) ||
                                  setting.Section.Equals("Keyboard", StringComparison.OrdinalIgnoreCase) ||
                                  setting.Section.StartsWith("Controls", StringComparison.OrdinalIgnoreCase)) &&
                                  LooksLikeKeyboardShortcut(setting.Value)));
                if ((!controller && !keyboard) || string.IsNullOrWhiteSpace(setting.Value) ||
                    setting.Value.Equals("None", StringComparison.OrdinalIgnoreCase)) continue;
                string chord = setting.Value;
                if (controller)
                {
                    string modifier = settings
                        .Where(candidate => candidate.Section.Equals(
                                                setting.Section, StringComparison.OrdinalIgnoreCase) &&
                                            candidate.Name.Equals(
                                                "ModifierAction", StringComparison.OrdinalIgnoreCase))
                        .Select(candidate => candidate.Value)
                        .SingleOrDefault() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(modifier) &&
                        !modifier.Equals("None", StringComparison.OrdinalIgnoreCase))
                        chord = setting.Value + "+" + modifier;
                }
                yield return new InputBinding(
                    module.Assembly,
                    setting.Name,
                    controller ? "Controller" : "Keyboard",
                    CanonicalChord(chord));
            }
        }

        private static IEnumerable<InputBinding> ReadInteractionRuntimeBindings()
        {
            ModuleSpec interaction = Modules.Single(value =>
                string.Equals(value.Assembly, "RunicInteraction", StringComparison.Ordinal));
            using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(AssemblyPath(interaction));
            const string catalogType = "RunicInteraction.Core.InteractionInputBindings";
            string catalog = Constant(assembly, catalogType, "DefaultBindingCatalog");
            string primary = Constant(assembly, catalogType, "PickupBypassControllerPrimary");
            string defaultModifier = Constant(
                assembly, catalogType, "DefaultPickupBypassControllerModifier");
            int expectedCount = Convert.ToInt32(AllTypes(assembly.MainModule.Types)
                .Single(type => string.Equals(type.FullName, catalogType, StringComparison.Ordinal))
                .Fields.Single(field => field.Name == "BindingCount" && field.HasConstant).Constant);
            string[] lines = catalog.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            Equal(expectedCount, lines.Length);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (string line in lines)
            {
                string[] parts = line.Split('|');
                Equal(5, parts.Length);
                True(ids.Add(parts[0]), "Duplicate Interaction runtime binding ID: " + parts[0]);
                string kind;
                if (parts[1].Equals("keyboard", StringComparison.OrdinalIgnoreCase)) kind = "Keyboard";
                else if (parts[1].Equals("controller", StringComparison.OrdinalIgnoreCase) ||
                         parts[1].Equals("gamepad", StringComparison.OrdinalIgnoreCase)) kind = "Controller";
                else throw new InvalidOperationException(
                    "Unknown Interaction runtime binding device: " + parts[1]);
                if (parts[0].Equals("pickup-bypass-controller", StringComparison.Ordinal))
                {
                    Equal(primary, parts[2]);
                    Equal(defaultModifier, parts[3]);
                    Equal(
                        defaultModifier,
                        ReadConfigDefault(
                            interaction,
                            "Controller",
                            "PickupBypassModifierAction"));
                }
                yield return new InputBinding(
                    interaction.Assembly,
                    parts[0],
                    kind,
                    CanonicalChord(parts[2] + "+" + parts[3]));
            }
        }

        private static int InteractionRuntimeBindingCount()
        {
            ModuleSpec interaction = Modules.Single(value =>
                string.Equals(value.Assembly, "RunicInteraction", StringComparison.Ordinal));
            using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(AssemblyPath(interaction));
            TypeDefinition type = AllTypes(assembly.MainModule.Types).Single(candidate =>
                string.Equals(
                    candidate.FullName,
                    "RunicInteraction.Core.InteractionInputBindings",
                    StringComparison.Ordinal));
            return Convert.ToInt32(type.Fields.Single(field =>
                field.Name == "BindingCount" && field.HasConstant).Constant);
        }

        private static string ReadConfigDefault(ModuleSpec module, string expectedSection, string expectedName)
        {
            string[] files = Directory.GetFiles(
                PathOf(module.Directory), "*.cfg.example", SearchOption.TopDirectoryOnly);
            Equal(1, files.Length);
            string section = string.Empty;
            foreach (string raw in File.ReadLines(files[0]))
            {
                string line = raw.Trim();
                Match sectionMatch = Regex.Match(line, @"^\[(?<name>[^]]+)\]$");
                if (sectionMatch.Success)
                {
                    section = sectionMatch.Groups["name"].Value;
                    continue;
                }
                Match setting = Regex.Match(line, @"^(?<name>[A-Za-z0-9_.-]+)\s*=\s*(?<value>.*)$");
                if (!setting.Success ||
                    !section.Equals(expectedSection, StringComparison.Ordinal) ||
                    !setting.Groups["name"].Value.Equals(expectedName, StringComparison.Ordinal)) continue;
                return setting.Groups["value"].Value.Trim();
            }
            throw new InvalidOperationException(
                module.Assembly + " config omits " + expectedSection + "." + expectedName + ".");
        }

        private static bool LooksLikeKeyboardShortcut(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            string[] parts = value.Split('+').Select(item => item.Trim())
                .Where(item => item.Length > 0).ToArray();
            if (parts.Length == 0 || parts.Length > 5) return false;
            return parts.All(part => Regex.IsMatch(
                part,
                @"^(?:[A-Z]|Alpha[0-9]|Keypad(?:[0-9]|Period)|F(?:[1-9]|1[0-2])|Left(?:Alt|Control|Shift)|Right(?:Alt|Control|Shift)|Mouse[0-9]|Space|Tab|Return|Escape|Backspace|Delete|Home|End|PageUp|PageDown|UpArrow|DownArrow|LeftArrow|RightArrow)$",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase));
        }

        private static string CanonicalChord(string value) =>
            string.Join("+", value.Split('+').Select(item => item.Trim())
                .Where(item => item.Length > 0)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase));

        private static IReadOnlyList<PatchRecord> ReadPatches()
        {
            var result = new List<PatchRecord>();
            foreach (ModuleSpec module in CompatibilityParticipants)
            {
                using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(AssemblyPath(module));
                foreach (TypeDefinition type in AllTypes(assembly.MainModule.Types))
                {
                    CustomAttribute[] typePatches = HarmonyPatchAttributes(type.CustomAttributes);
                    var recorded = new HashSet<MethodDefinition>();
                    if (typePatches.Length > 0)
                    {
                        MethodDefinition[] patchMethods = type.Methods.Where(IsHarmonyPatchMethod).ToArray();
                        True(patchMethods.Length > 0,
                            module.Assembly + " Harmony patch type has no patch method: " + type.FullName);
                        foreach (MethodDefinition method in patchMethods)
                        {
                            CustomAttribute[] methodPatches = HarmonyPatchAttributes(method.CustomAttributes);
                            result.AddRange(BuildPatchRecords(
                                module,
                                type,
                                method,
                                typePatches.Concat(methodPatches).ToArray()));
                            recorded.Add(method);
                        }
                    }
                    foreach (MethodDefinition method in type.Methods)
                    {
                        if (recorded.Contains(method)) continue;
                        CustomAttribute[] methodPatches = HarmonyPatchAttributes(method.CustomAttributes);
                        if (methodPatches.Length == 0) continue;
                        result.AddRange(BuildPatchRecords(
                            module,
                            type,
                            method,
                            typePatches.Concat(methodPatches).ToArray()));
                    }
                }
            }
            return result;
        }

        private static bool IsHarmonyPatchMethod(MethodDefinition method) =>
            IsPatchKind(method, "HarmonyPrefix", "Prefix") ||
            IsPatchKind(method, "HarmonyPostfix", "Postfix") ||
            IsPatchKind(method, "HarmonyTranspiler", "Transpiler") ||
            IsPatchKind(method, "HarmonyFinalizer", "Finalizer");

        private static CustomAttribute[] HarmonyPatchAttributes(
            IEnumerable<CustomAttribute> attributes) =>
            attributes.Where(attribute =>
                attribute.AttributeType.FullName == "HarmonyLib.HarmonyPatch").ToArray();

        private static IReadOnlyList<PatchRecord> BuildPatchRecords(
            ModuleSpec module,
            TypeDefinition ownerType,
            MethodDefinition ownerMethod,
            IReadOnlyList<CustomAttribute> attributes)
        {
            DynamicTargetSpec[] exactTargets = ReadExactDynamicTargets(ownerType);
            if (exactTargets.Length == 0)
                return new[] { BuildPatchRecord(module, ownerType, ownerMethod, attributes, null) };
            True(ownerType.Methods.Any(method =>
                    string.Equals(method.Name, "TargetMethods", StringComparison.Ordinal)),
                module.Assembly + " exact mutation descriptors require TargetMethods: " + ownerType.FullName);
            return exactTargets.Select(target =>
                    BuildPatchRecord(module, ownerType, ownerMethod, attributes, target))
                .ToArray();
        }

        private static DynamicTargetSpec[] ReadExactDynamicTargets(TypeDefinition ownerType)
        {
            CustomAttribute[] attributes = ownerType.CustomAttributes.Where(attribute =>
                string.Equals(attribute.AttributeType.FullName, ExactDynamicTargetAttribute,
                    StringComparison.Ordinal)).ToArray();
            var result = new List<DynamicTargetSpec>(attributes.Length);
            foreach (CustomAttribute attribute in attributes)
            {
                Equal(3, attribute.ConstructorArguments.Count);
                TypeReference targetType = attribute.ConstructorArguments[0].Value as TypeReference;
                string targetMethod = attribute.ConstructorArguments[1].Value as string;
                CustomAttributeArgument[] encodedParameters =
                    attribute.ConstructorArguments[2].Value as CustomAttributeArgument[];
                True(targetType != null,
                    ownerType.FullName + " durable mutation descriptor omits its declaring type.");
                True(!string.IsNullOrWhiteSpace(targetMethod),
                    ownerType.FullName + " durable mutation descriptor omits its method.");
                True(encodedParameters != null,
                    ownerType.FullName + " durable mutation descriptor has no canonical parameter array.");
                string[] argumentTypes = encodedParameters.Select(argument =>
                {
                    TypeReference type = argument.Value as TypeReference;
                    True(type != null,
                        ownerType.FullName + " durable mutation descriptor contains a non-Type parameter.");
                    return type.FullName;
                }).ToArray();
                result.Add(new DynamicTargetSpec(
                    targetType.FullName,
                    targetType.Scope?.Name,
                    targetMethod,
                    argumentTypes));
            }
            string[] keys = result.Select(value =>
                value.TargetAssembly + "|" + value.TargetType + "::" + value.TargetMethod + "(" +
                string.Join(",", value.ArgumentTypes.Select(NormalizeTypeName)) + ")").ToArray();
            Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
            return result.ToArray();
        }

        private static PatchRecord BuildPatchRecord(
            ModuleSpec module,
            TypeDefinition ownerType,
            MethodDefinition ownerMethod,
            IReadOnlyList<CustomAttribute> attributes,
            DynamicTargetSpec exactDynamicTarget)
        {
            TypeReference targetType = null;
            string targetMethod = null;
            bool argumentTypesSpecified = false;
            var argumentTypes = new List<string>();
            foreach (CustomAttribute attribute in attributes)
            {
                foreach (CustomAttributeArgument argument in attribute.ConstructorArguments)
                    ReadHarmonyArgument(
                        argument,
                        ref targetType,
                        ref targetMethod,
                        ref argumentTypesSpecified,
                        argumentTypes);
                foreach (CustomAttributeNamedArgument field in attribute.Fields)
                    ReadHarmonyNamedArgument(
                        field.Name,
                        field.Argument,
                        ref targetType,
                        ref targetMethod,
                        ref argumentTypesSpecified,
                        argumentTypes);
                foreach (CustomAttributeNamedArgument property in attribute.Properties)
                    ReadHarmonyNamedArgument(
                        property.Name,
                        property.Argument,
                        ref targetType,
                        ref targetMethod,
                        ref argumentTypesSpecified,
                        argumentTypes);
            }

            string owner = ownerType.FullName + (ownerMethod == null ? string.Empty : "::" + ownerMethod.Name);
            bool dynamic = ownerType.Methods.Any(method =>
                string.Equals(method.Name, "TargetMethod", StringComparison.Ordinal) ||
                string.Equals(method.Name, "TargetMethods", StringComparison.Ordinal) ||
                method.CustomAttributes.Any(attribute =>
                    attribute.AttributeType.FullName == "HarmonyLib.HarmonyTargetMethod" ||
                    attribute.AttributeType.FullName == "HarmonyLib.HarmonyTargetMethods"));
            DynamicTargetSpec dynamicTarget = exactDynamicTarget;
            if (dynamicTarget == null && dynamic &&
                ApprovedDynamicTargets.TryGetValue(ownerType.FullName, out DynamicTargetSpec approved))
            {
                dynamicTarget = approved;
            }
            if (dynamicTarget != null)
            {
                targetMethod = dynamicTarget.TargetMethod;
                argumentTypesSpecified = true;
                argumentTypes.Clear();
                foreach (string argumentType in dynamicTarget.ArgumentTypes) argumentTypes.Add(argumentType);
            }
            if (!dynamic)
            {
                True(targetType != null, module.Assembly + " patch target type is unresolved: " + owner);
                True(!string.IsNullOrWhiteSpace(targetMethod),
                    module.Assembly + " patch target method is unresolved: " + owner);
            }

            IEnumerable<MethodDefinition> patchMethods = ownerMethod == null
                ? ownerType.Methods
                : new[] { ownerMethod };
            bool transpiler = patchMethods.Any(method => IsPatchKind(method, "HarmonyTranspiler", "Transpiler"));
            bool prefix = patchMethods.Any(method => IsPatchKind(method, "HarmonyPrefix", "Prefix"));
            MethodDefinition[] postfixMethods = patchMethods.Where(method =>
                IsPatchKind(method, "HarmonyPostfix", "Postfix")).ToArray();
            bool postfix = postfixMethods.Length > 0;
            bool readsResult = postfixMethods.Any(method =>
                method.Parameters.Any(parameter => parameter.Name == "__result"));
            bool writesResult = postfixMethods.Any(method =>
                method.Parameters.Any(parameter => parameter.Name == "__result" &&
                                                   parameter.ParameterType is ByReferenceType));
            bool skippingPrefix = patchMethods.Any(method =>
                IsPatchKind(method, "HarmonyPrefix", "Prefix") &&
                method.ReturnType.FullName == "System.Boolean");
            CustomAttribute[] orderAttributes = ownerType.CustomAttributes
                .Concat(patchMethods.SelectMany(method => method.CustomAttributes))
                .Where(IsHarmonyOrderAttribute).ToArray();
            int? priority = orderAttributes
                .Where(attribute => attribute.AttributeType.FullName == "HarmonyLib.HarmonyPriority")
                .Select(ReadHarmonyPriority).Where(value => value.HasValue)
                .Select(value => value.Value).Cast<int?>().LastOrDefault();
            string[] before = orderAttributes
                .Where(attribute => attribute.AttributeType.FullName == "HarmonyLib.HarmonyBefore")
                .SelectMany(ReadHarmonyOwnerIds).Distinct(StringComparer.Ordinal).ToArray();
            string[] after = orderAttributes
                .Where(attribute => attribute.AttributeType.FullName == "HarmonyLib.HarmonyAfter")
                .SelectMany(ReadHarmonyOwnerIds).Distinct(StringComparer.Ordinal).ToArray();
            bool explicitOrder = priority.HasValue || before.Length > 0 || after.Length > 0;
            return new PatchRecord(
                module.Assembly,
                owner,
                dynamicTarget?.TargetType ?? targetType?.FullName,
                dynamicTarget?.TargetAssembly ?? targetType?.Scope?.Name,
                targetMethod,
                argumentTypesSpecified,
                argumentTypes.AsReadOnly(),
                transpiler,
                prefix,
                postfix,
                readsResult,
                writesResult,
                skippingPrefix,
                explicitOrder,
                priority,
                before,
                after,
                dynamic,
                exactDynamicTarget != null);
        }

        private static int? ReadHarmonyPriority(CustomAttribute attribute)
        {
            if (attribute.ConstructorArguments.Count != 1) return null;
            object value = attribute.ConstructorArguments[0].Value;
            return value == null ? null : Convert.ToInt32(value);
        }

        private static IEnumerable<string> ReadHarmonyOwnerIds(CustomAttribute attribute)
        {
            foreach (CustomAttributeArgument argument in attribute.ConstructorArguments)
            foreach (string value in ReadHarmonyOwnerIds(argument))
                yield return value;
        }

        private static IEnumerable<string> ReadHarmonyOwnerIds(CustomAttributeArgument argument)
        {
            if (argument.Value is string text)
            {
                if (!string.IsNullOrWhiteSpace(text)) yield return text;
                yield break;
            }
            if (!(argument.Value is CustomAttributeArgument[] values)) yield break;
            foreach (CustomAttributeArgument value in values)
            foreach (string nested in ReadHarmonyOwnerIds(value))
                yield return nested;
        }

        private static bool IsPatchKind(
            MethodDefinition method,
            string attributeName,
            string conventionalName) =>
            string.Equals(method.Name, conventionalName, StringComparison.Ordinal) ||
            method.CustomAttributes.Any(attribute =>
                attribute.AttributeType.FullName == "HarmonyLib." + attributeName);

        private static bool IsHarmonyOrderAttribute(CustomAttribute attribute) =>
            attribute.AttributeType.FullName == "HarmonyLib.HarmonyPriority" ||
            attribute.AttributeType.FullName == "HarmonyLib.HarmonyBefore" ||
            attribute.AttributeType.FullName == "HarmonyLib.HarmonyAfter";

        private static void ReadHarmonyNamedArgument(
            string name,
            CustomAttributeArgument argument,
            ref TypeReference targetType,
            ref string targetMethod,
            ref bool argumentTypesSpecified,
            ICollection<string> argumentTypes)
        {
            if (string.Equals(name, "declaringType", StringComparison.OrdinalIgnoreCase) &&
                argument.Value is TypeReference reference)
                targetType ??= reference;
            else if (string.Equals(name, "methodName", StringComparison.OrdinalIgnoreCase) &&
                     argument.Value is string value)
                targetMethod ??= value;
            else if (string.Equals(name, "argumentTypes", StringComparison.OrdinalIgnoreCase))
                ReadHarmonyArgument(
                    argument,
                    ref targetType,
                    ref targetMethod,
                    ref argumentTypesSpecified,
                    argumentTypes);
        }

        private static void ReadHarmonyArgument(
            CustomAttributeArgument argument,
            ref TypeReference targetType,
            ref string targetMethod,
            ref bool argumentTypesSpecified,
            ICollection<string> argumentTypes)
        {
            if (argument.Value is TypeReference reference)
            {
                targetType ??= reference;
                return;
            }
            if (argument.Value is string value)
            {
                targetMethod ??= value;
                return;
            }
            if (!(argument.Value is CustomAttributeArgument[] array)) return;
            argumentTypesSpecified = true;
            argumentTypes.Clear();
            foreach (CustomAttributeArgument item in array)
                if (item.Value is TypeReference itemType) argumentTypes.Add(itemType.FullName);
        }

        private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> roots)
        {
            foreach (TypeDefinition type in roots)
            {
                yield return type;
                foreach (TypeDefinition nested in AllTypes(type.NestedTypes)) yield return nested;
            }
        }

        private static string AssemblyPath(ModuleSpec module) =>
            PathOf(module.Directory, "bin", "Release", "netstandard2.1", module.Assembly + ".dll");
        private static string FindManagedDirectory()
        {
            string configured = Environment.GetEnvironmentVariable("VALHEIM_INSTALL");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                string managed = Path.Combine(configured, "valheim_Data", "Managed");
                if (File.Exists(Path.Combine(managed, "assembly_valheim.dll"))) return managed;
            }

            string project = File.ReadAllText(PathOf("RunicStorage", "RunicStorage.csproj"));
            Match match = Regex.Match(
                project,
                @"<VALHEIM_INSTALL[^>]*>(?<path>[^<]+)</VALHEIM_INSTALL>",
                RegexOptions.CultureInvariant);
            if (match.Success)
            {
                string managed = Path.Combine(
                    match.Groups["path"].Value.Trim(), "valheim_Data", "Managed");
                if (File.Exists(Path.Combine(managed, "assembly_valheim.dll"))) return managed;
            }
            throw new InvalidOperationException("The installed Valheim Managed directory could not be located.");
        }

        private static string FindBepInExCoreDirectory()
        {
            string configured = Environment.GetEnvironmentVariable("BEPINEX_PROFILE");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                string core = Path.Combine(configured, "core");
                if (File.Exists(Path.Combine(core, "BepInEx.dll"))) return core;
            }

            string project = File.ReadAllText(PathOf("RunicSuite14.Tests", "RunicSuite14.Tests.csproj"));
            Match match = Regex.Match(
                project,
                @"<BEPINEX_PROFILE[^>]*>(?<path>[^<]+)</BEPINEX_PROFILE>",
                RegexOptions.CultureInvariant);
            if (match.Success)
            {
                string core = Path.Combine(match.Groups["path"].Value.Trim(), "core");
                if (File.Exists(Path.Combine(core, "BepInEx.dll"))) return core;
            }
            throw new InvalidOperationException("The configured BepInEx core directory could not be located.");
        }

        private static string NormalizeTypeName(string value) =>
            (value ?? string.Empty).Replace('+', '/');
        private static string Constant(AssemblyDefinition assembly, string typeName, string fieldName)
        {
            TypeDefinition type = AllTypes(assembly.MainModule.Types).Single(value =>
                string.Equals(value.FullName, typeName, StringComparison.Ordinal));
            FieldDefinition field = type.Fields.Single(value =>
                string.Equals(value.Name, fieldName, StringComparison.Ordinal) && value.HasConstant);
            return field.Constant as string ?? string.Empty;
        }
        private static bool HasStringLiteral(AssemblyDefinition assembly, string value) =>
            AllTypes(assembly.MainModule.Types).SelectMany(type => type.Methods)
                .Where(method => method.HasBody)
                .SelectMany(method => method.Body.Instructions)
                .Any(instruction => instruction.OpCode.Code == Mono.Cecil.Cil.Code.Ldstr &&
                                    string.Equals(instruction.Operand as string, value, StringComparison.Ordinal));
        private static bool ReferencesType(AssemblyDefinition assembly, string typeName) =>
            assembly.MainModule.GetTypeReferences().Any(reference =>
                string.Equals(reference.FullName, typeName, StringComparison.Ordinal));
        private static bool ReferencesMethod(
            AssemblyDefinition assembly,
            string declaringType,
            string methodName) =>
            assembly.MainModule.GetMemberReferences().OfType<MethodReference>().Any(reference =>
                string.Equals(reference.DeclaringType.FullName, declaringType, StringComparison.Ordinal) &&
                string.Equals(reference.Name, methodName, StringComparison.Ordinal));
        private static TypeDefinition RequirePublicContract(AssemblyDefinition assembly, string typeName)
        {
            TypeDefinition type = AllTypes(assembly.MainModule.Types).SingleOrDefault(candidate =>
                string.Equals(candidate.FullName, typeName, StringComparison.Ordinal));
            True(type != null && type.IsPublic,
                assembly.Name.Name + " public contract is missing or non-public: " + typeName);
            return type;
        }
        private static MethodDefinition RequirePublicStaticMethod(
            AssemblyDefinition assembly,
            string typeName,
            string methodName,
            string returnType,
            params string[] parameterTypes)
        {
            TypeDefinition type = RequirePublicContract(assembly, typeName);
            MethodDefinition[] methods = type.Methods.Where(method =>
                    string.Equals(method.Name, methodName, StringComparison.Ordinal) &&
                    method.IsPublic && method.IsStatic &&
                    (returnType == null || string.Equals(
                        NormalizeTypeName(method.ReturnType.FullName),
                        NormalizeTypeName(returnType),
                        StringComparison.Ordinal)) &&
                    method.Parameters.Select(parameter => NormalizeTypeName(parameter.ParameterType.FullName))
                        .SequenceEqual(parameterTypes.Select(NormalizeTypeName), StringComparer.Ordinal))
                .ToArray();
            True(methods.Length == 1,
                assembly.Name.Name + " exact public API method resolved " + methods.Length +
                " candidates: " + typeName + "::" + methodName + "(" +
                string.Join(",", parameterTypes) + ")");
            return methods[0];
        }
        private static int IntConstant(AssemblyDefinition assembly, string typeName, string fieldName)
        {
            TypeDefinition type = AllTypes(assembly.MainModule.Types).Single(value =>
                string.Equals(value.FullName, typeName, StringComparison.Ordinal));
            FieldDefinition field = type.Fields.Single(value =>
                string.Equals(value.Name, fieldName, StringComparison.Ordinal) && value.HasConstant);
            return Convert.ToInt32(field.Constant);
        }
        private static string AssemblyAttribute(AssemblyDefinition assembly, string attributeType)
        {
            CustomAttribute[] matches = assembly.CustomAttributes.Where(attribute =>
                string.Equals(attribute.AttributeType.FullName, attributeType, StringComparison.Ordinal)).ToArray();
            True(matches.Length == 1,
                assembly.Name.Name + " must contain exactly one " + attributeType + ".");
            True(matches[0].ConstructorArguments.Count == 1,
                assembly.Name.Name + " has an invalid " + attributeType + " shape.");
            return matches[0].ConstructorArguments[0].Value as string ?? string.Empty;
        }
        private static int ReadBigEndianInt32(byte[] bytes, int offset) =>
            (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
        private static int? ReadInt32Constant(Mono.Cecil.Cil.Instruction instruction)
        {
            switch (instruction.OpCode.Code)
            {
                case Mono.Cecil.Cil.Code.Ldc_I4_M1: return -1;
                case Mono.Cecil.Cil.Code.Ldc_I4_0: return 0;
                case Mono.Cecil.Cil.Code.Ldc_I4_1: return 1;
                case Mono.Cecil.Cil.Code.Ldc_I4_2: return 2;
                case Mono.Cecil.Cil.Code.Ldc_I4_3: return 3;
                case Mono.Cecil.Cil.Code.Ldc_I4_4: return 4;
                case Mono.Cecil.Cil.Code.Ldc_I4_5: return 5;
                case Mono.Cecil.Cil.Code.Ldc_I4_6: return 6;
                case Mono.Cecil.Cil.Code.Ldc_I4_7: return 7;
                case Mono.Cecil.Cil.Code.Ldc_I4_8: return 8;
                case Mono.Cecil.Cil.Code.Ldc_I4_S: return Convert.ToInt32(instruction.Operand);
                case Mono.Cecil.Cil.Code.Ldc_I4: return Convert.ToInt32(instruction.Operand);
                default: return null;
            }
        }
        private static string Sha256Hex(string path)
        {
            using FileStream stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        private static string PathOf(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            return parts.Aggregate(root, Path.Combine);
        }
        private static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException($"Expected {expected}; actual {actual}.");
        }
        private static void True(bool value, string message = "Expected true.")
        { if (!value) throw new InvalidOperationException(message); }

        private sealed class ModuleSpec
        {
            internal ModuleSpec(
                string display,
                string directory,
                string assembly,
                string guid,
                string iconPath,
                bool requiresConfigExample = true)
            {
                Display = display;
                Directory = directory;
                Assembly = assembly;
                Guid = guid;
                IconPath = iconPath;
                RequiresConfigExample = requiresConfigExample;
            }
            internal string Display { get; } internal string Directory { get; } internal string Assembly { get; }
            internal string Guid { get; } internal string IconPath { get; }
            internal bool RequiresConfigExample { get; }
        }

        private sealed class DynamicTargetSpec
        {
            internal DynamicTargetSpec(
                string targetType,
                string targetAssembly,
                string targetMethod,
                params string[] argumentTypes)
            {
                TargetType = targetType;
                TargetAssembly = targetAssembly;
                TargetMethod = targetMethod;
                ArgumentTypes = argumentTypes ?? Array.Empty<string>();
            }

            internal string TargetType { get; }
            internal string TargetAssembly { get; }
            internal string TargetMethod { get; }
            internal IReadOnlyList<string> ArgumentTypes { get; }
        }
        private sealed class PatchRecord
        {
            internal PatchRecord(
                string module,
                string owner,
                string targetType,
                string targetAssembly,
                string targetMethod,
                bool argumentTypesSpecified,
                IReadOnlyList<string> argumentTypes,
                bool hasTranspiler,
                bool hasPrefix,
                bool hasPostfix,
                bool readsResult,
                bool writesResult,
                bool hasSkippingPrefix,
                bool hasExplicitOrder,
                int? priority,
                IReadOnlyList<string> before,
                IReadOnlyList<string> after,
                bool dynamic,
                bool exactDynamicDescriptor)
            {
                Module = module;
                Owner = owner;
                TargetType = targetType;
                TargetAssembly = targetAssembly;
                TargetMethod = targetMethod;
                ArgumentTypesSpecified = argumentTypesSpecified;
                ArgumentTypes = argumentTypes;
                HasTranspiler = hasTranspiler;
                HasPrefix = hasPrefix;
                HasPostfix = hasPostfix;
                ReadsResult = readsResult;
                WritesResult = writesResult;
                HasSkippingPrefix = hasSkippingPrefix;
                HasExplicitOrder = hasExplicitOrder;
                Priority = priority;
                Before = before;
                After = after;
                Dynamic = dynamic;
                ExactDynamicDescriptor = exactDynamicDescriptor;
            }
            internal string Module { get; }
            internal string Owner { get; }
            internal string TargetType { get; }
            internal string TargetAssembly { get; }
            internal string TargetMethod { get; }
            internal string Target => TargetType + "::" + TargetMethod;
            internal string CollisionKey => Target + (ArgumentTypesSpecified
                ? "(" + string.Join(",", ArgumentTypes.Select(NormalizeTypeName)) + ")"
                : "(*)");
            internal bool ArgumentTypesSpecified { get; }
            internal IReadOnlyList<string> ArgumentTypes { get; }
            internal bool HasTranspiler { get; }
            internal bool HasPrefix { get; }
            internal bool HasPostfix { get; }
            internal bool ReadsResult { get; }
            internal bool WritesResult { get; }
            internal bool HasSkippingPrefix { get; }
            internal bool HasExplicitOrder { get; }
            internal int? Priority { get; }
            internal IReadOnlyList<string> Before { get; }
            internal IReadOnlyList<string> After { get; }
            internal bool Dynamic { get; }
            internal bool ExactDynamicDescriptor { get; }
        }
        private sealed class InputBinding
        {
            internal InputBinding(string module, string name, string kind, string canonical)
            { Module = module; Name = name; Kind = kind; Canonical = canonical; }
            internal string Module { get; }
            internal string Name { get; }
            internal string Kind { get; }
            internal string Canonical { get; }
        }

        private sealed class ConfigInputSetting
        {
            internal ConfigInputSetting(
                string section,
                string name,
                string value,
                bool keyboardShortcut)
            {
                Section = section;
                Name = name;
                Value = value;
                KeyboardShortcut = keyboardShortcut;
            }

            internal string Section { get; }
            internal string Name { get; }
            internal string Value { get; }
            internal bool KeyboardShortcut { get; }
        }
    }
}

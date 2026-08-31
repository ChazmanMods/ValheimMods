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
            new ModuleSpec("World Engine", "RunicWorldEngine", "RunicWorldEngine", "chazman.RunicWorldEngine", "icon.png")
        };

        private static readonly ModuleSpec[] CompatibilityParticipants = Modules.Concat(new[]
        {
            new ModuleSpec("Core foundation", "RunicCore", "RunicCore", "chazman.RunicCore", "icon.png"),
            new ModuleSpec("Permissions foundation", "RunicPermissions", "RunicPermissions", "chazman.RunicPermissions", "icon.png"),
            new ModuleSpec("Transactions foundation", "RunicTransactions", "RunicTransactions", "chazman.RunicTransactions", "icon.png"),
            new ModuleSpec("Persistence foundation", "RunicPersistence", "RunicPersistence", "chazman.RunicPersistence", "icon.png"),
            new ModuleSpec("Build Camera companion", "RunicBuildCamera", "RunicBuildCamera", "chazman.RunicBuildCamera", "icon.png"),
            new ModuleSpec("Integrity companion", "RunicIntegrity", "RunicIntegrity", "chazman.RunicIntegrity", "icon.png")
        }).ToArray();

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
                ["Container::RPC_RequestOpen"] =
                    "Safety denies unresolved grave custody before Transactions evaluates an endpoint claim",
                ["Container::RPC_RequestTakeAll"] =
                    "Safety denies unresolved grave custody before Transactions evaluates an endpoint claim",
                ["Container::RPC_RequestStack"] =
                    "Safety denies unresolved grave custody before Transactions evaluates an endpoint claim",
                ["Container::CheckForChanges"] =
                    "Transactions suppresses claimed maintenance; the four gameplay postfixes only refresh event indices when vanilla remains eligible",
                ["Smelter::Awake"] = "additive station registration",
                ["CookingStation::Awake"] = "additive station registration",
                ["CraftingStation::GetHoverText"] = "bounded hover composition/capture",
                ["CookingStation::GetHoverText"] = "bounded hover composition/capture",
                ["Fermenter::GetHoverText"] = "bounded hover composition/capture",
                ["Plant::GetHoverText"] = "bounded hover composition/capture",
                ["Beehive::GetHoverText"] = "bounded hover composition/capture",
                ["Switch::GetHoverText"] = "bounded hover composition/capture",
                ["Player::UpdatePlacement"] = "Building transform and Crafting commit phases are independently scoped",
                ["Player::UpdatePlacementGhost"] = "Agriculture preview and Building final pose are independently gated",
                ["Player::Interact"] = "Production link selection and Agriculture harvest use exact disjoint targets",
                ["Player::ConsumeResources"] =
                    "Inventory reconciliation denial runs first; Crafting consumes its reservation only when the native resource path remains eligible",
                ["Player::CreateTombStone"] =
                    "Inventory opens an exception-finalized vanilla death-transfer scope and journals exact grave custody; Safety remains observational",
                ["Piece::SetCreator"] =
                    "Agriculture and Precision independently observe the completed native creator write for their bounded placement records",
                ["Player::SetLocalPlayer"] =
                    "Inventory topology rebinding and Build Camera session/effect cleanup are independent and idempotent",
                ["Player::SetControls"] =
                    "Inventory reconciliation and Build Camera keep their existing scoped controls; Production monotonically zeroes only the combat arguments owned by an accepted Alt link or Shift+Alt unlink gesture",
                ["Player::Update"] =
                    "Agriculture and Build Camera keep their existing scopes while Production samples only an exact station-targeted Alt link or Shift+Alt unlink gesture and never skips Player.Update",
                ["Player::TryPlacePiece"] =
                    "Inventory reconciliation denial runs before Crafting may reserve or start a remote build",
                ["Fermenter::Interact"] = "Interaction hold normalization precedes/does not bypass Production authority",
                ["CookingStation::OnUseItem"] = "Safety pre-mutation protection then Production ownership guard",
                ["CookingStation::OnAddFuelSwitch"] =
                    "Safety protection, Inventory lock denial, then Production ownership guard",
                ["Fermenter::AddItem"] =
                    "Safety protection, Inventory lock denial, then Production ownership guard",
                ["Humanoid::EquipItem"] =
                    "Inventory lock denial, Interaction temporary-equipment bookkeeping, then Inventory role relocation",
                ["Humanoid::Pickup"] =
                    "Inventory topology/filter denial precedes Interaction's independent pickup-filter denial",
                ["Humanoid::UnequipItem"] =
                    "Inventory reconciliation may deny the native unequip; Interaction's postfix only schedules a bounded no-op restore while the tool remains equipped",
                ["Humanoid::HideHandItems"] =
                    "Inventory reconciliation denial runs before Interaction captures native hidden-hand state",
                ["Humanoid::ShowHandItems"] =
                    "Inventory reconciliation denial precedes Interaction's post-vanilla hidden-hand restore observation",
                ["Incinerator::OnIncinerate"] =
                    "Safety confirmation/protection precedes Inventory's independent locked-item denial",
                ["Inventory::AddItem"] =
                    "Inventory reconciliation denies the exact native mutation before Crafting's null-safe output observation postfix",
                ["InventoryGui::DoCrafting"] =
                    "Inventory locked-upgrade denial precedes Crafting's exact material reservation/commit",
                ["InventoryGui::OnSelectedItem"] =
                    "Inventory locked-slot denial precedes Interaction's guarded transfer gesture",
                ["ItemStand::UseItem"] =
                    "Safety protection precedes Inventory's independent locked-item denial",
                ["Minimap::OnMapLeftClick"] =
                    "Exploration first vetoes clicks inside its owned panel; Portals then selects only an exact picker-owned marker while its modal session is active",
                ["Minimap::OnMapDblClick"] =
                    "Portals suppresses double-click map mutation only during its modal picker; Exploration independently vetoes input inside its owned panel",
                ["Minimap::OnMapMiddleClick"] =
                    "Portals suppresses middle-click map mutation only during its modal picker; Exploration independently vetoes input inside its owned panel",
                ["Minimap::OnMapRightClick"] =
                    "Portals suppresses right-click map mutation only during its modal picker; Exploration independently vetoes input inside its owned panel",
                ["Smelter::OnAddFuel"] =
                    "Safety protection precedes Inventory's independent locked-item denial",
                ["Smelter::OnAddOre"] =
                    "Safety protection precedes Inventory's independent locked-item denial",
                ["TextInput::Hide"] =
                    "Interaction and Portals independently clear only their own bounded edit sessions",
                ["ZInput::GetButton"] =
                    "Storage, Agriculture, and Inventory retain their audited paths; Production additionally denies only the combat or build action leased by an accepted station-targeted Alt link or Shift+Alt unlink gesture",
                ["ZInput::GetButtonDown"] =
                    "Storage, Agriculture, and Inventory retain their audited paths; Production additionally denies only the combat or build action leased by an accepted station-targeted Alt link or Shift+Alt unlink gesture",
                ["ZInput::GetButtonUp"] =
                    "Storage, Agriculture, and Inventory monotonically deny only their audited owned controller paths",
                ["ZInput::GetButtonPressedTimer"] =
                    "Storage, Agriculture, and Inventory monotonically deny only their audited owned controller paths",
                ["ZInput::GetButtonLastPressedTimer"] =
                    "Storage, Agriculture, and Inventory monotonically deny only their audited owned controller paths",
                ["WearNTear::Destroy"] =
                    "Transactions denies claimed endpoint destruction first; Portals next protects only an exact active picker source; Production then acquires station recovery state only while every earlier guard leaves the original eligible",
                ["WearNTear::Damage"] =
                    "Safety denies unresolved durable custody first; Portals may then monotonically deny damage only to its exact leased picker source",
                ["WearNTear::RPC_Damage"] =
                    "Safety denies unresolved durable custody first; Portals may then monotonically deny owner-RPC damage only to its exact leased picker source",
                ["ZDO::Deserialize"] =
                    "Agriculture Crafting Portals Production Precision and Storage independently advance only their bounded persistent-authority catalogue epochs after exact deserialization",
                ["ZDO::SetPrefab"] =
                    "Agriculture Crafting Portals Production Precision and Storage independently advance only their bounded persistent authority catalogues after an exact prefab transition",
                ["ZDOMan::AddToSector"] =
                    "Agriculture Crafting Portals Interaction Precision Production and Storage independently mark only their bounded persistent-authority ZDO indexes dirty after vanilla adds the record",
                ["ZDOMan::RemoveFromSector"] =
                    "Agriculture Crafting Portals Interaction Precision Production and Storage independently mark only their bounded persistent-authority ZDO indexes dirty before vanilla removes the record"
            };

        // A rationale alone can silently bless a newly arriving participant. Pin the exact
        // module set for every accepted overlap so any new patch owner forces a fresh review.
        private static readonly Dictionary<string, string[]> ApprovedOverlapModules =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["Beehive::GetHoverText"] = new[] { "RunicAgriculture", "RunicAwareness" },
                ["Container::Awake"] = new[] { "RunicAgriculture", "RunicCrafting", "RunicProduction", "RunicStorage" },
                ["Container::CheckForChanges"] = new[] { "RunicAgriculture", "RunicCrafting", "RunicProduction", "RunicStorage", "RunicTransactions" },
                ["Container::OnDestroyed"] = new[] { "RunicAgriculture", "RunicCrafting", "RunicProduction", "RunicStorage" },
                ["Container::RPC_RequestOpen"] = new[] { "RunicSafety", "RunicTransactions" },
                ["Container::RPC_RequestStack"] = new[] { "RunicSafety", "RunicTransactions" },
                ["Container::RPC_RequestTakeAll"] = new[] { "RunicSafety", "RunicTransactions" },
                ["CookingStation::Awake"] = new[] { "RunicInteraction", "RunicProduction" },
                ["CookingStation::GetHoverText"] = new[] { "RunicAwareness", "RunicProduction" },
                ["CookingStation::OnAddFuelSwitch"] = new[] { "RunicInventory", "RunicProduction", "RunicSafety" },
                ["CookingStation::OnUseItem"] = new[] { "RunicProduction", "RunicSafety" },
                ["CraftingStation::GetHoverText"] = new[] { "RunicAwareness", "RunicProduction" },
                ["Fermenter::AddItem"] = new[] { "RunicInventory", "RunicProduction", "RunicSafety" },
                ["Fermenter::GetHoverText"] = new[] { "RunicAwareness", "RunicProduction" },
                ["Fermenter::Interact"] = new[] { "RunicInteraction", "RunicProduction" },
                ["Humanoid::EquipItem"] = new[] { "RunicInteraction", "RunicInventory" },
                ["Humanoid::HideHandItems"] = new[] { "RunicInteraction", "RunicInventory" },
                ["Humanoid::Pickup"] = new[] { "RunicInteraction", "RunicInventory" },
                ["Humanoid::ShowHandItems"] = new[] { "RunicInteraction", "RunicInventory" },
                ["Humanoid::UnequipItem"] = new[] { "RunicInteraction", "RunicInventory" },
                ["Incinerator::OnIncinerate"] = new[] { "RunicInventory", "RunicSafety" },
                ["Inventory::AddItem"] = new[] { "RunicCrafting", "RunicInventory" },
                ["InventoryGui::DoCrafting"] = new[] { "RunicCrafting", "RunicInventory" },
                ["InventoryGui::OnSelectedItem"] = new[] { "RunicInteraction", "RunicInventory" },
                ["ItemStand::UseItem"] = new[] { "RunicInventory", "RunicSafety" },
                ["Minimap::OnMapDblClick"] = new[] { "RunicExploration", "RunicPortals" },
                ["Minimap::OnMapLeftClick"] = new[] { "RunicExploration", "RunicPortals" },
                ["Minimap::OnMapMiddleClick"] = new[] { "RunicExploration", "RunicPortals" },
                ["Minimap::OnMapRightClick"] = new[] { "RunicExploration", "RunicPortals" },
                ["Plant::GetHoverText"] = new[] { "RunicAgriculture", "RunicAwareness" },
                ["Player::Interact"] = new[] { "RunicAgriculture", "RunicProduction" },
                ["Player::ConsumeResources"] = new[] { "RunicCrafting", "RunicInventory" },
                ["Player::CreateTombStone"] = new[] { "RunicInventory", "RunicSafety" },
                ["Piece::SetCreator"] = new[] { "RunicAgriculture", "RunicPrecisionBuildTool" },
                ["Player::SetLocalPlayer"] = new[] { "RunicBuildCamera", "RunicInventory" },
                ["Player::SetControls"] = new[] { "RunicBuildCamera", "RunicInventory", "RunicProduction" },
                ["Player::TryPlacePiece"] = new[] { "RunicCrafting", "RunicInventory" },
                ["Player::Update"] = new[] { "RunicAgriculture", "RunicBuildCamera", "RunicProduction" },
                ["Player::UpdatePlacement"] = new[] { "RunicBuildCamera", "RunicCrafting", "RunicPrecisionBuildTool" },
                ["Player::UpdatePlacementGhost"] = new[] { "RunicAgriculture", "RunicBuildCamera", "RunicPrecisionBuildTool" },
                ["Smelter::Awake"] = new[] { "RunicInteraction", "RunicProduction" },
                ["Smelter::OnAddFuel"] = new[] { "RunicInventory", "RunicSafety" },
                ["Smelter::OnAddOre"] = new[] { "RunicInventory", "RunicSafety" },
                ["Switch::GetHoverText"] = new[] { "RunicAwareness", "RunicProduction" },
                ["TextInput::Hide"] = new[] { "RunicInteraction", "RunicPortals" },
                ["WearNTear::Damage"] = new[] { "RunicPortals", "RunicSafety" },
                ["WearNTear::Destroy"] = new[] { "RunicPortals", "RunicProduction", "RunicTransactions" },
                ["WearNTear::RPC_Damage"] = new[] { "RunicPortals", "RunicSafety" },
                ["ZDO::Deserialize"] = new[] { "RunicAgriculture", "RunicCrafting", "RunicPortals", "RunicPrecisionBuildTool", "RunicProduction", "RunicStorage" },
                ["ZDO::SetPrefab"] = new[] { "RunicAgriculture", "RunicCrafting", "RunicPortals", "RunicPrecisionBuildTool", "RunicProduction", "RunicStorage" },
                ["ZInput::GetButton"] = new[] { "RunicAgriculture", "RunicInventory", "RunicProduction", "RunicStorage" },
                ["ZInput::GetButtonDown"] = new[] { "RunicAgriculture", "RunicInventory", "RunicProduction", "RunicStorage" },
                ["ZInput::GetButtonLastPressedTimer"] = new[] { "RunicAgriculture", "RunicInventory", "RunicStorage" },
                ["ZInput::GetButtonPressedTimer"] = new[] { "RunicAgriculture", "RunicInventory", "RunicStorage" },
                ["ZInput::GetButtonUp"] = new[] { "RunicAgriculture", "RunicInventory", "RunicStorage" },
                ["ZDOMan::AddToSector"] = new[] { "RunicAgriculture", "RunicCrafting", "RunicInteraction", "RunicPortals", "RunicPrecisionBuildTool", "RunicProduction", "RunicStorage" },
                ["ZDOMan::RemoveFromSector"] = new[] { "RunicAgriculture", "RunicCrafting", "RunicInteraction", "RunicPortals", "RunicPrecisionBuildTool", "RunicProduction", "RunicStorage" }
            };

        private static readonly Dictionary<string, string> ApprovedInputOverlaps =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> PersistentWorldStateOwners =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "RunicCrafting",
                "RunicProduction",
                "RunicPortals",
                "RunicTransactions"
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
                ("the original design's exact fourteen module identities exist", ExactFourteenExist),
                ("every module has a test project and complete release surface", ReleaseSurfacesAreComplete),
                ("client server BepInEx Harmony and Cecil binaries match the audited environment", EnvironmentBinariesArePinned),
                ("plugin manifest assembly and package versions align", IdentitiesAlign),
                ("all fourteen GUIDs assemblies and package names are unique", IdentitiesAreUnique),
                ("gameplay modules have no hard gameplay-module references", NoGameplayHardReferences),
                ("optional peer discovery references only public contract namespaces", PrivatePeerReflectionIsAbsent),
                ("gameplay manifest hard dependencies are explicit and acyclic", GameplayManifestDependenciesAreExplicit),
                ("compiled and packaged Foundation dependency floors align", FoundationDependencyFloorsAlign),
                ("suite capability roots match Runic Core's canonical contract", CanonicalCapabilityRootsAlign),
                ("dedicated RPC contracts are connection-bound and durable paths are classified", DedicatedRpcContractsAreCanonical),
                ("direct persistent ZDO writes stay with declared single-purpose owners", PersistentWritesHaveOwners),
                ("all fourteen package icons are exact 256x256 PNG", IconsAreExact),
                ("all four Foundation icons are the selected unique medallions", FoundationIconsAreSelected),
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
            Console.WriteLine($"{tests.Length - failed}/{tests.Length} suite-14 audits passed.");
            return failed == 0 ? 0 : 1;
        }

        private static void ExactFourteenExist()
        {
            Equal(14, Modules.Length);
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
                string nestedTests = Path.Combine(directory, "Tests", module.Assembly + ".Tests.csproj");
                True(File.Exists(siblingTests) || File.Exists(nestedTests),
                    "Missing focused test project: " + module.Assembly + ".Tests");
                True(File.Exists(Path.Combine(directory, "CHANGELOG.md")),
                    "Missing changelog: " + module.Assembly);
                string[] examples = Directory.GetFiles(directory, "*.cfg.example", SearchOption.TopDirectoryOnly);
                Equal(1, examples.Length);

                using JsonDocument manifest = JsonDocument.Parse(
                    File.ReadAllText(Path.Combine(directory, "manifest.json")));
                string[] dependencies = manifest.RootElement.GetProperty("dependencies")
                    .EnumerateArray().Select(value => value.GetString() ?? string.Empty).ToArray();
                True(dependencies.Length >= 1 && dependencies.Length <= 8,
                    module.Assembly + " dependency list is missing or unbounded.");
                Equal(dependencies.Length,
                    dependencies.Distinct(StringComparer.OrdinalIgnoreCase).Count());
                Equal(1, dependencies.Count(value => string.Equals(
                    value, "denikson-BepInExPack_Valheim-5.4.2333", StringComparison.Ordinal)));
            }
        }

        private static void EnvironmentBinariesArePinned()
        {
            string managed = FindManagedDirectory();
            string clientAssembly = Path.Combine(managed, "assembly_valheim.dll");
            Equal("3B26C8512778F6E0664B5AF2A26F3C30993A00F584C1E76D9123A742B67E2004",
                Sha256Hex(clientAssembly));
            using (AssemblyDefinition game = AssemblyDefinition.ReadAssembly(clientAssembly))
            {
                TypeDefinition version = game.MainModule.Types.Single(value => value.FullName == "Version");
                MethodDefinition initializer = version.Methods.Single(value => value.Name == ".cctor");
                IList<Mono.Cecil.Cil.Instruction> instructions = initializer.Body.Instructions;
                True(instructions.Count >= 5 &&
                     ReadInt32Constant(instructions[0]) == 0 &&
                     ReadInt32Constant(instructions[1]) == 221 &&
                     ReadInt32Constant(instructions[2]) == 12 &&
                     instructions[3].OpCode.Code == Mono.Cecil.Cil.Code.Newobj &&
                     string.Equals(((MethodReference)instructions[3].Operand).DeclaringType.FullName,
                         "GameVersion", StringComparison.Ordinal) &&
                     instructions[4].OpCode.Code == Mono.Cecil.Cil.Code.Stsfld &&
                     string.Equals(((FieldReference)instructions[4].Operand).Name,
                         "<CurrentVersion>k__BackingField", StringComparison.Ordinal),
                    "Installed client assembly does not identify audited Valheim 0.221.12.");
            }

            string core = FindBepInExCoreDirectory();
            string bepInEx = Path.Combine(core, "BepInEx.dll");
            string harmony = Path.Combine(core, "0Harmony.dll");
            string cecil = Path.Combine(core, "Mono.Cecil.dll");
            Equal("E9AC3A950E91E71B13DF5480B36CE06AF27E981A688F0E62125B674D03A0713A", Sha256Hex(bepInEx));
            Equal("1A21CC03424FC82C3DD1346905D16494536B9595AE4162228D99FB7C285C1031", Sha256Hex(harmony));
            Equal("7AE470288FFF4A402899C254D0A76CEFEF55877F5C54F96E83C797CC5BB6E2F6", Sha256Hex(cecil));
            Equal("5.4.23.3", System.Diagnostics.FileVersionInfo.GetVersionInfo(bepInEx).FileVersion);
            Equal("2.9.0.0", System.Diagnostics.FileVersionInfo.GetVersionInfo(harmony).FileVersion);

            string dedicatedRoot = Environment.GetEnvironmentVariable("VALHEIM_DEDICATED_INSTALL");
            if (string.IsNullOrWhiteSpace(dedicatedRoot))
                dedicatedRoot = @"E:\SteamLibrary\steamapps\common\Valheim dedicated server";
            string serverAssembly = Path.Combine(
                dedicatedRoot, "valheim_server_Data", "Managed", "assembly_valheim.dll");
            string serverExecutable = Path.Combine(dedicatedRoot, "valheim_server.exe");
            True(File.Exists(serverAssembly) && File.Exists(serverExecutable),
                "Audited dedicated-server install is unavailable.");
            Equal("84A1B34F95774D36BE328390578D7B07C5CFFBC8CBB15119541900F055D486A3",
                Sha256Hex(serverAssembly));
            Equal("A1E5ACCF766C1177A7E0B82B457CBED74CB3C9EFB5EE8E5C1E0BBBB60BD52839",
                Sha256Hex(serverExecutable));
        }

        private static void IdentitiesAreUnique()
        {
            Equal(14, Modules.Select(value => value.Guid).Distinct(StringComparer.Ordinal).Count());
            Equal(14, Modules.Select(value => value.Assembly).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            var packageNames = Modules.Select(module =>
            {
                using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(PathOf(module.Directory, "manifest.json")));
                return manifest.RootElement.GetProperty("name").GetString();
            }).ToArray();
            Equal(14, packageNames.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        private static void NoGameplayHardReferences()
        {
            var gameplay = new HashSet<string>(Modules.Select(value => value.Assembly), StringComparer.OrdinalIgnoreCase);
            foreach (ModuleSpec module in CompatibilityParticipants)
            {
                using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(AssemblyPath(module));
                string[] forbidden = assembly.MainModule.AssemblyReferences.Select(value => value.Name)
                    .Where(value => gameplay.Contains(value)).ToArray();
                True(forbidden.Length == 0, module.Assembly + " hard references gameplay peer(s): " + string.Join(",", forbidden));
            }
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
            const string storageInventory = "Chazman-RunicInventory-1.0.0";
            int storageInventoryCount = 0;
            foreach (ModuleSpec module in Modules)
            {
                using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(PathOf(module.Directory, "manifest.json")));
                foreach (JsonElement dependency in manifest.RootElement.GetProperty("dependencies").EnumerateArray())
                {
                    string value = dependency.GetString() ?? string.Empty;
                    if (!gameplayPackages.Any(package => value.StartsWith(package + "-", StringComparison.OrdinalIgnoreCase)))
                        continue;
                    bool exactStorageInventory = string.Equals(module.Assembly, "RunicStorage", StringComparison.Ordinal) &&
                                                 string.Equals(value, storageInventory, StringComparison.Ordinal);
                    True(exactStorageInventory,
                        module.Assembly + " has an undeclared gameplay dependency " + value + ".");
                    storageInventoryCount++;
                }
            }
            Equal(1, storageInventoryCount);

            using JsonDocument inventoryManifest = JsonDocument.Parse(
                File.ReadAllText(PathOf("RunicInventory", "manifest.json")));
            True(!inventoryManifest.RootElement.GetProperty("dependencies").EnumerateArray()
                    .Select(value => value.GetString() ?? string.Empty)
                    .Any(value => value.StartsWith("Chazman-RunicStorage-", StringComparison.OrdinalIgnoreCase)),
                "Storage -> Inventory must remain acyclic; Inventory may not depend on Storage.");
        }

        private static void FoundationDependencyFloorsAlign()
        {
            var foundations = new Dictionary<string, FoundationSpec>(StringComparer.Ordinal)
            {
                ["chazman.RunicCore"] = new FoundationSpec("RunicCore", "Chazman-RunicCore-"),
                ["chazman.RunicPermissions"] = new FoundationSpec("RunicPermissions", "Chazman-RunicPermissions-"),
                ["chazman.RunicTransactions"] = new FoundationSpec("RunicTransactions", "Chazman-RunicTransactions-"),
                ["chazman.RunicPersistence"] = new FoundationSpec("RunicPersistence", "Chazman-RunicPersistence-")
            };
            foreach (FoundationSpec foundation in foundations.Values)
            {
                using JsonDocument foundationManifest = JsonDocument.Parse(
                    File.ReadAllText(PathOf(foundation.Directory, "manifest.json")));
                foundation.CurrentVersion = foundationManifest.RootElement
                    .GetProperty("version_number").GetString() ?? string.Empty;
                True(Version.TryParse(foundation.CurrentVersion, out _),
                    foundation.Directory + " has an invalid current release version.");
            }
            using var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(FindBepInExCoreDirectory());
            resolver.AddSearchDirectory(FindManagedDirectory());
            foreach (ModuleSpec module in Modules)
            {
                resolver.AddSearchDirectory(Path.GetDirectoryName(AssemblyPath(module)) ?? string.Empty);
                using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(
                    AssemblyPath(module),
                    new ReaderParameters { AssemblyResolver = resolver });
                TypeDefinition plugin = assembly.MainModule.Types.Single(type =>
                    type.CustomAttributes.Any(attribute =>
                        attribute.AttributeType.FullName == "BepInEx.BepInPlugin"));
                var compiled = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (CustomAttribute dependency in plugin.CustomAttributes.Where(attribute =>
                             attribute.AttributeType.FullName == "BepInEx.BepInDependency"))
                {
                    if (dependency.ConstructorArguments.Count < 2 ||
                        !(dependency.ConstructorArguments[0].Value is string guid) ||
                        !(dependency.ConstructorArguments[1].Value is string version) ||
                        !foundations.ContainsKey(guid)) continue;
                    compiled.Add(guid, version);
                }

                using JsonDocument manifest = JsonDocument.Parse(
                    File.ReadAllText(PathOf(module.Directory, "manifest.json")));
                string[] packaged = manifest.RootElement.GetProperty("dependencies")
                    .EnumerateArray().Select(value => value.GetString() ?? string.Empty).ToArray();
                foreach (KeyValuePair<string, FoundationSpec> foundation in foundations)
                {
                    string[] entries = packaged.Where(value =>
                        value.StartsWith(foundation.Value.PackagePrefix, StringComparison.Ordinal)).ToArray();
                    if (!compiled.TryGetValue(foundation.Key, out string floor))
                    {
                        True(entries.Length == 0,
                            module.Assembly + " manifest declares " + foundation.Key +
                            " but the compiled plugin has no matching hard dependency: " +
                            string.Join(",", entries));
                        continue;
                    }
                    True(entries.Length == 1,
                        module.Assembly + " compiled dependency " + foundation.Key +
                        " must have exactly one manifest entry; found " + string.Join(",", entries));
                    True(string.Equals(foundation.Value.CurrentVersion, floor, StringComparison.Ordinal),
                        module.Assembly + " compiled floor for " + foundation.Key + " is " + floor +
                        " but the current Foundation release is " + foundation.Value.CurrentVersion + ".");
                    True(string.Equals(foundation.Value.PackagePrefix + floor, entries[0], StringComparison.Ordinal),
                        module.Assembly + " manifest floor for " + foundation.Key + " is " + entries[0] +
                        " but the compiled floor requires " + foundation.Value.PackagePrefix + floor + ".");
                }
            }
        }

        private static void CanonicalCapabilityRootsAlign()
        {
            string corePath = PathOf("RunicCore", "bin", "Release", "netstandard2.1", "RunicCore.dll");
            using AssemblyDefinition core = AssemblyDefinition.ReadAssembly(corePath);
            using AssemblyDefinition sentinel = AssemblyDefinition.ReadAssembly(
                PathOf("RunicSentinel", "bin", "Release", "netstandard2.1", "RunicSentinel.dll"));
            using AssemblyDefinition world = AssemblyDefinition.ReadAssembly(
                PathOf("RunicWorldEngine", "bin", "Release", "netstandard2.1", "RunicWorldEngine.dll"));
            using AssemblyDefinition inventory = AssemblyDefinition.ReadAssembly(
                PathOf("RunicInventory", "bin", "Release", "netstandard2.1", "RunicInventory.dll"));
            using AssemblyDefinition safety = AssemblyDefinition.ReadAssembly(
                PathOf("RunicSafety", "bin", "Release", "netstandard2.1", "RunicSafety.dll"));
            using AssemblyDefinition portals = AssemblyDefinition.ReadAssembly(
                PathOf("RunicPortals", "bin", "Release", "netstandard2.1", "RunicPortals.dll"));
            using AssemblyDefinition storage = AssemblyDefinition.ReadAssembly(
                PathOf("RunicStorage", "bin", "Release", "netstandard2.1", "RunicStorage.dll"));
            using AssemblyDefinition interaction = AssemblyDefinition.ReadAssembly(
                PathOf("RunicInteraction", "bin", "Release", "netstandard2.1", "RunicInteraction.dll"));

            string securityAttest = Constant(
                core, "Runic.Foundation.Core.RunicCapabilityIds", "SecurityAttest");
            string securityEvidence = Constant(
                core, "Runic.Foundation.Core.RunicCapabilityIds", "SecurityEvidence");
            string securityAdmission = Constant(
                core, "Runic.Foundation.Core.RunicCapabilityIds", "SecurityAdmission");
            string zdoOwnership = Constant(
                core, "Runic.Foundation.Core.RunicCapabilityIds", "ZdoOwnership");
            string zdoObserve = Constant(
                core, "Runic.Foundation.Core.RunicCapabilityIds", "ZdoObserve");
            string itemLocks = Constant(
                core, "Runic.Foundation.Core.RunicCapabilityIds", "InventoryItemLocks");
            string durableInventory = Constant(
                core, "Runic.Foundation.Core.RunicCapabilityIds", "InventoryDurableOperations");
            string safetyConfirmation = Constant(
                core, "Runic.Foundation.Core.RunicCapabilityIds", "SafetyConfirmation");

            Equal(securityAttest,
                Constant(sentinel, "RunicSentinel.Contracts.SentinelCapabilityIds", "Attestation"));
            Equal(securityEvidence,
                Constant(sentinel, "RunicSentinel.Contracts.SentinelCapabilityIds", "Evidence"));
            Equal(securityAdmission,
                Constant(sentinel, "RunicSentinel.Contracts.SentinelCapabilityIds", "Admission"));
            Equal(itemLocks,
                Constant(inventory, "RunicInventory.Capabilities.InventoryCapabilityIds", "ItemLocks"));
            Equal(itemLocks,
                Constant(safety, "RunicSafety.Api.SafetyCapabilityIds", "InventoryProtection"));
            Equal(safetyConfirmation,
                Constant(safety, "RunicSafety.Api.SafetyCapabilityIds", "Confirmation"));

            True(HasStringLiteral(world, zdoOwnership),
                "World Engine does not publish Core's canonical ownership capability.");
            True(HasStringLiteral(world, zdoObserve),
                "World Engine does not publish Core's canonical observation capability.");
            True(!HasStringLiteral(world, "world.zdo.ownership-registry"),
                "World Engine still embeds its superseded ownership capability.");
            True(!HasStringLiteral(sentinel, "security.attestation"),
                "Sentinel still embeds its superseded attestation capability.");

            RequirePublicCoreContract(core, "Runic.Foundation.Core.IItemProtectionQuery");
            RequirePublicCoreContract(core, "Runic.Foundation.Core.IInventoryDurableOperationService");
            RequirePublicCoreContract(core, "Runic.Foundation.Core.InventoryDurableOperationIntent");
            RequirePublicCoreContract(core, "Runic.Foundation.Core.InventoryDurableOperationSnapshot");
            RequirePublicCoreContract(core, "Runic.Foundation.Core.InventoryDurableCustodySnapshot");
            RequirePublicCoreContract(core, "Runic.Foundation.Core.InventoryDurableCustodyEvidenceSource");
            RequirePublicCoreContract(core, "Runic.Foundation.Core.InventoryDurableCustodyResolution");
            RequirePublicCoreContract(core, "Runic.Foundation.Core.InventoryDurableCustodyResolutionOutcome");
            RequirePublicCoreContract(core, "Runic.Foundation.Core.InventoryDurableServerCustodyBinding");
            RequirePublicCoreContract(core, "Runic.Foundation.Core.InventoryDurableOutstandingOutcome");
            RequirePublicCoreContract(core, "Runic.Foundation.Core.InventoryDurableOutstandingObservation");
            RequirePublicCoreContract(core, "Runic.Foundation.Core.InventoryDurableOutstandingOperation");
            RequirePublicCoreContract(core, "Runic.Foundation.Core.InventoryDurableOutstandingSnapshot");
            RequirePublicCoreContract(core, "Runic.Foundation.Core.InventoryDurableFreshSessionProof");
            RequirePublicCoreContract(core, "Runic.Foundation.Core.InventoryDurableProfileSource");
            RequirePublicCoreContract(core, "Runic.Foundation.Core.InventoryDurableOperationPhase");
            RequirePublicCoreContract(core, "Runic.Foundation.Core.IHighImpactConfirmation");
            RequirePublicCoreContract(core, "Runic.Foundation.Core.ConfirmationContext");
            RequirePublicCoreContract(core, "Runic.Foundation.Core.ModuleRegistration");
            True(ReferencesType(inventory, "Runic.Foundation.Core.IItemProtectionQuery"),
                "Inventory does not implement Core's canonical item-lock query.");
            True(ReferencesType(inventory, "Runic.Foundation.Core.IInventoryDurableOperationService"),
                "Inventory does not implement Core's canonical durable-operation service.");
            True(HasStringLiteral(inventory, durableInventory),
                "Inventory does not publish Core's canonical durable-operation capability ID.");
            foreach (string duplicate in new[]
                     {
                         "InventoryDurableMutationKind",
                         "InventoryDurableOperationPhase",
                         "InventoryProfileReadbackState",
                         "InventoryDurableProfileSource",
                         "InventoryDurableCustodyKind",
                         "InventoryDurableCustodyEvidenceSource",
                         "InventoryDurableCustodySnapshot",
                         "InventoryDurableCustodyResolution",
                         "InventoryDurableCustodyResolutionOutcome",
                         "InventoryDurableServerCustodyBinding",
                         "InventoryDurableOutstandingOutcome",
                         "InventoryDurableOutstandingObservation",
                         "InventoryDurableOutstandingOperation",
                         "InventoryDurableOutstandingSnapshot",
                         "InventoryDurableFreshSessionProof",
                         "InventoryDurableOperationIntent",
                         "InventoryDurableOperationSnapshot",
                         "IInventoryDurableOperationService"
                     })
                True(!inventory.MainModule.Types.Any(type =>
                        string.Equals(type.Name, duplicate, StringComparison.Ordinal)),
                    "Inventory duplicates Core durable contract type " + duplicate + ".");
            string inventoryCapabilitiesSource = File.ReadAllText(PathOf(
                "RunicInventory", "Capabilities", "InventoryCapabilityIds.cs"));
            True(!inventoryCapabilitiesSource.Contains(
                    "\"inventory.durable-operations\"", StringComparison.Ordinal),
                "Inventory duplicates Core's durable-operation capability literal.");
            True(ReferencesType(safety, "Runic.Foundation.Core.IItemProtectionQuery"),
                "Safety does not consume Core's canonical item-lock query.");
            True(ReferencesType(storage, "Runic.Foundation.Core.IItemProtectionQuery"),
                "Storage does not consume Core's canonical item-lock query.");
            True(ReferencesType(interaction, "Runic.Foundation.Core.IItemProtectionQuery"),
                "Interaction does not consume Core's canonical item-lock query.");
            True(ReferencesType(safety, "Runic.Foundation.Core.IHighImpactConfirmation"),
                "Safety does not provide Core's canonical confirmation contract.");
            True(ReferencesType(portals, "Runic.Foundation.Core.IHighImpactConfirmation"),
                "Portals does not consume Core's canonical confirmation contract.");
            foreach (AssemblyDefinition consumer in new[]
                     { inventory, storage, interaction, safety, portals, sentinel, world })
            {
                True(!AllTypes(consumer.MainModule.Types).Any(type =>
                        string.Equals(type.FullName, "Runic.Foundation.Core.IItemProtectionQuery", StringComparison.Ordinal) ||
                        string.Equals(type.FullName, "Runic.Foundation.Core.IHighImpactConfirmation", StringComparison.Ordinal) ||
                        string.Equals(type.FullName, "Runic.Foundation.Core.ConfirmationContext", StringComparison.Ordinal)),
                    consumer.Name.Name + " duplicates a Core-owned shared contract.");
            }
        }

        private static void DedicatedRpcContractsAreCanonical()
        {
            using AssemblyDefinition core = AssemblyDefinition.ReadAssembly(
                PathOf("RunicCore", "bin", "Release", "netstandard2.1", "RunicCore.dll"));
            using AssemblyDefinition persistence = AssemblyDefinition.ReadAssembly(
                PathOf("RunicPersistence", "bin", "Release", "netstandard2.1", "RunicPersistence.dll"));
            using AssemblyDefinition transactions = AssemblyDefinition.ReadAssembly(
                PathOf("RunicTransactions", "bin", "Release", "netstandard2.1", "RunicTransactions.dll"));
            using AssemblyDefinition production = AssemblyDefinition.ReadAssembly(
                PathOf("RunicProduction", "bin", "Release", "netstandard2.1", "RunicProduction.dll"));
            using AssemblyDefinition portals = AssemblyDefinition.ReadAssembly(
                PathOf("RunicPortals", "bin", "Release", "netstandard2.1", "RunicPortals.dll"));
            using AssemblyDefinition safety = AssemblyDefinition.ReadAssembly(
                PathOf("RunicSafety", "bin", "Release", "netstandard2.1", "RunicSafety.dll"));
            using AssemblyDefinition sentinel = AssemblyDefinition.ReadAssembly(
                PathOf("RunicSentinel", "bin", "Release", "netstandard2.1", "RunicSentinel.dll"));

            string networkRpc = Constant(
                core, "Runic.Foundation.Core.RunicCapabilityIds", "NetworkRpc");
            Equal("network.rpc", networkRpc);
            True(HasStringLiteral(persistence, networkRpc),
                "Persistence does not publish Core's canonical direct-RPC capability.");

            string[] sharedContracts =
            {
                "Runic.Foundation.Persistence.IRunicRpcService",
                "Runic.Foundation.Persistence.RpcEndpointDescriptor",
                "Runic.Foundation.Persistence.RpcPeerSnapshot",
                "Runic.Foundation.Persistence.RpcRequestContext",
                "Runic.Foundation.Persistence.RpcPeerRequirement",
                "Runic.Foundation.Persistence.RpcReplayDurability",
                "Runic.Foundation.Persistence.IRpcPlayerBindingResolver"
            };
            foreach (string contract in sharedContracts)
                RequirePublicCoreContract(persistence, contract);

            TypeDefinition service = AllTypes(persistence.MainModule.Types).Single(type =>
                string.Equals(type.FullName,
                    "Runic.Foundation.Persistence.IRunicRpcService", StringComparison.Ordinal));
            foreach (string method in new[]
                     {
                         "RegisterEndpoint", "RegisterPeerRequirement",
                         "RegisterPlayerBindingResolver", "RegisterHandshakeClaimProvider",
                         "RegisterHandshakeClaimEvaluator", "SendToServer", "SendToPeer",
                         "TryGetPeer", "TryResolveActor", "GetPeers", "TryDisconnectPeer"
                     })
                True(service.Methods.Any(value => string.Equals(value.Name, method, StringComparison.Ordinal)),
                    "The direct-RPC contract is missing " + method + ".");

            TypeDefinition durability = AllTypes(persistence.MainModule.Types).Single(type =>
                string.Equals(type.FullName,
                    "Runic.Foundation.Persistence.RpcReplayDurability", StringComparison.Ordinal));
            FieldDefinition handlerDurable = durability.Fields.Single(field =>
                string.Equals(field.Name, "HandlerDurable", StringComparison.Ordinal));
            Equal(1, Convert.ToInt32(handlerDurable.Constant));

            RequirePublicCoreContract(
                transactions,
                "Runic.Foundation.Transactions.IDurableCompositeTokenDispositionSource");
            RequirePublicCoreContract(
                transactions,
                "Runic.Foundation.Transactions.DurableCompositeTokenDispositionState");
            RequirePublicCoreContract(
                transactions,
                "Runic.Foundation.Transactions.DurableCompositeTokenDispositionResult");
            TypeDefinition disposition = AllTypes(transactions.MainModule.Types).Single(type =>
                string.Equals(type.FullName,
                    "Runic.Foundation.Transactions.IDurableCompositeTokenDispositionSource",
                    StringComparison.Ordinal));
            True(disposition.Methods.Any(method =>
                    string.Equals(method.Name, "QueryTokenDisposition", StringComparison.Ordinal)),
                "Transactions does not expose the authenticated retired-marker disposition query.");
            TypeDefinition compositeFactory = AllTypes(transactions.MainModule.Types).Single(type =>
                string.Equals(type.FullName,
                    "Runic.Foundation.Transactions.IDurableCompositeOperationCoordinatorFactory",
                    StringComparison.Ordinal));
            True(compositeFactory.Interfaces.Any(contract => string.Equals(
                    contract.InterfaceType.FullName,
                    "Runic.Foundation.Transactions.IDurableCompositeTokenDispositionSource",
                    StringComparison.Ordinal)),
                "The shared composite factory does not own the authoritative token disposition query.");

            foreach ((AssemblyDefinition Assembly, string Name) consumer in new[]
                     {
                         (transactions, "Transactions"),
                         (production, "Production"),
                         (portals, "Portals")
                     })
            {
                True(ReferencesType(consumer.Assembly,
                        "Runic.Foundation.Persistence.IRunicRpcService"),
                    consumer.Name + " does not consume the canonical direct-RPC service.");
                True(ReferencesType(consumer.Assembly,
                        "Runic.Foundation.Persistence.RpcPeerSnapshot"),
                    consumer.Name + " does not bind work to an exact peer session snapshot.");
                True(ReferencesMethod(consumer.Assembly,
                        "Runic.Foundation.Persistence.IRunicRpcService", "RegisterEndpoint"),
                    consumer.Name + " does not register a typed direct-RPC endpoint.");
                foreach (string contract in sharedContracts)
                    True(!AllTypes(consumer.Assembly.MainModule.Types).Any(type =>
                            string.Equals(type.FullName, contract, StringComparison.Ordinal)),
                        consumer.Name + " duplicates Persistence-owned RPC contract " + contract + ".");
            }

            True(ReferencesMethod(transactions,
                    "Runic.Foundation.Persistence.IRunicRpcService", "SendToPeer"),
                "Transactions ownership return is not sent to an exact direct peer session.");
            foreach ((AssemblyDefinition Assembly, string Name) durableConsumer in new[]
                     { (production, "Production"), (portals, "Portals") })
            {
                True(ReferencesType(durableConsumer.Assembly,
                        "Runic.Foundation.Persistence.RpcReplayDurability"),
                    durableConsumer.Name + " does not classify persistent mutations separately from session replay.");
                True(ReferencesType(durableConsumer.Assembly,
                        "Runic.Foundation.Persistence.RpcActorAssurance"),
                    durableConsumer.Name + " does not request an explicit server actor-assurance level.");
                True(ReferencesMethod(durableConsumer.Assembly,
                        "Runic.Foundation.Persistence.IRunicRpcService", "TryResolveActor"),
                    durableConsumer.Name + " does not resolve its actor through the exact direct session.");
            }

            foreach ((AssemblyDefinition Assembly, string Name) admissionConsumer in new[]
                     { (safety, "Safety"), (sentinel, "Sentinel") })
            {
                True(ReferencesType(admissionConsumer.Assembly,
                        "Runic.Foundation.Persistence.IRunicRpcService"),
                    admissionConsumer.Name + " does not consume the canonical direct-RPC service.");
                True(ReferencesType(admissionConsumer.Assembly,
                        "Runic.Foundation.Persistence.RpcPeerRequirement"),
                    admissionConsumer.Name + " does not declare a typed pre-entry peer requirement.");
                foreach (string method in new[]
                         {
                             "RegisterHandshakeClaimProvider",
                             "RegisterHandshakeClaimEvaluator",
                             "RegisterPeerRequirement"
                         })
                    True(ReferencesMethod(admissionConsumer.Assembly,
                            "Runic.Foundation.Persistence.IRunicRpcService", method),
                        admissionConsumer.Name + " does not use canonical admission method " + method + ".");
                foreach (string contract in sharedContracts)
                    True(!AllTypes(admissionConsumer.Assembly.MainModule.Types).Any(type =>
                            string.Equals(type.FullName, contract, StringComparison.Ordinal)),
                        admissionConsumer.Name + " duplicates Persistence-owned RPC contract " + contract + ".");
            }
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

        private static void FoundationIconsAreSelected()
        {
            var expected = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["RunicCore"] = "63C37D84B730A81572CF19E362260EEFB8853B4EAB3A3B545F3B6D71ABAD0E4E",
                ["RunicPermissions"] = "EACDD55557A662CB4A3F98B74A0CA0125F10CAB88C1336F79FD0A4B8BD73B3CC",
                ["RunicTransactions"] = "391E0570BC071FA15DE7B0867296F8710D76CFA44BA3FACA441B11F0E2A7768C",
                ["RunicPersistence"] = "AE0040E7584DC2340CA94C5370B0112E2013F83100A666003B683A111CAC32D9"
            };
            const string retiredSharedIcon =
                "DB67FE011B15F35C088913BD02A2B07C5AE011C5F391E540059F551FE6FA94F9";
            var actual = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> icon in expected)
            {
                string path = PathOf(icon.Key, "icon.png");
                byte[] bytes = File.ReadAllBytes(path);
                True(bytes.Length >= 24 && bytes[0] == 0x89 && bytes[1] == 0x50 &&
                     bytes[2] == 0x4e && bytes[3] == 0x47,
                    icon.Key + " icon is not PNG");
                Equal(256, ReadBigEndianInt32(bytes, 16));
                Equal(256, ReadBigEndianInt32(bytes, 20));
                string hash = Convert.ToHexString(SHA256.HashData(bytes));
                Equal(icon.Value, hash);
                True(!string.Equals(hash, retiredSharedIcon, StringComparison.Ordinal),
                    icon.Key + " still uses the retired shared Foundation icon");
                actual.Add(hash);
            }
            Equal(expected.Count, actual.Count);
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
                    PatchRecord transactions = overlap.Single(value =>
                        value.Module == "RunicTransactions");
                    bool orderedGuards = transactions.HasSkippingPrefix &&
                                         transactions.Priority == 800 &&
                                         !transactions.HasPostfix && !transactions.HasTranspiler;
                    bool observationalPostfixes = overlap.Where(value =>
                            value.Module != "RunicTransactions")
                        .All(value => value.HasPostfix && !value.HasPrefix && !value.HasTranspiler);
                    if (!orderedGuards || !observationalPostfixes)
                    {
                        failures.Add(overlap.Key +
                                     " no longer has the Transactions guard plus observational gameplay postfixes");
                        continue;
                    }
                }
                if (target == "WearNTear::Destroy")
                {
                    PatchRecord transactions = overlap.Single(value =>
                        value.Module == "RunicTransactions" && value.HasPrefix);
                    PatchRecord portals = overlap.Single(value =>
                        value.Module == "RunicPortals" && value.HasPrefix);
                    PatchRecord production = overlap.Single(value =>
                        value.Module == "RunicProduction" && value.HasPrefix);
                    bool exactOrder = transactions.HasSkippingPrefix && transactions.Priority == 800 &&
                                      portals.HasSkippingPrefix && portals.Priority == 800 &&
                                      portals.After.Contains("chazman.RunicTransactions", StringComparer.Ordinal) &&
                                      portals.Before.Contains("chazman.RunicProduction", StringComparer.Ordinal) &&
                                      production.HasSkippingPrefix && production.Priority == 0 &&
                                      production.After.Contains("chazman.RunicTransactions", StringComparer.Ordinal) &&
                                      !transactions.HasTranspiler && !portals.HasTranspiler &&
                                      !production.HasTranspiler;
                    if (!exactOrder)
                    {
                        failures.Add(overlap.Key +
                                     " must run Transactions first, Portals second, and Production last");
                        continue;
                    }
                }
                if (target == "WearNTear::Damage" || target == "WearNTear::RPC_Damage")
                {
                    PatchRecord safety = overlap.Single(value =>
                        value.Module == "RunicSafety" && value.HasPrefix);
                    PatchRecord portals = overlap.Single(value =>
                        value.Module == "RunicPortals" && value.HasPrefix);
                    bool exactOrder = safety.HasSkippingPrefix && safety.Priority == 800 &&
                                      portals.HasSkippingPrefix && portals.Priority == 800 &&
                                      portals.After.Contains("chazman.RunicSafety", StringComparer.Ordinal) &&
                                      !safety.HasPostfix && !portals.HasPostfix &&
                                      !safety.HasTranspiler && !portals.HasTranspiler;
                    if (!exactOrder)
                    {
                        failures.Add(overlap.Key +
                                     " must run the Safety custody veto before the Portals picker veto");
                        continue;
                    }
                }
                if (target == "Container::RPC_RequestOpen" ||
                    target == "Container::RPC_RequestStack" ||
                    target == "Container::RPC_RequestTakeAll")
                {
                    PatchRecord safety = overlap.Single(value => value.Module == "RunicSafety");
                    PatchRecord transactions = overlap.Single(value => value.Module == "RunicTransactions");
                    bool exactOrder = safety.HasSkippingPrefix && safety.Priority == 800 &&
                                      safety.Before.Contains("chazman.RunicTransactions", StringComparer.Ordinal) &&
                                      transactions.HasSkippingPrefix &&
                                      !safety.HasPostfix && !safety.HasTranspiler &&
                                      !transactions.HasPostfix && !transactions.HasTranspiler;
                    if (!exactOrder)
                    {
                        failures.Add(overlap.Key +
                                     " must run the Safety custody denial before the Transactions claim guard");
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
                if (target == "Piece::SetCreator")
                {
                    bool observational = overlap.All(value => value.HasPostfix && !value.HasPrefix &&
                        !value.HasTranspiler);
                    if (!observational)
                    {
                        failures.Add(overlap.Key +
                                     " must remain two non-skipping post-write placement observers");
                        continue;
                    }
                }
                Console.WriteLine("  OVERLAP " + overlap.Key + " => " + rationale + " [" +
                                  string.Join(", ", overlap.Select(value =>
                                      value.Module + ":" + value.Owner)) + "]");
            }
            PatchRecord[] portalEnvironmentalGuard = patches.Where(value =>
                    value.Target == "WearNTear::ApplyDamage")
                .ToArray();
            if (portalEnvironmentalGuard.Length != 1 ||
                portalEnvironmentalGuard[0].Module != "RunicPortals" ||
                !portalEnvironmentalGuard[0].HasSkippingPrefix ||
                portalEnvironmentalGuard[0].Priority != 800 ||
                !portalEnvironmentalGuard[0].After.Contains(
                    "chazman.RunicSafety", StringComparer.Ordinal) ||
                portalEnvironmentalGuard[0].HasPostfix ||
                portalEnvironmentalGuard[0].HasTranspiler)
                failures.Add(
                    "WearNTear::ApplyDamage must remain one first-priority Portals-only " +
                    "environmental picker veto ordered after any Safety custody veto");
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
                    @"^##[^\r\n]*(?:authority|multiplayer|security boundary|safety and honest boundaries)",
                    RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);
                True(boundedHeading,
                    module.Assembly + " README has no explicit bounded-work/performance heading.");
                True(authorityHeading,
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
        private static void RequirePublicCoreContract(AssemblyDefinition core, string typeName)
        {
            TypeDefinition type = AllTypes(core.MainModule.Types).SingleOrDefault(candidate =>
                string.Equals(candidate.FullName, typeName, StringComparison.Ordinal));
            True(type != null && type.IsPublic, "Shared contract is missing or non-public: " + typeName);
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
            internal ModuleSpec(string display, string directory, string assembly, string guid, string iconPath)
            { Display = display; Directory = directory; Assembly = assembly; Guid = guid; IconPath = iconPath; }
            internal string Display { get; } internal string Directory { get; } internal string Assembly { get; }
            internal string Guid { get; } internal string IconPath { get; }
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
        private sealed class FoundationSpec
        {
            internal FoundationSpec(string directory, string packagePrefix)
            {
                Directory = directory;
                PackagePrefix = packagePrefix;
                CurrentVersion = string.Empty;
            }

            internal string Directory { get; }
            internal string PackagePrefix { get; }
            internal string CurrentVersion { get; set; }
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

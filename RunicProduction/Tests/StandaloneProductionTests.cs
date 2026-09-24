using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using RunicProduction.Contracts;
using RunicProduction.Core;
using RunicProduction.Integration;

namespace RunicProduction.Tests
{
    internal static class StandaloneProductionTests
    {
        internal static IReadOnlyList<KeyValuePair<string, Action>> Cases() =>
            new List<KeyValuePair<string, Action>>
            {
                Case("manifest has only the BepInEx runtime dependency", ManifestIsStandalone),
                Case("Valheim 1.0 production adapter signatures match", Valheim10AdaptersInitialize),
                Case("Valheim 1.0 cheated fermenter state round-trips", CheatedFermenterStateRoundTrips),
                Case("project has no Foundation project references", ProjectIsStandalone),
                Case("plugin has no hard Foundation dependency attributes", PluginIsStandalone),
                Case("assembly references no Foundation runtime", AssemblyIsStandalone),
                Case("retired transport and operation-state files are absent", RetiredFilesAreAbsent),
                Case("production source contains no retired runtime coupling", SourceHasNoRetiredCoupling),
                Case("background handoff is separate from setup claims and native mutation guards remain", RuntimeNeverTransfersOwnership),
                Case("handoff RPC publishes no remote inventory or item operations", RuntimeHasNoCustomRpc),
                Case("handoff checks stored authorization and has no live-player dependency", HandoffAuthorizationIsExact),
                Case("ordinary transfers have no persistent operation records", NoOperationPersistence),
                Case("legacy stable token fields remain format-compatible", TokenFieldsAreCompatible),
                Case("stable tokens are canonical lowercase nonempty GUIDs", TokensAreCanonical),
                Case("link and catalog storage keys preserve their namespace", StorageKeysAreCompatible),
                Case("runtime and package are 1.0.15", VersionsMatchPublishedPackage),
                Case("station role matrix matches supported gameplay", RoleMatrixIsExact),
                Case("mouse gestures map exactly to input output and replenish", MouseGesturesAreExact),
                Case("output gestures arm from the station without a physical output sub-target", StationLevelOutputControlsAreExact),
                Case("input output and replenishment role links are bounded multi-chest catalogs", MultiRoleLinksAreExact),
                Case("one chest can relay output into another station input", RelayChestRolesAreIndependent),
                Case("accepted mouse gestures own only their conflicting actions", GestureInputOwnershipIsExact),
                Case("linked chest rings preserve role colors and every replenish destination", LinkRingsAreExact),
                Case("fire and lamp fuel service uses exact native transactions", FireplaceFuelIsExact),
                Case("requested recipe and fermenter stock workflows remain explicit", RequestedStockWorkflowsAreExplicit),
                Case("link selection is bounded and expiring", SelectionIsBounded),
                Case("two-stage link admission requires every local proof", LinkAdmissionIsExact),
                Case("ingredient reserves are exact and deny malformed rules", IngredientReservesAreExact),
                Case("replenishment target reserves require a physical exemplar and honor in-flight work",
                    ReplenishmentTargetReservesAreExact),
                Case("nearby staging preserves per-chest reserves", NearbyStagingPreservesReserves),
                Case("stack chunk planning is bounded and exact", StackChunksAreExact),
                Case("smelter output batch planning preserves type boundaries", OutputBatchesAreExact),
                Case("station prefab policies are exact and deny-first", PrefabPoliciesAreExact),
                Case("replenishment selection is deterministic and bounded", ReplenishmentSelectionIsDeterministic),
                Case("timed replenishment and throughput links can coexist", TimedDestinationModesCanCoexist),
                Case("recipe candidate policy rejects ambiguity", RecipeCandidatesAreStrict),
                Case("catalog bounds remain sixteen destinations and thirty-two targets", CatalogBoundsArePreserved),
                Case("planned cooking and fermenting inputs fail closed", PlannedInputsFailClosed),
                Case("runtime retains in-memory rollback for paired mutations", RuntimeHasLocalRollback),
                Case("smelter output interception falls back to vanilla", OutputHookHasVanillaFallback),
                Case("Player interact prefix matches the installed void signature", PlayerInteractPatchMatchesVoid)
            }.AsReadOnly();

        private static void Valheim10AdaptersInitialize()
        {
            const BindingFlags instance = BindingFlags.Instance |
                                          BindingFlags.Public |
                                          BindingFlags.NonPublic;
            Type status = typeof(CookingStation).GetNestedType(
                "Status", BindingFlags.NonPublic);
            Require(status != null);
            Require(typeof(Smelter).GetMethod(
                "QueueOre", instance, null,
                new[] { typeof(string), typeof(bool) }, null) != null);
            Require(typeof(CookingStation).GetMethod(
                "GetSlot", instance, null,
                new[]
                {
                    typeof(int), typeof(string).MakeByRefType(),
                    typeof(float).MakeByRefType(), status.MakeByRefType(),
                    typeof(bool).MakeByRefType()
                }, null) != null);
            Require(typeof(CookingStation).GetMethod(
                "SetSlot", instance, null,
                new[] { typeof(int), typeof(string), typeof(float), status, typeof(bool) },
                null) != null);
            FieldInfo cheated = typeof(ItemDrop.ItemData).GetField(
                "m_cheated", BindingFlags.Instance | BindingFlags.Public);
            Require(cheated != null && cheated.FieldType == typeof(bool));
            Require(FermenterContentStorage.ResolveUsesHash(typeof(Fermenter)));
            MethodInfo changed = typeof(Inventory).GetMethod(
                "Changed",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(bool), typeof(bool) },
                null);
            Require(changed != null && changed.ReturnType == typeof(void));
        }

        private static void CheatedFermenterStateRoundTrips()
        {
            Require(FermenterStationState.TryCreate(
                "BarleyWineBase", new DateTime(2026, 9, 9, 0, 0, 0, DateTimeKind.Utc).Ticks,
                true, out FermenterStationState state));
            Require(state.Cheated);
            Require(FermenterStationState.TryParse(
                state.Serialize(), out FermenterStationState parsed));
            Require(parsed.Cheated);
            Require(parsed.InputPrefabId == state.InputPrefabId &&
                    parsed.StartTicks == state.StartTicks);
            Require(!FermenterStationState.TryCreate(string.Empty, 0L, true, out _));
        }

        private static void ManifestIsStandalone()
        {
            string source = Runic.Tests.LocalizedSource.ReadAllText(PathInMod("manifest.json"));
            Contains(source, "denikson-BepInExPack_Valheim-5.4.2350");
            NotContains(source, "Chazman-RunicCore");
            NotContains(source, "Chazman-RunicPersistence");
            NotContains(source, "Chazman-RunicPermissions");
            NotContains(source, "Chazman-RunicTransactions");
        }

        private static void ProjectIsStandalone()
        {
            string source = Runic.Tests.LocalizedSource.ReadAllText(PathInMod("RunicProduction.csproj"));
            Contains(source, "InventorySafety");
            NotContains(source, "ProjectReference");
            NotContains(source, "RunicCore");
            NotContains(source, "RunicPersistence");
            NotContains(source, "RunicPermissions");
            NotContains(source, "RunicTransactions");
        }

        private static void PluginIsStandalone()
        {
            string source = Runic.Tests.LocalizedSource.ReadAllText(PathInMod("Plugin.cs"));
            NotContains(source, "BepInDependency");
            NotContains(source, "RunicRegistry");
            NotContains(source, "RegisterService");
            NotContains(source, "RegisterModule");
        }

        private static void AssemblyIsStandalone()
        {
            string[] references = typeof(Plugin).Assembly.GetReferencedAssemblies()
                .Select(value => value.Name)
                .ToArray();
            Require(!references.Any(value => value != null &&
                (value.StartsWith("RunicCore", StringComparison.Ordinal) ||
                 value.StartsWith("RunicPersistence", StringComparison.Ordinal) ||
                 value.StartsWith("RunicPermissions", StringComparison.Ordinal) ||
                 value.StartsWith("RunicTransactions", StringComparison.Ordinal))));
        }

        private static void RetiredFilesAreAbsent()
        {
            string[] names =
            {
                "ProductionDirectRpc.cs",
                "ContainerOwnershipHandoff.cs",
                "ProductionMutationJournal.cs",
                "StockMutationJournal.cs",
                "CookingMutationJournal.cs",
                "ProductionTransactionGate.cs",
                "ProductionRemoteContainerZdoIndex.cs",
                "ProductionRemoteWardZdoIndex.cs",
                "MultiRouteAssignmentStores.cs",
                "StationDestructionLease.cs",
                "CookingProductionRuntime.cs",
                "RecipeReplenishmentRuntime.cs",
                "FermenterProductionRuntime.cs"
            };
            foreach (string name in names)
                Require(!Directory.EnumerateFiles(
                        ModRoot(), name, SearchOption.AllDirectories).Any(),
                    "Retired file remains: " + name);
        }

        private static void SourceHasNoRetiredCoupling()
        {
            string source = ProductionSource();
            foreach (string forbidden in new[]
                     {
                         "Runic.Foundation",
                         "RunicCoreApi",
                         "RunicRegistry",
                         "ProductionCapabilityIds",
                         "ProductionPermissionResolver",
                         "DurableOperationCoordinator",
                         "ProductionDirectRpc",
                         "ContainerOwnershipHandoff"
                     })
                NotContains(source, forbidden);
        }

        private static void RuntimeNeverTransfersOwnership()
        {
            string source = Runic.Tests.LocalizedSource.ReadAllText(PathInMod(
                "Integration", "ProductionRuntime.cs"));
            Contains(source, "IsOwner()");
            Contains(source, "TrySynchronizeLocallyOwnedContainer");
            string access = Runic.Tests.LocalizedSource.ReadAllText(PathInMod("Integration", "ValheimAccess.cs"));
            Contains(access, "GetByteArray(ZDOVars.s_items)");
            NotContains(access, "GetString(ZDOVars.s_items");
            Contains(access, "ProductionInventoryPayloadComparison.MatchesLoaded");
            Contains(access, "GetInt(ZDOVars.s_inUse, 0)");
            NotContains(source, "ClaimOwnership");
            NotContains(source, ".SetOwner(");
            NotContains(source, ".ClaimOwnership(");
            Contains(source, "TryPrepareLinkOwnership(");
            Contains(access, "ProductionSetupOwnership.TryAcquire(AccessStillValid");
            Contains(access, "ContainerAllows(chest, actorId)");
            Contains(access, "WardAllows(source, actorId)");
            Contains(access, "WardAllows(target, actorId)");
            Contains(access, "ContainerWithinReach(chest, player.transform.position");
            Contains(access, "!ContainerWritable(chest)");
            NotContains(source, "This process is not the station's current native owner.");
            NotContains(source, "The station and chest must both be owned by this local process.");
        }

        private static void RuntimeHasNoCustomRpc()
        {
            string source = ProductionSource();
            NotContains(source, "ZRoutedRpc");
            NotContains(source, "Register<ZPackage>");
            NotContains(source, "ProductionDirectRpc");
            string access = Runic.Tests.LocalizedSource.ReadAllText(PathInMod(
                "Integration", "ValheimAccess.cs"));
            Contains(access, "RPC_SetSlotVisual");
            string handoff = Runic.Tests.LocalizedSource.ReadAllText(PathInMod("Integration", "ProductionChestHandoff.cs"));
            Contains(handoff, "RunicAutomation.ContainerAuthority.TryAcquire");
            Contains(handoff, "ValheimAccess.Zdo(station)?.GetOwner() == sender");
            Contains(handoff, "TrySynchronizeLocallyOwnedContainer");
            string shared = Runic.Tests.LocalizedSource.ReadAllText(Path.Combine(ModRoot(), "..", "Shared", "InventorySafety", "ContainerAuthority.cs"));
            Contains(shared, "Register<string, ZDOID, long, string>");
            Contains(shared, "zdo.Set(Receipt, nonce)");
            Contains(shared, "zdo.SetOwner(sender)");
            NotContains(shared, "RemoveItem");
            NotContains(handoff, "ClaimOwnership");
            NotContains(handoff, "Player.m_localPlayer");
            NotContains(handoff, "ZPackage");
        }

        private static void HandoffAuthorizationIsExact()
        {
            string source = Runic.Tests.LocalizedSource.ReadAllText(PathInMod("Integration", "ProductionRuntime.cs"));
            string pending = source.Substring(source.IndexOf("private static void TryActivatePendingReplenishment",
                StringComparison.Ordinal));
            pending = pending.Substring(0, pending.IndexOf("private static bool SameTargetSet", StringComparison.Ordinal));
            NotContains(pending, "Player.m_localPlayer");
            NotContains(pending, "actor?.GetPlayerID");
            Contains(pending, ": link.OwnerId");
            Contains(pending, "principalId != link.OwnerId");
            Contains(source, "link.OwnerId == principal");
            Contains(source, "ValheimAccess.Creator(station) != link.StationOwnerId");
            Contains(source, "ValheimAccess.Creator(container) != link.TargetOwnerId");
            Contains(source, "ValheimAccess.ContainerAllows(container, link.OwnerId)");
            Contains(source, "ValheimAccess.WardAllows(stationPosition, link.OwnerId)");
            Contains(source, "ValheimAccess.WardAllows(currentPosition, link.OwnerId)");
            Contains(source, "ProtectedDestinationIds(station).Contains(chestZdo.m_uid)");
            Contains(source, "ProductionChestHandoff.TryAcquire(station, container, actorId)");
            Contains(source, "ProductionChestHandoff.TryAcquire(station, container, link.OwnerId)");
            Contains(source, "if (query.Truncated) yield break;");
            Require(typeof(ZNetView).GetMethod("InvokeRPC", new[] { typeof(long), typeof(string), typeof(object[]) }) != null);
            Require(typeof(ZDOMan).GetMethod("ForceSendZDO", new[] { typeof(long), typeof(ZDOID) }) != null);
            Require(typeof(ZDO).GetMethod("SetOwner", new[] { typeof(long) }) != null);
        }

        private static void NoOperationPersistence()
        {
            string source = ProductionSource();
            NotContains(source, "MutationJournal");
            NotContains(source, "DurableOperation");
            NotContains(source, "OperationId");
            NotContains(source, "pending-output.prefab");
            NotContains(source, "pending-output.amount");
        }

        private static void TokenFieldsAreCompatible()
        {
            Equal(
                "runic.transactions.world-object.token",
                ProductionEndpointIdentity.TokenStorageKey);
            Equal(
                "runic.transactions.world-object.token-present",
                ProductionEndpointIdentity.PresenceStorageKey);
        }

        private static void TokensAreCanonical()
        {
            string token = Guid.NewGuid().ToString("N");
            Require(ProductionEndpointIdentity.IsCanonicalToken(token));
            Require(!ProductionEndpointIdentity.IsCanonicalToken(token.ToUpperInvariant()));
            Require(!ProductionEndpointIdentity.IsCanonicalToken(Guid.Empty.ToString("N")));
            Require(!ProductionEndpointIdentity.IsCanonicalToken(
                Guid.NewGuid().ToString("D")));
            Require(!ProductionEndpointIdentity.IsCanonicalToken(null));
        }

        private static void StorageKeysAreCompatible()
        {
            string links = Runic.Tests.LocalizedSource.ReadAllText(PathInMod(
                "Integration", "ProductionLinkStore.cs"));
            string plans = Runic.Tests.LocalizedSource.ReadAllText(PathInMod(
                "Integration", "ReplenishmentPlanStore.cs"));
            string catalogs = Runic.Tests.LocalizedSource.ReadAllText(PathInMod(
                "Integration", "MultiReplenishmentCatalogStore.cs"));
            Contains(links, "Plugin.ModuleId + \".\" + localName");
            Contains(plans, "Plugin.ModuleId + \".stock.plan.record\"");
            Contains(catalogs,
                "Plugin.ModuleId + \".stock.destinations.index\"");
            Contains(catalogs,
                "Plugin.ModuleId + \".stock.destinations.slot.\"");
        }

        private static void VersionsMatchPublishedPackage()
        {
            Equal("1.0.15", Plugin.Version);
            Contains(Runic.Tests.LocalizedSource.ReadAllText(PathInMod("manifest.json")),
                "\"version_number\": \"1.0.15\"");
            Contains(Runic.Tests.LocalizedSource.ReadAllText(PathInMod(
                "Properties", "AssemblyInfo.cs")),
                "AssemblyInformationalVersion(\"1.0.15\")");
        }

        private static void RoleMatrixIsExact()
        {
            MethodInfo method = typeof(ProductionRuntime).GetMethod(
                "StationSupportsRole",
                BindingFlags.Static | BindingFlags.NonPublic);
            Require(method != null);
            bool Allows(ProductionStationKind station, ProductionLinkRole role) =>
                (bool)method.Invoke(null, new object[] { station, role });
            Require(Allows(ProductionStationKind.Beehive, ProductionLinkRole.Output));
            Require(!Allows(ProductionStationKind.Beehive, ProductionLinkRole.Input));
            Require(!Allows(ProductionStationKind.Beehive, ProductionLinkRole.Fuel));
            Require(!Allows(ProductionStationKind.Beehive, ProductionLinkRole.Replenishment));
            Require(Allows(ProductionStationKind.Smelter, ProductionLinkRole.Input));
            Require(Allows(ProductionStationKind.Smelter, ProductionLinkRole.Fuel));
            Require(Allows(ProductionStationKind.Smelter, ProductionLinkRole.Output));
            Require(!Allows(
                ProductionStationKind.Smelter,
                ProductionLinkRole.Replenishment));
            Require(Allows(
                ProductionStationKind.Cooking,
                ProductionLinkRole.Replenishment));
            Require(Allows(ProductionStationKind.Recipe, ProductionLinkRole.Output));
            Require(!Allows(ProductionStationKind.Fermenter, ProductionLinkRole.Fuel));
            Require(Allows(ProductionStationKind.Fireplace, ProductionLinkRole.Fuel));
            Require(Allows(ProductionStationKind.Fireplace, ProductionLinkRole.Input));
            Require(!Allows(ProductionStationKind.Fireplace, ProductionLinkRole.Output));
            Require(!Allows(ProductionStationKind.Fireplace, ProductionLinkRole.Replenishment));
        }

        private static void MouseGesturesAreExact()
        {
            Equal(
                ProductionLinkRole.Input,
                ProductionLinkGesturePolicy.RequestedRole(
                    ProductionLinkMouseButton.Left));
            Equal(
                ProductionLinkRole.Output,
                ProductionLinkGesturePolicy.RequestedRole(
                    ProductionLinkMouseButton.Right));
            Equal(
                ProductionLinkRole.Replenishment,
                ProductionLinkGesturePolicy.RequestedRole(
                    ProductionLinkMouseButton.Middle));
            Equal(
                RepeatedControlAction.CancelSelection,
                ProductionControlGuidance.DecideRepeatedControl(true, true));
            Contains(
                ProductionControlGuidance.Format(
                    ProductionLinkRole.Replenishment,
                    true,
                    0),
                "Alt+Middle Mouse");
            Contains(
                ProductionControlGuidance.Format(
                    ProductionLinkRole.Fuel,
                    false,
                    0),
                "Fuel Input");

            string input = Runic.Tests.LocalizedSource.ReadAllText(PathInMod(
                "Integration", "ProductionLinkInput.cs"));
            string runtime = Runic.Tests.LocalizedSource.ReadAllText(PathInMod(
                "Integration", "ProductionRuntime.cs"));
            Contains(input, "KeyCode.LeftAlt");
            Contains(input, "KeyCode.RightAlt");
            Contains(input, "KeyCode.LeftShift");
            Contains(input, "KeyCode.RightShift");
            Contains(input, "KeyCode.Mouse0");
            Contains(input, "KeyCode.Mouse1");
            Contains(input, "KeyCode.Mouse2");
            Contains(runtime, "Use \" + gesture +");
            Contains(runtime, "pending.Button == button && pending.Remove == remove");
            Contains(runtime, "Linked as \" + DisplayRole(selection.Role).ToLowerInvariant()");
            Contains(runtime, "ProductionLinkGesturePolicy.RequestedRole(button)");
            Contains(runtime, "RefineInputRoleForTarget(");
            Contains(runtime, "m_addWoodSwitch");
            Contains(runtime, "m_addFuelSwitch");
            Contains(runtime, "Fuel Input");
            Contains(runtime, "ResolveFuelEndpoints(station)");
            Contains(runtime, "ResolveRecipeInputs(station, null, ingredientLink.OwnerId)");
            NotContains(runtime, "bool shift = ShiftHeld;");
            NotContains(runtime, "bool control = ControlHeld;");
        }

        private static void StationLevelOutputControlsAreExact()
        {
            string runtime = Runic.Tests.LocalizedSource.ReadAllText(PathInMod(
                "Integration", "ProductionRuntime.cs"));
            Contains(runtime, "TryBeginLinkSelection(");
            Contains(runtime, "ProductionLinkGesturePolicy.RequestedRole(button)");
            NotContains(runtime, "pointsAtPhysicalOutput,");
            NotContains(runtime, "Point at this station's physical output location");
        }

        private static void MultiRoleLinksAreExact()
        {
            Equal(16, ProductionRoleLinkCatalog.HardMaximumLinks);
            ProductionRoleLinkCatalog input = ProductionRoleLinkCatalogPolicy.Empty(
                ProductionLinkRole.Input);
            StoredProductionLink first = RoleLink(ProductionLinkRole.Input, 1);
            Require(ProductionRoleLinkCatalogPolicy.TryAddOrRefresh(
                input, first, 8, out input, out _, out _));
            StoredProductionLink second = RoleLink(ProductionLinkRole.Input, 2);
            Require(ProductionRoleLinkCatalogPolicy.TryAddOrRefresh(
                input, second, 8, out input, out _, out _));
            Equal(2, input.Links.Count);
            Require(ProductionRoleLinkCatalogPolicy.TryRemove(
                input, first.TargetToken, first.TargetPrefabHash,
                out input, out _));
            Equal(1, input.Links.Count);

            ProductionRoleLinkCatalog bounded = ProductionRoleLinkCatalogPolicy.Empty(
                ProductionLinkRole.Output);
            for (int index = 0; index < 2; index++)
                Require(ProductionRoleLinkCatalogPolicy.TryAddOrRefresh(
                    bounded, RoleLink(ProductionLinkRole.Output, index + 10), 2,
                    out bounded, out _, out _));
            Require(!ProductionRoleLinkCatalogPolicy.TryAddOrRefresh(
                bounded, RoleLink(ProductionLinkRole.Output, 12), 2,
                out _, out _, out _));

            string runtime = Runic.Tests.LocalizedSource.ReadAllText(PathInMod(
                "Integration", "ProductionRuntime.cs"));
            Contains(runtime, "ResolveRoleEndpoints(");
            Contains(runtime, "ProductionRoleLinkCatalogStore.Publish");
            Contains(runtime, "MaximumLinksPerRole");
        }

        private static void RelayChestRolesAreIndependent()
        {
            string runtime = Runic.Tests.LocalizedSource.ReadAllText(PathInMod(
                "Integration", "ProductionRuntime.cs"));
            NotContains(runtime, "TargetConflictsWithOtherRole");
            NotContains(runtime, "already used by an incompatible role");
            Contains(runtime, "ProtectedDestinationIds(station)");
            Contains(runtime, "protectedDestinations.Remove(linked.Zdo.m_uid)");
            string readme = Runic.Tests.LocalizedSource.ReadAllText(PathInMod("README.md"));
            Contains(readme, "Output of one station and the Input of another");
        }

        private static void GestureInputOwnershipIsExact()
        {
            ProductionLinkInput.Reset();
            ProductionLinkInput.Consume(10, ProductionLinkMouseButton.Left);
            Require(ProductionLinkInput.ShouldSuppress("Attack", 10));
            Require(!ProductionLinkInput.ShouldSuppress("Block", 10));
            ProductionLinkInput.Reset();

            ProductionLinkInput.Consume(11, ProductionLinkMouseButton.Right);
            Require(ProductionLinkInput.ShouldSuppress("Block", 11));
            Require(ProductionLinkInput.ShouldSuppress("BuildMenu", 11));
            Require(!ProductionLinkInput.ShouldSuppress("Attack", 11));
            ProductionLinkInput.Reset();

            ProductionLinkInput.Consume(12, ProductionLinkMouseButton.Middle);
            Require(ProductionLinkInput.ShouldSuppress("SecondaryAttack", 12));
            Require(ProductionLinkInput.ShouldSuppress("Remove", 12));
            Require(!ProductionLinkInput.ShouldSuppress("Block", 12));
            ProductionLinkInput.Reset();

            string patches = Runic.Tests.LocalizedSource.ReadAllText(PathInMod(
                "Integration", "HarmonyPatches.cs"));
            string input = Runic.Tests.LocalizedSource.ReadAllText(PathInMod(
                "Integration", "ProductionLinkInput.cs"));
            Contains(patches, "ProductionPlayerUpdateInputPatch");
            Contains(patches, "ProductionConsumedButtonPatch");
            Contains(patches, "ProductionConsumedButtonDownPatch");
            Contains(patches, "ProductionSetControlsInputPatch");
            Equal(2, Count(patches, "[HarmonyAfter(\"chazman.RunicStorage\")]") );
            Contains(input, "_processedGestureFrame == frame");
            int setControls = patches.IndexOf(
                "Player.SetControls link input",
                StringComparison.Ordinal);
            int suppress = patches.IndexOf(
                "ProductionLinkInput.SuppressSetControls(",
                StringComparison.Ordinal);
            Require(setControls >= 0 && suppress > setControls,
                "SetControls must sample the gesture before neutralizing the first combat edge.");

            MethodInfo targetMethod = typeof(ProductionSetControlsInputPatch).GetMethod(
                "TargetMethod",
                BindingFlags.Static | BindingFlags.NonPublic);
            Require(targetMethod != null);
            Type[] setControlParameters =
            {
                typeof(UnityEngine.Vector3),
                typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool),
                typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool),
                typeof(bool)
            };
            MethodBase installed = typeof(Player).GetMethod(
                "SetControls",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                setControlParameters,
                null);
            Require(installed != null && installed.Name == "SetControls");
            Equal(12, installed.GetParameters().Length);
            MethodInfo hover = typeof(Player).GetMethod(
                nameof(Player.GetHoverObject),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);
            Require(hover != null && hover.ReturnType == typeof(UnityEngine.GameObject));
        }

        private static void LinkRingsAreExact()
        {
            UnityEngine.Color input = ProductionLinkHighlight.ColorFor(
                ProductionLinkHighlightRole.Input);
            UnityEngine.Color output = ProductionLinkHighlight.ColorFor(
                ProductionLinkHighlightRole.Output);
            UnityEngine.Color replenish = ProductionLinkHighlight.ColorFor(
                ProductionLinkHighlightRole.Replenishment);
            Require(input.g > 0.95f && input.r < 0.2f && input.b < 0.25f);
            Require(output.r > 0.95f && output.g > 0.7f && output.b < 0.1f);
            Require(replenish.g > 0.95f && replenish.b > 0.75f && replenish.r < 0.1f);

            string highlight = Runic.Tests.LocalizedSource.ReadAllText(PathInMod(
                "Integration", "ProductionLinkHighlight.cs"));
            string runtime = Runic.Tests.LocalizedSource.ReadAllText(PathInMod(
                "Integration", "ProductionRuntime.cs"));
            Contains(highlight, "LineRenderer");
            Contains(highlight,
                "ringRoot.AddComponent<LineRenderer>()");
            NotContains(highlight,
                "_visualRoot.AddComponent<LineRenderer>()");
            Contains(highlight, "if (!VisualsReady()) return false;");
            Contains(highlight, "FailClosed()");
            Contains(highlight, "Destroy(this);");
            Require(ProductionLinkHighlight.VisualGraphReady(
                true, 3, false));
            Require(!ProductionLinkHighlight.VisualGraphReady(
                false, 3, false));
            Require(!ProductionLinkHighlight.VisualGraphReady(
                true, 2, false));
            Require(!ProductionLinkHighlight.VisualGraphReady(
                true, 3, true));
            Contains(highlight, "LightType.Point");
            Contains(runtime, "SuccessfulLinkHighlightSeconds = 12f");
            Contains(runtime, "station.Component, ProductionLinkRole.Replenishment");
            Contains(runtime, "Signed plan records absent from it are inert");
            Contains(runtime, "RefreshLinkedHighlights(station, ContextHighlightSeconds)");
        }

        private static void FireplaceFuelIsExact()
        {
            string runtime = Runic.Tests.LocalizedSource.ReadAllText(PathInMod(
                "Integration", "ProductionRuntime.cs"));
            string access = Runic.Tests.LocalizedSource.ReadAllText(PathInMod(
                "Integration", "ValheimAccess.cs"));
            string patches = Runic.Tests.LocalizedSource.ReadAllText(PathInMod(
                "Integration", "HarmonyPatches.cs"));
            Contains(runtime, "RunFireplace(Fireplace station)");
            Contains(runtime, "station.m_fuelItem.gameObject");
            Contains(runtime, "ApplySourceToStation(");
            Contains(runtime, "ValheimAccess.SetFireplaceFuel");
            Contains(access, "RequiredMethod(typeof(Fireplace), \"SetFuel\"");
            Contains(patches, "ProductionFireplaceAwakePatch");
            Contains(patches, "ProductionFireplaceHoverPatch");
        }

        private static void RequestedStockWorkflowsAreExplicit()
        {
            string runtime = Runic.Tests.LocalizedSource.ReadAllText(PathInMod(
                "Integration", "ProductionRuntime.cs"));
            string configuration = Runic.Tests.LocalizedSource.ReadAllText(PathInMod("Configuration.cs"));
            string example = Runic.Tests.LocalizedSource.ReadAllText(PathInMod("RunicProduction.cfg.example"));
            string mutation = Runic.Tests.LocalizedSource.ReadAllText(PathInMod(
                "Integration", "ExactStockInventoryMutation.cs"));
            Contains(runtime, "TryPrepareCompositeSources(");
            Contains(runtime, "ApplyCompositeSourcesToDestination(");
            Contains(runtime, "ProtectedDestinationIds(station)");
            Contains(runtime, "CountInFlightOutput(");
            Contains(runtime, "inFlight > 0");
            Contains(runtime, "largestDeficit");
            Contains(runtime, "belowReserve ?? completionFallback");
            Contains(runtime, "already physically complete at the station");
            Contains(runtime, "composite inventory readback failed");
            Contains(runtime, "ResolveEndpointMutation(");
            Contains(runtime, "ProductionEndpointMutationState.Indeterminate");
            Contains(runtime, "Classification is a no-throw boundary");
            Contains(runtime,
                "catch { return ProductionEndpointMutationState.Indeterminate; }");
            Contains(runtime, "PauseAfterIndeterminate(");
            Contains(runtime, "SafetyPausedStations");
            Contains(runtime, "applied.Add(entry);");
            Contains(runtime, "stationRestored");
            Contains(runtime, "suppressed vanilla fallback after an indeterminate");
            Contains(runtime,
                "if (SafetyPausedStations.Contains(station.GetInstanceID())) return false;");
            Contains(runtime, "Later batches");
            Contains(mutation, "StockSnapshotPublicationException");
            Contains(mutation, "changed?.Invoke();");
            Contains(mutation, "rollbackProven");
            Contains(runtime, "IsActiveReplenishmentLink(");
            Contains(runtime, "(plan.Cursor + offset) % plan.Targets.Count");
            Contains(runtime, "current.Plan.AuthorizedPlayerId == link.OwnerId");
            Contains(runtime, "StoredProductionLink link = candidate;");
            Contains(runtime, "current.Link.Revision == planLink.Revision");
            Contains(runtime, "ProducerSignatureMatches");
            Contains(runtime, "ProductionRoleLinkCatalog.HardMaximumLinks;");
            Contains(runtime, "Plan activation must preserve an");
            NotContains(runtime, "ValheimAccess.Creator(container) != stationCreator");
            Contains(runtime, "TryResolveOutputDestination(");
            Contains(runtime, "MultiReplenishmentCatalogStore");
            Contains(runtime, "ReplenishmentProducerKind.DirectRecipe");
            Contains(configuration, "RecipeNearbyIngredientsEnabled = config.Bind(");
            Contains(configuration.Replace("\r\n", "\n"),
                "\n                true,\n                \"Use bounded");
            Contains(example, "Default value: true");
            Contains(example, "Enabled = true");
            NotContains(configuration, "MaximumReplenishmentDestinations");
            NotContains(example, "MaximumDestinationsPerStation");
        }

        private static void SelectionIsBounded()
        {
            Type runtime = typeof(ProductionRuntime);
            Equal(16, Constant<int>(runtime, "MaximumSelections"));
            Equal(30f, Constant<float>(runtime, "SelectionLifetimeSeconds"));
            Type selection = typeof(ProductionLinkSelection);
            Require(selection.GetField(
                "ExpiresAt", BindingFlags.Instance | BindingFlags.NonPublic) != null);
        }

        private static void LinkAdmissionIsExact()
        {
            Require(ExplicitLinkCommitPolicy.Allows(true, true, true));
            Require(!ExplicitLinkCommitPolicy.Allows(false, true, true));
            Require(!ExplicitLinkCommitPolicy.Allows(true, false, true));
            Require(!ExplicitLinkCommitPolicy.Allows(true, true, false));
        }

        private static void IngredientReservesAreExact()
        {
            Require(IngredientReservePolicy.TryParse(
                2, "Wood=10;Coal=3", out IngredientReservePolicy policy, out _));
            Equal(10, Reserve(policy, "Wood"));
            Equal(3, Reserve(policy, "Coal"));
            Equal(2, Reserve(policy, "Stone"));
            Equal(2L, policy.AvailableAboveReserve("Wood", 12));
            Require(!policy.AllowsConsumption("Wood", 12, 3));
            Require(!IngredientReservePolicy.TryParse(
                0, "Wood=1;Wood=2", out _, out _));
            Require(!IngredientReservePolicy.TryParse(
                0, "Wood=-1", out _, out _));
        }

        private static void ReplenishmentTargetReservesAreExact()
        {
            Require(ReplenishmentTargetReservePolicy.TryParse(
                10,
                "QueensJam=20;MeadHealthMinor=6",
                out ReplenishmentTargetReservePolicy policy,
                out _));
            Equal(2, policy.RuleCount);
            Require(policy.TryGetTargetReserve("QueensJam", out int jamReserve));
            Equal(20, jamReserve);
            Require(policy.TryGetTargetReserve("CarrotSoup", out int defaultReserve));
            Equal(10, defaultReserve);

            Require(policy.TryEvaluate(
                "QueensJam", 0, 0, out ReplenishmentTargetDemand absent));
            Equal(ReplenishmentDemandState.ExemplarAbsent, absent.State);
            Require(!absent.NeedsProduction);

            Require(policy.TryEvaluate(
                "QueensJam", 19, 0, out ReplenishmentTargetDemand needed));
            Equal(ReplenishmentDemandState.ProductionNeeded, needed.State);
            Require(needed.NeedsProduction);
            Equal(20, needed.Reserve);

            Require(policy.TryEvaluate(
                "QueensJam", 18, 2, out ReplenishmentTargetDemand committed));
            Equal(ReplenishmentDemandState.ReserveSatisfied, committed.State);
            Require(!committed.NeedsProduction);
            Equal(20L, committed.ProjectedCount);

            Require(!ReplenishmentTargetReservePolicy.TryParse(
                0, string.Empty, out _, out _));
            Require(!ReplenishmentTargetReservePolicy.TryParse(
                10, "QueensJam=0", out _, out _));
            Require(!ReplenishmentTargetReservePolicy.TryParse(
                10, "QueensJam=2;QueensJam=3", out _, out _));
            Require(!policy.TryEvaluate("QueensJam", -1, 0, out _));
        }

        private static void NearbyStagingPreservesReserves()
        {
            Require(IngredientReservePolicy.TryParse(
                2, string.Empty, out IngredientReservePolicy policy, out _));
            var requirements = new[] { new ReplenishmentRequirement("Wood", 3) };
            var input = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["Wood"] = 2
            };
            var sources = new[]
            {
                new NearbyIngredientSourceStock(
                    "source-a",
                    new Dictionary<string, int>(StringComparer.Ordinal)
                    {
                        ["Wood"] = 10
                    })
            };
            Require(NearbyIngredientStagingPlanner.TryPlan(
                requirements, input, sources, policy, 2,
                out NearbyIngredientStagingPlan plan, out _));
            Equal(3, plan.Contributions[0].Amount);
            Equal(2, plan.NextTransferAmount);
        }

        private static void StackChunksAreExact()
        {
            Require(new[] { 50, 50, 21 }
                .SequenceEqual(StackChunkPlanner.Plan(121, 50)));
            Throws<ArgumentOutOfRangeException>(() =>
                StackChunkPlanner.Plan(StackChunkPlanner.MaximumChunks + 1, 1));
        }

        private static void OutputBatchesAreExact()
        {
            IReadOnlyList<OutputBatchRequirement> same =
                OutputBatchPlanner.Plan("CopperOre", 3, "CopperOre");
            Equal(1, same.Count);
            Equal(4, same[0].Amount);
            IReadOnlyList<OutputBatchRequirement> changed =
                OutputBatchPlanner.Plan("CopperOre", 3, "TinOre");
            Equal(2, changed.Count);
            Equal("CopperOre", changed[0].InputPrefab);
            Equal("TinOre", changed[1].InputPrefab);
        }

        private static void PrefabPoliciesAreExact()
        {
            Require(CookingStationPrefabPolicy.TryParse(
                "piece_oven,piece_cookingstation",
                "piece_oven",
                out CookingStationPrefabPolicy cooking,
                out _));
            Require(!cooking.Allows("piece_oven"));
            Require(cooking.Allows("piece_cookingstation"));
            Require(StockStationPrefabPolicy.TryParse(
                "piece_cauldron,piece_preptable",
                "piece_preptable",
                out StockStationPrefabPolicy recipe,
                out _));
            Require(recipe.Allows("piece_cauldron"));
            Require(!recipe.Allows("piece_preptable"));
            Require(FermenterPrefabPolicy.TryParse(
                "fermenter", string.Empty,
                out FermenterPrefabPolicy fermenter,
                out _));
            Require(fermenter.Allows("fermenter"));
        }

        private static void ReplenishmentSelectionIsDeterministic()
        {
            ReplenishmentPlan plan = SelectorPlan(cursor: 1);
            ReplenishmentSelectionResult result =
                DeterministicReplenishmentSelector.Select(
                    plan,
                    target => string.Equals(
                        target.OutputPrefabId,
                        "FoodA",
                        StringComparison.Ordinal)
                        ? ReplenishmentTargetReadiness.Ready
                        : new ReplenishmentTargetReadiness(true, true, false, true));
            Require(result.HasAction);
            Equal("FoodA", result.Target.OutputPrefabId);
            Equal(0, result.SelectedIndex);
            Equal(1, result.NextCursor);
            Equal(2, result.InspectedTargets);
        }

        private static void TimedDestinationModesCanCoexist()
        {
            Equal(TimedCookingDestinationMode.None,
                TimedCookingDestinationPolicy.Resolve(false, false));
            Equal(TimedCookingDestinationMode.Throughput,
                TimedCookingDestinationPolicy.Resolve(true, false));
            Equal(TimedCookingDestinationMode.Replenishment,
                TimedCookingDestinationPolicy.Resolve(false, true));
            Equal(TimedCookingDestinationMode.Replenishment,
                TimedCookingDestinationPolicy.Resolve(true, true));
        }

        private static void RecipeCandidatesAreStrict()
        {
            Equal(RecipeCandidateDisposition.Missing,
                RecipeCandidatePolicy.Classify(0, false));
            Equal(RecipeCandidateDisposition.Ambiguous,
                RecipeCandidatePolicy.Classify(2, false));
            Equal(RecipeCandidateDisposition.RequireOnlyOneUnsupported,
                RecipeCandidatePolicy.Classify(1, true));
            Equal(RecipeCandidateDisposition.Eligible,
                RecipeCandidatePolicy.Classify(1, false));
        }

        private static void CatalogBoundsArePreserved()
        {
            Equal(16, MultiReplenishmentCatalog.HardMaximumDestinations);
            Equal(32, MultiReplenishmentCatalog.MaximumTargetAuthorizations);
            Equal(32, ReplenishmentPlan.MaximumTargets);
            Equal(64, NearbyIngredientContainerIndex.HardMaximumSourceChests);
        }

        private static void PlannedInputsFailClosed()
        {
            string source = Runic.Tests.LocalizedSource.ReadAllText(PathInMod(
                "Integration", "ProductionRuntime.cs"));
            Equal(2, Count(source, "else if (HasReplenishmentRecord("));
            Contains(source, "state == StoredRecordState.Invalid");
        }

        private static void RuntimeHasLocalRollback()
        {
            string source = Runic.Tests.LocalizedSource.ReadAllText(PathInMod(
                "Integration", "ProductionRuntime.cs"));
            Contains(source, "ApplyBefore(source.Inventory)");
            Contains(source, "ApplyBefore(destination.Inventory)");
            Contains(source, "EndpointStillOwned");
            NotContains(source, "operation journal");
        }

        private static void OutputHookHasVanillaFallback()
        {
            string source = Runic.Tests.LocalizedSource.ReadAllText(PathInMod(
                "Integration", "HarmonyPatches.cs"));
            Contains(source, "return !ProductionRuntime.TryDepositProducedOutput");
            Contains(source, "return true;");
        }

        private static void PlayerInteractPatchMatchesVoid()
        {
            MethodInfo installed = typeof(Player).GetMethod(
                "Interact",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(UnityEngine.GameObject), typeof(bool), typeof(bool) },
                null);
            Require(installed != null);
            Equal(typeof(void), installed.ReturnType);
            MethodInfo prefix = typeof(ProductionPlayerInteractPatch).GetMethod(
                "Prefix", BindingFlags.Static | BindingFlags.NonPublic);
            Require(prefix != null);
            Equal(typeof(bool), prefix.ReturnType);
            Equal(4, prefix.GetParameters().Length);
            Require(prefix.GetParameters().All(parameter => !parameter.ParameterType.IsByRef));
        }

        private static StoredProductionLink RoleLink(
            ProductionLinkRole role,
            int id) =>
            new StoredProductionLink
            {
                LinkId = "role-link-" + id,
                Role = role,
                TargetToken = Guid.NewGuid().ToString("N"),
                TargetPrefabHash = 1000 + id,
                Target = new ZDOID(700L, (uint)(id + 1)),
                ExpectedPosition = new UnityEngine.Vector3(id, 0f, id),
                OwnerId = 1L,
                StationOwnerId = 1L,
                TargetOwnerId = 1L,
                Revision = 1
            };

        private static ReplenishmentPlan SelectorPlan(int cursor)
        {
            var link = new ReplenishmentLinkBinding(
                "link.selector", "station.selector", "target.selector", 1);
            ReplenishmentTargetAuthorization Target(string output, byte marker) =>
                new ReplenishmentTargetAuthorization(
                    output,
                    ReplenishmentProducerKind.TimedCooking,
                    "producer." + output,
                    Enumerable.Repeat(marker, 32).ToArray(),
                    1,
                    string.Empty,
                    0,
                    1L,
                    "Player",
                    new[] { new ReplenishmentRequirement("Raw" + output, 1) });
            return new ReplenishmentPlan(
                "plan.selector",
                1,
                cursor,
                "piece_oven",
                ReplenishmentProducerKind.TimedCooking,
                link,
                1L,
                "Player",
                new[] { Target("FoodA", 1), Target("FoodB", 2) });
        }

        private static int Reserve(IngredientReservePolicy policy, string prefab)
        {
            Require(policy.TryGetProtectedReserve(prefab, out int value));
            return value;
        }

        private static T Constant<T>(Type type, string name)
        {
            FieldInfo field = type.GetField(
                name, BindingFlags.Static | BindingFlags.NonPublic);
            Require(field != null);
            return (T)field.GetRawConstantValue();
        }

        private static string ProductionSource() => string.Join(
            "\n",
            Directory.EnumerateFiles(ModRoot(), "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains(
                    Path.DirectorySeparatorChar + "Tests" +
                    Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(File.ReadAllText));

        private static string PathInMod(params string[] parts)
        {
            string path = ModRoot();
            foreach (string part in parts) path = Path.Combine(path, part);
            return path;
        }

        private static string ModRoot()
        {
            DirectoryInfo current = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (current != null)
            {
                string direct = Path.Combine(current.FullName, "RunicProduction.csproj");
                if (File.Exists(direct)) return current.FullName;
                string child = Path.Combine(
                    current.FullName, "RunicProduction", "RunicProduction.csproj");
                if (File.Exists(child)) return Path.GetDirectoryName(child);
                current = current.Parent;
            }
            throw new DirectoryNotFoundException("RunicProduction source root was not found.");
        }

        private static KeyValuePair<string, Action> Case(
            string name,
            Action action) =>
            new KeyValuePair<string, Action>(name, action);

        private static void Contains(string value, string expected) =>
            Require(value != null && value.Contains(expected, StringComparison.Ordinal),
                "Expected text was absent: " + expected);

        private static void NotContains(string value, string forbidden) =>
            Require(value == null || !value.Contains(forbidden, StringComparison.Ordinal),
                "Forbidden text remains: " + forbidden);

        private static int Count(string value, string expected)
        {
            int count = 0;
            int offset = 0;
            while ((offset = value.IndexOf(
                       expected, offset, StringComparison.Ordinal)) >= 0)
            {
                count++;
                offset += expected.Length;
            }
            return count;
        }

        private static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException(
                    "Expected " + expected + " but found " + actual + ".");
        }

        private static void Equal<T>(IEnumerable<T> expected, IEnumerable<T> actual)
        {
            if (!expected.SequenceEqual(actual))
                throw new InvalidOperationException("Sequences differ.");
        }

        private static void Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException(
                "Expected " + typeof(T).Name + ".");
        }

        private static void Require(bool condition, string message = "assertion failed")
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}

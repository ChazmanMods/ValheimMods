using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace RunicInventory.ReleaseHarness
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(InventoryGuid, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(SafetyGuid, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(InteractionGuid, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(StorageGuid, BepInDependency.DependencyFlags.HardDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "chazman.RunicInventory.ReleaseHarness";
        public const string Name = "Runic Inventory Release Harness";
        public const string Version = "1.0.0";

        private const string InventoryGuid = "chazman.RunicInventory";
        private const string SafetyGuid = "chazman.RunicSafety";
        private const string InteractionGuid = "chazman.RunicInteraction";
        private const string StorageGuid = "chazman.RunicStorage";
        private const string ResultEnvironmentVariable = "RUNIC_INVENTORY_RELEASE_HARNESS_RESULT";
        private const int ExpectedScenarioCount = 40;

        private readonly List<string> _scenarios = new List<string>();
        private bool _completed;

        private void Update()
        {
            if (_completed || ZNet.instance == null || ObjectDB.instance == null) return;
            _completed = true;
            RunAcceptance();
        }

        private void RunAcceptance()
        {
            string resultPath = Environment.GetEnvironmentVariable(ResultEnvironmentVariable);
            try
            {
                Require(!string.IsNullOrWhiteSpace(resultPath), "The harness result path is missing.");
                resultPath = Path.GetFullPath(resultPath);

                PluginInfo inventoryInfo = RequirePlugin(InventoryGuid, "1.0.2");
                PluginInfo safetyInfo = RequirePlugin(SafetyGuid, "1.0.0");
                PluginInfo interactionInfo = RequirePlugin(InteractionGuid, "1.0.0");
                PluginInfo storageInfo = RequirePlugin(StorageGuid, "1.0.1");
                Pass("exact-plugin-identities");

                Assembly inventoryAssembly = inventoryInfo.Instance.GetType().Assembly;
                Assembly safetyAssembly = safetyInfo.Instance.GetType().Assembly;
                Assembly interactionAssembly = interactionInfo.Instance.GetType().Assembly;
                Assembly storageAssembly = storageInfo.Instance.GetType().Assembly;
                Type apiType = RequiredType(inventoryAssembly, "RunicInventory.Api.InventoryIntegrationApi");
                MethodInfo protectionMethod = RequiredMethod(
                    apiType,
                    "TryGetProtection",
                    BindingFlags.Public | BindingFlags.Static,
                    typeof(bool),
                    typeof(object),
                    typeof(int).MakeByRefType());
                Pass("exact-public-protection-signature");

                object runtime = GetInventoryRuntime(inventoryInfo.Instance);
                Type runtimeType = runtime.GetType();
                FieldInfo modeField = RequiredField(runtimeType, "_mode");
                FieldInfo reasonField = RequiredField(runtimeType, "_reasonCode");
                FieldInfo loadField = RequiredField(runtimeType, "_playerLoadInProgress");
                FieldInfo disposedField = RequiredField(runtimeType, "_disposed");
                object initialMode = modeField.GetValue(runtime);
                object initialReason = reasonField.GetValue(runtime);
                object initialLoad = loadField.GetValue(runtime);
                object initialDisposed = disposedField.GetValue(runtime);

                ItemDrop.ItemData ordinaryFood = CreateFoodItem("RunicInventoryHarnessFood", 0, 0);
                try
                {
                    AssertProtection(protectionMethod, ordinaryFood, false, 0, "dedicated-unavailable-not-applicable");
                    AssertInactiveMode(runtime, modeField, reasonField, loadField, disposedField, protectionMethod,
                        ordinaryFood, "RemoteDedicatedCompatibility", "authority.local-player-owner-required",
                        false, false, false, 0, "remote-client-nonowner-not-applicable");
                    AssertInactiveMode(runtime, modeField, reasonField, loadField, disposedField, protectionMethod,
                        ordinaryFood, "BatchInert", "authority.batch-inert",
                        false, false, false, 0, "batch-inert-not-applicable");
                    AssertInactiveMode(runtime, modeField, reasonField, loadField, disposedField, protectionMethod,
                        ordinaryFood, "Disabled", "feature.disabled",
                        false, false, false, 0, "explicit-disable-not-applicable");

                    string[] stableFallbacks =
                    {
                        "topology.clear-incompatible-special-row",
                        "topology.equipped-item-outside-role",
                        "topology.width-not-eight",
                        "topology.height-too-small",
                        "topology.slot-bound-exceeded",
                        "topology.role-proof-failed"
                    };
                    for (int index = 0; index < stableFallbacks.Length; index++)
                    {
                        AssertInactiveMode(runtime, modeField, reasonField, loadField, disposedField,
                            protectionMethod, ordinaryFood, "MigrationSafeCompatibility", stableFallbacks[index],
                            false, false, false, 0, "stable-fallback-" + (index + 1).ToString(CultureInfo.InvariantCulture));
                    }

                    AssertInactiveMode(runtime, modeField, reasonField, loadField, disposedField, protectionMethod,
                        ordinaryFood, "MigrationSafeCompatibility", "runtime.exception",
                        false, false, true, 0, "runtime-fault-remains-unknown");
                    AssertInactiveMode(runtime, modeField, reasonField, loadField, disposedField, protectionMethod,
                        ordinaryFood, "MigrationSafeCompatibility", "topology.clear-incompatible-special-row",
                        true, false, true, 0, "player-load-overrides-stable-fallback");
                    AssertInactiveMode(runtime, modeField, reasonField, loadField, disposedField, protectionMethod,
                        ordinaryFood, "MigrationSafeCompatibility", "topology.clear-incompatible-special-row",
                        false, true, true, 0, "disposed-provider-remains-unknown");
                    AssertInactiveMode(runtime, modeField, reasonField, loadField, disposedField, protectionMethod,
                        ordinaryFood, "AuthoritativeLocal", "ok",
                        false, false, true, 0, "authoritative-without-evidence-remains-unknown");
                    AssertProtection(protectionMethod, new object(), false, 0, "foreign-native-type-not-applicable");

                    AssertSafetyAndInteractionFallbacks(
                        runtime,
                        modeField,
                        reasonField,
                        loadField,
                        disposedField,
                        protectionMethod,
                        ordinaryFood,
                        safetyAssembly,
                        interactionAssembly,
                        storageAssembly);

                    AssertAuthoritativeItemPaths(
                        runtime,
                        runtimeType,
                        protectionMethod,
                        safetyAssembly,
                        interactionAssembly,
                        storageAssembly);
                }
                finally
                {
                    modeField.SetValue(runtime, initialMode);
                    reasonField.SetValue(runtime, initialReason);
                    loadField.SetValue(runtime, initialLoad);
                    disposedField.SetValue(runtime, initialDisposed);
                }

                Require(_scenarios.Count == ExpectedScenarioCount,
                    "Expected " + ExpectedScenarioCount + " scenarios, observed " + _scenarios.Count + ".");
                string[] lines = new string[_scenarios.Count + 3];
                lines[0] = "status=PASS";
                lines[1] = "scenario_count=" + _scenarios.Count.ToString(CultureInfo.InvariantCulture);
                for (int index = 0; index < _scenarios.Count; index++)
                    lines[index + 2] = "scenario=" + _scenarios[index];
                lines[lines.Length - 1] = "marker=RUNIC_INVENTORY_RELEASE_HARNESS_PASS";
                File.WriteAllLines(resultPath, lines);
                Logger.LogInfo("RUNIC_INVENTORY_RELEASE_HARNESS_PASS scenarios=" + _scenarios.Count);
            }
            catch (Exception exception)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(resultPath))
                    {
                        resultPath = Path.GetFullPath(resultPath);
                        File.WriteAllLines(resultPath, new[]
                        {
                            "status=FAIL",
                            "scenario_count=" + _scenarios.Count.ToString(CultureInfo.InvariantCulture),
                            "error=" + Bound(exception.GetType().Name + ": " + exception.Message, 512)
                        });
                    }
                }
                catch { }
                Logger.LogError("RUNIC_INVENTORY_RELEASE_HARNESS_FAIL " + exception);
            }
        }

        private void AssertSafetyAndInteractionFallbacks(
            object runtime,
            FieldInfo modeField,
            FieldInfo reasonField,
            FieldInfo loadField,
            FieldInfo disposedField,
            MethodInfo protectionMethod,
            ItemDrop.ItemData item,
            Assembly safetyAssembly,
            Assembly interactionAssembly,
            Assembly storageAssembly)
        {
            SetRuntimeState(runtime, modeField, reasonField, loadField, disposedField,
                "MigrationSafeCompatibility", "topology.clear-incompatible-special-row", false, false);
            Require(ResolveSafety(safetyAssembly, item, out bool stablePresent) == 0 && stablePresent,
                "Safety did not observe stable fallback as NotApplicable.");
            Require(AuthorizeCooking(safetyAssembly, item),
                "Safety blocked ordinary cooking in stable existing-character fallback.");
            Pass("safety-cooking-allows-stable-existing-character-fallback");
            Require(ResolveInteraction(interactionAssembly, item) == 0,
                "Interaction did not observe stable fallback as NotApplicable.");
            Require(InteractionAllows(interactionAssembly, 0),
                "Interaction blocked its transfer policy in stable fallback.");
            Pass("interaction-transfer-policy-allows-stable-existing-character-fallback");
            AssertStorageCapture(
                storageAssembly,
                item,
                true,
                "ok",
                0,
                "storage-capture-allows-stable-existing-character-fallback");

            SetRuntimeState(runtime, modeField, reasonField, loadField, disposedField,
                "MigrationSafeCompatibility", "runtime.exception", false, false);
            Require(ResolveSafety(safetyAssembly, item, out bool faultPresent) == 3 && faultPresent,
                "Safety did not preserve a genuine provider fault as Unknown.");
            Require(!AuthorizeCooking(safetyAssembly, item),
                "Safety failed open during a genuine provider fault.");
            Pass("safety-cooking-denies-genuine-provider-fault");
            Require(ResolveInteraction(interactionAssembly, item) == 3,
                "Interaction did not preserve a genuine provider fault as Unknown.");
            Require(!InteractionAllows(interactionAssembly, 3),
                "Interaction failed open during a genuine provider fault.");
            Pass("interaction-transfer-policy-denies-genuine-provider-fault");
            AssertStorageCapture(
                storageAssembly,
                item,
                false,
                "protection.in-domain-unknown",
                -1,
                "storage-capture-denies-genuine-provider-fault");

            AssertProtection(protectionMethod, item, true, 0, "public-api-fault-is-governed-unknown");
        }

        private void AssertAuthoritativeItemPaths(
            object runtime,
            Type runtimeType,
            MethodInfo protectionMethod,
            Assembly safetyAssembly,
            Assembly interactionAssembly,
            Assembly storageAssembly)
        {
            MethodInfo authorityMethod = RequiredMethod(
                runtimeType,
                "IsAuthoritativeLocal",
                BindingFlags.NonPublic | BindingFlags.Static,
                typeof(bool),
                typeof(Player));
            MethodInfo rebind = RequiredMethod(
                runtimeType,
                "Rebind",
                BindingFlags.NonPublic | BindingFlags.Instance,
                typeof(void),
                typeof(Player),
                typeof(string));
            var harmony = new Harmony(Guid + ".authority");
            GameObject playerObject = null;
            Player originalLocalPlayer = Player.m_localPlayer;
            try
            {
                harmony.Patch(authorityMethod, prefix: new HarmonyMethod(
                    typeof(Plugin).GetMethod(nameof(ForceAuthoritative),
                        BindingFlags.NonPublic | BindingFlags.Static)));

                playerObject = new GameObject("RunicInventoryReleaseHarnessPlayer");
                playerObject.SetActive(false);
                Player player = playerObject.AddComponent<Player>();
                Inventory inventory = new Inventory("RunicInventoryReleaseHarness", null, 8, 4);
                RequiredField(typeof(Humanoid), "m_inventory").SetValue(player, inventory);
                RequiredField(typeof(Player), "m_customData").SetValue(
                    player, new Dictionary<string, string>(StringComparer.Ordinal));
                Player.m_localPlayer = player;

                ItemDrop.ItemData ordinary = CloneKnownCookingIngredient(0, 0);
                ItemDrop.ItemData incompatibleQuick = CloneKnownMaterial(5, 3);
                ItemDrop.ItemData quick = CloneKnownFood(5, 3);
                inventory.GetAllItems().Add(ordinary);
                inventory.GetAllItems().Add(incompatibleQuick);
                rebind.Invoke(runtime, new object[] { player, "release-harness-existing-character" });
                Require(string.Equals(
                        RequiredField(runtimeType, "_mode").GetValue(runtime).ToString(),
                        "MigrationSafeCompatibility",
                        StringComparison.Ordinal) &&
                    string.Equals(
                        RequiredField(runtimeType, "_reasonCode").GetValue(runtime) as string,
                        "topology.clear-incompatible-special-row",
                        StringComparison.Ordinal),
                    "The existing-character fixture did not naturally enter stable fallback.");
                AssertProtection(protectionMethod, ordinary, false, 0,
                    "existing-character-carried-item-not-applicable");
                Require(AuthorizeCooking(safetyAssembly, ordinary),
                    "Safety blocked a real carried item in natural existing-character fallback.");
                Pass("safety-cooking-allows-carried-item-in-natural-fallback");
                Require(ResolveInteraction(interactionAssembly, ordinary) == 0 &&
                        InteractionAllows(interactionAssembly, 0),
                    "Interaction blocked a real carried item in natural existing-character fallback.");
                Pass("interaction-transfer-policy-allows-carried-item-in-natural-fallback");
                AssertStorageCapture(
                    storageAssembly,
                    ordinary,
                    true,
                    "ok",
                    0,
                    "storage-capture-allows-carried-item-in-natural-fallback");

                inventory.GetAllItems().Remove(incompatibleQuick);
                inventory.GetAllItems().Add(quick);
                rebind.Invoke(runtime, new object[] { player, "release-harness-authoritative" });
                Logger.LogInfo(
                    "RUNIC_INVENTORY_RELEASE_AUTHORITATIVE_STATE mode=" +
                    RequiredField(runtimeType, "_mode").GetValue(runtime) + " reason=" +
                    RequiredField(runtimeType, "_reasonCode").GetValue(runtime) + " active=" +
                    RequiredField(runtimeType, "_topologyActive").GetValue(runtime));

                AssertProtection(protectionMethod, ordinary, true, 1, "authoritative-ordinary-item-unlocked");
                Require(AuthorizeCooking(safetyAssembly, ordinary),
                    "Safety blocked an authoritative unlocked cooking item.");
                Pass("safety-cooking-allows-authoritative-unlocked-item");
                Require(ResolveInteraction(interactionAssembly, ordinary) == 1 &&
                        InteractionAllows(interactionAssembly, 1),
                    "Interaction blocked an authoritative unlocked transfer policy decision.");
                Pass("interaction-transfer-policy-allows-authoritative-unlocked-item");
                AssertStorageCapture(
                    storageAssembly,
                    ordinary,
                    true,
                    "ok",
                    1,
                    "storage-capture-allows-authoritative-unlocked-item");

                SetGeneralSlotLock(runtime, runtimeType, 8, 4, 0, 0);
                AssertProtection(protectionMethod, ordinary, true, 2, "authoritative-general-lock-reported-locked");
                Require(!AuthorizeCooking(safetyAssembly, ordinary),
                    "Safety allowed a locked authoritative cooking item.");
                Pass("safety-cooking-denies-authoritative-locked-item");
                Require(ResolveInteraction(interactionAssembly, ordinary) == 2 &&
                        !InteractionAllows(interactionAssembly, 2),
                    "Interaction allowed a locked authoritative transfer policy decision.");
                Pass("interaction-transfer-policy-denies-authoritative-locked-item");
                AssertStorageCapture(
                    storageAssembly,
                    ordinary,
                    true,
                    "ok",
                    2,
                    "storage-capture-preserves-authoritative-locked-item");

                SetGeneralSlotLock(runtime, runtimeType, 8, 4, -1, -1);
                AssertProtection(protectionMethod, quick, true, 2, "authoritative-special-row-reported-locked");
                Require(!AuthorizeCooking(safetyAssembly, quick),
                    "Safety allowed an authoritative special-row item.");
                Pass("safety-cooking-denies-authoritative-special-row-item");
                Require(ResolveInteraction(interactionAssembly, quick) == 2 &&
                        !InteractionAllows(interactionAssembly, 2),
                    "Interaction allowed an authoritative special-row transfer policy decision.");
                Pass("interaction-transfer-policy-denies-authoritative-special-row-item");
                AssertStorageCapture(
                    storageAssembly,
                    quick,
                    true,
                    "ok",
                    2,
                    "storage-capture-preserves-authoritative-special-row-item");
            }
            finally
            {
                try { rebind.Invoke(runtime, new object[] { null, "release-harness-restore" }); }
                catch { }
                Player.m_localPlayer = originalLocalPlayer;
                harmony.UnpatchSelf();
                if (playerObject != null) Destroy(playerObject);
            }
        }

        private static bool ForceAuthoritative(ref bool __result)
        {
            __result = true;
            return false;
        }

        private void SetGeneralSlotLock(
            object runtime,
            Type runtimeType,
            int width,
            int height,
            int lockX,
            int lockY)
        {
            Type stateType = RequiredType(runtimeType.Assembly, "RunicInventory.Core.PersistedTopologyState");
            int slots = checked(width * height);
            byte[] bits = new byte[(slots + 7) / 8];
            if (lockX >= 0 && lockY >= 0)
            {
                int index = checked(lockY * width + lockX);
                bits[index >> 3] |= (byte)(1 << (index & 7));
            }
            ConstructorInfo constructor = stateType.GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new[] { typeof(int), typeof(int), typeof(byte[]) },
                null);
            Require(constructor != null, "Persisted topology constructor is missing.");
            object state = constructor.Invoke(new object[] { width, height, bits });
            RequiredField(runtimeType, "_persisted").SetValue(runtime, state);
        }

        private static ItemDrop.ItemData CloneKnownFood(int x, int y)
        {
            foreach (string prefabName in new[] { "Raspberry", "Blueberries", "Honey", "CookedMeat" })
            {
                GameObject prefab = ObjectDB.instance.GetItemPrefab(prefabName);
                ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null ||
                    drop.m_itemData.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Consumable) continue;
                ItemDrop.ItemData item = drop.m_itemData.Clone();
                item.m_dropPrefab = prefab;
                item.m_stack = 1;
                item.m_equipped = false;
                item.m_gridPos = new Vector2i(x, y);
                return item;
            }
            throw new InvalidOperationException("No audited vanilla food prefab was available.");
        }

        private static ItemDrop.ItemData CloneKnownCookingIngredient(int x, int y)
        {
            foreach (string prefabName in new[] { "RawMeat", "NeckTail", "DeerMeat" })
            {
                GameObject prefab = ObjectDB.instance.GetItemPrefab(prefabName);
                ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null) continue;
                ItemDrop.ItemData item = drop.m_itemData.Clone();
                item.m_dropPrefab = prefab;
                item.m_stack = 1;
                item.m_equipped = false;
                item.m_gridPos = new Vector2i(x, y);
                return item;
            }
            throw new InvalidOperationException("No audited vanilla cooking ingredient prefab was available.");
        }

        private static ItemDrop.ItemData CloneKnownMaterial(int x, int y)
        {
            foreach (string prefabName in new[] { "Wood", "Stone", "RawMeat" })
            {
                GameObject prefab = ObjectDB.instance.GetItemPrefab(prefabName);
                ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null ||
                    drop.m_itemData.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Material) continue;
                ItemDrop.ItemData item = drop.m_itemData.Clone();
                item.m_dropPrefab = prefab;
                item.m_stack = 1;
                item.m_equipped = false;
                item.m_gridPos = new Vector2i(x, y);
                return item;
            }
            throw new InvalidOperationException("No audited vanilla material prefab was available.");
        }

        private static ItemDrop.ItemData CreateFoodItem(string name, int x, int y) =>
            new ItemDrop.ItemData
            {
                m_shared = new ItemDrop.ItemData.SharedData
                {
                    m_name = name,
                    m_itemType = ItemDrop.ItemData.ItemType.Consumable,
                    m_maxStackSize = 50
                },
                m_stack = 1,
                m_equipped = false,
                m_gridPos = new Vector2i(x, y)
            };

        private void AssertInactiveMode(
            object runtime,
            FieldInfo modeField,
            FieldInfo reasonField,
            FieldInfo loadField,
            FieldInfo disposedField,
            MethodInfo protectionMethod,
            object item,
            string mode,
            string reason,
            bool loading,
            bool disposed,
            bool expectedApplies,
            int expectedState,
            string scenario)
        {
            SetRuntimeState(runtime, modeField, reasonField, loadField, disposedField,
                mode, reason, loading, disposed);
            AssertProtection(protectionMethod, item, expectedApplies, expectedState, scenario);
        }

        private static void SetRuntimeState(
            object runtime,
            FieldInfo modeField,
            FieldInfo reasonField,
            FieldInfo loadField,
            FieldInfo disposedField,
            string mode,
            string reason,
            bool loading,
            bool disposed)
        {
            modeField.SetValue(runtime, Enum.Parse(modeField.FieldType, mode));
            reasonField.SetValue(runtime, reason);
            loadField.SetValue(runtime, loading);
            disposedField.SetValue(runtime, disposed);
        }

        private void AssertProtection(
            MethodInfo method,
            object item,
            bool expectedApplies,
            int expectedState,
            string scenario)
        {
            object[] arguments = { item, 0 };
            object raw = method.Invoke(null, arguments);
            Require(raw is bool && (bool)raw == expectedApplies,
                scenario + " expected applicability " + expectedApplies +
                " but observed " + (raw ?? "null") + " with state " + arguments[1] + ".");
            Require(arguments[1] is int && (int)arguments[1] == expectedState,
                scenario + " returned state " + arguments[1] + ".");
            Pass(scenario);
        }

        private static int ResolveSafety(Assembly safetyAssembly, object item, out bool present)
        {
            Type adapter = RequiredType(safetyAssembly, "RunicSafety.Services.InventoryProtectionAdapter");
            MethodInfo resolve = RequiredMethod(adapter, "Resolve",
                BindingFlags.NonPublic | BindingFlags.Static, null, item.GetType(), typeof(bool).MakeByRefType());
            object[] arguments = { item, false };
            object state = resolve.Invoke(null, arguments);
            present = arguments[1] is bool && (bool)arguments[1];
            return Convert.ToInt32(state, CultureInfo.InvariantCulture);
        }

        private static bool AuthorizeCooking(Assembly safetyAssembly, ItemDrop.ItemData item)
        {
            Type pluginType = RequiredType(safetyAssembly, "RunicSafety.Plugin");
            PropertyInfo runtimeProperty = pluginType.GetProperty(
                "CurrentRuntime", BindingFlags.NonPublic | BindingFlags.Static);
            Require(runtimeProperty != null, "Safety CurrentRuntime is missing.");
            object runtime = runtimeProperty.GetValue(null, null);
            Require(runtime != null, "Safety runtime is unavailable.");
            Type destinationType = RequiredType(safetyAssembly, "RunicSafety.Api.ProtectionDestination");
            MethodInfo authorize = runtime.GetType().GetMethod(
                "AuthorizeItem", BindingFlags.NonPublic | BindingFlags.Instance);
            Require(authorize != null, "Safety AuthorizeItem path is missing.");
            object destination = Enum.Parse(destinationType, "CookingStation");
            return (bool)authorize.Invoke(runtime, new object[] { item, destination, null, null });
        }

        private static int ResolveInteraction(Assembly interactionAssembly, object item)
        {
            Type adapter = RequiredType(interactionAssembly, "RunicInteraction.Core.ItemProtectionQueryAdapter");
            MethodInfo resolve = adapter.GetMethod(
                "Resolve", BindingFlags.NonPublic | BindingFlags.Static);
            Require(resolve != null, "Interaction Resolve path is missing.");
            return Convert.ToInt32(resolve.Invoke(null, new[] { item }), CultureInfo.InvariantCulture);
        }

        private static bool InteractionAllows(Assembly interactionAssembly, int state)
        {
            Type adapter = RequiredType(interactionAssembly, "RunicInteraction.Core.ItemProtectionQueryAdapter");
            MethodInfo allows = adapter.GetMethod(
                "AllowsTransfer", BindingFlags.NonPublic | BindingFlags.Static);
            Require(allows != null, "Interaction AllowsTransfer path is missing.");
            Type stateType = allows.GetParameters()[0].ParameterType;
            return (bool)allows.Invoke(null, new[] { Enum.ToObject(stateType, state) });
        }

        private void AssertStorageCapture(
            Assembly storageAssembly,
            ItemDrop.ItemData item,
            bool expectedSuccess,
            string expectedFailureCode,
            int expectedState,
            string scenario)
        {
            Type adapter = RequiredType(storageAssembly, "RunicStorage.Engine.StorageItemProtection");
            MethodInfo generic = null;
            foreach (MethodInfo candidate in adapter.GetMethods(BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (candidate.Name == "TryCapture" && candidate.IsGenericMethodDefinition)
                {
                    generic = candidate;
                    break;
                }
            }
            Require(generic != null, "Storage TryCapture path is missing.");
            MethodInfo capture = generic.MakeGenericMethod(typeof(ItemDrop.ItemData));
            var items = new List<ItemDrop.ItemData> { item };
            object[] arguments = { items, null, null };
            bool success = capture.Invoke(null, arguments) is bool value && value;
            Require(success == expectedSuccess,
                scenario + " expected success " + expectedSuccess + " but observed " + success + ".");
            Require(string.Equals(arguments[2] as string, expectedFailureCode, StringComparison.Ordinal),
                scenario + " returned failure code " + (arguments[2] ?? "null") + ".");
            if (expectedSuccess)
            {
                Require(arguments[1] != null, scenario + " returned no protection snapshot.");
                MethodInfo stateAt = arguments[1].GetType().GetMethod(
                    "StateAt", BindingFlags.NonPublic | BindingFlags.Instance);
                Require(stateAt != null, "Storage protection snapshot StateAt is missing.");
                int state = Convert.ToInt32(
                    stateAt.Invoke(arguments[1], new object[] { 0 }), CultureInfo.InvariantCulture);
                Require(state == expectedState,
                    scenario + " expected state " + expectedState + " but observed " + state + ".");
            }
            Pass(scenario);
        }

        private static object GetInventoryRuntime(BaseUnityPlugin inventoryPlugin)
        {
            PropertyInfo property = inventoryPlugin.GetType().GetProperty(
                "Runtime", BindingFlags.NonPublic | BindingFlags.Instance);
            Require(property != null, "Inventory runtime property is missing.");
            object runtime = property.GetValue(inventoryPlugin, null);
            Require(runtime != null, "Inventory runtime is unavailable.");
            return runtime;
        }

        private static PluginInfo RequirePlugin(string guid, string version)
        {
            Require(Chainloader.PluginInfos.TryGetValue(guid, out PluginInfo info) && info?.Instance != null,
                "Required plugin is unavailable: " + guid + ".");
            Require(string.Equals(info.Metadata.Version.ToString(), version, StringComparison.Ordinal),
                guid + " expected " + version + " but loaded " + info.Metadata.Version + ".");
            return info;
        }

        private static Type RequiredType(Assembly assembly, string name)
        {
            Type type = assembly.GetType(name, false);
            Require(type != null, "Required type is missing: " + name + ".");
            return type;
        }

        private static FieldInfo RequiredField(Type type, string name)
        {
            Type current = type;
            while (current != null)
            {
                FieldInfo field = current.GetField(
                    name, BindingFlags.Public | BindingFlags.NonPublic |
                          BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
                if (field != null) return field;
                current = current.BaseType;
            }
            throw new MissingFieldException(type.FullName, name);
        }

        private static MethodInfo RequiredMethod(
            Type type,
            string name,
            BindingFlags flags,
            Type returnType,
            params Type[] parameters)
        {
            MethodInfo method = type.GetMethod(name, flags, null, parameters, null);
            Require(method != null, "Required method is missing: " + type.FullName + "." + name + ".");
            if (returnType != null)
                Require(method.ReturnType == returnType, "Required method return type drifted: " + name + ".");
            return method;
        }

        private void Pass(string scenario)
        {
            Require(!string.IsNullOrWhiteSpace(scenario) && scenario.Length <= 96,
                "A scenario identifier is invalid.");
            _scenarios.Add(scenario);
            Logger.LogInfo("RUNIC_INVENTORY_RELEASE_SCENARIO_PASS " + scenario);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static string Bound(string value, int maximum)
        {
            string normalized = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
            return normalized.Length <= maximum ? normalized : normalized.Substring(0, maximum);
        }
    }
}

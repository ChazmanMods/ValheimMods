using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RunicProduction.Core;
using UnityEngine;

namespace RunicProduction.Integration
{
    internal enum CookingSlotStatus
    {
        NotDone = 0,
        Done = 1,
        Burnt = 2
    }

    internal enum FermenterSlotStatus
    {
        Empty = 0,
        Exposed = 1,
        Fermenting = 2,
        Ready = 3
    }

    internal static class ValheimAccess
    {
        private static readonly Func<bool> ReadBypassCheatChecks =
            NativeCheatChecks.CreateReader(typeof(PlayerProfile));
        internal static bool BypassCheatChecks => ReadBypassCheatChecks();

        private delegate bool PlayerInputDelegate(Player player);

        private static readonly AccessTools.FieldRef<Smelter, ZNetView> SmelterView =
            AccessTools.FieldRefAccess<Smelter, ZNetView>("m_nview");
        private static readonly AccessTools.FieldRef<CookingStation, ZNetView> CookingView =
            AccessTools.FieldRefAccess<CookingStation, ZNetView>("m_nview");
        private static readonly AccessTools.FieldRef<Fermenter, ZNetView> FermenterView =
            AccessTools.FieldRefAccess<Fermenter, ZNetView>("m_nview");
        private static readonly AccessTools.FieldRef<Fireplace, ZNetView> FireplaceView =
            AccessTools.FieldRefAccess<Fireplace, ZNetView>("m_nview");
        private static readonly AccessTools.FieldRef<Fermenter, bool> FermenterHasRoof =
            AccessTools.FieldRefAccess<Fermenter, bool>("m_hasRoof");
        private static readonly AccessTools.FieldRef<Fermenter, bool> FermenterExposed =
            AccessTools.FieldRefAccess<Fermenter, bool>("m_exposed");
        private static readonly AccessTools.FieldRef<Container, ZNetView> ContainerView =
            AccessTools.FieldRefAccess<Container, ZNetView>("m_nview");

        private static readonly MethodInfo QueueSizeMethod =
            RequiredMethod(typeof(Smelter), "GetQueueSize");
        private static readonly MethodInfo QueueOreMethod =
            RequiredMethod(typeof(Smelter), "QueueOre", typeof(string), typeof(bool));
        private static readonly MethodInfo FuelMethod =
            RequiredMethod(typeof(Smelter), "GetFuel");
        private static readonly MethodInfo SetFuelMethod =
            RequiredMethod(typeof(Smelter), "SetFuel", typeof(float));
        private static readonly MethodInfo ConversionMethod =
            RequiredMethod(typeof(Smelter), "GetItemConversion", typeof(string));

        private static readonly Type CookingStatusType =
            typeof(CookingStation).GetNestedType("Status", BindingFlags.NonPublic) ??
            throw new MissingMemberException(typeof(CookingStation).FullName, "Status");
        private static readonly MethodInfo CookingGetSlotMethod = RequiredMethod(
            typeof(CookingStation), "GetSlot", typeof(int),
            typeof(string).MakeByRefType(), typeof(float).MakeByRefType(),
            CookingStatusType.MakeByRefType(), typeof(bool).MakeByRefType());
        private static readonly MethodInfo CookingSetSlotMethod = RequiredMethod(
            typeof(CookingStation), "SetSlot", typeof(int), typeof(string),
            typeof(float), CookingStatusType, typeof(bool));
        private static readonly MethodInfo CookingGetFuelMethod =
            RequiredMethod(typeof(CookingStation), "GetFuel");
        private static readonly MethodInfo CookingSetFuelMethod =
            RequiredMethod(typeof(CookingStation), "SetFuel", typeof(float));
        private static readonly MethodInfo CookingIsFireLitMethod =
            RequiredMethod(typeof(CookingStation), "IsFireLit");

        private static readonly MethodInfo FermenterUpdateCoverMethod =
            RequiredMethod(typeof(Fermenter), "UpdateCover", typeof(float), typeof(bool));
        private static readonly MethodInfo FireplaceSetFuelMethod =
            RequiredMethod(typeof(Fireplace), "SetFuel", typeof(float));
        private static readonly MethodInfo ContainerAccessMethod =
            RequiredMethod(typeof(Container), "CheckAccess", typeof(long));
        private static readonly MethodInfo ContainerLoadMethod =
            RequiredMethod(typeof(Container), "Load");
        private static readonly FieldInfo ContainerLoadingField =
            AccessTools.Field(typeof(Container), "m_loading") ??
            throw new MissingFieldException(typeof(Container).FullName, "m_loading");
        private static readonly MethodInfo PrivateAreaEnabledMethod =
            RequiredMethod(typeof(PrivateArea), "IsEnabled");
        private static readonly MethodInfo PrivateAreaInsideMethod =
            RequiredMethod(typeof(PrivateArea), "IsInside", typeof(Vector3), typeof(float));
        private static readonly MethodInfo PrivateAreaPermittedMethod =
            RequiredMethod(typeof(PrivateArea), "IsPermitted", typeof(long));
        private static readonly FieldInfo AllPrivateAreasField =
            AccessTools.Field(typeof(PrivateArea), "m_allAreas") ??
            throw new MissingFieldException(typeof(PrivateArea).FullName, "m_allAreas");
        private static readonly PlayerInputDelegate PlayerTakeInput = ResolvePlayerTakeInput();

        internal static void VerifySignatures()
        {
            FermenterContentStorage.VerifySignature();
            _ = QueueSizeMethod;
            _ = SmelterView;
            _ = CookingView;
            _ = FermenterView;
            _ = FireplaceView;
            _ = ContainerView;
            _ = FermenterUpdateCoverMethod;
            _ = FireplaceSetFuelMethod;
            _ = ContainerLoadMethod;
            _ = AllPrivateAreasField;
            if (PlayerTakeInput == null)
                throw new MissingMethodException(typeof(Player).FullName, "TakeInput");
        }

        internal static ZNetView View(Smelter station) =>
            station == null ? null : SmelterView(station);

        internal static ZNetView View(CookingStation station) =>
            station == null ? null : CookingView(station);

        internal static ZNetView View(Fermenter station) =>
            station == null ? null : FermenterView(station);

        internal static ZNetView View(Fireplace station) =>
            station == null ? null : FireplaceView(station);

        internal static ZNetView View(CraftingStation station) =>
            station == null ? null : station.GetComponent<ZNetView>();

        internal static ZNetView View(Container container)
        {
            if (container == null) return null;
            return container.m_rootObjectOverride != null
                ? container.m_rootObjectOverride
                : ContainerView(container);
        }

        internal static ZNetView View(Component component)
        {
            if (component is Smelter smelter) return View(smelter);
            if (component is CookingStation cooking) return View(cooking);
            if (component is Fermenter fermenter) return View(fermenter);
            if (component is Fireplace fireplace) return View(fireplace);
            if (component is CraftingStation crafting) return View(crafting);
            if (component is Container container) return View(container);
            return component == null ? null : component.GetComponentInParent<ZNetView>();
        }

        internal static ZDO Zdo(Smelter station) => View(station)?.GetZDO();
        internal static ZDO Zdo(CookingStation station) => View(station)?.GetZDO();
        internal static ZDO Zdo(Fermenter station) => View(station)?.GetZDO();
        internal static ZDO Zdo(Fireplace station) => View(station)?.GetZDO();
        internal static ZDO Zdo(CraftingStation station) => View(station)?.GetZDO();
        internal static ZDO Zdo(Container container) => View(container)?.GetZDO();
        internal static ZDO Zdo(Component component) => View(component)?.GetZDO();

        internal static bool IsNativeOwner(Component component)
        {
            ZNetView view = View(component);
            return view != null && view.IsValid() && view.IsOwner() && view.GetZDO() != null;
        }

        internal static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);

        internal static string StableId(Component component)
        {
            ZNetView view = View(component);
            return ProductionEndpointIdentity.TryGetOrEnsureToken(view, out string token)
                ? token
                : string.Empty;
        }

        internal static bool TryGetCookingStationPrefab(
            CookingStation station,
            out string prefabId,
            out GameObject registeredPrefab)
        {
            prefabId = string.Empty;
            registeredPrefab = null;
            ZDO zdo = Zdo(station);
            if (station == null || zdo == null || ZNetScene.instance == null) return false;
            registeredPrefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
            if (registeredPrefab == null ||
                registeredPrefab.GetComponent<CookingStation>() == null) return false;
            prefabId = PrefabName(registeredPrefab);
            return !string.IsNullOrEmpty(prefabId);
        }

        internal static bool TryGetFermenterPrefab(
            Fermenter station,
            out string prefabId,
            out GameObject registeredPrefab)
        {
            prefabId = string.Empty;
            registeredPrefab = null;
            ZDO zdo = Zdo(station);
            if (station == null || zdo == null || ZNetScene.instance == null) return false;
            registeredPrefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
            if (registeredPrefab == null ||
                registeredPrefab.GetComponent<Fermenter>() == null) return false;
            prefabId = PrefabName(registeredPrefab);
            return !string.IsNullOrEmpty(prefabId);
        }

        internal static GameObject RegisteredItemPrefab(string prefabId)
        {
            if (string.IsNullOrEmpty(prefabId)) return null;
            GameObject prefab = ObjectDB.instance?.GetItemPrefab(prefabId);
            return prefab != null ? prefab : ZNetScene.instance?.GetPrefab(prefabId);
        }

        internal static int QueueSize(Smelter station) =>
            (int)QueueSizeMethod.Invoke(station, null);

        internal static void QueueOre(
            Smelter station,
            string prefabName,
            bool cheated) =>
            QueueOreMethod.Invoke(station, new object[] { prefabName, cheated });

        internal static bool SmelterQueuedCheated(Smelter station) =>
            Zdo(station)?.GetBool(ZDOVars.s_cheatedQueued, false) ?? false;

        internal static bool SmelterOutputCheated(Smelter station)
        {
            ZDO zdo = Zdo(station);
            return zdo != null &&
                   (zdo.GetBool(ZDOVars.s_cheatedQueued, false) ||
                    zdo.GetBool(ZDOVars.s_cheated, false)) &&
                   !BypassCheatChecks;
        }

        internal static float Fuel(Smelter station) =>
            (float)FuelMethod.Invoke(station, null);

        internal static void SetFuel(Smelter station, float value) =>
            SetFuelMethod.Invoke(station, new object[] { value });

        internal static Smelter.ItemConversion Conversion(
            Smelter station,
            string inputPrefab) =>
            (Smelter.ItemConversion)ConversionMethod.Invoke(
                station, new object[] { inputPrefab });

        internal static string QueueTail(Smelter station, int index) =>
            Zdo(station)?.GetString("item" + index, string.Empty) ?? string.Empty;

        internal static void RestoreQueueTail(
            Smelter station,
            int previousCount,
            string previousValue,
            bool previousCheated)
        {
            ZDO zdo = Zdo(station) ??
                      throw new InvalidOperationException("Smelter ZDO is unavailable.");
            zdo.Set("item" + previousCount, previousValue ?? string.Empty);
            zdo.Set(ZDOVars.s_queued, Math.Max(0, previousCount), false);
            zdo.Set(ZDOVars.s_cheatedQueued, previousCheated);
        }

        internal static void GetCookingSlot(
            CookingStation station,
            int slot,
            out string item,
            out float elapsed,
            out CookingSlotStatus status)
        {
            GetCookingSlot(
                station, slot, out item, out elapsed, out status, out _);
        }

        internal static void GetCookingSlot(
            CookingStation station,
            int slot,
            out string item,
            out float elapsed,
            out CookingSlotStatus status,
            out bool cheated)
        {
            object[] values =
            {
                slot, string.Empty, 0f, Enum.ToObject(CookingStatusType, 0), false
            };
            CookingGetSlotMethod.Invoke(station, values);
            item = values[1] as string ?? string.Empty;
            elapsed = (float)values[2];
            status = (CookingSlotStatus)Convert.ToInt32(values[3]);
            cheated = (bool)values[4];
        }

        internal static void SetCookingSlot(
            CookingStation station,
            int slot,
            string item,
            float elapsed,
            CookingSlotStatus status,
            bool cheated = false)
        {
            CookingSetSlotMethod.Invoke(station, new[]
            {
                (object)slot,
                item ?? string.Empty,
                elapsed,
                Enum.ToObject(CookingStatusType, (int)status),
                cheated
            });
            ZNetView view = View(station);
            if (view != null && view.IsValid())
                view.InvokeRPC(
                    ZNetView.Everybody, "RPC_SetSlotVisual", slot, item ?? string.Empty);
        }

        internal static float CookingFuel(CookingStation station) =>
            (float)CookingGetFuelMethod.Invoke(station, null);

        internal static void SetCookingFuel(CookingStation station, float value) =>
            CookingSetFuelMethod.Invoke(station, new object[] { value });

        internal static bool CookingFireLit(CookingStation station) =>
            (bool)CookingIsFireLitMethod.Invoke(station, null);

        internal static string FermenterContent(Fermenter station) =>
            FermenterContentStorage.Read(Zdo(station));

        internal static long FermenterStartTicks(Fermenter station) =>
            string.IsNullOrEmpty(FermenterContent(station)) ? 0L :
                Zdo(station)?.GetLong(ZDOVars.s_startTime, 0L) ?? 0L;

        internal static bool FermenterCheated(Fermenter station) =>
            Zdo(station)?.GetBool(ZDOVars.s_cheatedQueued, false) ?? false;

        internal static bool FermenterOutputCheated(Fermenter station)
        {
            ZDO zdo = Zdo(station);
            return zdo != null &&
                   (zdo.GetBool(ZDOVars.s_cheatedQueued, false) ||
                    zdo.GetBool(ZDOVars.s_cheated, false)) &&
                   !BypassCheatChecks;
        }

        internal static void SetFermenterState(
            Fermenter station,
            string content,
            long startTicks,
            bool cheated = false)
        {
            ZDO zdo = Zdo(station) ??
                      throw new InvalidOperationException("Fermenter ZDO is unavailable.");
            string exact = content ?? string.Empty;
            if (exact.Length == 0)
            {
                FermenterContentStorage.Write(zdo, string.Empty);
                zdo.Set(ZDOVars.s_startTime, 0L);
                zdo.Set(ZDOVars.s_cheatedQueued, false);
                return;
            }
            if (startTicks <= 0L) throw new ArgumentOutOfRangeException(nameof(startTicks));
            FermenterContentStorage.Write(zdo, exact);
            zdo.Set(ZDOVars.s_startTime, startTicks);
            zdo.Set(ZDOVars.s_cheatedQueued, cheated);
        }

        internal static bool FermenterCovered(Fermenter station) =>
            station != null && FermenterHasRoof(station) && !FermenterExposed(station);

        internal static void RefreshFermenterCover(Fermenter station)
        {
            if (station == null) throw new ArgumentNullException(nameof(station));
            FermenterUpdateCoverMethod.Invoke(station, new object[] { 0f, true });
        }

        internal static bool FermenterDelayedTapActive(Fermenter station) =>
            station != null && station.IsInvoking("DelayedTap");

        internal static FermenterSlotStatus FermenterStatus(Fermenter station)
        {
            if (station == null) return FermenterSlotStatus.Empty;
            string content = FermenterContent(station);
            if (string.IsNullOrEmpty(content)) return FermenterSlotStatus.Empty;
            long ticks = FermenterStartTicks(station);
            if (ticks <= 0L || ZNet.instance == null) return FermenterSlotStatus.Exposed;
            try
            {
                double elapsed =
                    (ZNet.instance.GetTime() - new DateTime(ticks)).TotalSeconds;
                return elapsed > station.m_fermentationDuration
                    ? FermenterSlotStatus.Ready
                    : FermenterSlotStatus.Fermenting;
            }
            catch (ArgumentOutOfRangeException)
            {
                return FermenterSlotStatus.Exposed;
            }
        }

        internal static long NetworkTimeTicks()
        {
            if (ZNet.instance == null)
                throw new InvalidOperationException("Network time is unavailable.");
            return ZNet.instance.GetTime().Ticks;
        }

        internal static float FireplaceFuel(Fireplace station) =>
            Zdo(station)?.GetFloat(ZDOVars.s_fuel, 0f) ?? 0f;

        internal static void SetFireplaceFuel(Fireplace station, float value) =>
            FireplaceSetFuelMethod.Invoke(station, new object[] { value });

        internal static bool TryResolveExactContainer(
            GameObject root,
            ZDOID expected,
            out Container container)
        {
            container = null;
            if (root == null || expected.IsNone()) return false;
            Container[] candidates = root.GetComponentsInChildren<Container>(true);
            if (candidates == null || candidates.Length == 0 || candidates.Length > 64)
                return false;
            foreach (Container candidate in candidates)
            {
                ZDO zdo = Zdo(candidate);
                if (candidate == null || zdo == null || zdo.m_uid != expected) continue;
                if (container != null && !ReferenceEquals(container, candidate))
                {
                    container = null;
                    return false;
                }
                container = candidate;
            }
            return container != null;
        }

        internal static bool TryPrepareLinkOwnership(
            Player player, Component station, ZDO stationZdo,
            Container chest, ZDO chestZdo, float linkRange, out string detail)
        {
            detail = global::Runic.Localization.RunicText.Get("text_c87036cb2036");
            ZNetView stationView = View(station);
            ZNetView chestView = View(chest);
            bool AccessStillValid()
            {
                if (player == null || player != Player.m_localPlayer || !player.IsOwner() ||
                    player.GetPlayerID() == 0L || ZNet.instance == null ||
                    station == null || chest == null || !station.gameObject.activeInHierarchy ||
                    stationView == null || chestView == null || !stationView.IsValid() ||
                    !chestView.IsValid() || stationZdo == null || chestZdo == null ||
                    !ReferenceEquals(stationView.GetZDO(), stationZdo) ||
                    !ReferenceEquals(chestView.GetZDO(), chestZdo) ||
                    stationZdo.m_uid.IsNone() || chestZdo.m_uid.IsNone() ||
                    stationZdo.m_uid == chestZdo.m_uid ||
                    !NearbyIngredientContainerIndex.IsStaticNonWagon(chest) ||
                    !ContainerWritable(chest) || (bool)ContainerLoadingField.GetValue(chest)) return false;
                long actorId = player.GetPlayerID();
                Vector3 source = stationZdo.GetPosition();
                Vector3 target = chestZdo.GetPosition();
                return IsFinite(source) && IsFinite(target) && linkRange > 0f &&
                       (source - target).sqrMagnitude <= linkRange * linkRange &&
                       ContainerWithinReach(chest, player.transform.position,
                           Mathf.Clamp(player.m_maxInteractDistance, 1f, 10f)) &&
                       ContainerAllows(chest, actorId) && WardAllows(source, actorId) &&
                       WardAllows(target, actorId);
            }
            if (!AccessStillValid())
            {
                detail = global::Runic.Localization.RunicText.Get("text_270e3cda8267");
                return false;
            }
            bool OwnsStation() => stationView.IsValid() &&
                ReferenceEquals(stationView.GetZDO(), stationZdo) && stationView.IsOwner() &&
                stationZdo.GetOwner() == ZNet.GetUID();
            bool OwnsChest() => chestView.IsValid() &&
                ReferenceEquals(chestView.GetZDO(), chestZdo) && chestView.IsOwner() &&
                chestZdo.GetOwner() == ZNet.GetUID();
            if (!ProductionSetupOwnership.TryAcquire(AccessStillValid,
                    OwnsStation, () => stationView.ClaimOwnership(),
                    OwnsChest, () => { RunicAutomation.ContainerAuthority.TryAcquire(chest, "production-setup",
                        RunicAutomation.ContainerAuthority.PlayerContext(player), player.GetPlayerID()); })) return false;
            if (!RunicAutomation.ContainerAuthority.TryAcquire(chest, "production-setup",
                    RunicAutomation.ContainerAuthority.PlayerContext(player), player.GetPlayerID())) return false;
            detail = string.Empty;
            return true;
        }

        internal static bool TrySynchronizeLocallyOwnedContainer(
            Container container,
            out Inventory inventory)
        {
            inventory = null;
            if (container == null || !ContainerWritable(container) || RunicAutomation.ContainerAuthority.Blocked(container)) return false;
            ZNetView view = View(container);
            ZDO zdo = view != null && view.IsValid() && view.IsOwner()
                ? view.GetZDO()
                : null;
            if (zdo == null) return false;
            if (Runic.Compatibility.ModdedContainerCompatibility.IsDrawer(container))
                return ProductionContainerCompatibility.TryRefresh(container, out inventory);
            ZDOID exactId = zdo.m_uid;
            try { ContainerLoadMethod.Invoke(container, null); }
            catch { return false; }
            ZNetView current = View(container);
            if (current == null || !current.IsValid() || !current.IsOwner() ||
                current.GetZDO() == null || current.GetZDO().m_uid != exactId ||
                (bool)ContainerLoadingField.GetValue(container) || !ContainerWritable(container)) return false;
            inventory = container.GetInventory();
            if (inventory == null) return false;
            byte[] persisted = current.GetZDO().GetByteArray(ZDOVars.s_items);
            if (persisted == null || persisted.Length == 0)
                return inventory.GetAllItems().Count == 0;
            var snapshot = new ZPackage();
            inventory.Save(snapshot);
            return ProductionInventoryPayloadComparison.MatchesLoaded(persisted, snapshot.GetArray());
        }

        internal static bool ContainerAllows(Container container, long playerId)
        {
            if (container == null || playerId == 0L) return false;
            try
            {
                return (bool)ContainerAccessMethod.Invoke(
                    container, new object[] { playerId });
            }
            catch { return false; }
        }

        internal static bool ContainerWritable(Container container) =>
            container != null && container.isActiveAndEnabled && !RunicAutomation.ContainerAuthority.Blocked(container) && !container.IsInUse() &&
            ProductionContainerCompatibility.Allowed(container) &&
            Zdo(container) != null && Zdo(container).GetInt(ZDOVars.s_inUse, 0) == 0 &&
            (container.m_wagon == null || !container.m_wagon.InUse());

        internal static bool ContainerWithinReach(
            Container container,
            Vector3 actorPosition,
            float interactionRange) => ComponentWithinReach(container, actorPosition, interactionRange);

        internal static bool ComponentWithinReach(
            Component container,
            Vector3 actorPosition,
            float interactionRange)
        {
            if (container == null || interactionRange <= 0f ||
                !IsFinite(actorPosition)) return false;
            float squared = interactionRange * interactionRange;
            Collider[] colliders = container.GetComponentsInChildren<Collider>(true);
            if (colliders == null || colliders.Length > 64) return false;
            bool found = false;
            foreach (Collider collider in colliders)
            {
                if (collider == null || !collider.enabled) continue;
                found = true;
                Vector3 closest = collider.ClosestPoint(actorPosition);
                if (IsFinite(closest) &&
                    (actorPosition - closest).sqrMagnitude <= squared) return true;
            }
            return !found && IsFinite(container.transform.position) &&
                   (actorPosition - container.transform.position).sqrMagnitude <= squared;
        }

        internal static bool WardAllows(Vector3 position, long playerId)
        {
            if (!IsFinite(position) || playerId == 0L) return false;
            var areas = AllPrivateAreasField.GetValue(null) as List<PrivateArea>;
            if (areas == null) return false;
            bool found = false;
            bool allowed = false;
            bool denied = false;
            foreach (PrivateArea area in areas)
            {
                if (area == null ||
                    !(bool)PrivateAreaEnabledMethod.Invoke(area, null) ||
                    !(bool)PrivateAreaInsideMethod.Invoke(
                        area, new object[] { position, 0f })) continue;
                found = true;
                Piece piece = area.GetComponent<Piece>();
                long creator = piece == null ? 0L : piece.GetCreator();
                if (creator == 0L) return false;
                bool permits = creator == playerId ||
                               (bool)PrivateAreaPermittedMethod.Invoke(
                                   area, new object[] { playerId });
                allowed |= permits;
                denied |= !permits;
            }
            return !found || allowed && !denied;
        }

        internal static long Creator(Component component)
        {
            Piece piece = component == null
                ? null
                : component.GetComponentInParent<Piece>();
            return piece == null ? 0L : piece.GetCreator();
        }

        internal static string PrefabName(ItemDrop.ItemData item) =>
            item?.m_dropPrefab == null ? string.Empty : PrefabName(item.m_dropPrefab);

        internal static string PrefabName(GameObject prefab)
        {
            if (prefab == null) return string.Empty;
            string name = prefab.name ?? string.Empty;
            const string clone = "(Clone)";
            return name.EndsWith(clone, StringComparison.Ordinal)
                ? name.Substring(0, name.Length - clone.Length)
                : name;
        }

        internal static bool KeyboardKeyHeld(KeyCode key)
        {
            try { return ZInput.GetKey(key, false); }
            catch { return false; }
        }

        internal static bool PlayerTakesInput(Player player) =>
            player != null && PlayerTakeInput != null && PlayerTakeInput(player);

        internal static bool RawControllerAlternateUseHeld()
        {
            try
            {
                ZInput.ButtonDef definition = ZInput.instance?.GetButtonDef("JoyAltKeys");
                return definition != null &&
                       definition.Source == ZInput.InputSource.Gamepad &&
                       definition.Held;
            }
            catch { return false; }
        }

        internal static bool RawControllerButtonHeld(string action)
        {
            try
            {
                ZInput.ButtonDef definition = ZInput.instance?.GetButtonDef(action);
                return definition != null &&
                       definition.Source == ZInput.InputSource.Gamepad &&
                       definition.Held;
            }
            catch { return false; }
        }

        internal static void Message(Player player, string text)
        {
            if (player != null && !string.IsNullOrEmpty(text))
                player.Message(MessageHud.MessageType.Center, text, 0, null);
        }

        private static MethodInfo RequiredMethod(
            Type type,
            string name,
            params Type[] parameters)
        {
            MethodInfo method = AccessTools.Method(type, name, parameters);
            if (method == null) throw new MissingMethodException(type.FullName, name);
            return method;
        }

        private static PlayerInputDelegate ResolvePlayerTakeInput()
        {
            try
            {
                MethodInfo method = AccessTools.Method(
                    typeof(Player),
                    "TakeInput",
                    Type.EmptyTypes);
                return method == null
                    ? null
                    : AccessTools.MethodDelegate<PlayerInputDelegate>(method);
            }
            catch
            {
                return null;
            }
        }
    }
}

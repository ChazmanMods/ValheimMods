using HarmonyLib;
using System.Linq;
using TMPro;
using UnityEngine;

namespace RunicDisplayStands
{
    public class ContainerBridge : MonoBehaviour
    {
        private static ContainerBridge _opened;
        private static StandVirtualContainer _virtualContainer;
        private static bool _stackAllButtonOverridden;
        private static bool _stackAllButtonWasActive;
        private static string _stackAllButtonText;
        private static readonly System.Reflection.FieldInfo ContainerInventoryField =
            AccessTools.Field(typeof(Container), "m_inventory");
        private static readonly System.Reflection.MethodInfo InventoryChangedMethod =
            AccessTools.DeclaredMethod(typeof(Inventory), "Changed", new[] { typeof(bool), typeof(bool) });

        private Inventory _inventory;
        private ItemStand _itemStand;
        private ArmorStand _armorStand;
        private Player _user;
        private string _baseline;
        private bool _refreshing;
        private Transfer _transfer;

        internal static ContainerBridge Opened => _opened;
        internal bool InTransfer => _transfer != null;
        internal Inventory StandInventory => _inventory;
        internal bool IsArmorStand => _armorStand != null;
        internal bool HasAuthority => _user != null && _user == Player.m_localPlayer &&
            !_user.IsTeleporting() && GetComponent<ZNetView>() != null &&
            GetComponent<ZNetView>().IsValid() && GetComponent<ZNetView>().IsOwner() &&
            PrivateArea.CheckAccess(transform.position, 0f, flash: false);
        internal static bool IsStandInventory(Inventory inventory) =>
            _opened != null && ReferenceEquals(_opened._inventory, inventory);

        public static ContainerBridge GetOrCreate(GameObject go)
        {
            var bridge = go.GetComponent<ContainerBridge>();
            if (bridge != null) return bridge;
            bridge = go.AddComponent<ContainerBridge>();
            bridge.Initialize();
            return bridge;
        }

        private void Initialize()
        {
            _itemStand = GetComponent<ItemStand>();
            _armorStand = GetComponent<ArmorStand>();
            string name = _itemStand != null ? _itemStand.GetHoverName() : "$piece_armorstand";
            _inventory = new Inventory(name, null, _armorStand != null ? ArmorStandSlots.Width : 1,
                _armorStand != null ? ArmorStandSlots.Height : 1);
            _inventory.m_onChanged += OnInventoryChanged;
        }

        private System.Collections.Generic.List<ItemDrop.ItemData> ReadStand()
        {
            if (_armorStand != null) return ArmorStandAdapter.GetPlacedItems(_armorStand);
            return new System.Collections.Generic.List<ItemDrop.ItemData> { ItemStandAdapter.GetPlacedItem(_itemStand) };
        }

        private string StandContents(System.Collections.Generic.IEnumerable<ItemDrop.ItemData> items) =>
            _armorStand == null ? InventorySnapshot.Contents(items) :
            string.Join("\n", items.Where(i => i != null).OrderBy(ArmorStandSlots.Slot)
                .Select(i => ArmorStandSlots.Slot(i) + ":" + InventorySnapshot.Contents(new[] { i })));

        public bool OpenAsContainer(Humanoid user)
        {
            if (!(user is Player player) || player != Player.m_localPlayer || InventoryGui.instance == null) return false;
            if (_opened != null) return false;
            _user = player;
            if (!TryClaimOwnership()) { Warn("The stand is synchronizing. Please try again."); return false; }
            try { RefreshInventory(); }
            catch (System.Exception error) { Warn(error.Message); return false; }
            _opened = this;
            if (_virtualContainer == null)
            {
                var go = new GameObject("RunicDisplayStandsVirtualContainer");
                go.SetActive(false);
                _virtualContainer = go.AddComponent<StandVirtualContainer>();
            }
            _virtualContainer.m_name = _inventory.GetName();
            _virtualContainer.m_width = _inventory.GetWidth();
            _virtualContainer.m_height = _inventory.GetHeight();
            ContainerInventoryField.SetValue(_virtualContainer, _inventory);
            _virtualContainer.transform.SetParent(transform, false);
            InventoryGui.instance.Show(_virtualContainer, 1);
            SetUseEquipButton(true);
            return true;
        }

        public bool TakeOne(Humanoid user)
        {
            if (!(user is Player player) || player != Player.m_localPlayer || _opened != null) return false;
            _user = player;
            if (!TryClaimOwnership()) return false;
            try
            {
                RefreshInventory();
                return TakeItem(_inventory.GetItemAt(0, 0), false);
            }
            catch (System.Exception error) { Warn(error.Message); return false; }
        }

        internal bool TakeItem(ItemDrop.ItemData item, bool use)
        {
            if (item == null || !_inventory.ContainsItem(item)) return false;
            var received = item.Clone();
            received.m_stack = 1;
            received.m_equipped = false;
            bool success = Execute(() =>
            {
                if (!_user.GetInventory().AddItem(received))
                    throw new System.InvalidOperationException("There is no available inventory slot for that item.");
                _inventory.RemoveOneItem(item);
            });
            if (success && use)
            {
                // Equipment/use effects only run after both inventories committed.
                try { _user.EquipItem(received, triggerEquipEffects: false); }
                catch (System.Exception error) { Warn("Item transferred, but could not equip it: " + error.Message); }
            }
            return success;
        }

        internal void SwapEquip()
        {
            if (_user == null) return;
            if (_itemStand != null) { TakeItem(_inventory.GetItemAt(0, 0), true); return; }
            var outgoing = ArmorStandAdapter.PlayerLoadout(_user, _inventory);
            Execute(() => LoadoutSwap.Apply(_user.GetInventory(), _inventory, outgoing,
                item => _user.UnequipItem(item, triggerEquipEffects: false),
                item => _user.IsItemEquiped(item) || _user.EquipItem(item, triggerEquipEffects: false),
                RunicInventorySlots.Capture(_user)));
        }

        private bool Execute(System.Action action)
        {
            Transfer transfer = null;
            try
            {
                transfer = BeginTransfer();
                action();
                return transfer.Finish(true);
            }
            catch (System.Exception error)
            {
                transfer?.Finish(false);
                Warn(error.Message);
                return false;
            }
        }

        internal Transfer BeginTransfer()
        {
            if (_transfer != null) throw new System.InvalidOperationException("Another stand action is in progress.");
            if (!HasAuthority || _baseline != StandContents(ReadStand()))
                throw new System.InvalidOperationException("The stand changed or access was lost. Reopen it and try again.");
            return _transfer = new Transfer(this);
        }

        internal sealed class Transfer
        {
            private static readonly System.Reflection.FieldInfo[] EquipmentFields = new[]
            {
                "m_leftItem", "m_rightItem", "m_chestItem", "m_legItem", "m_helmetItem",
                "m_shoulderItem", "m_utilityItem", "m_trinketItem", "m_ammoItem",
                "m_hiddenLeftItem", "m_hiddenRightItem"
            }.Select(name => AccessTools.Field(typeof(Humanoid), name)).ToArray();
            private static readonly System.Reflection.MethodInfo SetupEquipmentMethod =
                AccessTools.Method(typeof(Humanoid), "SetupEquipment");
            private readonly ContainerBridge _bridge;
            private readonly InventorySnapshot _playerSnapshot;
            private readonly InventorySnapshot _standSnapshot;
            private readonly ItemDrop.ItemData[] _equipped;
            private readonly string _contents;
            private readonly System.Action _playerChanged;
            private readonly System.Action _standChanged;
            private bool _finished;

            internal Transfer(ContainerBridge bridge)
            {
                _bridge = bridge;
                var playerInventory = bridge._user.GetInventory();
                _playerSnapshot = new InventorySnapshot(playerInventory);
                _standSnapshot = new InventorySnapshot(bridge._inventory);
                _equipped = EquipmentFields.Select(field => (ItemDrop.ItemData)field.GetValue(bridge._user)).ToArray();
                _contents = InventorySnapshot.Contents(playerInventory.GetAllItems().Concat(bridge._inventory.GetAllItems()));
                _playerChanged = playerInventory.m_onChanged;
                _standChanged = bridge._inventory.m_onChanged;
                // No observer may publish an intermediate inventory state.
                playerInventory.m_onChanged = null;
                bridge._inventory.m_onChanged = null;
            }

            internal bool Finish(bool actionSucceeded)
            {
                if (_finished) return false;
                _finished = true;
                bool committed = false;
                var playerInventory = _bridge._user.GetInventory();
                try
                {
                    committed = TransferBoundary.Complete(actionSucceeded,
                        () => _bridge.HasAuthority &&
                            _bridge._baseline == _bridge.StandContents(_bridge.ReadStand()) &&
                            _contents == InventorySnapshot.Contents(playerInventory.GetAllItems().Concat(_bridge._inventory.GetAllItems())),
                        () => _bridge.CommitStand(), () =>
                        {
                            _playerSnapshot.Restore();
                            _standSnapshot.Restore();
                            // Restore exact drawn/sheathed references as well as item flags.
                            // EquipItem can reject during the same conditions that canceled
                            // the swap, so it is not a reliable rollback operation.
                            for (int i = 0; i < EquipmentFields.Length; i++)
                                EquipmentFields[i].SetValue(_bridge._user, _equipped[i]);
                            try { SetupEquipmentMethod.Invoke(_bridge._user, null); }
                            catch (System.Exception error) { Plugin.Log?.LogWarning("Equipment restored; visual refresh failed: " + error.Message); }
                        });
                    if (!committed) _bridge.Warn("Transfer canceled; items stayed in their original inventories. Reopen the stand if its ownership changed.");
                }
                catch (System.Exception error) { _bridge.Warn("Transfer canceled: " + error.Message); }
                finally
                {
                    playerInventory.m_onChanged = _playerChanged;
                    _bridge._inventory.m_onChanged = _standChanged;
                    _bridge._transfer = null;
                    try
                    {
                        InventoryChangedMethod.Invoke(playerInventory, new object[] { committed, false });
                        InventoryChangedMethod.Invoke(_bridge._inventory, new object[] { committed, false });
                    }
                    catch (System.Exception error) { Plugin.Log?.LogWarning("Inventory refresh failed: " + error.Message); }
                }
                if (committed)
                {
                    _bridge._baseline = _bridge.StandContents(_bridge._inventory.GetAllItems());
                    try { _bridge.PublishVisuals(); }
                    catch (System.Exception error) { Plugin.Log?.LogWarning("Stand transfer saved; visual refresh failed: " + error.Message); }
                }
                return committed;
            }
        }

        private bool CommitStand()
        {
            var items = _inventory.GetAllItems();
            StandWriteBatch batch;
            if (_itemStand != null)
            {
                if (items.Count > 1) throw new System.InvalidOperationException("An item stand holds one item.");
                batch = ItemStandAdapter.PrepareWrite(_itemStand, items.Count == 0 ? null : items[0]);
            }
            else batch = ArmorStandAdapter.PrepareWrite(_armorStand, items);
            // Preparation is read-only; recheck after serialization and before the first write.
            if (!HasAuthority || _baseline != StandContents(ReadStand())) return false;
            return batch.Commit();
        }

        private void PublishVisuals()
        {
            var items = _inventory.GetAllItems();
            if (_itemStand != null) ItemStandAdapter.PublishVisual(_itemStand, items.Count == 0 ? null : items[0]);
            else ArmorStandAdapter.PublishVisuals(_armorStand, items);
        }

        internal static void Closed()
        {
            if (_opened == null) return;
            // There is nothing to flush: every successful action already committed.
            if (_opened._transfer != null) return;
            _opened = null;
            SetUseEquipButton(false);
            if (_virtualContainer != null)
            {
                Destroy(_virtualContainer.gameObject);
                _virtualContainer = null;
            }
        }

        private void RefreshInventory()
        {
            var items = ReadStand();
            _baseline = StandContents(items);
            _refreshing = true;
            try
            {
                _inventory.GetAllItems().Clear();
                for (int i = 0; i < items.Count; i++)
                    if (items[i] != null)
                    {
                        items[i].m_gridPos = StandGridLayout.Position(i, _inventory.GetWidth());
                        items[i].m_equipped = false;
                        _inventory.GetAllItems().Add(items[i]);
                    }
            }
            finally { _refreshing = false; }
        }

        private void OnInventoryChanged()
        {
            if (_refreshing || InTransfer) return;
            // Persistence is deliberately not driven by individual item callbacks.
        }

        private bool TryClaimOwnership()
        {
            var nview = GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid() || !PrivateArea.CheckAccess(transform.position)) return false;
            nview.ClaimOwnership();
            return HasAuthority;
        }

        private void LateUpdate()
        {
            if (_opened == this && !InTransfer && !HasAuthority)
            {
                Warn("Stand access changed. Reopen it to continue.");
                InventoryGui.instance?.Hide();
                Closed();
            }
        }

        internal void Warn(string message)
        {
            Plugin.Log?.LogWarning(message);
            _user?.Message(MessageHud.MessageType.Center, global::Runic.Localization.RunicText.Get("text_1620e1d4e059") + message);
        }

        internal static void SetUseEquipButton(bool useEquip)
        {
            if (InventoryGui.instance == null || InventoryGui.instance.m_stackAllButton == null) return;
            var button = InventoryGui.instance.m_stackAllButton;
            var text = button.GetComponentInChildren<TMP_Text>(true);

            if (useEquip)
            {
                if (!_stackAllButtonOverridden)
                {
                    _stackAllButtonWasActive = button.gameObject.activeSelf;
                    _stackAllButtonText = text?.text;
                    _stackAllButtonOverridden = true;
                }

                button.gameObject.SetActive(true);
                if (text != null) text.text = "Use/equip";
                return;
            }

            if (!_stackAllButtonOverridden) return;
            button.gameObject.SetActive(_stackAllButtonWasActive);
            if (text != null) text.text = _stackAllButtonText;
            _stackAllButtonOverridden = false;
            _stackAllButtonText = null;
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "UpdateContainer")]
    [HarmonyPriority(Priority.Last)]
    internal static class InventoryGui_UpdateContainer_Stand_Patch
    {
        private static void Postfix()
        {
            if (ContainerBridge.Opened != null) ContainerBridge.SetUseEquipButton(true);
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "OnStackAll")]
    [HarmonyPriority(Priority.First)]
    internal static class InventoryGui_OnStackAll_Stand_Patch
    {
        private static bool Prefix()
        {
            if (ContainerBridge.Opened == null) return true;
            ContainerBridge.Opened.SwapEquip();
            return false;
        }
    }

    internal sealed class StandVirtualContainer : Container { }

    [HarmonyPatch(typeof(Container), nameof(Container.IsOwner))]
    internal static class VirtualContainer_IsOwner_Patch
    {
        private static bool Prefix(Container __instance, ref bool __result)
        {
            if (!(__instance is StandVirtualContainer)) return true;
            __result = ContainerBridge.Opened != null && ContainerBridge.Opened.HasAuthority;
            return false;
        }
    }

    [HarmonyPatch(typeof(Container), nameof(Container.SetInUse))]
    internal static class VirtualContainer_SetInUse_Patch
    {
        private static bool Prefix(Container __instance) => !(__instance is StandVirtualContainer);
    }

    [HarmonyPatch(typeof(Container), nameof(Container.StackAll))]
    internal static class VirtualContainer_StackAll_Patch
    {
        private static bool Prefix(Container __instance)
        {
            if (!(__instance is StandVirtualContainer)) return true;
            ContainerBridge.Opened?.SwapEquip();
            return false;
        }
    }

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
    internal static class InventoryGui_Hide_Stand_Patch
    {
        private static void Prefix() => ContainerBridge.Closed();
    }

    [HarmonyPatch(typeof(InventoryGui), "CloseContainer")]
    internal static class InventoryGui_CloseContainer_Stand_Patch
    {
        private static void Prefix() => ContainerBridge.Closed();
    }
}

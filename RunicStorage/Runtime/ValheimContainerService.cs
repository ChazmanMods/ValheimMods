using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Runic.Compatibility;
using ItemData = ItemDrop.ItemData;

namespace RunicStorage.Runtime
{
    internal enum StorageMoveFailure { None, InvalidInput, OwnershipChanged, NoCapacity, SnapshotInvalid, UnsupportedDrawerMetadata, MutationBusy, RolledBack, Indeterminate }

    internal static class ValheimContainerService
    {
        private static readonly MethodInfo CheckAccessMethod =
            AccessTools.Method(typeof(Container), "CheckAccess");
        private static readonly MethodInfo InventoryChangedMethod =
            AccessTools.Method(
                typeof(Inventory),
                "Changed",
                new[] { typeof(bool), typeof(bool) });
        private static readonly MethodInfo InventoryAddAtMethod = AccessTools.Method(
            typeof(Inventory),
            "AddItem",
            new[] { typeof(ItemData), typeof(int), typeof(int), typeof(int), typeof(bool) });

        internal static bool CanDiscover(
            Container container,
            long playerId,
            bool requireWritable,
            bool allowCurrentUse = false)
        {
            if (container == null || !container.isActiveAndEnabled ||
                container.GetInventory() == null) return false;
            Player player = Player.m_localPlayer;
            if (player == null || player.GetPlayerID() != playerId) return false;
            if (container.m_checkGuardStone && !PrivateArea.CheckAccess(
                    container.transform.position, 0f, flash: false, wardCheck: false))
                return false;
            try
            {
                if (CheckAccessMethod == null ||
                    !(bool)CheckAccessMethod.Invoke(container, new object[] { playerId }))
                    return false;
                return !requireWritable ||
                       StorageContainerAuthority.TryClaimWritableInventory(
                           container,
                           allowCurrentUse);
            }
            catch { return false; }
        }

        // Native RemoveItem leaves the detached object's count unchanged after a full move.
        internal static bool HasSourceStack(Inventory source, ItemData item)
            => source != null && item != null && item.m_stack > 0 &&
               source.GetAllItems().Contains(item);

        internal static int MoveUpTo(
            Inventory source,
            Inventory destination,
            ItemData sourceItem,
            int maximumQuantity,
            Player playerInventoryOwner = null,
            StorageMutationLease playerMutationLease = null,
            Container sourceContainer = null,
            Container destinationContainer = null)
            => MoveUpTo(source, destination, sourceItem, maximumQuantity, out _,
                playerInventoryOwner, playerMutationLease, sourceContainer, destinationContainer);

        internal static int MoveUpTo(
            Inventory source, Inventory destination, ItemData sourceItem, int maximumQuantity,
            out StorageMoveFailure failure,
            Player playerInventoryOwner = null, StorageMutationLease playerMutationLease = null,
            Container sourceContainer = null, Container destinationContainer = null)
        {
            failure = StorageMoveFailure.InvalidInput;
            if (source == null || destination == null || sourceItem == null ||
                ReferenceEquals(source, destination) || maximumQuantity <= 0 ||
                !HasSourceStack(source, sourceItem)) return 0;

            if (RunicAutomation.MutationGate.IsBlocked(source) || RunicAutomation.MutationGate.IsBlocked(destination))
            { failure = StorageMoveFailure.Indeterminate; return 0; }
            RunicAutomation.MutationLease standalone = null;
            if (playerMutationLease == null && !RunicAutomation.MutationGate.TryBegin("storage-transfer", out standalone))
            { failure = StorageMoveFailure.MutationBusy; return 0; }
            using var standaloneScope = standalone;
            RunicAutomation.MutationLease operation = RunicAutomation.MutationGate.Current;
            operation?.Track(source); operation?.Track(destination);

            Inventory playerInventory = playerInventoryOwner?.GetInventory();
            bool touchesPlayer = ReferenceEquals(source, playerInventory) ||
                                 ReferenceEquals(destination, playerInventory);
            if (touchesPlayer &&
                (playerInventoryOwner == null ||
                 !ReferenceEquals(playerInventoryOwner, Player.m_localPlayer) ||
                 !playerInventoryOwner.IsOwner() ||
                 playerMutationLease == null ||
                 !playerMutationLease.Covers(playerInventoryOwner, playerInventory)))
                throw new InvalidOperationException(
                    "A player inventory move requires its exact local owner lease.");
            failure = StorageMoveFailure.OwnershipChanged;
            if (!StillOwned(sourceContainer) || !StillOwned(destinationContainer)) return 0;
            Func<bool> sourceAuthority = StorageContainerAuthority.CaptureAuthority(sourceContainer);
            Func<bool> destinationAuthority = StorageContainerAuthority.CaptureAuthority(destinationContainer);

            int requested = Math.Min(maximumQuantity, sourceItem.m_stack);
            bool sourceDrawer = ModdedContainerCompatibility.IsDrawer(sourceContainer);
            bool destinationDrawer = ModdedContainerCompatibility.IsDrawer(destinationContainer);
            ItemData template = sourceDrawer ? ModdedContainerCompatibility.NativeTemplate(sourceItem) : sourceItem;
            if (destinationDrawer && !CanStoreInDrawer(sourceItem, destination))
            {
                failure = destination.GetAllItems().Count == 1 &&
                    destination.GetAllItems()[0].m_dropPrefab == sourceItem.m_dropPrefab
                    ? StorageMoveFailure.UnsupportedDrawerMetadata : StorageMoveFailure.InvalidInput;
                return 0;
            }
            int maximumStack = destinationDrawer
                ? destination.GetAllItems()[0].m_shared.m_maxStackSize
                : Math.Max(1, template.m_shared?.m_maxStackSize ?? 1);
            int capacity = 0;
            foreach (ItemData item in destination.GetAllItems())
                if ((destinationDrawer || CanMergeExact(template, item, sourceDrawer)) && item.m_stack < maximumStack)
                    capacity = AddSaturated(capacity, maximumStack - item.m_stack);
            for (int y = 0; y < destination.GetHeight(); y++)
            for (int x = 0; x < destination.GetWidth(); x++)
                if (!destinationDrawer && destination.GetItemAt(x, y) == null)
                    capacity = AddSaturated(capacity, maximumStack);

            int quantity = Math.Min(requested, capacity);
            failure = StorageMoveFailure.NoCapacity;
            if (quantity <= 0) return 0;
            StorageInventorySnapshot sourceBefore;
            StorageInventorySnapshot destinationBefore;
            try
            {
                sourceBefore = new StorageInventorySnapshot(source);
                destinationBefore = new StorageInventorySnapshot(destination);
            }
            catch
            {
                failure = StorageMoveFailure.SnapshotInvalid;
                return 0;
            }
            string expectedSource = null, expectedDestination = null;
            try
            {
                Inventory sourceShadow = sourceBefore.CreateShadow();
                Inventory destinationShadow = destinationBefore.CreateShadow();
                ItemData shadowItem = sourceShadow.GetItemAt(
                    sourceItem.m_gridPos.x, sourceItem.m_gridPos.y);
                if (shadowItem == null)
                    throw new InvalidOperationException("The source stack changed before publication.");
                ApplyExactMove(sourceShadow, destinationShadow, shadowItem, quantity, sourceDrawer, destinationDrawer);
                expectedSource = Fingerprint(sourceShadow);
                expectedDestination = Fingerprint(destinationShadow);

                Action sourceChanged = source.m_onChanged, destinationChanged = destination.m_onChanged;
                try
                {
                    source.m_onChanged = null; destination.m_onChanged = null;
                    ApplyExactMove(source, destination, sourceItem, quantity, sourceDrawer, destinationDrawer);
                }
                finally { source.m_onChanged = sourceChanged; destination.m_onChanged = destinationChanged; }
                sourceChanged?.Invoke();
                destinationChanged?.Invoke();
                if (!sourceAuthority() || !destinationAuthority() ||
                    !string.Equals(Fingerprint(source), expectedSource, StringComparison.Ordinal) ||
                    !string.Equals(Fingerprint(destination), expectedDestination, StringComparison.Ordinal))
                    throw new InvalidOperationException("An endpoint changed during Storage publication.");
                failure = StorageMoveFailure.None;
                operation?.Complete(RunicAutomation.MutationOutcome.Committed);
                return quantity;
            }
            catch (Exception publicationFailure)
            {
                var restoreFailures = new List<Exception> { publicationFailure };
                try { RestoreKnown(source, sourceBefore, expectedSource, sourceAuthority, playerInventoryOwner, playerMutationLease); }
                catch (Exception exception) { restoreFailures.Add(exception); }
                try { RestoreKnown(destination, destinationBefore, expectedDestination, destinationAuthority, playerInventoryOwner, playerMutationLease); }
                catch (Exception exception) { restoreFailures.Add(exception); }
                if (restoreFailures.Count > 1)
                {
                    failure = StorageMoveFailure.Indeterminate;
                    RunicAutomation.MutationGate.Block(source); RunicAutomation.MutationGate.Block(destination);
                    operation?.Complete(RunicAutomation.MutationOutcome.Indeterminate);
                    throw new RunicAutomation.MutationIndeterminateException("storage " + operation?.Id,
                        new AggregateException(restoreFailures));
                }
                failure = StorageMoveFailure.RolledBack;
                operation?.Complete(RunicAutomation.MutationOutcome.RolledBack);
                throw;
            }
        }

        internal static void RestoreKnown(Inventory inventory, StorageInventorySnapshot snapshot,
            string expected, Func<bool> authority, Player player, StorageMutationLease lease)
        {
            if (!authority() || player != null && ReferenceEquals(player.GetInventory(), inventory) && !player.IsOwner())
                throw new InvalidOperationException("Recovery authority lost.");
            try { snapshot.Verify(inventory); return; } catch { }
            if (expected == null || Fingerprint(inventory) != expected)
                throw new InvalidOperationException("Recovery would overwrite an unrelated inventory change.");
            RestoreInventory(inventory, snapshot, player, lease);
        }

        internal static void RestoreInventory(
            Inventory inventory,
            StorageInventorySnapshot backup,
            Player inventoryOwner = null,
            StorageMutationLease playerMutationLease = null)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            if (backup == null) throw new ArgumentNullException(nameof(backup));
            // A player lease must cover only its own inventory, not the paired chest.
            if (inventoryOwner != null && ReferenceEquals(inventoryOwner.GetInventory(), inventory) &&
                (playerMutationLease == null || !playerMutationLease.Covers(inventoryOwner, inventory)))
                throw new InvalidOperationException("The player restore lease is unavailable.");
            Action changed = inventory.m_onChanged;
            inventory.m_onChanged = null;
            try { backup.Restore(inventory); }
            finally { inventory.m_onChanged = changed; }
            InventoryChangedMethod?.Invoke(inventory, new object[] { false, false });
            backup.Verify(inventory);
        }

        internal static Inventory CloneInventory(Inventory source) =>
            new StorageInventorySnapshot(source).CreateShadow();

        internal static string Fingerprint(Inventory inventory)
        {
            var text = new System.Text.StringBuilder(SaveInventory(inventory).GetBase64());
            foreach (ItemData item in inventory.GetAllItems()) text.Append('|').Append(item.m_stack);
            return text.ToString();
        }

        internal static ZPackage SaveInventory(Inventory source)
        {
            var package = new ZPackage();
            source.Save(package);
            return package;
        }

        internal static bool CanSnapshotExactly(Inventory inventory)
        {
            try { CloneInventory(inventory); return true; }
            catch { return false; }
        }

        private static void ApplyExactMove(
            Inventory source,
            Inventory destination,
            ItemData sourceItem,
            int quantity, bool sourceDrawer = false, bool destinationDrawer = false)
        {
            if (source.GetItemAt(sourceItem.m_gridPos.x, sourceItem.m_gridPos.y) != sourceItem ||
                sourceItem.m_stack < quantity)
                throw new InvalidOperationException("The source changed during an exact move.");
            if (destinationDrawer)
            {
                // Keep the drawer's own item and oversized SharedData inside the drawer.
                // Its callback persists the native drawer quantity; do not use AddItem RPCs.
                if (!CanStoreInDrawer(sourceItem, destination))
                    throw new InvalidOperationException("The drawer item type changed.");
                ItemData target = destination.GetAllItems()[0];
                if (quantity > target.m_shared.m_maxStackSize - target.m_stack)
                    throw new InvalidOperationException("The drawer capacity changed.");
                target.m_stack += quantity;
                InventoryChangedMethod.Invoke(destination, new object[] { false, false });
                if (!RemoveExact(source, sourceItem, quantity, sourceDrawer))
                    throw new InvalidOperationException("The source changed during a drawer move.");
                return;
            }
            ItemData template = sourceDrawer ? ModdedContainerCompatibility.NativeTemplate(sourceItem) : sourceItem;
            int maximumStack = Math.Max(1, template.m_shared?.m_maxStackSize ?? 1);
            var mergeTargets = new List<ItemData>();
            foreach (ItemData item in destination.GetAllItems())
                if (CanMergeExact(template, item, sourceDrawer) && item.m_stack < maximumStack)
                    mergeTargets.Add(item);
            mergeTargets.Sort(CompareItemSlots);

            int remaining = quantity;
            foreach (ItemData target in mergeTargets)
            {
                if (remaining == 0) break;
                int moved = Math.Min(remaining, maximumStack - target.m_stack);
                AddAt(destination, template, moved, target.m_gridPos);
                remaining -= moved;
            }
            for (int y = 0; y < destination.GetHeight() && remaining > 0; y++)
            for (int x = 0; x < destination.GetWidth() && remaining > 0; x++)
            {
                if (destination.GetItemAt(x, y) != null) continue;
                int moved = Math.Min(remaining, maximumStack);
                AddAt(destination, template, moved, new Vector2i(x, y));
                remaining -= moved;
            }
            if (remaining != 0 || !RemoveExact(source, sourceItem, quantity, sourceDrawer))
                throw new InvalidOperationException("The source changed during an exact move.");
        }

        private static bool RemoveExact(Inventory source, ItemData item, int quantity, bool drawer)
        {
            if (!drawer) return source.RemoveItem(item, quantity);
            if (!source.GetAllItems().Contains(item) || item.m_stack < quantity) return false;
            item.m_stack -= quantity; // Keep ItemDrawers' assigned empty item in both live and shadow state.
            InventoryChangedMethod.Invoke(source, new object[] { false, false });
            return true;
        }

        private static bool CanStoreInDrawer(ItemData item, Inventory destination)
        {
            // Makail persists only prefab + count. Match its native handling of spawned
            // items: picked-up/cheated flags are not retained by a drawer. Still refuse
            // custom data, quality, attribution and other meaningful item differences.
            var drop = item?.m_dropPrefab != null ? item.m_dropPrefab.GetComponent<ItemDrop>() : null;
            if (drop == null || destination.GetAllItems().Count != 1) return false;
            ItemData target = destination.GetAllItems()[0];
            ItemData canonical = drop.m_itemData;
            return target.m_dropPrefab == item.m_dropPrefab && item.m_quality == canonical.m_quality &&
                item.m_variant == canonical.m_variant && item.m_worldLevel == canonical.m_worldLevel &&
                item.m_crafterID == canonical.m_crafterID &&
                string.Equals(item.m_crafterName ?? "", canonical.m_crafterName ?? "", StringComparison.Ordinal) &&
                !item.m_equipped && !item.m_shared.m_questItem &&
                item.m_durability.Equals(canonical.m_durability) &&
                DictionaryEquals(item.m_customData, canonical.m_customData);
        }

        private static void AddAt(
            Inventory destination,
            ItemData template,
            int quantity,
            Vector2i slot)
        {
            ItemData clone = template.Clone();
            clone.m_stack = quantity;
            if (InventoryAddAtMethod == null ||
                !(bool)InventoryAddAtMethod.Invoke(
                    destination,
                    new object[] { clone, quantity, slot.x, slot.y, false }) ||
                clone.m_stack != 0)
                throw new InvalidOperationException("The destination changed during an exact move.");
        }

        private static bool CanMergeExact(ItemData left, ItemData right, bool ignorePickupState = false) =>
            left != null && right != null &&
            string.Equals(
                ValheimContainerIdentity.ResourceId(left),
                ValheimContainerIdentity.ResourceId(right),
                StringComparison.Ordinal) &&
            left.m_quality == right.m_quality && left.m_variant == right.m_variant &&
            left.m_worldLevel == right.m_worldLevel && left.m_crafterID == right.m_crafterID &&
            string.Equals(left.m_crafterName ?? string.Empty,
                right.m_crafterName ?? string.Empty, StringComparison.Ordinal) &&
            (ignorePickupState || left.m_pickedUp == right.m_pickedUp) && left.m_equipped == right.m_equipped &&
            left.m_cheated == right.m_cheated &&
            left.m_durability.Equals(right.m_durability) &&
            DictionaryEquals(left.m_customData, right.m_customData);

        private static bool DictionaryEquals(
            IDictionary<string, string> left,
            IDictionary<string, string> right)
        {
            if ((left?.Count ?? 0) != (right?.Count ?? 0)) return false;
            if (left == null || left.Count == 0) return true;
            foreach (KeyValuePair<string, string> pair in left)
                if (right == null || !right.TryGetValue(pair.Key, out string value) ||
                    !string.Equals(pair.Value, value, StringComparison.Ordinal)) return false;
            return true;
        }

        private static int CompareItemSlots(ItemData left, ItemData right)
        {
            int y = left.m_gridPos.y.CompareTo(right.m_gridPos.y);
            return y != 0 ? y : left.m_gridPos.x.CompareTo(right.m_gridPos.x);
        }

        private static bool StillOwned(Container container) =>
            container == null || container.isActiveAndEnabled && container.IsOwner();

        private static int AddSaturated(int left, int right) =>
            left > int.MaxValue - right ? int.MaxValue : left + right;
    }
}

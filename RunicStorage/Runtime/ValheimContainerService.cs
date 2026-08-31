using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using ItemData = ItemDrop.ItemData;

namespace RunicStorage.Runtime
{
    internal static class ValheimContainerService
    {
        private static readonly MethodInfo CheckAccessMethod =
            AccessTools.Method(typeof(Container), "CheckAccess");
        private static readonly MethodInfo InventoryChangedMethod =
            AccessTools.Method(typeof(Inventory), "Changed");
        private static readonly MethodInfo InventoryAddAtMethod = AccessTools.Method(
            typeof(Inventory),
            "AddItem",
            new[] { typeof(ItemData), typeof(int), typeof(int), typeof(int) });

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
            if (requireWritable && !container.IsOwner()) return false;
            if (requireWritable && !allowCurrentUse &&
                (container.IsInUse() || container.m_wagon != null && container.m_wagon.InUse()))
                return false;
            try
            {
                return CheckAccessMethod != null &&
                       (bool)CheckAccessMethod.Invoke(container, new object[] { playerId });
            }
            catch { return false; }
        }

        internal static int MoveUpTo(
            Inventory source,
            Inventory destination,
            ItemData sourceItem,
            int maximumQuantity,
            Player playerInventoryOwner = null,
            StorageMutationLease playerMutationLease = null,
            Container sourceContainer = null,
            Container destinationContainer = null)
        {
            if (source == null || destination == null || sourceItem == null ||
                ReferenceEquals(source, destination) || maximumQuantity <= 0 ||
                sourceItem.m_stack <= 0) return 0;

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
            if (!StillOwned(sourceContainer) || !StillOwned(destinationContainer)) return 0;

            int requested = Math.Min(maximumQuantity, sourceItem.m_stack);
            int maximumStack = Math.Max(1, sourceItem.m_shared?.m_maxStackSize ?? 1);
            int capacity = 0;
            foreach (ItemData item in destination.GetAllItems())
                if (CanMergeExact(sourceItem, item) && item.m_stack < maximumStack)
                    capacity = AddSaturated(capacity, maximumStack - item.m_stack);
            for (int y = 0; y < destination.GetHeight(); y++)
            for (int x = 0; x < destination.GetWidth(); x++)
                if (destination.GetItemAt(x, y) == null)
                    capacity = AddSaturated(capacity, maximumStack);

            int quantity = Math.Min(requested, capacity);
            if (quantity <= 0 || !CanRoundTrip(source) || !CanRoundTrip(destination)) return 0;
            ZPackage sourceBefore = SaveInventory(source);
            ZPackage destinationBefore = SaveInventory(destination);
            try
            {
                Inventory sourceShadow = CloneInventory(source);
                Inventory destinationShadow = CloneInventory(destination);
                ItemData shadowItem = sourceShadow.GetItemAt(
                    sourceItem.m_gridPos.x, sourceItem.m_gridPos.y);
                if (shadowItem == null)
                    throw new InvalidOperationException("The source stack changed before publication.");
                ApplyExactMove(sourceShadow, destinationShadow, shadowItem, quantity);
                string expectedSource = SaveInventory(sourceShadow).GetBase64();
                string expectedDestination = SaveInventory(destinationShadow).GetBase64();

                ApplyExactMove(source, destination, sourceItem, quantity);
                if (!StillOwned(sourceContainer) || !StillOwned(destinationContainer) ||
                    !string.Equals(SaveInventory(source).GetBase64(), expectedSource, StringComparison.Ordinal) ||
                    !string.Equals(SaveInventory(destination).GetBase64(), expectedDestination, StringComparison.Ordinal))
                    throw new InvalidOperationException("An endpoint changed during Storage publication.");
                return quantity;
            }
            catch (Exception failure)
            {
                var restoreFailures = new List<Exception> { failure };
                try { RestoreInventory(source, sourceBefore, playerInventoryOwner, playerMutationLease); }
                catch (Exception exception) { restoreFailures.Add(exception); }
                try { RestoreInventory(destination, destinationBefore, playerInventoryOwner, playerMutationLease); }
                catch (Exception exception) { restoreFailures.Add(exception); }
                if (restoreFailures.Count > 1)
                    throw new AggregateException(
                        "A Storage move failed and one or more exact rollbacks failed.",
                        restoreFailures);
                throw;
            }
        }

        internal static void RestoreInventory(
            Inventory inventory,
            ZPackage backup,
            Player inventoryOwner = null,
            StorageMutationLease playerMutationLease = null)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            if (backup == null) throw new ArgumentNullException(nameof(backup));
            if (inventoryOwner != null &&
                (playerMutationLease == null ||
                 !playerMutationLease.Covers(inventoryOwner, inventory)))
                throw new InvalidOperationException("The player restore lease is unavailable.");

            byte[] targetBytes = backup.GetArray();
            string target = Convert.ToBase64String(targetBytes);
            LoadExactShadow(inventory, targetBytes, target);
            byte[] originalBytes = SaveInventory(inventory).GetArray();
            string original = Convert.ToBase64String(originalBytes);
            LoadExactShadow(inventory, originalBytes, original);
            Action changed = inventory.m_onChanged;
            inventory.m_onChanged = null;
            try
            {
                inventory.Load(new ZPackage(targetBytes));
                if (!string.Equals(SaveInventory(inventory).GetBase64(), target, StringComparison.Ordinal))
                    throw new InvalidOperationException("The Storage snapshot did not round-trip.");
            }
            catch (Exception applyFailure)
            {
                try
                {
                    inventory.Load(new ZPackage(originalBytes));
                    if (!string.Equals(SaveInventory(inventory).GetBase64(), original, StringComparison.Ordinal))
                        throw new InvalidOperationException("The original Storage inventory did not restore.");
                }
                catch (Exception restoreFailure)
                {
                    throw new InvalidOperationException(
                        "Storage snapshot application and rollback both failed.",
                        new AggregateException(applyFailure, restoreFailure));
                }
                throw new InvalidOperationException(
                    "Storage snapshot application was rejected and restored.", applyFailure);
            }
            finally
            {
                inventory.m_onChanged = changed;
            }
            InventoryChangedMethod?.Invoke(inventory, Array.Empty<object>());
            if (!string.Equals(SaveInventory(inventory).GetBase64(), target, StringComparison.Ordinal))
                throw new InvalidOperationException("The restored Storage inventory changed during publication.");
        }

        internal static Inventory CloneInventory(Inventory source)
        {
            byte[] bytes = SaveInventory(source).GetArray();
            return LoadExactShadow(source, bytes, Convert.ToBase64String(bytes));
        }

        internal static Inventory LoadExactShadow(
            Inventory shape,
            byte[] payload,
            string expected)
        {
            if (shape == null || payload == null || payload.Length == 0 ||
                string.IsNullOrEmpty(expected))
                throw new InvalidOperationException("An exact Storage snapshot is unavailable.");
            var shadow = new Inventory(
                shape.GetName(), null, shape.GetWidth(), shape.GetHeight());
            shadow.Load(new ZPackage(payload));
            if (!string.Equals(SaveInventory(shadow).GetBase64(), expected, StringComparison.Ordinal))
                throw new InvalidOperationException("A Storage snapshot is not an exact round trip.");
            return shadow;
        }

        internal static ZPackage SaveInventory(Inventory source)
        {
            var package = new ZPackage();
            source.Save(package);
            return package;
        }

        internal static bool CanRoundTrip(Inventory inventory)
        {
            try { CloneInventory(inventory); return true; }
            catch { return false; }
        }

        private static void ApplyExactMove(
            Inventory source,
            Inventory destination,
            ItemData sourceItem,
            int quantity)
        {
            if (source.GetItemAt(sourceItem.m_gridPos.x, sourceItem.m_gridPos.y) != sourceItem ||
                sourceItem.m_stack < quantity)
                throw new InvalidOperationException("The source changed during an exact move.");
            int maximumStack = Math.Max(1, sourceItem.m_shared?.m_maxStackSize ?? 1);
            var mergeTargets = new List<ItemData>();
            foreach (ItemData item in destination.GetAllItems())
                if (CanMergeExact(sourceItem, item) && item.m_stack < maximumStack)
                    mergeTargets.Add(item);
            mergeTargets.Sort(CompareItemSlots);

            int remaining = quantity;
            foreach (ItemData target in mergeTargets)
            {
                if (remaining == 0) break;
                int moved = Math.Min(remaining, maximumStack - target.m_stack);
                AddAt(destination, sourceItem, moved, target.m_gridPos);
                remaining -= moved;
            }
            for (int y = 0; y < destination.GetHeight() && remaining > 0; y++)
            for (int x = 0; x < destination.GetWidth() && remaining > 0; x++)
            {
                if (destination.GetItemAt(x, y) != null) continue;
                int moved = Math.Min(remaining, maximumStack);
                AddAt(destination, sourceItem, moved, new Vector2i(x, y));
                remaining -= moved;
            }
            if (remaining != 0 || !source.RemoveItem(sourceItem, quantity))
                throw new InvalidOperationException("The source changed during an exact move.");
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
                    new object[] { clone, quantity, slot.x, slot.y }) ||
                clone.m_stack != 0)
                throw new InvalidOperationException("The destination changed during an exact move.");
        }

        private static bool CanMergeExact(ItemData left, ItemData right) =>
            left != null && right != null &&
            string.Equals(
                ValheimContainerIdentity.ResourceId(left),
                ValheimContainerIdentity.ResourceId(right),
                StringComparison.Ordinal) &&
            left.m_quality == right.m_quality && left.m_variant == right.m_variant &&
            left.m_worldLevel == right.m_worldLevel && left.m_crafterID == right.m_crafterID &&
            string.Equals(left.m_crafterName ?? string.Empty,
                right.m_crafterName ?? string.Empty, StringComparison.Ordinal) &&
            left.m_pickedUp == right.m_pickedUp && left.m_equipped == right.m_equipped &&
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

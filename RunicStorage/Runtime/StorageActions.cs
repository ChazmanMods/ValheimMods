using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using RunicStorage.Engine;
using UnityEngine;
using ItemData = ItemDrop.ItemData;

namespace RunicStorage.Runtime;

internal sealed class StorageActions
{
	private static readonly FieldInfo CurrentContainerField = AccessTools.Field(typeof(InventoryGui), "m_currentContainer");

	private static readonly MethodInfo InventoryChangedMethod = AccessTools.Method(typeof(Inventory), "Changed", (Type[])null, (Type[])null);

	private readonly ContainerIndex _index;
	private readonly StorageSearchPanel _searchPanel;

	internal StorageActions(ContainerIndex index, StorageSearchPanel searchPanel)
	{
		_index = index ?? throw new ArgumentNullException(nameof(index));
		_searchPanel = searchPanel ?? throw new ArgumentNullException(nameof(searchPanel));
	}

	internal void QuickStack()
	{
        if (!TryBeginMutation("Quick Stack", out var player, out var mutationLease))
		{
			return;
		}
		using (mutationLease)
		{
			Inventory inventory = ((Humanoid)player).GetInventory();
			IReadOnlyList<Container> readOnlyList = Nearby(player, requireWritable: true, "quick-stack");
			int num = 0;
			int num2 = 0;
			int num3 = 0;
			int num4 = 0;
			int num5 = 0;
			int num6 = 0;
			int num7 = 0;
			int num8 = 0;
			Dictionary<Container, Inventory> dictionary = new Dictionary<Container, Inventory>();
			HashSet<Container> hashSet = new HashSet<Container>();
			List<ItemData> list = new List<ItemData>(inventory.GetAllItems());
			list.Sort(CompareGridPosition);
			if (!TryCaptureProtection(list, out var snapshot, out var failureCode))
			{
				Message(player, "Runic Storage: carried-item protection could not be proven; Quick Stack changed nothing.");
				LogAction("quick-stack", failureCode, $"stacks={list.Count} moved=0");
				return;
			}
			int num9 = 0;
			for (int i = 0; i < list.Count; i++)
			{
				ItemData val = list[i];
				StorageProtectionState storageProtectionState = snapshot.StateAt(i);
				if (IsProtected(player, val, storageProtectionState))
				{
					num4++;
					if (storageProtectionState == StorageProtectionState.Locked)
					{
						num9++;
					}
					if (val != null && (val.m_equipped || ((Humanoid)player).IsItemEquiped(val)))
					{
						num6++;
					}
					else if (val != null && PluginConfig.ProtectHotbar.Value && val.m_gridPos.y == 0)
					{
						num5++;
					}
					continue;
				}
				string text = ValheimContainerIdentity.ResourceId(val);
				if (text.Length == 0)
				{
					continue;
				}
				num3++;
				int stack = val.m_stack;
				bool flag = false;
				foreach (Container item in readOnlyList)
				{
					if (val.m_stack <= 0)
					{
						break;
					}
					if (!dictionary.TryGetValue(item, out var value))
					{
						if (hashSet.Contains(item))
						{
							continue;
						}
						if (!StorageContainerAuthority.TryGetSynchronizedServerInventory(item, out value))
						{
							hashSet.Add(item);
							num8++;
                            LogTransfer(PlayerEndpointId(player), ValheimContainerIdentity.EndpointId(item), text, 0, "ownership.denied");
							continue;
						}
						dictionary[item] = value;
					}
					if (ContainsResource(value, text))
					{
						if (!flag)
						{
							num2 = AddSaturated(num2, stack);
							num7++;
							flag = true;
						}
						int stack2 = val.m_stack;
						int num10 = ValheimContainerService.MoveUpTo(inventory, value, val, stack2, player, mutationLease, null, item);
						num = AddSaturated(num, num10);
                        LogTransfer(PlayerEndpointId(player), ValheimContainerIdentity.EndpointId(item), text, num10, (num10 > 0) ? "ok" : "destination.full");
						if (num10 >= stack2)
						{
							break;
						}
					}
				}
			}
			QuickStackNoOpReason reason = QuickStackDiagnostics.Classify(new QuickStackObservation(list.Count, num3, num4, readOnlyList.Count, num7, num));
			int num11 = Math.Max(0, num2 - num);
			string text2 = ((num > 0) ? $"Runic Storage: moved {num} item(s); {num11} matching item(s) remained." : QuickStackNoOpFeedback(reason, num5, num6, num9));
			Message(player, text2);
			LogAction("quick-stack", (num > 0) ? "ok" : QuickStackDiagnostics.ReasonCode(reason), $"carriedStacks={list.Count} eligibleStacks={num3} protectedStacks={num4} " + $"hotbarProtected={num5} equippedProtected={num6} itemLockProtected={num9} " + $"authorizedContainers={readOnlyList.Count} matchedStacks={num7} " + $"ownershipDenied={num8} moved={num} remainder={num11}");
		}
	}

	internal void StoreAllOpenedContainer()
	{
        if (!TryBeginMutation("Store All", out var player, out var mutationLease))
		{
			return;
		}
		using (mutationLease)
		{
			Container container = ((Object)(object)InventoryGui.instance == (Object)null || CurrentContainerField == null) ? null : CurrentContainerField.GetValue(InventoryGui.instance) as Container;
			if ((Object)(object)container == (Object)null)
			{
				Message(player, "Runic Storage: open a container before using Store All.");
				LogAction("store-all-opened-container", "ui.open-container-required", "container=false moved=0");
				return;
			}
			if ((int)container.m_privacy == 0 || !ValheimContainerService.CanDiscover(container, player.GetPlayerID(), requireWritable: true, allowCurrentUse: true))
			{
				Message(player, "Runic Storage: that container is personal, busy, or denied; Store All changed nothing.");
				LogAction("store-all-opened-container", "container.denied", "authorized=false moved=0");
				return;
			}
			if (!StorageContainerAuthority.TryGetExactOpenedLocalOwnerInventory(container, player, out var destination))
			{
				Message(player, "Runic Storage: ownership changed before Store All; nothing was changed.");
				LogAction("store-all-opened-container", "ownership.denied", "owner=false moved=0");
				return;
			}

			Inventory inventory = ((Humanoid)player).GetInventory();
			List<ItemData> items = new List<ItemData>(inventory.GetAllItems());
			items.Sort(CompareGridPosition);
			if (!TryCaptureProtection(items, out var protection, out var failureCode))
			{
				Message(player, "Runic Storage: carried-item protection could not be proven; Store All changed nothing.");
				LogAction("store-all-opened-container", failureCode, $"stacks={items.Count} moved=0");
				return;
			}

			int moved = 0;
			int eligible = 0;
			int protectedStacks = 0;
			int questStacks = 0;
			for (int i = 0; i < items.Count; i++)
			{
				ItemData item = items[i];
				if (item == null || item.m_stack <= 0)
				{
					continue;
				}
				if (item.m_shared != null && item.m_shared.m_questItem)
				{
					questStacks++;
					continue;
				}
				if (IsProtected(player, item, protection.StateAt(i)))
				{
					protectedStacks++;
					continue;
				}
				eligible = AddSaturated(eligible, item.m_stack);
				int requested = item.m_stack;
				int transferred = ValheimContainerService.MoveUpTo(inventory, destination, item, requested, player, mutationLease, null, container);
				moved = AddSaturated(moved, transferred);
                LogTransfer(PlayerEndpointId(player), ValheimContainerIdentity.EndpointId(container), ValheimContainerIdentity.ResourceId(item), transferred, (transferred > 0) ? "ok" : "destination.full");
			}

			int remainder = Math.Max(0, eligible - moved);
			string result;
			string feedback;
			if (moved > 0)
			{
				result = (remainder == 0) ? "ok" : "partial";
				feedback = (remainder == 0)
					? $"Runic Storage: stored {moved} item(s); protected, hotbar, equipped, and quest items were kept."
					: $"Runic Storage: stored {moved} item(s); {remainder} eligible item(s) could not fit.";
			}
			else if (items.Count == 0)
			{
				result = "inventory.empty";
				feedback = "Runic Storage: your carried inventory is empty.";
			}
			else if (eligible == 0)
			{
				result = "inventory.all-protected";
				feedback = "Runic Storage: every carried stack is protected, equipped, on the protected hotbar, or a quest item.";
			}
			else
			{
				result = "destination.full";
				feedback = "Runic Storage: the opened container has no room for eligible carried items.";
			}
			Message(player, feedback);
			LogAction("store-all-opened-container", result, $"stacks={items.Count} eligibleQuantity={eligible} protectedStacks={protectedStacks} questStacks={questStacks} moved={moved} remainder={remainder}");
		}
	}

	internal void Restock()
	{
        if (!TryBeginMutation("Restock", out var player, out var mutationLease))
		{
			return;
		}
		using (mutationLease)
		{
			Dictionary<string, int> dictionary = ParseTargets(PluginConfig.RestockTargets.Value);
			if (dictionary.Count == 0)
			{
				Message(player, "Runic Storage: Restock.Targets has no valid Item=Amount entries.");
				LogAction("restock", "config.targets-invalid", "targets=0");
				return;
			}
			Inventory inventory = ((Humanoid)player).GetInventory();
			List<ItemData> list = new List<ItemData>(inventory.GetAllItems());
			list.Sort(CompareGridPosition);
			if (!TryCaptureProtection(list, out var snapshot, out var failureCode))
			{
				Message(player, "Runic Storage: carried-item protection could not be proven; Restock changed nothing.");
				LogAction("restock", failureCode, $"stacks={list.Count} moved=0");
				return;
			}
			int num = 0;
			foreach (KeyValuePair<string, int> item in dictionary)
			{
				num = AddSaturated(num, Math.Max(0, item.Value - CountMatching(inventory, item.Key)));
			}
			if (num == 0)
			{
				Message(player, "Runic Storage: every configured restock target is already met.");
				LogAction("restock", "targets.already-met", $"targets={dictionary.Count} requested=0");
				return;
			}
			foreach (KeyValuePair<string, int> item2 in dictionary)
			{
				if (Math.Max(0, item2.Value - CountMatching(inventory, item2.Key)) > 0 && HasProtectedPartialMatch(player, list, in snapshot, item2.Key))
				{
					Message(player, "Runic Storage: Restock would modify a protected partial target stack; nothing was changed.");
					LogAction("restock", "protection.target-partial", "target=" + SafeLogValue(item2.Key) + " moved=0");
					return;
				}
			}
			IReadOnlyList<Container> readOnlyList = Nearby(
				player,
				requireWritable: true,
				"restock",
				allowCurrentUse: true);
			if (readOnlyList.Count == 0)
			{
				Message(player, "Runic Storage: no authorized public container is available in range for restocking.");
				LogAction("restock", "discovery.none-authorized", $"targets={dictionary.Count} requested={num}");
				return;
			}
			int num2 = 0;
			int num3 = 0;
			int num4 = 0;
			int num5 = 0;
			Dictionary<Container, Inventory> dictionary2 = new Dictionary<Container, Inventory>();
			HashSet<Container> hashSet = new HashSet<Container>();
			foreach (KeyValuePair<string, int> item3 in dictionary)
			{
				int num6 = CountMatching(inventory, item3.Key);
				int num7 = Math.Max(0, item3.Value - num6);
				foreach (Container item4 in readOnlyList)
				{
					if (num7 == 0)
					{
						break;
					}
					if (!dictionary2.TryGetValue(item4, out var value))
					{
						if (hashSet.Contains(item4))
						{
							continue;
						}
						if (!TryGetWritableInventory(item4, player, out value))
						{
							hashSet.Add(item4);
							num3++;
							continue;
						}
						dictionary2[item4] = value;
					}
					foreach (ItemData item5 in new List<ItemData>(value.GetAllItems()))
					{
						if (num7 == 0)
						{
							break;
						}
						if (Matches(item5, item3.Key))
						{
							num4++;
							int maximumQuantity = Math.Min(num7, item5.m_stack);
							int num8 = ValheimContainerService.MoveUpTo(value, inventory, item5, maximumQuantity, player, mutationLease, item4);
							num7 -= num8;
							num2 = AddSaturated(num2, num8);
                            LogTransfer(ValheimContainerIdentity.EndpointId(item4), PlayerEndpointId(player), item3.Key, num8, (num8 > 0) ? "ok" : "player.full");
							if (num8 == 0)
							{
								break;
							}
						}
					}
				}
				num5 = AddSaturated(num5, num7);
			}
			string result;
			string text;
			if (num2 > 0)
			{
				result = ((num5 == 0) ? "ok" : "partial");
				text = ((num5 == 0) ? $"Runic Storage: restocked {num2} item(s); all configured targets are met." : $"Runic Storage: restocked {num2} item(s); {num5} target item(s) remain unavailable or could not fit.");
			}
			else if (num4 == 0)
			{
				result = "resource.targets-unavailable";
				text = "Runic Storage: the missing configured target items were not found in authorized nearby containers.";
			}
			else
			{
				result = "player.full-or-ownership-unavailable";
				text = "Runic Storage: matching items were found, but your inventory was full or container ownership changed.";
			}
			Message(player, text);
			LogAction("restock", result, $"targets={dictionary.Count} requested={num} sources={readOnlyList.Count} " + $"matchingStacks={num4} ownershipDenied={num3} moved={num2} unmet={num5}");
		}
	}

	internal void Search()
	{
		Player player = Player.m_localPlayer;
		if (player == null)
		{
			LogAction("search", "player.unavailable", "player=false");
			return;
		}
		IReadOnlyList<Container> containers = Nearby(
			player,
			requireWritable: false,
			"search",
			allowCurrentUse: true);
		if (containers.Count == 0)
		{
			Message(player, "Runic Storage: no authorized public container is visible in the configured range.");
			LogAction("search", "discovery.none-authorized", "containers=0");
			return;
		}

		var catalog = new Dictionary<string, SearchAccumulator>(StringComparer.Ordinal);
		int unsynchronized = 0;
		int stacksExamined = 0;
		for (int containerIndex = 0; containerIndex < containers.Count; containerIndex++)
		{
			Container container = containers[containerIndex];
			if (!TryGetSearchInventory(container, player, out Inventory inventory))
			{
				unsynchronized++;
				continue;
			}
			List<ItemData> items = inventory.GetAllItems();
			for (int itemIndex = 0; itemIndex < items.Count && stacksExamined < 8192; itemIndex++)
			{
				stacksExamined++;
				ItemData item = items[itemIndex];
				string resource = ValheimContainerIdentity.ResourceId(item);
				if (resource.Length == 0 || item == null || item.m_stack <= 0) continue;
				if (!catalog.TryGetValue(resource, out SearchAccumulator entry))
				{
					if (catalog.Count >= StorageSearchPanel.EntryLimit) continue;
					entry = new SearchAccumulator(resource, FriendlyItemName(item));
					catalog.Add(resource, entry);
				}
				entry.Quantity = AddSaturated(entry.Quantity, item.m_stack);
				entry.AddContainer(container);
			}
		}

		var entries = new List<StorageSearchEntry>(catalog.Count);
		foreach (SearchAccumulator value in catalog.Values) entries.Add(value.ToEntry());
		entries.Sort((left, right) =>
		{
			int display = StringComparer.OrdinalIgnoreCase.Compare(left.DisplayName, right.DisplayName);
			return display != 0
				? display
				: StringComparer.Ordinal.Compare(left.ResourceId, right.ResourceId);
		});
		if (entries.Count == 0)
		{
			Message(player, "Runic Storage: the nearby synchronized containers are empty.");
			LogAction("search", "inventory.empty", $"containers={containers.Count} unsynchronized={unsynchronized}");
			return;
		}
		_searchPanel.Open(entries);
		Message(player, "Runic Storage: choose an item from the nearby-chest list.");
		LogAction("search", "ok", $"containers={containers.Count} unsynchronized={unsynchronized} kinds={entries.Count} stacks={stacksExamined}");
	}

	internal void SortOpenedContainer()
	{
		//IL_0083: Unknown result type (might be due to invalid IL or missing references)
		//IL_021a: Unknown result type (might be due to invalid IL or missing references)
		//IL_02f6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0319: Unknown result type (might be due to invalid IL or missing references)
		//IL_0335: Unknown result type (might be due to invalid IL or missing references)
		//IL_033a: Unknown result type (might be due to invalid IL or missing references)
        if (!TryBeginMutation("Sort", out var player, out var mutationLease))
		{
			return;
		}
		using (mutationLease)
		{
			Container val = ((Object)(object)InventoryGui.instance == (Object)null || CurrentContainerField == null) ? null : CurrentContainerField.GetValue(InventoryGui.instance) as Container;
			if ((Object)(object)val == (Object)null)
			{
				Message(player, "Runic Storage: open a container before sorting.");
				LogAction("sort-opened-container", "ui.open-container-required", "container=false");
				return;
			}
			if ((int)val.m_privacy == 0 || !ValheimContainerService.CanDiscover(val, player.GetPlayerID(), requireWritable: true, allowCurrentUse: true))
			{
				Message(player, "Runic Storage: that container is personal, busy, or denied.");
				LogAction("sort-opened-container", "container.denied", "authorized=false");
				return;
			}
			if (!StorageContainerAuthority.TryGetExactOpenedServerOwnerInventory(val, player, out var inventory))
			{
				Message(player, "Runic Storage: ownership changed before the sort; nothing was changed.");
				LogAction("sort-opened-container", "ownership.denied", "owner=false");
				return;
			}
			if (!ValheimContainerService.CanRoundTrip(inventory))
			{
				Message(player, "Runic Storage: this container contains an item that cannot be restored exactly under the current item definitions; sort was safely skipped.");
				LogAction("sort-opened-container", "inventory.not-roundtrippable", "changed=false");
				return;
			}
			ZPackage val2 = ValheimContainerService.SaveInventory(inventory);
			HashSet<string> hashSet = ParseLockedSlots(PluginConfig.LockedContainerSlots.Value, inventory.GetWidth(), inventory.GetHeight());
			List<ItemData> list = new List<ItemData>();
			List<Vector2i> list2 = new List<Vector2i>();
			List<ItemData> list3 = new List<ItemData>(inventory.GetAllItems());
			if (list3.Count == 0)
			{
				Message(player, "Runic Storage: the opened container is already empty.");
				LogAction("sort-opened-container", "inventory.empty", $"lockedSlots={hashSet.Count}");
				return;
			}
			foreach (ItemData item in list3)
			{
				if (!hashSet.Contains(SlotKey(item.m_gridPos.x, item.m_gridPos.y)))
				{
					list.Add(item);
				}
			}
			for (int i = 0; i < inventory.GetHeight(); i++)
			{
				for (int j = 0; j < inventory.GetWidth(); j++)
				{
					if (!hashSet.Contains(SlotKey(j, i)))
					{
						list2.Add(new Vector2i(j, i));
					}
				}
			}
			if (list.Count == 0)
			{
				Message(player, "Runic Storage: every occupied slot is locked; nothing was moved.");
				LogAction("sort-opened-container", "inventory.all-locked", $"stacks={list3.Count} lockedSlots={hashSet.Count}");
				return;
			}
			bool flag = false;
			try
			{
                Inventory obj = ValheimContainerService.CloneInventory(inventory);
                ApplySortExact(obj, hashSet);
                string @base = ValheimContainerService.SaveInventory(obj).GetBase64();
				list.Sort(CompareItems);
				for (int k = 0; k < list.Count && k < list2.Count; k++)
				{
					if (list[k].m_gridPos.x != list2[k].x || list[k].m_gridPos.y != list2[k].y)
					{
						flag = true;
					}
					list[k].m_gridPos = list2[k];
				}
				if (flag)
				{
					InventoryChangedMethod.Invoke(inventory, null);
				}
				if (!string.Equals(ValheimContainerService.SaveInventory(inventory).GetBase64(), @base, StringComparison.Ordinal))
				{
					throw new InvalidOperationException("The opened container changed during nonthrowing sort publication.");
				}
			}
			catch (Exception ex)
			{
				try
				{
					ValheimContainerService.RestoreInventory(inventory, val2);
				}
                catch (Exception ex2)
                {
                    throw new AggregateException(
                        "The opened-container sort failed and its exact rollback failed.", ex, ex2);
				}
				throw;
			}
			Message(player, flag ? $"Runic Storage: sorted {list.Count} stack(s); {hashSet.Count} slot(s) locked." : $"Runic Storage: {list.Count} movable stack(s) were already sorted; nothing changed.");
			LogAction("sort-opened-container", flag ? "ok" : "inventory.already-sorted", $"stacks={list3.Count} movable={list.Count} lockedSlots={hashSet.Count} changed={flag}");
		}
	}

	internal void ConsolidateCarriedStacks()
	{
        if (!TryBeginMutation("Consolidate", out var player, out var mutationLease))
		{
			return;
		}
		using (mutationLease)
		{
			Inventory inventory = ((Humanoid)player).GetInventory();
			List<ItemData> list = new List<ItemData>(inventory.GetAllItems());
			list.Sort(CompareGridPosition);
			if (!TryCaptureProtection(list, out var snapshot, out var failureCode))
			{
				Message(player, "Runic Storage: carried-item protection could not be proven; consolidation changed nothing.");
				LogAction("consolidate", failureCode, $"stacks={list.Count} changed=false");
				return;
			}
			if (!ValheimContainerService.CanRoundTrip(inventory))
			{
				Message(player, "Runic Storage: your inventory contains an item that cannot be restored exactly under the current item definitions; consolidation was safely skipped.");
				LogAction("consolidate", "inventory.not-roundtrippable", "changed=false");
				return;
			}
			ZPackage backup = ValheimContainerService.SaveInventory(inventory);
			if (list.Count == 0)
			{
				Message(player, "Runic Storage: your carried inventory is empty.");
				LogAction("consolidate", "inventory.empty", "stacks=0");
				return;
			}
			int num = 0;
			for (int i = 0; i < list.Count; i++)
			{
				if (IsProtected(player, list[i], snapshot.StateAt(i)))
				{
					num++;
				}
			}
			if (num == list.Count)
			{
				Message(player, "Runic Storage: every carried stack is equipped or in a protected hotbar slot.");
				LogAction("consolidate", "inventory.all-protected", $"stacks={list.Count} protectedStacks={num}");
				return;
			}
			int num2 = 0;
			try
			{
				Inventory obj = ValheimContainerService.CloneInventory(inventory);
				int num3 = ApplyConsolidation(obj, player, in snapshot);
				string @base = ValheimContainerService.SaveInventory(obj).GetBase64();
				num2 = ApplyConsolidation(inventory, player, in snapshot);
				if (num2 != num3)
				{
					throw new InvalidOperationException("The carried inventory changed after consolidation preflight.");
				}
				if (num2 > 0)
				{
					InventoryChangedMethod.Invoke(inventory, null);
				}
				if (!string.Equals(ValheimContainerService.SaveInventory(inventory).GetBase64(), @base, StringComparison.Ordinal))
				{
					throw new InvalidOperationException("The carried inventory changed during nonthrowing consolidation publication.");
				}
			}
			catch (Exception ex)
			{
				try
				{
					ValheimContainerService.RestoreInventory(inventory, backup, player, mutationLease);
				}
				catch (Exception ex2)
				{
                    throw new AggregateException(
                        "Carried-stack consolidation and its exact rollback both failed.", ex, ex2);
				}
				throw;
			}
			Message(player, (num2 > 0) ? $"Runic Storage: consolidated {num2} item(s) into compatible stacks." : "Runic Storage: no compatible partial backpack stacks needed consolidation; protected/equipped or metadata-different stacks were left alone.");
			LogAction("consolidate", (num2 > 0) ? "ok" : "inventory.no-compatible-stacks", $"stacks={list.Count} protectedStacks={num} moved={num2}");
		}
	}

    private static int ApplyConsolidation(Inventory inventory, Player player, in StorageProtectionSnapshot protection)
	{
		List<ItemData> list = new List<ItemData>(inventory.GetAllItems());
		list.Sort(CompareGridPosition);
		if (protection.Count != 0 && list.Count != protection.Count)
		{
			throw new InvalidOperationException("The carried inventory shape changed after protection capture.");
		}
		int num = 0;
		for (int i = 0; i < list.Count; i++)
		{
			ItemData val = list[i];
			if (val == null || val.m_stack <= 0 || IsProtected(player, val, protection.StateAt(i)))
			{
				continue;
			}
			int num2 = ((val.m_shared == null) ? val.m_stack : val.m_shared.m_maxStackSize);
			for (int j = i + 1; j < list.Count; j++)
			{
				if (val.m_stack >= num2)
				{
					break;
				}
				ItemData val2 = list[j];
				if (val2 != null && val2.m_stack > 0 && !IsProtected(player, val2, protection.StateAt(j)) && CanMerge(val, val2))
				{
					int num3 = Math.Min(num2 - val.m_stack, val2.m_stack);
					val.m_stack += num3;
					val2.m_stack -= num3;
					num = AddSaturated(num, num3);
					if (val2.m_stack == 0 && !inventory.RemoveItem(val2))
					{
						throw new InvalidOperationException("A consolidation source changed during mutation.");
					}
				}
			}
		}
        return num;
    }

    private static bool ApplySortExact(Inventory inventory, HashSet<string> lockedSlots)
    {
        var items = new List<ItemData>();
        var slots = new List<Vector2i>();
        foreach (ItemData item in inventory.GetAllItems())
            if (!lockedSlots.Contains(SlotKey(item.m_gridPos.x, item.m_gridPos.y)))
                items.Add(item);
        for (int y = 0; y < inventory.GetHeight(); y++)
        for (int x = 0; x < inventory.GetWidth(); x++)
            if (!lockedSlots.Contains(SlotKey(x, y))) slots.Add(new Vector2i(x, y));
        items.Sort(CompareItems);
        bool changed = false;
        for (int index = 0; index < items.Count && index < slots.Count; index++)
        {
            changed |= items[index].m_gridPos.x != slots[index].x ||
                       items[index].m_gridPos.y != slots[index].y;
            items[index].m_gridPos = slots[index];
        }
        return changed;
    }

	private IReadOnlyList<Container> Nearby(
		Player player,
		bool requireWritable,
		string action,
		bool allowCurrentUse = false)
	{
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_006d: Unknown result type (might be due to invalid IL or missing references)
		float num = Mathf.Clamp(PluginConfig.RangeMeters.Value, 1f, 50f);
		int num2 = Mathf.Clamp(PluginConfig.MaximumCandidates.Value, 1, 256);
		// The initial plugin scan can precede world synchronization. Reconcile loaded
		// containers at the action boundary so the very first Quick Stack/Restock/Search
		// sees chests that Valheim has finished creating without requiring the player to
		// open a chest first.
		int refreshed = _index.RefreshLoadedContainers();
		bool truncated;
		IReadOnlyList<Container> readOnlyList = _index.Nearest(((Component)player).transform.position, num, num2, out truncated);
		List<Container> list = new List<Container>();
		int num3 = 0;
		int num4 = 0;
		foreach (Container item in readOnlyList)
		{
			if ((int)item.m_privacy == 0)
			{
				num3++;
			}
			else if (!ValheimContainerService.CanDiscover(
				item,
				player.GetPlayerID(),
				requireWritable,
				allowCurrentUse))
			{
				num4++;
			}
			else
			{
				list.Add(item);
			}
		}
		if (PluginConfig.DebugTransfers.Value)
		{
			ManualLogSource log = Plugin.Log;
			if (log != null)
			{
				log.LogInfo((object)($"action={action} phase=discovery rangeMeters={num:0.##} maximumCandidates={num2} " + $"loadedRefresh={refreshed} indexed={readOnlyList.Count} authorized={list.Count} personalExcluded={num3} " + $"deniedOrBusy={num4} truncated={truncated}"));
			}
			if (truncated)
			{
				ManualLogSource log2 = Plugin.Log;
				if (log2 != null)
				{
					log2.LogWarning((object)("action=" + action + " result=discovery.truncated remedy=increase-MaximumCandidates-or-reduce-range"));
				}
			}
		}
		return list.AsReadOnly();
	}

	private static bool TryGetWritableInventory(
		Container container,
		Player player,
		out Inventory inventory)
	{
		if (InventoryGui.instance != null && CurrentContainerField != null &&
			ReferenceEquals(CurrentContainerField.GetValue(InventoryGui.instance), container) &&
			StorageContainerAuthority.TryGetExactOpenedLocalOwnerInventory(
				container,
				player,
				out inventory))
			return true;
		return StorageContainerAuthority.TryGetSynchronizedServerInventory(
			container,
			out inventory);
	}

	private static bool TryGetSearchInventory(
		Container container,
		Player player,
		out Inventory inventory)
	{
		if (InventoryGui.instance != null && CurrentContainerField != null &&
			ReferenceEquals(CurrentContainerField.GetValue(InventoryGui.instance), container) &&
			StorageContainerAuthority.TryGetExactOpenedLocalOwnerInventory(
				container,
				player,
				out inventory))
			return true;
		return ContainerHoverContents.TryGetSynchronizedReadSnapshot(
			container,
			out inventory,
			out _);
	}

	private static string FriendlyItemName(ItemData item)
	{
		string value = item?.m_shared?.m_name ?? ValheimContainerIdentity.ResourceId(item);
		try
		{
			if (Localization.instance != null && !string.IsNullOrEmpty(value))
				value = Localization.instance.Localize(value);
		}
		catch { }
		value = (value ?? string.Empty).Replace("<", string.Empty).Replace(">", string.Empty)
			.Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
		if (value.Length == 0) value = ValheimContainerIdentity.ResourceId(item);
		return value.Length <= 64 ? value : value.Substring(0, 64);
	}

	private sealed class SearchAccumulator
	{
		private readonly List<Container> _containers = new List<Container>();

		internal SearchAccumulator(string resourceId, string displayName)
		{
			ResourceId = resourceId;
			DisplayName = displayName;
		}

		internal string ResourceId { get; }
		internal string DisplayName { get; }
		internal int Quantity { get; set; }

		internal void AddContainer(Container container)
		{
			if (container != null && !_containers.Contains(container)) _containers.Add(container);
		}

		internal StorageSearchEntry ToEntry() =>
			new StorageSearchEntry(ResourceId, DisplayName, Quantity, _containers.AsReadOnly());
	}

	private static bool TryBeginMutation(
		string action,
		out Player player,
		out StorageMutationLease mutationLease)
	{
		mutationLease = null;
		if (!TryResolveOwnedLocalPlayer(action, out player)) return false;
		if (StorageMutationLease.TryBegin(player, out mutationLease)) return true;
		Message(player, "Runic Storage: another Storage action is already in progress.");
		Plugin.Log?.LogWarning(action + " denied with storage.operation-active.");
		return false;
	}

	private static bool TryResolveOwnedLocalPlayer(string action, out Player player)
	{
		player = Player.m_localPlayer;
		if ((Object)(object)player == (Object)null)
		{
			return false;
		}
		if (!PluginConfig.Enabled.Value)
		{
			Message(player, "Runic Storage is disabled in configuration.");
			return false;
		}
		if ((Object)(object)player == (Object)(object)Player.m_localPlayer && ((Character)player).IsOwner())
		{
			return true;
		}
		Message(player, "Runic Storage: " + action + " requires the owning local player; no carried items were changed.");
		ManualLogSource log = Plugin.Log;
		if (log != null)
		{
			log.LogWarning((object)(action + " denied with authority.local-player-owner-required."));
		}
		return false;
	}

	private static bool IsProtected(Player player, ItemData item, StorageProtectionState typedState = StorageProtectionState.NotApplicable)
	{
		if (item != null && !item.m_equipped && !((Humanoid)player).IsItemEquiped(item) && (!PluginConfig.ProtectHotbar.Value || item.m_gridPos.y != 0))
		{
			return typedState == StorageProtectionState.Locked;
		}
		return true;
	}

	private static bool TryCaptureProtection<T>(IReadOnlyList<T> items, out StorageProtectionSnapshot snapshot, out string failureCode) where T : class
	{
		return StorageItemProtection.TryCapture(items, out snapshot, out failureCode);
	}

	private static bool HasProtectedPartialMatch(Player player, IReadOnlyList<ItemData> items, in StorageProtectionSnapshot protection, string query)
	{
		for (int i = 0; i < items.Count; i++)
		{
			ItemData val = items[i];
			if (Matches(val, query) && IsProtected(player, val, protection.StateAt(i)))
			{
				int num = val?.m_shared?.m_maxStackSize ?? val?.m_stack ?? 0;
				if (val != null && val.m_stack > 0 && val.m_stack < num)
				{
					return true;
				}
			}
		}
		return false;
	}

	private static bool ContainsResource(Inventory inventory, string resourceId)
	{
		foreach (ItemData allItem in inventory.GetAllItems())
		{
			if (string.Equals(ValheimContainerIdentity.ResourceId(allItem), resourceId, StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}

	private static int CountMatching(Inventory inventory, string query)
	{
		int num = 0;
		foreach (ItemData allItem in inventory.GetAllItems())
		{
			if (Matches(allItem, query))
			{
				num = AddSaturated(num, Math.Max(0, allItem.m_stack));
			}
		}
		return num;
	}

	private static bool Matches(ItemData item, string query)
	{
		if (item == null)
		{
			return false;
		}
		if (string.Equals(ValheimContainerIdentity.ResourceId(item), query, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		if (string.Equals((item.m_shared?.m_name ?? string.Empty).TrimStart('$'), query.TrimStart('$'), StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		return false;
	}

	private static bool CanMerge(ItemData left, ItemData right)
	{
		if (left.m_shared != right.m_shared || !string.Equals(ValheimContainerIdentity.ResourceId(left), ValheimContainerIdentity.ResourceId(right), StringComparison.Ordinal) || left.m_quality != right.m_quality || left.m_variant != right.m_variant || left.m_worldLevel != right.m_worldLevel || left.m_crafterID != right.m_crafterID || !string.Equals(left.m_crafterName ?? string.Empty, right.m_crafterName ?? string.Empty, StringComparison.Ordinal) || left.m_pickedUp != right.m_pickedUp || left.m_equipped != right.m_equipped || !left.m_durability.Equals(right.m_durability))
		{
			return false;
		}
		return DictionaryEquals(left.m_customData, right.m_customData);
	}

	private static bool DictionaryEquals(IDictionary<string, string> left, IDictionary<string, string> right)
	{
		int num = left?.Count ?? 0;
		if (num != (right?.Count ?? 0))
		{
			return false;
		}
		if (num == 0)
		{
			return true;
		}
		foreach (KeyValuePair<string, string> item in left)
		{
			if (!right.TryGetValue(item.Key, out var value) || !string.Equals(item.Value, value, StringComparison.Ordinal))
			{
				return false;
			}
		}
		return true;
	}

	private static int CompareItems(ItemData left, ItemData right)
	{
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		int num = ((left.m_shared == null) ? int.MaxValue : ((int)left.m_shared.m_itemType));
		int value = ((right.m_shared == null) ? int.MaxValue : ((int)right.m_shared.m_itemType));
		int num2 = num.CompareTo(value);
		if (num2 != 0)
		{
			return num2;
		}
		string x = left.m_shared?.m_name ?? ValheimContainerIdentity.ResourceId(left);
		string y = right.m_shared?.m_name ?? ValheimContainerIdentity.ResourceId(right);
		int num3 = StringComparer.OrdinalIgnoreCase.Compare(x, y);
		if (num3 != 0)
		{
			return num3;
		}
		int num4 = right.m_quality.CompareTo(left.m_quality);
		if (num4 != 0)
		{
			return num4;
		}
		float num5 = ((left.m_shared == null) ? 0f : left.GetWeight(left.m_stack));
		float value2 = ((right.m_shared == null) ? 0f : right.GetWeight(right.m_stack));
		return num5.CompareTo(value2);
	}

	private static int CompareGridPosition(ItemData left, ItemData right)
	{
		int num = left.m_gridPos.y.CompareTo(right.m_gridPos.y);
		if (num == 0)
		{
			return left.m_gridPos.x.CompareTo(right.m_gridPos.x);
		}
		return num;
	}

	private static Dictionary<string, int> ParseTargets(string value)
	{
		Dictionary<string, int> dictionary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		string[] array = (value ?? string.Empty).Split(',');
		for (int i = 0; i < array.Length; i++)
		{
			string[] array2 = array[i].Split('=');
			if (array2.Length == 2 && int.TryParse(array2[1].Trim(), out var result) && result >= 0)
			{
				string text = array2[0].Trim();
				if (text.Length != 0)
				{
					dictionary[text] = result;
				}
			}
		}
		return dictionary;
	}

	private static HashSet<string> ParseLockedSlots(string value, int width, int height)
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		string[] array = (value ?? string.Empty).Split(';');
		for (int i = 0; i < array.Length; i++)
		{
			string[] array2 = array[i].Split(',');
			if (array2.Length == 2 && int.TryParse(array2[0], out var result) && int.TryParse(array2[1], out var result2) && result >= 0 && result < width && result2 >= 0 && result2 < height)
			{
				hashSet.Add(SlotKey(result, result2));
			}
		}
		return hashSet;
	}

	private static string SlotKey(int x, int y)
	{
		return x + "," + y;
	}

	private static string CompassDirection(Vector3 delta)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		float num = Mathf.Atan2(delta.x, delta.z) * 57.29578f;
		if (num < 0f)
		{
			num += 360f;
		}
		string[] array = new string[8] { "north", "northeast", "east", "southeast", "south", "southwest", "west", "northwest" };
		return array[Mathf.RoundToInt(num / 45f) % array.Length];
	}

	private static void Message(Player player, string text)
	{
		if (player != null)
		{
			((Character)player).Message((MessageType)2, text, 0, (Sprite)null);
		}
	}

	private static int AddSaturated(int left, int right)
	{
		if (left <= int.MaxValue - right)
		{
			return left + right;
		}
		return int.MaxValue;
	}

	private static string PlayerEndpointId(Player player) =>
		"valheim.player:" + player.GetPlayerID().ToString(
			System.Globalization.CultureInfo.InvariantCulture);

	private static void LogTransfer(string source, string destination, string resource, int quantity, string result)
	{
		if (PluginConfig.DebugTransfers.Value)
		{
			ManualLogSource log = Plugin.Log;
			if (log != null)
			{
				log.LogInfo((object)$"transfer source={source} destination={destination} resource={resource} quantity={quantity} result={result}");
			}
		}
	}

	private static string QuickStackNoOpFeedback(QuickStackNoOpReason reason, int hotbarProtected, int equippedProtected, int itemLockProtected)
	{
		return reason switch
		{
			QuickStackNoOpReason.InventoryEmpty => "Runic Storage: your carried inventory is empty.",
			QuickStackNoOpReason.AllStacksProtected => $"Runic Storage: no backpack stack is eligible; {hotbarProtected} hotbar, {equippedProtected} equipped, and {itemLockProtected} typed-lock stack(s) are protected.",
			QuickStackNoOpReason.NoAuthorizedContainers => $"Runic Storage: no authorized public container is available within {Mathf.Clamp(PluginConfig.RangeMeters.Value, 1f, 50f):0.#} m.",
			QuickStackNoOpReason.NoMatchingResources => "Runic Storage: no eligible backpack item matches an item already stored in an authorized nearby container.",
			_ => "Runic Storage: matching containers were full or ownership changed; nothing moved.",
		};
	}

	private static void LogAction(string action, string result, string details)
	{
		if (PluginConfig.DebugTransfers.Value)
		{
			ManualLogSource log = Plugin.Log;
			if (log != null)
			{
				log.LogInfo((object)("action=" + action + " result=" + result + " " + details));
			}
		}
	}

	private static string SafeLogValue(string value)
	{
		string text = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
		if (text.Length > 64)
		{
			text = text.Substring(0, 64);
		}
		return text.Replace(' ', '_');
	}
}

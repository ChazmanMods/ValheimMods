using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;
using RunicStorage.Engine;
using UnityEngine;

namespace RunicStorage.Runtime;

using ButtonDef = ZInput.ButtonDef;

internal sealed class StorageInputReader
{
	private string _loggedControllerSignature = string.Empty;

	internal StorageActionEdges ReadKeyboardEdges()
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		//IL_005b: Unknown result type (might be due to invalid IL or missing references)
		StorageActionEdges storageActionEdges = StorageActionEdges.None;
		if (ShortcutDown(PluginConfig.SortOpenedContainerKey.Value))
		{
			storageActionEdges |= StorageActionEdges.KeyboardSort;
		}
		if (ShortcutDown(PluginConfig.StoreAllOpenedContainerKey.Value))
		{
			storageActionEdges |= StorageActionEdges.KeyboardStoreAll;
		}
		if (ShortcutDown(PluginConfig.QuickStackKey.Value))
		{
			storageActionEdges |= StorageActionEdges.KeyboardQuickStack;
		}
		if (ShortcutDown(PluginConfig.RestockKey.Value))
		{
			storageActionEdges |= StorageActionEdges.KeyboardRestock;
		}
		if (ShortcutDown(PluginConfig.ConsolidateKey.Value))
		{
			storageActionEdges |= StorageActionEdges.KeyboardConsolidate;
		}
		if (ShortcutDown(PluginConfig.SearchKey.Value))
		{
			storageActionEdges |= StorageActionEdges.KeyboardSearch;
		}
		return storageActionEdges;
	}

	internal StorageActionEdges ReadControllerEdges(StorageRouteContext context)
	{
		if (!PluginConfig.ControllerShortcuts.Value || ZInput.instance == null)
		{
			return StorageActionEdges.None;
		}
		StorageActionEdges storageActionEdges = StorageControllerCollisionGuard.ConsumePendingEdge();
		if (storageActionEdges != StorageActionEdges.None)
		{
			return storageActionEdges;
		}
		if (_loggedControllerSignature.Length != 0)
		{
			string text = (PluginConfig.ControllerModifier.Value ?? string.Empty).Trim();
			ButtonDef val = ((text.Length == 0) ? null : ZInput.instance.GetButtonDef(text));
			if (val == null || !val.Held)
			{
				return StorageActionEdges.None;
			}
		}
		ControllerBindingState bindings = StorageControllerBindings.Resolve();
		LogControllerStatus(bindings);
		return StorageControllerCollisionGuard.ObserveAndConsume(bindings, context);
	}

	internal void InvalidateControllerBindings()
	{
		_loggedControllerSignature = string.Empty;
		StorageControllerBindings.Invalidate();
	}

	internal string ControllerStatusSummary()
	{
		if (!PluginConfig.ControllerShortcuts.Value)
		{
			return "disabled";
		}
		if (ZInput.instance == null)
		{
			return "waiting-for-zinput";
		}
		ControllerBindingState controllerBindingState = StorageControllerBindings.Resolve();
		if (!controllerBindingState.ModifierValid)
		{
			return "invalid-modifier";
		}
		return controllerBindingState.ValidRouteCount + "/5-routes-valid";
	}

	internal static string DescribeEdges(StorageActionEdges edges)
	{
		if (edges == StorageActionEdges.None)
		{
			return "none";
		}
		List<string> values = new List<string>();
		Add(values, edges, StorageActionEdges.KeyboardSort, "keyboard:sort-opened-container");
		Add(values, edges, StorageActionEdges.KeyboardStoreAll, "keyboard:store-all-opened-container");
		Add(values, edges, StorageActionEdges.KeyboardQuickStack, "keyboard:quick-stack");
		Add(values, edges, StorageActionEdges.KeyboardRestock, "keyboard:restock");
		Add(values, edges, StorageActionEdges.KeyboardConsolidate, "keyboard:consolidate");
		Add(values, edges, StorageActionEdges.KeyboardSearch, "keyboard:search");
		Add(values, edges, StorageActionEdges.ControllerSort, "controller:sort-opened-container");
		Add(values, edges, StorageActionEdges.ControllerQuickStack, "controller:quick-stack");
		Add(values, edges, StorageActionEdges.ControllerRestock, "controller:restock");
		Add(values, edges, StorageActionEdges.ControllerConsolidate, "controller:consolidate");
		Add(values, edges, StorageActionEdges.ControllerSearch, "controller:search");
		return string.Join(",", values);
	}

	private void LogControllerStatus(ControllerBindingState bindings)
	{
		if (string.Equals(_loggedControllerSignature, bindings.Signature, StringComparison.Ordinal))
		{
			return;
		}
		_loggedControllerSignature = bindings.Signature;
		if (!bindings.ModifierValid)
		{
			ManualLogSource log = Plugin.Log;
			if (log != null)
			{
				log.LogWarning((object)("Controller shortcuts are unavailable: ModifierAction '" + bindings.ModifierName + "' is not an existing Joy* ZInput action."));
			}
			return;
		}
		LogRouteStatus("QuickStack", bindings.QuickStackName, bindings.QuickStackValid);
		LogRouteStatus("Restock", bindings.RestockName, bindings.RestockValid);
		LogRouteStatus("SortOpenedContainer", bindings.SortName, bindings.SortValid);
		LogRouteStatus("Consolidate", bindings.ConsolidateName, bindings.ConsolidateValid);
		LogRouteStatus("Search", bindings.SearchName, bindings.SearchValid);
		ManualLogSource log2 = Plugin.Log;
		if (log2 != null)
		{
			log2.LogInfo((object)($"Controller controls (validation={bindings.ValidRouteCount}/5-routes-valid): hold {bindings.ModifierName}; " + "QuickStack=" + bindings.QuickStackName + ", Restock=" + bindings.RestockName + ", SortOpenedContainer=" + bindings.SortName + ", Consolidate=" + bindings.ConsolidateName + ", Search=" + bindings.SearchName + ". An authorized chord owns its modifier plus all configured Storage primary paths until full release."));
		}
	}

	private static void LogRouteStatus(string route, string action, bool valid)
	{
		if (!valid)
		{
			ManualLogSource log = Plugin.Log;
			if (log != null)
			{
				log.LogWarning((object)("Controller route " + route + " is disabled: '" + action + "' is missing, non-gamepad, duplicates the modifier, or duplicates an earlier route."));
			}
		}
	}

	private static bool ShortcutDown(KeyboardShortcut shortcut)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		if ((int)(shortcut).MainKey == 0 || !ZInput.GetKeyDown((shortcut).MainKey, false))
		{
			return false;
		}
		foreach (KeyCode modifier in (shortcut).Modifiers)
		{
			if ((int)modifier == 0 || !ZInput.GetKey(modifier, false))
			{
				return false;
			}
		}
		return true;
	}

	private static void Add(ICollection<string> values, StorageActionEdges edges, StorageActionEdges expected, string label)
	{
		if ((edges & expected) != StorageActionEdges.None)
		{
			values.Add(label);
		}
	}
}

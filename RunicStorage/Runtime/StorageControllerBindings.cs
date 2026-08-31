using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using RunicStorage.Engine;
using ButtonDef = ZInput.ButtonDef;

namespace RunicStorage.Runtime;

internal static class StorageControllerBindings
{
	private static bool _hasCachedState;
	private static ControllerBindingCacheKey _cachedKey;
	private static ControllerBindingState _cachedState;

	internal static ControllerBindingState Resolve()
	{
		string modifier = Name(PluginConfig.ControllerModifier);
		string quickStack = Name(PluginConfig.ControllerQuickStack);
		string restock = Name(PluginConfig.ControllerRestock);
		string sort = Name(PluginConfig.ControllerSort);
		string consolidate = Name(PluginConfig.ControllerConsolidate);
		string search = Name(PluginConfig.ControllerSearch);
		ControllerBindingCacheKey key = new ControllerBindingCacheKey(ZInput.instance, modifier, quickStack, restock, sort, consolidate, search);
		if (_hasCachedState && _cachedKey.Equals(key))
		{
			return _cachedState;
		}

		ButtonDef modifierDef = ResolveGamepad(modifier);
		HashSet<string> usedNames = new HashSet<string>(StringComparer.Ordinal);
		HashSet<string> usedPaths = new HashSet<string>(StringComparer.Ordinal);
		if (modifierDef != null && !Register(modifier, modifierDef, usedNames, usedPaths))
		{
			modifierDef = null;
		}
		ButtonDef quickStackDef = ResolveUnique(quickStack, usedNames, usedPaths);
		ButtonDef restockDef = ResolveUnique(restock, usedNames, usedPaths);
		ButtonDef sortDef = ResolveUnique(sort, usedNames, usedPaths);
		ButtonDef consolidateDef = ResolveUnique(consolidate, usedNames, usedPaths);
		ButtonDef searchDef = ResolveUnique(search, usedNames, usedPaths);
		ControllerBindingState state = new ControllerBindingState(
			string.Join("|", modifier, Path(modifierDef), quickStack, Path(quickStackDef), restock, Path(restockDef), sort, Path(sortDef), consolidate, Path(consolidateDef), search, Path(searchDef)),
			modifier, quickStack, restock, sort, consolidate, search,
			modifierDef, quickStackDef, restockDef, sortDef, consolidateDef, searchDef);
		_cachedKey = key;
		_cachedState = state;
		_hasCachedState = true;
		return state;
	}

	internal static void Invalidate()
	{
		_hasCachedState = false;
		_cachedKey = default;
		_cachedState = null;
	}

	private static ButtonDef ResolveUnique(string action, ISet<string> usedNames, ISet<string> usedPaths)
	{
		ButtonDef result = ResolveGamepad(action);
		return result == null || !Register(action, result, usedNames, usedPaths) ? null : result;
	}

	private static ButtonDef ResolveGamepad(string action)
	{
		if (action.Length == 0 || action.Length > 64 || !action.StartsWith("Joy", StringComparison.Ordinal) || ZInput.instance == null)
		{
			return null;
		}
		ButtonDef definition = ZInput.instance.GetButtonDef(action);
		return definition == null || definition.Source != ZInput.InputSource.Gamepad || string.IsNullOrEmpty(definition.GetActionPath(true)) ? null : definition;
	}

	private static bool Register(string action, ButtonDef definition, ISet<string> usedNames, ISet<string> usedPaths)
	{
		string path = Path(definition);
		if (path.Length == 0 || usedNames.Contains(action) || usedPaths.Contains(path))
		{
			return false;
		}
		usedNames.Add(action);
		usedPaths.Add(path);
		return true;
	}

	private static string Path(ButtonDef definition) => definition?.GetActionPath(true) ?? string.Empty;
	private static string Name(ConfigEntry<string> entry) => (entry?.Value ?? string.Empty).Trim();
}

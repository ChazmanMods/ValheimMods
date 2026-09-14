using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Runic.Foundation.Core;
using RunicStorage.Engine;
using RunicStorage.Runtime;
using UnityEngine;
using ItemData = ItemDrop.ItemData;

namespace RunicStorage;

[BepInPlugin("chazman.RunicStorage", "Runic Storage", "1.2.4")]
public sealed class Plugin : BaseUnityPlugin
{
	public const string Guid = "chazman.RunicStorage";

	public const string Name = "Runic Storage";

	public const string Version = "1.2.4";

	private readonly List<KeybindingRegistration> _keybindingRegistrations = new List<KeybindingRegistration>();

	private readonly KeybindingConflictRegistry _keybindings = new KeybindingConflictRegistry();

	private static readonly FieldInfo DragItemField = AccessTools.Field(typeof(InventoryGui), "m_dragItem");

	private static readonly FieldInfo CurrentContainerField = AccessTools.Field(typeof(InventoryGui), "m_currentContainer");

	private readonly Harmony _harmony = new Harmony("chazman.RunicStorage");

	private readonly StorageInputReader _inputReader = new StorageInputReader();

	private StorageActions _actions;
	private StorageSearchPanel _searchPanel;
	private ChestRulesPanel _rulesPanel;
	private static ChestRulesPanel ActiveRulesPanel;

	private bool _readyMessageShown;

	internal static ContainerIndex Index { get; private set; }

	internal static ManualLogSource Log { get; private set; }

	private static StorageSearchPanel ActiveSearchPanel { get; set; }

	internal static bool SearchPanelOpen => (ActiveSearchPanel?.IsOpen ?? false) || (ActiveRulesPanel?.IsOpen ?? false);
	internal static bool RulesPanelOpen => ActiveRulesPanel?.IsOpen ?? false;

	private void Awake()
	{
		Log = Logger;
		PluginConfig.Bind(Config);
		ChestGroupRuntime.Bind(Config);
		try
		{
			Config.SettingChanged += OnSettingChanged;
			ZInput.OnInputLayoutChanged += OnInputLayoutChanged;
			Localization.OnLanguageChange = (Action)Delegate.Combine(Localization.OnLanguageChange, new Action(ContainerHoverContents.InvalidateConfiguration));
			Index = new ContainerIndex();
			_searchPanel = new StorageSearchPanel();
			_rulesPanel = new ChestRulesPanel();
			ActiveRulesPanel = _rulesPanel;
			ActiveSearchPanel = _searchPanel;
			Runic.Shared.ModalGameplayInput.IsOpen = () => SearchPanelOpen;
			_actions = new StorageActions(Index, _searchPanel);
			RegisterKeybindings();
			_harmony.PatchAll(typeof(Plugin).Assembly);
			Index.RefreshLoadedContainers();
			Logger.LogInfo((object)"Runic Storage v1.2.4 ready: chest hover, Quick Stack, Restock, Search, Sort, Store All, and Consolidate use guarded native ownership.");
			if (!ContainerHoverContents.IsSupported)
			{
				Logger.LogWarning((object)"Chest-content hover is unavailable because its installed Container/PrivateArea adapter signatures did not match. Other Storage features remain enabled.");
			}
			LogConfigurationSummary("startup");
		}
		catch (Exception arg)
		{
			Shutdown();
			Logger.LogError((object)string.Format("{0} failed closed during startup: {1}", "Runic Storage", arg));
		}
	}

	private void Update()
	{
		if (_actions == null)
		{
			return;
		}
		try
		{
			ShowReadyMessage();
			_searchPanel?.Tick();
			_rulesPanel?.Tick();
			if (SearchPanelOpen) return;
			StorageRouteContext context = CaptureRouteContext();
			_actions.TickDeferredActions(context);
			StorageActionEdges num = _inputReader.ReadKeyboardEdges();
			StorageActionEdges storageActionEdges = _inputReader.ReadControllerEdges(context);
			StorageActionEdges edges = num | storageActionEdges;
			StorageActionRequest request = StorageActionRouter.Select(edges);
			if (!request.IsPresent)
			{
				return;
			}
			StorageRouteDecision storageRouteDecision = StorageActionRouter.Route(request, context);
			if (PluginConfig.DebugTransfers.Value)
			{
				Logger.LogInfo((object)("input-detected edges=" + StorageInputReader.DescribeEdges(edges) + " selected=" + StorageActionDiagnostics.ActionCode(request.Action) + " source=" + StorageActionDiagnostics.OriginCode(request.Origin) + " route=" + storageRouteDecision.Outcome.ToString().ToLowerInvariant() + " reason=" + StorageActionDiagnostics.RouteReasonCode(storageRouteDecision.Reason)));
			}
			if (storageRouteDecision.Outcome != StorageRouteOutcome.Execute)
			{
				string text = StorageActionDiagnostics.RouteFeedback(storageRouteDecision.Reason);
				if (text.Length != 0)
				{
					Player localPlayer = Player.m_localPlayer;
					if (localPlayer != null)
					{
						((Character)localPlayer).Message((MessageType)2, text, 0, (Sprite)null);
					}
				}
				return;
			}
			switch (request.Action)
			{
			case StorageActionKind.QuickStack:
				_actions.QuickStack();
				break;
			case StorageActionKind.StoreAllOpenedContainer:
				_actions.StoreAllOpenedContainer();
				break;
			case StorageActionKind.Restock:
				_actions.Restock();
				break;
			case StorageActionKind.SortOpenedContainer:
				_actions.SortOpenedContainer();
				break;
			case StorageActionKind.Consolidate:
				_actions.ConsolidateCarriedStacks();
				break;
			case StorageActionKind.Search:
				_actions.Search();
				break;
			}
		}
		catch (Exception ex)
		{
			Logger.LogError((object)("Runic Storage action stopped after restoring the in-flight item move; earlier completed moves, if any, remain applied: " + ex));
			Player localPlayer2 = Player.m_localPlayer;
			if (localPlayer2 != null)
			{
				((Character)localPlayer2).Message((MessageType)2, "Runic Storage stopped safely; completed moves may remain. Check BepInEx/LogOutput.log.", 0, (Sprite)null);
			}
		}
	}

	private void OnGUI()
	{
		try { _searchPanel?.Draw(); _rulesPanel?.Draw(); }
		catch (Exception ex)
		{
			Logger.LogWarning((object)("Storage search list closed after a UI error: " + ex.Message));
			_searchPanel?.Close();
			_rulesPanel?.Close();
		}
	}

	private void OnDestroy()
	{
		Shutdown();
	}

	private void RegisterKeybindings()
	{
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0064: Unknown result type (might be due to invalid IL or missing references)
		//IL_007e: Unknown result type (might be due to invalid IL or missing references)
		DisposeKeybindings();
		RegisterKey("quick-stack", "Quick Stack", PluginConfig.QuickStackKey.Value);
		RegisterKey("restock", "Restock", PluginConfig.RestockKey.Value);
		RegisterKey("sort", "Sort Opened Container", PluginConfig.SortOpenedContainerKey.Value);
		RegisterKey("store-all", "Store All Into Opened Container", PluginConfig.StoreAllOpenedContainerKey.Value);
		RegisterKey("consolidate", "Consolidate Carried Stacks", PluginConfig.ConsolidateKey.Value);
		RegisterKey("search", "Search Nearby Storage", PluginConfig.SearchKey.Value);
		if (PluginConfig.ControllerShortcuts.Value)
		{
			RegisterControllerKey("controller-quick-stack", "Quick Stack (Controller)", PluginConfig.ControllerQuickStack.Value);
			RegisterControllerKey("controller-restock", "Restock (Controller)", PluginConfig.ControllerRestock.Value);
			RegisterControllerKey("controller-sort", "Sort Opened Container (Controller)", PluginConfig.ControllerSort.Value);
			RegisterControllerKey("controller-consolidate", "Consolidate (Controller)", PluginConfig.ControllerConsolidate.Value);
			RegisterControllerKey("controller-search", "Search (Controller)", PluginConfig.ControllerSearch.Value);
		}
	}

	private void RegisterKey(string bindingId, string displayName, KeyboardShortcut shortcut)
	{
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_005c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0061: Unknown result type (might be due to invalid IL or missing references)
		List<string> list = new List<string>();
		foreach (KeyCode modifier in (shortcut).Modifiers)
		{
			list.Add(((object)modifier/*cast due to .constrained prefix*/).ToString());
		}
		_keybindingRegistrations.Add(_keybindings.Register(new KeybindingDescriptor("runic.storage", bindingId, displayName, new InputChord("keyboard", ((object)(shortcut).MainKey/*cast due to .constrained prefix*/).ToString(), list))));
	}

	private void RegisterControllerKey(string bindingId, string displayName, string action)
	{
		string text = (action ?? string.Empty).Trim();
		string text2 = (PluginConfig.ControllerModifier.Value ?? string.Empty).Trim();
		if (text.Length == 0 || text2.Length == 0 || string.Equals(text, text2, StringComparison.Ordinal))
		{
			return;
		}
		try
		{
			_keybindingRegistrations.Add(_keybindings.Register(new KeybindingDescriptor("runic.storage", bindingId, displayName, new InputChord("controller", text, new string[1] { text2 }))));
		}
		catch (ArgumentException ex)
		{
			Logger.LogWarning((object)("Controller keybinding " + displayName + " was not registered: " + ex.Message));
		}
	}

	private void OnSettingChanged(object sender, SettingChangedEventArgs arguments)
	{
		ContainerHoverContents.InvalidateConfiguration();
		_inputReader.InvalidateControllerBindings();
		StorageControllerCollisionGuard.Reset();
		try
		{
			RegisterKeybindings();
			LogConfigurationSummary("configuration-changed");
		}
		catch (Exception ex)
		{
			Logger.LogWarning((object)("Storage keybinding refresh failed: " + ex.Message));
		}
	}

	private void OnInputLayoutChanged()
	{
		_inputReader.InvalidateControllerBindings();
		StorageControllerCollisionGuard.Reset();
		ConfigEntry<bool> debugTransfers = PluginConfig.DebugTransfers;
		if (debugTransfers != null && debugTransfers.Value)
		{
			Logger.LogInfo((object)"Controller input layout changed; Storage controller bindings will be re-resolved once.");
		}
	}

	private void DisposeKeybindings()
	{
		for (int num = _keybindingRegistrations.Count - 1; num >= 0; num--)
		{
			try
			{
				_keybindingRegistrations[num].Dispose();
			}
			catch (Exception ex)
			{
				Logger.LogWarning((object)("Could not unregister a storage keybinding cleanly: " + ex.Message));
			}
		}
		_keybindingRegistrations.Clear();
	}

	internal static StorageRouteContext CaptureRouteContext()
	{
		Player localPlayer = Player.m_localPlayer;
		bool flag = InventoryGui.IsVisible();
		bool owner = (Object)(object)localPlayer != (Object)null &&
			(Object)(object)localPlayer == (Object)(object)Player.m_localPlayer &&
			((Character)localPlayer).IsOwner();
		return new StorageRouteContext(
			PluginConfig.Enabled?.Value ?? false,
			(Object)(object)localPlayer == (Object)null || InputBlocked(),
			HasDraggedItem(), flag, flag && HasOpenedContainer(),
			StoreGui.IsVisible(), Minimap.IsOpen(), owner, owner);
	}

	private static bool InputBlocked()
	{
		if (Console.IsVisible() || Menu.IsVisible() || TextInput.IsVisible())
		{
			return true;
		}
		if ((Object)(object)Chat.instance != (Object)null && Chat.instance.HasFocus())
		{
			return true;
		}
		if ((Object)(object)TextViewer.instance != (Object)null && TextViewer.instance.IsVisible())
		{
			return true;
		}
		if (Hud.InRadial() || Hud.IsPieceSelectionVisible() || GameCamera.InFreeFly() || PlayerCustomizaton.IsBarberGuiVisible())
		{
			return true;
		}
		Player localPlayer = Player.m_localPlayer;
		if (!((Object)(object)localPlayer == (Object)null) && !((Character)localPlayer).IsDead() && !((Character)localPlayer).InCutscene())
		{
			return ((Character)localPlayer).IsTeleporting();
		}
		return true;
	}

	private static bool HasDraggedItem()
	{
		if ((Object)(object)InventoryGui.instance != (Object)null)
		{
			return DragItemField?.GetValue(InventoryGui.instance) is ItemData;
		}
		return false;
	}

	private static bool HasOpenedContainer()
	{
		if ((Object)(object)InventoryGui.instance != (Object)null)
		{
			return CurrentContainerField?.GetValue(InventoryGui.instance) is Container;
		}
		return false;
	}

	private void ShowReadyMessage()
	{
		//IL_008b: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bf: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f4: Unknown result type (might be due to invalid IL or missing references)
		if (!_readyMessageShown && PluginConfig.Enabled.Value && PluginConfig.ShowReadyMessage.Value && !((Object)(object)Player.m_localPlayer == (Object)null) && !((Object)(object)MessageHud.instance == (Object)null))
		{
			_readyMessageShown = true;
			string text = (PluginConfig.ControllerShortcuts.Value ? (" Controller: hold " + PluginConfig.ControllerModifier.Value + "; see config for routes.") : string.Empty);
			((Character)Player.m_localPlayer).Message((MessageType)1, "Runic Storage ready — " + ShortcutLabel(PluginConfig.QuickStackKey.Value) + " Quick Stack; " + ShortcutLabel(PluginConfig.RestockKey.Value) + " Restock; " + ShortcutLabel(PluginConfig.SearchKey.Value) + " Search; " + ShortcutLabel(PluginConfig.ConsolidateKey.Value) + " Consolidate; open a chest and use " + ShortcutLabel(PluginConfig.SortOpenedContainerKey.Value) + " to Sort or " + ShortcutLabel(PluginConfig.StoreAllOpenedContainerKey.Value) + " to Store All." + text, 0, (Sprite)null);
		}
	}

	private void LogConfigurationSummary(string reason)
	{
		//IL_01f1: Unknown result type (might be due to invalid IL or missing references)
		//IL_020d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0229: Unknown result type (might be due to invalid IL or missing references)
		//IL_0245: Unknown result type (might be due to invalid IL or missing references)
		//IL_0261: Unknown result type (might be due to invalid IL or missing references)
		string text = (PluginConfig.ControllerShortcuts.Value ? ("hold " + PluginConfig.ControllerModifier.Value + ": quick=" + PluginConfig.ControllerQuickStack.Value + ", restock=" + PluginConfig.ControllerRestock.Value + ", sort=" + PluginConfig.ControllerSort.Value + ", consolidate=" + PluginConfig.ControllerConsolidate.Value + ", search=" + PluginConfig.ControllerSearch.Value) : "disabled");
		Logger.LogInfo((object)($"Storage configuration ({reason}): Enabled={PluginConfig.Enabled.Value}; " + $"Range={PluginConfig.RangeMeters.Value:0.##}m; MaxCandidates={PluginConfig.MaximumCandidates.Value}; " + $"ProtectHotbar={PluginConfig.ProtectHotbar.Value}; DebugTransfers={PluginConfig.DebugTransfers.Value}; " + $"Hover=[enabled={PluginConfig.ShowContentsOnHover.Value}, kinds={PluginConfig.HoverMaximumItemKinds.Value}, " + $"perLine={PluginConfig.HoverItemsPerLine.Value}, characters={PluginConfig.HoverMaximumCharacters.Value}, " + $"stacks={PluginConfig.HoverMaximumStacksExamined.Value}, " + $"snapshotCharacters={PluginConfig.HoverMaximumSnapshotCharacters.Value}, " + string.Format("retry={0:0.##}s, signatures={1}]; ", PluginConfig.HoverRefreshIntervalSeconds.Value, ContainerHoverContents.IsSupported ? "ready" : "unavailable") + "keyboard=[quick " + ShortcutLabel(PluginConfig.QuickStackKey.Value) + ", restock " + ShortcutLabel(PluginConfig.RestockKey.Value) + ", sort " + ShortcutLabel(PluginConfig.SortOpenedContainerKey.Value) + ", store-all " + ShortcutLabel(PluginConfig.StoreAllOpenedContainerKey.Value) + ", consolidate " + ShortcutLabel(PluginConfig.ConsolidateKey.Value) + ", search " + ShortcutLabel(PluginConfig.SearchKey.Value) + "]; controller=[" + text + "]; controller-validation=" + _inputReader.ControllerStatusSummary() + "."));
	}

	private static string ShortcutLabel(KeyboardShortcut shortcut)
	{
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Unknown result type (might be due to invalid IL or missing references)
		List<string> list = new List<string>();
		foreach (KeyCode modifier in (shortcut).Modifiers)
		{
			list.Add(((object)modifier/*cast due to .constrained prefix*/).ToString());
		}
		list.Add(((object)(shortcut).MainKey/*cast due to .constrained prefix*/).ToString());
		return string.Join("+", list);
	}

	private void Shutdown()
	{
		Config.SettingChanged -= OnSettingChanged;
		ZInput.OnInputLayoutChanged -= OnInputLayoutChanged;
		Localization.OnLanguageChange = (Action)Delegate.Remove(Localization.OnLanguageChange, new Action(ContainerHoverContents.InvalidateConfiguration));
		_inputReader.InvalidateControllerBindings();
		StorageControllerCollisionGuard.Reset();
		StorageSearchGameplayInputGuard.Reset();
		ContainerHoverContents.Reset();
		try
		{
			_harmony.UnpatchSelf();
		}
		catch (Exception ex)
		{
			Logger.LogWarning((object)("Could not unpatch Runic Storage cleanly: " + ex.Message));
		}
		DisposeKeybindings();
		_actions = null;
		_rulesPanel?.Dispose();
		_rulesPanel = null;
		ActiveRulesPanel = null;
		foreach (var label in UnityEngine.Object.FindObjectsByType<ChestExteriorLabel>(FindObjectsSortMode.None)) UnityEngine.Object.Destroy(label);
		_searchPanel?.Dispose();
		_searchPanel = null;
		ActiveSearchPanel = null;
		Runic.Shared.ModalGameplayInput.Reset();
		Index = null;
		Log = null;
	}
}

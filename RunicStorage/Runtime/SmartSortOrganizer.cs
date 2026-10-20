using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using RunicStorage.Engine;
using UnityEngine;

namespace RunicStorage.Runtime;

internal sealed partial class SmartSortOrganizer
{
    internal static Func<bool> AuthorityEnabled = () => false;
    internal static Func<float> AuthorityRadius = () => 10f;
    private const string AutoRulesKey = "SmartSort.AutoRules.v1";
    private readonly ConfigEntry<KeyboardShortcut> _key;
    private readonly ConfigEntry<float> _range;
    private readonly ConfigEntry<bool> _enabled;
    private readonly List<Chest> _pending = new();
    private Player _actor;
    private float _expires;
    private int _skipped;
    private IEnumerator<int> _preview;
    private sealed class Chest
    {
        internal Container Container;
        internal string Rules, Label;
        internal ChestRules Policy;
        internal bool DestinationOnly;
        internal SmartSortTransaction.Endpoint Endpoint;
    }

    internal SmartSortOrganizer(ConfigFile config)
    {
        _enabled = config.Bind("SmartSort", "Enabled", true, "Enable nearby chest organization.");
        _key = config.Bind("SmartSort", "OrganizeKey", new KeyboardShortcut(KeyCode.O, KeyCode.LeftAlt), "Press once to preview, again within 30 seconds to apply. Close inventory first.");
        _range = config.Bind("SmartSort", "RangeMeters", 10f, new ConfigDescription("Organization radius; also capped by Storage RangeMeters.", new AcceptableValueRange<float>(2f, 30f)));
        AuthorityEnabled = () => _enabled.Value;
        AuthorityRadius = () => Mathf.Clamp(_range.Value, 2f, 30f);
    }

    internal bool Handle(StorageRouteContext context)
    {
        if (RunicAutomation.Operations.InventoryOperationNetwork.Ready) return AuthorityHandle(context);
        if (_preview != null)
        {
            if (!_enabled.Value || !context.Enabled || context.GameplayInputBlocked || context.InventoryOpen || _actor != Player.m_localPlayer || !_actor || _actor.IsDead() || Input.GetKeyDown(KeyCode.Escape))
            { _preview.Dispose(); _preview = null; _pending.Clear(); return true; }
            try
            {
                // Work remains on Unity's main thread, but no longer monopolizes one frame.
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                do { if (!_preview.MoveNext()) { _preview.Dispose(); _preview = null; break; } }
                while ((System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000d / System.Diagnostics.Stopwatch.Frequency < 2d);
            }
            catch (Exception ex) { _preview?.Dispose(); _preview = null; _pending.Clear(); Plugin.Log.LogError(ex); Say("SmartSort preview stopped: " + ex.Message); }
            return true;
        }
        if (_pending.Count > 0 && (Time.realtimeSinceStartup >= _expires || _actor != Player.m_localPlayer || Input.GetKeyDown(KeyCode.Escape))) _pending.Clear();
        if (!_enabled.Value || !_key.Value.IsDown()) return false;
        if (!context.Enabled || context.GameplayInputBlocked || context.DraggedItem || context.InventoryOpen ||
            context.StoreOpen || context.MapOpen || !context.LocalPlayerOwnershipAvailable || !Player.m_localPlayer || Player.m_localPlayer.IsDead())
        { _pending.Clear(); return true; }
        try
        {
            if (_pending.Count > 0 && _actor == Player.m_localPlayer && Time.realtimeSinceStartup < _expires) Apply();
            else { _actor = Player.m_localPlayer; _preview = Preview().GetEnumerator(); }
        }
        catch (Exception ex)
        {
            _pending.Clear();
            Plugin.Log.LogError("SmartSort: " + ex);
            Say(ex is RunicAutomation.MutationIndeterminateException
                ? "SmartSort stopped: recovery needs inspection. Check LogOutput.log and SmartSortBackups."
                : "SmartSort: " + ex.Message);
        }
        return true;
    }

    private float Range => Mathf.Min(Mathf.Clamp(_range.Value, 2f, 30f), Mathf.Clamp(PluginConfig.RangeMeters.Value, 1f, 50f));
    internal void Draw()
    {
        if (RunicAutomation.Operations.InventoryOperationNetwork.Ready) { DrawAuthority(); return; }
        if (_preview != null) { GUI.Box(new Rect(Screen.width - 330, 60, 310, 45), "SmartSort: preparing preview... Esc cancels"); return; }
        if (_pending.Count == 0 || _actor != Player.m_localPlayer || Time.realtimeSinceStartup >= _expires) return;
        var style = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true, richText = false };
        float width = Mathf.Min(440, Screen.width - 40), height = Mathf.Min(Screen.height - 80, 160 + Math.Min(10, _pending.Count) * 27);
        GUILayout.BeginArea(new Rect(Screen.width - width - 20, 60, width, height), GUI.skin.box);
        GUILayout.Label("SmartSort — proposed chest contents", style);
        GUILayout.Label($"{_pending.Count} chests within {Range:0}m; {_skipped} excluded", style);
        foreach (var c in _pending.Take(10)) GUILayout.Label($"{c.Label} — {c.Endpoint.Planned.GetAllItems().Count} stacks", style);
        if (_pending.Count > 10) GUILayout.Label($"+ {_pending.Count - 10} more (see LogOutput.log)", style);
        GUILayout.Label($"Press {_key.Value} to organize. Esc cancels.\nExpires in {Mathf.CeilToInt(_expires - Time.realtimeSinceStartup)}s.", style);
        GUILayout.EndArea();
    }
    private bool Accessible(Container c) => c && ChestRuleStore.Eligible(c) && (int)c.m_privacy != 0 &&
        Vector3.Distance(c.transform.position, Player.m_localPlayer.transform.position) <= Range &&
        !c.IsInUse() && ValheimContainerService.CanDiscover(c, Player.m_localPlayer.GetPlayerID(), false);

    private IEnumerable<int> Preview()
    {
        _pending.Clear(); _actor = Player.m_localPlayer; _skipped = 0;
        if (!string.IsNullOrWhiteSpace(PluginConfig.LockedContainerSlots.Value))
            throw new InvalidOperationException("SmartSort is unavailable while container LockedSlots are configured.");
        Plugin.Index.RefreshLoadedContainers(_actor.transform.position, Range, out _);
        var candidates = Plugin.Index.Nearest(_actor.transform.position, Range, 65, out bool truncated);
        if (truncated || candidates.Count > 64) throw new InvalidOperationException("More than 64 chests nearby. Reduce SmartSort RangeMeters.");
        foreach (var c in candidates.OrderBy(ValheimContainerIdentity.EndpointId, StringComparer.Ordinal))
        {
            yield return 0;
            if (!StorageMutationLease.TryBegin(_actor, out var lease)) throw new InvalidOperationException("Another inventory action is active; try again.");
            using (lease)
            {
            string raw = ChestRuleStore.Raw(c);
            var view = ValheimContainerIdentity.NetworkView(c);
            if (!Accessible(c) || !ChestRuleStore.Read(c, out var rules) ||
                !StorageContainerAuthority.TryGetSynchronizedServerInventory(c, out var inventory))
            { _skipped++; continue; }
            var items = inventory.GetAllItems();
            if (items.Count > 512 || !StorageItemProtection.TryCapture(items, out var protectedItems, out _) ||
                items.Where((item, index) => item.m_equipped || item.m_shared.m_questItem || protectedItems.StateAt(index) == StorageProtectionState.Locked).Any())
            { _skipped++; continue; }
            var authority = StorageContainerAuthority.CaptureAuthority(c);
            bool configured = SmartSortCategories.HasManualRules(rules) && (!view || view.GetZDO().GetString(AutoRulesKey, "") != raw);
            _pending.Add(new Chest { Container = c, Rules = raw, Policy = rules, DestinationOnly = configured,
                Endpoint = new SmartSortTransaction.Endpoint { Live = inventory, Before = new StorageInventorySnapshot(inventory),
                    Authorized = () => Player.m_localPlayer == _actor && Accessible(c) && authority() && ChestRuleStore.Raw(c) == raw,
                    Persisted = () => StorageInventoryPayloadComparison.MatchesLoaded(view.GetZDO().GetByteArray(ZDOVars.s_items), ValheimContainerService.SaveInventory(inventory).GetArray()) } });
            }
        }
        if (_pending.Count < 2) { _pending.Clear(); Say($"SmartSort needs at least two eligible chests. Skipped {_skipped}; busy/remote chests may need a second preview."); yield break; }
        if (_pending.Sum(c => c.Endpoint.Live.GetAllItems().Count) > 2048) throw new InvalidOperationException("Too many stacks. Reduce SmartSort RangeMeters.");
        var policies = _pending.Select(c => c.DestinationOnly ? new Func<ItemDrop.ItemData, int>(item =>
            c.Policy.RoutingScore(ChestGroupRuntime.Facts(item), c.Endpoint.Live.GetAllItems().Any(existing =>
                ValheimContainerIdentity.ResourceId(existing) == ValheimContainerIdentity.ResourceId(item)))) : null).ToArray();
        // Plan from immutable snapshots: the player may interact while preparation spans frames.
        var captured = new List<Inventory>();
        foreach (var chest in _pending) { captured.Add(chest.Endpoint.Before.CreateShadow()); yield return 0; }
        var categoryCache = new Dictionary<(string, object), string>();
        string CachedCategory(ItemDrop.ItemData item)
        {
            var id = (ValheimContainerIdentity.ResourceId(item), (object)item.m_shared);
            if (!categoryCache.TryGetValue(id, out string value)) categoryCache[id] = value = Category(item);
            return value;
        }
        List<Inventory> planned = null;
        foreach (var step in SmartSortPlanner.BuildSteps(captured, CachedCategory, policies, result => planned = result)) yield return step;
        foreach (var chest in _pending)
        {
            if (!chest.Endpoint.Authorized()) throw new InvalidOperationException("A chest changed while preparing. Preview again.");
            chest.Endpoint.Before.Verify(chest.Endpoint.Live);
            yield return 0;
        }
        for (int i = 0; i < _pending.Count; i++)
        {
            _pending[i].Endpoint.Planned = planned[i];
            _pending[i].Label = _pending[i].DestinationOnly
                ? (string.IsNullOrWhiteSpace(_pending[i].Policy.Label) ? ChestExteriorLabel.AutomaticText(_pending[i].Policy) : _pending[i].Policy.Label) + " [destination]"
                : Label(planned[i]);
            Plugin.Log.LogInfo($"SmartSort preview {ValheimContainerIdentity.EndpointId(_pending[i].Container)} => {_pending[i].Label} ({planned[i].GetAllItems().Count} stacks)");
            yield return 0;
        }
        _expires = Time.realtimeSinceStartup + 30f;
        int before = _pending.Sum(c => c.Endpoint.Live.GetAllItems().Count), after = planned.Sum(i => i.GetAllItems().Count);
        Say($"SmartSort preview: {_pending.Count} chests, {before} -> {after} stacks; {_skipped} skipped. {_key.Value} again within 30s to apply. Details in log.");
    }

    private void Apply()
    {
        var chests = _pending.ToArray(); _pending.Clear();
        if (!string.IsNullOrWhiteSpace(PluginConfig.LockedContainerSlots.Value)) throw new InvalidOperationException("LockedSlots changed; preview again.");
        if (!StorageMutationLease.TryBegin(_actor, out var lease)) throw new InvalidOperationException("Another inventory action is active; preview again.");
        using (lease)
        {
            foreach (var c in chests)
            {
                if (!c.Endpoint.Authorized()) throw new InvalidOperationException("Chest access or rules changed; preview again.");
                c.Endpoint.Before.Verify(c.Endpoint.Live);
            }
            // Durable diagnostic snapshot, written before touching live inventories.
            string directory = Path.Combine(Paths.ConfigPath, "SmartSortBackups");
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + ".txt");
            var journal = new List<string> { "RunicStorage 1.4.1 SmartSort pre-apply snapshot; diagnostic recovery data (not an automatic undo).", "World: " + ZNet.instance.GetWorldName() };
            foreach (var c in chests)
                journal.Add(ValheimContainerIdentity.EndpointId(c.Container) + "\t" + c.Container.transform.position + "\t" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(c.Rules)) + "\t" + ValheimContainerService.SaveInventory(c.Endpoint.Live).GetBase64());
            File.WriteAllLines(file, journal);
            SmartSortTransaction.Apply(chests.Select(c => c.Endpoint).ToArray());
            int labeled = 0;
            foreach (var c in chests)
            {
                try
                {
                    if (c.DestinationOnly) continue;
                    if (!c.Endpoint.Authorized() || !ChestRuleStore.Read(c.Container, out var rules)) continue;
                    rules.ShowLabel = true; rules.Label = c.Label;
                    string encoded = rules.Encode();
                    var zdo = ValheimContainerIdentity.NetworkView(c.Container).GetZDO();
                    zdo.Set(ChestRuleStore.Key, encoded); zdo.Set(AutoRulesKey, encoded);
                    ChestRuleStore.Attach(c.Container); labeled++;
                }
                catch (Exception ex) { Plugin.Log.LogWarning("SmartSort label skipped: " + ex.Message); }
            }
            Say($"SmartSort organized {chests.Length} chests; labeled {labeled}; skipped {_skipped}. Empty chests are marked Spare Storage.");
            Plugin.Log.LogInfo("SmartSort committed. Before-state snapshot: " + file);
        }
    }

    private static string Category(ItemDrop.ItemData item) => SmartSortCategories.For(ChestGroupRuntime.Facts(item));
    internal static string Label(Inventory inventory)
    {
        var items = inventory.GetAllItems();
        if (items.Count == 0) return "Spare Storage";
        string label;
        if (items.Select(ValheimContainerIdentity.ResourceId).Distinct().Count() == 1)
            label = Localization.instance.Localize(items[0].m_shared.m_name);
        else
        {
            var categories = items.Select(Category).Distinct().Select(SmartSortCategories.Display).ToArray();
            label = categories.Length <= 2 ? string.Join(" / ", categories) : "Mixed Materials";
        }
        return HoverLabelPolicy.NormalizeDisplayLabel(label, "Storage");
    }
    private static void Say(string message) => Player.m_localPlayer?.Message(MessageHud.MessageType.Center, message);
}

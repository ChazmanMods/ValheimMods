using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RunicStorage.Engine;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RunicStorage.Runtime;

internal sealed class ChestRulesPanel : IDisposable
{
    private static readonly FieldInfo Current = AccessTools.Field(typeof(InventoryGui), "m_currentContainer");
    private Button _launcher;
    private GameObject _canvas;
    private RectTransform _panel, _list, _aux;
    private Container _chest;
    private ChestRules _draft;
    private ChestRules _library;
    private bool _libraryValid;
    private ChestCustomGroup _editingGroup;
    private string _expected, _filter = "", _tab = "Items";
    private int _page;
    private TMP_Text _status, _preview, _rememberText, _showText, _positionText, _priorityText, _explanation, _colorText, _backgroundText;
    private Image _previewBackground;
    private GameObject _choices;
    private TMP_InputField _color, _label, _search;
    private StorageSearchVanillaTheme _theme;
    internal bool IsOpen => _canvas;
    private readonly List<string> _catalog = new();
    private static readonly string[] Sides = { "Front", "Back", "Left", "Right", "Top" };

    internal void Tick()
    {
        var gui = InventoryGui.instance;
        var chest = gui ? Current.GetValue(gui) as Container : null;
        if (!(PluginConfig.Enabled?.Value ?? false))
        { if (_launcher) _launcher.gameObject.SetActive(false); Close(); return; }
        if (!_launcher && gui && gui.m_takeAllButton)
        {
            _theme = StorageSearchVanillaTheme.Create();
            _launcher = Button(gui.m_container, "Quick Stack Rules", 0, 0, 175, 36, Open);
            _launcher.name = "RunicQuickStackRules";
            var rect = (RectTransform)_launcher.transform;
            // Dedicated footer outside the chest body; never occupy either vanilla header button.
            rect.anchorMin = rect.anchorMax = new Vector2(1, 0);
            rect.pivot = new Vector2(1, 1); rect.anchoredPosition = new Vector2(-8, -6); rect.sizeDelta = new Vector2(175, 36);
        }
        if (_launcher) _launcher.gameObject.SetActive(ChestRuleStore.Eligible(chest));
        if (!IsOpen) return;
        if (!gui || !gui.m_container.gameObject.activeInHierarchy || chest != _chest || !Player.m_localPlayer ||
            Player.m_localPlayer.IsDead() || Vector3.Distance(Player.m_localPlayer.transform.position, _chest.transform.position) > 5)
        { Close(); return; }
        StorageSearchPanel.RenewCursorLease();
        if (StorageSearchGameplayInputGuard.PollPickerEscape()) {
            if (_choices) { UnityEngine.Object.Destroy(_choices); _choices = null; }
            else Close();
        }
    }

    private void Open()
    {
        if (IsOpen) return;
        var gui = InventoryGui.instance;
        _chest = gui ? Current.GetValue(gui) as Container : null;
        if (!ChestRuleStore.CanEdit(_chest))
        { Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "Chest is busy or access is unavailable. Reopen it and try again."); return; }
        _expected = ChestRuleStore.Raw(_chest);
        if (!ChestRules.TryDecode(_expected, out _draft))
        { Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "Chest rules could not be read. Existing rules were preserved."); return; }
        _libraryValid = ChestGroupRuntime.LoadLibrary(out _library);
        if (!_libraryValid) _library = new ChestRules();
        _editingGroup = new ChestCustomGroup();
        _theme = StorageSearchVanillaTheme.Create(); _page = 0; _filter = ""; _tab = "Items";
        _catalog.Clear();
        if (ObjectDB.instance)
            _catalog.AddRange(ObjectDB.instance.m_items.Where(p => p && p.GetComponent<ItemDrop>())
                .Select(p => p.name).Where(ChestRules.ValidId).Distinct().OrderBy(ChestExteriorLabel.DisplayName));
        Build();
    }

    private void Build()
    {
        _canvas = new GameObject("RunicChestRulesCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = _canvas.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var source = InventoryGui.instance.GetComponentInParent<Canvas>(); canvas.sortingOrder = source ? source.sortingOrder + 110 : 1010;
        var scaler = _canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 800); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight; scaler.matchWidthOrHeight = 1;
        var shade = Image("Backdrop", _canvas.transform, new Color(0, 0, 0, .7f));
        shade.rectTransform.anchorMin = Vector2.zero; shade.rectTransform.anchorMax = Vector2.one; shade.rectTransform.offsetMin = shade.rectTransform.offsetMax = Vector2.zero;
        var background = Image("ChestRules", _canvas.transform, _theme.HasNativePanel ? _theme.PanelColor : new Color(.16f, .11f, .08f));
        background.sprite = _theme.PanelSprite; background.type = _theme.PanelType;
        if (_theme.PanelMaterial) background.material = _theme.PanelMaterial;
        _panel = background.rectTransform; _panel.anchorMin = _panel.anchorMax = _panel.pivot = new Vector2(.5f, .5f); _panel.sizeDelta = new Vector2(850, 730);
        Text(_panel, "Quick Stack Rules", 24, 22, 570, 34, 25);
        _priorityText = Button(_panel, "", 620, 22, 205, 34, () => { _draft.Preferred = !_draft.Preferred; Refresh(); }, ChestRulesHelp.For("Priority")).GetComponentInChildren<TMP_Text>();
        _explanation = Text(_panel, "", 24, 57, 800, 38, 15);
        _rememberText = Button(_panel, "", 24, 98, 390, 34, () => {
            _draft.Remember = !_draft.Remember;
            if (_draft.Remember) { _draft.Learn(_chest.GetInventory().GetAllItems().Select(ValheimContainerIdentity.ResourceId)); _draft.ShowLabel = true; }
            Refresh();
        }, ChestRulesHelp.For("Remember")).GetComponentInChildren<TMP_Text>();
        Button(_panel, "Clear remembered items", 430, 98, 220, 34, () => { _draft.Memory.Clear(); Refresh(); });
        Button(_panel, "Clear rules", 660, 98, 165, 34, () => { _draft.Items.Clear(); _draft.Categories.Clear(); _draft.CustomGroups.Clear(); _draft.Excluded.Clear(); _draft.OnlyBiome = ""; Refresh(); });
        int tabIndex = 0;
        foreach (var tab in new[] { ("Items", "Always accept"), ("Groups", "Groups"), ("Excluded", "Never accept"), ("Accepted", "Accepted"), ("Remembered", "Remembered"), ("Biome", "Only from biome"), ("Custom", "Custom groups") })
        {
            string target = tab.Item1;
            Button(_panel, tab.Item2, 24 + tabIndex % 4 * 204, 141 + tabIndex / 4 * 36, 190, 30,
                () => { _tab = target; _page = 0; _filter = ""; _search?.SetTextWithoutNotify(""); Refresh(); }); tabIndex++;
        }
        (_search = Input(_panel, "Search items or rules", "", 24, 216, 600, 32, 128)).onValueChanged.AddListener(s => { _filter = s; _page = 0; Refresh(); });
        Button(_panel, "Previous", 637, 216, 90, 32, () => { _page = Math.Max(0, _page - 1); Refresh(); });
        Button(_panel, "Next", 735, 216, 90, 32, () => { _page++; Refresh(); });
        _list = Rect("Rules", _panel, 24, 257, 801, 166);
        _aux = Rect("GroupControls", _panel, 24, 432, 801, 34);
        _showText = Button(_panel, "", 24, 474, 180, 32, () => { _draft.ShowLabel = !_draft.ShowLabel; Refresh(); }, ChestRulesHelp.For("Exterior")).GetComponentInChildren<TMP_Text>();
        _label = Input(_panel, "Automatic label (or enter your own)", _draft.Label, 214, 474, 280, 32, 96);
        _color = Input(_panel, "Color name", ChestLabelColors.Names[ChestLabelColors.Resolve(_draft.Color)], 504, 474, 145, 32, 32);
        _colorText = Button(_panel, "", 659, 474, 166, 32, () => ShowChoices(ChestLabelColors.Names, index => {
            _color.SetTextWithoutNotify(ChestLabelColors.Names[index]); UpdatePreview();
            _status.text = "Text color: " + ChestLabelColors.Names[index] + " (" + ChestLabelColors.Hex[index] + ").";
        }, true), ChestRulesHelp.For("Text color")).GetComponentInChildren<TMP_Text>();
        _color.onEndEdit.AddListener(NormalizeColor);
        _label.onValueChanged.AddListener(_ => UpdatePreview()); _color.onValueChanged.AddListener(_ => UpdatePreview());
        for (int i = 0; i < Sides.Length; i++) { int side = i; Button(_panel, Sides[i], 24 + i * 162, 518, 152, 30, () => { _draft.Side = side; Refresh(); }); }
        _positionText = Text(_panel, "", 24, 557, 800, 25, 17);
        Button(_panel, "Smaller", 24, 592, 110, 30, () => { _draft.Size = Mathf.Max(.3f, _draft.Size - .1f); Refresh(); });
        Button(_panel, "Larger", 144, 592, 110, 30, () => { _draft.Size = Mathf.Min(2, _draft.Size + .1f); Refresh(); });
        Button(_panel, "←", 278, 592, 75, 30, () => { _draft.Horizontal = Mathf.Max(-1, _draft.Horizontal - .05f); Refresh(); });
        Button(_panel, "→", 363, 592, 75, 30, () => { _draft.Horizontal = Mathf.Min(1, _draft.Horizontal + .05f); Refresh(); });
        Button(_panel, "↓", 448, 592, 75, 30, () => { _draft.Vertical = Mathf.Max(-1, _draft.Vertical - .05f); Refresh(); });
        Button(_panel, "↑", 533, 592, 75, 30, () => { _draft.Vertical = Mathf.Min(1, _draft.Vertical + .05f); Refresh(); });
        Button(_panel, "Reset position", 628, 592, 197, 30, () => { _draft.Horizontal = _draft.Vertical = 0; _draft.Size = 1; Refresh(); });
        _previewBackground = Image("LabelPreviewBackground", _panel, Color.clear);
        _previewBackground.rectTransform.anchoredPosition = new Vector2(24, -632); _previewBackground.rectTransform.sizeDelta = new Vector2(560, 30);
        _previewBackground.raycastTarget = false;
        _preview = Text(_panel, "", 30, 632, 548, 30, 20);
        _backgroundText = Button(_panel, "", 595, 632, 230, 30, () => ShowChoices(new[] { "Transparent", "White", "Black" }, index => {
            _draft.Background = index; UpdatePreview();
        }), ChestRulesHelp.For("Background")).GetComponentInChildren<TMP_Text>();
        _status = Text(_panel, "", 24, 674, 550, 36, 16);
        Button(_panel, "Cancel", 595, 674, 105, 34, Close);
        Button(_panel, "Save", 715, 674, 110, 34, Save);
        Refresh();
    }

    private void Refresh()
    {
        if (!_list) return;
        foreach (Transform child in _list) { child.gameObject.SetActive(false); UnityEngine.Object.Destroy(child.gameObject); }
        IEnumerable<string> entries = _tab switch {
            "Groups" => ChestRuleGroups.All.Select(g => g.Id).Concat(CustomDefinitions().Select(g => g.Id)),
            "Accepted" => _draft.Items.Concat(_draft.Categories), "Remembered" => _draft.Memory,
            "Biome" => new[] { "biome:any" }.Concat(ChestRuleGroups.Biomes.Select(g => g.Id)),
            "Custom" => CustomDefinitions().Select(g => g.Id), _ => _catalog };
        var filtered = entries.Where(id => Name(id).IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0 || id.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        const int pageSize = 8;
        _page = Math.Min(_page, Math.Max(0, (filtered.Count - 1) / pageSize));
        for (int i = 0; i < pageSize && _page * pageSize + i < filtered.Count; i++)
        {
            string id = filtered[_page * pageSize + i];
            bool selected = _tab switch { "Excluded" => _draft.Excluded.Contains(id), "GroupItems" => _editingGroup.Items.Contains(id),
                "Biome" => id == (_draft.OnlyBiome.Length == 0 ? "biome:any" : _draft.OnlyBiome),
                "Custom" => id == _editingGroup.Id, "Remembered" => true, _ => _draft.Items.Contains(id) || _draft.Categories.Contains(id) };
            Button(_list, (selected ? "✓ " : "") + Name(id), (i % 2) * 406, (i / 2) * 41, 395, 35, () => {
                if (_tab == "Biome") { _draft.OnlyBiome = id == "biome:any" ? "" : id; if (_draft.OnlyBiome.Length > 0) _draft.ShowLabel = true; }
                else if (_tab == "Custom") _editingGroup = CustomDefinitions().First(g => g.Id == id).Copy();
                else {
                    bool group = _tab == "Groups" || _tab == "Accepted" && _draft.Categories.Contains(id);
                    var list = _tab == "Remembered" ? _draft.Memory : _tab == "Excluded" ? _draft.Excluded :
                        _tab == "GroupItems" ? _editingGroup.Items : group ? _draft.Categories : _draft.Items;
                    if (list.Contains(id)) { list.Remove(id); if (group) _draft.CustomGroups.RemoveAll(g => g.Id == id); }
                    else if (list.Count < ChestRules.Limit) {
                        var custom = group ? CustomDefinitions().FirstOrDefault(g => g.Id == id) : null;
                        if (custom != null && _draft.CustomGroups.Count >= 16) { _status.text = "A chest can use at most 16 custom groups."; return; }
                        list.Add(id); if (custom != null) _draft.CustomGroups.Add(custom.Copy());
                        _draft.ShowLabel = true;
                    }
                }
                Refresh();
            }, RowHelp(id));
        }
        _rememberText.text = "Remember contents: " + (_draft.Remember ? "ON" : "OFF") + " (" + _draft.Memory.Count + ")";
        _showText.text = "Exterior label: " + (_draft.ShowLabel ? "ON" : "OFF");
        _priorityText.text = "Priority: " + (_draft.Preferred ? "Preferred" : "Normal");
        _explanation.text = "Never accept blocks delivery to this chest, regardless of priority or other rules.\n" +
            (_draft.Preferred ? "Preferred: try this chest before Normal chests with equally specific rules; then use distance." :
            "Normal: Preferred chests with equally specific rules come first; otherwise use distance.");
        _positionText.text = $"{Sides[_draft.Side]}  |  Size {_draft.Size:0.0}  |  Horizontal {_draft.Horizontal:0.00} m  |  Vertical {_draft.Vertical:0.00} m  |  {_tab}: {_page + 1}/{Math.Max(1, (filtered.Count + pageSize - 1) / pageSize)}";
        RefreshAux();
        UpdatePreview();
    }
    private IEnumerable<ChestCustomGroup> CustomDefinitions() => _library.CustomGroups.Concat(_draft.CustomGroups)
        .GroupBy(g => g.Id).Select(g => g.First());
    private string Name(string id) => id == "biome:any" ? "Any biome" :
        CustomDefinitions().FirstOrDefault(g => g.Id == id)?.Name ?? ChestRuleGroups.Find(id)?.Name ?? ChestExteriorLabel.DisplayName(id);
    private void RefreshAux()
    {
        foreach (Transform child in _aux) { child.gameObject.SetActive(false); UnityEngine.Object.Destroy(child.gameObject); }
        if (_tab != "Custom" && _tab != "GroupItems") {
            Text(_aux, "Biome filter: " + (_draft.OnlyBiome.Length == 0 ? "Any" : Name(_draft.OnlyBiome)) +
                "  |  " + _draft.Items.Count + " always / " + _draft.Excluded.Count + " never / " + _draft.Categories.Count + " groups", 0, 0, 801, 32, 17); return;
        }
        Input(_aux, "Group name", _editingGroup.Name, 0, 0, 250, 32, 48).onValueChanged.AddListener(s => _editingGroup.Name = s);
        Button(_aux, "Items (" + _editingGroup.Items.Count + ")", 260, 0, 135, 32, () => { _tab = "GroupItems"; _page = 0; _filter = ""; _search?.SetTextWithoutNotify(""); Refresh(); });
        Button(_aux, "Save group", 405, 0, 135, 32, SaveGroup);
        Button(_aux, "Delete", 550, 0, 105, 32, DeleteGroup);
        Button(_aux, "New group", 665, 0, 135, 32, () => { _editingGroup = new ChestCustomGroup(); _tab = "GroupItems"; _page = 0; _filter = ""; _search?.SetTextWithoutNotify(""); Refresh(); });
    }
    private void SaveGroup()
    {
        if (!_libraryValid) { _status.text = "The saved group library is unreadable; it was preserved."; return; }
        _editingGroup.Name = _editingGroup.Name.Trim();
        if (!ChestRules.ValidId(_editingGroup.Name) || _editingGroup.Items.Count == 0) { _status.text = "Give the group a name and select at least one item."; return; }
        if (_library.CustomGroups.Any(g => g.Id != _editingGroup.Id && g.Name.Equals(_editingGroup.Name, StringComparison.OrdinalIgnoreCase)))
        { _status.text = "Choose a unique group name."; return; }
        var next = new ChestRules(); next.CustomGroups.AddRange(_library.CustomGroups.Where(g => g.Id != _editingGroup.Id).Select(g => g.Copy()));
        next.CustomGroups.Add(_editingGroup.Copy());
        try {
            ChestGroupRuntime.SaveLibrary(next); _library = next;
            if (_draft.Categories.Contains(_editingGroup.Id)) { _draft.CustomGroups.RemoveAll(g => g.Id == _editingGroup.Id); _draft.CustomGroups.Add(_editingGroup.Copy()); }
            _status.text = "Group saved. Select it under Groups, then Save the chest.";
        } catch (Exception ex) { _status.text = ex.Message; }
        Refresh();
    }
    private void DeleteGroup()
    {
        if (!_libraryValid) return;
        var next = new ChestRules(); next.CustomGroups.AddRange(_library.CustomGroups.Where(g => g.Id != _editingGroup.Id).Select(g => g.Copy()));
        try { ChestGroupRuntime.SaveLibrary(next); _library = next; _status.text = "Library entry removed; existing chest selections are preserved."; _editingGroup = new ChestCustomGroup(); }
        catch (Exception ex) { _status.text = ex.Message; }
        Refresh();
    }
    private void UpdatePreview()
    {
        if (!_preview) return;
        int index = ChestLabelColors.Resolve(_color.text);
        _preview.richText = true;
        _preview.text = ChestLabelColors.Markup(string.IsNullOrWhiteSpace(_label.text) ? ChestExteriorLabel.AutomaticText(_draft) : _label.text, ChestLabelColors.Hex[index]);
        _preview.color = Color.white;
        _colorText.text = "Text: " + ChestLabelColors.Names[index] + " ▼";
        _backgroundText.text = "Background: " + new[] { "Transparent", "White", "Black" }[_draft.Background] + " ▼";
        _previewBackground.color = _draft.Background == 1 ? Color.white : _draft.Background == 2 ? Color.black : Color.clear;
    }
    private void NormalizeColor(string input)
    {
        int index = ChestLabelColors.Resolve(input);
        _color.SetTextWithoutNotify(ChestLabelColors.Names[index]);
        UpdatePreview();
        if (_status) _status.text = input.Trim().Equals(ChestLabelColors.Names[index], StringComparison.OrdinalIgnoreCase) ?
            "Text color: " + ChestLabelColors.Names[index] + " (" + ChestLabelColors.Hex[index] + ")." :
            "Using " + ChestLabelColors.Names[index] + " for ‘" + input + "’ (" + ChestLabelColors.Hex[index] + ").";
    }
    private string RowHelp(string id)
    {
        string action = _tab switch {
            "Biome" => "Select this biome filter.", "Custom" => "Load this template for editing.",
            "Remembered" => "Forget this remembered item type. It may be learned again after later inventory changes if Remember stays ON.",
            "Accepted" => "Remove this explicit acceptance rule.", "Excluded" => "Toggle exclusion. When checked, this item cannot be delivered here by Runic Quick Stack, regardless of priority.",
            "GroupItems" => "Add or remove this item from the custom group being edited.",
            "Groups" => "Toggle this group. Multiple checked groups accept any matching item, subject to exclusions and the biome filter.",
            _ => "Toggle this exact-item exception. When checked, accept this item even when the chest is empty or its biome differs. Never accept still blocks it." };
        return Name(id) + " (" + id + ")\n" + action;
    }
    private void ShowChoices(string[] options, Action<int> choose, bool colors = false)
    {
        if (_choices) { UnityEngine.Object.Destroy(_choices); _choices = null; return; }
        var dismiss = Rect("CloseChoices", _panel, 0, 0, 850, 730);
        _choices = dismiss.gameObject;
        var overlay = _choices.AddComponent<Image>(); overlay.color = new Color(0, 0, 0, .45f);
        _choices.AddComponent<Button>().onClick.AddListener(() => { UnityEngine.Object.Destroy(_choices); _choices = null; });
        int rows = (options.Length + 2) / 3;
        var menu = Rect("Choices", dismiss, 350, colors ? 474 - rows * 36 - 12 : 588, 475, rows * 36 + 12);
        menu.gameObject.AddComponent<Image>().color = new Color(.1f, .07f, .04f, 1);
        for (int i = 0; i < options.Length; i++) {
            int index = i;
            Button(menu, options[i], 6 + i % 3 * 155, 6 + i / 3 * 36, 151, 32, () => {
                choose(index); UnityEngine.Object.Destroy(_choices); _choices = null;
            }, colors ? "Use " + options[i] + " text (" + ChestLabelColors.Hex[i] + ")." : "Use a " + options[i].ToLowerInvariant() + " label background.");
        }
    }
    private void Save()
    {
        _draft.Color = ChestLabelColors.Hex[ChestLabelColors.Resolve(_color.text)]; _draft.Label = _label.text.Trim();
        if (ChestRuleStore.Save(_chest, _draft, _expected, out string error)) Close();
        else _status.text = error;
    }
    internal void Draw() { if (IsOpen) StorageSearchGameplayInputGuard.CaptureGuiPointer(Event.current); }
    internal void Close() { if (_canvas) UnityEngine.Object.Destroy(_canvas); _canvas = null; _chest = null; }
    public void Dispose() { Close(); if (_launcher) UnityEngine.Object.Destroy(_launcher.gameObject); }

    private static RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
    {
        var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
        var r = (RectTransform)go.transform; r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1);
        r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(width, height); return r;
    }
    private static Image Image(string name, Transform parent, Color color)
    { var r = Rect(name, parent, 0, 0, 0, 0); var image = r.gameObject.AddComponent<Image>(); image.color = color; return image; }
    private TMP_Text Text(Transform parent, string value, float x, float y, float w, float h, int size = 18)
    {
        var r = Rect("Text", parent, x, y, w, h); var text = r.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = _theme.Font ? _theme.Font : TMP_Settings.defaultFontAsset; text.text = value; text.fontSize = size;
        text.color = new Color(1, .85f, .57f); text.richText = false; text.raycastTarget = false;
        text.alignment = TextAlignmentOptions.MidlineLeft; text.overflowMode = TextOverflowModes.Ellipsis; return text;
    }
    private Button Button(Transform parent, string label, float x, float y, float w, float h, UnityEngine.Events.UnityAction action, string help = null)
    {
        var r = Rect("Button", parent, x, y, w, h); var image = r.gameObject.AddComponent<Image>();
        image.sprite = _theme.ButtonSprite; image.type = _theme.ButtonImageType;
        image.color = _theme.ButtonSprite ? _theme.ButtonImageColor : new Color(.29f, .2f, .13f);
        var button = r.gameObject.AddComponent<Button>(); button.targetGraphic = image; button.onClick.AddListener(action);
        var caption = Text(r, label, 8, 0, w - 16, h); caption.alignment = TextAlignmentOptions.Center;
        caption.enableAutoSizing = true; caption.fontSizeMin = 14; caption.fontSizeMax = 18;
        Hint(r.gameObject, help ?? ChestRulesHelp.For(label)); return button;
    }
    private void Hint(GameObject target, string help)
    { var hint = target.AddComponent<ChestControlHint>(); hint.Help = help; hint.Font = _theme.Font ? _theme.Font : TMP_Settings.defaultFontAsset; }
    private TMP_InputField Input(Transform parent, string hint, string value, float x, float y, float w, float h, int limit)
    {
        var r = Rect("Input", parent, x, y, w, h); var image = r.gameObject.AddComponent<Image>(); image.color = new Color(.08f, .06f, .04f);
        var input = r.gameObject.AddComponent<TMP_InputField>(); input.targetGraphic = image; input.characterLimit = limit;
        var area = Rect("Viewport", r, 8, 0, w - 16, h); area.gameObject.AddComponent<RectMask2D>();
        var text = Text(area, value, 0, 0, w - 16, h); var placeholder = Text(area, hint, 0, 0, w - 16, h); placeholder.color = Color.gray;
        input.textViewport = area; input.textComponent = text; input.placeholder = placeholder; input.SetTextWithoutNotify(value);
        Hint(r.gameObject, ChestRulesHelp.For(hint));
        return input;
    }
}

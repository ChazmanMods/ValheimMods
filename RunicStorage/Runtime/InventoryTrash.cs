using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ItemData = ItemDrop.ItemData;

namespace RunicStorage.Runtime;

internal sealed class InventoryTrash
{
    internal static InventoryTrash Active;
    internal static bool ModalOpen => Active?._dialog != null;
    private readonly TrashSelection _selection = new();
    private readonly Dictionary<ItemData, GameObject> _outlines = new();
    private readonly ConfigEntry<bool> _enabled;
    private readonly HashSet<ItemData> _locked = new();
    private bool _protectionKnown;
    private Player _player;
    private Inventory _inventory;
    private InventoryGui _gui;
    private RectTransform _button;
    private GameObject _dialog;
    private TMP_Text _count;
    private TrashSelection.Entry[] _confirmation;
    private float _nextRefresh;
    private static readonly MethodInfo Position = AccessTools.Method(typeof(InventoryGrid), "GetButtonPos", new[] { typeof(GameObject) });
    private static readonly MethodInfo Element = AccessTools.Method(typeof(InventoryGrid), "GetElement", new[] { typeof(int), typeof(int), typeof(int) });
    private static readonly FieldInfo Drag = AccessTools.Field(typeof(InventoryGui), "m_dragItem");
    internal InventoryTrash(ConfigFile config) { Active = this; TrashInventoryLockGuard.ResetDiscovery(); _enabled = config.Bind("Inventory Trash", "Enabled", true, "Shift+Alt+right-click selects eligible player stacks; click the trashcan to confirm permanent deletion."); }

    internal void Tick()
    {
        TrashInventoryLockGuard.EnsureInstalled();
        var player = Player.m_localPlayer;
        var gui = InventoryGui.instance;
        if (!_enabled.Value || !PluginConfig.Enabled.Value || !player || player.IsDead() || !player.IsOwner() || !gui || !InventoryGui.IsVisible())
        { Clear(); return; }
        if (_player != player || _gui != gui || !ReferenceEquals(_inventory, player.GetInventory()))
        { Clear(); _player = player; _gui = gui; _inventory = player.GetInventory(); }
        if (!_button) CreateButton();
        PlaceButton();
        if (Time.unscaledTime >= _nextRefresh)
        {
            _nextRefresh = Time.unscaledTime + .1f;
            CaptureProtection(); _selection.Prune(_inventory, Eligible); RefreshOutlines();
        }
        if (_count) _count.text = _selection.Entries.Count == 0 ? "" : _selection.Entries.Count.ToString();
        if (_dialog && ZInput.GetKeyDown(KeyCode.Escape, false)) CloseDialog();
    }

    private void CaptureProtection()
    {
        _locked.Clear();
        var items = _inventory.GetAllItems();
        _protectionKnown = Engine.StorageItemProtection.TryCapture(items, out var snapshot, out _);
        if (!_protectionKnown) return;
        for (int i = 0; i < items.Count; i++) if (snapshot.StateAt(i) == Engine.StorageProtectionState.Locked) _locked.Add(items[i]);
    }
    private bool Eligible(ItemData item)
    {
        if (!_protectionKnown || item == null || item.m_shared == null) return false;
        int row = item.m_gridPos.y;
        int dedicatedRow = -1;
        if (_player.m_customData.TryGetValue("runic.inventory.extra-row.v1", out string value) && int.TryParse(value, out int roleRow)) dedicatedRow = roleRow;
        return TrashSelection.CellAllowed(row, _inventory.GetHeight(), _locked.Contains(item),
            item.m_equipped || _player.IsItemEquiped(item), item.m_shared.m_questItem, item.m_stack, dedicatedRow);
    }
    internal bool OwnsTrashChord(InventoryGrid grid) =>
        _enabled.Value && PluginConfig.Enabled.Value && InventoryGui.instance && grid == InventoryGui.instance.m_playerGrid &&
        (ZInput.GetKey(KeyCode.LeftAlt, false) || ZInput.GetKey(KeyCode.RightAlt, false)) &&
        (ZInput.GetKey(KeyCode.LeftShift, false) || ZInput.GetKey(KeyCode.RightShift, false));

    internal bool OnRightDown(InventoryGrid grid, UIInputHandler clicked)
    {
        if (!OwnsTrashChord(grid)) return false;
        // Always consume this chord on the player grid, including protected/empty cells.
        // The optional lock-method guard below prevents competing prefixes from locking it.
        Tick();
        if (_dialog || _inventory == null || Position == null || Drag == null || Drag.GetValue(_gui) != null) return true;
        CaptureProtection();
        var position = (Vector2i)Position.Invoke(grid, new object[] { clicked.gameObject });
        _selection.Toggle(_inventory, _inventory.GetItemAt(position.x, position.y), Eligible);
        RefreshOutlines(); return true;
    }

    private void RefreshOutlines()
    {
        foreach (var key in _outlines.Keys.ToArray())
            if (!_selection.Entries.Any(e => ReferenceEquals(e.Item, key)) || !_outlines[key])
            { if (_outlines[key]) UnityEngine.Object.Destroy(_outlines[key]); _outlines.Remove(key); }
        foreach (var entry in _selection.Entries)
        {
            if (_outlines.ContainsKey(entry.Item)) continue;
            var element = Element?.Invoke(_gui.m_playerGrid, new object[] { entry.Item.m_gridPos.x, entry.Item.m_gridPos.y, _inventory.GetWidth() }) as Component;
            if (!element) continue;
            var root = Rect("SmartSortTrashSelection", element.transform, Vector2.zero, Vector2.zero);
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one; root.offsetMin = new Vector2(2, 2); root.offsetMax = new Vector2(-2, -2);
            var fill = root.gameObject.AddComponent<Image>(); fill.color = new Color(1, 0, 0, .22f); fill.raycastTarget = false;
            Border(root, new Vector2(0,0),new Vector2(1,0),new Vector2(0,3));
            Border(root, new Vector2(0,1),new Vector2(1,1),new Vector2(0,3));
            Border(root, new Vector2(0,0),new Vector2(0,1),new Vector2(3,0));
            Border(root, new Vector2(1,0),new Vector2(1,1),new Vector2(3,0));
            _outlines[entry.Item] = root.gameObject;
        }
    }
    private static void Border(RectTransform parent, Vector2 min, Vector2 max, Vector2 size)
    { var rect=Rect("Red outline",parent,Vector2.zero,size); rect.anchorMin=min; rect.anchorMax=max; var image=rect.gameObject.AddComponent<Image>(); image.color=Color.red;image.raycastTarget=false; }

    private void CreateButton()
    {
        _button = Rect("SmartSortTrashTab", _gui.m_player, Vector2.zero, new Vector2(34,34));
        var button = Button(_button, "", () => OpenDialog());
        // Font-independent trashcan icon, with no dependency on emoji/font glyph coverage.
        Icon(_button, new Vector2(0,-3),new Vector2(15,17),new Color(1,.85f,.6f));
        Icon(_button, new Vector2(0,8),new Vector2(21,3),new Color(1,.85f,.6f));
        Icon(_button, new Vector2(0,12),new Vector2(8,3),new Color(1,.85f,.6f));
        for(int x=-4;x<=4;x+=4)Icon(_button,new Vector2(x,-3),new Vector2(1,11),new Color(.15f,.1f,.06f));
        _count=Text(_button,"",new Vector2(14,-13),new Vector2(30,18),12);
        var hint=_button.gameObject.AddComponent<ChestControlHint>(); hint.Font=StorageText.Resolve();
        hint.Help="Trash selected items\nShift+Alt+right-click: select/unselect\nHotbar, RunicInventory armor/quick-slot row, locked, equipped and quest items are protected.";
    }
    private void PlaceButton()
    {
        if (!_gui.m_weight || !_gui.m_armor) { _button.gameObject.SetActive(false); return; }
        // Use the existing values as anchors; never move or replace either tab.
        Vector3 a=_gui.m_weight.rectTransform.TransformPoint(_gui.m_weight.rectTransform.rect.center);
        Vector3 b=_gui.m_armor.rectTransform.TransformPoint(_gui.m_armor.rectTransform.rect.center);
        _button.position=(a+b)*.5f;
        _button.gameObject.SetActive(true);
        _button.SetAsLastSibling();
    }
    private void OpenDialog()
    {
        if (_dialog || Drag == null || Drag.GetValue(_gui) != null) return;
        CaptureProtection();_selection.Prune(_inventory,Eligible);RefreshOutlines();
        if (_selection.Entries.Count==0) { Tell("Shift+Alt+right-click items to select them for deletion."); return; }
        _confirmation=_selection.Entries.ToArray();
        var root=Rect("SmartSortTrashConfirm",_gui.m_player.parent,Vector2.zero,Vector2.zero);
        root.anchorMin=Vector2.zero;root.anchorMax=Vector2.one;root.offsetMin=root.offsetMax=Vector2.zero;
        _dialog=root.gameObject;
        var shade=root.gameObject.AddComponent<Image>();shade.color=new Color(0,0,0,.65f);shade.raycastTarget=true;
        var panel=Rect("Panel",root,Vector2.zero,new Vector2(490,210));
        panel.gameObject.AddComponent<Image>().color=new Color(.12f,.08f,.045f,1);
        Text(panel,$"Permanently delete {_confirmation.Sum(e=>(long)e.Before.m_stack)} items?",new Vector2(0,55),new Vector2(450,50),23);
        Text(panel,$"{_confirmation.Length} selected stacks. This cannot be undone.",new Vector2(0,8),new Vector2(450,35),17);
        Button(Rect("Confirm",panel,new Vector2(-150,-65),new Vector2(135,40)),"Confirm",Confirm);
        Button(Rect("Cancel",panel,new Vector2(0,-65),new Vector2(135,40)),"Cancel",CloseDialog);
        Button(Rect("Reset",panel,new Vector2(150,-65),new Vector2(135,40)),"Reset",()=>{CloseDialog();_selection.Entries.Clear();RefreshOutlines();});
        root.SetAsLastSibling();
    }
    private void Confirm()
    {
        try
        {
            if (!_player || _player.IsDead() || !InventoryGui.IsVisible() || !ReferenceEquals(_inventory,_player.GetInventory()) || Drag.GetValue(_gui)!=null)
                throw new InvalidOperationException("Inventory state changed. Nothing deleted.");
            var confirmed = _confirmation;
            if (confirmed == null || confirmed.Length == 0) return;
            long count = confirmed.Sum(e => (long)e.Before.m_stack);
            CaptureProtection();
            if (!StorageMutationLease.TryBegin(_player,out var lease)) throw new InvalidOperationException("Another inventory action is active. Nothing deleted.");
            using(lease)TrashDeletion.Apply(_player,_inventory,confirmed,Eligible,lease);
            _selection.Entries.Clear();CloseDialog();RefreshOutlines();
            Tell($"Deleted {count} items.");Plugin.Log.LogInfo($"Inventory trash: player confirmed deletion of {count} items.");
        }
        catch(Exception ex)
        {
            Plugin.Log.LogError("Inventory trash: "+ex);CloseDialog();Tell(ex.Message);
            CaptureProtection();_selection.Prune(_inventory,Eligible);RefreshOutlines();
        }
    }
    private void CloseDialog() { if(_dialog) { _dialog.SetActive(false); UnityEngine.Object.Destroy(_dialog); } _dialog=null;_confirmation=null; }
    internal void Clear()
    {
        CloseDialog();_selection.Entries.Clear();foreach(var outline in _outlines.Values)if(outline)UnityEngine.Object.Destroy(outline);_outlines.Clear();
        if(_button)UnityEngine.Object.Destroy(_button.gameObject);_button=null;_count=null;_inventory=null;_player=null;_gui=null;
    }
    private static RectTransform Rect(string name,Transform parent,Vector2 position,Vector2 size)
    {var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);var r=(RectTransform)go.transform;r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,.5f);r.anchoredPosition=position;r.sizeDelta=size;return r;}
    private static TMP_Text Text(Transform parent,string value,Vector2 position,Vector2 size,int fontSize)
    {var r=Rect("Text",parent,position,size);var t=StorageText.Add(r);t.text=value;t.fontSize=fontSize;t.richText=false;t.alignment=TextAlignmentOptions.Center;t.color=new Color(1,.88f,.65f);t.raycastTarget=false;return t;}
    private static Button Button(RectTransform rect,string label,UnityEngine.Events.UnityAction action)
    {var image=rect.gameObject.AddComponent<Image>();image.color=new Color(.28f,.18f,.09f,.98f);var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=image;button.onClick.AddListener(action);if(label.Length>0)Text(rect,label,Vector2.zero,rect.sizeDelta,19);return button;}
    private static void Icon(Transform parent,Vector2 position,Vector2 size,Color color)
    {var r=Rect("Icon",parent,position,size);var i=r.gameObject.AddComponent<Image>();i.color=color;i.raycastTarget=false;}
    private static void Tell(string text)=>Player.m_localPlayer?.Message(MessageHud.MessageType.Center,text);
}

[HarmonyPatch(typeof(InventoryGrid), "OnRightDown", new[] { typeof(UIInputHandler) })]
internal static class TrashRightClickPatch
{
    [HarmonyPriority(Priority.First + 100)]
    [HarmonyBefore("chazman.RunicInventory")]
    private static bool Prefix(InventoryGrid __instance, UIInputHandler __0)
    {
        try { return !(InventoryTrash.Active?.OnRightDown(__instance,__0)??false); }
        catch(Exception ex) {Plugin.Log.LogError("Trash selection rejected: "+ex);return false;}
    }
}

using System;
using System.Globalization;
using RunicAutomation.Operations;
using RunicStorage.Engine;
using UnityEngine;

namespace RunicStorage.Runtime;

internal sealed partial class SmartSortOrganizer
{
    private string authorityPreview;
    private string[] authorityLabels;
    private bool authorityWaiting;
    private int authorityGeneration;
    private string authorityPreviewRequestId;
    private bool AuthorityHandle(StorageRouteContext context)
    {
        bool available = _enabled.Value && context.Enabled && !context.GameplayInputBlocked && !context.DraggedItem &&
            !context.InventoryOpen && !context.StoreOpen && !context.MapOpen && context.LocalPlayerOwnershipAvailable &&
            Player.m_localPlayer && !Player.m_localPlayer.IsDead();
        if (!available || _actor != Player.m_localPlayer || Input.GetKeyDown(KeyCode.Escape) ||
            (authorityPreview != null && Time.realtimeSinceStartup >= _expires))
        {
            if (authorityPreviewRequestId != null) InventoryOperationNetwork.CancelQuery(authorityPreviewRequestId);
            authorityPreviewRequestId = null;
            authorityPreview = null; authorityLabels = null; authorityWaiting = false; authorityGeneration++;
        }
        if (!_key.Value.IsDown()) return false;
        if (!available || authorityWaiting) return true;
        _actor = Player.m_localPlayer;
        int range = Mathf.FloorToInt(Range);
        if (authorityPreview != null)
        {
            var request = new StorageAuthorityRequest { Action = AuthorityStorageAction.Organize, Amount = range, Rules = authorityPreview };
            if (StorageOperationClient.Submit(request, Array.Empty<Tuple<ItemDrop.ItemData, int>>(), (accepted, _) => {
                if (accepted) Say("SmartSort organized the confirmed chests.");
            })) { authorityPreview = null; authorityLabels = null; }
            return true;
        }
        var preview = new StorageAuthorityRequest { Action = AuthorityStorageAction.PreviewOrganize, Amount = range,
            Escrow = NativeInventoryCodec.Save(new Inventory("empty", null, 32, 32)) };
        int generation = ++authorityGeneration; authorityWaiting = true;
        authorityPreviewRequestId = Guid.NewGuid().ToString("N");
        bool sent = InventoryOperationNetwork.Send(ZNet.instance.GetWorldUID().ToString(CultureInfo.InvariantCulture), authorityPreviewRequestId,
            StorageAuthorityCommands.QueryKind, preview.Encode(), (status, reason, response) => {
                if (generation != authorityGeneration) return;
                authorityPreviewRequestId = null;
                authorityWaiting = false;
                if (status != (int)OperationStatus.Committed) { Say("SmartSort: " + reason.Replace('-', ' ')); return; }
                try
                {
                    var p = new ZPackage(response); string signature = p.ReadString(); int count = p.ReadInt();
                    if (signature.Length != 64 || count < 2 || count > 64) throw new InvalidOperationException("Invalid preview");
                    var labels = new string[count];
                    for (int n = 0; n < count; n++) { labels[n] = p.ReadString(); if (labels[n].Length > 4096) throw new InvalidOperationException("Invalid preview label"); }
                    OperationPacket.End(p); authorityPreview = signature; authorityLabels = labels; _expires = Time.realtimeSinceStartup + 30;
                    Say("SmartSort preview ready. Press " + _key.Value + " again within 30 seconds to apply.");
                }
                catch (Exception error) { Plugin.Log.LogError("SmartSort preview rejected: " + error); }
            });
        if (!sent) { authorityWaiting = false; authorityPreviewRequestId = null; }
        return true;
    }
    private void DrawAuthority()
    {
        if (authorityWaiting) { GUI.Box(new Rect(Screen.width - 370, 60, 350, 45), "SmartSort: preparing preview..."); return; }
        if (authorityLabels == null || _actor != Player.m_localPlayer || Time.realtimeSinceStartup >= _expires) return;
        GUILayout.BeginArea(new Rect(Screen.width - 420, 60, 400, Math.Min(650, 95 + authorityLabels.Length * 22)), GUI.skin.box);
        GUILayout.Label("SmartSort — proposed chest contents");
        foreach (var label in authorityLabels) GUILayout.Label(label);
        GUILayout.Label(_key.Value + " to apply · Esc to cancel"); GUILayout.EndArea();
    }
}

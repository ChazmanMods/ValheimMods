using System;
using HarmonyLib;
using RunicSigns.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RunicSigns.Runtime;

internal sealed class SignRuntime : MonoBehaviour, IPlaced
{
    internal const string SettingsKey = "RunicSigns.Settings.v1";
    private const string LeaseKey = "RunicSigns.SaveLease.v1", ExpiryKey = "RunicSigns.SaveExpiry.v1";
    private const string RequestRpc = "RunicSigns_RequestSave_v1", ResponseRpc = "RunicSigns_SaveResponse_v1";
    private Sign _sign;
    private ZNetView _view;
    private SignAppearance _appearance;
    private SignSettings _preview;
    private string _previewCaption;
    private string _rendered;
    private float _next;
    private string _pendingToken, _expectedStyle, _expectedText, _newStyle, _newText;
    private long _requestedOwner;
    private float _requestDeadline;
    private bool _granted, _nativeWrite;
    private Action<bool, string> _completion;
    internal bool Saving => _pendingToken != null;
    public void OnPlaced()
    {
        // Native Player.PlacePiece calls IPlaced after setting the creator. Loaded signs and
        // placement ghosts never receive this callback, so their saved appearance is preserved.
        if (!_view || !_view.IsValid() || !_view.IsOwner() || Raw.Length != 0 ||
            !SignPlacement.IsLocalPlacement(_sign)) return;
        var settings = SignPlacement.CurrentSettings;
        if (!settings.Valid) return;
        if (SignPlacement.HasCaption) {
            _nativeWrite = true;
            try { _sign.SetText(SignPlacement.Caption); }
            finally { _nativeWrite = false; }
            if (NativeText != SignPlacement.Caption) return;
        }
        _view.GetZDO().Set(SettingsKey, settings.Encode());
        Render();
    }
    internal string Raw => _view.GetZDO().GetString(SettingsKey, "");
    internal string NativeText => _view.GetZDO().GetString(ZDOVars.s_text, _sign.m_defaultText);
    internal static SignRuntime Attach(Sign sign)
    {
        if (!SignAccess.Eligible(sign)) return null;
        return sign.GetComponent<SignRuntime>() ?? sign.gameObject.AddComponent<SignRuntime>();
    }
    private void Awake()
    {
        _sign = GetComponent<Sign>(); _view = GetComponent<ZNetView>();
        _appearance = new SignAppearance(transform, _sign.m_textWidget);
        _view.Register<string, string, string>(RequestRpc, RequestSave);
        _view.Register<string, string>(ResponseRpc, SaveResponse);
        Render();
    }
    private void Update()
    {
        if (!_view || !_view.IsValid()) { CancelSave(); return; }
        if (Saving)
        {
            if (Time.unscaledTime > _requestDeadline) Finish(false, "Save timed out. Your draft is still here; wait a moment and try again.");
            else if (_granted && _view.IsOwner() && _view.GetZDO().GetString(LeaseKey, "") == _pendingToken) Commit();
        }
        if (Time.unscaledTime >= _next) { _next = Time.unscaledTime + .25f; Render(); }
    }
    internal void BeginSave(SignSettings settings, string text, string expectedStyle, string expectedText, Action<bool, string> completion)
    {
        if (Saving) return;
        if (!SignAccess.Local(_sign) || !settings.Valid || !SignSettings.ValidText(text))
        { completion(false, "Check sign access and caption (maximum 1024 characters)."); return; }
        _expectedStyle = expectedStyle; _expectedText = expectedText; _newStyle = settings.Encode(); _newText = text;
        _completion = completion; _pendingToken = Guid.NewGuid().ToString("N");
        _requestedOwner = _view.GetZDO().GetOwner(); _granted = false;
        _requestDeadline = Time.unscaledTime + 8;
        // The owner serializes save attempts before passing ownership, like vanilla containers.
        _view.InvokeRPC(_requestedOwner, RequestRpc, _pendingToken, expectedStyle, expectedText);
    }
    private double Now => ZNet.instance.GetTimeSeconds();
    private bool Busy => EditPolicy.LeaseActive(_view.GetZDO().GetString(LeaseKey, ""),
        _view.GetZDO().GetLong(ExpiryKey, 0) / 1000d, Now);
    private void RequestSave(long sender, string token, string expectedStyle, string expectedText)
    {
        if (!_view.IsOwner() || !Guid.TryParseExact(token, "N", out _) ||
            expectedStyle == null || expectedStyle.Length > SignSettings.RecordLimit || expectedText == null || expectedText.Length > 4096) return;
        string error = EditPolicy.Reject(expectedStyle, Raw, expectedText, NativeText, SignAccess.Sender(_sign, sender), Busy);
        if (error.Length == 0)
        {
            var zdo = _view.GetZDO();
            zdo.Set(LeaseKey, token); zdo.Set(ExpiryKey, (long)((Now + EditPolicy.LeaseSeconds) * 1000));
            zdo.SetOwner(sender);
            if (sender != ZNet.GetUID()) ZDOMan.instance.ForceSendZDO(sender, zdo.m_uid);
        }
        _view.InvokeRPC(sender, ResponseRpc, token, error);
    }
    private void SaveResponse(long sender, string token, string error)
    {
        if (!Saving || token != _pendingToken || sender != _requestedOwner) return;
        if (!string.IsNullOrEmpty(error)) { Finish(false, error); return; }
        // ZDO ownership and the RPC may arrive in either order. Wait for both.
        _granted = true;
    }
    private void Commit()
    {
        string error = EditPolicy.Reject(_expectedStyle, Raw, _expectedText, NativeText, SignAccess.Local(_sign), !Busy);
        if (error.Length != 0) { Finish(false, error); return; }
        try
        {
            _nativeWrite = true;
            // Native SetText preserves authorship, wards, filtering and muted-author behavior.
            _sign.SetText(_newText);
            if (NativeText != _newText) { Finish(false, "Valheim rejected the caption. Check ward access."); return; }
            _preview = null; _previewCaption = null; _rendered = null;
            _view.GetZDO().Set(SettingsKey, _newStyle);
            Render();
            Finish(Raw == _newStyle, Raw == _newStyle ? "Saved." : "Settings could not be verified. Reopen the sign.");
        }
        catch (Exception ex) { Plugin.Log.LogError("Sign save failed: " + ex); Finish(false, "Save failed. Reopen the sign to check its current state."); }
        finally { _nativeWrite = false; }
    }
    internal bool AllowNativeWrite => _nativeWrite || !Busy;
    internal void CancelSave() { if (Saving) Finish(false, "Save cancelled."); }
    private void Finish(bool success, string message)
    {
        if (_view && _view.IsValid() && _view.IsOwner() && _view.GetZDO().GetString(LeaseKey, "") == _pendingToken)
        { _view.GetZDO().Set(LeaseKey, ""); _view.GetZDO().Set(ExpiryKey, 0L); }
        var complete = _completion; _completion = null; _pendingToken = null; _granted = false;
        complete?.Invoke(success, message);
    }
    internal void Preview(SignSettings settings, string caption)
    {
        if (!settings.Valid || !SignSettings.ValidText(caption)) return;
        _preview = settings.Clone(); _previewCaption = caption; _rendered = null; Render();
    }
    internal void EndPreview()
    {
        _preview = null; _previewCaption = null; _rendered = null; Render();
    }
    internal void Render()
    {
        if (!_view || !_view.IsValid()) return;
        string raw = _preview != null ? _preview.Encode() : Raw;
        if (raw == _rendered) return;
        _rendered = raw;
        if (!SignSettings.TryDecode(raw, out var settings))
        { _appearance.Restore(); Plugin.Log.LogWarning("Unrecognized RunicSigns data preserved; sign uses its original appearance."); return; }
        if (raw.Length == 0) { _appearance.Restore(); return; }
        _appearance.Apply(settings, _previewCaption, () => SignAccess.CanView(_sign));
    }
    private void OnDestroy()
    {
        CancelSave(); _appearance?.Dispose();
        if (_view) { _view.Unregister(RequestRpc); _view.Unregister(ResponseRpc); }
    }
}

[HarmonyPatch]
internal static class SignPatches
{
    [HarmonyPostfix, HarmonyPatch(typeof(Sign), "Awake")]
    private static void Attach(Sign __instance) => SignRuntime.Attach(__instance);
    [HarmonyPrefix, HarmonyPatch(typeof(TextInput), nameof(TextInput.RequestText))]
    private static bool Edit(TextReceiver sign)
    {
        if (!(sign is Sign target) || !SignAccess.Eligible(target)) return true;
        SignEditor.Open(target); return false;
    }
    [HarmonyPrefix, HarmonyPatch(typeof(Sign), nameof(Sign.SetText))]
    private static bool ProtectSave(Sign __instance)
    {
        var runtime = __instance.GetComponent<SignRuntime>();
        return !runtime || runtime.AllowNativeWrite;
    }
}

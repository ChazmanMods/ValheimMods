using System;
using HarmonyLib;
using RunicSigns.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RunicSigns.Runtime;

internal sealed class SignRuntime : MonoBehaviour
{
    internal const string SettingsKey = "RunicSigns.Settings.v1";
    private const string LeaseKey = "RunicSigns.SaveLease.v1", ExpiryKey = "RunicSigns.SaveExpiry.v1";
    private const string RequestRpc = "RunicSigns_RequestSave_v1", ResponseRpc = "RunicSigns_SaveResponse_v1";
    private Sign _sign;
    private ZNetView _view;
    private Vector3 _baseScale;
    private Vector3 _baseTextPosition;
    private float _baseFontSize, _baseFontMin, _baseFontMax;
    private bool _baseAutoSize, _baseRichText;
    private Color _baseColor;
    private FontStyles _baseStyle;
    private TextAlignmentOptions _baseAlignment;
    private Image _background;
    private string _rendered;
    private float _next;
    private string _pendingToken, _expectedStyle, _expectedText, _newStyle, _newText;
    private long _requestedOwner;
    private float _requestDeadline;
    private bool _granted, _nativeWrite;
    private Action<bool, string> _completion;
    internal bool Saving => _pendingToken != null;
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
        _baseScale = transform.localScale;
        var text = _sign.m_textWidget;
        if (text)
        {
            _baseFontSize = text.fontSize; _baseFontMin = text.fontSizeMin; _baseFontMax = text.fontSizeMax;
            _baseAutoSize = text.enableAutoSizing; _baseRichText = text.richText;
            _baseColor = text.color; _baseStyle = text.fontStyle; _baseAlignment = text.alignment;
            _baseTextPosition = text.rectTransform.localPosition;
        }
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
        { completion(false, "Check sign access and caption (maximum 256 characters)."); return; }
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
    internal void Render()
    {
        if (!_view || !_view.IsValid()) return;
        string raw = Raw;
        if (raw == _rendered) return;
        _rendered = raw;
        if (!SignSettings.TryDecode(raw, out var settings))
        { Restore(); Plugin.Log.LogWarning("Unrecognized RunicSigns data preserved; sign uses its original appearance."); return; }
        if (raw.Length == 0) { Restore(); return; }
        transform.localScale = _baseScale * settings.Scale;
        var text = _sign.m_textWidget;
        if (!text) return; // Dedicated server still applies physical scale.
        text.richText = false;
        ColorUtility.TryParseHtmlString(LabelColors.Hex[settings.Color], out var color); text.color = color;
        text.fontStyle = (settings.Bold ? FontStyles.Bold : FontStyles.Normal) | (settings.Italic ? FontStyles.Italic : FontStyles.Normal);
        text.alignment = settings.Alignment == 0 ? TextAlignmentOptions.Left : settings.Alignment == 2 ? TextAlignmentOptions.Right : TextAlignmentOptions.Center;
        text.enableAutoSizing = true; text.fontSizeMax = _baseFontSize * settings.TextSize;
        text.fontSizeMin = Mathf.Min(text.fontSizeMax, Mathf.Max(1, _baseFontSize * .25f));
        text.fontSize = text.fontSizeMax;
        // Rect dimensions are text-local units; localPosition is in the parent's space.
        // Apply the text transform's scale and rotation exactly once. The sign/ancestor
        // transforms then carry the offset with the board, including resized/rotated signs.
        var textRect = text.rectTransform;
        var localOffset = Vector2.Scale(textRect.rect.size, new Vector2(settings.Horizontal, settings.Vertical));
        textRect.localPosition = _baseTextPosition + textRect.localRotation *
            Vector3.Scale(textRect.localScale, new Vector3(localOffset.x, localOffset.y, 0));
        if (!_background && settings.Background != 0)
        {
            var go = new GameObject("RunicSignsBackground", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(text.transform.parent, false);
            _background = go.GetComponent<Image>(); _background.raycastTarget = false;
            var rect = _background.rectTransform; var source = text.rectTransform;
            rect.anchorMin = source.anchorMin; rect.anchorMax = source.anchorMax; rect.pivot = source.pivot;
            rect.sizeDelta = source.sizeDelta; rect.localRotation = source.localRotation; rect.localScale = source.localScale;
            rect.localPosition = _baseTextPosition;
            rect.SetSiblingIndex(source.GetSiblingIndex());
        }
        if (_background) { _background.gameObject.SetActive(settings.Background != 0); _background.color = settings.Background == 1 ? Color.white : Color.black; }
    }
    private void Restore()
    {
        transform.localScale = _baseScale;
        if (_background) _background.gameObject.SetActive(false);
        var text = _sign.m_textWidget;
        if (!text) return;
        text.fontSize = _baseFontSize; text.fontSizeMin = _baseFontMin; text.fontSizeMax = _baseFontMax;
        text.enableAutoSizing = _baseAutoSize; text.richText = _baseRichText; text.color = _baseColor;
        text.fontStyle = _baseStyle; text.alignment = _baseAlignment; text.rectTransform.localPosition = _baseTextPosition;
    }
    private void OnDestroy()
    {
        CancelSave(); Restore();
        if (_background) Destroy(_background.gameObject);
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

using System;
using HarmonyLib;
using RunicSigns.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RunicSigns.Runtime;

// A deliberately small editor: vanilla font, no asset bundle, catalog or custom sign pieces.
internal sealed class SignEditor : MonoBehaviour
{
    private static SignEditor _instance;
    private GameObject _canvas;
    private Sign _sign;
    private SignRuntime _runtime;
    private SignSettings _draft;
    private string _expectedStyle, _expectedText;
    private TMP_FontAsset _font;
    private TMP_InputField _caption, _color;
    private TMP_Text _status, _preview, _sizeLabel, _fontLabel, _positionLabel, _colorLabel;
    private Image _previewBackground;
    private Button _save;
    private CanvasGroup _fields;
    private TMP_Text _backgroundLabel, _alignmentLabel, _boldLabel;
    private bool _oldCursorVisible;
    private CursorLockMode _oldCursorLock;
    private bool _closing;
    private static bool _pollingCancel;
    private static int _cancelFrame = -10;
    private static readonly EditorInputState InputState = new();
    internal static bool IsOpen => InputState.IsOpen && _instance && _instance.isActiveAndEnabled &&
        _instance._canvas && _instance._canvas.activeInHierarchy;
    internal static bool BlockGameplay => IsOpen;
    internal static bool SuppressCancel => !_pollingCancel && (IsOpen || Time.frameCount == _cancelFrame);
    private void Awake() { InputState.Reset(); _instance = this; }

    internal static void Open(Sign sign)
    {
        if (!_instance || !SignAccess.Local(sign)) return;
        Close();
        var editor = _instance;
        editor._runtime = SignRuntime.Attach(sign);
        if (!editor._runtime) return;
        editor._expectedStyle = editor._runtime.Raw; editor._expectedText = editor._runtime.NativeText;
        if (!SignSettings.TryDecode(editor._expectedStyle, out editor._draft))
        {
            Player.m_localPlayer.Message(MessageHud.MessageType.Center, "This sign uses unrecognized settings. Its saved data was preserved.");
            return;
        }
        editor._sign = sign;
        // Display the permitted/filtered caption; never expose raw hidden UGC in the editor.
        editor._font = sign.m_textWidget.font;
        editor._oldCursorVisible = Cursor.visible; editor._oldCursorLock = Cursor.lockState;
        try
        {
            editor.Build(sign.GetText());
            editor.Refresh();
            editor._canvas.SetActive(true);
            InputState.Open();
            editor.RenewCursor();
        }
        catch (Exception ex)
        {
            Close();
            Plugin.Log.LogError("Sign editor could not open; gameplay controls released: " + ex);
            Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "The sign editor could not open. Your controls have been restored.");
        }
    }
    private void Update()
    {
        if (!InputState.IsOpen) return;
        try
        {
            if (!IsOpen || !_sign || !_runtime || !SignAccess.Local(_sign)) { Close(); return; }
            _pollingCancel = true;
            bool cancel;
            try { cancel = ZInput.GetKeyDown(KeyCode.Escape, false) || ZInput.GetButtonDown("JoyButtonB"); }
            finally { _pollingCancel = false; }
            if (cancel) { _cancelFrame = Time.frameCount; Close(); return; }
            _fields.interactable = !_runtime.Saving;
            _save.interactable = !_runtime.Saving;
            RenewCursor();
        }
        catch (Exception ex)
        {
            Close();
            Plugin.Log.LogError("Sign editor failed; gameplay controls released: " + ex);
        }
    }
    private void LateUpdate() { if (IsOpen) RenewCursor(); }
    private void RenewCursor() { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
    internal static void KeepCursor() { if (IsOpen) _instance.RenewCursor(); }
    internal static void Close()
    {
        var e = _instance;
        if (!e) { InputState.Reset(); return; }
        if (e._closing) return;
        e._closing = true;
        bool hadCanvas = e._canvas;
        // Release the modal FIRST, even if TMP callbacks, cancellation or Unity cleanup fail.
        InputState.Close(Time.unscaledTime, 0);
        try
        {
            uint held = 0;
            for (int i = 0; i < EditorInputState.ClosingActions.Length; i++)
                if (ZInput.instance?.GetButtonDef(EditorInputState.ClosingActions[i])?.Held == true) held |= 1u << i;
            InputState.Close(Time.unscaledTime, held);
            if (e._runtime) e._runtime.CancelSave();
            if (e._caption) e._caption.DeactivateInputField();
            if (e._color) e._color.DeactivateInputField();
        }
        catch (Exception ex) { Plugin.Log.LogWarning("Sign editor cleanup skipped a failed callback: " + ex.Message); }
        finally
        {
            var canvas = e._canvas;
            e._canvas = null; e._sign = null; e._runtime = null;
            e._caption = e._color = null;
            e._closing = false;
            if (hadCanvas) { Cursor.visible = e._oldCursorVisible; Cursor.lockState = e._oldCursorLock; }
            if (canvas) { canvas.SetActive(false); Destroy(canvas); }
        }
    }
    private void OnDisable() { if (_instance == this) Close(); }
    private void OnDestroy() { Close(); InputState.Reset(); if (_instance == this) _instance = null; }
    internal static bool SuppressClosingAction(string name) => InputState.SuppressClosingAction(name,
        ZInput.instance?.GetButtonDef(name)?.Held == true, Time.unscaledTime);

    private void Build(string caption)
    {
        _canvas = new GameObject("RunicSignsEditor", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        _canvas.SetActive(false);
        var canvas = _canvas.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 2000;
        var scaler = _canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1000, 820); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        var shade = Box(_canvas.transform, "Shade", 0, 0, 0, 0, new Color(0, 0, 0, .7f));
        shade.rectTransform.anchorMin = Vector2.zero; shade.rectTransform.anchorMax = Vector2.one;
        shade.rectTransform.offsetMin = shade.rectTransform.offsetMax = Vector2.zero;
        var panel = Box(_canvas.transform, "Panel", 0, 0, 800, 744, new Color(.085f, .095f, .10f, 1)).rectTransform;
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, .5f);
        Text(panel, "Runic Signs", 28, 18, 600, 40, 28);
        Text(panel, "A caption. A color. The right size.", 28, 61, 744, 28, 17).color = new Color(.7f, .74f, .75f);
        var fields = Rect("Fields", panel, 0, 100, 800, 490);
        _fields = fields.gameObject.AddComponent<CanvasGroup>();
        Text(fields, "Caption", 28, 0, 744, 24);
        _caption = Input(fields, caption, "Write your sign…", 28, 30, 744, 88, SignSettings.TextLimit, true);
        _caption.onValueChanged.AddListener(_ => Refresh());
        _sizeLabel = Text(fields, "", 28, 135, 250, 32);
        Button(fields, "−", 288, 135, 48, 34, () => Adjust(true, -.25f));
        Button(fields, "+", 344, 135, 48, 34, () => Adjust(true, .25f));
        Button(fields, "1×", 400, 135, 62, 34, () => { _draft.Scale = 1; Refresh(); });
        Text(fields, "Whole sign · 0.25× to 4×", 484, 135, 288, 32, 16);
        _fontLabel = Text(fields, "", 28, 181, 250, 32);
        Button(fields, "−", 288, 181, 48, 34, () => Adjust(false, -.1f));
        Button(fields, "+", 344, 181, 48, 34, () => Adjust(false, .1f));
        Button(fields, "1×", 400, 181, 62, 34, () => { _draft.TextSize = 1; Refresh(); });
        Text(fields, "Long captions shrink to fit", 484, 181, 288, 32, 16);
        _colorLabel = Text(fields, "Color", 28, 231, 260, 28);
        _color = Input(fields, LabelColors.Names[_draft.Color], "Color name", 288, 226, 174, 36, 32, false);
        _color.onEndEdit.AddListener(value => {
            _draft.Color = LabelColors.Resolve(value);
            _color.SetTextWithoutNotify(LabelColors.Names[_draft.Color]); Refresh();
        });
        Button(fields, "‹", 484, 226, 44, 36, () => CycleColor(-1));
        Button(fields, "›", 536, 226, 44, 36, () => CycleColor(1));
        _boldLabel = Button(fields, "", 592, 226, 180, 36, () => { _draft.Bold = !_draft.Bold; Refresh(); }).GetComponentInChildren<TMP_Text>();
        _backgroundLabel = Button(fields, "", 28, 276, 358, 38, () => { _draft.Background = (_draft.Background + 1) % 3; Refresh(); }).GetComponentInChildren<TMP_Text>();
        _alignmentLabel = Button(fields, "", 402, 276, 370, 38, () => { _draft.Alignment = (_draft.Alignment + 1) % 3; Refresh(); }).GetComponentInChildren<TMP_Text>();
        _positionLabel = Text(fields, "", 28, 329, 280, 32, 17);
        Button(fields, "←", 316, 329, 48, 34, () => Move(-.05f, 0));
        Button(fields, "→", 372, 329, 48, 34, () => Move(.05f, 0));
        Button(fields, "↓", 428, 329, 48, 34, () => Move(0, -.05f));
        Button(fields, "↑", 484, 329, 48, 34, () => Move(0, .05f));
        Button(fields, "Center", 548, 329, 224, 34, () => { _draft.Horizontal = _draft.Vertical = 0; Refresh(); });
        Text(fields, "Text preview", 28, 382, 744, 24, 16);
        _previewBackground = Box(fields, "Preview", 28, 412, 744, 72, new Color(.2f, .15f, .1f));
        _preview = Text(_previewBackground.transform, "", 12, 2, 720, 68, 28);
        _preview.enableAutoSizing = true; _preview.fontSizeMin = 8;
        _status = Text(panel, "Changes apply when you save. Escape or Cancel discards the draft.", 28, 604, 744, 48, 16);
        _status.textWrappingMode = TextWrappingModes.Normal;
        _save = Button(panel, "Save sign", 28, 670, 358, 44, Save);
        Button(panel, "Cancel", 402, 670, 370, 44, Close);
    }
    private void Adjust(bool board, float delta)
    {
        if (board) _draft.Scale = Mathf.Clamp(Mathf.Round((_draft.Scale + delta) * 100) / 100, .25f, 4);
        else _draft.TextSize = Mathf.Clamp(Mathf.Round((_draft.TextSize + delta) * 100) / 100, .3f, 3);
        Refresh();
    }
    private void Move(float x, float y)
    {
        _draft.Horizontal = Mathf.Clamp(Mathf.Round((_draft.Horizontal + x) * 100) / 100, -.4f, .4f);
        _draft.Vertical = Mathf.Clamp(Mathf.Round((_draft.Vertical + y) * 100) / 100, -.4f, .4f); Refresh();
    }
    private void CycleColor(int direction)
    {
        _draft.Color = (_draft.Color + direction + LabelColors.Names.Length) % LabelColors.Names.Length;
        _color.SetTextWithoutNotify(LabelColors.Names[_draft.Color]); Refresh();
    }
    private void Refresh()
    {
        if (!_preview) return;
        _sizeLabel.text = $"Sign size   {_draft.Scale:0.##}×";
        _fontLabel.text = $"Text size   {_draft.TextSize:0.##}×";
        _colorLabel.text = "Color   " + LabelColors.Names[_draft.Color];
        _backgroundLabel.text = "Background: " + new[] { "Transparent", "White", "Black" }[_draft.Background];
        _alignmentLabel.text = "Align: " + new[] { "Left", "Center", "Right" }[_draft.Alignment];
        _boldLabel.text = "Bold: " + (_draft.Bold ? "On" : "Off");
        _positionLabel.text = $"Text offset   {_draft.Horizontal:0.##}, {_draft.Vertical:0.##}";
        _preview.text = string.IsNullOrEmpty(_caption.text) ? "Your caption" : _caption.text;
        ColorUtility.TryParseHtmlString(LabelColors.Hex[_draft.Color], out var color); _preview.color = color;
        _preview.fontStyle = _draft.Bold ? FontStyles.Bold : FontStyles.Normal;
        _preview.fontSizeMax = 28 * _draft.TextSize;
        _preview.alignment = _draft.Alignment == 0 ? TextAlignmentOptions.Left : _draft.Alignment == 2 ? TextAlignmentOptions.Right : TextAlignmentOptions.Center;
        _previewBackground.color = _draft.Background == 1 ? Color.white : _draft.Background == 2 ? Color.black : new Color(.2f, .15f, .1f);
    }
    private void Save()
    {
        if (!_runtime || _runtime.Saving) return;
        _draft.Color = LabelColors.Resolve(_color.text); Refresh();
        _status.text = "Saving…";
        _runtime.BeginSave(_draft, _caption.text.Replace("\r\n", "\n"), _expectedStyle, _expectedText, (success, message) => {
            if (!_canvas || _closing) return;
            if (success) { Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "Sign saved."); Close(); }
            else _status.text = message;
        });
    }
    private static RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
    {
        var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
        var r = (RectTransform)go.transform; r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1);
        r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(width, height); return r;
    }
    private static Image Box(Transform parent, string name, float x, float y, float w, float h, Color color)
    { var image = Rect(name, parent, x, y, w, h).gameObject.AddComponent<Image>(); image.color = color; return image; }
    private TMP_Text Text(Transform parent, string value, float x, float y, float w, float h, int size = 19)
    {
        var text = Rect("Text", parent, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = _font; text.text = value; text.fontSize = size; text.color = new Color(.94f, .89f, .77f);
        text.richText = false; text.raycastTarget = false; text.alignment = TextAlignmentOptions.MidlineLeft;
        text.overflowMode = TextOverflowModes.Ellipsis; return text;
    }
    private Button Button(Transform parent, string caption, float x, float y, float w, float h, UnityEngine.Events.UnityAction action)
    {
        var image = Box(parent, caption, x, y, w, h, new Color(.22f, .25f, .26f));
        var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image; button.onClick.AddListener(action);
        Text(image.transform, caption, 6, 0, w - 12, h).alignment = TextAlignmentOptions.Center;
        return button;
    }
    private TMP_InputField Input(Transform parent, string value, string hint, float x, float y, float w, float h, int limit, bool multi)
    {
        var image = Box(parent, "Input", x, y, w, h, new Color(.04f, .045f, .05f));
        var input = image.gameObject.AddComponent<TMP_InputField>(); input.targetGraphic = image;
        input.richText = false;
        input.characterLimit = limit; input.lineType = multi ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;
        var area = Rect("Viewport", image.transform, 10, 4, w - 20, h - 8); area.gameObject.AddComponent<RectMask2D>();
        var text = Text(area, "", 0, 0, w - 20, h - 8); text.textWrappingMode = TextWrappingModes.Normal;
        text.alignment = multi ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft;
        var placeholder = Text(area, hint, 0, 0, w - 20, h - 8); placeholder.color = Color.gray;
        input.textViewport = area; input.textComponent = text; input.placeholder = placeholder; input.SetTextWithoutNotify(value);
        return input;
    }
}

[HarmonyPatch(typeof(ZInput), "GetKeyDown", typeof(KeyCode), typeof(bool))]
internal static class SignEscapeGuard
{
    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    private static void Postfix(KeyCode key, ref bool __result)
    { if (key == KeyCode.Escape && SignEditor.SuppressCancel) __result = false; }
}
[HarmonyPatch(typeof(ZInput), "GetButtonDown", typeof(string))]
internal static class SignControllerCancelGuard
{
    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    private static void Postfix(string name, ref bool __result)
    { if (name == "JoyButtonB" && SignEditor.SuppressCancel) __result = false; }
}

[HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateMouseCapture))]
internal static class SignCursorGuard
{
    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    private static void Postfix() => SignEditor.KeepCursor();
}

[HarmonyPatch]
internal static class SignClosingActionGuard
{
    private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        foreach (string name in new[] { "GetButton", "GetButtonDown", "GetButtonUp" })
            yield return AccessTools.Method(typeof(ZInput), name, new[] { typeof(string) });
    }
    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    private static void Postfix(string name, ref bool __result)
    { if (SignEditor.SuppressClosingAction(name)) __result = false; }
}

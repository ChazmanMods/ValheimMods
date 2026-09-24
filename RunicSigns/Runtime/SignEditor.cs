using System;
using Runic.Shared;
using System.Collections.Generic;
using System.Globalization;
using RunicSigns.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RunicSigns.Runtime;

internal sealed class SignEditor : MonoBehaviour
{
    private static SignEditor _instance;
    private static readonly EditorInputState InputState = new();
    private static bool _pollingCancel;
    private static int _cancelFrame = -10;
    private GameObject _canvas, _emojiPicker, _colorPicker, _effectsPicker;
    private Sign _sign;
    private SignRuntime _runtime;
    private SignPlacementPreview _ghost;
    private SignSettings _draft;
    private string _expectedStyle, _expectedText, _text;
    private bool _placement, _closing, _oldCursorVisible, _coarse;
    private CursorLockMode _oldCursorLock;
    private TMP_FontAsset _font;
    private TMP_InputField _caption, _x, _y, _z, _sizeField, _inkField, _opacityField, _curveV, _curveD;
    private TMP_Text _status, _target, _board, _units, _fit, _background, _alignment, _effect, _step;
    private Button _save;
    private CanvasGroup _fields;
    private readonly HashSet<TMP_InputField> _invalid = new();
    private int _start, _end;
    internal static bool IsOpen => InputState.IsOpen && _instance && _instance.isActiveAndEnabled && _instance._canvas && _instance._canvas.activeInHierarchy;
    internal static bool BlockGameplay => IsOpen;
    internal static bool SuppressCancel => !_pollingCancel && (IsOpen || Time.frameCount == _cancelFrame);
    internal static bool IsPlacementTarget(GameObject ghost) => IsOpen && _instance._placement && _instance._sign && _instance._sign.gameObject == ghost;
    private void Awake() { InputState.Reset(); _instance = this; }
    internal static void Open(Sign sign) => OpenEditor(sign, false);
    internal static void OpenPlacement(Sign sign) => OpenEditor(sign, true);
    private static void OpenEditor(Sign sign, bool placement)
    {
        if (!_instance || !sign || !sign.m_textWidget || (!placement && (!SignAccess.Local(sign) || !SignAccess.CanView(sign)))) return;
        Close(); var e = _instance;
        e._sign = sign; e._placement = placement;
        e._oldCursorVisible = Cursor.visible; e._oldCursorLock = Cursor.lockState;
        try {
            if (placement) {
                e._ghost = sign.GetComponent<SignPlacementPreview>(); if (!e._ghost) return;
                e._draft = SignPlacement.CurrentSettings; e._text = SignPlacement.Caption ?? sign.m_defaultText;
            } else {
                e._runtime = SignRuntime.Attach(sign); if (!e._runtime) return;
                e._expectedStyle = e._runtime.Raw; e._expectedText = e._runtime.NativeText;
                if (!SignSettings.TryDecode(e._expectedStyle, out e._draft)) {
                    Player.m_localPlayer?.Message(MessageHud.MessageType.Center, global::Runic.Localization.RunicText.Get("text_7bd0f355b778")); return;
                }
                e._text = sign.GetText(); // permitted/filtered display, never raw hidden text
                if (e._text != e._expectedText) e._draft.Runs.Clear();
            }
            SignFormatting.NormalizeEditorOpacity(e._draft);
            e._font = sign.m_textWidget.font; e._start = e._end = 0; e._coarse = false; e._invalid.Clear();
            e.Build(); e._canvas.SetActive(true); InputState.Open(); e.SyncStyleFields(); e.Refresh(); e.RenewCursor();
        } catch (Exception ex) { Close(); Plugin.Log.LogError("Sign designer could not open: " + ex); }
    }
    private bool Saving => _runtime && _runtime.Saving;
    private void Update()
    {
        if (!InputState.IsOpen) return;
        try {
            if (!IsOpen || !_sign || !Player.m_localPlayer || Player.m_localPlayer.IsDead() ||
                (_placement ? !_ghost || !_sign.gameObject.activeInHierarchy : !_runtime || !SignAccess.Local(_sign) || !SignAccess.CanView(_sign))) { Close(); return; }
            _pollingCancel = true; bool cancel;
            try { cancel = ZInput.GetKeyDown(KeyCode.Escape, false) || ZInput.GetButtonDown("JoyButtonB"); }
            finally { _pollingCancel = false; }
            if (cancel) {
                _cancelFrame = Time.frameCount;
                if (_effectsPicker) { CloseEffects(); }
                else if (_colorPicker) { CloseColors(); }
                else if (_emojiPicker) { _emojiPicker.SetActive(false); Destroy(_emojiPicker); _emojiPicker = null; } else Close();
                return;
            }
            if (_caption.isFocused) CaptureSelection();
            bool popup = _colorPicker || _emojiPicker || _effectsPicker;
            _fields.interactable = !Saving && !popup;
            _save.interactable = !Saving && !popup && _invalid.Count == 0 && _draft.Valid;
            RenewCursor();
        } catch (Exception ex) { Close(); Plugin.Log.LogError("Sign designer failed; controls restored: " + ex); }
    }
    private void LateUpdate() { if (IsOpen) RenewCursor(); }
    private void RenewCursor() { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
    internal static void KeepCursor() { if (IsOpen) _instance.RenewCursor(); }
    internal static bool SuppressClosingAction(string name) => InputState.SuppressClosingAction(name, ZInput.instance?.GetButtonDef(name)?.Held == true, Time.unscaledTime);
    internal static void Close()
    {
        var e = _instance; if (!e) { InputState.Reset(); return; } if (e._closing) return;
        e._closing = true; bool hadCanvas = e._canvas; InputState.Close(Time.unscaledTime, 0);
        try {
            uint held = 0;
            for (int i = 0; i < EditorInputState.ClosingActions.Length; i++)
                if (ZInput.instance?.GetButtonDef(EditorInputState.ClosingActions[i])?.Held == true) held |= 1u << i;
            InputState.Close(Time.unscaledTime, held);
            if (e._runtime) { e._runtime.CancelSave(); e._runtime.EndPreview(); }
            if (e._ghost && Player.m_localPlayer) e._ghost.Apply(SignPlacement.CurrentSettings, SignPlacement.Caption, Player.m_localPlayer);
            if (e._caption) e._caption.DeactivateInputField();
        } catch (Exception ex) { Plugin.Log.LogWarning("Designer cleanup: " + ex.Message); }
        finally {
            var canvas = e._canvas; e._canvas = null; e._sign = null; e._runtime = null; e._ghost = null;
            e._caption = null; e._emojiPicker = null; e._colorPicker = null; e._effectsPicker = null; e._invalid.Clear(); e._closing = false;
            if (hadCanvas) { Cursor.visible = e._oldCursorVisible; Cursor.lockState = e._oldCursorLock; }
            if (canvas) { canvas.SetActive(false); Destroy(canvas); }
        }
    }
    private void OnDisable() { if (_instance == this) Close(); }
    private void OnDestroy() { Close(); InputState.Reset(); if (_instance == this) _instance = null; }

    private void Build()
    {
        _canvas = new GameObject("RunicSignDesigner", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)); _canvas.SetActive(false);
        var canvas = _canvas.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 2000;
        var scaler = _canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1440, 900); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        var shade = Box(_canvas.transform, "Input shield", 0, 0, 0, 0, new Color(0, 0, 0, .04f)).rectTransform;
        shade.anchorMin = Vector2.zero; shade.anchorMax = Vector2.one; shade.offsetMin = shade.offsetMax = Vector2.zero;
        var panel = Box(_canvas.transform, "Designer", 24, 0, 484, 850, new Color(.07f, .085f, .09f, .97f)).rectTransform;
        panel.anchorMin = panel.anchorMax = new Vector2(0, .5f); panel.pivot = new Vector2(0, .5f);
        Text(panel, _placement ? global::Runic.Localization.RunicText.Get("text_0e0052261f59") : global::Runic.Localization.RunicText.Get("text_869cad5c228a"), 18, 12, 448, 34, 25);
        Text(panel, global::Runic.Localization.RunicText.Get("text_db228dce09d4"), 18, 50, 448, 25, 16);
        var fields = Rect("Fields", panel, 0, 82, 484, 668); _fields = fields.gameObject.AddComponent<CanvasGroup>();
        Text(fields, global::Runic.Localization.RunicText.Get("text_87d296ec9489"), 18, 0, 290, 24);
        Button(fields, global::Runic.Localization.RunicText.Get("text_9491fc41c391"), 342, -3, 124, 29, () => { if (!_emojiPicker) _emojiPicker = Runic.Shared.EmojiPicker.Open(_canvas.transform, _caption, _font); });
        _caption = Input(fields, _text, 18, 30, 448, 95, SignSettings.TextLimit, true); Runic.Shared.EmojiInput.Attach(_caption);
        _caption.onDeselect.AddListener(_ => CaptureSelection());
        _caption.onValueChanged.AddListener(value => {
            var normalized = value.Replace("\r\n", "\n").Replace('\r', '\n');
            if (normalized != value) { value = normalized; _caption.SetTextWithoutNotify(value); }
            if (!SignSettings.ValidText(value)) { _invalid.Add(_caption); return; } // TMP may deliver a surrogate pair in two callbacks.
            _invalid.Remove(_caption);
            try { _draft.Runs = SignFormatting.Edit(_text, value, _draft.Runs); _text = value; Refresh(); }
            catch (ArgumentException ex) { _caption.SetTextWithoutNotify(_text); _status.text = ex.Message; }
        });
        _target = Text(fields, global::Runic.Localization.RunicText.Get("text_ad17cd50f8a8"), 18, 130, 320, 25, 14);
        Button(fields, global::Runic.Localization.RunicText.Get("text_1fc9a387654d"), 364, 129, 102, 27, () => {
            _caption.selectionStringAnchorPosition = 0; _caption.selectionStringFocusPosition = _text.Length;
            CaptureSelection();
        });
        Button(fields, global::Runic.Localization.RunicText.Get("text_94fee62e68e2"), 18, 163, 104, 32, () => Style(s => s.Bold = (s.Bold == -1 ? _draft.Bold : s.Bold == 1) ? 0 : 1));
        Button(fields, global::Runic.Localization.RunicText.Get("text_9bf37cb58a69"), 130, 163, 104, 32, () => Style(s => s.Italic = (s.Italic == -1 ? _draft.Italic : s.Italic == 1) ? 0 : 1));
        Text(fields, global::Runic.Localization.RunicText.Get("text_1795efa176d4"), 250, 163, 85, 32, 17);
        _sizeField = Number(fields, _draft.TextSize, 338, 163, 128, .1f, 20, value => Style(s => s.Size = value));
        Text(fields, global::Runic.Localization.RunicText.Get("text_a8516e586340"), 18, 205, 120, 30, 17);
        var ink = _inkField = Input(fields, _draft.Ink == "" ? LabelColors.Names[_draft.Color] : _draft.Ink, 144, 205, 186, 30, 32, false);
        ink.onValueChanged.AddListener(value => {
            if (!SignFormatting.TryColor(value, out var hex)) { _invalid.Add(ink); _status.text = global::Runic.Localization.RunicText.Get("text_00725bb5d8b3"); return; }
            _invalid.Remove(ink); Style(s => s.Ink = hex);
        });
        Text(fields, global::Runic.Localization.RunicText.Get("text_074aeca55852"), 18, 245, 120, 30, 17);
        _opacityField = Number(fields, _draft.Opacity * 100, 144, 245, 90, 0, 100, value => Style(s => {
            string hex = s.Ink != "" ? s.Ink : _draft.Ink != "" ? _draft.Ink : LabelColors.Hex[_draft.Color];
            s.Ink = hex.Substring(0, 7) + ((int)Math.Round(value * 2.55)).ToString("X2");
        }));
        Button(fields, global::Runic.Localization.RunicText.Get("text_aebb29d51c14"), 250, 245, 216, 30, () => {
            SignFormatting.ResetText(_draft);
            _start = _end = 0;
            _caption.selectionStringAnchorPosition = _caption.selectionStringFocusPosition = 0;
            _target.text = global::Runic.Localization.RunicText.Get("text_56daa5d66c4f");
            SyncStyleFields(); Refresh();
            _status.text = global::Runic.Localization.RunicText.Get("text_fd60b1e12fd8");
        });
        Button(fields, global::Runic.Localization.RunicText.Get("text_3a8d02640242"), 18, 282, 448, 30, ShowColors);
        _fit = Button(fields, "", 18, 322, 218, 30, () => { _draft.Fit = !_draft.Fit; Refresh(); }).GetComponentInChildren<TMP_Text>();
        _alignment = Button(fields, "", 248, 322, 218, 30, () => { _draft.Alignment = (_draft.Alignment + 1) % 3; Refresh(); }).GetComponentInChildren<TMP_Text>();
        _background = Button(fields, "", 18, 361, 218, 30, () => { _draft.Background = (_draft.Background + 1) % 3; Refresh(); }).GetComponentInChildren<TMP_Text>();
        _effect = Button(fields, "", 248, 361, 218, 30, ShowEffects).GetComponentInChildren<TMP_Text>();
        _board = Text(fields, "", 18, 401, 218, 30, 17);
        Button(fields, "−", 248, 401, 64, 30, () => Resize(-.25f)); Button(fields, "+", 324, 401, 64, 30, () => Resize(.25f));
        Button(fields, "1×", 400, 401, 66, 30, () => { _draft.Scale = 1; Refresh(); });
        _units = Button(fields, "", 18, 442, 448, 30, ChangeUnits).GetComponentInChildren<TMP_Text>();
        Text(fields, global::Runic.Localization.RunicText.Get("text_0abba441f16e"), 18, 481, 120, 30, 17);
        _x = Number(fields, _draft.Horizontal, 144, 481, 122, -2000, 2000, value => { _draft.Horizontal = value; Refresh(); });
        Text(fields, global::Runic.Localization.RunicText.Get("text_727cd3a64d79"), 18, 520, 120, 30, 17);
        _y = Number(fields, _draft.Vertical, 144, 520, 122, -2000, 2000, value => { _draft.Vertical = value; Refresh(); });
        Button(fields, "←", 282, 481, 85, 30, () => Move(-1, 0)); Button(fields, "→", 381, 481, 85, 30, () => Move(1, 0));
        Button(fields, "↓", 282, 520, 85, 30, () => Move(0, -1)); Button(fields, "↑", 381, 520, 85, 30, () => Move(0, 1));
        Text(fields, global::Runic.Localization.RunicText.Get("text_f1dbc33978a9"), 18, 559, 120, 30, 17);
        _z = Number(fields, _draft.Depth, 144, 559, 122, -2000, 2000, value => { _draft.Depth = value; Refresh(); });
        Button(fields, global::Runic.Localization.RunicText.Get("text_76900f1bfd16"), 282, 559, 85, 30, () => Move(0, 0, -1));
        Button(fields, global::Runic.Localization.RunicText.Get("text_f1c65e14817e"), 381, 559, 85, 30, () => Move(0, 0, 1));
        _step = Button(fields, "", 18, 598, 218, 30, () => { _coarse = !_coarse; Refresh(); }).GetComponentInChildren<TMP_Text>();
        Button(fields, global::Runic.Localization.RunicText.Get("text_e02bdedb2cd9"), 248, 598, 218, 30, () => { _draft.Horizontal = _draft.Vertical = _draft.Depth = 0; UpdateOffsets(); Refresh(); });
        Text(fields, global::Runic.Localization.RunicText.Get("text_4293772467bf"), 18, 634, 448, 32, 14);
        _status = Text(panel, "", 18, 748, 448, 44, 16);
        _save = Button(panel, _placement ? global::Runic.Localization.RunicText.Get("text_7726ebd914f1") : global::Runic.Localization.RunicText.Get("text_7ce42330a8fc"), 18, 800, 218, 34, Save);
        Button(panel, global::Runic.Localization.RunicText.Get("text_19766ed6ccb2"), 248, 800, 218, 34, Close);
    }
    private void CaptureSelection()
    {
        if (!_caption || _closing || _colorPicker) return;
        int previousStart = _start, previousEnd = _end;
        _start = _caption.selectionStringAnchorPosition; _end = _caption.selectionStringFocusPosition;
        SignFormatting.Selection(_text, ref _start, ref _end);
        if (_target) _target.text = _start == _end ? global::Runic.Localization.RunicText.Get("text_ad17cd50f8a8") : global::Runic.Localization.RunicText.Format("text_351e25982bab", _end - _start);
        if (previousStart != _start || previousEnd != _end) SyncStyleFields();
    }
    private void Style(Action<TextStyle> selection)
    {
        try {
            SignFormatting.ApplyEditor(_draft, _text, _start, _end, selection);
            SyncStyleFields(); Refresh();
        } catch (ArgumentException ex) { _status.text = ex.Message; }
    }
    private void CloseColors()
    {
        if (!_colorPicker) return;
        _colorPicker.SetActive(false); Destroy(_colorPicker); _colorPicker = null;
        _fields.interactable = !Saving;
    }
    private void ShowColors()
    {
        if (_colorPicker || Saving) return;
        // Capture the selection before popup buttons take focus. Never re-read it from a color control.
        int start = _start, end = _end;
        string caption = _text;
        var shade = Box(_canvas.transform, "NamedColorPicker", 0, 0, 0, 0, new Color(0, 0, 0, .65f));
        _colorPicker = shade.gameObject;
        shade.rectTransform.anchorMin = Vector2.zero; shade.rectTransform.anchorMax = Vector2.one;
        shade.rectTransform.offsetMin = shade.rectTransform.offsetMax = Vector2.zero;
        var panel = Box(shade.transform, "Colors", 0, 0, 660, 530, new Color(.07f, .085f, .09f)).rectTransform;
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, .5f);
        Text(panel, global::Runic.Localization.RunicText.Get("text_46a8d1ebf257"), 20, 14, 500, 34, 25);
        Button(panel, global::Runic.Localization.RunicText.Get("text_7d9eb7acb13e"), 546, 16, 94, 30, CloseColors);
        string sample = caption.Substring(start, end - start).Replace("\n", " ");
        Text(panel, start == end ? global::Runic.Localization.RunicText.Get("text_41e3fd80fa3d") : global::Runic.Localization.RunicText.Get("text_3fa806630387") + sample + "”", 20, 56, 620, 30, 17);
        for (int i = 0; i < LabelColors.Names.Length; i++) {
            int index = i;
            float x = 20 + i % 3 * 210, y = 100 + i / 3 * 47;
            var button = Button(panel, "", x, y, 200, 40, () => {
                CloseColors();
                if (_text != caption) { _status.text = global::Runic.Localization.RunicText.Get("text_022d7444f525"); return; }
                _start = start; _end = end;
                Style(style => style.Ink = LabelColors.Hex[index]);
                _inkField.SetTextWithoutNotify(LabelColors.Names[index]); _invalid.Remove(_inkField);
                _status.text = LabelColors.Names[index] + (start == end ? global::Runic.Localization.RunicText.Get("text_93d085f09127") : global::Runic.Localization.RunicText.Get("text_5d2783bbe912"));
            });
            ColorUtility.TryParseHtmlString(LabelColors.Hex[index], out var color);
            var swatch = Box(button.transform, "Swatch", 9, 8, 24, 24, color); swatch.raycastTarget = false;
            Text(button.transform, LabelColors.Names[index], 43, 0, 150, 40, 18);
        }
        Text(panel, global::Runic.Localization.RunicText.Get("text_8cbe2e50c200"), 20, 489, 620, 25, 16);
        _fields.interactable = false; _save.interactable = false;
    }
    private void CloseEffects()
    {
        if (!_effectsPicker) return;
        _invalid.Remove(_curveV); _invalid.Remove(_curveD);
        _effectsPicker.SetActive(false); Destroy(_effectsPicker); _effectsPicker = null;
        _curveV = _curveD = null; _fields.interactable = !Saving;
        Refresh();
    }
    private void ShowEffects()
    {
        if (_effectsPicker || Saving) return;
        var shade = Box(_canvas.transform, "TextEffects", 0, 0, 0, 0, new Color(0, 0, 0, .65f));
        _effectsPicker = shade.gameObject;
        shade.rectTransform.anchorMin = Vector2.zero; shade.rectTransform.anchorMax = Vector2.one;
        shade.rectTransform.offsetMin = shade.rectTransform.offsetMax = Vector2.zero;
        var panel = Box(shade.transform, "Effects", 0, 0, 660, 410, new Color(.07f, .085f, .09f)).rectTransform;
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, .5f);
        Text(panel, global::Runic.Localization.RunicText.Get("text_b6a04be45d40"), 20, 14, 490, 34, 25);
        Button(panel, global::Runic.Localization.RunicText.Get("text_11a6767d5674"), 546, 16, 94, 30, CloseEffects);
        Text(panel, global::Runic.Localization.RunicText.Get("text_5da37bd17aa1"), 20, 56, 620, 30, 17);
        TMP_Text outline = null, shadow = null;
        void Labels() {
            outline.text = global::Runic.Localization.RunicText.Get("text_43d3183fc46b") + ((_draft.Effect & 1) != 0 ? global::Runic.Localization.RunicText.Get("text_130011756125") : global::Runic.Localization.RunicText.Get("text_ca7981b46ecf"));
            shadow.text = global::Runic.Localization.RunicText.Get("text_febfba761071") + ((_draft.Effect & 2) != 0 ? global::Runic.Localization.RunicText.Get("text_130011756125") : global::Runic.Localization.RunicText.Get("text_ca7981b46ecf"));
        }
        outline = Button(panel, "", 20, 100, 300, 36, () => { _draft.Effect ^= 1; Labels(); Refresh(); }).GetComponentInChildren<TMP_Text>();
        shadow = Button(panel, "", 340, 100, 300, 36, () => { _draft.Effect ^= 2; Labels(); Refresh(); }).GetComponentInChildren<TMP_Text>();
        Labels();
        Text(panel, global::Runic.Localization.RunicText.Get("text_9ff470f1393a"), 20, 158, 280, 30, 20);
        _curveV = CurveNumber(panel, 158, () => _draft.CurveVertical, v => _draft.CurveVertical = v);
        Text(panel, global::Runic.Localization.RunicText.Get("text_f2639c60f10b"), 20, 194, 620, 26, 17);
        Text(panel, global::Runic.Localization.RunicText.Get("text_c35dca4d4d30"), 20, 238, 370, 30, 20);
        _curveD = CurveNumber(panel, 238, () => _draft.CurveDepth, v => _draft.CurveDepth = v);
        Text(panel, global::Runic.Localization.RunicText.Get("text_0432312ecc2d"), 20, 274, 620, 26, 17);
        Text(panel, global::Runic.Localization.RunicText.Get("text_f79f5f759f39"), 20, 310, 620, 26, 17);
        Button(panel, global::Runic.Localization.RunicText.Get("text_844efb5d0703"), 20, 356, 620, 34, () => {
            _draft.Effect = 0; _draft.CurveVertical = _draft.CurveDepth = 0;
            _curveV.SetTextWithoutNotify("0"); _curveD.SetTextWithoutNotify("0");
            _invalid.Remove(_curveV); _invalid.Remove(_curveD); Labels(); Refresh();
        });
        _fields.interactable = false; _save.interactable = false;
    }
    private TMP_InputField CurveNumber(Transform panel, float y, Func<float> read, Action<float> write)
    {
        var field = Number(panel, CaptionCurve.ToDisplay(read()), 455, y, 140,
            -CaptionCurve.DisplayLimit, CaptionCurve.DisplayLimit,
            value => { write(CaptionCurve.FromDisplay(value)); Refresh(); });
        void Nudge(int direction) {
            write(CaptionCurve.Step(read(), direction));
            field.SetTextWithoutNotify(F(CaptionCurve.ToDisplay(read())));
            _invalid.Remove(field); Refresh();
        }
        Button(panel, "−", 410, y, 38, 30, () => Nudge(-1));
        Button(panel, "+", 602, y, 38, 30, () => Nudge(1));
        return field;
    }
    private void SyncStyleFields()
    {
        var style = _start == _end ? null : _draft.Runs.Find(r => r.Start <= _start && r.Start + r.Length > _start)?.Style;
        string ink = style != null && style.Ink != "" ? style.Ink : _draft.Ink != "" ? _draft.Ink : LabelColors.Hex[_draft.Color];
        SetField(_sizeField, F(_start == _end ? _draft.TextSize : style != null && style.Size != 0 ? style.Size : 1));
        SetField(_inkField, ink);
        SetField(_opacityField, F(Convert.ToInt32(ink.Substring(7, 2), 16) / 2.55f));
    }
    private void SetField(TMP_InputField field, string value)
    {
        if (!field || field.isFocused) return;
        field.SetTextWithoutNotify(value); _invalid.Remove(field);
    }
    private void Resize(float delta) { _draft.Scale = Mathf.Clamp(_draft.Scale + delta, .25f, 4); Refresh(); }
    private void Move(int x, int y, int z = 0)
    {
        float step = _draft.OffsetUnit == 0 ? (_coarse ? 1 : .05f) : (_coarse ? 25 : 1);
        _draft.Horizontal = Mathf.Clamp(_draft.Horizontal + x * step, -_draft.OffsetLimit, _draft.OffsetLimit);
        _draft.Vertical = Mathf.Clamp(_draft.Vertical + y * step, -_draft.OffsetLimit, _draft.OffsetLimit);
        _draft.Depth = Mathf.Clamp(_draft.Depth + z * step, -_draft.OffsetLimit, _draft.OffsetLimit);
        UpdateOffsets(); Refresh();
    }
    private void ChangeUnits()
    {
        var text = _sign.m_textWidget; var size = text.rectTransform.rect.size; float factor = text.isOrthographic ? 1 : .1f;
        if (size.x == 0 || size.y == 0) return;
        _draft.Horizontal *= _draft.OffsetUnit == 0 ? size.x / factor : factor / size.x;
        _draft.Depth *= _draft.OffsetUnit == 0 ? size.x / factor : factor / size.x;
        _draft.Vertical *= _draft.OffsetUnit == 0 ? size.y / factor : factor / size.y;
        _draft.OffsetUnit = 1 - _draft.OffsetUnit;
        _draft.Horizontal = Mathf.Clamp(_draft.Horizontal, -_draft.OffsetLimit, _draft.OffsetLimit);
        _draft.Vertical = Mathf.Clamp(_draft.Vertical, -_draft.OffsetLimit, _draft.OffsetLimit);
        _draft.Depth = Mathf.Clamp(_draft.Depth, -_draft.OffsetLimit, _draft.OffsetLimit);
        UpdateOffsets(); Refresh();
    }
    private void UpdateOffsets() { _x.SetTextWithoutNotify(F(_draft.Horizontal)); _y.SetTextWithoutNotify(F(_draft.Vertical)); _z.SetTextWithoutNotify(F(_draft.Depth)); _invalid.Remove(_x); _invalid.Remove(_y); _invalid.Remove(_z); }
    private void Refresh()
    {
        if (!_status || _closing) return;
        _board.text = global::Runic.Localization.RunicText.Format("text_9895776fef34", _draft.Scale); _fit.text = _draft.Fit ? global::Runic.Localization.RunicText.Get("text_eab84da1acb4") : global::Runic.Localization.RunicText.Get("text_63388322506f");
        _alignment.text = global::Runic.Localization.RunicText.Get("text_c688f6e0cbe9") + new[] { "Left", "Center", "Right" }[_draft.Alignment];
        _background.text = global::Runic.Localization.RunicText.Get("text_79aa081ebba8") + new[] { "Transparent", "White", "Black" }[_draft.Background];
        _effect.text = global::Runic.Localization.RunicText.Get("text_b21fe67c9c5c");
        _units.text = _draft.OffsetUnit == 0 ? global::Runic.Localization.RunicText.Get("text_53932b515723") : global::Runic.Localization.RunicText.Get("text_cf444777435f");
        _step.text = _coarse ? global::Runic.Localization.RunicText.Get("text_f6d39c92cdcf") : global::Runic.Localization.RunicText.Get("text_7ed52beb0793");
        if (!_draft.Valid) { _status.text = global::Runic.Localization.RunicText.Get("text_5ecbaa9cbe14"); _save.interactable = false; return; }
        _status.text = _invalid.Count > 0 ? global::Runic.Localization.RunicText.Get("text_82b7dd75cfe2") : global::Runic.Localization.RunicText.Get("text_072793f8c89d") + (_placement ? global::Runic.Localization.RunicText.Get("text_16e0f36da477") : global::Runic.Localization.RunicText.Get("text_eafca176ed93"));
        _save.interactable = !Saving && !_effectsPicker && !_colorPicker && !_emojiPicker && _invalid.Count == 0;
        if (_placement) _ghost.ShowDraft(_draft, _text); else _runtime.Preview(_draft, _text);
    }
    private void Save()
    {
        if (Saving || _invalid.Count != 0 || !_draft.Valid || !SignSettings.ValidText(_text)) return;
        if (_placement) { SignPlacement.UseDraft(_draft, _text); Close(); return; }
        _status.text = global::Runic.Localization.RunicText.Get("text_23e39291d613");
        _runtime.BeginSave(_draft, _text, _expectedStyle, _expectedText, (success, message) => {
            if (!_canvas || _closing) return; if (success) Close(); else _status.text = message;
        });
    }
    private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private TMP_InputField Number(Transform parent, float value, float x, float y, float width, float min, float max, Action<float> change)
    {
        var input = Input(parent, F(value), x, y, width, 30, 16, false);
        input.onValueChanged.AddListener(raw => {
            if (!(float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || float.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out number)) || !SignSettings.InRange(number, min, max)) {
                _invalid.Add(input); if (_status) _status.text = global::Runic.Localization.RunicText.Format("text_01eb476d9144", min, max); return;
            }
            _invalid.Remove(input); change(number);
        }); return input;
    }
    private static RectTransform Rect(string name, Transform parent, float x, float y, float w, float h)
    {
        var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
        var r = (RectTransform)go.transform; r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1);
        r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h); return r;
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
        Text(image.transform, caption, 5, 0, w - 10, h, 17).alignment = TextAlignmentOptions.Center; return button;
    }
    private TMP_InputField Input(Transform parent, string value, float x, float y, float w, float h, int limit, bool multi)
    {
        var image = Box(parent, "Input", x, y, w, h, new Color(.04f, .045f, .05f));
        var input = image.gameObject.AddComponent<TMP_InputField>(); input.targetGraphic = image; input.richText = false;
        input.resetOnDeActivation = false; input.onFocusSelectAll = false;
        input.characterLimit = limit; input.lineType = multi ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;
        var area = Rect("Viewport", image.transform, 8, 3, w - 16, h - 6); area.gameObject.AddComponent<RectMask2D>();
        var text = Text(area, "", 0, 0, w - 16, h - 6); text.textWrappingMode = TextWrappingModes.Normal;
        text.alignment = multi ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft;
        input.textViewport = area; input.textComponent = text; input.SetTextWithoutNotify(value); return input;
    }
}

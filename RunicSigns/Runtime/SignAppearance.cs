using System;
using RunicSigns.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RunicSigns.Runtime;

// One appearance implementation for ghost, local draft and synchronized world sign.
internal sealed class SignAppearance : IDisposable
{
    private readonly Transform _root;
    private readonly TextMeshProUGUI _text;
    private readonly Vector3 _scale, _position;
    private readonly float _font, _min, _max;
    private readonly bool _auto, _rich, _overrideColorTags;
    private readonly TextWrappingModes _wrapping;
    private float _curveVertical, _curveDepth;
    private readonly Color _color;
    private readonly FontStyles _style;
    private readonly TextAlignmentOptions _alignment;
    private readonly TextOverflowModes _overflow;
    private readonly ITextPreprocessor _preprocessor;
    private readonly Material _baseMaterial;
    private readonly Formatter _formatter = new();
    private Material _effectMaterial;
    private int _effect = -1;
    private Image _background;
    private int _backgroundMode;
    internal SignAppearance(Transform root, TextMeshProUGUI text)
    {
        _root = root; _text = text; _scale = root.localScale;
        if (!text) return;
        Runic.Shared.EmojiRenderer.Attach(text);
        _position = text.rectTransform.localPosition;
        _wrapping = text.textWrappingMode;
        text.OnPreRenderText += BendText;
        _font = text.fontSize; _min = text.fontSizeMin; _max = text.fontSizeMax;
        _auto = text.enableAutoSizing; _rich = text.richText; _color = text.color;
        _overrideColorTags = text.overrideColorTags;
        _style = text.fontStyle; _alignment = text.alignment; _overflow = text.overflowMode;
        _preprocessor = text.textPreprocessor; _baseMaterial = text.fontSharedMaterial;
    }
    internal void Apply(SignSettings s, string draft = null, Func<bool> permitted = null)
    {
        _root.localScale = _scale * s.Scale;
        if (!_text) return;
        _formatter.Settings = s; _formatter.Draft = draft; _formatter.Permitted = permitted;
        _text.textPreprocessor = _formatter;
        _text.richText = true; _text.fontStyle = FontStyles.Normal; _text.color = Color.white;
        _text.overrideColorTags = false;
        _text.alignment = s.Alignment == 0 ? TextAlignmentOptions.Left : s.Alignment == 2 ? TextAlignmentOptions.Right : TextAlignmentOptions.Center;
        _text.enableAutoSizing = s.Fit; _text.fontSizeMax = _font * s.TextSize;
        _text.fontSizeMin = Mathf.Min(_text.fontSizeMax, Mathf.Max(1, _font * .25f));
        _text.fontSize = _text.fontSizeMax;
        _text.overflowMode = TextOverflowModes.Overflow;
        _text.textWrappingMode = s.Fit ? _wrapping : TextWrappingModes.NoWrap;
        _curveVertical = s.CurveVertical; _curveDepth = s.CurveDepth;
        var rect = _text.rectTransform;
        var units = s.OffsetUnit == 0 ? rect.rect.size : new Vector2(_text.isOrthographic ? 1 : .1f, _text.isOrthographic ? 1 : .1f);
        var offset = Vector2.Scale(units, new Vector2(s.Horizontal, s.Vertical));
        rect.localPosition = _position + rect.localRotation * Vector3.Scale(rect.localScale, new Vector3(offset.x, offset.y, -units.x * s.Depth));
        _backgroundMode = s.Background;
        if (!_background && s.Background != 0) {
            var go = new GameObject("RunicSignsBackground", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(_text.transform.parent, false);
            _background = go.GetComponent<Image>(); _background.raycastTarget = false;
            var bg = _background.rectTransform;
            bg.anchorMin = rect.anchorMin; bg.anchorMax = rect.anchorMin; bg.pivot = new Vector2(.5f, .5f);
            bg.sizeDelta = rect.sizeDelta; bg.localRotation = rect.localRotation; bg.localScale = rect.localScale;
            bg.SetSiblingIndex(rect.GetSiblingIndex());
        }
        if (_background) {
            _background.gameObject.SetActive(s.Background != 0);
            _background.rectTransform.localPosition = rect.localPosition;
            _background.color = s.Background == 1 ? Color.white : Color.black;
        }
        SetEffect(s.Effect);
        _text.havePropertiesChanged = true;
        _text.SetAllDirty();
    }
    private void SetEffect(int effect)
    {
        if (_effect == effect) return; _effect = effect;
        if (_effectMaterial) UnityEngine.Object.Destroy(_effectMaterial);
        _effectMaterial = null; _text.fontSharedMaterial = _baseMaterial;
        if (!_baseMaterial) return;
        _effectMaterial = new Material(_baseMaterial);
        // TMP multiplies rich-text colors by the font material's face tint.
        // Use a private neutral material even when no outline/shadow is requested.
        if (_effectMaterial.HasProperty("_FaceColor")) _effectMaterial.SetColor("_FaceColor", Color.white);
        if ((effect & 1) != 0 && _effectMaterial.HasProperty("_OutlineWidth")) {
            _effectMaterial.EnableKeyword("OUTLINE_ON");
            _effectMaterial.SetFloat("_OutlineWidth", .16f); _effectMaterial.SetColor("_OutlineColor", Color.black);
        }
        if ((effect & 2) != 0 && _effectMaterial.HasProperty("_UnderlayOffsetX")) {
            _effectMaterial.EnableKeyword("UNDERLAY_ON");
            _effectMaterial.SetColor("_UnderlayColor", new Color(0, 0, 0, .8f));
            _effectMaterial.SetFloat("_UnderlayOffsetX", .7f); _effectMaterial.SetFloat("_UnderlayOffsetY", -.7f);
            _effectMaterial.SetFloat("_UnderlaySoftness", .25f);
        }
        _text.fontSharedMaterial = _effectMaterial;
        _text.UpdateMeshPadding();
    }
    // TMP calls this on fresh geometry before uploading every mesh, including emoji submeshes.
    private void BendText(TMP_TextInfo info)
    {
        if (!Runic.Shared.CaptionMeshCurve.Apply(info, _curveVertical, _curveDepth, out var min, out var max))
        { if (_background) _background.gameObject.SetActive(false); return; }
        float minX = min.x, minY = min.y, maxX = max.x, maxY = max.y, maxZ = max.z;
        if (_background && _backgroundMode != 0) {
            var rect = _text.rectTransform; var bg = _background.rectTransform;
            float padding = Math.Max(1, _text.fontSize * .15f);
            bg.sizeDelta = new Vector2(maxX - minX + padding * 2, maxY - minY + padding * 2);
            var center = new Vector3((minX + maxX) * .5f, (minY + maxY) * .5f, maxZ + .01f);
            bg.localPosition = rect.localPosition + rect.localRotation * Vector3.Scale(rect.localScale, center);
            _background.gameObject.SetActive(true);
        }
    }
    internal void Restore()
    {
        if (_root) _root.localScale = _scale;
        _backgroundMode = 0;
        if (_background) _background.gameObject.SetActive(false);
        if (!_text) return;
        _text.textPreprocessor = _preprocessor; _text.fontSize = _font; _text.fontSizeMin = _min; _text.fontSizeMax = _max;
        _curveVertical = _curveDepth = 0;
        _text.textWrappingMode = _wrapping;
        _text.enableAutoSizing = _auto; _text.richText = _rich; _text.color = _color;
        _text.overrideColorTags = _overrideColorTags;
        _text.fontStyle = _style; _text.alignment = _alignment; _text.overflowMode = _overflow;
        _text.rectTransform.localPosition = _position; _text.fontSharedMaterial = _baseMaterial;
        _effect = -1; _text.havePropertiesChanged = true; _text.SetAllDirty();
    }
    public void Dispose()
    {
        Restore(); if (_text) _text.OnPreRenderText -= BendText;
        if (_background) UnityEngine.Object.Destroy(_background.gameObject);
        if (_effectMaterial) UnityEngine.Object.Destroy(_effectMaterial);
    }
    private sealed class Formatter : ITextPreprocessor
    {
        internal SignSettings Settings;
        internal string Draft;
        internal Func<bool> Permitted;
        public string PreprocessText(string text) => SignFormatting.Render(
            Draft != null && (Permitted == null || Permitted()) ? Draft : text, Settings);
    }
}

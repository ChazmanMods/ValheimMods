using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Runic.Shared;

internal sealed class CaptionBend : MonoBehaviour
{
    private TextMeshProUGUI _text;
    private Image _background;
    private Vector3 _backgroundPosition;
    private float _vertical, _depth;
    private float _wrapRadius, _wrapDepthRadius;

    internal static CaptionBend Attach(TextMeshProUGUI text, Image background = null)
    {
        var bend = text.gameObject.AddComponent<CaptionBend>();
        bend._text = text; bend._background = background;
        if (background) bend._backgroundPosition = background.rectTransform.localPosition;
        text.OnPreRenderText += bend.Render;
        return bend;
    }
    internal void Set(float vertical, float depth)
    {
        _vertical = vertical; _depth = depth;
        _text.havePropertiesChanged = true;
        _text.SetAllDirty();
    }
    internal void SetSurfaceWrap(float radius, float depthRadius)
    {
        _wrapRadius = radius; _wrapDepthRadius = depthRadius;
        _text.havePropertiesChanged = true;
        _text.SetAllDirty();
    }
    private void Render(TMP_TextInfo info)
    {
        if (!CaptionMeshCurve.Apply(info, _vertical, _depth, out _, out var max,
                _wrapRadius, _wrapDepthRadius)) return;
        // A backward bend must not disappear behind its own opaque backing.
        if (_background) {
            var text = _text.rectTransform;
            _background.rectTransform.localPosition = _backgroundPosition +
                text.localRotation * Vector3.Scale(text.localScale, new Vector3(0, 0,
                    _depth == 0 && _wrapRadius == 0 ? 0 : Mathf.Max(0, max.z) + .01f));
        }
    }
    private void OnDestroy() { if (_text) _text.OnPreRenderText -= Render; }
}

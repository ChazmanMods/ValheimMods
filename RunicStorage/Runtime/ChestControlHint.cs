using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RunicStorage.Runtime;

// Non-interactive hover/focus help; each control owns and disposes its popup.
internal sealed class ChestControlHint : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler, IPointerDownHandler
{
    internal string Help;
    internal TMP_FontAsset Font;
    private bool _hovered, _selected;
    private float _start;
    private GameObject _popup;
    private RectTransform _box;
    private static ChestControlHint _active;
    private void Activate() { if (_active && _active != this) _active.Hide(); _active = this; _start = Time.unscaledTime; }
    public void OnPointerEnter(PointerEventData e) { _hovered = true; Activate(); }
    public void OnPointerExit(PointerEventData e) { _hovered = false; Hide(); }
    public void OnSelect(BaseEventData e) { _selected = true; Activate(); }
    public void OnDeselect(BaseEventData e) { _selected = false; Hide(); }
    public void OnPointerDown(PointerEventData e) { Hide(); _start = Time.unscaledTime + .5f; }
    private void Update()
    {
        if (_active != this || (!_hovered && !_selected) || string.IsNullOrEmpty(Help) || Time.unscaledTime - _start < .4f) return;
        if (!_popup) {
            _popup = new GameObject("RunicChestHelp", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            _popup.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            _popup.GetComponent<Canvas>().sortingOrder = 32000;
            var scaler = _popup.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 800); scaler.matchWidthOrHeight = 1;
            var box = new GameObject("Help", typeof(RectTransform), typeof(Image)); box.transform.SetParent(_popup.transform, false);
            _box = (RectTransform)box.transform; _box.pivot = Vector2.zero;
            var bg = box.GetComponent<Image>(); bg.color = new Color(.06f, .04f, .025f, .98f); bg.raycastTarget = false;
            var label = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI)); label.transform.SetParent(_box, false);
            var text = label.GetComponent<TextMeshProUGUI>(); text.font = Font; text.fontSize = 18; text.richText = false;
            text.color = new Color(1, .9f, .7f); text.text = Help; text.raycastTarget = false;
            float height = Mathf.Clamp(text.GetPreferredValues(Help, 330, 1000).y + 24, 54, 240);
            _box.sizeDelta = new Vector2(354, height);
            var rect = text.rectTransform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = new Vector2(12, 12); rect.offsetMax = new Vector2(-12, -12);
        }
        var root = (RectTransform)_popup.transform;
        Vector2 screen = _hovered ? (Vector2)UnityEngine.Input.mousePosition : (Vector2)RectTransformUtility.WorldToScreenPoint(null, transform.position);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out var point);
        point += new Vector2(18, 18);
        point.x = Mathf.Clamp(point.x, root.rect.xMin + 8, root.rect.xMax - _box.rect.width - 8);
        point.y = Mathf.Clamp(point.y, root.rect.yMin + 8, root.rect.yMax - _box.rect.height - 8);
        _box.anchoredPosition = point;
    }
    private void Hide() { if (_popup) Destroy(_popup); _popup = null; }
    private void OnDisable() { _hovered = _selected = false; Hide(); }
    private void OnDestroy() => Hide();
}

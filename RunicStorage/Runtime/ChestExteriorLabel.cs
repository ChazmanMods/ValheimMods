using System;
using System.Collections.Generic;
using System.Linq;
using RunicStorage.Engine;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RunicStorage.Runtime;

internal sealed class ChestExteriorLabel : MonoBehaviour
{
    private Container _chest;
    private readonly List<GameObject> _labels = new();
    private string _last;
    private float _next;
    private bool _learnPending;
    internal void RequestLearning() => _learnPending = true;
    internal static string AutomaticText(ChestRules rules) => ChestLabelLayout.AutomaticText(rules, DisplayName);
    internal static string DisplayName(string id)
    {
        var prefab = ObjectDB.instance ? ObjectDB.instance.GetItemPrefab(id) : null;
        var drop = prefab ? prefab.GetComponent<ItemDrop>() : null;
        string raw = drop ? drop.m_itemData?.m_shared?.m_name : null;
        string display = !string.IsNullOrWhiteSpace(raw) && Localization.instance != null ? Localization.instance.Localize(raw) : raw;
        return string.IsNullOrWhiteSpace(display) || display.StartsWith("$") || display.StartsWith("[") ? id : display;
    }
    private void Awake() { _chest = GetComponent<Container>(); }
    private void Update()
    {
        if (_learnPending)
        {
            _learnPending = false;
            try { ChestRuleStore.Learn(_chest); }
            catch (Exception ex) { Plugin.Log?.LogWarning("Chest memory update skipped: " + ex.Message); }
        }
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + .75f;
        if (!(PluginConfig.Enabled?.Value ?? false) || !Player.m_localPlayer || Vector3.Distance(transform.position, Player.m_localPlayer.transform.position) > 40f)
        { SetVisible(false); return; }
        string raw = ChestRuleStore.Raw(_chest);
        if (raw == _last) { SetVisible(true); return; }
        _last = raw;
        ClearLabels();
        if (!ChestRules.TryDecode(raw, out var rules) || !rules.ShowLabel) return;
        try { Create(rules); }
        catch (Exception ex) { Plugin.Log?.LogWarning("Chest label unavailable: " + ex.Message); }
    }
    private void Create(ChestRules rules)
    {
        if (rules.Side == 4 && _chest.m_closed) {
            // Read the CLOSED lid even while its GameObject is inactive. When open/closed
            // variants share a mesh, place the same local label on both mesh transforms.
            // Native SetActive/animation then owns visibility and movement of each label.
            var lid = _chest.m_closed.GetComponentsInChildren<MeshFilter>(true)
                .Where(UsableMesh).OrderByDescending(f => f.sharedMesh.bounds.size.x * f.sharedMesh.bounds.size.z).FirstOrDefault();
            if (lid) {
                var bounds = TransformBounds(lid.sharedMesh.bounds, lid.transform);
                var closedLabel = CreateLabel(rules, bounds, lid.transform);
                if (_chest.m_open && _chest.m_open != _chest.m_closed) {
                    var openLid = _chest.m_open.GetComponentsInChildren<MeshFilter>(true)
                        .FirstOrDefault(f => UsableMesh(f) && f.sharedMesh == lid.sharedMesh);
                    if (openLid) {
                        var openLabel = CreateLabel(rules, bounds, openLid.transform);
                        openLabel.transform.localPosition = closedLabel.transform.localPosition;
                        openLabel.transform.localRotation = closedLabel.transform.localRotation;
                        openLabel.transform.localScale = closedLabel.transform.localScale;
                    }
                }
                return;
            }
        }
        // Use physical body geometry, never the currently-open lid, snow caps, particles,
        // neighboring containers, or labels created during a previous save.
        Bounds body = default;
        bool found = false;
        foreach (var collider in _chest.GetComponentsInChildren<Collider>(true)) {
            if (collider.isTrigger || !BodyTransform(collider.transform)) continue;
            if (collider is MeshCollider mesh && mesh.sharedMesh)
                Include(ref body, ref found, TransformBounds(mesh.sharedMesh.bounds, mesh.transform));
            else if (collider is BoxCollider box)
                Include(ref body, ref found, TransformBounds(new Bounds(box.center, box.size), box.transform));
        }
        if (!found) foreach (var mesh in _chest.GetComponentsInChildren<MeshFilter>(true))
            if (UsableMesh(mesh) && BodyTransform(mesh.transform))
                Include(ref body, ref found, TransformBounds(mesh.sharedMesh.bounds, mesh.transform));
        if (!found) throw new InvalidOperationException("No stable chest body geometry was found.");
        CreateLabel(rules, body, transform);
    }
    private bool UsableMesh(MeshFilter mesh) => mesh && mesh.sharedMesh &&
        mesh.GetComponentInParent<Container>() == _chest && !mesh.GetComponentInParent<Canvas>() &&
        !mesh.GetComponentInParent<TMP_Text>();
    private bool BodyTransform(Transform candidate)
    {
        if (candidate.GetComponentInParent<Container>() != _chest || candidate.GetComponentInParent<Canvas>()) return false;
        if ((_chest.m_open && candidate.IsChildOf(_chest.m_open.transform)) ||
            (_chest.m_closed && candidate.IsChildOf(_chest.m_closed.transform))) return false;
        for (var node = candidate; node && node != transform; node = node.parent)
            if (node.name.IndexOf("snow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                node.name.IndexOf("attach", StringComparison.OrdinalIgnoreCase) >= 0) return false;
        return true;
    }
    private Bounds TransformBounds(Bounds mesh, Transform source)
    {
        Bounds result = default; bool found = false;
        for (int mask = 0; mask < 8; mask++) {
            var corner = mesh.center + Vector3.Scale(mesh.extents, new Vector3((mask & 1) == 0 ? -1 : 1,
                (mask & 2) == 0 ? -1 : 1, (mask & 4) == 0 ? -1 : 1));
            var point = transform.InverseTransformPoint(source.TransformPoint(corner));
            if (!found) { result = new Bounds(point, Vector3.zero); found = true; } else result.Encapsulate(point);
        }
        return result;
    }
    private static void Include(ref Bounds total, ref bool found, Bounds next)
    { if (found) total.Encapsulate(next); else { total = next; found = true; } }
    private GameObject CreateLabel(ChestRules rules, Bounds bounds, Transform anchor)
    {
        var faceNormal = ChestLabelFaces.Normal(rules.Side); var faceUp = ChestLabelFaces.Up(rules.Side);
        Vector3 normal = new Vector3(faceNormal.X, faceNormal.Y, faceNormal.Z);
        Vector3 up = new Vector3(faceUp.X, faceUp.Y, faceUp.Z);
        Quaternion rotation = Quaternion.LookRotation(-normal, up);
        var position = ChestLabelFaces.Position(rules.Side,
            new System.Numerics.Vector3(bounds.center.x, bounds.center.y, bounds.center.z),
            new System.Numerics.Vector3(bounds.extents.x, bounds.extents.y, bounds.extents.z), rules.Horizontal, rules.Vertical);
        float width = rules.Side == 2 || rules.Side == 3 ? bounds.size.z : bounds.size.x;
        var label = new GameObject("RunicChestLabel", typeof(RectTransform), typeof(Canvas));
        _labels.Add(label);
        // Set render mode before the final transform; changing Canvas mode can reset its rect.
        label.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        label.transform.SetParent(transform, false);
        label.transform.localPosition = new Vector3(position.X, position.Y, position.Z);
        label.transform.localRotation = rotation;
        label.transform.localScale = Vector3.one / ChestLabelLayout.UnitsPerMetre;
        label.transform.SetParent(anchor, true);
        var area = (RectTransform)label.transform;
        area.sizeDelta = new Vector2(ChestLabelLayout.Width(width), rules.Side == 4 ? 45 : 30);
        // Draw the backing first, then uGUI text on the same world-space canvas. A 3D TMP
        // component has an additional font-unit scale and made this label nearly microscopic.
        if (rules.Background != 0) {
            var backing = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            backing.transform.SetParent(area, false);
            var rect = (RectTransform)backing.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(-2, -2); rect.offsetMax = new Vector2(2, 2);
            var image = backing.GetComponent<Image>(); image.color = rules.Background == 1 ? Color.white : Color.black;
            image.raycastTarget = false;
        }
        var textObject = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(area, false);
        var text = textObject.GetComponent<TextMeshProUGUI>();
        text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
        var sign = ZNetScene.instance ? ZNetScene.instance.GetPrefab("sign") : null;
        var signText = sign ? sign.GetComponentInChildren<TMP_Text>(true) : null;
        var font = signText ? signText.font : TMP_Settings.defaultFontAsset;
        if (!font) font = StorageSearchVanillaTheme.Create().Font;
        text.font = font;
        text.alignment = TextAlignmentOptions.Center;
        text.fontSize = ChestLabelLayout.FontSize(rules.Size);
        text.enableAutoSizing = true; text.fontSizeMin = 8f; text.fontSizeMax = Mathf.Max(8f, ChestLabelLayout.FontSize(rules.Size));
        text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.richText = true;
        text.text = ChestLabelColors.Markup(string.IsNullOrWhiteSpace(rules.Label) ? AutomaticText(rules) : rules.Label, rules.Color);
        text.color = Color.white;
        text.raycastTarget = false;
        return label;
    }
    private void SetVisible(bool visible) { foreach (var label in _labels) if (label) label.SetActive(visible); }
    private void ClearLabels() { foreach (var label in _labels) if (label) { label.SetActive(false); Destroy(label); } _labels.Clear(); }
    private void OnDestroy() => ClearLabels();
}

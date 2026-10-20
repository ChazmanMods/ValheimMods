using System;
using TMPro;
using UnityEngine;

namespace RunicStorage.Runtime;

internal static class StorageText
{
    internal static TMP_FontAsset Resolve(TMP_FontAsset preferred = null)
    {
        if (preferred) return preferred;
        var gui = InventoryGui.instance;
        if (gui && gui.m_weight && gui.m_weight.font) return gui.m_weight.font;
        var sign = ZNetScene.instance ? ZNetScene.instance.GetPrefab("sign") : null;
        var text = sign ? sign.GetComponentInChildren<TMP_Text>(true) : null;
        return text ? text.font : null;
    }

    internal static TextMeshProUGUI Add(RectTransform rect, TMP_FontAsset preferred = null)
    {
        var font = Resolve(preferred);
        if (!font) throw new InvalidOperationException("Valheim UI font is not ready.");
        bool active = rect.gameObject.activeSelf;
        rect.gameObject.SetActive(false);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font; // Assigned before TMP Awake/OnEnable can request LiberationSans.
        rect.gameObject.SetActive(active);
        return text;
    }
}

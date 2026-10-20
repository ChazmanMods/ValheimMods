using TMPro;
using UnityEngine;

namespace RunicStorage.Runtime;

internal static class StorageTextFactory
{
    // TMP.Awake tries to load LiberationSans when no font has been assigned yet.
    // Valheim supplies its own fonts. Configure inactive objects before Awake runs.
    internal static TextMeshProUGUI Add(GameObject target, TMP_FontAsset font)
    {
        bool active = target.activeSelf;
        target.SetActive(false);
        var text = target.AddComponent<TextMeshProUGUI>();
        text.font = font != null ? font : TMP_Settings.defaultFontAsset;
        // If the game font is not ready, keep this label inactive rather than invoke
        // TMP with a missing font. UI rebuilds can retry once the game theme is ready.
        target.SetActive(active && text.font != null);
        return text;
    }
}

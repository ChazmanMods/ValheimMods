using System;
using BepInEx.Configuration;
using UnityEngine;

namespace RunicInventory.Integration
{
    // Recognized by Configuration Manager by type/member names, without a hard dependency.
    internal sealed class ConfigurationManagerAttributes
    {
        public bool ReadOnly => (InventoryConfig.Enabled?.Value ?? false) &&
                                !(Plugin.Instance?.Runtime?.CanDisable(out _) ?? true);
        public Action<ConfigEntryBase> CustomDrawer = InventorySettingsDrawer.DrawEnabled;
    }

    internal static class InventorySettingsDrawer
    {
        internal static void DrawEnabled(ConfigEntryBase entry)
        {
            if (!(entry is ConfigEntry<bool> enabled)) return;
            string reason = string.Empty;
            bool canDisable = Plugin.Instance?.Runtime?.CanDisable(out reason) ?? true;
            bool previous = GUI.enabled;
            try
            {
                GUI.enabled = previous && (!enabled.Value || canDisable);
                bool next = GUILayout.Toggle(enabled.Value, enabled.Value ? "Enabled" : "Disabled");
                if (next != enabled.Value) enabled.Value = next;
            }
            finally { GUI.enabled = previous; }
            if (enabled.Value && !canDisable)
                GUILayout.Label(reason, new GUIStyle(GUI.skin.label) { wordWrap = true });
        }
    }
}

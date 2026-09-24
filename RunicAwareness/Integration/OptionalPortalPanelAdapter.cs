using System;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using UnityEngine;

namespace RunicAwareness.Integration
{
    /// <summary>
    /// Optional, read-only detection for the Runic Portals setup guide. The integration uses only
    /// BepInEx's public plugin/configuration surface, so Awareness never acquires an assembly or
    /// package dependency on Runic Portals.
    /// </summary>
    internal sealed class OptionalPortalPanelAdapter
    {
        internal const string PortalPluginGuid = "chazman.RunicPortals";
        private BaseUnityPlugin _plugin;
        private ConfigEntry<bool> _enabled;
        private ConfigEntry<bool> _universalRouting;
        private ConfigEntry<bool> _showSetupPanel;
        private int _lastPortalHoverFrame = -10;

        internal bool SetupPanelVisible(Player player)
        {
            if (player == null)
            {
                _lastPortalHoverFrame = -10;
                return false;
            }
            if (!TryBind()) return false;
            if (!_enabled.Value || !_universalRouting.Value || !_showSetupPanel.Value)
            {
                _lastPortalHoverFrame = -10;
                return false;
            }

            GameObject hovered = player.GetHoverObject();
            if (hovered != null && hovered.GetComponentInParent<TeleportWorld>() != null)
                _lastPortalHoverFrame = Time.frameCount;
            int age = Time.frameCount - _lastPortalHoverFrame;
            return age >= 0 && age <= 1;
        }

        private bool TryBind()
        {
            try
            {
                if (!Chainloader.PluginInfos.TryGetValue(
                        PortalPluginGuid,
                        out PluginInfo information) || information?.Instance == null)
                {
                    Clear();
                    return false;
                }

                BaseUnityPlugin plugin = information.Instance;
                if (ReferenceEquals(_plugin, plugin) && _enabled != null &&
                    _universalRouting != null && _showSetupPanel != null)
                    return true;

                ConfigFile config = plugin.Config;
                if (config == null ||
                    !config.TryGetEntry(global::Runic.Localization.RunicText.Get("text_c910d474dcd7"), global::Runic.Localization.RunicText.Get("text_92c1cdfdf4cb"), out ConfigEntry<bool> enabled) ||
                    !config.TryGetEntry(
                        global::Runic.Localization.RunicText.Get("text_5697d03daef4"), "UniversalRouting", out ConfigEntry<bool> universalRouting) ||
                    !config.TryGetEntry(
                        global::Runic.Localization.RunicText.Get("text_34e108c0896d"), "ShowSetupPanel", out ConfigEntry<bool> showSetupPanel))
                {
                    Clear();
                    return false;
                }

                _plugin = plugin;
                _enabled = enabled;
                _universalRouting = universalRouting;
                _showSetupPanel = showSetupPanel;
                return true;
            }
            catch (Exception)
            {
                Clear();
                return false;
            }
        }

        private void Clear()
        {
            _plugin = null;
            _enabled = null;
            _universalRouting = null;
            _showSetupPanel = null;
            _lastPortalHoverFrame = -10;
        }
    }
}

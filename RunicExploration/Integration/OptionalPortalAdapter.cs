using System;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;

namespace RunicExploration.Integration
{
    internal sealed class OptionalPortalAdapter : IDisposable
    {
        private const float RetrySeconds = 10f;

        private BaseUnityPlugin _plugin;
        private ConfigEntry<bool> _enabled;
        private float _nextBindAt;
        private bool _assemblyDirty = true;
        private string _statusLine = string.Empty;

        internal OptionalPortalAdapter()
        {
            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
        }

        internal string StatusLine => _statusLine;

        internal void Refresh(float now)
        {
            if (!TryBind(now))
            {
                _statusLine = string.Empty;
                return;
            }

            try
            {
                _statusLine = _enabled.Value && _plugin != null
                    ? "Runic Portals: authorized directory remains available in portal context."
                    : string.Empty;
            }
            catch
            {
                ClearBinding();
                _nextBindAt = now + RetrySeconds;
            }
        }

        public void Dispose()
        {
            AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
            ClearBinding();
        }

        private bool TryBind(float now)
        {
            if (_plugin != null && _enabled != null) return true;
            if (!_assemblyDirty && now < _nextBindAt) return false;
            _assemblyDirty = false;
            _nextBindAt = now + RetrySeconds;
            try
            {
                if (!Chainloader.PluginInfos.TryGetValue(
                        "chazman.RunicPortals", out PluginInfo information) ||
                    information?.Instance?.Config == null ||
                    !information.Instance.Config.TryGetEntry(
                        "General", "Enabled", out ConfigEntry<bool> enabled))
                    return false;
                _plugin = information.Instance;
                _enabled = enabled;
                return true;
            }
            catch
            {
                ClearBinding();
                return false;
            }
        }

        private void OnAssemblyLoad(object sender, AssemblyLoadEventArgs arguments)
        {
            if (string.Equals(
                    arguments?.LoadedAssembly?.GetName().Name,
                    "RunicPortals",
                    StringComparison.Ordinal))
                _assemblyDirty = true;
        }

        private void ClearBinding()
        {
            _plugin = null;
            _enabled = null;
            _statusLine = string.Empty;
        }
    }
}

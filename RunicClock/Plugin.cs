using System;
using System.Globalization;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace RunicClock
{
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "chazman.RunicClock";
        public const string Name = "Runic Clock";
        public const string Version = "1.0.1";
        private ClockConfig _settings;
        private ClockView _view;
        private Player _player;
        private EnvMan _environment;
        private ZNet _network;
        private float _nextRefresh;
        private volatile bool _refreshRequested;
        private bool _ready, _failed;

        private void Awake()
        {
            // No server runtime, Harmony patches, RPCs, or network registration.
            if (Application.isBatchMode) { Logger.LogInfo("RunicClock is client-only; no server runtime started."); return; }
            _settings = new ClockConfig(Config);
            _view = new ClockView(_settings);
            Config.SettingChanged += SettingsChanged;
            Logger.LogInfo(Name + " " + Version + " ready. Left Alt+C toggles the clock; all settings are local.");
        }

        private void SettingsChanged(object sender, SettingChangedEventArgs args) => _refreshRequested = true;

        private void Update()
        {
            if (_settings == null || _failed) return;
            try
            {
                if (!_settings.Enabled.Value || !HasWorld())
                {
                    _ready = false; _nextRefresh = 0;
                    _player = null; _environment = null; _network = null;
                    return;
                }
                if (!ReferenceEquals(_player, Player.m_localPlayer) || !ReferenceEquals(_environment, EnvMan.instance) || !ReferenceEquals(_network, ZNet.instance))
                {
                    _player = Player.m_localPlayer; _environment = EnvMan.instance; _network = ZNet.instance;
                    _ready = false; _nextRefresh = 0;
                }
                if (!InputBlocked() && _settings.ToggleKey.Value.IsDown()) _settings.Visible.Value = !_settings.Visible.Value;
                if (!_settings.Visible.Value) { _ready = false; return; }
                float now = Time.unscaledTime;
                if (_refreshRequested) { _refreshRequested = false; _nextRefresh = 0; }
                if (now < _nextRefresh && now >= _nextRefresh - 0.25f) return;
                _nextRefresh = now + 0.25f;
                _ready = ClockModel.TryGameTime(_environment.GetDayFraction(), _settings.Use24Hour.Value, out string gameTime);
                if (!_ready) return;
                int day = _environment.GetDay();
                if (day < 0) { _ready = false; return; }
                DateTime local = DateTime.Now;
                _view.SetText(gameTime, "Day " + day.ToString(CultureInfo.InvariantCulture),
                    "Local " + ClockModel.FormatTime(local.Hour, local.Minute, _settings.RealTime24Hour.Value), EnvMan.IsDay());
            }
            catch (Exception error) { Fail(error); }
        }

        private static bool HasWorld() => Player.m_localPlayer != null && EnvMan.instance != null && ZNet.instance != null && Hud.instance != null;

        private static bool InputBlocked() => Menu.IsVisible() || global::Console.IsVisible() || TextInput.IsVisible() ||
            InventoryGui.IsVisible() || Minimap.IsOpen() || StoreGui.IsVisible() || Hud.IsPieceSelectionVisible() ||
            UnifiedPopup.IsVisible() || ConnectPanel.IsVisible() || Feedback.IsVisible() ||
            PlayerCustomizaton.IsBarberGuiVisible() || ZInput.VirtualKeyboardOpen || Game.IsPaused() ||
            (TextViewer.instance != null && TextViewer.instance.IsVisible()) ||
            (Chat.instance != null && (Chat.instance.HasFocus() || Chat.instance.IsChatDialogWindowVisible()));

        private void OnGUI()
        {
            if (_settings == null || _failed || !_ready || !_settings.Enabled.Value || !_settings.Visible.Value ||
                Event.current == null || Event.current.type != EventType.Repaint) return;
            try
            {
                if (!HasWorld() || !ReferenceEquals(_player, Player.m_localPlayer) ||
                    !ReferenceEquals(_environment, EnvMan.instance) || !ReferenceEquals(_network, ZNet.instance) ||
                    Hud.instance.m_userHidden || !Hud.instance.IsVisible() ||
                    (Hud.instance.m_loadingScreen != null && Hud.instance.m_loadingScreen.gameObject.activeInHierarchy) ||
                    (_settings.HideInMenus.Value && InputBlocked())) return;
                _view.Draw();
            }
            catch (Exception error) { Fail(error); }
        }

        private void Fail(Exception error)
        {
            _failed = true; _ready = false;
            Logger.LogError("RunicClock stopped its overlay after an error; gameplay is unchanged. " + error);
        }

        private void OnDestroy()
        {
            if (_settings != null) Config.SettingChanged -= SettingsChanged;
            _view?.Dispose(); _view = null; _ready = false;
            _player = null; _environment = null; _network = null;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;
using RunicPortals.Core;
using UnityEngine;

namespace RunicPortals.Integration
{
    /// <summary>
    /// Session-only normal-map presentation. Every native PinData returned by AddPin is retained
    /// by identity and removed explicitly; no proximity or name-based cleanup can touch user pins.
    /// </summary>
    internal sealed class PortalMapOverlayRuntime
    {
        private const Minimap.PinType PortalPinType = Minimap.PinType.Icon3;
        private const float OverlayWidth = 430f;
        private const float OverlayHeight = 44f;
        private static readonly FieldInfo VisibleIconTypesField = typeof(Minimap).GetField(
            "m_visibleIconTypes",
            BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo ToggleIconFilterMethod = typeof(Minimap).GetMethod(
            "ToggleIconFilter",
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            new[] { typeof(Minimap.PinType) },
            null);

        private readonly PortalMapOverlayModel _model = new PortalMapOverlayModel();
        private readonly List<Minimap.PinData> _pins = new List<Minimap.PinData>();
        private Minimap _map;
        private PortalMapOverlayView _view;
        private bool _mapOpen;
        private bool _suspended;
        private bool _faulted;
        private bool _filterStateCaptured;
        private bool _portalPinTypeWasVisible;
        private bool _loading;
        private bool _truncated;
        private bool _unavailable;
        private bool _directoryVisible;
        private int _renderedRevision = int.MinValue;

        internal string SelectedNetworkName => _model.SelectedNetwork;
        internal int VisiblePinCount => _pins.Count;
        internal bool IsMapOpen => _mapOpen;

        /// <summary>
        /// Replaces the complete already-authorized client snapshot. A context token must combine
        /// the current world and stable local identity; changing it atomically clears prior pins,
        /// recent-network memory, and all prior candidates.
        /// </summary>
        internal bool ReplaceAuthorizedSnapshot(
            string contextToken,
            IReadOnlyList<PortalMapCandidate> candidates)
        {
            if (_faulted) return false;
            try
            {
                if (_model.SetContext(contextToken))
                    ClearPins();
                bool accepted = _model.TryReplace(candidates);
                InvalidatePins();
                RefreshPinsIfVisible();
                return accepted;
            }
            catch (Exception exception)
            {
                DisableOverlay(exception, "snapshot");
                return false;
            }
        }

        /// <summary>
        /// Makes an entered/used network the in-memory default. This never writes configuration or
        /// sends a packet; the value is discarded with the world/identity context.
        /// </summary>
        internal void NoteNetworkUsed(string contextToken, string networkName)
        {
            if (_faulted) return;
            try
            {
                if (_model.SetContext(contextToken))
                    ClearPins();
                if (_model.NoteNetworkUsed(networkName))
                {
                    InvalidatePins();
                    RefreshPinsIfVisible();
                }
            }
            catch (Exception exception)
            {
                DisableOverlay(exception, "recent-network");
            }
        }

        internal void SetRefreshState(bool loading, bool truncated, bool unavailable = false)
        {
            _loading = loading;
            _truncated = truncated;
            _unavailable = unavailable;
        }

        internal void OnMapOpened(Minimap map)
        {
            if (_faulted || !IsLargeMap(map)) return;
            if (_map != map)
            {
                ClearPins();
                DestroyView();
                _map = map;
            }
            _mapOpen = true;
            _suspended = false;
            _directoryVisible = false;
            EnsureView(map);
            SetViewVisible(true);
            InvalidatePins();
            RefreshPinsIfVisible();
        }

        /// <summary>
        /// Called from the ordinary client update. The modal picker owns its own pins; passing
        /// suspend=true removes only this layer's pins until the picker closes.
        /// </summary>
        internal void Tick(Minimap map, bool suspend)
        {
            if (_faulted) return;
            try
            {
                if (!IsLargeMap(map))
                {
                    OnMapClosed();
                    return;
                }
                if (!_mapOpen || _map != map) OnMapOpened(map);

                if (suspend)
                {
                    if (!_suspended) ClearPins();
                    _suspended = true;
                    SetViewVisible(false);
                    return;
                }

                if (_suspended)
                {
                    _suspended = false;
                    InvalidatePins();
                    SetViewVisible(true);
                }

                if (CanReadCycleInput() && ZInput.GetKeyDown(KeyCode.P, false) &&
                    NoKeyboardModifiersHeld())
                {
                    _directoryVisible = ToggleDirectoryVisibility(_directoryVisible);
                    if (!_directoryVisible) ClearPins();
                    InvalidatePins();
                }
                RefreshPinsIfVisible();
            }
            catch (Exception exception)
            {
                DisableOverlay(exception, "tick");
            }
        }

        internal bool TryCycleNetwork(int direction)
        {
            if (_faulted || !_mapOpen || _suspended || !_model.Cycle(direction))
                return false;
            InvalidatePins();
            RefreshPinsIfVisible();
            return true;
        }

        internal void OnMapClosed()
        {
            ClearPins();
            _mapOpen = false;
            _suspended = false;
            _directoryVisible = false;
            SetViewVisible(false);
            _map = null;
        }

        internal void OnIdentityOrWorldChanged(string contextToken)
        {
            if (_faulted) return;
            try
            {
                if (!_model.SetContext(contextToken)) return;
                _directoryVisible = false;
                ClearPins();
                InvalidatePins();
                RefreshPinsIfVisible();
            }
            catch (Exception exception)
            {
                DisableOverlay(exception, "context");
            }
        }

        internal void Shutdown()
        {
            ClearPins();
            _model.SetContext(string.Empty);
            _mapOpen = false;
            _suspended = false;
            _directoryVisible = false;
            _map = null;
            _loading = false;
            _truncated = false;
            _unavailable = false;
            DestroyView();
        }

        internal void DrawOverlay()
        {
            if (_faulted || !_mapOpen || _suspended || !IsLargeMap(_map)) return;
            string text = _unavailable
                ? global::Runic.Localization.RunicText.Get("text_7f15d02c423b")
                : !_directoryVisible
                ? global::Runic.Localization.RunicText.Get("text_e8ffe43e2148")
                : _model.EligibleCount == 0
                ? global::Runic.Localization.RunicText.Get("text_654fefd0b424")
                : global::Runic.Localization.RunicText.Get("text_134b41763227") + _pins.Count + global::Runic.Localization.RunicText.Get("text_506262eac58f");
            if (_loading) text += global::Runic.Localization.RunicText.Get("text_9c80e7502868");
            else if (_truncated) text += global::Runic.Localization.RunicText.Get("text_2ba273c5c037");
            float width = Math.Min(OverlayWidth, Math.Max(220f, Screen.width - 32f));
            var bounds = new Rect(
                Math.Max(16f, Screen.width - width - 20f),
                20f,
                width,
                OverlayHeight);
            GUI.Box(bounds, text);
        }

        internal static bool ToggleDirectoryVisibility(bool current) => !current;

        private void RefreshPinsIfVisible()
        {
            if (!_mapOpen || _suspended || !_directoryVisible || !IsLargeMap(_map) ||
                _renderedRevision == _model.Revision)
                return;
            ClearPins();
            PortalMapCandidate[] candidates = _model.SelectedCandidates();
            if (candidates.Length != 0) CaptureFilterState();
            for (int index = 0; index < candidates.Length; index++)
            {
                PortalMapCandidate candidate = candidates[index];
                Minimap.PinData pin = _map.AddPin(
                    new Vector3(candidate.X, candidate.Y, candidate.Z),
                    PortalPinType,
                    EscapeRichText(candidate.DisplayName),
                    false,
                    false,
                    0L,
                    Splatform.PlatformUserID.None);
                if (pin == null)
                    throw new InvalidOperationException("Minimap.AddPin returned no pin data.");
                PortalMapMarkerSprite.Apply(pin);
                _pins.Add(pin);
            }
            _renderedRevision = _model.Revision;
        }

        private void ClearPins()
        {
            Minimap map = _map;
            if (map)
            {
                for (int index = _pins.Count - 1; index >= 0; index--)
                {
                    Minimap.PinData pin = _pins[index];
                    if (pin == null) continue;
                    try { map.RemovePin(pin); }
                    catch (Exception exception)
                    {
                        Diagnostics.Trace(
                            "Temporary portal map pin cleanup yielded: " +
                            exception.GetType().Name + ": " + exception.Message);
                    }
                }
            }
            _pins.Clear();
            RestoreFilterState(map);
            _renderedRevision = int.MinValue;
        }

        private void CaptureFilterState()
        {
            _filterStateCaptured = false;
            if (!_map || VisibleIconTypesField == null || ToggleIconFilterMethod == null)
                throw new MissingMemberException(
                    "Installed Minimap icon-filter contract is unavailable.");
            var visible = VisibleIconTypesField.GetValue(_map) as bool[];
            int type = (int)PortalPinType;
            if (visible == null || type < 0 || type >= visible.Length)
                throw new InvalidOperationException("Installed Minimap icon filters are invalid.");
            _portalPinTypeWasVisible = visible[type];
            _filterStateCaptured = true;
            if (ShouldToggleFilterOnCapture(_portalPinTypeWasVisible))
                ToggleIconFilterMethod.Invoke(_map, new object[] { PortalPinType });
        }

        private void RestoreFilterState(Minimap map)
        {
            if (!_filterStateCaptured) return;
            _filterStateCaptured = false;
            if (!map || _portalPinTypeWasVisible || VisibleIconTypesField == null ||
                ToggleIconFilterMethod == null)
                return;
            var visible = VisibleIconTypesField.GetValue(map) as bool[];
            int type = (int)PortalPinType;
            if (visible != null && type >= 0 && type < visible.Length &&
                ShouldToggleFilterOnCleanup(_portalPinTypeWasVisible, visible[type]))
                ToggleIconFilterMethod.Invoke(map, new object[] { PortalPinType });
        }

        internal static bool ShouldToggleFilterOnCapture(bool initiallyVisible) =>
            !initiallyVisible;

        internal static bool ShouldToggleFilterOnCleanup(
            bool initiallyVisible,
            bool currentlyVisible) =>
            !initiallyVisible && currentlyVisible;

        private void InvalidatePins() => _renderedRevision = int.MinValue;

        private void EnsureView(Minimap map)
        {
            if (_view && _view.gameObject == map.gameObject) return;
            DestroyView();
            _view = map.gameObject.AddComponent<PortalMapOverlayView>();
            _view.hideFlags = HideFlags.HideInInspector | HideFlags.DontSave;
            _view.Bind(this);
        }

        private void DestroyView()
        {
            if (!_view) return;
            _view.Bind(null);
            UnityEngine.Object.Destroy(_view);
            _view = null;
        }

        private void SetViewVisible(bool visible)
        {
            if (_view) _view.enabled = visible;
        }

        private void DisableOverlay(Exception exception, string operation)
        {
            Diagnostics.Error(
                exception,
                global::Runic.Localization.RunicText.Get("text_979c238d6c0a") + operation +
                global::Runic.Localization.RunicText.Get("text_bef9ae6d3754"));
            ClearPins();
            _faulted = true;
            _mapOpen = false;
            _suspended = false;
            SetViewVisible(false);
        }

        private static bool IsLargeMap(Minimap map) =>
            map && map.m_mode == Minimap.MapMode.Large && map.m_largeRoot &&
            map.m_largeRoot.activeInHierarchy;

        private static bool CanReadCycleInput()
        {
            if (ZInput.VirtualKeyboardOpen || TextInput.IsVisible() || Console.IsVisible() ||
                Menu.IsVisible() || InventoryGui.IsVisible())
                return false;
            return Chat.instance == null || !Chat.instance.HasFocus();
        }

        private static bool NoKeyboardModifiersHeld() =>
            !ZInput.GetKey(KeyCode.LeftAlt, false) &&
            !ZInput.GetKey(KeyCode.RightAlt, false) &&
            !ZInput.GetKey(KeyCode.LeftControl, false) &&
            !ZInput.GetKey(KeyCode.RightControl, false) &&
            !ZInput.GetKey(KeyCode.LeftShift, false) &&
            !ZInput.GetKey(KeyCode.RightShift, false);

        private static string EscapeRichText(string value) =>
            (value ?? string.Empty).Replace('<', '\u2039').Replace('>', '\u203a');
    }

    internal sealed class PortalMapOverlayView : MonoBehaviour
    {
        private PortalMapOverlayRuntime _owner;

        internal void Bind(PortalMapOverlayRuntime owner) => _owner = owner;

        private void OnGUI() => _owner?.DrawOverlay();
    }
}

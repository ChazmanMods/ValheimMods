using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using BepInEx.Logging;
using RunicExploration.Core;
using UnityEngine;

namespace RunicExploration.Integration
{
    internal sealed class ExplorationRuntime : IDisposable
    {
        private const string SearchControlName = "RunicExploration.Search";
        private readonly ManualLogSource _log;
        private readonly bool _inert;
        private readonly List<PinEvidence> _evidence = new List<PinEvidence>(256);
        private readonly KnownPinRebuildGate _rebuildGate = new KnownPinRebuildGate();
        private readonly OptionalPortalAdapter _portals;
        private KnownPinIndex _index = new KnownPinIndex(
            KnownPinIndexStatus.Ready,
            Array.Empty<KnownPinRecord>(),
            0,
            0,
            0);
        private KnownPinSearchResult _search = new KnownPinSearchResult(
            Array.Empty<KnownPinRecord>(),
            0,
            false);
        private GUIContent[] _resultContent = Array.Empty<GUIContent>();
        private GUIStyle _boxStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _smallStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _textFieldStyle;
        private Minimap _map;
        private int _playerInstanceId;
        private ulong _fingerprint;
        private bool _hasFingerprint;
        private bool _visible;
        private bool _failed;
        private bool _sensitiveCleared;
        private bool _searchFocused;
        private bool _panelRectValid;
        private Rect _panelRect;
        private float _nextRefresh;
        private float _nextPortalRefresh;
        private int _styleFontSize;
        private int _softFailureCount;
        private string _query = string.Empty;
        private string _summary = "Known pins: waiting for the local map.";
        private string _problem = string.Empty;
        private string _navigation = string.Empty;
        private string _sailing = string.Empty;
        private string _portalStatus = string.Empty;
        private string _categoryButtonText = "Category: all";
        private string _scopeButtonText = "Source: all";
        private KnownPinCategoryFilter _categoryFilter;
        private KnownPinScopeFilter _scopeFilter;
        private KnownPinRecord _selected;
        private bool _hasSelected;
        private ulong _navigationSignature;
        private ulong _sailingSignature;
        private bool _hasNavigationSignature;
        private bool _hasSailingSignature;

        internal ExplorationRuntime(ManualLogSource log, bool inert)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _inert = inert;
            _portals = inert ? null : new OptionalPortalAdapter();
        }

        internal bool BlocksMapInput =>
            !_inert && !_failed && _visible && _searchFocused &&
            (ExplorationConfig.Enabled?.Value ?? false);

        internal bool BlocksMapPointer(Vector2 guiPosition) =>
            !_inert && !_failed && _visible && _panelRectValid &&
            (ExplorationConfig.Enabled?.Value ?? false) && _panelRect.Contains(guiPosition);

        internal void Update()
        {
            if (_inert || _failed) return;
            // No-map and disabled worlds are rejected before any Minimap instance, pin list,
            // explored-array, or optional integration access.
            if (!(ExplorationConfig.Enabled?.Value ?? false) || Game.m_noMap)
            {
                ClearSensitive();
                return;
            }
            if (!Minimap.IsOpen())
            {
                HidePanel();
                return;
            }
            Player player = Player.m_localPlayer;
            Minimap minimap = Minimap.instance;
            if (player == null || minimap == null)
            {
                ClearSensitive();
                return;
            }
            _sensitiveCleared = false;
            int playerInstanceId = player.GetInstanceID();
            if (!ReferenceEquals(_map, minimap) || _playerInstanceId != playerInstanceId)
            {
                ClearIndex();
                _map = minimap;
                _playerInstanceId = playerInstanceId;
            }
            _visible = true;

            float now = Time.unscaledTime;
            if (now < _nextRefresh && now >= 0f) return;
            _nextRefresh = now + FiniteClamp(
                ExplorationConfig.RefreshInterval.Value,
                0.25f,
                3f,
                0.5f);
            bool includeShared = ExplorationConfig.IncludeSharedPins.Value;
            KnownPinCaptureStatus capture = KnownPinSource.Capture(
                minimap,
                includeShared,
                _evidence,
                out ulong fingerprint,
                out _);
            if (capture != KnownPinCaptureStatus.Ready)
            {
                string problem = capture == KnownPinCaptureStatus.Oversized
                    ? "Known pin browser withheld: the local pin list exceeds the hard 10,000-pin ceiling."
                    : "Known pin browser unavailable: explored-map evidence is incompatible.";
                KnownPinIndexStatus status = capture == KnownPinCaptureStatus.Oversized
                    ? KnownPinIndexStatus.Oversized
                    : KnownPinIndexStatus.InvalidSource;
                if (_index.Status != status ||
                    !string.Equals(_problem, problem, StringComparison.Ordinal))
                {
                    _problem = problem;
                    _index = new KnownPinIndex(
                        status,
                        Array.Empty<KnownPinRecord>(),
                        0,
                        0,
                        0);
                    RefreshSearch();
                    ClearSelection();
                }
                _hasFingerprint = false;
                _rebuildGate.Reset();
            }
            else
            {
                _problem = string.Empty;
                fingerprint = Mix(fingerprint, player.GetPlayerID());
                fingerprint = Mix(fingerprint, includeShared ? 1L : 0L);
                if (_rebuildGate.ShouldRebuild(
                        fingerprint, _fingerprint, _hasFingerprint, now))
                {
                    _index = KnownPinIndexer.Build(
                        _evidence,
                        player.GetPlayerID(),
                        includeShared);
                    _fingerprint = fingerprint;
                    _hasFingerprint = true;
                    _rebuildGate.MarkPublished(now);
                    RestoreOrClearSelection();
                    RefreshSearch();
                    if (ExplorationConfig.VerboseLogging.Value)
                        _log.LogInfo(
                            "Exploration rebuilt its bounded known-pin index: known-evidence=" +
                            _index.Inspected + ", accepted=" + _index.Accepted +
                            ", visible=" + _index.Records.Length +
                            ", merged=" + _index.Merged + ".");
                }
            }

            UpdateReadouts(player, minimap, includeShared);
            if (_portals != null && ExplorationConfig.ShowKnownPinBrowser.Value &&
                (now >= _nextPortalRefresh || now < 0f))
            {
                _nextPortalRefresh = now + 5f;
                _portals.Refresh(now);
                _portalStatus = _portals.StatusLine;
            }
            else if (!ExplorationConfig.ShowKnownPinBrowser.Value)
                _portalStatus = string.Empty;
        }

        internal void Draw()
        {
            if (_inert || _failed || !_visible || Game.m_noMap ||
                !(ExplorationConfig.Enabled?.Value ?? false) || Event.current == null)
            {
                _searchFocused = false;
                _panelRectValid = false;
                return;
            }
            bool browser = ExplorationConfig.ShowKnownPinBrowser.Value;
            bool navigation = ExplorationConfig.ShowNavigationReadout.Value &&
                              !string.IsNullOrEmpty(_navigation);
            bool sailing = ExplorationConfig.ShowSailingReadout.Value &&
                           !string.IsNullOrEmpty(_sailing);
            bool portal = browser && !string.IsNullOrEmpty(_portalStatus);
            if (!browser && !navigation && !sailing && !portal)
            {
                _searchFocused = false;
                _panelRectValid = false;
                return;
            }

            bool controller = false;
            try { controller = ZInput.IsGamepadActive(); }
            catch (Exception) { }
            float scale = Mathf.Clamp(
                FiniteClamp(ExplorationConfig.UiScale.Value, 0.75f, 1.75f, 1f) *
                (controller
                    ? FiniteClamp(
                        ExplorationConfig.ControllerScaleMultiplier.Value,
                        1f,
                        1.5f,
                        1.15f)
                    : 1f),
                0.75f,
                2.625f);
            int fontSize = Mathf.Clamp(Mathf.RoundToInt(14f * scale), 11, 28);
            EnsureStyles(fontSize);
            Rect safe = Screen.safeArea;
            float margin = Mathf.Max(8f, 10f * scale);
            float availableWidth = safe.width - margin * 2f;
            float availableHeight = safe.height - margin * 2f;
            if (!Finite(availableWidth) || !Finite(availableHeight) ||
                availableWidth < 260f || availableHeight < 150f * scale)
            {
                _searchFocused = false;
                _panelRectValid = false;
                return;
            }
            float width = Mathf.Min(520f * scale, availableWidth);
            float rowHeight = Mathf.Max(24f, 28f * scale);
            int requestedRows = browser ? Math.Min(
                _search.Records.Length,
                Math.Max(5, Math.Min(KnownPinSearch.HardMaximumResults,
                    ExplorationConfig.MaximumResults.Value))) : 0;
            float fixedHeight = 118f * scale +
                                (navigation ? 66f * scale : 0f) +
                                (sailing ? 66f * scale : 0f) +
                                (portal ? 34f * scale : 0f);
            int rowsByHeight = Math.Max(
                0,
                Mathf.FloorToInt((safe.height - margin * 2f - fixedHeight) / rowHeight));
            int visibleRows = Math.Min(requestedRows, rowsByHeight);
            float height = Mathf.Min(
                availableHeight,
                fixedHeight + visibleRows * rowHeight);
            height = Mathf.Max(Mathf.Min(150f * scale, availableHeight), height);
            float x = ExplorationConfig.PanelSide.Value == ExplorationPanelSide.Right
                ? safe.xMax - width - margin
                : safe.xMin + margin;
            _panelRect = new Rect(x, safe.yMin + margin, width, height);
            _panelRectValid = true;
            GUI.Box(_panelRect, GUIContent.none, _boxStyle);

            float inset = 12f * scale;
            float currentY = _panelRect.y + inset;
            float innerWidth = _panelRect.width - inset * 2f;
            GUI.Label(
                new Rect(_panelRect.x + inset, currentY, innerWidth, 25f * scale),
                global::Runic.Localization.RunicText.Get("text_4c1ca11313cd"),
                _titleStyle);
            currentY += 27f * scale;

            if (browser)
            {
                GUI.SetNextControlName(SearchControlName);
                string entered = GUI.TextField(
                    new Rect(_panelRect.x + inset, currentY, innerWidth, 27f * scale),
                    _query,
                    BoundedText.MaximumQueryCharacters,
                    _textFieldStyle);
                string bounded = BoundedText.Sanitize(
                    entered,
                    BoundedText.MaximumQueryCharacters,
                    1);
                if (!string.Equals(_query, bounded, StringComparison.Ordinal))
                {
                    _query = bounded;
                    RefreshSearch();
                }
                currentY += 31f * scale;

                float half = (innerWidth - 6f * scale) / 2f;
                if (GUI.Button(
                        new Rect(_panelRect.x + inset, currentY, half, 25f * scale),
                        _categoryButtonText,
                        _buttonStyle))
                {
                    _categoryFilter = (KnownPinCategoryFilter)(
                        ((int)_categoryFilter + 1) %
                        (Enum.GetValues(typeof(KnownPinCategoryFilter)).Length));
                    UpdateFilterButtonText();
                    RefreshSearch();
                }
                if (GUI.Button(
                        new Rect(_panelRect.x + inset + half + 6f * scale, currentY,
                            half, 25f * scale),
                        _scopeButtonText,
                        _buttonStyle))
                {
                    _scopeFilter = (KnownPinScopeFilter)(
                        ((int)_scopeFilter + 1) %
                        (Enum.GetValues(typeof(KnownPinScopeFilter)).Length));
                    UpdateFilterButtonText();
                    RefreshSearch();
                }
                currentY += 29f * scale;

                string status = !string.IsNullOrEmpty(_problem) ? _problem : _summary;
                GUI.Label(
                    new Rect(_panelRect.x + inset, currentY, innerWidth, 30f * scale),
                    status,
                    _smallStyle);
                currentY += 31f * scale;

                // TextField and either filter button can replace both arrays during this same
                // IMGUI pass. Clamp against the post-input snapshot rather than the row count
                // calculated before input, so a narrowing keystroke cannot index stale bounds.
                int currentVisibleRows = ClampVisibleRows(
                    visibleRows,
                    _search.Records.Length,
                    _resultContent.Length);
                for (int index = 0; index < currentVisibleRows; index++)
                {
                    if (GUI.Button(
                            new Rect(_panelRect.x + inset, currentY, innerWidth, rowHeight - 3f),
                            _resultContent[index],
                            _buttonStyle))
                        SelectAndCenter(_search.Records[index]);
                    currentY += rowHeight;
                }
                if (_search.Records.Length == 0 && string.IsNullOrEmpty(_problem))
                {
                    GUI.Label(
                        new Rect(_panelRect.x + inset, currentY, innerWidth, rowHeight),
                        global::Runic.Localization.RunicText.Get("text_c90bdbffe800"),
                        _smallStyle);
                    currentY += rowHeight;
                }
            }

            if (navigation && currentY + 45f * scale < _panelRect.yMax)
            {
                GUI.Label(
                    new Rect(_panelRect.x + inset, currentY, innerWidth, 62f * scale),
                    _navigation,
                    _labelStyle);
                currentY += 66f * scale;
            }
            if (sailing && currentY + 45f * scale < _panelRect.yMax)
            {
                GUI.Label(
                    new Rect(_panelRect.x + inset, currentY, innerWidth, 62f * scale),
                    _sailing,
                    _labelStyle);
                currentY += 66f * scale;
            }
            if (portal && currentY + 20f < _panelRect.yMax)
                GUI.Label(
                    new Rect(_panelRect.x + inset, currentY, innerWidth, 30f * scale),
                    _portalStatus,
                    _smallStyle);

            _searchFocused = browser && string.Equals(
                GUI.GetNameOfFocusedControl(),
                SearchControlName,
                StringComparison.Ordinal);
            if (_searchFocused && Event.current.type == EventType.KeyDown &&
                Event.current.keyCode == KeyCode.Escape)
            {
                GUI.FocusControl(null);
                _searchFocused = false;
                Event.current.Use();
            }
        }

        internal void OnConfigurationChanged(ConfigDefinition definition)
        {
            _nextRefresh = 0f;
            _styleFontSize = 0;
            _hasFingerprint = false;
            _rebuildGate.Reset();
            _hasNavigationSignature = false;
            _hasSailingSignature = false;
            if (!(ExplorationConfig.Enabled?.Value ?? false)) ClearSensitive();
            else if (definition != null &&
                     string.Equals(definition.Section, "KnownMap", StringComparison.Ordinal) &&
                     string.Equals(definition.Key, "IncludeSharedPins", StringComparison.Ordinal))
                ClearIndex();
            else RefreshSearch();
            if (ExplorationConfig.VerboseLogging?.Value ?? false)
                _log.LogInfo("Exploration configuration refreshed: " +
                             (definition == null
                                 ? "unknown"
                                 : definition.Section + "/" + definition.Key));
        }

        internal static int ClampVisibleRows(int requested, int records, int content)
        {
            if (requested <= 0 || records <= 0 || content <= 0) return 0;
            return Math.Min(requested, Math.Min(records, content));
        }

        internal void FailClosed(string operation, Exception exception)
        {
            if (_failed) return;
            _failed = true;
            ClearSensitive();
            _log.LogError(
                "Runic Exploration stopped its display after " + operation +
                "; the vanilla map and world remain unchanged. " +
                exception.GetType().Name + ": " + exception.Message);
        }

        public void Dispose()
        {
            _portals?.Dispose();
            ClearSensitive();
            _boxStyle = _titleStyle = _labelStyle = _smallStyle = _buttonStyle =
                _textFieldStyle = null;
        }

        private void UpdateReadouts(Player player, Minimap minimap, bool includeShared)
        {
            try
            {
                Vector3 playerPosition = player.transform.position;
                Vector3 selectedPosition = default;
                bool selectedValid = _hasSelected && KnownPinSource.TryResolve(
                    minimap,
                    includeShared,
                    _selected,
                    out selectedPosition);
                if (_hasSelected && !selectedValid) ClearSelection();

                if (selectedValid && ExplorationConfig.ShowNavigationReadout.Value)
                {
                    ulong signature = Signature(_selected.EvidenceHash,
                        Quantize(playerPosition.x, 5f), Quantize(playerPosition.y, 5f),
                        Quantize(playerPosition.z, 5f));
                    if (!_hasNavigationSignature || signature != _navigationSignature)
                    {
                        _navigation = NavigationFormatter.FormatSelected(
                            _selected,
                            playerPosition.x,
                            playerPosition.y,
                            playerPosition.z);
                        _navigationSignature = signature;
                        _hasNavigationSignature = true;
                    }
                }
                else
                {
                    _navigation = string.Empty;
                    _hasNavigationSignature = false;
                }

                Ship ship = ExplorationConfig.ShowSailingReadout.Value
                    ? player.GetControlledShip()
                    : null;
                EnvMan environment = EnvMan.instance;
                if (ship != null && environment != null)
                {
                    float windFactor = ship.GetWindAngleFactor();
                    float windIntensity = environment.GetWindIntensity();
                    Ship.Speed speed = ship.GetSpeedSetting();
                    bool sailUp = ship.IsSailUp();
                    Heightmap.Biome biome = player.GetCurrentBiome();
                    double? distance = selectedValid
                        ? Math.Sqrt(
                            (double)(selectedPosition.x - playerPosition.x) *
                            (selectedPosition.x - playerPosition.x) +
                            (double)(selectedPosition.z - playerPosition.z) *
                            (selectedPosition.z - playerPosition.z))
                        : (double?)null;
                    ulong signature = Signature(
                        (ulong)(int)speed,
                        sailUp ? 1 : 0,
                        Quantize(windFactor, 0.01f),
                        Quantize(windIntensity, 0.01f));
                    signature = Mix(signature, (long)biome);
                    signature = Mix(signature, distance.HasValue
                        ? (long)Math.Round(distance.Value / 10d, MidpointRounding.AwayFromZero)
                        : -1L);
                    if (!_hasSailingSignature || signature != _sailingSignature)
                    {
                        _sailing = NavigationFormatter.FormatSailing(
                            speed.ToString(),
                            sailUp,
                            windFactor,
                            windIntensity,
                            biome.ToString(),
                            distance);
                        _sailingSignature = signature;
                        _hasSailingSignature = true;
                    }
                }
                else
                {
                    _sailing = string.Empty;
                    _hasSailingSignature = false;
                }
            }
            catch (Exception exception)
            {
                _navigation = string.Empty;
                _sailing = string.Empty;
                _hasNavigationSignature = false;
                _hasSailingSignature = false;
                if (_softFailureCount < 3)
                {
                    _softFailureCount++;
                    _log.LogWarning(
                        "Exploration ignored a local readout failure: " +
                        exception.GetType().Name + ".");
                }
            }
        }

        private void SelectAndCenter(KnownPinRecord record)
        {
            if (_map == null || Game.m_noMap ||
                !KnownPinSource.TryResolve(
                    _map,
                    ExplorationConfig.IncludeSharedPins.Value,
                    record,
                    out Vector3 position))
            {
                _hasFingerprint = false;
                _rebuildGate.Reset();
                ClearSelection();
                return;
            }
            _selected = record;
            _hasSelected = true;
            _hasNavigationSignature = false;
            _hasSailingSignature = false;
            _map.ShowPointOnMap(position);
            Player player = Player.m_localPlayer;
            if (player != null) UpdateReadouts(
                player,
                _map,
                ExplorationConfig.IncludeSharedPins.Value);
        }

        private void RefreshSearch()
        {
            _search = KnownPinSearch.Filter(
                _index.Records,
                _query,
                _categoryFilter,
                _scopeFilter,
                Math.Max(5, Math.Min(KnownPinSearch.HardMaximumResults,
                    ExplorationConfig.MaximumResults?.Value ?? 12)));
            _resultContent = new GUIContent[_search.Records.Length];
            for (int index = 0; index < _search.Records.Length; index++)
                _resultContent[index] = new GUIContent(_search.Records[index].DisplayRow);
            _summary = "Known explored pins: " + _index.Records.Length.ToString(
                           CultureInfo.InvariantCulture) +
                       " | matches: " + _search.Records.Length.ToString(
                           CultureInfo.InvariantCulture) +
                       (_search.Truncated ? "+" : string.Empty) +
                       " | merged duplicates: " + _index.Merged.ToString(
                           CultureInfo.InvariantCulture);
        }

        private void RestoreOrClearSelection()
        {
            if (!_hasSelected) return;
            for (int index = 0; index < _index.Records.Length; index++)
            {
                KnownPinRecord candidate = _index.Records[index];
                if (candidate.EvidenceHash != _selected.EvidenceHash) continue;
                _selected = candidate;
                return;
            }
            ClearSelection();
        }

        private void ClearSelection()
        {
            _selected = default;
            _hasSelected = false;
            _navigation = string.Empty;
            _hasNavigationSignature = false;
            _sailingSignature = 0UL;
            _hasSailingSignature = false;
        }

        private void ClearIndex()
        {
            _evidence.Clear();
            _index = new KnownPinIndex(
                KnownPinIndexStatus.Ready,
                Array.Empty<KnownPinRecord>(),
                0,
                0,
                0);
            _hasFingerprint = false;
            _fingerprint = 0UL;
            _rebuildGate.Reset();
            _problem = string.Empty;
            ClearSelection();
            RefreshSearch();
        }

        private void ClearSensitive()
        {
            HidePanel();
            if (_sensitiveCleared) return;
            _sensitiveCleared = true;
            _query = string.Empty;
            ClearIndex();
            _map = null;
            _playerInstanceId = 0;
            _portalStatus = string.Empty;
            _navigation = string.Empty;
            _sailing = string.Empty;
        }

        private void HidePanel()
        {
            _visible = false;
            _searchFocused = false;
            _panelRectValid = false;
        }

        private void EnsureStyles(int fontSize)
        {
            if (_boxStyle != null && _styleFontSize == fontSize) return;
            _boxStyle = new GUIStyle(GUI.skin.box);
            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize + 2,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false
            };
            _titleStyle.normal.textColor = new Color(0.96f, 0.82f, 0.4f, 1f);
            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
                richText = false
            };
            _smallStyle = new GUIStyle(_labelStyle) { fontSize = Math.Max(10, fontSize - 2) };
            _smallStyle.normal.textColor = new Color(0.78f, 0.86f, 0.9f, 1f);
            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = Math.Max(10, fontSize - 1),
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false
            };
            _textFieldStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize = fontSize,
                alignment = TextAnchor.MiddleLeft
            };
            _styleFontSize = fontSize;
        }

        private static string CategoryFilterLabel(KnownPinCategoryFilter filter)
        {
            switch (filter)
            {
                case KnownPinCategoryFilter.TaggedAssets: return global::Runic.Localization.RunicText.Get("text_d350dea08831");
                case KnownPinCategoryFilter.Tombstones: return "tombstones";
                case KnownPinCategoryFilter.Beds: return "beds";
                case KnownPinCategoryFilter.Custom: return "custom";
                case KnownPinCategoryFilter.Bosses: return "bosses";
                default: return "all";
            }
        }

        private static string ScopeFilterLabel(KnownPinScopeFilter filter)
        {
            switch (filter)
            {
                case KnownPinScopeFilter.Personal: return "personal";
                case KnownPinScopeFilter.Shared: return "shared";
                default: return "all";
            }
        }

        private void UpdateFilterButtonText()
        {
            _categoryButtonText = "Category: " + CategoryFilterLabel(_categoryFilter);
            _scopeButtonText = "Source: " + ScopeFilterLabel(_scopeFilter);
        }

        private static int Quantize(float value, float unit)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || unit <= 0f) return 0;
            double rounded = Math.Round(value / unit, MidpointRounding.AwayFromZero);
            return rounded > int.MaxValue ? int.MaxValue :
                rounded < int.MinValue ? int.MinValue : (int)rounded;
        }

        private static float FiniteClamp(float value, float minimum, float maximum, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) value = fallback;
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private static bool Finite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        private static ulong Signature(ulong first, int second, int third, int fourth)
        {
            ulong hash = 1469598103934665603UL ^ first;
            hash *= 1099511628211UL;
            hash = Mix(hash, second);
            hash = Mix(hash, third);
            return Mix(hash, fourth);
        }

        private static ulong Signature(ulong first, int second, int third, int fourth, int fifth)
        {
            ulong hash = Signature(first, second, third, fourth);
            return Mix(hash, fifth);
        }

        private static ulong Mix(ulong hash, long value)
        {
            unchecked
            {
                hash ^= (ulong)value;
                return hash * 1099511628211UL;
            }
        }

        private static ulong Mix(ulong hash, int value) => Mix(hash, (long)value);
    }
}

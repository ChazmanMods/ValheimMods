using System;
using BepInEx.Configuration;
using QuietBuildRotation.Integration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace QuietBuildRotation.UI
{
    internal enum OrientationAxis
    {
        None,
        Yaw,
        Pitch,
        Roll,
        Sway,
        Heave,
        Surge
    }

    /// <summary>
    /// Presents effective placement and target transforms as an extension of Valheim's native
    /// selected-piece panel. It owns no Canvas: its rows clone the panel's description text and
    /// remain parented to the native SelectedInfo hierarchy.
    /// </summary>
    internal static class OrientationPresenter
    {
        private const string ActiveOpen = "<color=#D8BC70><b>";
        private const string MatchOpen = "<color=#91C98B><b>";
        private const string HighlightClose = "</b></color>";
        private const string ValueOpen = "<color=#FFF1C4><size=115%><b>";
        private const string ValueClose = "</b></size></color>";
        private const float DefaultFontSize = 36f;
        private const float HorizontalInset = 12f;
        private const float PanelPadding = 8f;
        private const float RowGap = 4f;
        private const float GeometryTolerance = 0.01f;

        private static RectTransform _readoutRoot;
        private static VerticalLayoutGroup _readoutLayout;
        private static GameObject _placeEntry;
        private static GameObject _targetEntry;
        private static GameObject _axisGuideEntry;
        private static TMP_Text _placeText;
        private static TMP_Text _targetText;
        private static TMP_Text _axisGuideText;
        private static RectTransform _layoutRoot;
        private static Hud _attachedHud;
        private static Transform _selectedInfoRoot;
        private static RectTransform _selectedInfoRect;
        private static RectTransform _selectedPieceRect;
        private static RectTransform _requirementsRect;
        private static RectTransform _buildMenuHelpRect;
        private static Image _selectedInfoBackground;
        private static Vector2 _panelBaseSize;
        private static Vector2 _panelBasePosition;
        private static Vector2 _panelAppliedSize;
        private static Vector2 _panelAppliedPosition;
        private static Vector2 _buildMenuHelpBasePosition;
        private static Vector2 _buildMenuHelpAppliedPosition;
        private static float _requirementsTop;
        private static float _topReserve;
        private static float _baseSlack;
        private static float _styledRowHeight;
        private static float _styledLegendRowHeight;
        private static float _styledPanelWidth = -1f;
        private static float _styledPanelScaleX = -1f;
        private static float _styledPanelScaleY = -1f;
        private static bool _panelExtended;
        private static bool _backgroundRaycastSuppressed;
        private static bool _backgroundRaycastOriginal;
        private static bool _templateWarningLogged;
        private static bool _attachmentLogged;
        private static bool _activationLogged;
        private static int _styledFontSize = -1;

        private static bool _placeActive;
        private static bool _targetActive;
        private static bool _axisGuideActive;
        private static bool _havePlaceSignature;
        private static Quaternion _lastPlacementRotation;
        private static Vector3 _lastPlacementPosition;
        private static Vector3 _lastPlacementDelta;
        private static PlacementReferenceFrame _lastReferenceFrame;
        private static bool _lastAbsolutePosition;
        private static OrientationAxis _lastAxis;
        private static float _lastStep;
        private static bool _lastStepIsFine;
        private static bool _lastMatched;
        private static bool _haveTargetSignature;
        private static int _lastTargetId;
        private static Vector3 _lastTargetPosition;
        private static Quaternion _lastTargetRotation;
        private static float _lastTargetDistance;
        private static int _lastSnapHostId;

        internal static void Attach(Hud hud)
        {
            if (IsAttachedTo(hud))
                return;

            Destroy();

            if (!hud || !hud.m_buildHud || !hud.m_pieceDescription)
            {
                WarnMissingTemplate();
                return;
            }

            Transform selectedInfo = FindSelectedInfoRoot(hud);
            GameObject template = hud.m_pieceDescription.gameObject;
            RectTransform selectedInfoRect = selectedInfo as RectTransform;
            RectTransform selectedPieceRect = hud.m_buildSelection
                ? hud.m_buildSelection.transform.parent as RectTransform
                : null;
            RectTransform requirementsRect = selectedInfo
                ? selectedInfo.Find("requirements") as RectTransform
                : null;
            RectTransform buildMenuHelpRect = selectedInfo
                ? selectedInfo.Find("build_menu_help") as RectTransform
                : null;
            Transform backgroundTransform = selectedInfo
                ? selectedInfo.Find("Bkg2")
                : null;
            Image background = backgroundTransform
                ? backgroundTransform.GetComponent<Image>()
                : null;
            if (!selectedInfoRect || !selectedPieceRect || !requirementsRect ||
                !background || !template ||
                !HasSupportedPanelTopology(
                    selectedInfoRect,
                    selectedPieceRect,
                    requirementsRect))
            {
                WarnMissingTemplate();
                return;
            }

            _selectedInfoRect = selectedInfoRect;
            _selectedPieceRect = selectedPieceRect;
            _requirementsRect = requirementsRect;
            _buildMenuHelpRect = buildMenuHelpRect;
            _selectedInfoBackground = background;
            _backgroundRaycastOriginal = background.raycastTarget;
            CapturePanelBaseline();

            _readoutRoot = CreateReadoutRoot(selectedInfo);
            if (!_readoutRoot)
            {
                WarnMissingTemplate();
                return;
            }

            _placeEntry = UnityEngine.Object.Instantiate(template, _readoutRoot, false);
            _targetEntry = UnityEngine.Object.Instantiate(template, _readoutRoot, false);
            _axisGuideEntry = UnityEngine.Object.Instantiate(template, _readoutRoot, false);
            _placeEntry.name = "RunicPrecision_PlaceOrientation";
            _targetEntry.name = "RunicPrecision_TargetOrientation";
            _axisGuideEntry.name = "RunicPrecision_WorldAxisLegend";

            _placeText = PrepareEntry(_placeEntry);
            _targetText = PrepareEntry(_targetEntry);
            _axisGuideText = PrepareEntry(_axisGuideEntry);
            if (!_placeText || !_targetText || !_axisGuideText)
            {
                Destroy();
                WarnMissingTemplate();
                return;
            }

            _placeEntry.transform.SetSiblingIndex(0);
            _targetEntry.transform.SetSiblingIndex(1);
            _axisGuideEntry.transform.SetSiblingIndex(2);
            _axisGuideText.text = BuildAxisGuideLegend();
            _layoutRoot = _readoutRoot;
            _attachedHud = hud;
            _selectedInfoRoot = selectedInfo;
            SetPlaceActive(false);
            SetTargetActive(false);
            SetAxisGuideActive(false);
            ApplyVisualStyle(true);
            Invalidate();

            if (!_attachmentLogged)
            {
                _attachmentLogged = true;
                Diagnostics.Info(
                    "Runic v2 orientation rows attached to Valheim's native selected-piece panel.");
            }
        }

        /// <summary>
        /// Presents one placement line and, when a valid target exists, one target line. The
        /// caller supplies canShow from its placement/display gate; Diagnostics adds the
        /// adapter/config gate so unavailable Runic features never leave stale hints visible.
        /// </summary>
        internal static void Present(
            bool canShow,
            Vector3 placementPosition,
            Quaternion placementRotation,
            Vector3 placementDelta,
            PlacementReferenceFrame referenceFrame,
            bool absolutePosition,
            OrientationAxis activeAxis,
            float activeStep,
            bool activeStepIsFine,
            TargetInfo target,
            float targetDistance,
            int snapHostId,
            bool matchFeedback)
        {
            EnsureAttached();
            ApplyVisualStyle(false);
            bool textChanged = false;

            bool runicAvailable = canShow && Diagnostics.CanRun;
            bool showPlacement = runicAvailable &&
                                 PluginConfig.ShowPlacementAngles != null &&
                                 PluginConfig.ShowPlacementAngles.Value;
            bool showTarget = runicAvailable &&
                              PluginConfig.ShowTargetAngles != null &&
                              PluginConfig.ShowTargetAngles.Value &&
                              target.IsValid;

            SetPlaceActive(showPlacement);
            SetTargetActive(showTarget);

            if (showPlacement && PlaceSignatureChanged(
                    placementPosition,
                    placementRotation,
                    placementDelta,
                    referenceFrame,
                    absolutePosition,
                    activeAxis,
                    activeStep,
                    activeStepIsFine,
                    matchFeedback))
            {
                string value = BuildPlacementText(
                    placementPosition,
                    placementRotation,
                    placementDelta,
                    referenceFrame,
                    absolutePosition,
                    activeAxis,
                    activeStep,
                    activeStepIsFine,
                    matchFeedback);
                if (_placeText && !string.Equals(_placeText.text, value, StringComparison.Ordinal))
                {
                    _placeText.text = value;
                    textChanged = true;
                }
            }

            if (showTarget && TargetSignatureChanged(target, targetDistance, snapHostId))
            {
                string value = BuildTargetText(target, targetDistance, snapHostId);
                if (_targetText && !string.Equals(_targetText.text, value, StringComparison.Ordinal))
                {
                    _targetText.text = value;
                    textChanged = true;
                }
            }

            // TMP preferred sizes are calculated from the assigned string. Rebuild once after
            // a value change so wrapped panel text settles before the frame is presented.
            if (textChanged) RebuildLayout();

            if (showPlacement)
                LogFirstActivation();
        }

        internal static void Hide()
        {
            SetPlaceActive(false);
            SetTargetActive(false);
            SetAxisGuideActive(false);
        }

        /// <summary>
        /// Shows the piece-local axis legend inside the existing native readout hierarchy. The
        /// guide presenter owns hold-state and calls this false on every release/failure path.
        /// </summary>
        internal static void SetAxisGuideLegend(bool active)
        {
            if (active)
            {
                EnsureAttached();
                ApplyVisualStyle(false);
            }

            bool show = active && Diagnostics.CanRun && _axisGuideEntry && _axisGuideText;
            bool textChanged = false;
            if (show)
            {
                string value = BuildAxisGuideLegend();
                if (!string.Equals(_axisGuideText.text, value, StringComparison.Ordinal))
                {
                    _axisGuideText.text = value;
                    textChanged = true;
                }
            }
            SetAxisGuideActive(show);
            if (textChanged) RebuildLayout();
        }

        internal static void Invalidate()
        {
            _havePlaceSignature = false;
            _haveTargetSignature = false;
        }

        internal static void Destroy()
        {
            RestorePanelGeometry();

            // Unity destruction is deferred until the end of the frame. Deactivate first so a
            // HUD recreation can never leave two visible Runic rows for one frame.
            if (_readoutRoot)
            {
                _readoutRoot.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(_readoutRoot.gameObject);
            }
            else
            {
                if (_placeEntry)
                {
                    _placeEntry.SetActive(false);
                    UnityEngine.Object.Destroy(_placeEntry);
                }
                if (_targetEntry)
                {
                    _targetEntry.SetActive(false);
                    UnityEngine.Object.Destroy(_targetEntry);
                }
                if (_axisGuideEntry)
                {
                    _axisGuideEntry.SetActive(false);
                    UnityEngine.Object.Destroy(_axisGuideEntry);
                }
            }
            _readoutRoot = null;
            _readoutLayout = null;
            _placeEntry = null;
            _targetEntry = null;
            _axisGuideEntry = null;
            _placeText = null;
            _targetText = null;
            _axisGuideText = null;
            _layoutRoot = null;
            _attachedHud = null;
            _selectedInfoRoot = null;
            _selectedInfoRect = null;
            _selectedPieceRect = null;
            _requirementsRect = null;
            _buildMenuHelpRect = null;
            _selectedInfoBackground = null;
            _panelBaseSize = Vector2.zero;
            _panelBasePosition = Vector2.zero;
            _panelAppliedSize = Vector2.zero;
            _panelAppliedPosition = Vector2.zero;
            _buildMenuHelpBasePosition = Vector2.zero;
            _buildMenuHelpAppliedPosition = Vector2.zero;
            _requirementsTop = 0f;
            _topReserve = 0f;
            _baseSlack = 0f;
            _styledRowHeight = 0f;
            _styledLegendRowHeight = 0f;
            _styledPanelWidth = -1f;
            _styledPanelScaleX = -1f;
            _styledPanelScaleY = -1f;
            _panelExtended = false;
            _backgroundRaycastSuppressed = false;
            _backgroundRaycastOriginal = false;
            _placeActive = false;
            _targetActive = false;
            _axisGuideActive = false;
            _styledFontSize = -1;
            Invalidate();
        }

        private static bool PlaceSignatureChanged(
            Vector3 placementPosition,
            Quaternion placementRotation,
            Vector3 placementDelta,
            PlacementReferenceFrame referenceFrame,
            bool absolutePosition,
            OrientationAxis activeAxis,
            float activeStep,
            bool activeStepIsFine,
            bool matched)
        {
            float step = Mathf.Abs(activeStep);
            bool changed = !_havePlaceSignature ||
                           !VectorMath.Approximately(_lastPlacementPosition, placementPosition, 0.0005f) ||
                           !Equivalent(_lastPlacementRotation, placementRotation) ||
                           !VectorMath.Approximately(_lastPlacementDelta, placementDelta, 0.0005f) ||
                           _lastReferenceFrame != referenceFrame ||
                           _lastAbsolutePosition != absolutePosition ||
                           _lastAxis != activeAxis ||
                           !Approximately(_lastStep, step) ||
                           _lastStepIsFine != activeStepIsFine ||
                           _lastMatched != matched;
            if (!changed) return false;

            _havePlaceSignature = true;
            _lastPlacementPosition = placementPosition;
            _lastPlacementRotation = placementRotation;
            _lastPlacementDelta = placementDelta;
            _lastReferenceFrame = referenceFrame;
            _lastAbsolutePosition = absolutePosition;
            _lastAxis = activeAxis;
            _lastStep = step;
            _lastStepIsFine = activeStepIsFine;
            _lastMatched = matched;
            return true;
        }

        private static bool TargetSignatureChanged(
            TargetInfo target,
            float targetDistance,
            int snapHostId)
        {
            bool changed = !_haveTargetSignature ||
                           _lastTargetId != target.InstanceId ||
                           !VectorMath.Approximately(_lastTargetPosition, target.Position, 0.0005f) ||
                           !Equivalent(_lastTargetRotation, target.Rotation) ||
                           !Approximately(_lastTargetDistance, targetDistance) ||
                           _lastSnapHostId != snapHostId;
            if (!changed) return false;

            _haveTargetSignature = true;
            _lastTargetId = target.InstanceId;
            _lastTargetPosition = target.Position;
            _lastTargetRotation = target.Rotation;
            _lastTargetDistance = targetDistance;
            _lastSnapHostId = snapHostId;
            return true;
        }

        private static string BuildPlacementText(
            Vector3 position,
            Quaternion rotation,
            Vector3 delta,
            PlacementReferenceFrame referenceFrame,
            bool absolutePosition,
            OrientationAxis axis,
            float step,
            bool isFine,
            bool matched)
        {
            OrientationAngles angles = OrientationAngles.FromQuaternion(rotation);
            string text = "<b>PRECISION</b>    " +
                          AngleValue("PITCH", angles.Pitch) + "    " +
                          AngleValue("ROLL", angles.Roll) + "    " +
                          AngleValue("YAW", angles.Yaw) + "\n";

            if (PluginConfig.ShowPositionReadout != null &&
                PluginConfig.ShowPositionReadout.Value)
            {
                string frame = referenceFrame == PlacementReferenceFrame.Local
                    ? "LOCAL"
                    : "WORLD";
                text += "POS " + VectorValue(position) +
                        "    |    Δ" + frame + " " + VectorValue(delta) +
                        (absolutePosition ? "    " + MatchOpen + "POSITION LOCK" + HighlightClose : string.Empty) +
                        "\n";
            }

            if (axis != OrientationAxis.None)
            {
                bool movement = axis == OrientationAxis.Sway ||
                                axis == OrientationAxis.Heave ||
                                axis == OrientationAxis.Surge;
                string unit = movement ? "m" : "°";
                text += ActiveOpen + "NEXT    " + (isFine ? "FINE " : string.Empty) +
                        AxisName(axis) + "    " + OrientationAngles.FormatValue(Mathf.Abs(step)) +
                        unit + HighlightClose;

                if (!isFine)
                {
                    float fineStep = FineStepForAxis(axis);
                    string fineShortcut = ShortcutLabel(FineShortcutForAxis(axis));
                    if (!string.IsNullOrEmpty(fineShortcut))
                    {
                        text += "    |    " + fineShortcut + " FINE " +
                                OrientationAngles.FormatValue(fineStep) + unit;
                    }
                }
            }
            else
            {
                text += "STEPS    ROTATE " +
                        OrientationAngles.FormatValue(PluginConfig.RotationStepDegrees) +
                        "°    |    FINE " +
                        OrientationAngles.FormatValue(PluginConfig.FineRotationStepDegrees) +
                        "°";
            }

            return matched
                ? text + "    " + MatchOpen + "MATCHED" + HighlightClose
                : text;
        }

        private static string BuildTargetText(
            TargetInfo target,
            float targetDistance,
            int snapHostId)
        {
            OrientationAngles angles = OrientationAngles.FromQuaternion(target.Rotation);
            string text = "<b>TARGET</b>    " +
                   AngleValue("PITCH", angles.Pitch) + "    " +
                   AngleValue("ROLL", angles.Roll) + "    " +
                   AngleValue("YAW", angles.Yaw) + "\n";
            if (PluginConfig.ShowPositionReadout != null &&
                PluginConfig.ShowPositionReadout.Value)
            {
                text += "POS " + VectorValue(target.Position) +
                        "    |    DIST " + FormatDistance(targetDistance) +
                        (snapHostId != 0
                            ? "    |    " + MatchOpen + "SNAP #" + snapHostId + HighlightClose
                            : string.Empty) +
                        "\n";
            }

            return text + MatchOpen + BuildMatchHint() + HighlightClose;
        }

        private static string VectorValue(Vector3 value) =>
            "(" + OrientationAngles.FormatValue(value.x) + ", " +
            OrientationAngles.FormatValue(value.y) + ", " +
            OrientationAngles.FormatValue(value.z) + ")";

        private static string FormatDistance(float value) =>
            QuaternionMath.IsFinite(value) && value >= 0f
                ? OrientationAngles.FormatValue(value) + "m"
                : "--";

        private static string BuildAxisGuideLegend() =>
            "<b>PIECE AXES</b>    " +
            "<color=" + AxisGuidePalette.PitchHex + "><b>PITCH X</b></color>" +
            "    |    " +
            "<color=" + AxisGuidePalette.RollHex + "><b>ROLL Z</b></color>" +
            "    |    " +
            "<color=" + AxisGuidePalette.YawHex + "><b>YAW Y</b></color>";

        private static string BuildMatchHint()
        {
            string hint = null;
            AppendMatchHint(ref hint, PluginConfig.MatchRotation, "EXACT");
            AppendMatchHint(ref hint, PluginConfig.MatchPitch, "PITCH");
            AppendMatchHint(ref hint, PluginConfig.MatchRoll, "ROLL");
            AppendMatchHint(ref hint, PluginConfig.MatchYaw, "YAW");
            return string.IsNullOrEmpty(hint) ? "MATCH ORIENTATION" : hint;
        }

        private static void AppendMatchHint(
            ref string hint,
            ConfigEntry<KeyboardShortcut> shortcutEntry,
            string action)
        {
            string shortcut = ShortcutLabel(shortcutEntry);
            if (string.IsNullOrEmpty(shortcut)) return;

            if (!string.IsNullOrEmpty(hint)) hint += "    |    ";
            hint += shortcut + " " + action;
        }

        private static string AngleValue(string axis, float angle) =>
            axis + " " + ValueOpen + OrientationAngles.FormatDegrees(angle) + ValueClose;

        private static string ShortcutLabel(ConfigEntry<KeyboardShortcut> entry)
        {
            if (entry == null || entry.Value.MainKey == KeyCode.None)
                return null;
            return entry.Value.ToString();
        }

        private static float FineStepForAxis(OrientationAxis axis)
        {
            switch (axis)
            {
                case OrientationAxis.Sway:
                    return PluginConfig.FineSideStepMeters.Value;
                case OrientationAxis.Heave:
                    return PluginConfig.FineUpDownStepMeters.Value;
                case OrientationAxis.Surge:
                    return PluginConfig.FineForwardBackStepMeters.Value;
                default:
                    return PluginConfig.FineRotationStepDegrees;
            }
        }

        private static ConfigEntry<KeyboardShortcut> FineShortcutForAxis(OrientationAxis axis)
        {
            switch (axis)
            {
                case OrientationAxis.Yaw:
                    return PluginConfig.FineYawWheelChord;
                case OrientationAxis.Pitch:
                    return PluginConfig.FinePitchWheelChord;
                case OrientationAxis.Roll:
                    return PluginConfig.FineRollWheelChord;
                case OrientationAxis.Sway:
                    return PluginConfig.FineMoveRight;
                case OrientationAxis.Heave:
                    return PluginConfig.FineMoveUp;
                case OrientationAxis.Surge:
                    return PluginConfig.FineMoveForward;
                default:
                    return null;
            }
        }

        private static string AxisName(OrientationAxis axis)
        {
            switch (axis)
            {
                case OrientationAxis.Yaw: return "YAW";
                case OrientationAxis.Pitch: return "PITCH";
                case OrientationAxis.Roll: return "ROLL";
                case OrientationAxis.Sway: return "SWAY X";
                case OrientationAxis.Heave: return "HEAVE Y";
                case OrientationAxis.Surge: return "SURGE Z";
                default: return string.Empty;
            }
        }

        private static TMP_Text PrepareEntry(GameObject entry)
        {
            TMP_Text label = entry.GetComponent<TMP_Text>() ??
                             entry.GetComponentInChildren<TMP_Text>(true);
            if (!label) return null;
            HorizontalOrVerticalLayoutGroup entryLayout =
                entry.GetComponent<HorizontalOrVerticalLayoutGroup>();
            if (entryLayout) entryLayout.enabled = false;
            ContentSizeFitter entryFitter = entry.GetComponent<ContentSizeFitter>();
            if (entryFitter) entryFitter.enabled = false;

            RectTransform entryRect = entry.transform as RectTransform;
            RectTransform labelRect = label.rectTransform;
            if (entryRect && labelRect && labelRect.parent == entryRect)
            {
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;
            }

            LayoutElement row = entry.GetComponent<LayoutElement>();
            if (!row) row = entry.AddComponent<LayoutElement>();
            row.ignoreLayout = false;
            row.flexibleWidth = 1f;
            label.richText = true;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            entry.SetActive(false);
            return label;
        }

        private static RectTransform CreateReadoutRoot(Transform selectedInfo)
        {
            if (!selectedInfo) return null;

            GameObject root = new GameObject(
                "RunicPrecision_OrientationReadout",
                typeof(RectTransform),
                typeof(LayoutElement),
                typeof(VerticalLayoutGroup));
            RectTransform rect = root.transform as RectTransform;
            rect.SetParent(selectedInfo, false);
            rect.localScale = Vector3.one;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            LayoutElement parentLayout = root.GetComponent<LayoutElement>();
            parentLayout.ignoreLayout = true;

            _readoutLayout = root.GetComponent<VerticalLayoutGroup>();
            _readoutLayout.childControlHeight = true;
            _readoutLayout.childControlWidth = true;
            _readoutLayout.childForceExpandHeight = false;
            _readoutLayout.childForceExpandWidth = true;
            return rect;
        }

        private static bool HasSupportedPanelTopology(
            RectTransform selectedInfo,
            RectTransform selectedPiece,
            RectTransform requirements) =>
            HasFixedAnchor(selectedInfo, 0.5f, 0f) &&
            HasFixedAnchor(selectedPiece, 0.5f, 1f) &&
            HasFixedAnchor(requirements, 0.5f, 0f);

        private static bool HasFixedAnchor(RectTransform rect, float x, float y) =>
            rect &&
            Mathf.Abs(rect.anchorMin.x - x) <= GeometryTolerance &&
            Mathf.Abs(rect.anchorMax.x - x) <= GeometryTolerance &&
            Mathf.Abs(rect.anchorMin.y - y) <= GeometryTolerance &&
            Mathf.Abs(rect.anchorMax.y - y) <= GeometryTolerance;

        private static void CapturePanelBaseline()
        {
            if (!_selectedInfoRect || !_selectedPieceRect || !_requirementsRect) return;

            _panelBaseSize = _selectedInfoRect.sizeDelta;
            _panelBasePosition = _selectedInfoRect.anchoredPosition;
            _requirementsTop = _requirementsRect.anchoredPosition.y +
                               (1f - _requirementsRect.pivot.y) *
                               _requirementsRect.rect.height;
            _topReserve = -_selectedPieceRect.anchoredPosition.y +
                          _selectedPieceRect.pivot.y *
                          _selectedPieceRect.rect.height;
            _baseSlack = _selectedInfoRect.rect.height -
                         _requirementsTop -
                         _topReserve;
            if (_buildMenuHelpRect)
                _buildMenuHelpBasePosition = _buildMenuHelpRect.anchoredPosition;
            _panelExtended = false;
        }

        private static float PanelCanvasScale(bool horizontal)
        {
            if (!_selectedInfoRect) return 1f;

            Transform canvas = _selectedInfoRect.root;
            Vector3 panelScale = _selectedInfoRect.lossyScale;
            Vector3 canvasScale = canvas ? canvas.lossyScale : Vector3.one;
            float numerator = horizontal
                ? Mathf.Abs(panelScale.x)
                : Mathf.Abs(panelScale.y);
            float denominator = horizontal
                ? Mathf.Abs(canvasScale.x)
                : Mathf.Abs(canvasScale.y);
            return denominator > 0.0001f && numerator > 0.0001f
                ? numerator / denominator
                : 1f;
        }

        private static void ApplyVisualStyle(bool force)
        {
            if (!_readoutRoot || !_selectedInfoRect ||
                !_placeText || !_targetText || !_axisGuideText)
                return;

            int fontSize = PluginConfig.ReadoutFontSize == null
                ? (int)DefaultFontSize
                : PluginConfig.ReadoutFontSize.Value;
            float panelWidth = _selectedInfoRect.rect.width;
            float scaleX = PanelCanvasScale(true);
            float scaleY = PanelCanvasScale(false);
            if (!force && _styledFontSize == fontSize &&
                Mathf.Abs(_styledPanelWidth - panelWidth) < 0.5f &&
                Mathf.Abs(_styledPanelScaleX - scaleX) < 0.001f &&
                Mathf.Abs(_styledPanelScaleY - scaleY) < 0.001f)
                return;

            _styledFontSize = fontSize;
            _styledPanelWidth = panelWidth;
            _styledPanelScaleX = scaleX;
            _styledPanelScaleY = scaleY;
            float localFontSize = fontSize / scaleY;
            float localMinimumFontSize = Mathf.Max(16f, fontSize * 0.58f) / scaleY;
            _styledRowHeight = Mathf.Ceil(fontSize * 4.25f / scaleY);
            _styledLegendRowHeight = Mathf.Ceil(fontSize * 1.5f / scaleY);
            _readoutLayout.spacing = RowGap / scaleY;

            StyleText(_placeText, localFontSize, localMinimumFontSize);
            StyleText(_targetText, localFontSize, localMinimumFontSize);
            StyleText(_axisGuideText, localFontSize, localMinimumFontSize);
            StyleRow(_placeEntry, _styledRowHeight);
            StyleRow(_targetEntry, _styledRowHeight);
            StyleRow(_axisGuideEntry, _styledLegendRowHeight);
            ResizeForActiveRows();
            RebuildLayout();
        }

        private static void StyleText(
            TMP_Text text,
            float fontSize,
            float minimumFontSize)
        {
            if (!text) return;
            text.enableAutoSizing = true;
            text.fontSize = fontSize;
            text.fontSizeMax = fontSize;
            text.fontSizeMin = Mathf.Min(fontSize, minimumFontSize);
            text.lineSpacing = -4f;
        }

        private static void StyleRow(GameObject entry, float rowHeight)
        {
            if (!entry) return;
            LayoutElement row = entry.GetComponent<LayoutElement>();
            if (!row) row = entry.AddComponent<LayoutElement>();
            row.minHeight = rowHeight;
            row.preferredHeight = rowHeight;
            row.flexibleHeight = 0f;
        }

        private static float CurrentRootHeight(float rowHeight, float legendRowHeight)
        {
            int rows = (_placeActive ? 1 : 0) +
                       (_targetActive ? 1 : 0) +
                       (_axisGuideActive ? 1 : 0);
            if (rows < 1) return 0f;

            float height = (_placeActive ? rowHeight : 0f) +
                           (_targetActive ? rowHeight : 0f) +
                           (_axisGuideActive ? legendRowHeight : 0f);
            float spacing = _readoutLayout ? _readoutLayout.spacing : RowGap;
            return height + (rows - 1) * spacing;
        }

        private static void ResizeForActiveRows()
        {
            if (!_readoutRoot || _styledFontSize < 1) return;

            bool active = _placeActive || _targetActive || _axisGuideActive;
            if (active)
            {
                float height = CurrentRootHeight(
                    _styledRowHeight,
                    _styledLegendRowHeight);
                ExtendPanel(height);
            }
            else
            {
                RestorePanelGeometry();
            }

            _readoutRoot.gameObject.SetActive(active);
        }

        private static void ExtendPanel(float contentHeight)
        {
            if (!_selectedInfoRect || !_readoutRoot || contentHeight <= 0f) return;

            if (_panelExtended)
            {
                // Another UI mod may legitimately change this panel. Only revise the height/Y
                // axes when they still match the values most recently applied by this presenter;
                // width/X always remain under the other layout owner's control.
                if (!OwnedPanelGeometryMatches(_panelAppliedSize, _panelAppliedPosition))
                    return;
            }
            else if (!OwnedPanelGeometryMatches(_panelBaseSize, _panelBasePosition))
            {
                // Adopt a compatible same-Hud baseline changed while the Runic rows were hidden.
                CapturePanelBaseline();
            }

            float scaleX = PanelCanvasScale(true);
            float scaleY = PanelCanvasScale(false);
            float horizontalInset = HorizontalInset / scaleX;
            float lowerPadding = PanelPadding / scaleY;
            float upperPadding = PanelPadding / scaleY;
            float extension = Mathf.Max(
                0f,
                contentHeight + lowerPadding + upperPadding - _baseSlack);

            Vector2 desiredSize = new Vector2(
                _selectedInfoRect.sizeDelta.x,
                _panelBaseSize.y + extension);
            Vector2 desiredPosition = new Vector2(
                _selectedInfoRect.anchoredPosition.x,
                _panelBasePosition.y + extension * _selectedInfoRect.pivot.y);
            _selectedInfoRect.sizeDelta = desiredSize;
            _selectedInfoRect.anchoredPosition = desiredPosition;
            _panelAppliedSize = desiredSize;
            _panelAppliedPosition = desiredPosition;

            _readoutRoot.anchorMin = Vector2.zero;
            _readoutRoot.anchorMax = Vector2.one;
            _readoutRoot.offsetMin = new Vector2(
                horizontalInset,
                _requirementsTop + lowerPadding);
            _readoutRoot.offsetMax = new Vector2(
                -horizontalInset,
                -(_topReserve + upperPadding));

            if (_buildMenuHelpRect &&
                (!_panelExtended ||
                 Mathf.Abs(
                     _buildMenuHelpRect.anchoredPosition.y -
                     _buildMenuHelpAppliedPosition.y) <= GeometryTolerance))
            {
                _buildMenuHelpAppliedPosition = new Vector2(
                    _buildMenuHelpRect.anchoredPosition.x,
                    _buildMenuHelpBasePosition.y + extension);
                _buildMenuHelpRect.anchoredPosition = _buildMenuHelpAppliedPosition;
            }

            if (_selectedInfoBackground &&
                !_backgroundRaycastSuppressed &&
                _selectedInfoBackground.raycastTarget)
            {
                _selectedInfoBackground.raycastTarget = false;
                _backgroundRaycastSuppressed = true;
            }

            _panelExtended = true;
            LayoutRebuilder.ForceRebuildLayoutImmediate(_selectedInfoRect);
        }

        private static void RestorePanelGeometry()
        {
            if (_panelExtended && _selectedInfoRect)
            {
                if (OwnedPanelGeometryMatches(_panelAppliedSize, _panelAppliedPosition))
                {
                    Vector2 restoredSize = _selectedInfoRect.sizeDelta;
                    restoredSize.y = _panelBaseSize.y;
                    _selectedInfoRect.sizeDelta = restoredSize;
                    Vector2 restoredPosition = _selectedInfoRect.anchoredPosition;
                    restoredPosition.y = _panelBasePosition.y;
                    _selectedInfoRect.anchoredPosition = restoredPosition;
                }

                if (_buildMenuHelpRect &&
                    Mathf.Abs(
                        _buildMenuHelpRect.anchoredPosition.y -
                        _buildMenuHelpAppliedPosition.y) <= GeometryTolerance)
                {
                    Vector2 restoredHelpPosition = _buildMenuHelpRect.anchoredPosition;
                    restoredHelpPosition.y = _buildMenuHelpBasePosition.y;
                    _buildMenuHelpRect.anchoredPosition = restoredHelpPosition;
                }

                LayoutRebuilder.ForceRebuildLayoutImmediate(_selectedInfoRect);
            }

            if (_backgroundRaycastSuppressed && _selectedInfoBackground)
                _selectedInfoBackground.raycastTarget = _backgroundRaycastOriginal;

            _panelExtended = false;
            _backgroundRaycastSuppressed = false;
        }

        private static bool OwnedPanelGeometryMatches(Vector2 size, Vector2 position) =>
            _selectedInfoRect &&
            Mathf.Abs(_selectedInfoRect.sizeDelta.y - size.y) <= GeometryTolerance &&
            Mathf.Abs(_selectedInfoRect.anchoredPosition.y - position.y) <= GeometryTolerance;

        private static Transform FindSelectedInfoRoot(Hud hud)
        {
            Transform selectedPiece = hud?.m_buildSelection?.transform?.parent;
            Transform selectedInfo = selectedPiece?.parent;
            return selectedInfo && hud.m_buildHud &&
                   selectedInfo.IsChildOf(hud.m_buildHud.transform)
                ? selectedInfo
                : null;
        }

        private static void SetPlaceActive(bool active)
        {
            bool changed = _placeActive != active || (_placeEntry && _placeEntry.activeSelf != active);
            if (!changed) return;

            _placeActive = active;
            if (_placeEntry && _placeEntry.activeSelf != active) _placeEntry.SetActive(active);
            ResizeForActiveRows();
            RebuildLayout();
        }

        private static void SetTargetActive(bool active)
        {
            bool changed = _targetActive != active || (_targetEntry && _targetEntry.activeSelf != active);
            if (!changed) return;

            _targetActive = active;
            if (_targetEntry && _targetEntry.activeSelf != active) _targetEntry.SetActive(active);
            ResizeForActiveRows();
            RebuildLayout();
        }

        private static void SetAxisGuideActive(bool active)
        {
            bool changed = _axisGuideActive != active ||
                           (_axisGuideEntry && _axisGuideEntry.activeSelf != active);
            if (!changed) return;

            _axisGuideActive = active;
            if (_axisGuideEntry && _axisGuideEntry.activeSelf != active)
                _axisGuideEntry.SetActive(active);
            ResizeForActiveRows();
            RebuildLayout();
        }

        private static void EnsureAttached()
        {
            Hud hud = Hud.instance;
            if (IsAttachedTo(hud))
                return;

            if (hud)
                Attach(hud);
        }

        private static bool IsAttachedTo(Hud hud) =>
            hud && _attachedHud == hud && _selectedInfoRoot &&
            _selectedInfoRect && _selectedPieceRect && _requirementsRect &&
            _selectedInfoBackground &&
            _placeEntry && _targetEntry && _axisGuideEntry &&
            _placeText && _targetText && _axisGuideText;

        private static void RebuildLayout()
        {
            if (_layoutRoot)
                LayoutRebuilder.ForceRebuildLayoutImmediate(_layoutRoot);
        }

        private static void LogFirstActivation()
        {
            if (_activationLogged || !_placeEntry) return;

            if (_placeEntry.activeInHierarchy)
            {
                _activationLogged = true;
                Diagnostics.Info(
                    "Runic v2 Precision orientation row is active in Valheim's selected-piece panel.");
            }
        }

        private static bool Equivalent(Quaternion left, Quaternion right) =>
            QuaternionMath.AreEquivalent(left, right);

        private static bool Approximately(float left, float right) =>
            Mathf.Abs(left - right) < 0.0001f;

        private static void WarnMissingTemplate()
        {
            if (_templateWarningLogged) return;
            _templateWarningLogged = true;
            Diagnostics.DisableAdapter(
                "Valheim's native selected-piece panel was not found, so the required live " +
                "orientation readout could not be created");
        }
    }
}

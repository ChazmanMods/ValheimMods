using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using QuietBuildRotation.Integration;
using QuietBuildRotation.UI;
using UnityEngine;

namespace QuietBuildRotation
{
    /// <summary>
    /// Coordinates one local placement session. All Harmony entry points are guarded here so a
    /// patched call becomes a vanilla/no-op path whenever startup verification or configuration
    /// disables Runic behavior.
    /// </summary>
    internal static class PlacementRuntime
    {
        private static readonly PlacementSession Session = new PlacementSession();
        private static readonly PoseController Pose = new PoseController(Session);
        private static readonly RelativeTransformHistory PlacementHistory =
            new RelativeTransformHistory();

        private static ValheimInputSource _inputSource;
        private static InputRouter _inputRouter;
        private static TargetInspector _targetInspector;
        private static InputBindings _bindings;
        private static bool _initialized;
        private static bool _precisionModeActive;

        private static bool _placementCaptureOpen;
        private static Piece _stagedPlacedPiece;
        private static string _capturePrefabName;

        private static Player _framePlayer;
        private static int _yawBeforeInput;
        private static float _scrollBeforeInput;
        private static bool _consumeWheel;
        private static OrientationAxis _displayAxis;
        private static float _displayStep;
        private static bool _displayStepIsFine;
        private static bool _axisGuidesHeld;
        private static int _axisGuideInputFrame = -1;

        private static Quaternion _lastPlacementRotation = Quaternion.identity;
        private static TargetInfo _lastTarget;

        private static InputChord _searchNextChord;
        private static InputChord _toggleFavoriteChord;
        private static InputChord _nextFavoriteChord;
        private static InputChord _nextRecentChord;
        private static InputChord _undoChord;
        private static InputChord _areaRepairChord;

        internal static void Initialize()
        {
            if (!BuildCatalogRuntime.Initialize(out string catalogError))
                throw new InvalidOperationException(catalogError);
            if (!BuildingMutationRuntime.Initialize(out string mutationError))
            {
                BuildCatalogRuntime.Shutdown();
                throw new InvalidOperationException(mutationError);
            }
            _inputSource = new ValheimInputSource();
            _inputRouter = new InputRouter(_inputSource);
            _targetInspector = new TargetInspector(PlacementAdapter.TryPieceRayTest);
            RebuildBindings();
            _initialized = true;
        }

        internal static void Shutdown()
        {
            Session.Exit();
            PlacementHistory.Clear();
            AbortPlacementCapture();
            _precisionModeActive = false;
            _targetInspector?.Clear();
            _targetInspector = null;
            _inputRouter = null;
            _inputSource = null;
            _framePlayer = null;
            _consumeWheel = false;
            BuildCatalogRuntime.Shutdown();
            BuildingMutationRuntime.Shutdown();
            ClearAxisGuideInput();
            AxisGuidePresenter.Destroy();
            _initialized = false;
            OrientationPresenter.Hide();
        }

        internal static void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus) return;
            _framePlayer = null;
            _consumeWheel = false;
            AbortPlacementCapture();
            ClearAxisGuideInput();
            AxisGuidePresenter.Hide();
            Session.ClearWheelPreview();
            OrientationPresenter.Hide();
        }

        internal static void OnConfigurationChanged()
        {
            if (!_initialized) return;
            RebuildBindings();
            OrientationPresenter.Invalidate();
            ClearAxisGuideInput();
            AxisGuidePresenter.Hide();

            if (PluginConfig.Enabled == null || PluginConfig.Enabled.Value) return;
            EndPlacementSession();
        }

        /// <summary>
        /// The keyboard radial is also evaluated before placement input. A held guide chord may
        /// own G only while Runic has an eligible rotatable placement ghost.
        /// </summary>
        internal static bool ShouldSuppressAxisGuideRadial(Player player)
        {
            bool reservesG = _initialized && _bindings.AxisGuides.Contains(KeyCode.G);
            bool reservationActive = _initialized &&
                                     AxisGuideRadialSuppressionPolicy.IsGuideReservationActive(
                                         _bindings.AxisGuides, _inputSource);
            return IsPrecisionModeActive &&
                   ShouldSuppressRadialInput(player, reservesG, reservationActive);
        }

        internal static void BeforePlacementInput(Player player, bool takeInput, float dt)
        {
            if (!_initialized || !Diagnostics.RuntimeAvailable ||
                player == null || player != Player.m_localPlayer)
                return;

            _framePlayer = player;
            _consumeWheel = false;
            AbortPlacementCapture();
            _yawBeforeInput = PlacementAdapter.GetPlaceRotation(player);
            _scrollBeforeInput = PlacementAdapter.GetScrollAmount(player);

            if (!PlacementAdapter.IsHammerBuildMode(player))
            {
                // Still sample and discard the wheel once through the gated router. This keeps
                // focus/build-mode transitions from replaying stale device input.
                ProcessGatedFrame(player, takeInput, false, false);
                EndPlacementSession();
                return;
            }

            int selectedPrefabId = PlacementAdapter.GetSelectedPrefabId(player);
            if (selectedPrefabId == 0)
            {
                ProcessGatedFrame(player, takeInput, false, false);
                EndPlacementSession();
                return;
            }

            float yawStep = PlacementAdapter.GetPlaceRotationDegrees(player);
            int yawIndex = PlacementAdapter.GetPlaceRotation(player);
            Quaternion vanillaBase = Quaternion.AngleAxis(yawIndex * yawStep, Vector3.up);
            if (Pose.ObserveSelection(selectedPrefabId, vanillaBase, yawIndex))
                OnSessionGenerationChanged();
            Piece selectedPiece = PlacementAdapter.GetSelectedPiece(player);
            BuildCatalogRuntime.ObserveSelection(player, selectedPiece);

            GameObject ghost = PlacementAdapter.GetPlacementGhost(player);
            Piece ghostPiece = ghost ? ghost.GetComponent<Piece>() : null;
            bool activeGhost = ghost && ghost.activeInHierarchy && ghostPiece;
            bool canRotate = activeGhost && ghostPiece.m_canRotate;
            bool inputGate = IsInputGateOpen(player, takeInput, activeGhost);
            if (!canRotate || !IsDisplayGateOpen(player, activeGhost))
                OrientationPresenter.Hide();

            bool toggled = _inputRouter.WasExactActionPressed(
                _bindings.PrecisionModeToggle, in _bindings);
            if (toggled)
                TogglePrecisionMode(player, vanillaBase, yawIndex);

            // Activation precedes routing so a pitch/roll/yaw wheel gesture made with the
            // toggle edge is applied immediately instead of being sampled while disabled and
            // discarded until a later placement action.
            InputContext context = BuildInputContext(
                takeInput && inputGate,
                activeGhost,
                canRotate,
                canRotate);
            InputFrameResult result = _inputRouter.ProcessFrame(in context, in _bindings);

            bool precisionActive = IsPrecisionModeActive;
            RecordAxisGuideInput(
                result.AxisGuidesHeld && precisionActive,
                canRotate,
                inputGate && precisionActive);
            _consumeWheel = result.ConsumeWheel;
            Pose.UpdateWheelPreview(result.ActiveAxis, result.ActiveStep, result.ActiveStepIsFine);
            _displayAxis = ToPresenterAxis(result.ActiveAxis);
            _displayStep = result.ActiveStep;
            _displayStepIsFine = result.ActiveStepIsFine;

            if (!result.WheelCommand.IsNone)
                Pose.Apply(result.WheelCommand, CurrentReferenceFrame);

            if (!result.DiscreteCommand.IsNone)
                ApplyDiscrete(player, ghost, result.DiscreteCommand);

            BeginPlacementCapture(player, selectedPiece, inputGate && precisionActive);
        }

        internal static void AfterPlacementInput(Player player, bool takeInput, float dt)
        {
            if (!_initialized || player == null || player != Player.m_localPlayer || player != _framePlayer)
                return;

            if (_consumeWheel)
            {
                // Vanilla reads the same physical wheel later in UpdatePlacement. Restore only
                // the two values a recognized Runic wheel chord could have changed.
                PlacementAdapter.SetPlaceRotation(player, _yawBeforeInput);
                PlacementAdapter.SetScrollAmount(player, _scrollBeforeInput);
            }

            if (Session.IsActive && PlacementAdapter.IsHammerBuildMode(player))
            {
                int yawIndex = PlacementAdapter.GetPlaceRotation(player);
                float yawStep = PlacementAdapter.GetPlaceRotationDegrees(player);
                Quaternion vanillaBase = Quaternion.AngleAxis(yawIndex * yawStep, Vector3.up);
                Pose.ReconcileVanillaYaw(yawIndex, yawStep, vanillaBase);
            }

            CommitPlacementCapture(player);

            _framePlayer = null;
            _consumeWheel = false;
        }

        internal static void AbortPlacementInput(Player player)
        {
            if (!_initialized || player == null || player != _framePlayer)
                return;

            // A recognized Runic chord may have temporarily neutralized the two values that
            // vanilla reads later in UpdatePlacement. Restore the exact outer-frame values even
            // when another prefix, Valheim, or a postfix aborts the call.
            if (_consumeWheel)
            {
                PlacementAdapter.SetPlaceRotation(player, _yawBeforeInput);
                PlacementAdapter.SetScrollAmount(player, _scrollBeforeInput);
            }

            _framePlayer = null;
            _consumeWheel = false;
            AbortPlacementCapture();
            ClearAxisGuideInput();
            AxisGuidePresenter.Hide();
        }

        internal static void OnPlacementGhostSetup(Player player)
        {
            if (!_initialized || !Diagnostics.CanRun || player == null || player != Player.m_localPlayer)
                return;

            if (!PlacementAdapter.IsHammerBuildMode(player))
            {
                EndPlacementSession();
                return;
            }

            int selectedPrefabId = PlacementAdapter.GetSelectedPrefabId(player);
            if (selectedPrefabId == 0) return;

            int yawIndex = PlacementAdapter.GetPlaceRotation(player);
            float yawStep = PlacementAdapter.GetPlaceRotationDegrees(player);
            Quaternion vanillaBase = Quaternion.AngleAxis(yawIndex * yawStep, Vector3.up);
            // Every new ghost is a new cache/presentation generation. The core preserves a
            // deliberate Runic pose for the same prefab, while untouched/translation-only state
            // adopts Valheim's fresh base rotation (including random-init yaw) exactly.
            Pose.BeginGhostGeneration(selectedPrefabId, vanillaBase, yawIndex);
            OnSessionGenerationChanged();
        }

        internal static void BeforePlacementGhost(Player player)
        {
            if (!_initialized || !Diagnostics.CanRun || player == null || player != Player.m_localPlayer)
                return;

            GameObject ghost = PlacementAdapter.GetPlacementGhost(player);
            _targetInspector.BeginPlacementUpdate(
                ghost,
                Session.Generation);
        }

        internal static void ObserveSnapSearch(
            Player player,
            Transform searchRoot,
            bool result,
            Transform ghostSnapPoint,
            Transform targetSnapPoint)
        {
            if (!_initialized || !Diagnostics.CanRun || player == null ||
                player != Player.m_localPlayer)
            {
                return;
            }

            _targetInspector.ObserveSnapSearch(
                searchRoot,
                result,
                ghostSnapPoint,
                targetSnapPoint);
        }

        internal static void AfterPlacementGhost(Player player)
        {
            if (!_initialized || !Diagnostics.CanRun || player == null || player != Player.m_localPlayer)
            {
                ClearAxisGuideInput();
                AxisGuidePresenter.Hide();
                OrientationPresenter.Hide();
                return;
            }

            GameObject ghost = PlacementAdapter.GetPlacementGhost(player);
            _targetInspector.CompletePlacementUpdate(
                ghost,
                Session.Generation);

            Piece ghostPiece = ghost && ghost.activeInHierarchy ? ghost.GetComponent<Piece>() : null;
            // Presentation follows placement state, not the per-frame input gate. TakeInput can
            // close transiently while Valheim still owns and renders a valid placement ghost;
            // tying the native rows to that flag makes the readout blink or disappear entirely.
            bool canShow = PlacementAdapter.IsHammerBuildMode(player) &&
                           ghostPiece && ghostPiece.m_canRotate &&
                           IsPrecisionModeActive &&
                           IsDisplayGateOpen(player, true);
            if (!canShow)
            {
                ClearAxisGuideInput();
                AxisGuidePresenter.Hide();
                OrientationPresenter.Hide();
                return;
            }

            if (_axisGuidesHeld && _axisGuideInputFrame == Time.frameCount)
                AxisGuidePresenter.Present(ghost);
            else
                AxisGuidePresenter.Hide();

            float now = Time.unscaledTime;
            _targetInspector.Refresh(player, ghost, now, false);
            _lastPlacementRotation = ghost.transform.rotation;
            _lastTarget = _targetInspector.Current;
            BuildReadoutGeometry(
                ghost,
                _lastTarget,
                out Vector3 placementDelta,
                out float targetDistance,
                out int snapHostId);
            OrientationPresenter.Present(
                true,
                ghost.transform.position,
                _lastPlacementRotation,
                placementDelta,
                CurrentReferenceFrame,
                Session.HasAbsolutePosition,
                _displayAxis,
                _displayStep,
                _displayStepIsFine,
                _lastTarget,
                targetDistance,
                snapHostId,
                Session.IsMatchFeedbackActive(now));
        }

        internal static void AbortPlacementGhost()
        {
            if (!_initialized) return;
            _targetInspector.AbortPlacementUpdate();
            ClearAxisGuideInput();
            AxisGuidePresenter.Hide();
            OrientationPresenter.Hide();
        }

        internal static Quaternion ComposeCandidateRotation(float x, float y, float z)
        {
            Quaternion vanilla = Quaternion.Euler(x, y, z);
            if (!_initialized || !Diagnostics.CanRun || !Session.IsActive ||
                !IsPrecisionModeActive)
                return vanilla;

            try
            {
                return Pose.ComposeEffectiveRotation(vanilla);
            }
            catch (Exception exception)
            {
                RunicHookGuard.Disable("placement rotation composition", exception);
                return vanilla;
            }
        }

        internal static bool ApplyCandidateTranslation(bool vanillaAltPlace, Player player)
        {
            if (!_initialized || !Diagnostics.CanRun || !Session.IsActive ||
                !IsPrecisionModeActive || player == null || player != Player.m_localPlayer)
                return vanillaAltPlace;

            try
            {
                GameObject ghost = PlacementAdapter.GetPlacementGhost(player);
                if (!ghost) return vanillaAltPlace;

                if (!ghost.activeInHierarchy ||
                    (Session.WorldOffset.sqrMagnitude < 0.00000001f &&
                     !Session.HasAbsolutePosition))
                {
                    return vanillaAltPlace;
                }

                // This hook runs after vanilla assigned candidate position/rotation but before its
                // automatic snap search and every placement-validity check.
                ghost.transform.position = Pose.ComposePosition(
                    ghost.transform.position,
                    ghost.transform.rotation);
                return vanillaAltPlace || Session.HasAbsolutePosition;
            }
            catch (Exception exception)
            {
                RunicHookGuard.Disable("pre-snap translation composition", exception);
                return vanillaAltPlace;
            }
        }

        private static void ApplyDiscrete(Player player, GameObject ghost, SemanticCommand command)
        {
            switch (command.Kind)
            {
                case SemanticCommandKind.Reset:
                    Pose.Reset();
                    PlacementAdapter.SetPlaceRotation(player, 0);
                    PlacementAdapter.SetScrollAmount(player, 0f);
                    _yawBeforeInput = 0;
                    _scrollBeforeInput = 0f;
                    PlacementAdapter.SetManualSnapPoint(player, -1);
                    _targetInspector.Clear();
                    _displayAxis = OrientationAxis.None;
                    _displayStep = 0f;
                    _displayStepIsFine = false;
                    Diagnostics.Verbose("Placement pose reset.");
                    return;

                case SemanticCommandKind.MatchOrientation:
                case SemanticCommandKind.MatchPitch:
                case SemanticCommandKind.MatchRoll:
                case SemanticCommandKind.MatchYaw:
                    ApplyOrientationMatch(player, ghost, command.Kind);
                    return;

                case SemanticCommandKind.MatchPositionX:
                case SemanticCommandKind.MatchPositionY:
                case SemanticCommandKind.MatchPositionZ:
                case SemanticCommandKind.MatchPosition:
                case SemanticCommandKind.MatchTransform:
                    ApplyPositionOrTransformMatch(player, ghost, command.Kind);
                    return;

                case SemanticCommandKind.MatchSnapSide:
                    ApplySnapSideMatch(player, ghost);
                    return;

                case SemanticCommandKind.RepeatTransform:
                    ApplyRepeatTransform(player);
                    return;

                default:
                    Pose.Apply(command, CurrentReferenceFrame);
                    return;
            }
        }

        private static void ApplyOrientationMatch(
            Player player,
            GameObject ghost,
            SemanticCommandKind kind)
        {
            float now = Time.unscaledTime;
            if (!_targetInspector.TryGetRotationForMatch(
                    player, ghost, now, out Quaternion targetRotation, out TargetInfo target))
            {
                player.Message(
                    MessageHud.MessageType.Center,
                    "Aim at a build piece to match orientation.",
                    0,
                    null);
                return;
            }

            bool matched;
            string component;
            switch (kind)
            {
                case SemanticCommandKind.MatchPitch:
                    component = "Pitch";
                    matched = Pose.MatchOrientationComponent(
                        targetRotation, RotationAxis.Pitch, now);
                    break;
                case SemanticCommandKind.MatchRoll:
                    component = "Roll";
                    matched = Pose.MatchOrientationComponent(
                        targetRotation, RotationAxis.Roll, now);
                    break;
                case SemanticCommandKind.MatchYaw:
                    component = "Yaw";
                    matched = Pose.MatchOrientationComponent(
                        targetRotation, RotationAxis.Yaw, now);
                    break;
                default:
                    component = "Orientation";
                    matched = Pose.MatchOrientation(targetRotation, now);
                    break;
            }

            if (!matched)
            {
                player.Message(
                    MessageHud.MessageType.Center,
                    component + " could not be matched.",
                    0,
                    null);
                return;
            }

            _targetInspector.MarkMatched(now);
            _lastTarget = target;
            player.Message(
                MessageHud.MessageType.Center,
                component + " matched.",
                0,
                null);
            Diagnostics.Verbose("Matched target " + component.ToLowerInvariant() + ".");
        }

        private static void ApplyPositionOrTransformMatch(
            Player player,
            GameObject ghost,
            SemanticCommandKind kind)
        {
            float now = Time.unscaledTime;
            if (!_targetInspector.TryGetRotationForMatch(
                    player, ghost, now, out Quaternion targetRotation, out TargetInfo target))
            {
                player.Message(
                    MessageHud.MessageType.Center,
                    "Aim at a build piece to match position or transform.",
                    0,
                    null);
                return;
            }

            bool matched;
            string label;
            switch (kind)
            {
                case SemanticCommandKind.MatchPositionX:
                    label = "World X";
                    matched = Pose.MatchPositionComponent(
                        target.Position, SemanticCommandKind.MatchPositionX, now);
                    break;
                case SemanticCommandKind.MatchPositionY:
                    label = "World Y";
                    matched = Pose.MatchPositionComponent(
                        target.Position, SemanticCommandKind.MatchPositionY, now);
                    break;
                case SemanticCommandKind.MatchPositionZ:
                    label = "World Z";
                    matched = Pose.MatchPositionComponent(
                        target.Position, SemanticCommandKind.MatchPositionZ, now);
                    break;
                case SemanticCommandKind.MatchTransform:
                    label = "Full transform";
                    matched = Pose.MatchTransform(
                        new PlacementTransform(target.Position, targetRotation), now);
                    break;
                default:
                    label = "Position";
                    matched = Pose.MatchPosition(target.Position, now);
                    break;
            }

            if (!matched)
            {
                player.Message(MessageHud.MessageType.Center, label + " could not be matched.", 0, null);
                return;
            }

            _targetInspector.MarkMatched(now);
            _lastTarget = target;
            player.Message(
                MessageHud.MessageType.Center,
                label + " matched; vanilla validation remains authoritative.",
                0,
                null);
        }

        private static void ApplySnapSideMatch(Player player, GameObject ghost)
        {
            float now = Time.unscaledTime;
            if (!_targetInspector.TryGetSnapAlignmentForMatch(
                    ghost,
                    Session.Generation,
                    out PlacementTransform aligned,
                    out Piece host) ||
                !Pose.MatchTransform(aligned, now))
            {
                player.Message(
                    MessageHud.MessageType.Center,
                    "No completed vanilla snap pair is available. Select a source with Tab, then approach a target side.",
                    0,
                    null);
                return;
            }

            player.Message(
                MessageHud.MessageType.Center,
                "Snap side locked with opposed normals and target tangent; validation remains vanilla.",
                0,
                null);
            Diagnostics.Verbose("Snap-side match locked to host " + host.GetInstanceID() + ".");
        }

        private static void ApplyRepeatTransform(Player player)
        {
            float now = Time.unscaledTime;
            if (!PlacementHistory.TryPredictNext(out PlacementTransform predicted) ||
                !Pose.MatchTransform(predicted, now))
            {
                player.Message(
                    MessageHud.MessageType.Center,
                    "Repeat requires two successful placements in this session.",
                    0,
                    null);
                return;
            }

            player.Message(
                MessageHud.MessageType.Center,
                "Relative offset and rotation repeated; placement still requires vanilla approval.",
                0,
                null);
        }

        private static InputContext BuildInputContext(
            bool takeInput,
            bool hasActiveGhost,
            bool canRotate,
            bool canTranslate)
        {
            return new InputContext(
                PluginConfig.Enabled != null && PluginConfig.Enabled.Value &&
                IsPrecisionModeActive,
                Diagnostics.RuntimeAvailable,
                Application.isFocused,
                takeInput,
                hasActiveGhost,
                canRotate,
                canTranslate,
                PluginConfig.RotationIncrementsPerCircle.Value,
                PluginConfig.FineRotationIncrementsPerCircle.Value,
                PluginConfig.SideStepMeters.Value,
                PluginConfig.UpDownStepMeters.Value,
                PluginConfig.ForwardBackStepMeters.Value,
                PluginConfig.FineSideStepMeters.Value,
                PluginConfig.FineUpDownStepMeters.Value,
                PluginConfig.FineForwardBackStepMeters.Value);
        }

        private static void ProcessGatedFrame(
            Player player,
            bool takeInput,
            bool hasActiveGhost,
            bool canRotate)
        {
            if (_inputRouter == null) return;
            InputContext context = BuildInputContext(false, hasActiveGhost, canRotate, false);
            InputFrameResult result = _inputRouter.ProcessFrame(in context, in _bindings);
            RecordAxisGuideInput(result.AxisGuidesHeld, canRotate, false);
        }

        private static bool IsInputGateOpen(Player player, bool takeInput, bool hasActiveGhost)
        {
            if (!takeInput)
                return false;

            return IsDisplayGateOpen(player, hasActiveGhost);
        }

        private static bool ShouldSuppressRadialInput(
            Player player,
            bool configuredBindingReservesKey,
            bool configuredChordIsHeld)
        {
            bool isLocalPlayer = player != null && player == Player.m_localPlayer;
            bool inPlaceMode = isLocalPlayer && PlacementAdapter.IsHammerBuildMode(player);
            GameObject ghost = inPlaceMode ? PlacementAdapter.GetPlacementGhost(player) : null;
            Piece ghostPiece = ghost && ghost.activeInHierarchy ? ghost.GetComponent<Piece>() : null;
            bool activeRotatableGhost = ghostPiece && ghostPiece.m_canRotate;
            bool displayGateOpen = activeRotatableGhost &&
                                   IsDisplayGateOpen(player, true) &&
                                   Hud.instance != null && !Hud.InRadial();
            bool inputGateOpen = displayGateOpen && PlacementAdapter.CanTakeInput(player);

            return AxisGuideRadialSuppressionPolicy.ShouldSuppress(
                Diagnostics.CanRun,
                isLocalPlayer,
                inPlaceMode,
                activeRotatableGhost,
                displayGateOpen,
                inputGateOpen,
                configuredBindingReservesKey,
                configuredChordIsHeld);
        }

        private static bool IsDisplayGateOpen(Player player, bool hasActiveGhost)
        {
            if (!Diagnostics.CanRun || player == null || !Application.isFocused || !hasActiveGhost)
                return false;
            if (Console.IsVisible() || (Chat.instance != null && Chat.instance.HasFocus()) ||
                Menu.IsVisible() || Game.IsPaused())
                return false;
            if (Hud.instance == null || Hud.IsPieceSelectionVisible() || TextInput.IsVisible())
                return false;

            InventoryGui inventory = InventoryGui.instance;
            return inventory == null ||
                   (!InventoryGui.IsVisible() &&
                    !inventory.IsSkillsPanelOpen &&
                    !inventory.IsTrophisPanelOpen &&
                    !inventory.IsTextPanelOpen);
        }

        private static void EndPlacementSession()
        {
            Pose.ExitPlacement();
            PlacementHistory.Clear();
            BuildingMutationRuntime.EndPlacementSession();
            _precisionModeActive = false;
            AbortPlacementCapture();
            _targetInspector?.Clear();
            ClearAxisGuideInput();
            AxisGuidePresenter.Hide();
            OrientationPresenter.Hide();
        }

        private static void OnSessionGenerationChanged()
        {
            _targetInspector?.Clear();
            _lastTarget = default;
            ClearAxisGuideInput();
            AxisGuidePresenter.Hide();
            OrientationPresenter.Invalidate();
        }

        internal static void UpdateUtilities()
        {
            if (!_initialized || !Diagnostics.CanRun || !Application.isFocused ||
                _inputRouter == null)
                return;

            Player player = Player.m_localPlayer;
            if (!player || !PlacementAdapter.IsHammerBuildMode(player) ||
                Console.IsVisible() || (Chat.instance != null && Chat.instance.HasFocus()) ||
                Menu.IsVisible() || Game.IsPaused() || TextInput.IsVisible())
                return;

            bool pieceMenu = Hud.instance != null && Hud.IsPieceSelectionVisible();
            if (!pieceMenu)
                TryTogglePrecisionModeFromUpdate(player);

            string message = null;
            if (pieceMenu)
            {
                BuildCatalogAction? action = null;
                if (_inputRouter.WasExactActionPressed(_searchNextChord, in _bindings))
                    action = BuildCatalogAction.SearchNext;
                else if (_inputRouter.WasExactActionPressed(_toggleFavoriteChord, in _bindings))
                    action = BuildCatalogAction.ToggleFavorite;
                else if (_inputRouter.WasExactActionPressed(_nextFavoriteChord, in _bindings))
                    action = BuildCatalogAction.NextFavorite;
                else if (_inputRouter.WasExactActionPressed(_nextRecentChord, in _bindings))
                    action = BuildCatalogAction.NextRecent;

                if (action.HasValue)
                    BuildCatalogRuntime.Execute(player, action.Value, out message);
            }
            else if (IsPrecisionModeActive && PlacementAdapter.CanTakeInput(player))
            {
                if (_inputRouter.WasExactActionPressed(_undoChord, in _bindings))
                    BuildingMutationRuntime.TryUndo(player, out message);
                else if (_inputRouter.WasExactActionPressed(_areaRepairChord, in _bindings))
                    BuildingMutationRuntime.TryAreaRepair(player, out message);
            }

            if (!string.IsNullOrEmpty(message))
                player.Message(MessageHud.MessageType.Center, message, 0, null);
        }

        /// <summary>
        /// Samples the explicit mode toggle from the plugin's every-frame Update loop. Valheim
        /// does not guarantee that Player.UpdatePlacement runs on every frame in which a valid
        /// build-mode key edge occurs, so placement-only polling can lose an otherwise valid P.
        /// The shared ValheimInputSource consumes the edge once even if placement also runs.
        /// </summary>
        private static void TryTogglePrecisionModeFromUpdate(Player player)
        {
            if (PluginConfig.Enabled == null || !PluginConfig.Enabled.Value ||
                !IsToggleGateOpen(player))
                return;

            int selectedPrefabId = PlacementAdapter.GetSelectedPrefabId(player);
            if (selectedPrefabId == 0)
                return;

            int yawIndex = PlacementAdapter.GetPlaceRotation(player);
            float yawStep = PlacementAdapter.GetPlaceRotationDegrees(player);
            Quaternion vanillaBase = Quaternion.AngleAxis(yawIndex * yawStep, Vector3.up);
            if (Pose.ObserveSelection(selectedPrefabId, vanillaBase, yawIndex))
                OnSessionGenerationChanged();

            if (_inputRouter.WasExactActionPressed(
                    _bindings.PrecisionModeToggle, in _bindings))
                TogglePrecisionMode(player, vanillaBase, yawIndex);
        }

        private static bool IsToggleGateOpen(Player player)
        {
            if (!player || player != Player.m_localPlayer ||
                !PlacementAdapter.IsHammerBuildMode(player) || !Application.isFocused)
                return false;
            if (Console.IsVisible() || (Chat.instance != null && Chat.instance.HasFocus()) ||
                Menu.IsVisible() || Game.IsPaused() || TextInput.IsVisible())
                return false;
            if (Hud.instance == null || Hud.IsPieceSelectionVisible())
                return false;

            InventoryGui inventory = InventoryGui.instance;
            return inventory == null ||
                   (!InventoryGui.IsVisible() &&
                    !inventory.IsSkillsPanelOpen &&
                    !inventory.IsTrophisPanelOpen &&
                    !inventory.IsTextPanelOpen);
        }

        /// <summary>
        /// Keeps the Precision rows attached to Valheim's selected-piece panel without changing
        /// ownership or visibility of the native build HUD. The Runic-only children hide while
        /// the piece-selection menu is open, leaving that menu entirely under vanilla control.
        /// </summary>
        internal static void AfterHudUpdateBuild(Hud hud, Player player)
        {
            if (!hud)
                return;

            OrientationPresenter.Attach(hud);
            if (!_initialized || !Diagnostics.CanRun || !player ||
                player != Player.m_localPlayer || !PlacementAdapter.IsHammerBuildMode(player) ||
                !IsPrecisionModeActive ||
                Hud.IsPieceSelectionVisible())
                OrientationPresenter.Hide();
        }

        internal static void ObservePieceCreator(Piece piece, long creator)
        {
            if (!_placementCaptureOpen || !_framePlayer || !piece ||
                creator != _framePlayer.GetPlayerID() || _stagedPlacedPiece)
                return;
            if (!string.Equals(
                    Utils.GetPrefabName(piece.gameObject),
                    _capturePrefabName,
                    StringComparison.Ordinal) ||
                !VectorMath.IsFinite(piece.transform.position) ||
                !QuaternionMath.TryNormalize(piece.transform.rotation, out _))
                return;
            _stagedPlacedPiece = piece;
        }

        private static void BeginPlacementCapture(
            Player player,
            Piece selectedPiece,
            bool inputGate)
        {
            string prefabName = inputGate && selectedPiece
                ? Utils.GetPrefabName(selectedPiece.gameObject)
                : null;
            _placementCaptureOpen = inputGate && player && selectedPiece &&
                                    player == Player.m_localPlayer &&
                                    !string.IsNullOrEmpty(prefabName) &&
                                    prefabName.Length <= BoundedBuildCatalog.MaximumIdLength;
            _capturePrefabName = _placementCaptureOpen ? prefabName : null;
            _stagedPlacedPiece = null;
        }

        private static void CommitPlacementCapture(Player player)
        {
            Piece placed = _stagedPlacedPiece;
            bool commit = _placementCaptureOpen && player && player == _framePlayer && placed;
            AbortPlacementCapture();
            if (!commit) return;

            PlacementTransform transform = new PlacementTransform(
                placed.transform.position,
                placed.transform.rotation);
            if (!PlacementHistory.ObserveCommit(transform)) return;
            BuildingMutationRuntime.ObserveCommittedPlacement(player, placed);
        }

        private static void AbortPlacementCapture()
        {
            _placementCaptureOpen = false;
            _stagedPlacedPiece = null;
            _capturePrefabName = null;
        }

        private static void TogglePrecisionMode(
            Player player,
            Quaternion vanillaBase,
            int vanillaYawIndex)
        {
            if (PluginConfig.RequirePrecisionMode == null ||
                !PluginConfig.RequirePrecisionMode.Value)
            {
                player.Message(
                    MessageHud.MessageType.Center,
                    "Precision mode is always active because RequirePrecisionMode is disabled.",
                    0,
                    null);
                return;
            }

            _precisionModeActive = !_precisionModeActive;
            Session.ResetPose(vanillaBase, vanillaYawIndex);
            _targetInspector.Clear();
            ClearAxisGuideInput();
            AxisGuidePresenter.Hide();
            OrientationPresenter.Invalidate();
            if (!_precisionModeActive) OrientationPresenter.Hide();
            player.Message(
                MessageHud.MessageType.Center,
                _precisionModeActive
                    ? "Runic precision mode enabled."
                    : "Runic precision mode disabled; placement is vanilla.",
                0,
                null);
        }

        private static bool IsPrecisionModeActive =>
            PluginConfig.RequirePrecisionMode == null ||
            !PluginConfig.RequirePrecisionMode.Value ||
            _precisionModeActive;

        private static PlacementReferenceFrame CurrentReferenceFrame =>
            PluginConfig.ReferenceFrame?.Value ?? PlacementReferenceFrame.World;

        private static void BuildReadoutGeometry(
            GameObject ghost,
            TargetInfo target,
            out Vector3 placementDelta,
            out float targetDistance,
            out int snapHostId)
        {
            placementDelta = Session.WorldOffset;
            if (CurrentReferenceFrame == PlacementReferenceFrame.Local && ghost)
            {
                placementDelta = QuaternionMath.InverseSafe(ghost.transform.rotation) *
                                 placementDelta;
            }

            targetDistance = target.IsValid && ghost
                ? Vector3.Distance(ghost.transform.position, target.Position)
                : -1f;
            if (!QuaternionMath.IsFinite(targetDistance)) targetDistance = -1f;
            if (!_targetInspector.TryGetValidatedSnapHost(
                    ghost,
                    Session.Generation,
                    out snapHostId))
            {
                snapHostId = 0;
            }
        }

        private static void RebuildBindings()
        {
            _bindings = new InputBindings(
                new RotationInputBindings(
                    ToInputChord(PluginConfig.YawWheelChord),
                    ToInputChord(PluginConfig.PitchWheelChord),
                    ToInputChord(PluginConfig.RollWheelChord),
                    ToInputChord(PluginConfig.FineYawWheelChord),
                    ToInputChord(PluginConfig.FinePitchWheelChord),
                    ToInputChord(PluginConfig.FineRollWheelChord)),
                ToInputChord(PluginConfig.MatchRotation),
                ToInputChord(PluginConfig.MatchPitch),
                ToInputChord(PluginConfig.MatchRoll),
                ToInputChord(PluginConfig.MatchYaw),
                ToInputChord(PluginConfig.MatchPositionX),
                ToInputChord(PluginConfig.MatchPositionY),
                ToInputChord(PluginConfig.MatchPositionZ),
                ToInputChord(PluginConfig.MatchPosition),
                ToInputChord(PluginConfig.MatchTransform),
                ToInputChord(PluginConfig.MatchSnapSide),
                ToInputChord(PluginConfig.RepeatTransform),
                ToInputChord(PluginConfig.ResetPitch),
                ToInputChord(PluginConfig.ResetRoll),
                ToInputChord(PluginConfig.ResetYaw),
                ToInputChord(PluginConfig.ResetSway),
                ToInputChord(PluginConfig.ResetHeave),
                ToInputChord(PluginConfig.ResetSurge),
                ToInputChord(PluginConfig.PrecisionModeToggle),
                ToInputChord(PluginConfig.AxisGuides),
                ToInputChord(PluginConfig.Reset),
                new MovementInputBindings(
                    ToInputChord(PluginConfig.MoveUp),
                    ToInputChord(PluginConfig.MoveDown),
                    ToInputChord(PluginConfig.MoveLeft),
                    ToInputChord(PluginConfig.MoveRight),
                    ToInputChord(PluginConfig.MoveForward),
                    ToInputChord(PluginConfig.MoveBackward),
                    ToInputChord(PluginConfig.FineMoveUp),
                    ToInputChord(PluginConfig.FineMoveDown),
                    ToInputChord(PluginConfig.FineMoveLeft),
                    ToInputChord(PluginConfig.FineMoveRight),
                    ToInputChord(PluginConfig.FineMoveForward),
                    ToInputChord(PluginConfig.FineMoveBackward)));

            _searchNextChord = ToInputChord(PluginConfig.SearchNext);
            _toggleFavoriteChord = ToInputChord(PluginConfig.ToggleFavorite);
            _nextFavoriteChord = ToInputChord(PluginConfig.NextFavorite);
            _nextRecentChord = ToInputChord(PluginConfig.NextRecent);
            _undoChord = ToInputChord(PluginConfig.UndoLastPlacement);
            _areaRepairChord = ToInputChord(PluginConfig.AreaRepair);
        }

        private static InputChord ToInputChord(ConfigEntry<KeyboardShortcut> entry)
        {
            if (entry == null)
                return new InputChord(KeyCode.None);

            KeyboardShortcut shortcut = entry.Value;
            List<KeyCode> modifiers = new List<KeyCode>();
            foreach (KeyCode modifier in shortcut.Modifiers)
                modifiers.Add(modifier);
            return new InputChord(shortcut.MainKey, modifiers.ToArray());
        }

        private static OrientationAxis ToPresenterAxis(RotationAxis axis)
        {
            switch (axis)
            {
                case RotationAxis.Yaw: return OrientationAxis.Yaw;
                case RotationAxis.Pitch: return OrientationAxis.Pitch;
                case RotationAxis.Roll: return OrientationAxis.Roll;
                default: return OrientationAxis.None;
            }
        }

        private static void RecordAxisGuideInput(
            bool held,
            bool canRotate,
            bool inputGate)
        {
            _axisGuideInputFrame = Time.frameCount;
            _axisGuidesHeld = held && canRotate && inputGate;
            if (!_axisGuidesHeld)
                AxisGuidePresenter.Hide();
        }

        private static void ClearAxisGuideInput()
        {
            _axisGuidesHeld = false;
            _axisGuideInputFrame = -1;
        }

    }
}

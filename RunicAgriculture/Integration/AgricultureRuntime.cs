using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using BepInEx.Logging;
using RunicAgriculture.Core;
using UnityEngine;

namespace RunicAgriculture.Integration
{
    internal sealed class AgricultureRuntime : IDisposable
    {
        private const int UnlimitedActions = 1_000_000;
        private readonly IAgriculturePatternService _patternService;
        private readonly ManualLogSource _log;
        private readonly ValheimPlacementValidator _validator = new ValheimPlacementValidator();
        private readonly PreviewPool _previewPool =
            new PreviewPool(AgricultureConfig.HardMaximumPreview);
        private readonly ReplantConfirmation<Vector3> _replant =
            new ReplantConfirmation<Vector3>(AgricultureConfig.HardMaximumHarvest);
        private readonly Collider[] _alignmentHits = new Collider[96];
        private readonly Collider[] _harvestHits = new Collider[160];
        private readonly Dictionary<string, string> _matureToPlant =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly AgricultureControlBar _controlBar = new AgricultureControlBar();
        private PreviewSnapshot _lastPreview;
        private bool _disabledForSession;
        private float _nextPreviewUpdate;
        private bool _controllerActionsVerified;
        private string _controllerPathSignature;
        private string _lastControllerProblem;
        private int _controllerHarvestRequestFrame = -1;
        private ControllerEditorField _controllerEditorField = ControllerEditorField.Rows;
        private int _previewValidCount;
        private int _previewGroundValidCount;
        private int _previewTotalCount;
        private string _previewIssueSummary = string.Empty;
        private string _previewLimitSummary = string.Empty;
        private string _previewBatchActionSummary = string.Empty;
        private float _patternYawDegrees;
        private int _mutationActive;
        private Piece _seedBudgetPiece;
        private Vector3 _seedBudgetPlayerPosition;
        private int _seedBudgetValue;
        private float _seedBudgetExpiresAt;

        internal AgricultureRuntime(
            IAgriculturePatternService patternService,
            ManualLogSource log)
        {
            _patternService = patternService ?? throw new ArgumentNullException(nameof(patternService));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            ValheimAccess.Verify();
        }

        internal bool IsOperational =>
            !_disabledForSession && (AgricultureConfig.Enabled?.Value ?? false);

        internal void DisableForSession(string reasonCode)
        {
            if (_disabledForSession) return;
            _disabledForSession = true;
            _lastPreview = null;
            _previewPool.Hide();
            RestoreBuildHintsIfOwned();
            AgricultureInputConsumption.Reset();
            Message(Player.m_localPlayer,
                "Runic Agriculture disabled for this session after an error. Check LogOutput.log.");
            Publish(reasonCode, "Agriculture runtime disabled after an unexpected failure.",
                "Restart Valheim, then check BepInEx/LogOutput.log before using batch actions.");
        }

        internal string ConfigurationSummary() => AgricultureFeedbackText.Configuration(
            AgricultureConfig.Enabled.Value,
            AgricultureConfig.Pattern.Value,
            AgricultureConfig.Rows.Value,
            AgricultureConfig.Columns.Value,
            AgricultureConfig.Spacing.Value,
            AgricultureConfig.HardMaximumPreview,
            AgricultureConfig.HarvestRadius.Value,
            AgricultureConfig.MaximumHarvest.Value);

        internal string ControlSummary()
        {
            AgricultureControllerBindings controller = AgricultureConfig.CurrentControllerBindings();
            string controllerText = AgricultureConfig.ControllerEnabled.Value
                ? controller.TryValidate(out string problem)
                    ? "controller confirm " + controller.ConfirmChord + ", cycle " +
                      controller.CycleChord + ", harvest " + controller.AreaHarvestChord +
                      ", choose setting " +
                      ControllerActionDisplay.Friendly(controller.PreviousEditorField) + "/" +
                      ControllerActionDisplay.Friendly(controller.NextEditorField) +
                      " unmodified in crop preview, adjust " +
                      ControllerActionDisplay.Friendly(controller.DecreaseEditorValue) + "/" +
                      ControllerActionDisplay.Friendly(controller.IncreaseEditorValue) +
                      " unmodified in crop preview"
                    : "controller disabled by invalid bindings (" + problem + ")"
                : "controller controls disabled";
            return "keyboard plant Left Click" +
                   ", cycle " + ShortcutLabel(AgricultureConfig.CyclePattern.Value) +
                   ", harvest " + ShortcutLabel(AgricultureConfig.AreaHarvest.Value) +
                   ", replant " + ShortcutLabel(AgricultureConfig.ConfirmReplant.Value) +
                   ", rows " + ShortcutLabel(AgricultureConfig.DecreaseRows.Value) + "/" +
                   ShortcutLabel(AgricultureConfig.IncreaseRows.Value) +
                   ", columns " + ShortcutLabel(AgricultureConfig.DecreaseColumns.Value) + "/" +
                   ShortcutLabel(AgricultureConfig.IncreaseColumns.Value) +
                   ", side " + ShortcutLabel(AgricultureConfig.ToggleShapeSide.Value) +
                   ", trapezoid left " + ShortcutLabel(AgricultureConfig.DecreaseLeftPinch.Value) + "/" +
                   ShortcutLabel(AgricultureConfig.IncreaseLeftPinch.Value) +
                   ", right " + ShortcutLabel(AgricultureConfig.DecreaseRightPinch.Value) + "/" +
                   ShortcutLabel(AgricultureConfig.IncreaseRightPinch.Value) +
                   ", live wheel rotate Wheel, rows Alt+Wheel, columns Shift+Wheel, spacing Alt+Shift+Wheel" +
                   ", direct patterns Numpad 1-7, cycle Alt+BuildMenu" +
                   "; " + controllerText + ".";
        }

        internal void OnConfigurationChanged(string changedSetting)
        {
            _lastPreview = null;
            _nextPreviewUpdate = 0f;
            _seedBudgetExpiresAt = 0f;
            _controllerEditorField = ControllerPatternEditor.Normalize(
                AgricultureConfig.Pattern.Value,
                _controllerEditorField);
            if (changedSetting != null &&
                (changedSetting.StartsWith("Controller Controls/", StringComparison.Ordinal) ||
                 changedSetting.StartsWith("Controller Pattern Editor/", StringComparison.Ordinal)))
            {
                _controllerActionsVerified = false;
                _controllerPathSignature = null;
                _lastControllerProblem = null;
            }
            if (!IsOperational) _previewPool.Hide();
            string summary = ConfigurationSummary();
            _log.LogInfo("Runic Agriculture configuration changed (" + changedSetting + "): " + summary);
            _log.LogInfo("Runic Agriculture controls after configuration change: " + ControlSummary());
            Player player = Player.m_localPlayer;
            if (player != null)
                Message(player, "Runic Agriculture updated: " + summary + ".");
            AgricultureControllerBindings bindings = AgricultureConfig.CurrentControllerBindings();
            if (AgricultureConfig.ControllerEnabled.Value && !bindings.TryValidate(out string problem))
                ReportControllerProblem(player, problem);
        }

        internal void ApplyBuildHintsReplacement(KeyHints hints)
        {
            GameObject buildHints = hints != null ? hints.m_buildHints : null;
            Player player = Player.m_localPlayer;
            if (buildHints == null || !IsPlantingControlContext(player))
            {
                _controlBar.Restore();
                return;
            }

            // Respect the player's global Key Hints setting and any earlier compatibility patch.
            // Agriculture replaces the content of the same native panel; it does not draw a
            // second IMGUI window over the game.
            if (!buildHints.activeInHierarchy)
            {
                _controlBar.Restore();
                return;
            }
            bool controller = false;
            try { controller = ZInput.IsGamepadActive(); }
            catch (Exception) { }
            _controlBar.Apply(
                hints,
                BuildControlBarContent(player, controller),
                AgricultureConfig.ControlBarScale.Value);
        }

        private AgricultureControlBarContent BuildControlBarContent(Player player, bool controller)
        {
            PlantPattern pattern = AgricultureConfig.Pattern.Value;
            Piece piece = player?.GetSelectedPiece();
            string crop = piece != null ? piece.m_name : string.Empty;
            try
            {
                if (!string.IsNullOrEmpty(crop) && Localization.instance != null)
                    crop = Localization.instance.Localize(crop);
            }
            catch (Exception) { }
            if (string.IsNullOrWhiteSpace(crop)) crop = "Selected crop";

            string dimensions = (pattern == PlantPattern.Row ? 1 : AgricultureConfig.Rows.Value) +
                                " rows × " + AgricultureConfig.Columns.Value + " columns";
            string readiness = _lastPreview == null
                ? "preview loading"
                : _previewValidCount + "/" + _previewTotalCount + " ready" +
                  (_previewGroundValidCount > _previewValidCount
                      ? "; red: " + (_previewGroundValidCount - _previewValidCount) +
                        " missing planting resources"
                      : string.Empty) +
                  (string.IsNullOrEmpty(_previewIssueSummary)
                      ? string.Empty
                      : "; amber: " + _previewIssueSummary) +
                  (string.IsNullOrEmpty(_previewLimitSummary)
                      ? string.Empty
                      : "; " + _previewLimitSummary) +
                  (string.IsNullOrEmpty(_previewBatchActionSummary)
                      ? string.Empty
                      : "; " + _previewBatchActionSummary);
            bool replant = IsReplantPreviewForSelection(player);

            if (!controller)
            {
                return new AgricultureControlBarContent(
                    new AgricultureHintRow(
                        replant ? ShortcutLabel(AgricultureConfig.ConfirmReplant.Value) : "Mouse-1",
                        (replant ? "Replant " : "Plant ") + crop + " • " +
                        pattern + " • " + readiness),
                    new AgricultureHintRow(
                        "Alt + Wheel  ↓ / ↑",
                        "Rows  − / +   " + (pattern == PlantPattern.Row
                            ? "1 (Row pattern)"
                            : AgricultureConfig.Rows.Value.ToString(CultureInfo.InvariantCulture))),
                    new AgricultureHintRow(
                        "Shift + Wheel  ↓ / ↑",
                        "Columns  − / +   " + AgricultureConfig.Columns.Value),
                    new AgricultureHintRow(
                        "Wheel",
                        "Rotate " + _patternYawDegrees.ToString(
                            "0.#", CultureInfo.InvariantCulture) + "°  •  spacing " +
                        AgricultureConfig.Spacing.Value.ToString(
                            "0.0", CultureInfo.InvariantCulture) + " m"),
                    new AgricultureHintRow(
                        "Alt + Mouse-2  /  Num 2",
                        "Pattern " + pattern + " • " + dimensions +
                        " • change / select basic Grid"));
            }

            AgricultureControllerBindings bindings = AgricultureConfig.CurrentControllerBindings();
            string problem = string.Empty;
            bool controllerActionsReady = AgricultureConfig.ControllerEnabled.Value &&
                                          EnsureControllerActions(player) &&
                                          bindings.TryValidate(out problem);
            if (!controllerActionsReady)
            {
                string fallback = AgricultureConfig.ControllerEnabled.Value
                    ? "Controller Agriculture bindings unavailable" +
                      (string.IsNullOrWhiteSpace(problem) ? "." : ": " + problem + ".")
                    : "Controller Agriculture controls are disabled.";
                return new AgricultureControlBarContent(
                    new AgricultureHintRow("Mouse-1", "Plant " + crop + " — " + readiness),
                    new AgricultureHintRow("Alt + Wheel  ↓ / ↑", "Rows  − / +   " + AgricultureConfig.Rows.Value),
                    new AgricultureHintRow("Shift + Wheel  ↓ / ↑", "Columns  − / +   " + AgricultureConfig.Columns.Value),
                    new AgricultureHintRow("Wheel", "Rotate • spacing " + AgricultureConfig.Spacing.Value.ToString(
                        "0.0", CultureInfo.InvariantCulture) + " m"),
                    new AgricultureHintRow("Alt + Shift + Wheel", fallback));
            }

            if (replant)
                return new AgricultureControlBarContent(
                    new AgricultureHintRow(
                        ValheimAccess.ControllerChordLabel(bindings, bindings.Confirm),
                        "Replant " + crop + " — " + readiness));

            _controllerEditorField = ControllerPatternEditor.Normalize(pattern, _controllerEditorField);
            string field = ControllerEditorFieldLabel(pattern, _controllerEditorField);
            string choose = ValheimAccess.ControllerControlLabel(bindings.PreviousEditorField) +
                            " / " +
                            ValheimAccess.ControllerControlLabel(bindings.NextEditorField);
            string adjust = ValheimAccess.ControllerControlLabel(bindings.DecreaseEditorValue) +
                            " / " +
                            ValheimAccess.ControllerControlLabel(bindings.IncreaseEditorValue);
            return new AgricultureControlBarContent(
                new AgricultureHintRow(
                    ValheimAccess.ControllerChordLabel(bindings, bindings.Confirm),
                    "Plant " + crop + " • " + pattern + " • " + readiness),
                new AgricultureHintRow(
                    ValheimAccess.ControllerChordLabel(bindings, bindings.Cycle),
                    "Pattern " + pattern + " " + dimensions),
                new AgricultureHintRow(choose, "Choose " + field),
                new AgricultureHintRow(adjust, "Adjust " + field),
                new AgricultureHintRow("", "Spacing " + AgricultureConfig.Spacing.Value.ToString(
                    "0.0", CultureInfo.InvariantCulture) + " m"));
        }

        internal void TickInput(Player player)
        {
            if (player == null || player != Player.m_localPlayer || !player.IsOwner()) return;
            AgricultureInputConsumption.BeginSample(Time.frameCount);
            AgricultureControllerCollisionGuard.Poll();
            if (!ValheimAccess.PlayerTakesInput(player)) return;

            bool keyboardCycle = ValheimAccess.ShortcutDown(AgricultureConfig.CyclePattern.Value);
            bool keyboardPattern = false;
            bool keyboardReplant = ValheimAccess.ShortcutDown(AgricultureConfig.ConfirmReplant.Value);
            PatternEditAction patternEdit = ReadPatternEditAction();
            int rotationDirection = 0;
            bool hasPlantSelection = IsPlantPiece(player.GetSelectedPiece());
            bool plantingContext = hasPlantSelection && IsPlantingControlContext(player);
            bool editorContext = plantingContext && !IsReplantPreviewForSelection(player);
            PlantPattern? directPattern = null;

            if (plantingContext)
            {
                bool alt = ValheimAccess.KeyHeld(KeyCode.LeftAlt, KeyCode.RightAlt);
                bool shift = ValheimAccess.KeyHeld(KeyCode.LeftShift, KeyCode.RightShift);
                bool control = ValheimAccess.KeyHeld(KeyCode.LeftControl, KeyCode.RightControl);

                if (editorContext)
                {
                    // The shared wheel is sampled before its exact-frame lease is armed. A crop
                    // rotation or edit owns the wheel for the rest of this frame.
                    float wheel = ValheimAccess.MouseWheel();
                    AgricultureWheelTarget target = AgricultureWheelRouter.Resolve(
                        alt,
                        shift,
                        control,
                        wheel);
                    if (target != AgricultureWheelTarget.None)
                    {
                        AgricultureInputConsumption.ConsumeWheel(Time.frameCount);
                        if (target == AgricultureWheelTarget.Rotation)
                            rotationDirection = wheel > 0f ? 1 : -1;
                        else
                            patternEdit = AgricultureWheelRouter.ToEditAction(target, wheel > 0f);
                    }

                    // Plain BuildMenu/RMB is never touched. Only the exact Alt+BuildMenu edge cycles
                    // and receives a one-frame lease so Valheim cannot open the piece selector too.
                    if (alt && !shift && !control && ValheimAccess.ButtonDown("BuildMenu"))
                    {
                        AgricultureInputConsumption.ConsumeBuildMenu(Time.frameCount);
                        keyboardCycle = true;
                    }

                    if (!alt && !shift && !control && TryReadDirectPattern(out PlantPattern selected))
                        directPattern = selected;
                }

                if (!alt && !shift && !control && ValheimAccess.ButtonDown("Attack"))
                {
                    AgricultureInputConsumption.ConsumePlace(Time.frameCount);
                    if (IsReplantPreviewForSelection(player)) keyboardReplant = true;
                    else keyboardPattern = true;
                }
            }

            bool controllerReady = plantingContext && EnsureControllerActions(player);
            AgricultureControllerBindings controller = AgricultureConfig.CurrentControllerBindings();
            bool modifierHeld = controllerReady &&
                                ValheimAccess.ControllerButtonHeldRaw(controller.Modifier);
            bool controllerCycle = modifierHeld &&
                                   ValheimAccess.ControllerButtonDownRaw(controller.Cycle);
            bool controllerConfirm = modifierHeld &&
                                     ValheimAccess.ControllerButtonDownRaw(controller.Confirm);
            bool controllerPreviousField = controllerReady && !modifierHeld && editorContext &&
                                           ValheimAccess.ControllerButtonDownRaw(
                                               controller.PreviousEditorField);
            bool controllerNextField = controllerReady && !modifierHeld && editorContext &&
                                       ValheimAccess.ControllerButtonDownRaw(
                                           controller.NextEditorField);
            bool controllerDecrease = controllerReady && !modifierHeld && editorContext &&
                                      ValheimAccess.ControllerButtonDownRaw(
                                          controller.DecreaseEditorValue);
            bool controllerIncrease = controllerReady && !modifierHeld && editorContext &&
                                      ValheimAccess.ControllerButtonDownRaw(
                                          controller.IncreaseEditorValue);

            var frame = new AgricultureInputFrame(
                keyboardCycle,
                keyboardPattern,
                keyboardReplant,
                controllerCycle,
                controllerConfirm,
                _lastPreview != null && _lastPreview.IsReplant);
            RoutedAgricultureAction action = AgricultureActionRouter.Resolve(frame);
            if (patternEdit == PatternEditAction.None && rotationDirection == 0 &&
                action == RoutedAgricultureAction.None &&
                !directPattern.HasValue && !controllerPreviousField && !controllerNextField &&
                !controllerDecrease && !controllerIncrease) return;

            Trace("input routed to " +
                  (rotationDirection != 0 ? "RotatePattern" :
                      patternEdit != PatternEditAction.None ? patternEdit.ToString() : action.ToString()) +
                  (controllerCycle || controllerConfirm || controllerPreviousField ||
                   controllerNextField || controllerDecrease || controllerIncrease
                      ? " from controller"
                      : " from keyboard") + ".");
            if (!IsOperational)
            {
                Message(player, _disabledForSession
                    ? "Runic Agriculture is disabled for this session after an error; check LogOutput.log."
                    : "Runic Agriculture is disabled in Configuration Manager.");
                return;
            }
            if (!hasPlantSelection)
            {
                Message(player, "Runic agriculture: select a crop with the cultivator first.");
                return;
            }

            // Capture every recognized controller edge before Valheim's original Player.Update
            // runs. Even when a simultaneous keyboard edit wins below, a lower-priority controller
            // primary must not fall through as a vanilla action.
            if (controllerCycle && !TryCaptureControllerGesture(player, controller.Cycle)) return;
            if (controllerConfirm && !TryCaptureControllerGesture(player, controller.Confirm)) return;
            if (controllerPreviousField &&
                !TryCaptureControllerEditorControl(player, controller.PreviousEditorField)) return;
            if (controllerNextField &&
                !TryCaptureControllerEditorControl(player, controller.NextEditorField)) return;
            if (controllerDecrease &&
                !TryCaptureControllerEditorControl(player, controller.DecreaseEditorValue)) return;
            if (controllerIncrease &&
                !TryCaptureControllerEditorControl(player, controller.IncreaseEditorValue)) return;

            if (controllerPreviousField || controllerNextField)
            {
                _controllerEditorField = ControllerPatternEditor.Move(
                    AgricultureConfig.Pattern.Value,
                    _controllerEditorField,
                    controllerNextField ? 1 : -1);
                return;
            }

            if (controllerDecrease || controllerIncrease)
            {
                patternEdit = ControllerPatternEditor.ToEditAction(
                    ControllerPatternEditor.Normalize(
                        AgricultureConfig.Pattern.Value,
                        _controllerEditorField),
                    controllerIncrease);
            }

            if (rotationDirection != 0)
            {
                RotatePattern(player, rotationDirection);
                return;
            }

            // Live edits win over cycle/confirmation in the same frame so an input collision can
            // never commit the preview dimensions that were visible before this edit.
            if (patternEdit != PatternEditAction.None)
            {
                string selectedCrop = PrefabIdentity.Of(player.GetSelectedPiece()?.gameObject);
                if ((_lastPreview != null && _lastPreview.IsReplant) ||
                    (_replant.IsPending && string.Equals(
                        _replant.CropId,
                        selectedCrop,
                        StringComparison.Ordinal)))
                {
                    Message(player,
                        "Runic replant uses its saved harvest positions; shape editing resumes on the next normal planting preview.");
                    return;
                }
                ApplyPatternEdit(player, patternEdit);
                return;
            }

            if (directPattern.HasValue)
            {
                SetPattern(player, directPattern.Value, "numpad");
                return;
            }

            if (action == RoutedAgricultureAction.CyclePattern)
            {
                CyclePattern(player);
            }
            else if (action == RoutedAgricultureAction.ConfirmReplant)
            {
                ConfirmReplant(player);
            }
            else
            {
                ConfirmPattern(player);
            }
        }

        internal bool IsAreaHarvestRequested(Player player, GameObject targetObject)
        {
            Pickable target = FindPickable(targetObject);
            if (!CanOfferAreaHarvest(player, target)) return false;
            if (ValheimAccess.ShortcutDown(AgricultureConfig.AreaHarvest.Value)) return true;
            if (!AgricultureConfig.ControllerEnabled.Value || !EnsureControllerActions(player)) return false;
            AgricultureControllerBindings controller = AgricultureConfig.CurrentControllerBindings();
            bool requested = ValheimAccess.ControllerButtonHeldRaw(controller.Modifier) &&
                             ValheimAccess.ControllerButtonDownRaw(controller.AreaHarvest);
            if (requested) _controllerHarvestRequestFrame = Time.frameCount;
            return requested;
        }

        internal string HarvestControlHint()
        {
            string hint = ShortcutLabel(AgricultureConfig.AreaHarvest.Value);
            if (!AgricultureConfig.ControllerEnabled.Value) return hint;
            AgricultureControllerBindings controller = AgricultureConfig.CurrentControllerBindings();
            return EnsureControllerActions(Player.m_localPlayer) && controller.TryValidate(out _)
                ? hint + " or " + controller.AreaHarvestChord
                : hint;
        }

        internal void UpdatePreview(Player player)
        {
            if (!IsOperational || player == null || player != Player.m_localPlayer || !player.IsOwner())
            {
                _lastPreview = null;
                ClearControlBarPreview();
                _previewPool.Hide();
                return;
            }

            Piece piece = player.GetSelectedPiece();
            GameObject rootGhost = ValheimAccess.GetPlacementGhost(player);
            if (!IsPlantPiece(piece) || rootGhost == null || !rootGhost.activeInHierarchy)
            {
                _lastPreview = null;
                ClearControlBarPreview();
                _previewPool.Hide();
                return;
            }

            if (Time.unscaledTime < _nextPreviewUpdate) return;

            float minimumSpacing = MinimumSpacingFor(piece);
            if (AgricultureConfig.Spacing.Value < minimumSpacing)
                AgricultureConfig.Spacing.Value = minimumSpacing;

            string cropId = PrefabIdentity.Of(piece.gameObject);
            Quaternion rotation = ResolveAlignment(player, piece, rootGhost.transform.position);
            List<Vector3> requested = BuildRequestedPositions(
                player,
                piece,
                rootGhost.transform.position,
                rotation,
                cropId);
            if (requested.Count == 0)
            {
                _nextPreviewUpdate = Time.unscaledTime + 0.1f;
                _lastPreview = null;
                ClearControlBarPreview();
                _previewPool.Hide();
                return;
            }

            bool replantPreview = _replant.IsPending &&
                                  string.Equals(_replant.CropId, cropId, StringComparison.Ordinal);
            List<RuntimePreviewPosition> previews = BuildValidatedPreview(player, piece, requested, rotation);
            _lastPreview = new PreviewSnapshot(cropId, piece, rotation, requested, replantPreview);
            _previewTotalCount = previews.Count;
            _previewValidCount = 0;
            _previewGroundValidCount = 0;
            var invalidReasons = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int index = 0; index < previews.Count; index++)
            {
                RuntimePreviewPosition preview = previews[index];
                if (preview.IsGroundValid)
                {
                    _previewGroundValidCount++;
                    if (preview.IsValid) _previewValidCount++;
                    continue;
                }
                string reason = preview.ReasonCode ?? AgricultureReasonCodes.PlacementFailed;
                invalidReasons.TryGetValue(reason, out int count);
                invalidReasons[reason] = count + 1;
            }
            _previewIssueSummary = PreviewIssueSummary(invalidReasons);
            _previewBatchActionSummary = BatchActionIssue(player, piece);

            _previewPool.Show(rootGhost, previews);
            _nextPreviewUpdate = Time.unscaledTime + PreviewRefreshInterval(previews.Count);
        }

        internal bool TryAreaHarvest(Player player, GameObject targetObject)
        {
            if (!IsOperational || player == null || player != Player.m_localPlayer || !player.IsOwner())
                return false;
            bool controllerRequest = _controllerHarvestRequestFrame == Time.frameCount;
            _controllerHarvestRequestFrame = -1;
            Pickable target = FindPickable(targetObject);
            if (!CanOfferAreaHarvest(player, target))
            {
                Trace("area harvest preserved the original interaction because the targeted " +
                      "Pickable was unavailable or access was denied.");
                return false;
            }
            if (!TryCaptureHarvestCandidate(target, out HarvestPickableCandidate aimed))
            {
                if (target != null)
                    Message(player, "Runic harvest: this Pickable is not currently available.");
                Trace("area harvest preserved the original interaction because the targeted " +
                      "Pickable did not satisfy the live network/availability contract.");
                return false;
            }

            if (controllerRequest &&
                !TryCaptureControllerGesture(player, AgricultureConfig.CurrentControllerBindings().AreaHarvest))
                return false;

            string matureCropId = aimed.PrefabName;
            string plantCropId = FindPlantForMature(matureCropId);

            float radius = Mathf.Clamp(AgricultureConfig.HarvestRadius.Value, 1f, 8f);
            int maximum = Mathf.Clamp(
                AgricultureConfig.MaximumHarvest.Value,
                1,
                AgricultureConfig.HardMaximumHarvest);
            int count = Physics.OverlapSphereNonAlloc(
                target.transform.position,
                radius,
                _harvestHits,
                Physics.AllLayers,
                QueryTriggerInteraction.Collide);
            var unique = new HashSet<Pickable>();
            var candidates = new List<HarvestPickableCandidate>(Math.Min(count + 1, _harvestHits.Length + 1));
            // The aimed Pickable is always first even when the bounded physics buffer is full or
            // its collider is absent. Remaining candidates are selected independently of collider
            // enumeration and Unity instance IDs.
            unique.Add(target);
            candidates.Add(aimed);
            for (int index = 0; index < Math.Min(count, _harvestHits.Length); index++)
            {
                Collider hit = _harvestHits[index];
                _harvestHits[index] = null;
                Pickable pickable = FindPickable(hit != null ? hit.gameObject : null);
                if (pickable == null || !unique.Add(pickable) ||
                    !TryCaptureHarvestCandidate(pickable, out HarvestPickableCandidate candidate) ||
                    !HarvestPickableBatchPolicy.IsExactPrefab(
                        aimed.PrefabName,
                        aimed.PrefabHash,
                        candidate.PrefabName,
                        candidate.PrefabHash))
                    continue;
                candidates.Add(candidate);
            }

            candidates.Sort((left, right) =>
                HarvestPickableBatchPolicy.Compare(
                    ReferenceEquals(left.Pickable, target),
                    (left.Position - aimed.Position).sqrMagnitude,
                    left.ZdoId.UserID,
                    left.ZdoId.ID,
                    ReferenceEquals(right.Pickable, target),
                    (right.Position - aimed.Position).sqrMagnitude,
                    right.ZdoId.UserID,
                    right.ZdoId.ID));

            var harvestedPositions = new List<Vector3>(maximum);
            int requestsSent = 0;
            bool aimedWardDenied = false;
            for (int index = 0; index < candidates.Count && requestsSent < maximum; index++)
            {
                HarvestPickableCandidate snapshot = candidates[index];
                if (!TryCaptureHarvestCandidate(
                        snapshot.Pickable, out HarvestPickableCandidate candidate) ||
                    !HarvestPickableBatchPolicy.IsExactPrefab(
                        aimed.PrefabName,
                        aimed.PrefabHash,
                        candidate.PrefabName,
                        candidate.PrefabHash) ||
                    (candidate.Position - aimed.Position).sqrMagnitude > radius * radius)
                    continue;
                if (!PrivateArea.CheckAccess(candidate.Position, 0f, false, false))
                {
                    aimedWardDenied |= ReferenceEquals(candidate.Pickable, target);
                    continue;
                }

                // Interact's Boolean is m_useInteractAnimation, not a mutation result. The complete
                // precheck above covers every branch that can prevent InvokeRPC; after it passes,
                // count only a request sent, never a synchronously confirmed harvest.
                candidate.Pickable.Interact(player, false, false);
                harvestedPositions.Add(candidate.Position);
                requestsSent++;
            }

            if (requestsSent == 0)
            {
                Message(player, "Runic harvest: no currently available, permitted Pickables.");
                Trace("area harvest found candidates but sent no permitted pick requests; " +
                      (aimedWardDenied ? "the aimed Pickable was ward-denied." :
                          "the original interaction remains available."));
                // A ward denial is an intentional fail-closed result. Other last-moment state
                // changes preserve the original aimed interaction instead of swallowing it.
                return aimedWardDenied;
            }

            bool replantAuthorized = PrivateArea.CheckAccess(aimed.Position, 0f, false, false);
            if (HarvestPickableBatchPolicy.OffersReplant(
                    AgricultureConfig.OfferReplantPreview.Value,
                    replantAuthorized,
                    plantCropId))
            {
                _replant.Offer(plantCropId, harvestedPositions);
                Message(player, "Runic harvest: " + requestsSent +
                    " request(s) sent. Select the matching seed, then confirm with " +
                    ShortcutLabel(AgricultureConfig.ConfirmReplant.Value) +
                    (AgricultureConfig.ControllerEnabled.Value
                        ? " or " + AgricultureConfig.CurrentControllerBindings().ConfirmChord
                        : string.Empty) + ".");
            }
            else
            {
                _replant.Clear();
                Message(player, "Runic harvest: " + requestsSent + " request(s) sent.");
            }
            Trace("area harvest sent " + requestsSent + " owner-validated pick request(s) for '" +
                  matureCropId + "'.");
            return true;
        }

        internal bool CanOfferAreaHarvest(Player player, Pickable target)
        {
            if (!IsOperational || player == null || player != Player.m_localPlayer ||
                !player.IsOwner() || target == null ||
                !TryCaptureHarvestCandidate(target, out HarvestPickableCandidate candidate))
                return false;

            bool authorized = PrivateArea.CheckAccess(candidate.Position, 0f, false, false);
            return HarvestPickableBatchPolicy.AllowsAreaHarvest(authorized);
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _mutationActive, 0);
            _lastPreview = null;
            ClearControlBarPreview();
            _replant.Clear();
            _controllerHarvestRequestFrame = -1;
            RestoreBuildHintsIfOwned();
            AgricultureInputConsumption.Reset();
            AgricultureControllerCollisionGuard.Reset();
            _controlBar.Dispose();
            _previewPool.Dispose();
            NearbySeedContainerIndex.Clear();
        }

        private void CyclePattern(Player player)
        {
            SetPattern(player, PatternEditor.Next(AgricultureConfig.Pattern.Value), "cycle");
        }

        private void SetPattern(Player player, PlantPattern pattern, string source)
        {
            if (!Enum.IsDefined(typeof(PlantPattern), pattern)) return;
            AgricultureConfig.Pattern.Value = pattern;
            _controllerEditorField = ControllerPatternEditor.Normalize(
                pattern,
                _controllerEditorField);
            Trace("planting pattern selected as " + pattern + " from " + source + ".");
        }

        private static PatternEditAction ReadPatternEditAction()
        {
            if (ValheimAccess.ShortcutDown(AgricultureConfig.IncreaseRows.Value))
                return PatternEditAction.IncreaseRows;
            if (ValheimAccess.ShortcutDown(AgricultureConfig.DecreaseRows.Value))
                return PatternEditAction.DecreaseRows;
            if (ValheimAccess.ShortcutDown(AgricultureConfig.IncreaseColumns.Value))
                return PatternEditAction.IncreaseColumns;
            if (ValheimAccess.ShortcutDown(AgricultureConfig.DecreaseColumns.Value))
                return PatternEditAction.DecreaseColumns;
            if (ValheimAccess.ShortcutDown(AgricultureConfig.ToggleShapeSide.Value))
                return PatternEditAction.ToggleSide;
            if (ValheimAccess.ShortcutDown(AgricultureConfig.DecreaseLeftPinch.Value))
                return PatternEditAction.DecreaseLeftPinch;
            if (ValheimAccess.ShortcutDown(AgricultureConfig.IncreaseLeftPinch.Value))
                return PatternEditAction.IncreaseLeftPinch;
            if (ValheimAccess.ShortcutDown(AgricultureConfig.DecreaseRightPinch.Value))
                return PatternEditAction.DecreaseRightPinch;
            if (ValheimAccess.ShortcutDown(AgricultureConfig.IncreaseRightPinch.Value))
                return PatternEditAction.IncreaseRightPinch;
            return PatternEditAction.None;
        }

        private void RotatePattern(Player player, int direction)
        {
            float step = ValheimAccess.PlaceRotationDegrees(player);
            if (float.IsNaN(step) || float.IsInfinity(step) || step <= 0f) step = 22.5f;
            _patternYawDegrees = Mathf.Repeat(
                _patternYawDegrees + (direction > 0 ? step : -step),
                360f);
            _lastPreview = null;
            _nextPreviewUpdate = 0f;
            Message(
                player,
                "Runic shape: rotated to " +
                _patternYawDegrees.ToString("0.#", CultureInfo.InvariantCulture) + "°.");
        }

        private void ApplyPatternEdit(Player player, PatternEditAction action)
        {
            PlantPattern pattern = AgricultureConfig.Pattern.Value;
            var before = new PatternEditState(
                AgricultureConfig.Rows.Value,
                AgricultureConfig.Columns.Value,
                AgricultureConfig.MirrorShape.Value,
                AgricultureConfig.TrapezoidLeftPinch.Value,
                AgricultureConfig.TrapezoidRightPinch.Value,
                AgricultureConfig.Spacing.Value);
            PatternEditState after = PatternEditor.Apply(pattern, before, action);
            float minimumSpacing = MinimumSpacingFor(player.GetSelectedPiece());
            if (after.Spacing < minimumSpacing)
                after = new PatternEditState(
                    after.Rows,
                    after.Columns,
                    after.Mirrored,
                    after.LeftPinch,
                    after.RightPinch,
                    minimumSpacing);
            if (after.Equals(before))
            {
                string reason = pattern == PlantPattern.Row &&
                                (action == PatternEditAction.IncreaseRows ||
                                 action == PatternEditAction.DecreaseRows)
                    ? "Row has one forward row; use the column controls to change its length."
                    : action == PatternEditAction.ToggleSide && !PatternEditor.SupportsMirror(pattern)
                        ? pattern + " is symmetric; side switching applies to RightTriangle, HalfCircle, and Trapezoid."
                        : (action == PatternEditAction.DecreaseLeftPinch ||
                           action == PatternEditAction.IncreaseLeftPinch ||
                           action == PatternEditAction.DecreaseRightPinch ||
                           action == PatternEditAction.IncreaseRightPinch) &&
                          pattern != PlantPattern.Trapezoid
                            ? "Independent taper controls apply only to Trapezoid."
                            : action == PatternEditAction.DecreaseSpacing &&
                              before.Spacing <= minimumSpacing
                                ? "That crop requires at least " +
                                  minimumSpacing.ToString("0.0", CultureInfo.InvariantCulture) +
                                  "m spacing."
                                : "That pattern setting is already at its safe limit.";
                Message(player, "Runic shape: " + reason);
                return;
            }

            if (after.Rows != before.Rows) AgricultureConfig.Rows.Value = after.Rows;
            else if (after.Columns != before.Columns) AgricultureConfig.Columns.Value = after.Columns;
            else if (after.Mirrored != before.Mirrored) AgricultureConfig.MirrorShape.Value = after.Mirrored;
            else if (!after.LeftPinch.Equals(before.LeftPinch))
                AgricultureConfig.TrapezoidLeftPinch.Value = (float)after.LeftPinch;
            else if (!after.RightPinch.Equals(before.RightPinch))
                AgricultureConfig.TrapezoidRightPinch.Value = (float)after.RightPinch;
            else if (!after.Spacing.Equals(before.Spacing))
                AgricultureConfig.Spacing.Value = (float)after.Spacing;
            string state = pattern + " " + (pattern == PlantPattern.Row
                ? after.Columns + " columns"
                : after.Rows + "x" + after.Columns);
            if (PatternEditor.SupportsMirror(pattern))
                state += ", " + PatternEditor.OrientationLabel(pattern, after.Mirrored).ToLowerInvariant();
            if (pattern == PlantPattern.Trapezoid)
                state += ", taper L " + Mathf.RoundToInt((float)after.LeftPinch * 100f) +
                         "% / R " + Mathf.RoundToInt((float)after.RightPinch * 100f) + "%";
            state += ", spacing " + after.Spacing.ToString("0.0", CultureInfo.InvariantCulture) + "m";
            Message(player, "Runic shape: " + state + ".");
            Trace("live pattern edit " + action + " applied to " + pattern + ".");
        }

        private void ConfirmPattern(Player player)
        {
            PreviewSnapshot snapshot = _lastPreview;
            if (snapshot == null || snapshot.IsReplant)
            {
                Message(player, snapshot != null
                    ? "Runic planting: this is a replant preview; use the replant confirmation control."
                    : "Runic planting: no preview is ready. Select a crop and aim at plantable ground.");
                Trace("pattern confirmation produced no mutation because no ordinary preview was ready.");
                return;
            }
            ExecuteBatch(player, snapshot.Piece, snapshot.CropId, snapshot.Positions, snapshot.Rotation);
        }

        private void ConfirmReplant(Player player)
        {
            Piece piece = player.GetSelectedPiece();
            if (!IsPlantPiece(piece))
            {
                Message(player, "Runic replant: select the matching crop with the cultivator first.");
                Trace("replant confirmation ignored because no crop is selected.");
                return;
            }

            string cropId = PrefabIdentity.Of(piece.gameObject);
            if (!_replant.TryConfirm(cropId, out IReadOnlyList<Vector3> positions, out string reason))
            {
                Message(player, FriendlyReason(reason));
                Trace("replant confirmation denied: " + reason + ".");
                return;
            }

            Quaternion rotation = Quaternion.Euler(0f, player.transform.eulerAngles.y, 0f);
            int planted = ExecuteBatch(player, piece, cropId, positions, rotation);
            if (planted == 0 && positions.Count > 0)
                _replant.Offer(cropId, positions);
        }

        private int ExecuteBatch(
            Player player,
            Piece piece,
            string expectedCropId,
            IReadOnlyList<Vector3> requested,
            Quaternion rotation)
        {
            if (player == null || player != Player.m_localPlayer || !player.IsOwner())
            {
                Deny(player, AgricultureReasonCodes.NotAuthoritative);
                return 0;
            }
            if (!IsPlantPiece(piece) || !string.Equals(
                    PrefabIdentity.Of(player.GetSelectedPiece()?.gameObject),
                    expectedCropId,
                    StringComparison.Ordinal))
            {
                Deny(player, AgricultureReasonCodes.CropChanged);
                return 0;
            }

            var validations = new List<PlacementValidationResult>(requested.Count);
            var sampledPositions = new List<Vector3>(requested.Count);
            float requiredSpacing = _validator.RequiredSpacing(piece);
            for (int index = 0; index < requested.Count; index++)
            {
                RuntimePlacementValidation current = _validator.Validate(player, piece, requested[index], rotation);
                PlacementValidationResult result = ApplyPlannedSpacing(
                    current.Result,
                    current.Position,
                    sampledPositions,
                    validations,
                    requiredSpacing);
                sampledPositions.Add(current.Position);
                validations.Add(result);
            }

            BatchPlan plan = BatchPlanner.Plan(
                validations,
                BuildBudget(player, piece),
                AgricultureConfig.InvalidPolicy.Value,
                AgricultureConfig.ResourcePolicy.Value);
            if (plan.Blocked)
            {
                Deny(player, plan.ReasonCode);
                return 0;
            }

            ItemDrop.ItemData batchTool = null;
            if (plan.SuccessfulCount > 0 &&
                !CanStartBatchAction(player, piece, out batchTool, out string batchReason))
            {
                Deny(player, batchReason);
                return 0;
            }

            if (!TryEnterMutation(
                    "runic.agriculture/plant-batch",
                    out IDisposable mutationLease))
            {
                Message(player, "Runic planting paused: another Agriculture batch is in progress.");
                return 0;
            }

            using (mutationLease)
            {
                int planted = 0;
                int skipped = 0;
                string stopReason = null;
                string firstSkippedReason = null;
                for (int index = 0; index < plan.Decisions.Count; index++)
                {
                    BatchDecision decision = plan.Decisions[index];
                    if (!decision.ShouldPlace)
                    {
                        skipped++;
                        if (string.IsNullOrEmpty(firstSkippedReason))
                            firstSkippedReason = decision.ReasonCode;
                        if (decision.ReasonCode == AgricultureReasonCodes.NoSeeds ||
                            decision.ReasonCode == AgricultureReasonCodes.NoDurability ||
                            decision.ReasonCode == AgricultureReasonCodes.NoStamina)
                        {
                            stopReason = decision.ReasonCode;
                            break;
                        }
                        continue;
                    }

                    // Revalidate immediately before placing so changed terrain and access apply.
                    RuntimePlacementValidation fresh = _validator.Validate(
                        player, piece, sampledPositions[index], rotation);
                    if (!fresh.Result.IsValid)
                    {
                        skipped++;
                        if (string.IsNullOrEmpty(firstSkippedReason))
                            firstSkippedReason = fresh.Result.ReasonCode;
                        stopReason = fresh.Result.ReasonCode;
                        if (AgricultureConfig.InvalidPolicy.Value ==
                            InvalidPositionPolicy.BlockConfirmation)
                            break;
                        continue;
                    }
                    if (!CanCommitOne(player, piece, expectedCropId, out string commitReason))
                    {
                        stopReason = commitReason;
                        break;
                    }

                    try
                    {
                        bool consumeResources = PlantingGridPolicy.ConsumesSeedResources(
                            IsFreeBuild(piece), player.NoCostCheat());
                        if (!NearbySeedResourceService.TryDebitOne(
                                player,
                                piece,
                                AgricultureConfig.NearbySeedChestRange.Value,
                                consumeResources,
                                out NearbySeedResourceService.PlantResourceDebit debit))
                        {
                            stopReason = AgricultureReasonCodes.NoSeeds;
                            break;
                        }
                        using (debit)
                        {
                            player.PlacePiece(piece, fresh.Position, rotation, false, false);
                            debit.Complete();
                        }
                        _seedBudgetExpiresAt = 0f;
                        planted++;
                    }
                    catch (Exception exception)
                    {
                        stopReason = AgricultureReasonCodes.PlacementFailed;
                        _log.LogError("One planting commit failed after revalidation: " + exception);
                        break;
                    }
                }

                if (planted > 0)
                {
                    try { ChargeBatchActionCosts(player, batchTool); }
                    catch (Exception exception)
                    {
                        stopReason = AgricultureReasonCodes.PlacementFailed;
                        _log.LogError(
                            "The planting batch completed but its one stamina/tool action " +
                            "could not be charged cleanly: " + exception);
                    }
                }

                string summary = "Runic planting: " + planted + " planted";
                if (skipped > 0) summary += ", " + skipped + " skipped";
                string explanation = !string.IsNullOrEmpty(stopReason)
                    ? stopReason
                    : firstSkippedReason;
                if (!string.IsNullOrEmpty(explanation) &&
                    explanation != AgricultureReasonCodes.Valid)
                    summary += ". " + FriendlyReason(explanation);
                Message(player, summary + ".");
                Trace("batch result: planted=" + planted + ", skipped=" + skipped +
                      ", reason=" + (explanation ?? AgricultureReasonCodes.Valid) + ".");
                return planted;
            }
        }

        private List<Vector3> BuildRequestedPositions(
            Player player,
            Piece piece,
            Vector3 origin,
            Quaternion rotation,
            string selectedCropId)
        {
            if (_replant.IsPending &&
                string.Equals(_replant.CropId, selectedCropId, StringComparison.Ordinal))
            {
                _previewLimitSummary = "confirmed replant positions";
                return new List<Vector3>(_replant.Positions);
            }

            int configuredLimit = AgricultureConfig.HardMaximumPreview;
            float spacing = Math.Max(
                AgricultureConfig.Spacing.Value,
                MinimumSpacingFor(piece));
            var capacityRequest = new PatternRequest(
                AgricultureConfig.Pattern.Value,
                AgricultureConfig.Rows.Value,
                AgricultureConfig.Columns.Value,
                spacing,
                configuredLimit,
                AgricultureConfig.MirrorShape.Value,
                AgricultureConfig.TrapezoidLeftPinch.Value,
                AgricultureConfig.TrapezoidRightPinch.Value);
            IReadOnlyList<PlanarPoint> capacity = _patternService.Generate(capacityRequest);
            // The pattern service returns the exact requested footprint in its deterministic fill
            // order: Grid/Row right-to-left, masked shapes centre-out.
            IReadOnlyList<PlanarPoint> planar = capacity;

            int latticeCells = AgricultureConfig.Pattern.Value == PlantPattern.Row
                ? AgricultureConfig.Columns.Value
                : checked(AgricultureConfig.Rows.Value * AgricultureConfig.Columns.Value);
            bool performanceCapped = capacity.Count == configuredLimit &&
                                     latticeCells > configuredLimit;
            if (performanceCapped)
                _previewLimitSummary = "performance cap " + configuredLimit +
                                       " cells (Configuration Manager)";
            else
                _previewLimitSummary = string.Empty;

            var positions = new List<Vector3>(planar.Count);
            Vector3 right = rotation * Vector3.right;
            Vector3 forward = rotation * Vector3.forward;
            for (int index = 0; index < planar.Count; index++)
            {
                positions.Add(origin + right * (float)planar[index].Right +
                              forward * (float)planar[index].Forward);
            }
            return positions;
        }

        private List<RuntimePreviewPosition> BuildValidatedPreview(
            Player player,
            Piece piece,
            IReadOnlyList<Vector3> requested,
            Quaternion rotation)
        {
            var sampled = new List<Vector3>(requested.Count);
            var validations = new List<PlacementValidationResult>(requested.Count);
            float requiredSpacing = _validator.RequiredSpacing(piece);
            for (int index = 0; index < requested.Count; index++)
            {
                RuntimePlacementValidation validation = _validator.Validate(player, piece, requested[index], rotation);
                PlacementValidationResult result = ApplyPlannedSpacing(
                    validation.Result,
                    validation.Position,
                    sampled,
                    validations,
                    requiredSpacing);
                sampled.Add(validation.Position);
                validations.Add(result);
            }

            int seedBudget = SeedActionBudget(player, piece, false);
            bool unlimitedSeeds = seedBudget >= UnlimitedActions;
            int groundValidCells = 0;
            for (int index = 0; index < validations.Count; index++)
                if (validations[index].IsValid) groundValidCells++;
            int maximumSelectedCells = PlantingGridPolicy.SelectableSeedCount(
                Math.Max(0, seedBudget),
                groundValidCells,
                unlimitedSeeds);
            BatchPlan plan = BatchPlanner.PlanPreview(validations, maximumSelectedCells);
            var previews = new List<RuntimePreviewPosition>(requested.Count);
            for (int index = 0; index < plan.Decisions.Count; index++)
            {
                BatchDecision decision = plan.Decisions[index];
                previews.Add(new RuntimePreviewPosition(
                    sampled[index],
                    rotation,
                    decision.ShouldPlace,
                    decision.ReasonCode));
            }
            return previews;
        }

        private static PlacementValidationResult ApplyPlannedSpacing(
            PlacementValidationResult current,
            Vector3 position,
            IReadOnlyList<Vector3> previousPositions,
            IReadOnlyList<PlacementValidationResult> previousValidations,
            float requiredSpacing)
        {
            if (!current.IsValid) return current;
            float tolerance = Math.Max(0.01f, requiredSpacing * 0.02f);
            float minimum = Math.Max(0f, requiredSpacing - tolerance);
            float minimumSquared = minimum * minimum;
            for (int index = 0; index < previousPositions.Count; index++)
            {
                if (!previousValidations[index].IsValid) continue;
                if ((previousPositions[index] - position).sqrMagnitude < minimumSquared)
                    return new PlacementValidationResult(false, AgricultureReasonCodes.SpacingBlocked);
            }
            return current;
        }

        private PlacementBudget BuildBudget(Player player, Piece piece)
        {
            int seedActions = SeedActionBudget(player, piece, true);
            // One accepted left-click is one cultivator/stamina action. Per-cell seeds remain the
            // only resource budget that truncates a synchronous planting batch.
            return new PlacementBudget(seedActions, UnlimitedActions, UnlimitedActions);
        }

        private int SeedActionBudget(Player player, Piece piece, bool forceFresh)
        {
            if (player == null || piece == null) return 0;
            if (!PlantingGridPolicy.ConsumesSeedResources(
                    IsFreeBuild(piece),
                    player.NoCostCheat())) return UnlimitedActions;
            Vector3 position = player.transform.position;
            if (!forceFresh && ReferenceEquals(_seedBudgetPiece, piece) &&
                Time.unscaledTime < _seedBudgetExpiresAt &&
                (position - _seedBudgetPlayerPosition).sqrMagnitude < 0.25f)
                return _seedBudgetValue;
            int available = NearbySeedResourceService.AvailablePlantings(
                player,
                piece,
                AgricultureConfig.NearbySeedChestRange.Value);
            _seedBudgetPiece = piece;
            _seedBudgetPlayerPosition = position;
            _seedBudgetValue = Math.Min(UnlimitedActions, Math.Max(0, available));
            _seedBudgetExpiresAt = Time.unscaledTime + 0.25f;
            return _seedBudgetValue;
        }

        private float MinimumSpacingFor(Piece piece)
        {
            float nativeRadius = _validator.RequiredSpacing(piece);
            // Valheim tests a sphere against the other plant's collider. Leaving the centres at
            // exactly m_growRadius can therefore touch the collider and become unhealthy even
            // though the displayed decimal looks valid. One rounded editor step of clearance is
            // the smallest stable spacing across the audited vanilla crops.
            float collisionClearance = Math.Max(0.05f, nativeRadius * 0.05f);
            float required = Math.Max(
                (float)PatternEditor.MinimumSpacing,
                nativeRadius + collisionClearance);
            required = Mathf.Ceil(required * 10f) / 10f;
            return Mathf.Clamp(required, (float)PatternEditor.MinimumSpacing,
                (float)PatternEditor.MaximumSpacing);
        }

        private bool CanCommitOne(
            Player player,
            Piece piece,
            string expectedCropId,
            out string reason)
        {
            if (player != Player.m_localPlayer || !player.IsOwner())
            {
                reason = AgricultureReasonCodes.NotAuthoritative;
                return false;
            }
            Piece selected = player.GetSelectedPiece();
            if (!IsPlantPiece(selected) || !string.Equals(
                    PrefabIdentity.Of(selected.gameObject), expectedCropId, StringComparison.Ordinal))
            {
                reason = AgricultureReasonCodes.CropChanged;
                return false;
            }
            reason = AgricultureReasonCodes.Valid;
            return true;
        }

        private static bool CanStartBatchAction(
            Player player,
            Piece piece,
            out ItemDrop.ItemData tool,
            out string reason)
        {
            tool = player?.RightItem;
            if (player == null || piece == null || tool == null)
            {
                reason = AgricultureReasonCodes.NoDurability;
                return false;
            }
            if (tool.m_shared.m_useDurability &&
                tool.m_durability < ValheimAccess.GetPlaceDurability(player, tool))
            {
                reason = AgricultureReasonCodes.NoDurability;
                return false;
            }
            float stamina = ValheimAccess.GetBuildStamina(player);
            if (!player.HaveStamina(stamina))
            {
                reason = AgricultureReasonCodes.NoStamina;
                return false;
            }
            reason = AgricultureReasonCodes.Valid;
            return true;
        }

        private static void ChargeBatchActionCosts(
            Player player,
            ItemDrop.ItemData tool)
        {
            float stamina = ValheimAccess.GetBuildStamina(player);
            if (stamina > 0f) player.UseStamina(stamina);
            PieceTable pieceTable = tool?.m_shared.m_buildPieces;
            if (pieceTable != null && pieceTable.m_skill != Skills.SkillType.None)
                player.RaiseSkill(pieceTable.m_skill, 1f);
            if (tool != null && tool.m_shared.m_useDurability)
                tool.m_durability = Mathf.Max(
                    0f,
                    tool.m_durability - ValheimAccess.GetPlaceDurability(player, tool));
            tool?.m_shared.m_buildEffect?.Create(
                player.transform.position,
                Quaternion.identity,
                null,
                1f,
                -1);
        }

        private static bool IsFreeBuild(Piece piece) =>
            piece != null && ZoneSystem.instance != null &&
            ZoneSystem.instance.GetGlobalKey(piece.FreeBuildKey());

        private Quaternion ResolveAlignment(Player player, Piece piece, Vector3 origin)
        {
            Quaternion alignment;
            switch (AgricultureConfig.Alignment.Value)
            {
                case AgricultureAlignment.WorldAxes:
                    alignment = Quaternion.identity;
                    break;
                case AgricultureAlignment.ExistingCropRow:
                    alignment = TryFindExistingRow(piece, origin, out Quaternion row)
                        ? row
                        : Quaternion.Euler(0f, player.transform.eulerAngles.y, 0f);
                    break;
                default:
                    alignment = Quaternion.Euler(0f, player.transform.eulerAngles.y, 0f);
                    break;
            }
            return alignment * Quaternion.Euler(0f, _patternYawDegrees, 0f);
        }

        private bool TryFindExistingRow(Piece selectedPiece, Vector3 origin, out Quaternion rotation)
        {
            rotation = Quaternion.identity;
            string cropId = PrefabIdentity.Of(selectedPiece.gameObject);
            int count = Physics.OverlapSphereNonAlloc(
                origin,
                8f,
                _alignmentHits,
                LayerMask.GetMask("piece", "piece_nonsolid"),
                QueryTriggerInteraction.Collide);
            var plants = new HashSet<Plant>();
            var positions = new List<Vector3>();
            for (int index = 0; index < Math.Min(count, _alignmentHits.Length); index++)
            {
                Collider hit = _alignmentHits[index];
                _alignmentHits[index] = null;
                Plant plant = hit != null ? hit.GetComponentInParent<Plant>() : null;
                if (plant == null || !plants.Add(plant)) continue;
                if (!string.Equals(PrefabIdentity.Of(plant.gameObject), cropId, StringComparison.Ordinal))
                    continue;
                positions.Add(plant.transform.position);
            }
            positions.Sort((left, right) =>
            {
                int distance = (left - origin).sqrMagnitude.CompareTo((right - origin).sqrMagnitude);
                if (distance != 0) return distance;
                int x = left.x.CompareTo(right.x);
                if (x != 0) return x;
                int z = left.z.CompareTo(right.z);
                return z != 0 ? z : left.y.CompareTo(right.y);
            });
            if (positions.Count < 2) return false;
            Vector3 direction = positions[1] - positions[0];
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f) return false;
            rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            return true;
        }

        private bool TryCaptureControllerGesture(
            Player player,
            ValheimControllerAction primary)
        {
            AgricultureControllerBindings controller = AgricultureConfig.CurrentControllerBindings();
            if (AgricultureControllerCollisionGuard.TryCapture(
                    controller,
                    primary,
                    out string problem)) return true;
            string message = "Runic Agriculture controller action blocked: " + problem + ".";
            _log.LogWarning(message + " No agriculture mutation was attempted.");
            Message(player, message + " Release the controls and try again, or use the keyboard shortcut.");
            Trace("controller capture failed closed: " + problem + ".");
            return false;
        }

        private bool TryCaptureControllerEditorControl(
            Player player,
            ValheimControllerAction primary)
        {
            AgricultureControllerBindings controller = AgricultureConfig.CurrentControllerBindings();
            if (AgricultureControllerCollisionGuard.TryCaptureEditorControl(
                    controller,
                    primary,
                    out string problem)) return true;
            string message = "Runic Agriculture controller editor input blocked: " + problem + ".";
            _log.LogWarning(message + " No pattern setting was changed.");
            Message(player, message + " Release the D-pad/editor control and try again, or use the keyboard shortcut.");
            Trace("unmodified controller editor capture failed closed: " + problem + ".");
            return false;
        }

        private bool EnsureControllerActions(Player player)
        {
            if (!AgricultureConfig.ControllerEnabled.Value) return false;
            AgricultureControllerBindings bindings = AgricultureConfig.CurrentControllerBindings();
            if (!bindings.TryValidate(out string problem))
            {
                _controllerActionsVerified = false;
                ReportControllerProblem(player, problem);
                return false;
            }
            if (!ValheimAccess.TryVerifyControllerActions(
                    bindings,
                    out problem,
                    out string pathSignature))
            {
                _controllerActionsVerified = false;
                if (!string.Equals(problem, "Valheim ZInput is not initialized", StringComparison.Ordinal))
                    ReportControllerProblem(player, problem);
                return false;
            }

            bool changed = !_controllerActionsVerified || !string.Equals(
                _controllerPathSignature,
                pathSignature,
                StringComparison.Ordinal);
            _controllerActionsVerified = true;
            _controllerPathSignature = pathSignature;
            _lastControllerProblem = null;
            if (changed)
                _log.LogInfo("Runic Agriculture validated distinct Valheim 1.0 controller paths: " +
                             pathSignature + ".");
            return true;
        }

        private bool IsPlantingControlContext(Player player)
        {
            if (!IsOperational || !(AgricultureConfig.ShowContextualControls?.Value ?? false) ||
                player == null || player != Player.m_localPlayer || !player.IsOwner() ||
                !player.InPlaceMode() || !ValheimAccess.PlayerTakesInput(player)) return false;
            if (Hud.instance == null || Hud.instance.m_userHidden || Hud.IsPieceSelectionVisible() ||
                Game.IsPaused() || InventoryGui.IsVisible() || Minimap.IsOpen() ||
                Menu.IsVisible() || global::Console.IsVisible() || TextInput.IsVisible() ||
                ZInput.VirtualKeyboardOpen) return false;
            Piece piece = player.GetSelectedPiece();
            GameObject ghost = ValheimAccess.GetPlacementGhost(player);
            return IsPlantPiece(piece) && ghost != null && ghost.activeInHierarchy;
        }

        private bool IsReplantPreviewForSelection(Player player)
        {
            if (_lastPreview != null && _lastPreview.IsReplant) return true;
            if (!_replant.IsPending || player == null) return false;
            return string.Equals(
                _replant.CropId,
                PrefabIdentity.Of(player.GetSelectedPiece()?.gameObject),
                StringComparison.Ordinal);
        }

        private static bool TryReadDirectPattern(out PlantPattern pattern)
        {
            KeyCode[] keys =
            {
                KeyCode.Keypad1,
                KeyCode.Keypad2,
                KeyCode.Keypad3,
                KeyCode.Keypad4,
                KeyCode.Keypad5,
                KeyCode.Keypad6,
                KeyCode.Keypad7
            };
            for (int index = 0; index < keys.Length; index++)
            {
                if (!ValheimAccess.KeyDown(keys[index])) continue;
                return AgriculturePatternHotkeys.TryResolveNumpadSlot(index + 1, out pattern);
            }
            pattern = default;
            return false;
        }

        private static string ControllerEditorFieldLabel(
            PlantPattern pattern,
            ControllerEditorField field)
        {
            switch (field)
            {
                case ControllerEditorField.Rows:
                    return "Rows " + AgricultureConfig.Rows.Value;
                case ControllerEditorField.Columns:
                    return "Columns " + AgricultureConfig.Columns.Value;
                case ControllerEditorField.Spacing:
                    return "Spacing " + AgricultureConfig.Spacing.Value.ToString(
                               "0.0", CultureInfo.InvariantCulture) + " m";
                case ControllerEditorField.Side:
                    return "Side " + PatternEditor.OrientationLabel(
                        pattern,
                        AgricultureConfig.MirrorShape.Value);
                case ControllerEditorField.LeftTaper:
                    return "Left taper " + Mathf.RoundToInt(
                        AgricultureConfig.TrapezoidLeftPinch.Value * 100f) + "%";
                case ControllerEditorField.RightTaper:
                    return "Right taper " + Mathf.RoundToInt(
                        AgricultureConfig.TrapezoidRightPinch.Value * 100f) + "%";
                default:
                    return field.ToString();
            }
        }

        private void ClearControlBarPreview()
        {
            _previewValidCount = 0;
            _previewGroundValidCount = 0;
            _previewTotalCount = 0;
            _previewIssueSummary = string.Empty;
            _previewLimitSummary = string.Empty;
            _previewBatchActionSummary = string.Empty;
        }

        private static string PreviewIssueSummary(
            IReadOnlyDictionary<string, int> invalidReasons)
        {
            if (invalidReasons == null || invalidReasons.Count == 0) return string.Empty;
            var ordered = new List<KeyValuePair<string, int>>();
            foreach (KeyValuePair<string, int> pair in invalidReasons)
            {
                int count = Math.Max(0, pair.Value);
                if (count > 0) ordered.Add(new KeyValuePair<string, int>(pair.Key, count));
            }
            ordered.Sort((left, right) =>
            {
                int count = right.Value.CompareTo(left.Value);
                return count != 0 ? count : string.CompareOrdinal(left.Key, right.Key);
            });
            if (ordered.Count == 0) return string.Empty;

            var parts = new List<string>(Math.Min(ordered.Count, 4));
            int displayed = Math.Min(3, ordered.Count);
            for (int index = 0; index < displayed; index++)
                parts.Add(ordered[index].Value + " " + PreviewIssueLabel(ordered[index].Key));
            if (ordered.Count > displayed)
            {
                int remaining = 0;
                for (int index = displayed; index < ordered.Count; index++)
                    remaining += ordered[index].Value;
                parts.Add(remaining + " other blocked");
            }
            return string.Join(", ", parts);
        }

        private static float PreviewRefreshInterval(int previewCount)
        {
            if (previewCount > 800) return 0.35f;
            if (previewCount > 400) return 0.25f;
            if (previewCount > 128) return 0.15f;
            return 0.1f;
        }

        private string BatchActionIssue(Player player, Piece piece)
        {
            if (player == null || piece == null || SeedActionBudget(player, piece, false) <= 0)
                return string.Empty;
            if (CanStartBatchAction(player, piece, out _, out string reason))
                return string.Empty;
            switch (reason)
            {
                case AgricultureReasonCodes.NoStamina:
                    return "need stamina for the one batch action";
                case AgricultureReasonCodes.NoDurability:
                    return "cultivator needs durability for the batch";
                default:
                    return "batch action unavailable";
            }
        }

        private static string PreviewIssueLabel(string reasonCode)
        {
            switch (reasonCode)
            {
                case AgricultureReasonCodes.TerrainUnavailable: return "no terrain";
                case AgricultureReasonCodes.CropChanged: return "crop changed";
                case AgricultureReasonCodes.SpacingBlocked: return "too close";
                case AgricultureReasonCodes.SlopeInvalid: return "too steep";
                case AgricultureReasonCodes.NotCultivated: return "not cultivated";
                case AgricultureReasonCodes.BiomeInvalid: return "wrong biome";
                case AgricultureReasonCodes.OutOfRange: return "out of range";
                case AgricultureReasonCodes.WaterBlocked: return "in water";
                case AgricultureReasonCodes.WardDenied: return "ward denied";
                case AgricultureReasonCodes.NoBuildZone: return "in a no-build area";
                case AgricultureReasonCodes.PlayerBlocked: return "blocked by a player";
                case AgricultureReasonCodes.NoSeeds: return "without seeds";
                case AgricultureReasonCodes.NoDurability: return "without durability";
                case AgricultureReasonCodes.NoStamina: return "without stamina";
                case AgricultureReasonCodes.PlacementFailed: return "placement failed";
                default: return "invalid";
            }
        }

        private void RestoreBuildHintsIfOwned()
        {
            _controlBar.Restore();
        }

        private void ReportControllerProblem(Player player, string problem)
        {
            if (string.Equals(_lastControllerProblem, problem, StringComparison.Ordinal)) return;
            _lastControllerProblem = problem;
            string message = "Runic Agriculture controller controls disabled: " + problem + ".";
            _log.LogError(message + " Keyboard controls remain available.");
            Message(player, message + " Use keyboard controls or correct Configuration Manager.");
        }

        private string FindPlantForMature(string matureCropId)
        {
            if (_matureToPlant.TryGetValue(matureCropId, out string cached)) return cached;
            if (ZNetScene.instance == null) return string.Empty;
            List<string> names = ZNetScene.instance.GetPrefabNames();
            names.Sort(StringComparer.Ordinal);
            for (int index = 0; index < names.Count; index++)
            {
                GameObject prefab = ZNetScene.instance.GetPrefab(names[index]);
                Plant plant = prefab != null ? prefab.GetComponent<Plant>() : null;
                Piece piece = prefab != null ? prefab.GetComponent<Piece>() : null;
                if (plant == null || piece == null || plant.m_grownPrefabs == null) continue;
                for (int grownIndex = 0; grownIndex < plant.m_grownPrefabs.Length; grownIndex++)
                {
                    GameObject grown = plant.m_grownPrefabs[grownIndex];
                    string grownId = PrefabIdentity.Of(grown);
                    if (!string.IsNullOrEmpty(grownId) && !_matureToPlant.ContainsKey(grownId))
                        _matureToPlant.Add(grownId, PrefabIdentity.Of(prefab));
                }
            }
            if (_matureToPlant.TryGetValue(matureCropId, out cached)) return cached;
            _matureToPlant[matureCropId] = string.Empty;
            return string.Empty;
        }

        private static bool TryCaptureHarvestCandidate(
            Pickable pickable,
            out HarvestPickableCandidate candidate)
        {
            candidate = null;
            if (!IsPickableReady(pickable, out ZDO zdo)) return false;
            int prefabHash = zdo.GetPrefab();
            GameObject registeredPrefab = ZNetScene.instance != null
                ? ZNetScene.instance.GetPrefab(prefabHash)
                : null;
            Pickable registeredPickable = registeredPrefab
                ? registeredPrefab.GetComponent<Pickable>()
                : null;
            string prefabName = registeredPickable
                ? PrefabIdentity.Of(registeredPrefab)
                : string.Empty;
            Vector3 position = pickable.transform.position;
            if (string.IsNullOrEmpty(prefabName) || prefabHash == 0 ||
                float.IsNaN(position.x) || float.IsInfinity(position.x) ||
                float.IsNaN(position.y) || float.IsInfinity(position.y) ||
                float.IsNaN(position.z) || float.IsInfinity(position.z)) return false;
            candidate = new HarvestPickableCandidate(
                pickable,
                prefabName,
                prefabHash,
                zdo.m_uid,
                position);
            return true;
        }

        internal static bool IsPickableReady(Pickable pickable, out ZDO zdo)
        {
            zdo = null;
            if (!pickable ||
                !HarvestPickableBatchPolicy.Supports(HarvestComponentContract.Pickable))
                return false;
            ZNetView view = pickable.GetComponent<ZNetView>();
            zdo = view && view.IsValid() ? view.GetZDO() : null;
            bool identityValid = zdo != null && zdo.IsValid() && !zdo.m_uid.IsNone() &&
                                 zdo.GetPrefab() != 0 && view.GetZDO() == zdo;
            bool inTar = false;
            if (pickable.m_tarPreventsPicking)
            {
                Floating floating = pickable.GetComponent<Floating>();
                inTar = floating && floating.IsInTar();
            }
            bool picked = pickable.GetPicked() ||
                          identityValid && zdo.GetBool(ZDOVars.s_picked, false);
            if (!HarvestPickableBatchPolicy.IsReady(
                    view && view.IsValid(),
                    identityValid,
                    pickable.CanBePicked(),
                    picked,
                    pickable.m_tarPreventsPicking,
                    inTar))
            {
                zdo = null;
                return false;
            }
            return true;
        }

        private static Pickable FindPickable(GameObject gameObject)
        {
            if (gameObject == null) return null;
            Pickable pickable = gameObject.GetComponent<Pickable>();
            return pickable != null ? pickable : gameObject.GetComponentInParent<Pickable>();
        }

        private static bool IsPlantPiece(Piece piece) =>
            piece != null && piece.GetComponent<Plant>() != null;

        private bool TryEnterMutation(string boundary, out IDisposable lease)
        {
            lease = null;
            if (_disabledForSession ||
                Interlocked.CompareExchange(ref _mutationActive, 1, 0) != 0)
                return false;
            lease = new MutationScope(this);
            return true;
        }

        private sealed class MutationScope : IDisposable
        {
            private AgricultureRuntime _owner;

            internal MutationScope(AgricultureRuntime owner) => _owner = owner;

            public void Dispose()
            {
                AgricultureRuntime owner = Interlocked.Exchange(ref _owner, null);
                if (owner != null) Interlocked.Exchange(ref owner._mutationActive, 0);
            }
        }

        private static string ShortcutLabel(BepInEx.Configuration.KeyboardShortcut shortcut)
        {
            var parts = new List<string>();
            IEnumerable<KeyCode> modifiers = shortcut.Modifiers;
            if (modifiers != null)
                foreach (KeyCode modifier in modifiers) parts.Add(FriendlyKey(modifier));
            parts.Add(FriendlyKey(shortcut.MainKey));
            return string.Join(" + ", parts);
        }

        private static string FriendlyKey(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.LeftAlt: return "Left Alt";
                case KeyCode.RightAlt: return "Right Alt";
                case KeyCode.LeftControl: return "Left Ctrl";
                case KeyCode.RightControl: return "Right Ctrl";
                case KeyCode.LeftShift: return "Left Shift";
                case KeyCode.RightShift: return "Right Shift";
                default: return key.ToString();
            }
        }

        private static void Message(Player player, string text)
        {
            if (player != null)
                player.Message(MessageHud.MessageType.Center, text, 0, null);
        }

        private static void StatusMessage(Player player, string text)
        {
            if (player != null)
                player.Message(MessageHud.MessageType.TopLeft, text, 0, null);
        }

        private void Trace(string text)
        {
            if (AgricultureConfig.VerboseLogging.Value)
                _log.LogInfo("[Agriculture verbose] " + text);
        }

        private void Deny(Player player, string reasonCode)
        {
            string friendly = FriendlyReason(reasonCode);
            Message(player, friendly);
            Trace("action denied: " + reasonCode + ".");
            Publish(reasonCode, friendly, "Adjust amber blocked positions or supply the missing planting resources, then confirm again.");
        }

        private static string FriendlyReason(string reasonCode)
        {
            switch (reasonCode)
            {
                case AgricultureReasonCodes.Valid:
                    return "Runic agriculture completed.";
                case AgricultureReasonCodes.NotAuthoritative:
                    return "Runic agriculture: the local player is not the authoritative owner.";
                case AgricultureReasonCodes.CropChanged:
                    return "Runic agriculture: the selected crop changed; review the new preview.";
                case AgricultureReasonCodes.InvalidBatchBlocked:
                    return "Runic agriculture: confirmation blocked because at least one preview is invalid.";
                case AgricultureReasonCodes.CostBatchBlocked:
                    return "Runic agriculture: confirmation blocked because full costs are unavailable.";
                case AgricultureReasonCodes.NoSeeds:
                    return "Runic agriculture stopped when personal inventory and eligible nearby chests ran out of planting resources.";
                case AgricultureReasonCodes.NoDurability:
                    return "Runic agriculture stopped before cultivator durability was exhausted.";
                case AgricultureReasonCodes.NoStamina:
                    return "Runic agriculture stopped at the available stamina budget.";
                case AgricultureReasonCodes.ReplantNotOffered:
                    return "Runic replant: no harvested positions are awaiting confirmation.";
                case AgricultureReasonCodes.ReplantCropMismatch:
                    return "Runic replant: select the crop matching the pending harvest.";
                case AgricultureReasonCodes.WardDenied:
                    return "Runic agriculture: a ward denies this position.";
                case AgricultureReasonCodes.SpacingBlocked:
                    return "Runic agriculture: crop spacing changed; remaining positions were not planted.";
                case AgricultureReasonCodes.WaterBlocked:
                    return "Runic agriculture: this crop cannot be planted in water.";
                case AgricultureReasonCodes.TerrainUnavailable:
                    return "Runic agriculture: terrain could not be sampled at this position.";
                case AgricultureReasonCodes.SlopeInvalid:
                    return "Runic agriculture: this position is too steep.";
                case AgricultureReasonCodes.BiomeInvalid:
                    return "Runic agriculture: this crop cannot grow in the current biome.";
                case AgricultureReasonCodes.NotCultivated:
                    return "Runic agriculture: this position requires cultivated ground.";
                case AgricultureReasonCodes.OutOfRange:
                    return "Runic agriculture: this position is outside placement range.";
                case AgricultureReasonCodes.NoBuildZone:
                    return "Runic agriculture: building is not allowed at this position.";
                case AgricultureReasonCodes.PlayerBlocked:
                    return "Runic agriculture: a player is blocking this position.";
                case AgricultureReasonCodes.PlacementFailed:
                    return "Runic agriculture: Valheim rejected the placement; no further positions were attempted.";
                default:
                    return "Runic agriculture stopped: " + reasonCode + ".";
            }
        }

        private static void Publish(string code, string reason, string remedy)
        {
            try
            {
                Plugin.Instance?.Log.LogWarning(
                    "Agriculture " + code + ": " + reason + " Remedy: " + remedy);
            }
            catch (Exception)
            {
                // Logging is presentation only; it never controls agriculture.
            }
        }

        private sealed class PreviewSnapshot
        {
            internal PreviewSnapshot(
                string cropId,
                Piece piece,
                Quaternion rotation,
                IReadOnlyList<Vector3> positions,
                bool isReplant)
            {
                CropId = cropId;
                Piece = piece;
                Rotation = rotation;
                Positions = positions;
                IsReplant = isReplant;
            }

            internal string CropId { get; }
            internal Piece Piece { get; }
            internal Quaternion Rotation { get; }
            internal IReadOnlyList<Vector3> Positions { get; }
            internal bool IsReplant { get; }
        }

        private sealed class HarvestPickableCandidate
        {
            internal HarvestPickableCandidate(
                Pickable pickable,
                string prefabName,
                int prefabHash,
                ZDOID zdoId,
                Vector3 position)
            {
                Pickable = pickable;
                PrefabName = prefabName;
                PrefabHash = prefabHash;
                ZdoId = zdoId;
                Position = position;
            }

            internal Pickable Pickable { get; }
            internal string PrefabName { get; }
            internal int PrefabHash { get; }
            internal ZDOID ZdoId { get; }
            internal Vector3 Position { get; }
        }
    }
}

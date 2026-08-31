using System;
using System.Collections.Generic;
using System.Globalization;

namespace RunicAgriculture.Core
{
    /// <summary>
    /// Valheim controller action names that Runic Agriculture may read through ZInput.
    /// The defaults use semantic actions so they follow the player's selected controller layout.
    /// </summary>
    public enum ValheimControllerAction
    {
        JoyAltKeys = 0,
        JoyPlace = 1,
        JoyRotate = 2,
        JoyUse = 3,
        JoyRemove = 4,
        JoyButtonA = 5,
        JoyButtonB = 6,
        JoyButtonX = 7,
        JoyButtonY = 8,
        JoyDPadUp = 9,
        JoyDPadDown = 10,
        JoyDPadLeft = 11,
        JoyDPadRight = 12,
        JoyLBumper = 13,
        JoyRBumper = 14,
        JoyLTrigger = 15,
        JoyRTrigger = 16,
        JoyLStick = 17,
        JoyRStick = 18,
        JoyPrevSnap = 19
    }

    public sealed class AgricultureControllerBindings
    {
        public AgricultureControllerBindings(
            ValheimControllerAction modifier,
            ValheimControllerAction confirm,
            ValheimControllerAction cycle,
            ValheimControllerAction areaHarvest)
            : this(
                modifier,
                confirm,
                cycle,
                areaHarvest,
                ValheimControllerAction.JoyDPadUp,
                ValheimControllerAction.JoyDPadDown,
                ValheimControllerAction.JoyDPadLeft,
                ValheimControllerAction.JoyDPadRight)
        {
        }

        public AgricultureControllerBindings(
            ValheimControllerAction modifier,
            ValheimControllerAction confirm,
            ValheimControllerAction cycle,
            ValheimControllerAction areaHarvest,
            ValheimControllerAction previousEditorField,
            ValheimControllerAction nextEditorField,
            ValheimControllerAction decreaseEditorValue,
            ValheimControllerAction increaseEditorValue)
        {
            if (!Enum.IsDefined(typeof(ValheimControllerAction), modifier))
                throw new ArgumentOutOfRangeException(nameof(modifier));
            if (!Enum.IsDefined(typeof(ValheimControllerAction), confirm))
                throw new ArgumentOutOfRangeException(nameof(confirm));
            if (!Enum.IsDefined(typeof(ValheimControllerAction), cycle))
                throw new ArgumentOutOfRangeException(nameof(cycle));
            if (!Enum.IsDefined(typeof(ValheimControllerAction), areaHarvest))
                throw new ArgumentOutOfRangeException(nameof(areaHarvest));
            if (!Enum.IsDefined(typeof(ValheimControllerAction), previousEditorField))
                throw new ArgumentOutOfRangeException(nameof(previousEditorField));
            if (!Enum.IsDefined(typeof(ValheimControllerAction), nextEditorField))
                throw new ArgumentOutOfRangeException(nameof(nextEditorField));
            if (!Enum.IsDefined(typeof(ValheimControllerAction), decreaseEditorValue))
                throw new ArgumentOutOfRangeException(nameof(decreaseEditorValue));
            if (!Enum.IsDefined(typeof(ValheimControllerAction), increaseEditorValue))
                throw new ArgumentOutOfRangeException(nameof(increaseEditorValue));
            Modifier = modifier;
            Confirm = confirm;
            Cycle = cycle;
            AreaHarvest = areaHarvest;
            PreviousEditorField = previousEditorField;
            NextEditorField = nextEditorField;
            DecreaseEditorValue = decreaseEditorValue;
            IncreaseEditorValue = increaseEditorValue;
        }

        public ValheimControllerAction Modifier { get; }
        public ValheimControllerAction Confirm { get; }
        public ValheimControllerAction Cycle { get; }
        public ValheimControllerAction AreaHarvest { get; }
        public ValheimControllerAction PreviousEditorField { get; }
        public ValheimControllerAction NextEditorField { get; }
        public ValheimControllerAction DecreaseEditorValue { get; }
        public ValheimControllerAction IncreaseEditorValue { get; }

        public bool TryValidate(out string problem)
        {
            ValheimControllerAction[] actions = AllActions();
            for (int index = 1; index < actions.Length; index++)
            {
                if (actions[index] == Modifier)
                {
                    problem = "the controller modifier must differ from every action";
                    return false;
                }
                for (int other = index + 1; other < actions.Length; other++)
                {
                    if (actions[index] != actions[other]) continue;
                    problem = "every controller agriculture action must use a different control";
                    return false;
                }
            }
            problem = string.Empty;
            return true;
        }

        public bool TryValidateEffectivePaths(
            string modifierPath,
            string confirmPath,
            string cyclePath,
            string areaHarvestPath,
            out string problem)
        {
            return TryValidatePathSet(
                new[] { "modifier", "confirm", "cycle", "area harvest" },
                new[] { modifierPath, confirmPath, cyclePath, areaHarvestPath },
                out problem);
        }

        public bool TryValidateEffectivePaths(
            string modifierPath,
            string confirmPath,
            string cyclePath,
            string areaHarvestPath,
            string previousEditorFieldPath,
            string nextEditorFieldPath,
            string decreaseEditorValuePath,
            string increaseEditorValuePath,
            out string problem)
        {
            return TryValidatePathSet(
                new[]
                {
                    "modifier", "confirm", "cycle", "area harvest",
                    "previous editor field", "next editor field",
                    "decrease editor value", "increase editor value"
                },
                new[]
                {
                    modifierPath, confirmPath, cyclePath, areaHarvestPath,
                    previousEditorFieldPath, nextEditorFieldPath,
                    decreaseEditorValuePath, increaseEditorValuePath
                },
                out problem);
        }

        public bool ContainsChordPrimary(ValheimControllerAction action) =>
            action == Confirm || action == Cycle || action == AreaHarvest;

        public bool ContainsEditorPrimary(ValheimControllerAction action) =>
            action == PreviousEditorField || action == NextEditorField ||
            action == DecreaseEditorValue || action == IncreaseEditorValue;

        private bool TryValidatePathSet(
            string[] labels,
            string[] paths,
            out string problem)
        {
            if (!TryValidate(out problem)) return false;
            var owners = new System.Collections.Generic.Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < paths.Length; index++)
            {
                string path = paths[index]?.Trim();
                if (string.IsNullOrEmpty(path))
                {
                    problem = labels[index] + " has no effective controller action path";
                    return false;
                }
                if (owners.TryGetValue(path, out string existing))
                {
                    problem = labels[index] + " resolves to the same physical control as " + existing;
                    return false;
                }
                owners.Add(path, labels[index]);
            }
            problem = string.Empty;
            return true;
        }

        public string ConfirmChord => FormatChord(Confirm);
        public string CycleChord => FormatChord(Cycle);
        public string AreaHarvestChord => FormatChord(AreaHarvest);

        private ValheimControllerAction[] AllActions() => new[]
        {
            Modifier,
            Confirm,
            Cycle,
            AreaHarvest,
            PreviousEditorField,
            NextEditorField,
            DecreaseEditorValue,
            IncreaseEditorValue
        };

        private string FormatChord(ValheimControllerAction primary) =>
            ControllerActionDisplay.Friendly(Modifier) + " + " +
            ControllerActionDisplay.Friendly(primary);
    }

    public static class ControllerActionDisplay
    {
        public static string Friendly(ValheimControllerAction action)
        {
            switch (action)
            {
                case ValheimControllerAction.JoyAltKeys: return "Controller Alt";
                case ValheimControllerAction.JoyPlace: return "Place";
                case ValheimControllerAction.JoyRotate: return "Rotate";
                case ValheimControllerAction.JoyUse: return "Use";
                case ValheimControllerAction.JoyRemove: return "Remove";
                case ValheimControllerAction.JoyButtonA: return "A / Cross";
                case ValheimControllerAction.JoyButtonB: return "B / Circle";
                case ValheimControllerAction.JoyButtonX: return "X / Square";
                case ValheimControllerAction.JoyButtonY: return "Y / Triangle";
                case ValheimControllerAction.JoyDPadUp: return "D-pad Up";
                case ValheimControllerAction.JoyDPadDown: return "D-pad Down";
                case ValheimControllerAction.JoyDPadLeft: return "D-pad Left";
                case ValheimControllerAction.JoyDPadRight: return "D-pad Right";
                case ValheimControllerAction.JoyLBumper: return "Left Bumper";
                case ValheimControllerAction.JoyRBumper: return "Right Bumper";
                case ValheimControllerAction.JoyLTrigger: return "Left Trigger";
                case ValheimControllerAction.JoyRTrigger: return "Right Trigger";
                case ValheimControllerAction.JoyLStick: return "Left Stick Click";
                case ValheimControllerAction.JoyRStick: return "Right Stick Click";
                case ValheimControllerAction.JoyPrevSnap: return "Left Stick Click";
                default: throw new ArgumentOutOfRangeException(nameof(action));
            }
        }
    }

    public static class AgricultureControllerPathRouting
    {
        public static bool ShouldSuppress(
            string requestedPath,
            string acceptedModifierPath,
            string acceptedPrimaryPath)
        {
            if (string.IsNullOrWhiteSpace(requestedPath)) return false;
            string requested = requestedPath.Trim();
            return (!string.IsNullOrWhiteSpace(acceptedModifierPath) &&
                    string.Equals(
                        requested,
                        acceptedModifierPath.Trim(),
                        StringComparison.OrdinalIgnoreCase)) ||
                   (!string.IsNullOrWhiteSpace(acceptedPrimaryPath) &&
                    string.Equals(
                        requested,
                        acceptedPrimaryPath.Trim(),
                        StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Tracks the effective paths consumed by one controller-modifier session. Primaries leave
    /// independently after their release frame, so the player can keep the modifier held and
    /// safely issue another validated agriculture action without exposing that action to vanilla.
    /// </summary>
    internal sealed class AgricultureControllerSuppressionSession
    {
        private const int MaximumPrimaryPaths = 7;
        private readonly List<PrimaryLatch> _primaries = new List<PrimaryLatch>();
        private string _modifierPath;
        private int _modifierReleasedFrame = -1;

        internal bool IsActive => !string.IsNullOrEmpty(_modifierPath);
        internal string ModifierPath => _modifierPath;
        internal int PrimaryCount => _primaries.Count;

        internal bool TryCapture(
            string modifierPath,
            string primaryPath,
            out string problem)
        {
            string modifier = Normalize(modifierPath);
            string primary = Normalize(primaryPath);
            if (modifier.Length == 0 || primary.Length == 0 || Same(modifier, primary))
            {
                problem = "the accepted controller chord no longer has two distinct effective paths";
                return false;
            }
            if (IsActive && !Same(_modifierPath, modifier))
            {
                problem = "the controller modifier changed while an accepted gesture is being released";
                return false;
            }

            for (int index = 0; index < _primaries.Count; index++)
            {
                if (!Same(_primaries[index].Path, primary)) continue;
                _primaries[index].ReleasedFrame = -1;
                problem = string.Empty;
                return true;
            }
            if (_primaries.Count >= MaximumPrimaryPaths)
            {
                problem = "too many controller primaries are still being released";
                return false;
            }

            if (!IsActive) _modifierPath = modifier;
            _modifierReleasedFrame = -1;
            _primaries.Add(new PrimaryLatch(primary));
            problem = string.Empty;
            return true;
        }

        internal void Advance(
            int frame,
            bool modifierHeld,
            Func<string, bool> primaryHeld)
        {
            if (!IsActive) return;
            if (primaryHeld == null) throw new ArgumentNullException(nameof(primaryHeld));

            for (int index = _primaries.Count - 1; index >= 0; index--)
            {
                PrimaryLatch primary = _primaries[index];
                if (primaryHeld(primary.Path))
                {
                    primary.ReleasedFrame = -1;
                    continue;
                }
                if (primary.ReleasedFrame < 0)
                {
                    primary.ReleasedFrame = frame;
                    continue;
                }
                if (primary.ReleasedFrame != frame) _primaries.RemoveAt(index);
            }

            if (modifierHeld)
            {
                _modifierReleasedFrame = -1;
                return;
            }
            if (_modifierReleasedFrame < 0)
            {
                _modifierReleasedFrame = frame;
                return;
            }
            if (_modifierReleasedFrame != frame && _primaries.Count == 0) Reset();
        }

        internal bool ContainsPrimary(string path)
        {
            string normalized = Normalize(path);
            for (int index = 0; index < _primaries.Count; index++)
                if (Same(_primaries[index].Path, normalized)) return true;
            return false;
        }

        internal bool ShouldSuppress(string requestedPath)
        {
            string requested = Normalize(requestedPath);
            if (requested.Length == 0 || !IsActive) return false;
            if (Same(requested, _modifierPath)) return true;
            return ContainsPrimary(requested);
        }

        internal void Reset()
        {
            _primaries.Clear();
            _modifierPath = null;
            _modifierReleasedFrame = -1;
        }

        private static string Normalize(string path) => path?.Trim() ?? string.Empty;

        private static bool Same(string left, string right) =>
            string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

        private sealed class PrimaryLatch
        {
            internal PrimaryLatch(string path) => Path = path;
            internal string Path { get; }
            internal int ReleasedFrame { get; set; } = -1;
        }
    }

    /// <summary>
    /// Tracks context-owned unmodified editor controls independently through their release frame.
    /// Storage and Inventory use Controller Alt chords; keeping these editor controls unmodified
    /// makes the crop-preview context the additional namespace instead of competing for a chord.
    /// </summary>
    internal sealed class AgricultureUnmodifiedControllerSuppression
    {
        private const int MaximumPaths = 4;
        private readonly List<PrimaryLatch> _paths = new List<PrimaryLatch>();

        internal bool IsActive => _paths.Count != 0;
        internal int Count => _paths.Count;

        internal bool TryCapture(string path, out string problem)
        {
            string normalized = Normalize(path);
            if (normalized.Length == 0)
            {
                problem = "the accepted unmodified editor control has no effective path";
                return false;
            }
            for (int index = 0; index < _paths.Count; index++)
            {
                if (!Same(_paths[index].Path, normalized)) continue;
                _paths[index].ReleasedFrame = -1;
                problem = string.Empty;
                return true;
            }
            if (_paths.Count >= MaximumPaths)
            {
                problem = "too many unmodified editor controls are still being released";
                return false;
            }
            _paths.Add(new PrimaryLatch(normalized));
            problem = string.Empty;
            return true;
        }

        internal void Advance(int frame, Func<string, bool> held)
        {
            if (held == null) throw new ArgumentNullException(nameof(held));
            for (int index = _paths.Count - 1; index >= 0; index--)
            {
                PrimaryLatch path = _paths[index];
                if (held(path.Path))
                {
                    path.ReleasedFrame = -1;
                    continue;
                }
                if (path.ReleasedFrame < 0)
                {
                    path.ReleasedFrame = frame;
                    continue;
                }
                if (path.ReleasedFrame != frame) _paths.RemoveAt(index);
            }
        }

        internal bool ShouldSuppress(string path)
        {
            string normalized = Normalize(path);
            for (int index = 0; index < _paths.Count; index++)
                if (Same(_paths[index].Path, normalized)) return true;
            return false;
        }

        internal bool Contains(string path) => ShouldSuppress(path);

        internal void Reset() => _paths.Clear();

        private static string Normalize(string path) => path?.Trim() ?? string.Empty;

        private static bool Same(string left, string right) =>
            string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

        private sealed class PrimaryLatch
        {
            internal PrimaryLatch(string path) => Path = path;
            internal string Path { get; }
            internal int ReleasedFrame { get; set; } = -1;
        }
    }

    public enum RoutedAgricultureAction
    {
        None = 0,
        CyclePattern = 1,
        ConfirmPattern = 2,
        ConfirmReplant = 3
    }

    public readonly struct AgricultureInputFrame
    {
        public AgricultureInputFrame(
            bool keyboardCycle,
            bool keyboardConfirmPattern,
            bool keyboardConfirmReplant,
            bool controllerCycle,
            bool controllerConfirm,
            bool replantPreviewReady)
        {
            KeyboardCycle = keyboardCycle;
            KeyboardConfirmPattern = keyboardConfirmPattern;
            KeyboardConfirmReplant = keyboardConfirmReplant;
            ControllerCycle = controllerCycle;
            ControllerConfirm = controllerConfirm;
            ReplantPreviewReady = replantPreviewReady;
        }

        public bool KeyboardCycle { get; }
        public bool KeyboardConfirmPattern { get; }
        public bool KeyboardConfirmReplant { get; }
        public bool ControllerCycle { get; }
        public bool ControllerConfirm { get; }
        public bool ReplantPreviewReady { get; }
    }

    /// <summary>
    /// Resolves at most one state-changing agriculture action per input frame. Cycling wins over
    /// confirmation so a simultaneous chord can never confirm a now-stale preview.
    /// </summary>
    public static class AgricultureActionRouter
    {
        public static RoutedAgricultureAction Resolve(AgricultureInputFrame input)
        {
            if (input.KeyboardCycle || input.ControllerCycle)
                return RoutedAgricultureAction.CyclePattern;
            if (input.KeyboardConfirmReplant)
                return RoutedAgricultureAction.ConfirmReplant;
            if (input.KeyboardConfirmPattern)
                return RoutedAgricultureAction.ConfirmPattern;
            if (!input.ControllerConfirm) return RoutedAgricultureAction.None;
            return input.ReplantPreviewReady
                ? RoutedAgricultureAction.ConfirmReplant
                : RoutedAgricultureAction.ConfirmPattern;
        }
    }

    public static class AgricultureFeedbackText
    {
        public static string Preview(
            PlantPattern pattern,
            int valid,
            int total,
            bool replant,
            string confirmControl,
            string cycleControl)
        {
            if (valid < 0 || total < 0 || valid > total)
                throw new ArgumentOutOfRangeException(nameof(valid));
            string kind = replant ? "replant" : pattern.ToString().ToLowerInvariant();
            string result = "Runic " + kind + ": " + valid + "/" + total +
                            " positions ready | Confirm " + Require(confirmControl, nameof(confirmControl));
            return replant
                ? result
                : result + " | Cycle " + Require(cycleControl, nameof(cycleControl));
        }

        public static string Configuration(
            bool enabled,
            PlantPattern pattern,
            int rows,
            int columns,
            double spacing,
            int maximumPreview,
            double harvestRadius,
            int maximumHarvest)
        {
            if (rows < 1 || columns < 1 || maximumPreview < 1 || maximumHarvest < 1)
                throw new ArgumentOutOfRangeException(nameof(rows));
            return (enabled ? "enabled" : "disabled") + "; pattern " + pattern + " " +
                   rows + "x" + columns + " at " +
                   spacing.ToString("0.##", CultureInfo.InvariantCulture) +
                   "m; preview cap " + maximumPreview + "; harvest " +
                   harvestRadius.ToString("0.##", CultureInfo.InvariantCulture) +
                   "m / " + maximumHarvest + " plants";
        }

        public static string PatternEditing(
            PlantPattern pattern,
            int rows,
            int columns,
            bool mirrored,
            double leftPinch,
            double rightPinch,
            string rowControls,
            string columnControls,
            string sideControl,
            string leftPinchControls,
            string rightPinchControls)
        {
            if (rows < 1 || columns < 1 || leftPinch < 0d || leftPinch > 1d ||
                rightPinch < 0d || rightPinch > 1d)
                throw new ArgumentOutOfRangeException(nameof(rows));
            string dimensions = pattern == PlantPattern.Row
                ? columns + " columns"
                : rows + "x" + columns;
            string result = "Shape " + dimensions + " | " +
                            (pattern == PlantPattern.Row
                                ? "Columns " + Require(columnControls, nameof(columnControls))
                                : "Rows " + Require(rowControls, nameof(rowControls)) +
                                  ", columns " + Require(columnControls, nameof(columnControls)));
            if (PatternEditor.SupportsMirror(pattern))
                result += " | " + PatternEditor.OrientationLabel(pattern, mirrored) + " " +
                          Require(sideControl, nameof(sideControl));
            if (pattern == PlantPattern.Trapezoid)
                result += " | Taper L " + Percent(leftPinch) + " " +
                          Require(leftPinchControls, nameof(leftPinchControls)) +
                          ", R " + Percent(rightPinch) + " " +
                          Require(rightPinchControls, nameof(rightPinchControls));
            return result;
        }

        private static string Percent(double value) =>
            Math.Round(value * 100d).ToString("0", CultureInfo.InvariantCulture) + "%";

        private static string Require(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Control text is required.", name);
            return value.Trim();
        }
    }
}

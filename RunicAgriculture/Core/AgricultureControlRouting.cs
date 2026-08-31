using System;
using System.Collections.Generic;

namespace RunicAgriculture.Core
{
    public enum AgricultureWheelTarget
    {
        None = 0,
        Rows = 1,
        Columns = 2,
        Spacing = 3,
        Rotation = 4
    }

    /// <summary>
    /// Exact mouse-wheel precedence for a live crop preview. Bare wheel rotates the planting
    /// footprint. Alt+Shift is the most-specific edit chord and wins over its two subsets.
    /// </summary>
    public static class AgricultureWheelRouter
    {
        public static AgricultureWheelTarget Resolve(
            bool altHeld,
            bool shiftHeld,
            bool controlHeld,
            double wheelDelta)
        {
            if (double.IsNaN(wheelDelta) || double.IsInfinity(wheelDelta))
                throw new ArgumentOutOfRangeException(nameof(wheelDelta));
            if (wheelDelta == 0d || controlHeld) return AgricultureWheelTarget.None;
            if (altHeld && shiftHeld) return AgricultureWheelTarget.Spacing;
            if (altHeld) return AgricultureWheelTarget.Rows;
            if (shiftHeld) return AgricultureWheelTarget.Columns;
            return AgricultureWheelTarget.Rotation;
        }

        public static PatternEditAction ToEditAction(
            AgricultureWheelTarget target,
            bool increase)
        {
            switch (target)
            {
                case AgricultureWheelTarget.Rows:
                    return increase
                        ? PatternEditAction.IncreaseRows
                        : PatternEditAction.DecreaseRows;
                case AgricultureWheelTarget.Columns:
                    return increase
                        ? PatternEditAction.IncreaseColumns
                        : PatternEditAction.DecreaseColumns;
                case AgricultureWheelTarget.Spacing:
                    return increase
                        ? PatternEditAction.IncreaseSpacing
                        : PatternEditAction.DecreaseSpacing;
                case AgricultureWheelTarget.None:
                case AgricultureWheelTarget.Rotation:
                    return PatternEditAction.None;
                default:
                    throw new ArgumentOutOfRangeException(nameof(target));
            }
        }
    }

    public static class AgriculturePatternHotkeys
    {
        public static bool TryResolveNumpadSlot(int slot, out PlantPattern pattern)
        {
            if (slot >= 1 && slot <= 7)
            {
                pattern = (PlantPattern)(slot - 1);
                return Enum.IsDefined(typeof(PlantPattern), pattern);
            }
            pattern = default;
            return false;
        }
    }

    public enum ControllerEditorField
    {
        Rows = 0,
        Columns = 1,
        Spacing = 2,
        Side = 3,
        LeftTaper = 4,
        RightTaper = 5
    }

    public static class ControllerPatternEditor
    {
        private static readonly ControllerEditorField[] RowFields =
        {
            ControllerEditorField.Columns,
            ControllerEditorField.Spacing
        };

        private static readonly ControllerEditorField[] SymmetricFields =
        {
            ControllerEditorField.Rows,
            ControllerEditorField.Columns,
            ControllerEditorField.Spacing
        };

        private static readonly ControllerEditorField[] MirroredFields =
        {
            ControllerEditorField.Rows,
            ControllerEditorField.Columns,
            ControllerEditorField.Spacing,
            ControllerEditorField.Side
        };

        private static readonly ControllerEditorField[] TrapezoidFields =
        {
            ControllerEditorField.Rows,
            ControllerEditorField.Columns,
            ControllerEditorField.Spacing,
            ControllerEditorField.Side,
            ControllerEditorField.LeftTaper,
            ControllerEditorField.RightTaper
        };

        public static IReadOnlyList<ControllerEditorField> FieldsFor(PlantPattern pattern)
        {
            if (!Enum.IsDefined(typeof(PlantPattern), pattern))
                throw new ArgumentOutOfRangeException(nameof(pattern));
            if (pattern == PlantPattern.Row) return RowFields;
            if (pattern == PlantPattern.Trapezoid) return TrapezoidFields;
            return PatternEditor.SupportsMirror(pattern) ? MirroredFields : SymmetricFields;
        }

        public static ControllerEditorField Normalize(
            PlantPattern pattern,
            ControllerEditorField current)
        {
            IReadOnlyList<ControllerEditorField> fields = FieldsFor(pattern);
            for (int index = 0; index < fields.Count; index++)
                if (fields[index] == current) return current;
            return fields[0];
        }

        public static ControllerEditorField Move(
            PlantPattern pattern,
            ControllerEditorField current,
            int direction)
        {
            if (direction == 0) return Normalize(pattern, current);
            IReadOnlyList<ControllerEditorField> fields = FieldsFor(pattern);
            ControllerEditorField normalized = Normalize(pattern, current);
            int index = 0;
            while (index < fields.Count && fields[index] != normalized) index++;
            int next = (index + (direction > 0 ? 1 : -1) + fields.Count) % fields.Count;
            return fields[next];
        }

        public static PatternEditAction ToEditAction(
            ControllerEditorField field,
            bool increase)
        {
            switch (field)
            {
                case ControllerEditorField.Rows:
                    return increase
                        ? PatternEditAction.IncreaseRows
                        : PatternEditAction.DecreaseRows;
                case ControllerEditorField.Columns:
                    return increase
                        ? PatternEditAction.IncreaseColumns
                        : PatternEditAction.DecreaseColumns;
                case ControllerEditorField.Spacing:
                    return increase
                        ? PatternEditAction.IncreaseSpacing
                        : PatternEditAction.DecreaseSpacing;
                case ControllerEditorField.Side:
                    return PatternEditAction.ToggleSide;
                case ControllerEditorField.LeftTaper:
                    return increase
                        ? PatternEditAction.IncreaseLeftPinch
                        : PatternEditAction.DecreaseLeftPinch;
                case ControllerEditorField.RightTaper:
                    return increase
                        ? PatternEditAction.IncreaseRightPinch
                        : PatternEditAction.DecreaseRightPinch;
                default:
                    throw new ArgumentOutOfRangeException(nameof(field));
            }
        }
    }
}

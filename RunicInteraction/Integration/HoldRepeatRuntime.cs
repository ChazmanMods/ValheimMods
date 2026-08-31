using System;
using System.Runtime.CompilerServices;

namespace RunicInteraction.Integration
{
    internal static class HoldRepeatRuntime
    {
        private sealed class Marker { }
        private static readonly ConditionalWeakTable<Switch, Marker> Inputs =
            new ConditionalWeakTable<Switch, Marker>();

        internal readonly struct IntervalState
        {
            internal IntervalState(bool changed, float previous)
            {
                Changed = changed;
                Previous = previous;
            }

            internal bool Changed { get; }
            internal float Previous { get; }
        }

        internal static void Register(Switch input)
        {
            if (!input) return;
            try { Inputs.GetValue(input, _ => new Marker()); }
            catch (ArgumentException) { }
        }

        internal static IntervalState Begin(Switch input, bool hold)
        {
            if (!hold || !FeatureOn() || !input || !Inputs.TryGetValue(input, out _))
                return default;
            float previous = input.m_holdRepeatInterval;
            // Player.Interact is already the installed vanilla 0.2 s cadence. A positive epsilon
            // enables Switch hold handling without adding a second, drifting throttle.
            input.m_holdRepeatInterval = float.Epsilon;
            return new IntervalState(true, previous);
        }

        internal static void End(Switch input, IntervalState state)
        {
            if (state.Changed && input) input.m_holdRepeatInterval = state.Previous;
        }

        internal static bool ShouldConvertCookingHold(CookingStation station, bool hold)
        {
            return hold && FeatureOn() && station && !station.m_addFoodSwitch &&
                   !ValheimAccess.CookingHasDoneItem(station);
        }

        internal static bool ShouldConvertFermenterHold(Fermenter station, bool hold)
        {
            if (!hold || !FeatureOn() || !station) return false;
            ZNetView view = station.GetComponent<ZNetView>();
            ZDO zdo = view ? view.GetZDO() : null;
            return zdo != null && string.IsNullOrEmpty(zdo.GetString(ZDOVars.s_content));
        }

        private static bool FeatureOn() =>
            InteractionConfig.Enabled.Value && InteractionConfig.HoldToRepeat.Value;
    }
}

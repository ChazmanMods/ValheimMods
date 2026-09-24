using System;

namespace Runic.Shared;

internal static class CaptionCurve
{
    // Keep serialized coefficients unchanged. UI units are 100 times finer than the old control.
    internal const float DisplayLimit = 100f;
    internal static float ToDisplay(float coefficient) => coefficient * 100f;
    internal static float FromDisplay(float value) => value / 100f;
    internal static float Step(float coefficient, int direction) =>
        FromDisplay((float)Math.Round(Math.Max(-DisplayLimit, Math.Min(DisplayLimit,
            ToDisplay(coefficient) + direction)), 4));
    internal static bool Valid(float coefficient) => !float.IsNaN(coefficient) &&
        !float.IsInfinity(coefficient) && Math.Abs(coefficient) <= 1;

    // Stationary ends, signed center displacement; measured in the text's local coordinates.
    internal static float Arc(float x, float left, float right)
    {
        float half = (right - left) * .5f;
        if (!(half > .0001f)) return 0;
        float normalized = (x - (left + half)) / half;
        return Math.Max(0, 1 - normalized * normalized) * half;
    }
}

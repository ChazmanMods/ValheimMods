namespace RunicSigns.Core;
internal static class SignGeometry
{
    internal static float Arc(float x, float left, float right) => Runic.Shared.CaptionCurve.Arc(x, left, right);
}

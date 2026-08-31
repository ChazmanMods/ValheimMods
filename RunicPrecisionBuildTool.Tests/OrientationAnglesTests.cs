using System;
using UnityEngine;

namespace QuietBuildRotation.Tests
{
    internal static class OrientationAnglesTests
    {
        internal static void Register()
        {
            TestRunner.Run("Y-X-Z display decomposition recovers regular compound angles", RegularDecomposition);
            TestRunner.Run("Positive singularity uses stable canonical yaw and zero roll", PositiveSingularity);
            TestRunner.Run("Negative singularity uses stable canonical yaw and zero roll", NegativeSingularity);
            TestRunner.Run("89.95-degree pitch retains projected yaw and roll", ThresholdRetainsProjectedAngles);
            TestRunner.Run("Near-singular q and -q retain identical finite display", EquivalentQuaternionStability);
            TestRunner.Run("Y-X-Z display remains continuous while crossing the positive pole", PoleCrossingContinuity);
            TestRunner.Run("Signed angles normalize to [-180, 180)", SignedNormalization);
            TestRunner.Run("Display formatting removes near-zero noise without false precision", DisplayFormatting);
            TestRunner.Run("Invalid quaternion display fails safely to identity", InvalidQuaternion);
        }

        private static void RegularDecomposition()
        {
            Quaternion q = Compose(67.5f, 30f, -15f);
            OrientationAngles angles = OrientationAngles.FromQuaternion(q);
            TestAssert.Near(30f, angles.Pitch, 0.001f);
            TestAssert.Near(-15f, angles.Roll, 0.001f);
            TestAssert.Near(67.5f, angles.Yaw, 0.001f);
        }

        private static void PositiveSingularity()
        {
            Quaternion q = Compose(30f, 90f, 15f);
            OrientationAngles angles = OrientationAngles.FromQuaternion(q);
            OrientationAngles negated = OrientationAngles.FromQuaternion(
                new Quaternion(-q.x, -q.y, -q.z, -q.w));
            TestAssert.Near(90f, angles.Pitch, 0.001f);
            TestAssert.Near(0f, angles.Roll, 0f);
            TestAssert.Near(15f, angles.Yaw, 0.001f);
            TestAssert.Near(angles.Pitch, negated.Pitch, 0f);
            TestAssert.Near(angles.Roll, negated.Roll, 0f);
            TestAssert.Near(angles.Yaw, negated.Yaw, 0f);
        }

        private static void NegativeSingularity()
        {
            OrientationAngles angles = OrientationAngles.FromQuaternion(Compose(30f, -90f, 15f));
            TestAssert.Near(-90f, angles.Pitch, 0.001f);
            TestAssert.Near(0f, angles.Roll, 0f);
            TestAssert.Near(45f, angles.Yaw, 0.001f);
        }

        private static void EquivalentQuaternionStability()
        {
            Quaternion q = Compose(170f, 89.96f, -25f);
            Quaternion negated = new Quaternion(-q.x, -q.y, -q.z, -q.w);
            OrientationAngles first = OrientationAngles.FromQuaternion(q);
            OrientationAngles second = OrientationAngles.FromQuaternion(negated);

            TestAssert.True(IsFinite(first.Pitch) && IsFinite(first.Roll) && IsFinite(first.Yaw));
            TestAssert.Near(89.96f, first.Pitch, 0.01f);
            TestAssert.Near(first.Pitch, second.Pitch, 0f);
            TestAssert.Near(first.Roll, second.Roll, 0f);
            TestAssert.Near(first.Yaw, second.Yaw, 0f);
        }

        private static void ThresholdRetainsProjectedAngles()
        {
            OrientationAngles angles = OrientationAngles.FromQuaternion(
                Compose(-132f, OrientationAngles.SingularityThresholdDegrees, 41f));
            TestAssert.Near(OrientationAngles.SingularityThresholdDegrees, angles.Pitch, 0.01f);
            TestAssert.Near(41f, angles.Roll, 0.01f);
            TestAssert.Near(-132f, angles.Yaw, 0.01f);
        }

        private static void PoleCrossingContinuity()
        {
            OrientationAngles below = OrientationAngles.FromQuaternion(Compose(30f, 89.999f, 15f));
            OrientationAngles atPole = OrientationAngles.FromQuaternion(Compose(30f, 90f, 15f));
            OrientationAngles above = OrientationAngles.FromQuaternion(Compose(30f, 90.001f, 15f));

            TestAssert.True(below.Pitch < atPole.Pitch);
            TestAssert.True(above.Pitch > atPole.Pitch);
            TestAssert.True(SignedDistance(below.Yaw, atPole.Yaw) < 1f);
            TestAssert.True(SignedDistance(above.Yaw, atPole.Yaw) < 1f);
            TestAssert.True(SignedDistance(below.Roll, atPole.Roll) < 1f);
            TestAssert.True(SignedDistance(above.Roll, atPole.Roll) < 1f);
        }

        private static void SignedNormalization()
        {
            TestAssert.Near(-180f, OrientationAngles.NormalizeSigned(180f), 0f);
            TestAssert.Near(-180f, OrientationAngles.NormalizeSigned(540f), 0f);
            TestAssert.Near(179.5f, OrientationAngles.NormalizeSigned(-180.5f), 0.001f);
            TestAssert.Near(-179.5f, OrientationAngles.NormalizeSigned(180.5f), 0.001f);
        }

        private static void DisplayFormatting()
        {
            TestAssert.Equal("0\u00b0", OrientationAngles.FormatDegrees(0.049f));
            TestAssert.Equal("0\u00b0", OrientationAngles.FormatDegrees(-0.05f));
            TestAssert.Equal("68\u00b0", OrientationAngles.FormatDegrees(68.0001f));
            TestAssert.Equal("67.5\u00b0", OrientationAngles.FormatDegrees(67.5f));
            TestAssert.Equal("67.51\u00b0", OrientationAngles.FormatDegrees(67.512f));
            TestAssert.Equal("-180\u00b0", OrientationAngles.FormatDegrees(179.999f));
            TestAssert.Equal("0.01", OrientationAngles.FormatValue(0.01f));
            TestAssert.Equal("0.05", OrientationAngles.FormatValue(0.05f));
        }

        private static void InvalidQuaternion()
        {
            OrientationAngles angles = OrientationAngles.FromQuaternion(
                new Quaternion(float.NaN, float.PositiveInfinity, 0f, 0f));
            TestAssert.Near(0f, angles.Pitch, 0f);
            TestAssert.Near(0f, angles.Roll, 0f);
            TestAssert.Near(0f, angles.Yaw, 0f);
        }

        private static Quaternion Compose(float yaw, float pitch, float roll) =>
            QuaternionMath.AngleAxis(yaw, 0f, 1f, 0f) *
            QuaternionMath.AngleAxis(pitch, 1f, 0f, 0f) *
            QuaternionMath.AngleAxis(roll, 0f, 0f, 1f);

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        private static float SignedDistance(float left, float right) =>
            Math.Abs(OrientationAngles.NormalizeSigned(left - right));
    }
}

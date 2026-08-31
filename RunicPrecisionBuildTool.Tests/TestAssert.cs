using System;
using UnityEngine;

namespace QuietBuildRotation.Tests
{
    internal static class TestAssert
    {
        internal static void True(bool condition, string message = "Expected true.")
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        internal static void False(bool condition, string message = "Expected false.") =>
            True(!condition, message);

        internal static void Equal<T>(T expected, T actual, string message = null)
        {
            if (!Equals(expected, actual))
                throw new InvalidOperationException(
                    message ?? $"Expected {expected}, got {actual}.");
        }

        internal static void Near(float expected, float actual, float tolerance, string message = null)
        {
            if (float.IsNaN(actual) || Math.Abs(expected - actual) > tolerance)
                throw new InvalidOperationException(
                    message ?? $"Expected {expected} +/- {tolerance}, got {actual}.");
        }

        internal static void VectorNear(Vector3 expected, Vector3 actual, float tolerance, string message = null)
        {
            if (Math.Abs(expected.x - actual.x) > tolerance ||
                Math.Abs(expected.y - actual.y) > tolerance ||
                Math.Abs(expected.z - actual.z) > tolerance)
            {
                throw new InvalidOperationException(
                    message ?? $"Expected ({expected.x}, {expected.y}, {expected.z}), " +
                    $"got ({actual.x}, {actual.y}, {actual.z}).");
            }
        }

        internal static void QuaternionNear(
            Quaternion expected,
            Quaternion actual,
            float toleranceDegrees,
            string message = null)
        {
            float angle = QuaternionMath.AngleDegrees(expected, actual);
            if (float.IsNaN(angle) || angle > toleranceDegrees)
                throw new InvalidOperationException(
                    message ?? $"Quaternion angle {angle} exceeds {toleranceDegrees} degrees.");
        }
    }
}

using System;
using System.Globalization;
using UnityEngine;

namespace QuietBuildRotation
{
    /// <summary>
    /// Canonical Y-X-Z decomposition using the same physical axis names as the controls:
    /// yaw=world Y, pitch=world X, and roll=world Z. The inverse recomposition helper is used
    /// only by explicit component-match commands; ordinary incremental placement remains pure
    /// quaternion math.
    /// </summary>
    internal readonly struct OrientationAngles
    {
        internal const float SingularityThresholdDegrees = 89.95f;
        internal const float DisplayZeroThresholdDegrees = 0.05f;

        private const float IntegralDisplayTolerance = 0.005f;
        private const double ProjectedForwardEpsilonSquared = 0.0000000001d;
        private const double SingularityThresholdProjection = 0.0008726645152351496d;
        private const double MatchSingularityProjectionSquared = 0.000000000001d;

        private OrientationAngles(float pitch, float roll, float yaw)
        {
            Pitch = pitch;
            Roll = roll;
            Yaw = yaw;
        }

        internal float Pitch { get; }

        internal float Roll { get; }

        internal float Yaw { get; }

        /// <summary>
        /// Decomposes R = Ry(yaw) * Rx(pitch) * Rz(roll). At the specified gimbal threshold,
        /// projected-forward yaw and basis-derived roll select the stable Euler branch. Through
        /// the final 0.05 degrees, the otherwise ambiguous roll is smoothly canonicalized toward
        /// zero while preserving the coupled pole heading. This prevents the 180-degree branch
        /// flip across a pole and is deterministic for q and -q.
        /// </summary>
        internal static OrientationAngles FromQuaternion(Quaternion rotation)
        {
            if (!QuaternionMath.TryNormalize(rotation, out Quaternion normalized))
                return default;

            return FromNormalizedQuaternion(normalized);
        }

        /// <summary>
        /// Lossless-ish Y-X-Z decomposition for explicit component matching. Unlike the display
        /// path, this applies neither the 0.05-degree deadband nor the near-pole smoothing, so an
        /// untouched sub-display component survives recomposition. At the exact gimbal pole,
        /// roll is deterministically canonicalized to zero and the observable coupled heading is
        /// retained as yaw.
        /// </summary>
        internal static bool TryDecomposeForMatch(
            Quaternion rotation,
            out OrientationAngles angles)
        {
            if (!QuaternionMath.TryNormalize(rotation, out Quaternion q))
            {
                angles = default;
                return false;
            }

            double x = q.x;
            double y = q.y;
            double z = q.z;
            double w = q.w;
            double m00 = 1d - 2d * (y * y + z * z);
            double m02 = 2d * (x * z + y * w);
            double m10 = 2d * (x * y + z * w);
            double m11 = 1d - 2d * (x * x + z * z);
            double m12 = 2d * (y * z - x * w);
            double m20 = 2d * (x * z - y * w);
            double m22 = 1d - 2d * (x * x + y * y);

            double sinePitch = Clamp(-m12, -1d, 1d);
            double projectedForwardSquared = m02 * m02 + m22 * m22;
            double pitchDegrees;
            double rollDegrees;
            double yawDegrees;
            if (projectedForwardSquared <= MatchSingularityProjectionSquared)
            {
                pitchDegrees = sinePitch < 0d ? -90d : 90d;
                yawDegrees = Math.Atan2(-m20, m00) * (180d / Math.PI);
                rollDegrees = 0d;
            }
            else
            {
                pitchDegrees = Math.Asin(sinePitch) * (180d / Math.PI);
                yawDegrees = Math.Atan2(m02, m22) * (180d / Math.PI);
                rollDegrees = Math.Atan2(m10, m11) * (180d / Math.PI);
            }

            angles = new OrientationAngles(
                NormalizeSignedForMatch((float)pitchDegrees),
                NormalizeSignedForMatch((float)rollDegrees),
                NormalizeSignedForMatch((float)yawDegrees));
            return true;
        }

        /// <summary>
        /// Reconstructs R = Ry(yaw) * Rx(pitch) * Rz(roll), the exact inverse convention used
        /// by <see cref="FromQuaternion"/>. This is validating so a partial match
        /// can fail without mutating the current placement when any component is non-finite.
        /// </summary>
        internal static bool TryRecompose(
            float pitch,
            float roll,
            float yaw,
            out Quaternion rotation)
        {
            if (!QuaternionMath.IsFinite(pitch) ||
                !QuaternionMath.IsFinite(roll) ||
                !QuaternionMath.IsFinite(yaw))
            {
                rotation = Quaternion.identity;
                return false;
            }

            Quaternion yawRotation = QuaternionMath.AngleAxis(yaw, 0f, 1f, 0f);
            Quaternion pitchRotation = QuaternionMath.AngleAxis(pitch, 1f, 0f, 0f);
            Quaternion rollRotation = QuaternionMath.AngleAxis(roll, 0f, 0f, 1f);
            return QuaternionMath.TryNormalize(
                yawRotation * pitchRotation * rollRotation,
                out rotation);
        }

        private static OrientationAngles FromNormalizedQuaternion(Quaternion q)
        {
            double x = q.x;
            double y = q.y;
            double z = q.z;
            double w = q.w;

            // Selected matrix terms for a normalized quaternion.
            double m00 = 1d - 2d * (y * y + z * z);
            double m02 = 2d * (x * z + y * w);
            double m10 = 2d * (x * y + z * w);
            double m11 = 1d - 2d * (x * x + z * z);
            double m12 = 2d * (y * z - x * w);
            double m20 = 2d * (x * z - y * w);
            double m22 = 1d - 2d * (x * x + y * y);

            double sinePitch = Clamp(-m12, -1d, 1d);
            double pitchDegrees = Math.Asin(sinePitch) * (180d / Math.PI);
            double projectedForwardSquared = m02 * m02 + m22 * m22;
            double yawDegrees;
            double rollDegrees;

            if (Math.Abs(pitchDegrees) >= SingularityThresholdDegrees &&
                projectedForwardSquared <= ProjectedForwardEpsilonSquared)
            {
                // At +90 only yaw-roll is observable; at -90 only yaw+roll is observable.
                // yaw=atan2(-m20,m00), roll=0 is the same stable canonical choice for both.
                pitchDegrees = sinePitch < 0d ? -90d : 90d;
                yawDegrees = Math.Atan2(-m20, m00) * (180d / Math.PI);
                rollDegrees = 0d;
            }
            else if (Math.Abs(pitchDegrees) >= SingularityThresholdDegrees)
            {
                double projectedYaw = Math.Atan2(m02, m22) * (180d / Math.PI);
                double projectedRoll = Math.Atan2(m10, m11) * (180d / Math.PI);

                // Across a pole the principal Y-X-Z solution flips yaw and roll by 180 degrees.
                // Select the equivalent branch whose roll is canonical in [-90, 90], allowing
                // pitch to pass smoothly beyond +/-90 instead of flipping both other axes.
                if (projectedRoll > 90d || projectedRoll < -90d)
                {
                    projectedYaw = NormalizeSignedDouble(projectedYaw + 180d);
                    projectedRoll = NormalizeSignedDouble(projectedRoll + 180d);
                    pitchDegrees = pitchDegrees >= 0d
                        ? 180d - pitchDegrees
                        : -180d - pitchDegrees;
                }

                double blend = Math.Sqrt(projectedForwardSquared) /
                               SingularityThresholdProjection;
                blend = Clamp(blend, 0d, 1d);
                rollDegrees = projectedRoll * blend;

                // At +90 the observable heading is yaw-roll; at -90 it is yaw+roll.
                // Preserve that heading as roll converges to its zero canonical value.
                double coupledYaw = Math.Atan2(-m20, m00) * (180d / Math.PI);
                yawDegrees = pitchDegrees >= 0d
                    ? coupledYaw + rollDegrees
                    : coupledYaw - rollDegrees;
            }
            else
            {
                // forward=(m02,m12,m22), right.y=m10, up.y=m11
                yawDegrees = Math.Atan2(m02, m22) * (180d / Math.PI);
                rollDegrees = Math.Atan2(m10, m11) * (180d / Math.PI);
            }

            return new OrientationAngles(
                NormalizeSigned((float)pitchDegrees),
                NormalizeSigned((float)rollDegrees),
                NormalizeSigned((float)yawDegrees));
        }

        internal static float NormalizeSigned(float degrees)
        {
            if (!QuaternionMath.IsFinite(degrees))
                return 0f;

            degrees %= 360f;
            if (degrees < -180f)
                degrees += 360f;
            else if (degrees >= 180f)
                degrees -= 360f;

            return Math.Abs(degrees) <= DisplayZeroThresholdDegrees ? 0f : degrees;
        }

        internal static string FormatDegrees(float degrees)
        {
            degrees = NormalizeSigned(degrees);
            float nearestInteger = (float)Math.Round(degrees);
            if (Math.Abs(degrees - nearestInteger) < IntegralDisplayTolerance)
                return NormalizeSigned(nearestInteger).ToString("0", CultureInfo.InvariantCulture) +
                       "\u00b0";

            return degrees.ToString("0.##", CultureInfo.InvariantCulture) + "\u00b0";
        }

        /// <summary>
        /// Formats a configured step magnitude. Unlike an orientation angle, a 0.01-degree or
        /// metre step must not be normalized or suppressed by the 0.05-degree display deadband.
        /// </summary>
        internal static string FormatValue(float value)
        {
            if (!QuaternionMath.IsFinite(value))
                return "0";

            float nearestInteger = (float)Math.Round(value);
            if (Math.Abs(value - nearestInteger) < IntegralDisplayTolerance)
                return nearestInteger.ToString("0", CultureInfo.InvariantCulture);

            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static double Clamp(double value, double minimum, double maximum)
        {
            if (value < minimum)
                return minimum;
            return value > maximum ? maximum : value;
        }

        private static double NormalizeSignedDouble(double degrees)
        {
            degrees %= 360d;
            if (degrees < -180d)
                degrees += 360d;
            else if (degrees >= 180d)
                degrees -= 360d;
            return degrees;
        }

        private static float NormalizeSignedForMatch(float degrees)
        {
            if (!QuaternionMath.IsFinite(degrees))
                return 0f;

            degrees %= 360f;
            if (degrees < -180f)
                degrees += 360f;
            else if (degrees >= 180f)
                degrees -= 360f;
            return degrees;
        }
    }
}

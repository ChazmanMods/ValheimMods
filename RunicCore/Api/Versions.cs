using System;
using System.Collections.Generic;
using System.Globalization;

namespace Runic.Foundation.Core
{
    /// <summary>An immutable Semantic Versioning 2.0.0 value.</summary>
    public sealed class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
    {
        public SemanticVersion(
            int major,
            int minor,
            int patch,
            string preRelease = null,
            string buildMetadata = null)
        {
            if (major < 0) throw new ArgumentOutOfRangeException(nameof(major));
            if (minor < 0) throw new ArgumentOutOfRangeException(nameof(minor));
            if (patch < 0) throw new ArgumentOutOfRangeException(nameof(patch));
            ValidateIdentifiers(preRelease, false, nameof(preRelease));
            ValidateIdentifiers(buildMetadata, true, nameof(buildMetadata));

            Major = major;
            Minor = minor;
            Patch = patch;
            PreRelease = EmptyToNull(preRelease);
            BuildMetadata = EmptyToNull(buildMetadata);
        }

        public int Major { get; }
        public int Minor { get; }
        public int Patch { get; }
        public string PreRelease { get; }
        public string BuildMetadata { get; }
        public bool IsPrerelease => PreRelease != null;

        public static SemanticVersion Parse(string value)
        {
            if (!TryParse(value, out SemanticVersion version))
                throw new FormatException("The value is not a valid Semantic Versioning 2.0.0 version.");
            return version;
        }

        public static bool TryParse(string value, out SemanticVersion version)
        {
            version = null;
            if (string.IsNullOrEmpty(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
                return false;

            string coreAndPreRelease = value;
            string build = null;
            int plus = value.IndexOf('+');
            if (plus >= 0)
            {
                if (plus == value.Length - 1 || value.IndexOf('+', plus + 1) >= 0)
                    return false;
                build = value.Substring(plus + 1);
                coreAndPreRelease = value.Substring(0, plus);
            }

            string core = coreAndPreRelease;
            string preRelease = null;
            int dash = coreAndPreRelease.IndexOf('-');
            if (dash >= 0)
            {
                if (dash == coreAndPreRelease.Length - 1)
                    return false;
                preRelease = coreAndPreRelease.Substring(dash + 1);
                core = coreAndPreRelease.Substring(0, dash);
            }

            string[] components = core.Split('.');
            if (components.Length != 3 ||
                !TryParseCoreNumber(components[0], out int major) ||
                !TryParseCoreNumber(components[1], out int minor) ||
                !TryParseCoreNumber(components[2], out int patch))
            {
                return false;
            }

            try
            {
                version = new SemanticVersion(major, minor, patch, preRelease, build);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        public int CompareTo(SemanticVersion other)
        {
            if (ReferenceEquals(other, null)) return 1;

            int comparison = Major.CompareTo(other.Major);
            if (comparison != 0) return comparison;
            comparison = Minor.CompareTo(other.Minor);
            if (comparison != 0) return comparison;
            comparison = Patch.CompareTo(other.Patch);
            if (comparison != 0) return comparison;

            if (PreRelease == null) return other.PreRelease == null ? 0 : 1;
            if (other.PreRelease == null) return -1;

            string[] left = PreRelease.Split('.');
            string[] right = other.PreRelease.Split('.');
            int count = Math.Min(left.Length, right.Length);
            for (int index = 0; index < count; index++)
            {
                comparison = ComparePreReleaseIdentifier(left[index], right[index]);
                if (comparison != 0) return comparison;
            }

            return left.Length.CompareTo(right.Length);
        }

        public bool Equals(SemanticVersion other) =>
            !ReferenceEquals(other, null) &&
            Major == other.Major && Minor == other.Minor && Patch == other.Patch &&
            string.Equals(PreRelease, other.PreRelease, StringComparison.Ordinal) &&
            string.Equals(BuildMetadata, other.BuildMetadata, StringComparison.Ordinal);

        public override bool Equals(object obj) => Equals(obj as SemanticVersion);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Major;
                hash = hash * 397 ^ Minor;
                hash = hash * 397 ^ Patch;
                hash = hash * 397 ^ (PreRelease == null ? 0 : StringComparer.Ordinal.GetHashCode(PreRelease));
                hash = hash * 397 ^ (BuildMetadata == null ? 0 : StringComparer.Ordinal.GetHashCode(BuildMetadata));
                return hash;
            }
        }

        public override string ToString()
        {
            string value = Major.ToString(CultureInfo.InvariantCulture) + "." +
                           Minor.ToString(CultureInfo.InvariantCulture) + "." +
                           Patch.ToString(CultureInfo.InvariantCulture);
            if (PreRelease != null) value += "-" + PreRelease;
            if (BuildMetadata != null) value += "+" + BuildMetadata;
            return value;
        }

        public static bool operator <(SemanticVersion left, SemanticVersion right) =>
            Compare(left, right) < 0;

        public static bool operator >(SemanticVersion left, SemanticVersion right) =>
            Compare(left, right) > 0;

        public static bool operator <=(SemanticVersion left, SemanticVersion right) =>
            Compare(left, right) <= 0;

        public static bool operator >=(SemanticVersion left, SemanticVersion right) =>
            Compare(left, right) >= 0;

        private static int Compare(SemanticVersion left, SemanticVersion right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (ReferenceEquals(left, null)) return -1;
            return left.CompareTo(right);
        }

        private static bool TryParseCoreNumber(string value, out int number)
        {
            number = 0;
            if (string.IsNullOrEmpty(value) || value.Length > 1 && value[0] == '0')
                return false;
            for (int index = 0; index < value.Length; index++)
                if (value[index] < '0' || value[index] > '9') return false;
            return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number);
        }

        private static void ValidateIdentifiers(string value, bool allowLeadingZero, string parameterName)
        {
            if (string.IsNullOrEmpty(value)) return;
            string[] identifiers = value.Split('.');
            foreach (string identifier in identifiers)
            {
                if (identifier.Length == 0)
                    throw new ArgumentException("Version identifiers cannot be empty.", parameterName);

                bool numeric = true;
                foreach (char character in identifier)
                {
                    bool valid = character >= 'a' && character <= 'z' ||
                                 character >= 'A' && character <= 'Z' ||
                                 character >= '0' && character <= '9' ||
                                 character == '-';
                    if (!valid)
                        throw new ArgumentException("Version identifiers contain an invalid character.", parameterName);
                    if (character < '0' || character > '9') numeric = false;
                }

                if (!allowLeadingZero && numeric && identifier.Length > 1 && identifier[0] == '0')
                    throw new ArgumentException("Numeric prerelease identifiers cannot contain leading zeroes.", parameterName);
            }
        }

        private static string EmptyToNull(string value) => string.IsNullOrEmpty(value) ? null : value;

        private static int ComparePreReleaseIdentifier(string left, string right)
        {
            bool leftNumeric = IsNumeric(left);
            bool rightNumeric = IsNumeric(right);
            if (leftNumeric && !rightNumeric) return -1;
            if (!leftNumeric && rightNumeric) return 1;
            if (!leftNumeric) return StringComparer.Ordinal.Compare(left, right);

            int lengthComparison = left.Length.CompareTo(right.Length);
            return lengthComparison != 0 ? lengthComparison : StringComparer.Ordinal.Compare(left, right);
        }

        private static bool IsNumeric(string value)
        {
            foreach (char character in value)
                if (character < '0' || character > '9') return false;
            return value.Length > 0;
        }
    }

    /// <summary>
    /// Version of a wire or integration contract. Matching major versions are compatible;
    /// minor versions are additive revisions within that contract generation.
    /// </summary>
    public readonly struct ProtocolVersion : IComparable<ProtocolVersion>, IEquatable<ProtocolVersion>
    {
        public ProtocolVersion(int major, int minor)
        {
            if (major < 0) throw new ArgumentOutOfRangeException(nameof(major));
            if (minor < 0) throw new ArgumentOutOfRangeException(nameof(minor));
            Major = major;
            Minor = minor;
        }

        public int Major { get; }
        public int Minor { get; }

        public bool IsCompatibleWith(ProtocolVersion other) => Major == other.Major;

        public int CompareTo(ProtocolVersion other)
        {
            int comparison = Major.CompareTo(other.Major);
            return comparison != 0 ? comparison : Minor.CompareTo(other.Minor);
        }

        public bool Equals(ProtocolVersion other) => Major == other.Major && Minor == other.Minor;
        public override bool Equals(object obj) => obj is ProtocolVersion other && Equals(other);
        public override int GetHashCode() => unchecked(Major * 397 ^ Minor);
        public override string ToString() =>
            Major.ToString(CultureInfo.InvariantCulture) + "." + Minor.ToString(CultureInfo.InvariantCulture);

        public static ProtocolVersion Parse(string value)
        {
            if (!TryParse(value, out ProtocolVersion version))
                throw new FormatException("Protocol versions must use the form major.minor.");
            return version;
        }

        public static bool TryParse(string value, out ProtocolVersion version)
        {
            version = default;
            if (string.IsNullOrEmpty(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
                return false;
            string[] components = value.Split('.');
            if (components.Length != 2 ||
                !int.TryParse(components[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major) ||
                !int.TryParse(components[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minor) ||
                major < 0 || minor < 0)
            {
                return false;
            }
            version = new ProtocolVersion(major, minor);
            return true;
        }
    }

    public static class RunicCoreMetadata
    {
        public const string SemanticVersionText = "1.1.0";
        public const string ProtocolVersionText = "1.0";
        public static readonly SemanticVersion SemanticVersion = SemanticVersion.Parse(SemanticVersionText);
        public static readonly ProtocolVersion ProtocolVersion = ProtocolVersion.Parse(ProtocolVersionText);
    }
}

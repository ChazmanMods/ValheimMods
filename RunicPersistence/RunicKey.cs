using System;
using Runic.Foundation.Core;

namespace Runic.Foundation.Persistence
{
    public readonly struct RunicKey : IEquatable<RunicKey>
    {
        private RunicKey(string value)
        {
            Value = value;
        }

        public string Value { get; }

        public static RunicKey Create(string moduleId, string localName)
        {
            string module = RequireModuleId(moduleId, nameof(moduleId));
            string name = RunicIdentifier.Require(localName, nameof(localName));
            return new RunicKey(module + "." + name);
        }

        public bool Equals(RunicKey other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is RunicKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        }

        public override string ToString()
        {
            return Value ?? string.Empty;
        }

        public static bool operator ==(RunicKey left, RunicKey right) => left.Equals(right);

        public static bool operator !=(RunicKey left, RunicKey right) => !left.Equals(right);

        internal static string RequireModuleId(string value, string parameterName)
        {
            string moduleId = RunicIdentifier.Require(value, parameterName);
            if (!moduleId.StartsWith("runic.", StringComparison.Ordinal) || moduleId.Length == 6)
                throw new ArgumentException(
                    "Persistence module IDs must be canonical Runic Core IDs beginning with 'runic.'.",
                    parameterName);
            if (moduleId.IndexOf('.', "runic.".Length) >= 0)
                throw new ArgumentException(
                    "Persistence module IDs must use exactly two segments: 'runic.<module>'.",
                    parameterName);
            return moduleId;
        }
    }
}

using System;
using System.Collections.Generic;

namespace Runic.Foundation.Core
{
    /// <summary>Immutable discovery metadata published by one installed Runic module.</summary>
    public sealed class ModuleDescriptor
    {
        private readonly IReadOnlyList<string> _ownedCapabilities;
        private readonly IReadOnlyList<string> _extensionPoints;

        public ModuleDescriptor(
            string moduleId,
            string displayName,
            SemanticVersion semanticVersion,
            ProtocolVersion protocolVersion,
            IEnumerable<string> ownedCapabilities = null,
            IEnumerable<string> extensionPoints = null)
        {
            ModuleId = RunicIdentifier.Require(moduleId, nameof(moduleId));
            if (string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException("A module display name is required.", nameof(displayName));
            SemanticVersion = semanticVersion ?? throw new ArgumentNullException(nameof(semanticVersion));
            ProtocolVersion = protocolVersion;
            DisplayName = displayName.Trim();
            _ownedCapabilities = CopyIdentifiers(ownedCapabilities, nameof(ownedCapabilities));
            _extensionPoints = CopyIdentifiers(extensionPoints, nameof(extensionPoints));
        }

        public ModuleDescriptor(
            string moduleId,
            string displayName,
            string semanticVersion,
            string protocolVersion,
            IEnumerable<string> ownedCapabilities = null,
            IEnumerable<string> extensionPoints = null)
            : this(
                moduleId,
                displayName,
                SemanticVersion.Parse(semanticVersion),
                ProtocolVersion.Parse(protocolVersion),
                ownedCapabilities,
                extensionPoints)
        {
        }

        public string ModuleId { get; }
        public string Id => ModuleId;
        public string DisplayName { get; }
        public SemanticVersion SemanticVersion { get; }
        public SemanticVersion Version => SemanticVersion;
        public ProtocolVersion ProtocolVersion { get; }
        public IReadOnlyList<string> OwnedCapabilities => _ownedCapabilities;
        public IReadOnlyList<string> Capabilities => _ownedCapabilities;
        public IReadOnlyList<string> ExtensionPoints => _extensionPoints;

        public bool HasCapability(string capabilityId)
        {
            if (capabilityId == null) return false;
            for (int index = 0; index < _ownedCapabilities.Count; index++)
                if (string.Equals(_ownedCapabilities[index], capabilityId, StringComparison.Ordinal))
                    return true;
            return false;
        }

        private static IReadOnlyList<string> CopyIdentifiers(
            IEnumerable<string> values,
            string parameterName)
        {
            if (values == null) return Array.Empty<string>();

            SortedSet<string> unique = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string value in values)
            {
                RunicIdentifier.Require(value, parameterName);
                if (!unique.Add(value))
                    throw new ArgumentException("Duplicate Runic identifier: " + value, parameterName);
            }

            string[] copy = new string[unique.Count];
            unique.CopyTo(copy);
            return Array.AsReadOnly(copy);
        }
    }
}

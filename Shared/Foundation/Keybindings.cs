using System;
using System.Collections.Generic;
using System.Text;

namespace Runic.Foundation.Core
{
    /// <summary>
    /// Device-qualified input chord whose modifiers are normalized and order-independent.
    /// The type does not depend on UnityEngine so it can be used by tools and tests.
    /// </summary>
    public sealed class InputChord : IEquatable<InputChord>
    {
        private readonly IReadOnlyList<string> _modifiers;

        public InputChord(
            string deviceId,
            string primaryControl,
            IEnumerable<string> modifiers = null)
        {
            DeviceId = RunicIdentifier.Require(deviceId, nameof(deviceId));
            PrimaryControl = RequireControl(primaryControl, nameof(primaryControl));
            string primaryCanonical = CanonicalizeControl(PrimaryControl);

            SortedDictionary<string, string> unique =
                new SortedDictionary<string, string>(StringComparer.Ordinal);
            if (modifiers != null)
            {
                foreach (string modifierValue in modifiers)
                {
                    string modifier = RequireControl(modifierValue, nameof(modifiers));
                    string canonical = CanonicalizeControl(modifier);
                    if (string.Equals(canonical, primaryCanonical, StringComparison.Ordinal))
                    {
                        throw new ArgumentException(
                            "The primary control cannot also be a modifier.",
                            nameof(modifiers));
                    }
                    if (unique.ContainsKey(canonical))
                        throw new ArgumentException("Duplicate input modifier: " + modifier, nameof(modifiers));
                    unique.Add(canonical, modifier);
                }
            }

            string[] copy = new string[unique.Count];
            int index = 0;
            foreach (string modifier in unique.Values) copy[index++] = modifier;
            _modifiers = Array.AsReadOnly(copy);

            StringBuilder canonicalId = new StringBuilder(DeviceId).Append(':');
            foreach (string canonical in unique.Keys) canonicalId.Append(canonical).Append('+');
            canonicalId.Append(primaryCanonical);
            CanonicalId = canonicalId.ToString();
        }

        public string DeviceId { get; }
        public string PrimaryControl { get; }
        public IReadOnlyList<string> Modifiers => _modifiers;
        public string CanonicalId { get; }

        public bool Equals(InputChord other) =>
            !ReferenceEquals(other, null) &&
            string.Equals(CanonicalId, other.CanonicalId, StringComparison.Ordinal);

        public override bool Equals(object obj) => Equals(obj as InputChord);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(CanonicalId);

        public override string ToString()
        {
            StringBuilder display = new StringBuilder();
            for (int index = 0; index < _modifiers.Count; index++)
            {
                if (index > 0) display.Append(" + ");
                display.Append(_modifiers[index]);
            }
            if (_modifiers.Count > 0) display.Append(" + ");
            return display.Append(PrimaryControl).Append(" [").Append(DeviceId).Append(']').ToString();
        }

        private static string RequireControl(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("An input control name is required.", parameterName);
            string trimmed = value.Trim();
            if (trimmed.Length > 64)
                throw new ArgumentException("Input control names cannot exceed 64 characters.", parameterName);
            foreach (char character in trimmed)
            {
                if (char.IsControl(character) || character == ':' || character == '+' || character == '|')
                    throw new ArgumentException("The input control name contains a reserved character.", parameterName);
            }
            if (CanonicalizeControl(trimmed).Length == 0)
                throw new ArgumentException("The input control name has no canonical characters.", parameterName);
            return trimmed;
        }

        private static string CanonicalizeControl(string value)
        {
            StringBuilder canonical = new StringBuilder(value.Length);
            foreach (char character in value)
            {
                if (char.IsWhiteSpace(character) || character == '_' || character == '-') continue;
                canonical.Append(char.ToLowerInvariant(character));
            }
            return canonical.ToString();
        }
    }

    public sealed class KeybindingDescriptor
    {
        public KeybindingDescriptor(
            string moduleId,
            string bindingId,
            string displayName,
            InputChord chord,
            string context = "gameplay")
        {
            ModuleId = RunicIdentifier.Require(moduleId, nameof(moduleId));
            BindingId = RunicIdentifier.Require(bindingId, nameof(bindingId));
            Context = RunicIdentifier.Require(context, nameof(context));
            if (string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException("A keybinding display name is required.", nameof(displayName));
            Chord = chord ?? throw new ArgumentNullException(nameof(chord));
            DisplayName = displayName.Trim();
        }

        public string ModuleId { get; }
        public string BindingId { get; }
        public string QualifiedId => ModuleId + "/" + BindingId;
        public string DisplayName { get; }
        public InputChord Chord { get; }
        public string Context { get; }
    }

    public sealed class KeybindingConflict
    {
        internal KeybindingConflict(InputChord chord, IReadOnlyList<KeybindingDescriptor> bindings)
        {
            Chord = chord;
            Bindings = bindings;
        }

        public InputChord Chord { get; }
        public IReadOnlyList<KeybindingDescriptor> Bindings { get; }
    }

    public enum KeybindingChangeKind
    {
        Registered = 0,
        Unregistered = 1
    }

    public sealed class KeybindingsChangedEventArgs : EventArgs
    {
        internal KeybindingsChangedEventArgs(
            KeybindingChangeKind kind,
            KeybindingDescriptor binding,
            KeybindingConflict conflict)
        {
            Kind = kind;
            Binding = binding;
            Conflict = conflict;
        }

        public KeybindingChangeKind Kind { get; }
        public KeybindingDescriptor Binding { get; }

        /// <summary>The current exact-chord conflict, or null when fewer than two bindings remain.</summary>
        public KeybindingConflict Conflict { get; }
    }

    /// <summary>
    /// Thread-safe exact-chord registry for Runic and known Valheim bindings. Context is retained
    /// for actionable reporting; exact chords are reported even across different contexts because
    /// contexts can overlap at runtime.
    /// </summary>
    public sealed class KeybindingConflictRegistry
    {
        private readonly object _sync = new object();
        private readonly Dictionary<string, BindingEntry> _byQualifiedId =
            new Dictionary<string, BindingEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<BindingEntry>> _byChord =
            new Dictionary<string, List<BindingEntry>>(StringComparer.Ordinal);
        private long _nextToken;

        public event EventHandler<KeybindingsChangedEventArgs> Changed;

        public KeybindingRegistration Register(KeybindingDescriptor binding)
        {
            if (binding == null) throw new ArgumentNullException(nameof(binding));
            KeybindingConflict conflict;
            long token;
            lock (_sync)
            {
                if (_byQualifiedId.ContainsKey(binding.QualifiedId))
                {
                    throw new InvalidOperationException(
                        "A keybinding is already registered as '" + binding.QualifiedId + "'.");
                }
                if (_nextToken == long.MaxValue)
                    throw new InvalidOperationException("The keybinding registration token space is exhausted.");
                token = ++_nextToken;
                BindingEntry entry = new BindingEntry(binding, token);
                _byQualifiedId.Add(binding.QualifiedId, entry);
                if (!_byChord.TryGetValue(binding.Chord.CanonicalId, out List<BindingEntry> entries))
                {
                    entries = new List<BindingEntry>();
                    _byChord.Add(binding.Chord.CanonicalId, entries);
                }
                entries.Add(entry);
                conflict = CreateConflictLocked(binding.Chord.CanonicalId);
            }

            RaiseChanged(new KeybindingsChangedEventArgs(
                KeybindingChangeKind.Registered,
                binding,
                conflict));
            return new KeybindingRegistration(this, binding, token);
        }

        public bool Unregister(string moduleId, string bindingId)
        {
            RunicIdentifier.Require(moduleId, nameof(moduleId));
            RunicIdentifier.Require(bindingId, nameof(bindingId));
            return Unregister(moduleId + "/" + bindingId, (long?)null);
        }

        public bool TryGetBinding(
            string moduleId,
            string bindingId,
            out KeybindingDescriptor binding)
        {
            binding = null;
            if (!RunicIdentifier.IsValid(moduleId) || !RunicIdentifier.IsValid(bindingId))
                return false;
            lock (_sync)
            {
                if (!_byQualifiedId.TryGetValue(moduleId + "/" + bindingId, out BindingEntry entry))
                    return false;
                binding = entry.Binding;
                return true;
            }
        }

        public IReadOnlyList<KeybindingDescriptor> GetBindings()
        {
            lock (_sync)
            {
                List<KeybindingDescriptor> bindings = new List<KeybindingDescriptor>(_byQualifiedId.Count);
                foreach (BindingEntry entry in _byQualifiedId.Values) bindings.Add(entry.Binding);
                bindings.Sort((left, right) =>
                    StringComparer.Ordinal.Compare(left.QualifiedId, right.QualifiedId));
                return bindings.AsReadOnly();
            }
        }

        public IReadOnlyList<KeybindingConflict> GetConflicts()
        {
            lock (_sync)
            {
                List<KeybindingConflict> conflicts = new List<KeybindingConflict>();
                foreach (string chordId in _byChord.Keys)
                {
                    KeybindingConflict conflict = CreateConflictLocked(chordId);
                    if (conflict != null) conflicts.Add(conflict);
                }
                conflicts.Sort((left, right) =>
                    StringComparer.Ordinal.Compare(left.Chord.CanonicalId, right.Chord.CanonicalId));
                return conflicts.AsReadOnly();
            }
        }

        public IReadOnlyList<KeybindingConflict> GetConflictsFor(string moduleId)
        {
            RunicIdentifier.Require(moduleId, nameof(moduleId));
            IReadOnlyList<KeybindingConflict> all = GetConflicts();
            List<KeybindingConflict> matches = new List<KeybindingConflict>();
            foreach (KeybindingConflict conflict in all)
            {
                foreach (KeybindingDescriptor binding in conflict.Bindings)
                {
                    if (!string.Equals(binding.ModuleId, moduleId, StringComparison.Ordinal)) continue;
                    matches.Add(conflict);
                    break;
                }
            }
            return matches.AsReadOnly();
        }

        internal bool IsActive(string qualifiedId, long token)
        {
            lock (_sync)
                return _byQualifiedId.TryGetValue(qualifiedId, out BindingEntry entry) && entry.Token == token;
        }

        internal bool Unregister(string qualifiedId, long? requiredToken)
        {
            KeybindingDescriptor binding;
            KeybindingConflict conflict;
            lock (_sync)
            {
                if (!_byQualifiedId.TryGetValue(qualifiedId, out BindingEntry entry) ||
                    requiredToken.HasValue && entry.Token != requiredToken.Value)
                {
                    return false;
                }
                binding = entry.Binding;
                _byQualifiedId.Remove(qualifiedId);
                List<BindingEntry> chordEntries = _byChord[binding.Chord.CanonicalId];
                chordEntries.RemoveAll(candidate => candidate.Token == entry.Token);
                if (chordEntries.Count == 0) _byChord.Remove(binding.Chord.CanonicalId);
                conflict = CreateConflictLocked(binding.Chord.CanonicalId);
            }

            RaiseChanged(new KeybindingsChangedEventArgs(
                KeybindingChangeKind.Unregistered,
                binding,
                conflict));
            return true;
        }

        private KeybindingConflict CreateConflictLocked(string chordId)
        {
            if (!_byChord.TryGetValue(chordId, out List<BindingEntry> entries) || entries.Count < 2)
                return null;
            List<KeybindingDescriptor> bindings = new List<KeybindingDescriptor>(entries.Count);
            foreach (BindingEntry entry in entries) bindings.Add(entry.Binding);
            bindings.Sort((left, right) =>
                StringComparer.Ordinal.Compare(left.QualifiedId, right.QualifiedId));
            return new KeybindingConflict(bindings[0].Chord, bindings.AsReadOnly());
        }

        private void RaiseChanged(KeybindingsChangedEventArgs arguments)
        {
            EventHandler<KeybindingsChangedEventArgs> handlers = Changed;
            if (handlers == null) return;
            foreach (EventHandler<KeybindingsChangedEventArgs> handler in handlers.GetInvocationList())
            {
                try { handler(this, arguments); }
                catch (Exception) { }
            }
        }

        private sealed class BindingEntry
        {
            internal BindingEntry(KeybindingDescriptor binding, long token)
            {
                Binding = binding;
                Token = token;
            }

            internal KeybindingDescriptor Binding { get; }
            internal long Token { get; }
        }
    }

    public sealed class KeybindingRegistration : IDisposable
    {
        private readonly KeybindingConflictRegistry _registry;
        private readonly long _token;

        internal KeybindingRegistration(
            KeybindingConflictRegistry registry,
            KeybindingDescriptor binding,
            long token)
        {
            _registry = registry;
            Binding = binding;
            _token = token;
        }

        public KeybindingDescriptor Binding { get; }
        public bool IsActive => _registry.IsActive(Binding.QualifiedId, _token);
        public void Dispose() => _registry.Unregister(Binding.QualifiedId, _token);
    }
}

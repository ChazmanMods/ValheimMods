using System;
using System.Collections.Generic;

namespace RunicSentinel.Core
{
    // Authentication evidence is process-local and tied to an exact socket lifetime.
    // Neither a ready peer nor an admin-list entry is authentication evidence.
    internal sealed class SteamSessionEvidence
    {
        private const int MaximumSessions = 64;
        private readonly Dictionary<ulong, Entry> _entries = new Dictionary<ulong, Entry>();

        internal sealed class Entry
        {
            internal ulong Subject;
            internal object Connection;
            internal uint Handle;
            internal bool Accepted, Validated, Denied;
        }

        internal Entry Begin(ulong subject, object connection, uint handle)
        {
            if (subject == 0 || connection == null || handle == 0) return null;
            if (_entries.TryGetValue(subject, out Entry existing))
            {
                // Concurrent/repeated authentication must not inherit an older verdict.
                existing.Denied = true;
                return null;
            }
            if (_entries.Count >= MaximumSessions) return null;
            var entry = new Entry { Subject = subject, Connection = connection, Handle = handle };
            _entries.Add(subject, entry);
            return entry;
        }

        internal void Complete(Entry entry, bool accepted)
        {
            if (entry == null || !_entries.TryGetValue(entry.Subject, out Entry current) ||
                !ReferenceEquals(entry, current)) return;
            if (!accepted) current.Denied = true;
            else current.Accepted = true;
        }

        internal void Validate(ulong subject, bool valid)
        {
            if (!_entries.TryGetValue(subject, out Entry entry)) return;
            if (!valid) entry.Denied = true;
            else if (!entry.Denied) entry.Validated = true;
        }

        internal bool IsRejected(ulong subject) =>
            _entries.TryGetValue(subject, out Entry entry) && entry.Denied;

        internal bool TryValidate(ulong subject, object connection, uint handle, out string reason)
        {
            reason = "steam-session-unobserved: reconnect after the server update";
            if (!_entries.TryGetValue(subject, out Entry entry)) return false;
            if (!ReferenceEquals(connection, entry.Connection) || handle != entry.Handle)
            { reason = "steam-session-connection-mismatch"; return false; }
            if (entry.Denied)
            { reason = "steam-session-rejected"; return false; }
            if (!entry.Accepted || !entry.Validated)
            { reason = "steam-session-pending: wait a moment and reopen F3"; return false; }
            reason = string.Empty;
            return true;
        }

        internal void Remove(ulong subject) => _entries.Remove(subject);
        internal void RemoveConnection(object connection)
        {
            var remove = new List<ulong>();
            foreach (var pair in _entries)
                if (ReferenceEquals(pair.Value.Connection, connection)) remove.Add(pair.Key);
            foreach (ulong subject in remove) _entries.Remove(subject);
        }
        internal void Clear() => _entries.Clear();
    }
}

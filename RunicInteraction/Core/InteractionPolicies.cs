using System;
using System.Collections.Generic;

namespace RunicInteraction.Core
{
    internal enum DoorCloseDecision
    {
        Remove = 0,
        Wait = 1,
        Close = 2
    }

    internal static class DoorClosePolicy
    {
        internal static DoorCloseDecision Evaluate(
            bool valid,
            bool owner,
            bool open,
            bool eligible,
            bool obstructed,
            float quietSeconds,
            float requiredQuietSeconds)
        {
            if (!valid || !open || !owner) return DoorCloseDecision.Remove;
            if (!eligible || obstructed || quietSeconds < requiredQuietSeconds)
                return DoorCloseDecision.Wait;
            return DoorCloseDecision.Close;
        }
    }

    internal enum TextEntryKind
    {
        Unsupported = 0,
        Portal = 1,
        Sign = 2,
        Tame = 3
    }

    internal readonly struct TextEntryDecision
    {
        internal TextEntryDecision(bool allowed, string reason)
        {
            Allowed = allowed;
            Reason = reason ?? string.Empty;
        }

        internal bool Allowed { get; }
        internal string Reason { get; }
    }

    internal static class TextEntryPolicy
    {
        internal static TextEntryDecision Validate(
            TextEntryKind kind,
            string value,
            int limit,
            bool receiverValid,
            bool authorized,
            bool inRange)
        {
            if (kind == TextEntryKind.Unsupported) return new TextEntryDecision(true, string.Empty);
            if (!receiverValid) return new TextEntryDecision(false, "receiver-invalid");
            if (!authorized) return new TextEntryDecision(false, "permission-changed");
            if (!inRange) return new TextEntryDecision(false, "out-of-range");
            if (value == null) return new TextEntryDecision(false, "null-text");
            if (value.Length > limit) return new TextEntryDecision(false, "too-long");

            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (char.IsHighSurrogate(character))
                {
                    if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                        return new TextEntryDecision(false, "invalid-unicode");
                    index++;
                    continue;
                }
                if (char.IsLowSurrogate(character))
                    return new TextEntryDecision(false, "invalid-unicode");
                if (!char.IsControl(character)) continue;
                if (kind == TextEntryKind.Sign && (character == '\n' || character == '\t')) continue;
                return new TextEntryDecision(false, "control-character");
            }
            return new TextEntryDecision(true, string.Empty);
        }
    }

    internal sealed class PickupFilterSet
    {
        internal const int MaximumRules = 128;
        private readonly HashSet<string> _rules;

        private PickupFilterSet(HashSet<string> rules, bool truncated)
        {
            _rules = rules;
            Truncated = truncated;
        }

        internal int Count => _rules.Count;
        internal bool Truncated { get; }

        internal bool Matches(string prefabName, string sharedName)
        {
            return !string.IsNullOrEmpty(prefabName) && _rules.Contains(prefabName) ||
                   !string.IsNullOrEmpty(sharedName) && _rules.Contains(sharedName);
        }

        internal static PickupFilterSet Parse(string text)
        {
            var rules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool truncated = false;
            if (!string.IsNullOrEmpty(text))
            {
                string[] candidates = text.Split(new[] { ',', ';', '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries);
                foreach (string candidate in candidates)
                {
                    string value = candidate.Trim();
                    if (value.Length == 0 || value.Length > 128) continue;
                    if (rules.Count >= MaximumRules && !rules.Contains(value))
                    {
                        truncated = true;
                        continue;
                    }
                    rules.Add(value);
                }
            }
            return new PickupFilterSet(rules, truncated);
        }
    }

    internal readonly struct MenuSelection
    {
        internal MenuSelection(string recipePrefab, bool upgrade, int activeGroup)
        {
            RecipePrefab = recipePrefab ?? string.Empty;
            Upgrade = upgrade;
            ActiveGroup = activeGroup;
        }

        internal string RecipePrefab { get; }
        internal bool Upgrade { get; }
        internal int ActiveGroup { get; }
        internal bool HasRecipe => RecipePrefab.Length != 0;
    }

    internal static class EquipmentRestorePolicy
    {
        internal static bool MayRestore(
            bool enabled,
            bool isLocalPlayer,
            bool alive,
            bool swimming,
            bool attacking,
            bool dodging,
            bool temporaryToolStillEquipped,
            bool userSelectedReplacement)
        {
            return enabled && isLocalPlayer && alive && !swimming && !attacking && !dodging &&
                   !temporaryToolStillEquipped && !userSelectedReplacement;
        }
    }

    internal static class TransferGesturePolicy
    {
        internal static bool MayTransfer(
            bool enabled,
            bool containerOpen,
            bool containerOwned,
            bool permission,
            bool wardAccess,
            bool hasItem,
            bool questItem,
            bool dragActive,
            bool itemProtectionAllowsTransfer)
        {
            return enabled && containerOpen && containerOwned && permission && wardAccess && hasItem &&
                   !questItem && !dragActive && itemProtectionAllowsTransfer;
        }
    }
}

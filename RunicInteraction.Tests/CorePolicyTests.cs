using System;
using RunicInteraction.Core;

namespace RunicInteraction.Tests
{
    internal static class CorePolicyTests
    {
        internal static void Register()
        {
            TestRunner.Run("door policy removes invalid and closed entries", DoorPolicyRemovesTerminalEntries);
            TestRunner.Run("door policy waits for owner eligibility quiet and clearance", DoorPolicyWaitsSafely);
            TestRunner.Run("door policy closes only a quiet eligible owner door", DoorPolicyClosesOnlyWhenSafe);
            TestRunner.Run("text validation rejects stale permission and range", TextValidationRejectsAuthorityChanges);
            TestRunner.Run("text validation enforces length unicode and controls", TextValidationEnforcesContent);
            TestRunner.Run("sign validation allows only its deliberate whitespace controls", SignValidationAllowsSafeFormatting);
            TestRunner.Run("pickup rules are exact case-insensitive and trimmed", PickupRulesAreExact);
            TestRunner.Run("pickup rules are bounded at 128 unique entries", PickupRulesAreBounded);
            TestRunner.Run("context memory is bounded and least-recently-used", ContextMemoryIsBoundedLru);
            TestRunner.Run("transfer policy requires every ownership and permission gate", TransferPolicyFailsClosed);
            TestRunner.Run("equipment restore requires a legal idle local state", EquipmentPolicyFailsClosed);
            TestRunner.Run("menu selection distinguishes craft and upgrade rows", MenuSelectionTracksMode);
        }

        private static void DoorPolicyRemovesTerminalEntries()
        {
            TestAssert.Equal(DoorCloseDecision.Remove,
                DoorClosePolicy.Evaluate(false, true, true, true, false, 10f, 1f));
            TestAssert.Equal(DoorCloseDecision.Remove,
                DoorClosePolicy.Evaluate(true, true, false, true, false, 10f, 1f));
        }

        private static void DoorPolicyWaitsSafely()
        {
            TestAssert.Equal(DoorCloseDecision.Remove,
                DoorClosePolicy.Evaluate(true, false, true, true, false, 10f, 1f));
            TestAssert.Equal(DoorCloseDecision.Wait,
                DoorClosePolicy.Evaluate(true, true, true, false, false, 10f, 1f));
            TestAssert.Equal(DoorCloseDecision.Wait,
                DoorClosePolicy.Evaluate(true, true, true, true, true, 10f, 1f));
            TestAssert.Equal(DoorCloseDecision.Wait,
                DoorClosePolicy.Evaluate(true, true, true, true, false, 0.99f, 1f));
        }

        private static void DoorPolicyClosesOnlyWhenSafe() =>
            TestAssert.Equal(DoorCloseDecision.Close,
                DoorClosePolicy.Evaluate(true, true, true, true, false, 1f, 1f));

        private static void TextValidationRejectsAuthorityChanges()
        {
            TestAssert.Equal("receiver-invalid",
                TextEntryPolicy.Validate(TextEntryKind.Portal, "x", 10, false, true, true).Reason);
            TestAssert.Equal("permission-changed",
                TextEntryPolicy.Validate(TextEntryKind.Sign, "x", 10, true, false, true).Reason);
            TestAssert.Equal("out-of-range",
                TextEntryPolicy.Validate(TextEntryKind.Tame, "x", 10, true, true, false).Reason);
            TestAssert.True(TextEntryPolicy.Validate(
                TextEntryKind.Unsupported, null, 0, false, false, false).Allowed);
        }

        private static void TextValidationEnforcesContent()
        {
            TestAssert.Equal("null-text",
                TextEntryPolicy.Validate(TextEntryKind.Portal, null, 10, true, true, true).Reason);
            TestAssert.Equal("too-long",
                TextEntryPolicy.Validate(TextEntryKind.Portal, "elevenchars", 10, true, true, true).Reason);
            TestAssert.Equal("control-character",
                TextEntryPolicy.Validate(TextEntryKind.Portal, "bad\n", 10, true, true, true).Reason);
            TestAssert.Equal("invalid-unicode",
                TextEntryPolicy.Validate(TextEntryKind.Portal, "\ud800", 10, true, true, true).Reason);
            TestAssert.True(TextEntryPolicy.Validate(
                TextEntryKind.Tame, "Boar \ud83d\udc17", 10, true, true, true).Allowed);
        }

        private static void SignValidationAllowsSafeFormatting()
        {
            TestAssert.True(TextEntryPolicy.Validate(
                TextEntryKind.Sign, "one\ttwo\nthree", 20, true, true, true).Allowed);
            TestAssert.False(TextEntryPolicy.Validate(
                TextEntryKind.Sign, "bad\0", 20, true, true, true).Allowed);
        }

        private static void PickupRulesAreExact()
        {
            PickupFilterSet set = PickupFilterSet.Parse(" Wood ; $item_stone\nWood ");
            TestAssert.Equal(2, set.Count);
            TestAssert.True(set.Matches("wood", null));
            TestAssert.True(set.Matches(null, "$ITEM_STONE"));
            TestAssert.False(set.Matches("FineWood", "$item_finewood"));
        }

        private static void PickupRulesAreBounded()
        {
            string[] rules = new string[130];
            for (int index = 0; index < rules.Length; index++) rules[index] = "item" + index;
            PickupFilterSet set = PickupFilterSet.Parse(string.Join(",", rules));
            TestAssert.Equal(PickupFilterSet.MaximumRules, set.Count);
            TestAssert.True(set.Truncated);
            TestAssert.False(set.Matches("item129", null));
        }

        private static void ContextMemoryIsBoundedLru()
        {
            var memory = new BoundedContextMemory<string, int>(2, StringComparer.Ordinal);
            memory.Put("a", 1);
            memory.Put("b", 2);
            TestAssert.True(memory.TryGet("a", out int one));
            TestAssert.Equal(1, one);
            memory.Put("c", 3);
            TestAssert.Equal(2, memory.Count);
            TestAssert.False(memory.TryGet("b", out _));
            TestAssert.True(memory.TryGet("a", out _));
            TestAssert.True(memory.TryGet("c", out _));
        }

        private static void TransferPolicyFailsClosed()
        {
            bool Allowed(bool enabled = true, bool open = true, bool owner = true,
                bool permission = true, bool ward = true, bool item = true,
                bool quest = false, bool drag = false, bool itemProtectionAllows = true) =>
                TransferGesturePolicy.MayTransfer(enabled, open, owner, permission, ward,
                    item, quest, drag, itemProtectionAllows);
            TestAssert.True(Allowed());
            TestAssert.False(Allowed(owner: false));
            TestAssert.False(Allowed(permission: false));
            TestAssert.False(Allowed(ward: false));
            TestAssert.False(Allowed(quest: true));
            TestAssert.False(Allowed(drag: true));
            TestAssert.False(Allowed(itemProtectionAllows: false));
        }

        private static void EquipmentPolicyFailsClosed()
        {
            bool Allowed(bool enabled = true, bool local = true, bool alive = true,
                bool swimming = false, bool attacking = false, bool dodging = false,
                bool tool = false, bool replacement = false) =>
                EquipmentRestorePolicy.MayRestore(enabled, local, alive, swimming,
                    attacking, dodging, tool, replacement);
            TestAssert.True(Allowed());
            TestAssert.False(Allowed(alive: false));
            TestAssert.False(Allowed(swimming: true));
            TestAssert.False(Allowed(attacking: true));
            TestAssert.False(Allowed(dodging: true));
            TestAssert.False(Allowed(tool: true));
            TestAssert.False(Allowed(replacement: true));
        }

        private static void MenuSelectionTracksMode()
        {
            var craft = new MenuSelection("RecipeA", false, 2);
            var upgrade = new MenuSelection("RecipeA", true, 3);
            TestAssert.True(craft.HasRecipe);
            TestAssert.False(craft.Upgrade);
            TestAssert.True(upgrade.Upgrade);
            TestAssert.Equal(3, upgrade.ActiveGroup);
            TestAssert.False(new MenuSelection(null, false, 0).HasRecipe);
        }
    }
}

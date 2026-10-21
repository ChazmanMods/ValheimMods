using System;

namespace Runic.Foundation.Core.Tests
{
    internal static class KeybindingTests
    {
        internal static void Register()
        {
            TestRunner.Run("Input chords normalize modifier order and spelling", ChordNormalization);
            TestRunner.Run("Registry detects exact cross-module and vanilla conflicts", ConflictDetection);
            TestRunner.Run("Removing a binding resolves its conflict", ConflictRemoval);
            TestRunner.Run("Stale keybinding leases cannot remove replacements", StaleLeaseSafety);
        }

        private static void ChordNormalization()
        {
            InputChord left = new InputChord(
                "keyboard-mouse",
                "Mouse Scroll",
                new[] { "Left-Shift", "Left Alt" });
            InputChord right = new InputChord(
                "keyboard-mouse",
                "mouse-scroll",
                new[] { "left_alt", "left shift" });
            TestAssert.True(left.Equals(right));
            TestAssert.Equal(left.GetHashCode(), right.GetHashCode());
        }

        private static void ConflictDetection()
        {
            KeybindingConflictRegistry registry = new KeybindingConflictRegistry();
            InputChord chord = new InputChord("keyboard", "F");
            registry.Register(new KeybindingDescriptor(
                RunicModuleIds.Valheim,
                "forsaken-power",
                "Forsaken Power",
                chord,
                "gameplay"));
            registry.Register(new KeybindingDescriptor(
                "test.interaction",
                "configure",
                "Configure",
                new InputChord("keyboard", "f"),
                "configuration"));
            TestAssert.Equal(1, registry.GetConflicts().Count);
            TestAssert.Equal(2, registry.GetConflicts()[0].Bindings.Count);
            TestAssert.Equal(1, registry.GetConflictsFor("test.interaction").Count);
        }

        private static void ConflictRemoval()
        {
            KeybindingConflictRegistry registry = new KeybindingConflictRegistry();
            InputChord chord = new InputChord("keyboard", "G");
            registry.Register(new KeybindingDescriptor(
                "test.alpha", "action", "Alpha", chord));
            KeybindingRegistration second = registry.Register(new KeybindingDescriptor(
                "test.beta", "action", "Beta", chord));
            TestAssert.Equal(1, registry.GetConflicts().Count);
            second.Dispose();
            TestAssert.Equal(0, registry.GetConflicts().Count);
        }

        private static void StaleLeaseSafety()
        {
            KeybindingConflictRegistry registry = new KeybindingConflictRegistry();
            KeybindingDescriptor binding = new KeybindingDescriptor(
                "test.alpha",
                "action",
                "Alpha",
                new InputChord("keyboard", "H"));
            KeybindingRegistration stale = registry.Register(binding);
            registry.Unregister("test.alpha", "action");
            KeybindingRegistration current = registry.Register(binding);
            stale.Dispose();
            TestAssert.True(current.IsActive);
        }
    }
}

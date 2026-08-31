using System;
using System.Collections.Generic;

namespace RunicInteraction.Core
{
    internal sealed class InteractionInputChord : IEquatable<InteractionInputChord>
    {
        private readonly IReadOnlyList<string> _modifiers;

        internal InteractionInputChord(
            string deviceId,
            string primaryControl,
            params string[] modifiers)
        {
            DeviceId = Require(deviceId, nameof(deviceId));
            PrimaryControl = Require(primaryControl, nameof(primaryControl));
            string[] copy = modifiers == null
                ? Array.Empty<string>()
                : (string[])modifiers.Clone();
            for (int index = 0; index < copy.Length; index++)
                copy[index] = Require(copy[index], nameof(modifiers));
            Array.Sort(copy, StringComparer.OrdinalIgnoreCase);
            _modifiers = Array.AsReadOnly(copy);
            CanonicalId = Canonical(DeviceId) + ":" +
                          string.Join("+", copy).ToLowerInvariant() + ":" +
                          Canonical(PrimaryControl);
        }

        internal string DeviceId { get; }
        internal string PrimaryControl { get; }
        internal IReadOnlyList<string> Modifiers => _modifiers;
        internal string CanonicalId { get; }

        public bool Equals(InteractionInputChord other) =>
            other != null && string.Equals(
                CanonicalId, other.CanonicalId, StringComparison.Ordinal);

        public override bool Equals(object value) =>
            Equals(value as InteractionInputChord);

        public override int GetHashCode() =>
            StringComparer.Ordinal.GetHashCode(CanonicalId);

        private static string Require(string value, string parameter)
        {
            string result = value?.Trim();
            if (string.IsNullOrEmpty(result) || result.Length > 64)
                throw new ArgumentException(
                    "An input identifier between 1 and 64 characters is required.",
                    parameter);
            return result;
        }

        private static string Canonical(string value) =>
            value.Replace(" ", string.Empty)
                .Replace("_", string.Empty)
                .Replace("-", string.Empty)
                .ToLowerInvariant();
    }

    internal sealed class KeybindingDescriptor
    {
        internal KeybindingDescriptor(
            string bindingId,
            string displayName,
            InteractionInputChord chord,
            string context)
        {
            BindingId = bindingId ?? throw new ArgumentNullException(nameof(bindingId));
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
            Chord = chord ?? throw new ArgumentNullException(nameof(chord));
            Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        internal string BindingId { get; }
        internal string QualifiedId => Plugin.ModuleId + "/" + BindingId;
        internal string DisplayName { get; }
        internal InteractionInputChord Chord { get; }
        internal string Context { get; }
    }

    /// <summary>Builds the complete bounded Interaction input catalog.</summary>
    internal static class InteractionInputBindings
    {
        internal const string PickupBypassControllerPrimary = "JoyUse";
        internal const string DefaultPickupBypassControllerModifier = "JoyRStick";
        internal const int BindingCount = 6;
        internal const int MaximumControllerModifierOptions = 14;

        internal const string DefaultBindingCatalog =
            "transfer-left-alt|keyboard|Mouse0|LeftAlt|inventory\n" +
            "transfer-right-alt|keyboard|Mouse0|RightAlt|inventory\n" +
            "transfer-controller|controller|JoyButtonX|JoyLTrigger|inventory\n" +
            "pickup-bypass-left-alt|keyboard|Use|LeftAlt|interaction\n" +
            "pickup-bypass-right-alt|keyboard|Use|RightAlt|interaction\n" +
            "pickup-bypass-controller|controller|JoyUse|JoyRStick|interaction";

        private static readonly string[] ControllerModifierOptions =
        {
            "JoyButtonB",
            "JoyButtonY",
            "JoyBack",
            "JoyStart",
            "JoyLStick",
            "JoyRStick",
            "JoyDPadLeft",
            "JoyDPadRight",
            "JoyDPadUp",
            "JoyDPadDown",
            "JoyLBumper",
            "JoyRBumper",
            "JoyLTrigger",
            "JoyRTrigger"
        };

        internal static string[] CreateControllerModifierOptions() =>
            (string[])ControllerModifierOptions.Clone();

        internal static string ResolveControllerModifier(
            string configured,
            out bool usedDefault)
        {
            string candidate = (configured ?? string.Empty).Trim();
            for (int index = 0; index < ControllerModifierOptions.Length; index++)
            {
                string allowed = ControllerModifierOptions[index];
                if (!string.Equals(
                        candidate, allowed, StringComparison.OrdinalIgnoreCase)) continue;
                usedDefault = false;
                return allowed;
            }

            usedDefault = true;
            return DefaultPickupBypassControllerModifier;
        }

        internal static IReadOnlyList<KeybindingDescriptor> CreateDescriptors(
            string controllerModifier)
        {
            string modifier = ResolveControllerModifier(
                controllerModifier, out _);
            return Array.AsReadOnly(new[]
            {
                Binding("transfer-left-alt", "Full-stack transfer (left Alt)",
                    "keyboard", "Mouse0", "LeftAlt", "inventory"),
                Binding("transfer-right-alt", "Full-stack transfer (right Alt)",
                    "keyboard", "Mouse0", "RightAlt", "inventory"),
                Binding("transfer-controller", "Full-stack transfer (controller)",
                    "controller", "JoyButtonX", "JoyLTrigger", "inventory"),
                Binding("pickup-bypass-left-alt", "Bypass pickup filter (left Alt)",
                    "keyboard", "Use", "LeftAlt", "interaction"),
                Binding("pickup-bypass-right-alt", "Bypass pickup filter (right Alt)",
                    "keyboard", "Use", "RightAlt", "interaction"),
                Binding("pickup-bypass-controller", "Bypass pickup filter (controller)",
                    "controller", PickupBypassControllerPrimary, modifier, "interaction")
            });
        }

        private static KeybindingDescriptor Binding(
            string id,
            string display,
            string device,
            string primary,
            string modifier,
            string context) =>
            new KeybindingDescriptor(
                id,
                display,
                new InteractionInputChord(device, primary, modifier),
                context);
    }
}

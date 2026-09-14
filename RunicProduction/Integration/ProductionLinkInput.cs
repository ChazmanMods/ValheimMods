using System;
using RunicProduction.Core;
using UnityEngine;

namespace RunicProduction.Integration
{
    /// <summary>
    /// Short input leases for mouse actions accepted as Production link-selection gestures. The
    /// local Player.Update and SetControls prefixes sample the raw edge; later vanilla ZInput
    /// readers receive a neutral value for the corresponding combat action until that mouse button
    /// is released and its down edge has cleared. Frame and physical-press guards make the
    /// two timing hooks idempotent even when an input edge spans multiple rendered frames.
    /// </summary>
    internal static class ProductionLinkInput
    {
        private static int _sampleFrame = -1;
        private static int _processedGestureFrame = -1;
        private static bool _captureActive;
        private static ProductionLinkMouseButton _consumedButton;

        internal static void BeginSample(int frame)
        {
            if (_sampleFrame == frame) return;
            if (!_captureActive) { _sampleFrame = frame; return; }
            // A failed input read is not proof of release. Keep the accepted press captured.
            try
            {
                KeyCode key = MouseKey(_consumedButton);
                BeginSample(frame, ZInput.GetKey(key, false), ZInput.GetKeyDown(key, false));
            }
            catch { _sampleFrame = frame; }
        }

        internal static void BeginSample(int frame, bool capturedButtonHeld, bool capturedButtonDown)
        {
            if (_sampleFrame == frame) return;
            _sampleFrame = frame;
            // Fast clicks can be released while their down edge is still reported. Waiting
            // for BOTH states to clear prevents that stale edge from cancelling the selection.
            if (_captureActive && !capturedButtonHeld && !capturedButtonDown)
                _captureActive = false;
        }

        internal static bool CanReadGesture(int frame) =>
            !(_captureActive || _processedGestureFrame == frame);

        internal static bool TryReadExactGesture(
            int frame,
            out ProductionLinkMouseButton button,
            out bool remove)
        {
            button = ProductionLinkMouseButton.Left;
            remove = false;
            if (!CanReadGesture(frame) ||
                !AltHeld || ControlHeld) return false;

            bool left = KeyDown(KeyCode.Mouse0);
            bool right = KeyDown(KeyCode.Mouse1);
            bool middle = KeyDown(KeyCode.Mouse2);
            int edges = (left ? 1 : 0) + (right ? 1 : 0) + (middle ? 1 : 0);
            if (edges != 1) return false;
            button = left
                ? ProductionLinkMouseButton.Left
                : right
                    ? ProductionLinkMouseButton.Right
                    : ProductionLinkMouseButton.Middle;
            remove = ShiftHeld;
            return true;
        }

        internal static void Consume(int frame, ProductionLinkMouseButton button)
        {
            _sampleFrame = frame;
            _processedGestureFrame = frame;
            _captureActive = true;
            _consumedButton = button;
        }

        internal static bool ShouldSuppress(string action, int frame)
        {
            if (!_captureActive || string.IsNullOrEmpty(action)) return false;
            switch (_consumedButton)
            {
                case ProductionLinkMouseButton.Left:
                    return string.Equals(action, "Attack", StringComparison.Ordinal);
                case ProductionLinkMouseButton.Right:
                    return string.Equals(action, "Block", StringComparison.Ordinal) ||
                           string.Equals(action, "BuildMenu", StringComparison.Ordinal);
                case ProductionLinkMouseButton.Middle:
                    return string.Equals(action, "SecondaryAttack", StringComparison.Ordinal) ||
                           string.Equals(action, "Remove", StringComparison.Ordinal);
                default:
                    return false;
            }
        }

        internal static bool SuppressSetControls(
            Player player,
            int frame,
            ref bool attack,
            ref bool attackHold,
            ref bool secondaryAttack,
            ref bool secondaryAttackHold,
            ref bool block,
            ref bool blockHold)
        {
            if (!_captureActive || player == null || player != Player.m_localPlayer)
                return false;
            switch (_consumedButton)
            {
                case ProductionLinkMouseButton.Left:
                    attack = false;
                    attackHold = false;
                    break;
                case ProductionLinkMouseButton.Right:
                    block = false;
                    blockHold = false;
                    break;
                case ProductionLinkMouseButton.Middle:
                    secondaryAttack = false;
                    secondaryAttackHold = false;
                    break;
            }
            return true;
        }

        internal static void Reset()
        {
            _sampleFrame = -1;
            _processedGestureFrame = -1;
            _captureActive = false;
            _consumedButton = ProductionLinkMouseButton.Left;
        }

        private static bool KeyDown(KeyCode key)
        {
            try { return ZInput.GetKeyDown(key, false); }
            catch { return false; }
        }

        private static KeyCode MouseKey(ProductionLinkMouseButton button)
        {
            switch (button)
            {
                case ProductionLinkMouseButton.Right: return KeyCode.Mouse1;
                case ProductionLinkMouseButton.Middle: return KeyCode.Mouse2;
                default: return KeyCode.Mouse0;
            }
        }

        private static bool AltHeld =>
            ValheimAccess.KeyboardKeyHeld(KeyCode.LeftAlt) ||
            ValheimAccess.KeyboardKeyHeld(KeyCode.RightAlt);

        private static bool ShiftHeld =>
            ValheimAccess.KeyboardKeyHeld(KeyCode.LeftShift) ||
            ValheimAccess.KeyboardKeyHeld(KeyCode.RightShift);

        private static bool ControlHeld =>
            ValheimAccess.KeyboardKeyHeld(KeyCode.LeftControl) ||
            ValheimAccess.KeyboardKeyHeld(KeyCode.RightControl);
    }
}

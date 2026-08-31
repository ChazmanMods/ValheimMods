using RunicInteraction.Core;
using UnityEngine;

namespace RunicInteraction.Integration
{
    internal static class TextEntryRuntime
    {
        private static TextReceiver _receiver;
        private static TextEntryKind _kind;
        private static int _limit;

        internal static void Begin(TextReceiver receiver, ref int requestedLimit)
        {
            if (!FeatureOn()) return;
            _receiver = receiver;
            _kind = Classify(receiver);
            if (_kind == TextEntryKind.Unsupported)
            {
                _limit = 0;
                return;
            }
            int configured = ConfiguredLimit(_kind, requestedLimit);
            _limit = Mathf.Max(1, Mathf.Min(requestedLimit, configured));
            requestedLimit = _limit;
        }

        internal static bool ValidateCommit(TextInput input, string value)
        {
            if (!FeatureOn() || _kind == TextEntryKind.Unsupported) return true;
            TextReceiver queued = ValheimAccess.GetQueuedTextReceiver(input);
            bool sameReceiver = queued != null && ReferenceEquals(queued, _receiver);
            MonoBehaviour receiverObject = _receiver as MonoBehaviour;
            Player player = Player.m_localPlayer;
            ZNetView view = receiverObject ? receiverObject.GetComponent<ZNetView>() : null;
            bool receiverValid = sameReceiver && receiverObject && view && view.IsValid();
            bool authorized = receiverValid && IsAuthorized(receiverObject, _kind);
            float range = _kind == TextEntryKind.Tame ? 15f : InteractionConfig.TextCommitRange.Value;
            bool inRange = receiverValid && player &&
                           Vector3.Distance(player.transform.position, receiverObject.transform.position) <= range;
            TextEntryDecision decision = TextEntryPolicy.Validate(
                _kind, value, _limit, receiverValid, authorized, inRange);
            if (decision.Allowed) return true;

            ValheimAccess.ClearQueuedTextReceiver(input);
            if (player)
            {
                if (decision.Reason == "permission-changed")
                    player.Message(MessageHud.MessageType.Center, "$piece_noaccess");
                else if (decision.Reason == "out-of-range")
                    player.Message(MessageHud.MessageType.Center, "$msg_outofrange");
                else
                    player.Message(MessageHud.MessageType.Center, "$msg_blocked");
            }
            Diagnostics.Trace("Text entry declined safely: " + decision.Reason + ".");
            ClearSession();
            return false;
        }

        internal static void AfterHide(TextInput input)
        {
            if (input != null) ValheimAccess.ClearQueuedTextReceiver(input);
            ClearSession();
        }

        internal static void OnConfigurationChanged()
        {
            if (!FeatureOn()) ClearSession();
        }

        internal static void Shutdown() => ClearSession();

        private static bool IsAuthorized(MonoBehaviour receiver, TextEntryKind kind)
        {
            if (kind == TextEntryKind.Tame)
                return receiver is Tameable tameable && tameable.IsTamed();
            return PrivateArea.CheckAccess(receiver.transform.position, 0f, flash: false);
        }

        private static TextEntryKind Classify(TextReceiver receiver)
        {
            if (receiver is TeleportWorld) return TextEntryKind.Portal;
            if (receiver is Sign) return TextEntryKind.Sign;
            if (receiver is Tameable) return TextEntryKind.Tame;
            return TextEntryKind.Unsupported;
        }

        private static int ConfiguredLimit(TextEntryKind kind, int fallback)
        {
            switch (kind)
            {
                case TextEntryKind.Portal: return InteractionConfig.PortalTextLimit.Value;
                case TextEntryKind.Sign: return InteractionConfig.SignTextLimit.Value;
                case TextEntryKind.Tame: return InteractionConfig.TameTextLimit.Value;
                default: return fallback;
            }
        }

        private static void ClearSession()
        {
            _receiver = null;
            _kind = TextEntryKind.Unsupported;
            _limit = 0;
        }

        private static bool FeatureOn() =>
            InteractionConfig.Enabled.Value && InteractionConfig.TextEntryPolish.Value;
    }
}

using RunicInteraction.Core;
using UnityEngine;

namespace RunicInteraction.Integration
{
    internal static class EquipmentRestoreRuntime
    {
        internal readonly struct EquipCapture
        {
            internal EquipCapture(
                bool local,
                bool tool,
                ItemDrop.ItemData right,
                ItemDrop.ItemData left)
            {
                Local = local;
                Tool = tool;
                Right = right;
                Left = left;
            }

            internal bool Local { get; }
            internal bool Tool { get; }
            internal ItemDrop.ItemData Right { get; }
            internal ItemDrop.ItemData Left { get; }
        }

        private static ItemDrop.ItemData _right;
        private static ItemDrop.ItemData _left;
        private static ItemDrop.ItemData _tool;
        private static int _dueFrame = -1;
        private static ItemDrop.ItemData _swimRight;
        private static ItemDrop.ItemData _swimLeft;
        private static int _swimDueFrame = -1;
        private static bool _restoring;

        internal static void BeforeHideHands(Humanoid actor, bool onlyRightHand)
        {
            if (!FeatureOn() || onlyRightHand || _restoring || !actor || actor != Player.m_localPlayer) return;
            Player player = actor as Player;
            if (!player || !player.IsSwimming() || player.IsOnGround()) return;
            _swimRight = IsCombatHand(actor.RightItem) ? actor.RightItem : null;
            _swimLeft = IsCombatHand(actor.LeftItem) ? actor.LeftItem : null;
            _swimDueFrame = -1;
        }

        internal static void AfterShowHands(Humanoid actor, bool onlyRightHand)
        {
            if (!FeatureOn() || onlyRightHand || _restoring || !actor || actor != Player.m_localPlayer) return;
            if (_swimRight == null && _swimLeft == null) return;
            if (SwimCandidatesRestored(actor))
            {
                ClearSwim();
                return;
            }
            _swimDueFrame = Time.frameCount + 1;
        }

        internal static EquipCapture BeforeEquip(Humanoid actor, ItemDrop.ItemData item)
        {
            bool local = actor && actor == Player.m_localPlayer;
            bool tool = item != null && item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Tool;
            if (!local || !tool || _restoring) return new EquipCapture(local, tool, null, null);
            return new EquipCapture(
                true,
                true,
                IsCombatHand(actor.RightItem) ? actor.RightItem : null,
                IsCombatHand(actor.LeftItem) ? actor.LeftItem : null);
        }

        internal static void AfterEquip(
            Humanoid actor,
            ItemDrop.ItemData item,
            bool result,
            EquipCapture capture)
        {
            if (!result || !capture.Local || _restoring || item == null) return;
            if (capture.Tool)
            {
                _tool = item;
                _dueFrame = -1;
                if (capture.Right != null || capture.Left != null)
                {
                    _right = capture.Right;
                    _left = capture.Left;
                }
                return;
            }
            if (IsCombatHand(item)) Clear(); // A deliberate replacement always wins.
        }

        internal static void AfterUnequip(Humanoid actor, ItemDrop.ItemData item)
        {
            if (_restoring || !actor || actor != Player.m_localPlayer || item == null || item != _tool) return;
            if (_right == null && _left == null)
            {
                Clear();
                return;
            }
            _dueFrame = Time.frameCount + 1;
        }

        internal static void Tick()
        {
            Player player = Player.m_localPlayer;
            if (!FeatureOn() || !player)
            {
                Clear();
                return;
            }
            ProcessSwimRestore(player);
            if (_tool != null && !player.GetInventory().ContainsItem(_tool))
            {
                _tool = null;
                if (_right != null || _left != null) _dueFrame = Time.frameCount + 1;
            }
            if (_dueFrame < 0 || Time.frameCount < _dueFrame) return;

            bool toolStillEquipped = _tool != null && player.IsItemEquiped(_tool);
            bool userReplacement = HasUnrelatedCombat(player, _right, _left);
            if (!EquipmentRestorePolicy.MayRestore(
                    true,
                    player == Player.m_localPlayer,
                    !player.IsDead(),
                    player.IsSwimming() && !player.IsOnGround(),
                    player.InAttack(),
                    player.InDodge(),
                    toolStillEquipped,
                    userReplacement))
            {
                if (userReplacement) Clear();
                else _dueFrame = Time.frameCount + 1;
                return;
            }

            ItemDrop.ItemData right = player.RightItem == _right || player.LeftItem == _right
                ? null
                : LegalCandidate(player, _right) ? _right : null;
            ItemDrop.ItemData left = player.RightItem == _left || player.LeftItem == _left
                ? null
                : LegalCandidate(player, _left) ? _left : null;
            Clear();
            if (right == null && left == null) return;
            _restoring = true;
            try
            {
                if (right != null) player.EquipItem(right);
                if (left != null) player.EquipItem(left);
            }
            finally
            {
                _restoring = false;
            }
        }

        internal static void OnConfigurationChanged()
        {
            if (!FeatureOn()) Clear();
        }

        internal static void Shutdown() => Clear();

        private static bool LegalCandidate(Player player, ItemDrop.ItemData item)
        {
            return item != null && player.GetInventory().ContainsItem(item) && IsCombatHand(item) &&
                   (!item.m_shared.m_useDurability || item.m_durability > 0f);
        }

        private static void ProcessSwimRestore(Player player)
        {
            if (_swimDueFrame < 0 || Time.frameCount < _swimDueFrame) return;
            if (SwimCandidatesRestored(player))
            {
                ClearSwim();
                return;
            }
            bool unrelatedReplacement = HasUnrelatedCombat(player, _swimRight, _swimLeft);
            if (!EquipmentRestorePolicy.MayRestore(
                    true,
                    player == Player.m_localPlayer,
                    !player.IsDead(),
                    player.IsSwimming() && !player.IsOnGround(),
                    player.InAttack(),
                    player.InDodge(),
                    false,
                    unrelatedReplacement))
            {
                if (unrelatedReplacement) ClearSwim();
                else _swimDueFrame = Time.frameCount + 1;
                return;
            }

            ItemDrop.ItemData right = player.RightItem == _swimRight
                ? null
                : LegalCandidate(player, _swimRight) ? _swimRight : null;
            ItemDrop.ItemData left = player.LeftItem == _swimLeft
                ? null
                : LegalCandidate(player, _swimLeft) ? _swimLeft : null;
            ClearSwim();
            if (right == null && left == null) return;
            _restoring = true;
            try
            {
                if (right != null) player.EquipItem(right);
                if (left != null) player.EquipItem(left);
            }
            finally
            {
                _restoring = false;
            }
        }

        private static bool SwimCandidatesRestored(Humanoid actor) =>
            (_swimRight == null || actor.RightItem == _swimRight) &&
            (_swimLeft == null || actor.LeftItem == _swimLeft);

        private static bool HasUnrelatedCombat(
            Humanoid actor,
            ItemDrop.ItemData storedRight,
            ItemDrop.ItemData storedLeft) =>
            IsCombatHand(actor.RightItem) && actor.RightItem != storedRight && actor.RightItem != storedLeft ||
            IsCombatHand(actor.LeftItem) && actor.LeftItem != storedRight && actor.LeftItem != storedLeft;

        private static bool IsCombatHand(ItemDrop.ItemData item)
        {
            if (item == null) return false;
            switch (item.m_shared.m_itemType)
            {
                case ItemDrop.ItemData.ItemType.OneHandedWeapon:
                case ItemDrop.ItemData.ItemType.Shield:
                case ItemDrop.ItemData.ItemType.Bow:
                case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
                    return true;
                default:
                    return false;
            }
        }

        private static void Clear()
        {
            _right = null;
            _left = null;
            _tool = null;
            _dueFrame = -1;
            ClearSwim();
        }

        private static void ClearSwim()
        {
            _swimRight = null;
            _swimLeft = null;
            _swimDueFrame = -1;
        }

        private static bool FeatureOn() =>
            InteractionConfig.Enabled.Value && InteractionConfig.EquipmentRestore.Value;
    }
}

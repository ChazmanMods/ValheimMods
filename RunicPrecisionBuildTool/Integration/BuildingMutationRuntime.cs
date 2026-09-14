using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace QuietBuildRotation.Integration
{
    internal static class BuildingMutationRuntime
    {
        internal const int ColliderCapacity = 256;
        internal const int RepairCandidateCapacity = 64;
        internal const int SupportInstanceCapacity = 2048;
        internal const int SupportColliderCapacity = 128;

        private const float PositionTolerance = 0.005f;
        private const float RotationToleranceDegrees = 0.05f;
        private const float FullHealthTolerance = 0.0001f;

        private delegate bool CheckCanRemovePieceDelegate(Player player, Piece piece);
        private delegate ItemDrop.ItemData GetRightItemDelegate(Humanoid humanoid);
        private delegate float GetPlaceDurabilityDelegate(Player player, ItemDrop.ItemData item);
        private delegate void InventoryChangedDelegate(
            Inventory inventory,
            bool success,
            bool cheatedStateChanged);

        private static readonly Collider[] ColliderBuffer = new Collider[ColliderCapacity];
        private static readonly RepairCandidate[] RepairCandidates =
            new RepairCandidate[RepairCandidateCapacity];

        private static CheckCanRemovePieceDelegate _checkCanRemovePiece;
        private static GetRightItemDelegate _getRightItem;
        private static GetPlaceDurabilityDelegate _getPlaceDurability;
        private static InventoryChangedDelegate _inventoryChanged;
        private static AccessTools.FieldRef<WearNTear, List<Collider>> _supportColliders;
        private static AccessTools.FieldRef<WearNTear, bool> _clearCachedSupport;
        private static int _pieceMask;
        private static UndoRecord _undo;

        internal static bool Initialize(out string error)
        {
            error = null;
            try
            {
                MethodInfo check = AccessTools.Method(
                    typeof(Player), "CheckCanRemovePiece", new[] { typeof(Piece) });
                if (check == null || check.ReturnType != typeof(bool))
                    throw new MissingMethodException(typeof(Player).FullName, "CheckCanRemovePiece");
                _checkCanRemovePiece =
                    AccessTools.MethodDelegate<CheckCanRemovePieceDelegate>(check);

                MethodInfo getRight = AccessTools.Method(
                    typeof(Humanoid), "GetRightItem", Type.EmptyTypes);
                MethodInfo changed = AccessTools.Method(
                    typeof(Inventory), "Changed", new[] { typeof(bool), typeof(bool) });
                MethodInfo getPlaceDurability = AccessTools.Method(
                    typeof(Player), "GetPlaceDurability", new[] { typeof(ItemDrop.ItemData) });
                if (getRight == null || getRight.ReturnType != typeof(ItemDrop.ItemData))
                    throw new MissingMethodException(typeof(Humanoid).FullName, "GetRightItem");
                if (changed == null || changed.ReturnType != typeof(void))
                    throw new MissingMethodException(typeof(Inventory).FullName, "Changed");
                if (getPlaceDurability == null || getPlaceDurability.ReturnType != typeof(float))
                    throw new MissingMethodException(typeof(Player).FullName, "GetPlaceDurability");
                _getRightItem = AccessTools.MethodDelegate<GetRightItemDelegate>(getRight);
                _getPlaceDurability =
                    AccessTools.MethodDelegate<GetPlaceDurabilityDelegate>(getPlaceDurability);
                _inventoryChanged = AccessTools.MethodDelegate<InventoryChangedDelegate>(changed);

                FieldInfo support = AccessTools.Field(typeof(WearNTear), "m_supportColliders");
                FieldInfo dirty = AccessTools.Field(typeof(WearNTear), "m_clearCachedSupport");
                if (support == null || support.FieldType != typeof(List<Collider>))
                    throw new MissingFieldException(typeof(WearNTear).FullName, "m_supportColliders");
                if (dirty == null || dirty.FieldType != typeof(bool))
                    throw new MissingFieldException(typeof(WearNTear).FullName, "m_clearCachedSupport");
                _supportColliders =
                    AccessTools.FieldRefAccess<WearNTear, List<Collider>>(support);
                _clearCachedSupport =
                    AccessTools.FieldRefAccess<WearNTear, bool>(dirty);
                _pieceMask = LayerMask.GetMask("piece");
                if (_pieceMask == 0)
                    throw new InvalidOperationException("Valheim's piece physics layer was not resolved");
                return true;
            }
            catch (Exception exception)
            {
                Shutdown();
                error = "safe building mutation adapter failed: " +
                        exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        internal static void Shutdown()
        {
            _undo = default;
            _checkCanRemovePiece = null;
            _getRightItem = null;
            _getPlaceDurability = null;
            _inventoryChanged = null;
            _supportColliders = null;
            _clearCachedSupport = null;
            _pieceMask = 0;
            Array.Clear(ColliderBuffer, 0, ColliderBuffer.Length);
            Array.Clear(RepairCandidates, 0, RepairCandidates.Length);
        }

        internal static void EndPlacementSession() => _undo = default;

        internal static void ObserveCommittedPlacement(Player player, Piece piece)
        {
            _undo = default;
            if (!player || player != Player.m_localPlayer || !piece ||
                piece.GetCreator() != player.GetPlayerID())
                return;

            ZNetView view = piece.GetComponent<ZNetView>();
            WearNTear wear = piece.GetComponent<WearNTear>();
            if (!view || !view.IsValid() || !view.IsOwner() || !wear ||
                !IsInert(piece))
                return;

            ZDO zdo = view.GetZDO();
            if (zdo == null || zdo.m_uid.IsNone() || zdo.GetPrefab() == 0) return;
            Vector3 position = zdo.GetPosition();
            Quaternion rotation = zdo.GetRotation();
            if (!VectorMath.IsFinite(position) ||
                !QuaternionMath.TryNormalize(rotation, out _)) return;

            _undo = new UndoRecord(
                piece,
                piece.GetInstanceID(),
                player.GetPlayerID(),
                position,
                rotation);
        }

        internal static bool TryUndo(Player player, out string message)
        {
            message = null;
            UndoRecord record = _undo;
            Piece piece = record.Piece;
            if (!player || player != Player.m_localPlayer || !record.IsValid || !piece ||
                piece.GetInstanceID() != record.InstanceId)
            {
                _undo = default;
                message = "No eligible Runic placement is available to undo.";
                return false;
            }

            WearNTear wear = piece.GetComponent<WearNTear>();
            ZNetView view = piece.GetComponent<ZNetView>();
            Vector3 position = piece.transform.position;
            bool freshOwner = view && view.IsValid() && view.IsOwner();
            bool creator = piece.GetCreator() == record.Creator &&
                           record.Creator == player.GetPlayerID();
            bool access = PrivateArea.CheckAccess(position, 0f, false, false);
            bool inRange = Vector3.Distance(player.transform.position, position) <=
                           Mathf.Max(0f, player.m_maxPlaceDistance);
            bool outsideNoBuild = !Location.IsInsideNoBuildLocation(position);
            bool unchanged = VectorMath.Approximately(position, record.Position, PositionTolerance) &&
                             QuaternionMath.AngleDegrees(
                                 piece.transform.rotation, record.Rotation) <= RotationToleranceDegrees;
            bool fullHealth = wear && wear.GetHealthPercentage() >= 1f - FullHealthTolerance;
            bool inert = IsInert(piece);
            bool independent = wear && IsStructurallyIndependent(wear);
            bool nativePolicy = piece.m_canBeRemoved && piece.CanBeRemoved() &&
                                _checkCanRemovePiece != null && _checkCanRemovePiece(player, piece);

            MutationEvidence evidence = new MutationEvidence(
                Diagnostics.CanRun,
                player && player == Player.m_localPlayer,
                freshOwner,
                creator,
                access,
                inRange,
                outsideNoBuild,
                unchanged,
                fullHealth,
                inert,
                independent,
                nativePolicy,
                true,
                true);
            MutationDenial denial = MutationPolicy.EvaluateUndo(in evidence);
            if (denial != MutationDenial.None)
            {
                message = UndoDenialMessage(denial);
                return false;
            }

            // Clear before the first irreversible native callback so a second key press cannot
            // duplicate removal drops after a partial failure.
            _undo = default;
            try
            {
                IRemoved removed = piece.GetComponent<IRemoved>();
                removed?.OnRemoved();
                wear.Remove(false);
                message = "Most recent eligible placement removed through its native owner path.";
                return true;
            }
            catch (Exception exception)
            {
                message = "Undo stopped after its one-shot native mutation began (" +
                          exception.GetType().Name + "). Check the world before retrying.";
                return false;
            }
        }

        internal static bool TryAreaRepair(Player player, out string message)
        {
            message = null;
            if (!player || player != Player.m_localPlayer || !Diagnostics.CanRun)
            {
                message = "Area repair is unavailable.";
                return false;
            }
            ItemDrop.ItemData tool = _getRightItem?.Invoke(player);
            if (tool?.m_shared?.m_buildPieces == null)
            {
                message = "Equip a build hammer before area repair.";
                return false;
            }

            float radius = Mathf.Clamp(PluginConfig.AreaRepairRadiusMeters.Value, 1f, 12f);
            int maximum = Mathf.Clamp(
                PluginConfig.AreaRepairMaximumPieces.Value, 1, RepairCandidateCapacity);
            int hitCount = 0;
            int candidateCount = 0;
            try
            {
                hitCount = Physics.OverlapSphereNonAlloc(
                    player.transform.position,
                    radius,
                    ColliderBuffer,
                    _pieceMask);
                if (hitCount >= ColliderCapacity)
                {
                    message = "Area repair discovery reached its 256-collider bound; nothing was changed.";
                    return false;
                }

                for (int index = 0; index < hitCount; index++)
                {
                    Collider collider = ColliderBuffer[index];
                    WearNTear wear = collider ? collider.GetComponentInParent<WearNTear>() : null;
                    if (!wear || ContainsCandidate(wear, candidateCount)) continue;
                    Piece piece = wear.GetComponent<Piece>();
                    if (!piece || wear.GetHealthPercentage() >= 1f - FullHealthTolerance)
                        continue;

                    float distanceSquared =
                        (piece.transform.position - player.transform.position).sqrMagnitude;
                    InsertCandidate(
                        new RepairCandidate(wear, piece, distanceSquared),
                        ref candidateCount);
                }

                if (candidateCount == 0)
                {
                    message = "No damaged eligible pieces were found in the bounded area.";
                    return false;
                }

                int repaired = 0;
                bool inventoryChanged = false;
                try
                {
                    for (int index = 0; index < candidateCount && repaired < maximum; index++)
                    {
                        RepairCandidate candidate = RepairCandidates[index];
                        if (!TryBuildFreshRepairEvidence(
                                player,
                                tool,
                                candidate,
                                radius,
                                out MutationEvidence evidence,
                                out float durabilityCost))
                            continue;
                        if (MutationPolicy.EvaluateRepair(in evidence) != MutationDenial.None)
                            continue;
                        if (!candidate.Wear.Repair()) continue;

                        repaired++;
                        if (tool.m_shared.m_useDurability)
                        {
                            tool.m_durability = Mathf.Max(
                                0f,
                                tool.m_durability - durabilityCost);
                            inventoryChanged = true;
                        }
                    }
                }
                finally
                {
                    if (inventoryChanged)
                    {
                        Inventory inventory = player.GetInventory();
                        // Area repair changes only the equipped tool's durability. The two
                        // Valheim 1.0 flags are item-add success and cheated-state change;
                        // neither applies to this ordinary inventory-content notification.
                        if (inventory != null) _inventoryChanged?.Invoke(inventory, false, false);
                    }
                }

                message = repaired > 0
                    ? "Area repair completed: " + repaired +
                      " piece(s), one native durability drain per success."
                    : "No candidate passed fresh owner, creator, ward, range, policy, cooldown, and durability checks.";
                return repaired > 0;
            }
            finally
            {
                Array.Clear(ColliderBuffer, 0, ColliderBuffer.Length);
                Array.Clear(RepairCandidates, 0, RepairCandidates.Length);
            }
        }

        private static bool TryBuildFreshRepairEvidence(
            Player player,
            ItemDrop.ItemData tool,
            RepairCandidate candidate,
            float radius,
            out MutationEvidence evidence,
            out float durabilityCost)
        {
            Piece piece = candidate.Piece;
            WearNTear wear = candidate.Wear;
            ZNetView view = piece ? piece.GetComponent<ZNetView>() : null;
            Vector3 position = piece ? piece.transform.position : Vector3.zero;
            durabilityCost = tool != null && tool.m_shared != null &&
                             tool.m_shared.m_useDurability &&
                             _getPlaceDurability != null
                ? _getPlaceDurability(player, tool)
                : 0f;
            bool validCost = QuaternionMath.IsFinite(durabilityCost) && durabilityCost >= 0f;
            float nativeRange = Mathf.Max(0f, player.m_maxPlaceDistance);
            bool toolAvailable = tool != null && tool.m_shared != null &&
                                 tool == _getRightItem?.Invoke(player) &&
                                 validCost &&
                                 (!tool.m_shared.m_useDurability ||
                                  tool.m_durability >= durabilityCost);
            evidence = new MutationEvidence(
                Diagnostics.CanRun,
                player && player == Player.m_localPlayer,
                view && view.IsValid() && view.IsOwner(),
                piece && piece.GetCreator() == player.GetPlayerID(),
                piece && PrivateArea.CheckAccess(position, 0f, false, false),
                piece && Vector3.Distance(player.transform.position, position) <=
                         Mathf.Min(radius, nativeRange),
                piece && !Location.IsInsideNoBuildLocation(position),
                true,
                true,
                true,
                true,
                piece && piece.m_canBeRemoved && _checkCanRemovePiece != null &&
                _checkCanRemovePiece(player, piece) && wear &&
                wear.GetHealthPercentage() < 1f - FullHealthTolerance,
                toolAvailable,
                true);
            return piece && wear;
        }

        private static bool IsStructurallyIndependent(WearNTear target)
        {
            if (!target || _supportColliders == null || _clearCachedSupport == null)
                return false;
            List<WearNTear> all = WearNTear.GetAllInstances();
            if (all == null || all.Count > SupportInstanceCapacity)
                return false;

            for (int index = 0; index < all.Count; index++)
            {
                WearNTear other = all[index];
                if (!other || other == target) continue;
                if (_clearCachedSupport(other)) return false;
                List<Collider> supports = _supportColliders(other);
                if (supports == null || supports.Count > SupportColliderCapacity)
                    return false;
                for (int supportIndex = 0; supportIndex < supports.Count; supportIndex++)
                {
                    Collider support = supports[supportIndex];
                    if (support && support.GetComponentInParent<WearNTear>() == target)
                        return false;
                }
            }
            return true;
        }

        private static bool IsInert(Piece piece)
        {
            if (!piece) return false;
            MonoBehaviour[] behaviours = piece.GetComponentsInChildren<MonoBehaviour>(true);
            if (behaviours == null || behaviours.Length > 64) return false;
            for (int index = 0; index < behaviours.Length; index++)
            {
                MonoBehaviour behaviour = behaviours[index];
                if (!behaviour || behaviour == piece || behaviour is WearNTear ||
                    behaviour is ZNetView)
                    continue;

                // This is an allowlist. Unknown and future behaviours may carry
                // access, production, terrain, inventory, or ownership state that cannot be
                // proven unchanged from a transform/health snapshot, so they are never eligible.
                return false;
            }
            return true;
        }

        private static bool ContainsCandidate(WearNTear wear, int count)
        {
            for (int index = 0; index < count; index++)
                if (RepairCandidates[index].Wear == wear) return true;
            return false;
        }

        private static void InsertCandidate(RepairCandidate candidate, ref int count)
        {
            int insertion = count;
            for (int index = 0; index < count; index++)
            {
                RepairCandidate current = RepairCandidates[index];
                if (candidate.DistanceSquared < current.DistanceSquared ||
                    (Mathf.Approximately(candidate.DistanceSquared, current.DistanceSquared) &&
                     candidate.InstanceId < current.InstanceId))
                {
                    insertion = index;
                    break;
                }
            }

            if (count < RepairCandidateCapacity) count++;
            else if (insertion >= RepairCandidateCapacity) return;
            for (int index = count - 1; index > insertion; index--)
                RepairCandidates[index] = RepairCandidates[index - 1];
            RepairCandidates[insertion] = candidate;
        }

        private static string UndoDenialMessage(MutationDenial denial)
        {
            switch (denial)
            {
                case MutationDenial.ObjectAuthorityUnavailable:
                    return "Undo stopped because this process is not the current ZDO owner.";
                case MutationDenial.WrongCreator:
                    return "Undo stopped because creator ownership changed or is not yours.";
                case MutationDenial.WardDenied:
                    return "Undo stopped by a fresh ward/access check.";
                case MutationDenial.OutOfRange:
                    return "Undo stopped because the piece is outside normal hammer range.";
                case MutationDenial.Changed:
                    return "Undo refused: the piece position or rotation changed.";
                case MutationDenial.Damaged:
                    return "Undo refused: the piece is no longer at full health.";
                case MutationDenial.AccessedOrInteractive:
                    return "Undo refused: interactive/accessed piece types are never eligible.";
                case MutationDenial.StructurallyDependedUpon:
                    return "Undo refused: structural independence could not be proven.";
                default:
                    return "Undo stopped by a fresh native placement/removal policy check (" +
                           denial + ").";
            }
        }

        private readonly struct UndoRecord
        {
            internal UndoRecord(
                Piece piece,
                int instanceId,
                long creator,
                Vector3 position,
                Quaternion rotation)
            {
                Piece = piece;
                InstanceId = instanceId;
                Creator = creator;
                Position = position;
                Rotation = rotation;
            }

            internal Piece Piece { get; }
            internal int InstanceId { get; }
            internal long Creator { get; }
            internal Vector3 Position { get; }
            internal Quaternion Rotation { get; }
            internal bool IsValid => Piece && InstanceId != 0 && Creator != 0L;
        }

        private readonly struct RepairCandidate
        {
            internal RepairCandidate(
                WearNTear wear,
                Piece piece,
                float distanceSquared)
            {
                Wear = wear;
                Piece = piece;
                DistanceSquared = distanceSquared;
                InstanceId = piece ? piece.GetInstanceID() : 0;
            }

            internal WearNTear Wear { get; }
            internal Piece Piece { get; }
            internal float DistanceSquared { get; }
            internal int InstanceId { get; }
        }
    }
}

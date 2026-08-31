using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RunicBuildCamera.Core;
using UnityEngine;

namespace RunicBuildCamera.Integration
{
    /// <summary>
    /// Bounded camera-centred pickup for ordinary loose ItemDrop instances. Discovery is local,
    /// but every target must independently pass its current ward, ownership, inventory, weight,
    /// and distance checks before the ordinary Humanoid.Pickup path is allowed to run.
    /// </summary>
    internal static class RemotePickupRuntime
    {
        private readonly struct CooldownEntry
        {
            internal CooldownEntry(float retryAt, long ordinal)
            {
                RetryAt = retryAt;
                Ordinal = ordinal;
            }

            internal float RetryAt { get; }
            internal long Ordinal { get; }
        }

        private static readonly Collider[] ColliderBuffer =
            new Collider[PickupPolicy.ColliderCapacity];
        private static readonly HashSet<ZDOID> SeenThisScan = new HashSet<ZDOID>();
        private static readonly Dictionary<ZDOID, CooldownEntry> Cooldowns =
            new Dictionary<ZDOID, CooldownEntry>();
        private static readonly FieldInfo EnableAutoPickupField =
            AccessTools.Field(typeof(Player), "m_enableAutoPickup");
        private static readonly FieldInfo AutoPickupMaskField =
            AccessTools.Field(typeof(Player), "m_autoPickupMask");

        private static bool _subscribed;
        private static bool _wasActive;
        private static Player _lastPlayer;
        private static float _nextScanAt;
        private static long _cooldownOrdinal;

        internal static void Tick()
        {
            EnsureSubscribed();

            bool hasContext = BuildCameraRuntime.TryGetActiveContext(
                out Player player,
                out Vector3 cameraPosition,
                out _);
            bool active = BuildCameraRuntime.IsActive &&
                          BuildCameraConfig.PickupEnabled != null &&
                          BuildCameraConfig.PickupEnabled.Value &&
                          hasContext;
            if (!active || player == null || player != Player.m_localPlayer ||
                player.IsTeleporting() ||
                !TryReadAutoPickupState(player, out bool autoPickupEnabled, out int pickupMask) ||
                !autoPickupEnabled)
            {
                if (_wasActive || _lastPlayer != null || Cooldowns.Count != 0)
                    ResetTransientState();
                return;
            }

            if (player != _lastPlayer)
            {
                ResetTransientState();
                _lastPlayer = player;
            }

            _wasActive = true;
            float now = Time.time;
            if (!PickupPolicy.IsScanDue(now, _nextScanAt)) return;

            float interval = BuildCameraConfig.PickupIntervalSeconds != null
                ? BuildCameraConfig.PickupIntervalSeconds.Value
                : 0.20f;
            _nextScanAt = PickupPolicy.NextScanAt(now, interval);

            float range = BuildCameraConfig.PickupRange != null
                ? BuildCameraConfig.PickupRange.Value
                : 0f;
            if (float.IsNaN(range) || float.IsInfinity(range) || range <= 0f) return;

            if (!IsFinite(cameraPosition)) return;

            Inventory inventory = player.GetInventory();
            if (inventory == null) return;

            Scan(player, inventory, cameraPosition, range, interval, now, pickupMask);
        }

        internal static void Reset()
        {
            ResetTransientState();
        }

        internal static void Shutdown()
        {
            if (_subscribed)
            {
                BuildCameraConfig.Changed -= OnConfigurationChanged;
                _subscribed = false;
            }
            ResetTransientState();
        }

        private static void Scan(
            Player player,
            Inventory inventory,
            Vector3 cameraPosition,
            float range,
            float interval,
            float now,
            int pickupMask)
        {
            int count;
            try
            {
                count = Physics.OverlapSphereNonAlloc(
                    cameraPosition,
                    range,
                    ColliderBuffer,
                    pickupMask);
            }
            catch
            {
                return;
            }

            SeenThisScan.Clear();
            PruneExpiredCooldowns(now);
            int attempts = 0;

            try
            {
                int boundedCount = Math.Min(count, ColliderBuffer.Length);
                for (int index = 0;
                     index < boundedCount && attempts < PickupPolicy.MaximumAttemptsPerScan;
                     index++)
                {
                    Collider collider = ColliderBuffer[index];
                    if (!collider) continue;

                    ItemDrop itemDrop = ResolveLooseItem(collider);
                    if (!itemDrop) continue;

                    ZNetView view = itemDrop.GetComponent<ZNetView>();
                    ZDO zdo = view != null && view.IsValid() ? view.GetZDO() : null;
                    if (zdo == null || zdo.m_uid.IsNone() || !SeenThisScan.Add(zdo.m_uid))
                        continue;

                    try
                    {
                        itemDrop.Load();
                        if (!TryEvaluate(
                                itemDrop,
                                zdo.m_uid,
                                player,
                                inventory,
                                cameraPosition,
                                range,
                                now))
                            continue;

                        // RequestOwn is a no-op when this peer already owns the drop. When it
                        // needs an asynchronous handoff, the per-ZDO cooldown prevents an RPC
                        // request on every camera frame.
                        itemDrop.RequestOwn();
                        attempts++;
                        if (!itemDrop.CanPickup(true))
                        {
                            SetCooldown(
                                zdo.m_uid,
                                PickupPolicy.OwnershipRetryAt(now, interval));
                            continue;
                        }

                        // Ownership may have arrived since the preceding scan. Re-evaluate all
                        // mutable target conditions at the final synchronous commit boundary.
                        itemDrop.Load();
                        if (!TryEvaluate(
                                itemDrop,
                                zdo.m_uid,
                                player,
                                inventory,
                                cameraPosition,
                                range,
                                now,
                                ignoreCooldown: true))
                        {
                            SetCooldown(zdo.m_uid, PickupPolicy.FailedPickupRetryAt(now));
                            continue;
                        }

                        bool pickedUp = player.Pickup(
                            itemDrop.gameObject,
                            autoequip: true,
                            autoPickupDelay: true);
                        SetCooldown(
                            zdo.m_uid,
                            pickedUp
                                ? PickupPolicy.CompletedPickupRetryAt(now)
                                : PickupPolicy.FailedPickupRetryAt(now));
                    }
                    catch
                    {
                        SetCooldown(zdo.m_uid, PickupPolicy.FailedPickupRetryAt(now));
                    }
                }
            }
            finally
            {
                SeenThisScan.Clear();
                for (int index = 0; index < Math.Min(count, ColliderBuffer.Length); index++)
                    ColliderBuffer[index] = null;
            }
        }

        private static bool TryEvaluate(
            ItemDrop itemDrop,
            ZDOID id,
            Player player,
            Inventory inventory,
            Vector3 cameraPosition,
            float range,
            float now,
            bool ignoreCooldown = false)
        {
            ItemDrop.ItemData item = itemDrop.m_itemData;
            ItemDrop.ItemData.SharedData shared = item?.m_shared;
            if (item == null || shared == null) return false;

            bool coolingDown = !ignoreCooldown &&
                               Cooldowns.TryGetValue(id, out CooldownEntry entry) &&
                               now < entry.RetryAt;
            Vector3 targetPosition = itemDrop.transform.position;
            bool withinRange = IsFinite(targetPosition) &&
                               PickupPolicy.IsWithinRange(
                                   (targetPosition - cameraPosition).sqrMagnitude,
                                   range);
            bool wardAllows = withinRange &&
                              PrivateArea.CheckAccess(
                                  targetPosition,
                                  0f,
                                  flash: false,
                                  wardCheck: true);
            bool uniqueOrQuest = shared.m_questItem ||
                                 player.HaveUniqueKey(shared.m_name);
            bool canAdd = inventory.CanAddItem(item);
            float itemWeight = item.GetWeight();
            float resultingWeight = inventory.GetTotalWeight() + itemWeight;
            float maximumWeight = player.GetMaxCarryWeight();
            bool tooHeavy = float.IsNaN(itemWeight) || float.IsInfinity(itemWeight) ||
                            itemWeight < 0f ||
                            float.IsNaN(resultingWeight) || float.IsInfinity(resultingWeight) ||
                            float.IsNaN(maximumWeight) || float.IsInfinity(maximumWeight) ||
                            resultingWeight > maximumWeight;

            PickupCandidateFacts facts = new PickupCandidateFacts(
                hasValidNetworkIdentity: !id.IsNone(),
                isWithinRange: withinRange,
                wardAllows: wardAllows,
                autoPickupEnabled: itemDrop.m_autoPickup,
                isPiece: itemDrop.IsPiece(),
                isInTar: itemDrop.InTar(),
                isUniqueOrQuestItem: uniqueOrQuest,
                inventoryCanAdd: canAdd,
                wouldExceedCarryWeight: tooHeavy,
                isCoolingDown: coolingDown);
            return PickupPolicy.Evaluate(in facts) == PickupRejectionReason.None;
        }

        private static ItemDrop ResolveLooseItem(Collider collider)
        {
            Rigidbody body = collider.attachedRigidbody;
            if (!body) return null;

            ItemDrop itemDrop = body.GetComponent<ItemDrop>();
            if (itemDrop) return itemDrop;

            FloatingTerrainDummy dummy = body.GetComponent<FloatingTerrainDummy>();
            return dummy && dummy.m_parent
                ? dummy.m_parent.gameObject.GetComponent<ItemDrop>()
                : null;
        }

        private static void SetCooldown(ZDOID id, float retryAt)
        {
            if (id.IsNone()) return;

            if (!Cooldowns.ContainsKey(id) &&
                Cooldowns.Count >= PickupPolicy.MaximumCooldownEntries)
                RemoveOldestCooldown();

            if (_cooldownOrdinal == long.MaxValue)
            {
                // Ordinal wrap is practically unreachable, but resetting the bounded table is
                // safer than allowing ambiguous eviction order.
                Cooldowns.Clear();
                _cooldownOrdinal = 0L;
            }
            long ordinal = ++_cooldownOrdinal;
            Cooldowns[id] = new CooldownEntry(retryAt, ordinal);
        }

        private static void PruneExpiredCooldowns(float now)
        {
            if (Cooldowns.Count == 0) return;
            List<ZDOID> expired = null;
            foreach (KeyValuePair<ZDOID, CooldownEntry> pair in Cooldowns)
            {
                if (now < pair.Value.RetryAt) continue;
                if (expired == null) expired = new List<ZDOID>();
                expired.Add(pair.Key);
            }

            if (expired == null) return;
            foreach (ZDOID id in expired) Cooldowns.Remove(id);
        }

        private static void RemoveOldestCooldown()
        {
            bool found = false;
            ZDOID oldestId = ZDOID.None;
            long oldestOrdinal = long.MaxValue;
            foreach (KeyValuePair<ZDOID, CooldownEntry> pair in Cooldowns)
            {
                if (found && pair.Value.Ordinal >= oldestOrdinal) continue;
                found = true;
                oldestId = pair.Key;
                oldestOrdinal = pair.Value.Ordinal;
            }
            if (found) Cooldowns.Remove(oldestId);
        }

        private static void EnsureSubscribed()
        {
            if (_subscribed) return;
            BuildCameraConfig.Changed += OnConfigurationChanged;
            _subscribed = true;
        }

        private static void OnConfigurationChanged() => ResetTransientState();

        private static void ResetTransientState()
        {
            _wasActive = false;
            _lastPlayer = null;
            _nextScanAt = 0f;
            _cooldownOrdinal = 0L;
            Cooldowns.Clear();
            SeenThisScan.Clear();
            Array.Clear(ColliderBuffer, 0, ColliderBuffer.Length);
        }

        private static bool TryReadAutoPickupState(
            Player player,
            out bool enabled,
            out int pickupMask)
        {
            enabled = false;
            pickupMask = 0;
            if (player == null || EnableAutoPickupField == null || AutoPickupMaskField == null ||
                EnableAutoPickupField.FieldType != typeof(bool) ||
                AutoPickupMaskField.FieldType != typeof(int) ||
                !EnableAutoPickupField.IsStatic || AutoPickupMaskField.IsStatic)
                return false;

            try
            {
                enabled = (bool)EnableAutoPickupField.GetValue(null);
                pickupMask = (int)AutoPickupMaskField.GetValue(player);
                return pickupMask != 0;
            }
            catch
            {
                enabled = false;
                pickupMask = 0;
                return false;
            }
        }

        private static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}

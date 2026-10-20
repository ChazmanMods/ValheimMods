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
    /// but every target must independently pass its current ownership, inventory, weight,
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

        private static readonly ItemDrop[] ItemBuffer =
            new ItemDrop[PickupPolicy.CandidateCapacity];
        private static readonly HashSet<ZDOID> SeenThisScan = new HashSet<ZDOID>();
        private static readonly Dictionary<ZDOID, CooldownEntry> Cooldowns =
            new Dictionary<ZDOID, CooldownEntry>();
        private static readonly FieldInfo EnableAutoPickupField =
            AccessTools.Field(typeof(Player), "m_enableAutoPickup");
        private static readonly FieldInfo ItemInstancesField =
            AccessTools.Field(typeof(ItemDrop), "s_instances");

        private static bool _subscribed;
        private static bool _wasActive;
        private static Player _lastPlayer;
        private static float _nextScanAt;
        private static long _cooldownOrdinal;
        private static int _scanCursor;
        private static int _pendingCount;
        private static int _pendingIndex;
        private static float _nextDiagnosticAt;
        private static float _nextWarningAt;
        private static bool _autoPickupOffShown;

        internal static void ReportFailure(Exception exception) =>
            Report("Camera pickup update failed: " + exception);

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
            if (!active) _autoPickupOffShown = false;
            if (!active || player == null || player != Player.m_localPlayer ||
                player.IsTeleporting() ||
                !TryReadAutoPickupState(player, out bool autoPickupEnabled) ||
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

            Scan(player, inventory, cameraPosition, range, interval, now);
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
            float now)
        {
            int count;
            try
            {
                var items = ItemInstancesField?.GetValue(null) as List<ItemDrop>;
                if (items == null)
                {
                    Report("Camera pickup could not read Valheim's loaded item registry.");
                    return;
                }
                if (_pendingIndex >= _pendingCount)
                {
                    _pendingCount = PickupScanWindow.CopyNext(items, ItemBuffer, ref _scanCursor);
                    _pendingIndex = 0;
                }
                count = _pendingCount;
            }
            catch (Exception exception)
            {
                Report("Camera pickup discovery failed: " + exception);
                return;
            }

            SeenThisScan.Clear();
            PruneExpiredCooldowns(now);
            int attempts = 0;

            try
            {
                int boundedCount = Math.Min(count, ItemBuffer.Length);
                while (_pendingIndex < boundedCount && attempts < PickupPolicy.MaximumAttemptsPerScan)
                {
                    ItemDrop itemDrop = ItemBuffer[_pendingIndex];
                    ItemBuffer[_pendingIndex++] = null;
                    if (!itemDrop || !itemDrop.isActiveAndEnabled ||
                        !itemDrop.gameObject.activeInHierarchy) continue;

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
                        if (!pickedUp) Report("Camera pickup declined by the normal pickup path or another mod.", false);
                        SetCooldown(
                            zdo.m_uid,
                            pickedUp
                                ? PickupPolicy.CompletedPickupRetryAt(now)
                                : PickupPolicy.FailedPickupRetryAt(now));
                    }
                    catch (Exception exception)
                    {
                        Report("Camera pickup failed: " + exception);
                        SetCooldown(zdo.m_uid, PickupPolicy.FailedPickupRetryAt(now));
                    }
                }
            }
            finally
            {
                SeenThisScan.Clear();
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
            // Fish are actors with their own interaction/fishing rules, even though they also have ItemDrop.
            bool isFish = itemDrop.GetComponent<Fish>() != null;
            if (withinRange && !isFish && !PlayerWardPickupAccess.Allows(targetPosition))
            {
                Report("Camera pickup rejected " + shared.m_name + ": PlayerWardDenied", false);
                return false;
            }
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
                isFish: isFish,
                autoPickupEnabled: itemDrop.m_autoPickup,
                isPiece: itemDrop.IsPiece(),
                isInTar: itemDrop.InTar(),
                isUniqueOrQuestItem: uniqueOrQuest,
                inventoryCanAdd: canAdd,
                wouldExceedCarryWeight: tooHeavy,
                isCoolingDown: coolingDown);
            PickupRejectionReason reason = PickupPolicy.Evaluate(in facts);
            if (withinRange && reason != PickupRejectionReason.None &&
                reason != PickupRejectionReason.CoolingDown)
                Report("Camera pickup rejected " + shared.m_name + ": " + reason, false);
            return reason == PickupRejectionReason.None;
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
            _scanCursor = 0;
            _pendingCount = 0;
            _pendingIndex = 0;
            Cooldowns.Clear();
            SeenThisScan.Clear();
            Array.Clear(ItemBuffer, 0, ItemBuffer.Length);
        }

        private static bool TryReadAutoPickupState(Player player, out bool enabled)
        {
            enabled = false;
            if (player == null || EnableAutoPickupField == null ||
                EnableAutoPickupField.FieldType != typeof(bool) || !EnableAutoPickupField.IsStatic)
            {
                Report("Camera pickup could not read Valheim's auto-pickup setting.");
                return false;
            }
            try
            {
                enabled = (bool)EnableAutoPickupField.GetValue(null);
                if (!enabled && !_autoPickupOffShown)
                    player.Message(MessageHud.MessageType.TopLeft, "$hud_autopickup: $hud_off");
                _autoPickupOffShown = !enabled;
                if (!enabled) Report("Camera pickup paused: player auto-pickup is off.", false);
                return true;
            }
            catch (Exception exception)
            {
                Report("Camera pickup state read failed: " + exception);
                return false;
            }
        }

        private static void Report(string message, bool warning = true)
        {
            if (warning)
            {
                if (Time.time < _nextWarningAt) return;
                _nextWarningAt = Time.time + 5f;
                Diagnostics.Warn(message);
            }
            else
            {
                if (Time.time < _nextDiagnosticAt) return;
                _nextDiagnosticAt = Time.time + 5f;
                Diagnostics.Verbose(message);
            }
        }

        private static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}

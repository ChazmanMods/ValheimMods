using System;
using System.Collections.Generic;
using UnityEngine;

namespace RunicInteraction.Integration
{
    internal static class DoorAutoCloseRuntime
    {
        private const int MaximumTimers = 64;
        private const int TickBudget = 8;
        private const float MaximumLifetimeSeconds = 70f;
        private static readonly List<PendingDoorTimer> Timers =
            new List<PendingDoorTimer>(MaximumTimers);
        private static readonly Collider[] Obstructions = new Collider[32];
        private static int _cursor;
        private static bool _closing;

        internal readonly struct DoorOpenCapture
        {
            internal DoorOpenCapture(
                Door door,
                Player actor,
                ZDOID id,
                uint revision,
                int prefabHash)
            {
                Door = door;
                Actor = actor;
                Id = id;
                Revision = revision;
                PrefabHash = prefabHash;
            }

            internal Door Door { get; }
            internal Player Actor { get; }
            internal ZDOID Id { get; }
            internal uint Revision { get; }
            internal int PrefabHash { get; }
            internal bool Eligible => Door && Actor && !Id.IsNone();
        }

        internal static void Initialize() => Shutdown();

        internal static void ObserveState(Door door)
        {
            Player actor = Player.m_localPlayer;
            if (_closing || !FeatureOn() || !door || !actor || !actor.IsOwner() || actor.IsDead() ||
                !IsPlayerBuilt(door)) return;
            ZNetView view = door.GetComponent<ZNetView>();
            ZDO zdo = view && view.IsValid() ? view.GetZDO() : null;
            if (zdo == null || !zdo.IsValid() || zdo.m_uid.IsNone()) return;
            int state = zdo.GetInt(ZDOVars.s_state);
            for (int index = 0; index < Timers.Count; index++)
            {
                if (Timers[index].Id != zdo.m_uid) continue;
                if (state == 0) Timers.RemoveAt(index);
                // Repeated UpdateState calls must not postpone the closing deadline.
                else if (Timers[index].OpenState != state)
                {
                    Timers[index].OpenState = state;
                    Timers[index].DueRealtime = Time.realtimeSinceStartup + EffectiveDelaySeconds();
                }
                return;
            }
            if (state == 0 || door.m_canNotBeClosed || door.m_keyItem || Timers.Count >= MaximumTimers ||
                !PrivateArea.CheckAccess(door.transform.position, 0f, false, false)) return;
            var capture = new DoorOpenCapture(door, actor, zdo.m_uid, zdo.DataRevision, zdo.GetPrefab());
            var timer = NewTimer(capture, Time.realtimeSinceStartup);
            timer.OpenState = state;
            Timers.Add(timer);
        }

        internal static DoorOpenCapture BeforeInteract(Door door, Humanoid character, bool hold)
        {
            if (!FeatureOn() || !door || hold || !(character is Player actor) ||
                actor != Player.m_localPlayer || !actor.IsOwner() || door.m_canNotBeClosed ||
                door.m_keyItem || !IsPlayerBuilt(door))
                return default;
            ZNetView view = door.GetComponent<ZNetView>();
            ZDO zdo = view && view.IsValid() ? view.GetZDO() : null;
            if (zdo == null || !zdo.IsValid() || zdo.m_uid.IsNone() ||
                zdo.GetInt(ZDOVars.s_state) != 0)
                return default;
            return new DoorOpenCapture(door, actor, zdo.m_uid, zdo.DataRevision, zdo.GetPrefab());
        }

        internal static void AfterInteract(DoorOpenCapture capture, bool vanillaAccepted)
        {
            if (!capture.Eligible || !vanillaAccepted || !FeatureOn() || !IsPlayerBuilt(capture.Door)) return;
            float now = Time.realtimeSinceStartup;
            for (int index = 0; index < Timers.Count; index++)
            {
                if (Timers[index].Id != capture.Id) continue;
                Timers[index] = NewTimer(capture, now);
                return;
            }
            if (Timers.Count >= MaximumTimers)
            {
                Diagnostics.Warn("Door auto-close reached its bounded timer capacity; this door remains open.");
                return;
            }
            Timers.Add(NewTimer(capture, now));
        }

        internal static void Tick()
        {
            if (!FeatureOn())
            {
                Shutdown();
                return;
            }
            int budget = Math.Min(TickBudget, Timers.Count);
            for (int count = 0; count < budget && Timers.Count > 0; count++)
            {
                if (_cursor >= Timers.Count) _cursor = 0;
                int index = _cursor;
                PendingDoorTimer timer = Timers[index];
                if (TickOne(timer))
                {
                    Timers.RemoveAt(index);
                    if (_cursor >= Timers.Count) _cursor = 0;
                }
                else
                {
                    _cursor++;
                }
            }
        }

        internal static void OnConfigurationChanged()
        {
            if (!FeatureOn()) Shutdown();
        }

        internal static void ObserveWardTopologyMutation(ZDO zdo)
        {
        }

        internal static void Shutdown()
        {
            Timers.Clear();
            _cursor = 0;
        }

        private static PendingDoorTimer NewTimer(DoorOpenCapture capture, float now) =>
            new PendingDoorTimer
            {
                Door = capture.Door,
                Actor = capture.Actor,
                Id = capture.Id,
                ExpectedPrefabHash = capture.PrefabHash,
                DueRealtime = now + EffectiveDelaySeconds(),
                ExpiresRealtime = now + MaximumLifetimeSeconds
            };

        private static bool TickOne(PendingDoorTimer timer)
        {
            float now = Time.realtimeSinceStartup;
            if (timer == null || now >= timer.ExpiresRealtime || !timer.Door || !timer.Actor)
                return true;
            if (timer.Actor != Player.m_localPlayer || !timer.Actor.IsOwner() || timer.Actor.IsDead() ||
                !IsPlayerBuilt(timer.Door)) return true;
            ZNetView view = timer.Door.GetComponent<ZNetView>();
            ZDO zdo = view && view.IsValid() ? view.GetZDO() : null;
            if (zdo == null || !zdo.IsValid() || zdo.m_uid != timer.Id ||
                zdo.GetPrefab() != timer.ExpectedPrefabHash || zdo.GetInt(ZDOVars.s_state) == 0)
                return true;
            if (now < timer.DueRealtime) return false;
            if (timer.Door.m_canNotBeClosed || timer.Door.m_keyItem)
                return true;
            if (!ValheimAccess.DoorCanInteract(timer.Door)) return false;
            if (!PrivateArea.CheckAccess(timer.Door.transform.position, 0f, false, false))
                return true;
            if (IsObstructed(timer.Door))
            {
                timer.DueRealtime = now + 0.5f;
                return false;
            }
            if (!view.IsOwner())
            {
                view.ClaimOwnership();
                timer.DueRealtime = now + 0.25f;
                return false;
            }
            try
            {
                _closing = true;
                ValheimAccess.CloseDoor(timer.Door, ZNet.GetUID());
                return zdo.GetInt(ZDOVars.s_state) == 0;
            }
            catch (Exception exception)
            {
                Diagnostics.Warn("Door auto-close stopped: " + exception.GetType().Name + ".");
                return true;
            }
            finally { _closing = false; }
        }

        private static bool IsPlayerBuilt(Door door)
        {
            // Network ownership and interaction do not imply player construction.
            Piece piece = door.GetComponent<Piece>();
            return piece && piece.IsPlacedByPlayer();
        }

        private static bool IsObstructed(Door door)
        {
            int count = Physics.OverlapSphereNonAlloc(
                door.transform.position + Vector3.up,
                InteractionConfig.DoorObstructionRadius.Value,
                Obstructions,
                Physics.AllLayers,
                QueryTriggerInteraction.Ignore);
            bool full = count >= Obstructions.Length;
            for (int index = 0; index < Math.Min(count, Obstructions.Length); index++)
            {
                Collider collider = Obstructions[index];
                Obstructions[index] = null;
                if (!collider || collider.transform.IsChildOf(door.transform)) continue;
                Rigidbody body = collider.attachedRigidbody;
                if (collider.GetComponentInParent<Character>() || body && !body.isKinematic)
                    return true;
            }
            return full;
        }

        private static bool FeatureOn() =>
            InteractionConfig.Enabled?.Value == true &&
            InteractionConfig.AutoCloseDoors?.Value == true;

        private static float EffectiveDelaySeconds() =>
            Mathf.Max(
                Mathf.Clamp(
                    InteractionConfig.DoorDelaySeconds.Value, 1f, 60f),
                Mathf.Clamp(
                    InteractionConfig.DoorRecentUseSeconds.Value, 0.5f, 10f));

        private sealed class PendingDoorTimer
        {
            internal Door Door;
            internal Player Actor;
            internal ZDOID Id;
            internal int ExpectedPrefabHash;
            internal int OpenState;
            internal float DueRealtime;
            internal float ExpiresRealtime;
        }
    }
}

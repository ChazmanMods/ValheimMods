using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace RunicBuildCamera.Integration
{
    /// <summary>
    /// Version-sensitive Valheim access is isolated here and verified before camera patches run.
    /// </summary>
    internal static class ValheimAdapter
    {
        private delegate bool TakeInputDelegate(Player player);

        internal sealed class RangeState
        {
            internal Player Player;
            internal float Original;
            internal float Requested;
            internal int Depth;
        }

        internal sealed class RangeLease : IDisposable
        {
            private readonly int _playerId;
            private readonly RangeState _state;
            private bool _disposed;

            internal RangeLease(int playerId, RangeState state)
            {
                _playerId = playerId;
                _state = state;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                ExitRange(_playerId, _state);
            }
        }

        private static readonly Dictionary<int, RangeState> RangeStates =
            new Dictionary<int, RangeState>();

        private static AccessTools.FieldRef<Player, float> _maxPlaceDistance;
        private static AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> _rightItem;
        private static TakeInputDelegate _takeInput;
        private static bool _initialized;

        internal static MethodInfo UpdatePlacementMethod { get; private set; }

        internal static MethodInfo UpdatePlacementGhostMethod { get; private set; }

        internal static MethodInfo UpdateCameraMethod { get; private set; }

        internal static MethodInfo SetControlsMethod { get; private set; }

        internal static bool Initialize(out string error)
        {
            error = null;
            if (_initialized) return true;

            try
            {
                FieldInfo rangeField = RequireField(typeof(Player), "m_maxPlaceDistance", typeof(float));
                _maxPlaceDistance = AccessTools.FieldRefAccess<Player, float>(rangeField);
                FieldInfo rightItemField = RequireField(
                    typeof(Humanoid), "m_rightItem", typeof(ItemDrop.ItemData));
                _rightItem = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>(rightItemField);

                UpdatePlacementMethod = RequireMethod(
                    typeof(Player), "UpdatePlacement", typeof(void), typeof(bool), typeof(float));
                UpdatePlacementGhostMethod = RequireMethod(
                    typeof(Player), "UpdatePlacementGhost", typeof(void), typeof(bool));
                UpdateCameraMethod = RequireMethod(
                    typeof(GameCamera), "UpdateCamera", typeof(void), typeof(float));
                SetControlsMethod = RequireMethod(
                    typeof(Player),
                    "SetControls",
                    typeof(void),
                    typeof(UnityEngine.Vector3),
                    typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool),
                    typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool),
                    typeof(bool));

                MethodInfo takeInput = AccessTools.Method(typeof(Player), "TakeInput", Type.EmptyTypes);
                if (takeInput == null || takeInput.ReturnType != typeof(bool))
                    throw new MissingMethodException(typeof(Player).FullName, "TakeInput()");
                _takeInput = AccessTools.MethodDelegate<TakeInputDelegate>(takeInput);

                _initialized = true;
                return true;
            }
            catch (Exception exception)
            {
                Shutdown();
                error = $"Valheim camera adapter verification failed: " +
                        $"{exception.GetType().Name}: {exception.Message}";
                return false;
            }
        }

        internal static void Shutdown()
        {
            ForceRestoreRanges();
            _maxPlaceDistance = null;
            _rightItem = null;
            _takeInput = null;
            UpdatePlacementMethod = null;
            UpdatePlacementGhostMethod = null;
            UpdateCameraMethod = null;
            SetControlsMethod = null;
            _initialized = false;
        }

        internal static bool IsLocalPlayer(Player player) =>
            player != null && Player.m_localPlayer != null && player == Player.m_localPlayer;

        internal static bool IsUsableLocalPlayer(Player player)
        {
            if (!IsLocalPlayer(player)) return false;
            try
            {
                return !player.IsDead() && !player.IsTeleporting();
            }
            catch
            {
                return false;
            }
        }

        internal static bool IsBuildToolEquipped(Player player)
        {
            if (!IsUsableLocalPlayer(player)) return false;
            try
            {
                ItemDrop.ItemData item = _rightItem != null ? _rightItem(player) : null;
                return item != null && item.m_shared != null && item.m_shared.m_buildPieces != null &&
                       player.InPlaceMode();
            }
            catch
            {
                return false;
            }
        }

        internal static bool CanTakeInput(Player player)
        {
            if (!IsUsableLocalPlayer(player) || _takeInput == null) return false;
            try
            {
                return _takeInput(player);
            }
            catch
            {
                return false;
            }
        }

        internal static RangeLease EnterRemoteActionRange(
            Player player,
            float requestedPlayerRange,
            float requestedStationRange)
        {
            if (!_initialized || !IsLocalPlayer(player) || _maxPlaceDistance == null ||
                !IsFinite(requestedPlayerRange) || !IsPositiveFinite(requestedStationRange))
            {
                return null;
            }

            int playerId = player.GetInstanceID();
            if (!RangeStates.TryGetValue(playerId, out RangeState state))
            {
                state = new RangeState
                {
                    Player = player,
                    Original = _maxPlaceDistance(player),
                    Requested = requestedStationRange,
                    Depth = 0
                };
                RangeStates.Add(playerId, state);
            }

            state.Depth++;
            // This is a scoped cap, not a permanent global reach edit. Setting the exact value
            // prevents another larger base/mod value from silently defeating the configured
            // detached-camera limit. The exact prior value is restored by the lease finalizer.
            _maxPlaceDistance(player) = requestedPlayerRange;
            return new RangeLease(playerId, state);
        }

        internal static void ForceRestoreRanges()
        {
            foreach (RangeState state in RangeStates.Values)
            {
                try
                {
                    if (state.Player != null && _maxPlaceDistance != null)
                        _maxPlaceDistance(state.Player) = state.Original;
                }
                catch
                {
                    // Cleanup is best effort during world teardown; every live state is removed.
                }
            }
            RangeStates.Clear();
        }

        internal static bool TryGetScopedStationRange(out float range)
        {
            foreach (RangeState state in RangeStates.Values)
            {
                if (state.Depth <= 0 || !IsPositiveFinite(state.Requested)) continue;
                range = state.Requested;
                return true;
            }
            range = 0f;
            return false;
        }

        private static void ExitRange(int playerId, RangeState expected)
        {
            if (!RangeStates.TryGetValue(playerId, out RangeState current) ||
                !ReferenceEquals(current, expected))
            {
                return;
            }

            current.Depth--;
            if (current.Depth > 0) return;

            if (current.Player != null && _maxPlaceDistance != null)
                _maxPlaceDistance(current.Player) = current.Original;
            RangeStates.Remove(playerId);
        }

        private static FieldInfo RequireField(Type owner, string name, Type fieldType)
        {
            FieldInfo field = AccessTools.Field(owner, name);
            if (field == null || field.FieldType != fieldType)
                throw new MissingFieldException(owner.FullName, name);
            return field;
        }

        private static MethodInfo RequireMethod(
            Type owner,
            string name,
            Type returnType,
            params Type[] parameters)
        {
            MethodInfo method = AccessTools.Method(owner, name, parameters);
            if (method == null || method.ReturnType != returnType)
                throw new MissingMethodException(owner.FullName, name);
            return method;
        }

        private static bool IsPositiveFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

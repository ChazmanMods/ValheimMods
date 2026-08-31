using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RunicBuildCamera.Integration
{
    /// <summary>
    /// Temporarily moves only the local player's already-existing SE_Demister ball to the
    /// detached camera. This runtime never adds a status effect, instantiates a ball, changes a
    /// prefab, or creates a network object.
    /// </summary>
    internal static class DemisterRuntime
    {
        private readonly struct ForceFieldBaseline
        {
            internal ForceFieldBaseline(ParticleSystemForceField field, float endRange)
            {
                Field = field;
                EndRange = endRange;
            }

            internal ParticleSystemForceField Field { get; }
            internal float EndRange { get; }
        }

        private sealed class BallState
        {
            internal BallState(GameObject ball, ForceFieldBaseline[] forceFields)
            {
                Ball = ball;
                ForceFields = forceFields ?? Array.Empty<ForceFieldBaseline>();
            }

            internal GameObject Ball { get; }
            internal ForceFieldBaseline[] ForceFields { get; }
        }

        private static readonly FieldInfo BallInstanceField =
            AccessTools.Field(typeof(SE_Demister), "m_ballInstance");
        private static readonly Dictionary<int, BallState> BallStates =
            new Dictionary<int, BallState>();
        private static readonly Dictionary<int, int> EffectBallIds =
            new Dictionary<int, int>();

        private static bool _subscribed;

        internal static void AfterStatusEffectUpdate(SE_Demister effect)
        {
            EnsureSubscribed();
            if (effect == null)
            {
                RefreshActiveState();
                return;
            }

            GameObject ball = GetBall(effect);
            if (!ball)
            {
                RestoreEffect(effect);
                PruneDestroyedBalls();
                return;
            }

            int effectId = effect.GetInstanceID();
            int ballId = ball.GetInstanceID();
            if (EffectBallIds.TryGetValue(effectId, out int previousBallId) &&
                previousBallId != ballId)
                RestoreBall(previousBallId);
            EffectBallIds[effectId] = ballId;

            bool followEnabled = BuildCameraConfig.DemisterFollowCamera != null &&
                                 BuildCameraConfig.DemisterFollowCamera.Value;
            bool hasContext = BuildCameraRuntime.TryGetActiveContext(
                out Player player,
                out Vector3 cameraPosition,
                out _);
            if (!followEnabled || !hasContext || player == null ||
                player != Player.m_localPlayer || effect.m_character != player ||
                !IsFinite(cameraPosition))
            {
                RestoreEffect(effect);
                return;
            }

            BallState state = GetOrCaptureBall(ball);
            if (state == null) return;

            float multiplier = BuildCameraConfig.DemisterRangeMultiplier != null
                ? BuildCameraConfig.DemisterRangeMultiplier.Value
                : 1f;
            ApplyRange(state, multiplier);
            ball.transform.position = cameraPosition;
            PruneDestroyedBalls();
        }

        internal static void BeforeRemoveEffects(SE_Demister effect)
        {
            EnsureSubscribed();
            RestoreEffect(effect);
            PruneDestroyedBalls();
        }

        /// <summary>Called from the camera-frame patch so exit restores immediately.</summary>
        internal static void RefreshActiveState()
        {
            EnsureSubscribed();
            bool followEnabled = BuildCameraConfig.DemisterFollowCamera != null &&
                                 BuildCameraConfig.DemisterFollowCamera.Value;
            if (!followEnabled ||
                !BuildCameraRuntime.TryGetActiveContext(out _, out _, out _))
                RestoreAll();
            else
                PruneDestroyedBalls();
        }

        internal static void OnCameraExit() => RestoreAll();

        internal static void Shutdown()
        {
            if (_subscribed)
            {
                BuildCameraConfig.Changed -= OnConfigurationChanged;
                _subscribed = false;
            }
            RestoreAll();
        }

        private static BallState GetOrCaptureBall(GameObject ball)
        {
            int id = ball.GetInstanceID();
            if (BallStates.TryGetValue(id, out BallState existing) &&
                ReferenceEquals(existing.Ball, ball))
                return existing;

            if (existing != null) RestoreState(existing);

            ParticleSystemForceField[] fields;
            try
            {
                fields = ball.GetComponentsInChildren<ParticleSystemForceField>(true);
            }
            catch
            {
                fields = Array.Empty<ParticleSystemForceField>();
            }

            var baselines = new List<ForceFieldBaseline>(fields.Length);
            foreach (ParticleSystemForceField field in fields)
            {
                if (!field) continue;
                baselines.Add(new ForceFieldBaseline(field, field.endRange));
            }

            var captured = new BallState(ball, baselines.ToArray());
            BallStates[id] = captured;
            return captured;
        }

        private static void ApplyRange(BallState state, float multiplier)
        {
            float safeMultiplier = IsFinite(multiplier) && multiplier > 0f
                ? multiplier
                : 1f;
            foreach (ForceFieldBaseline baseline in state.ForceFields)
            {
                ParticleSystemForceField field = baseline.Field;
                if (!field) continue;
                double scaled = (double)baseline.EndRange * safeMultiplier;
                field.endRange = double.IsNaN(scaled) || double.IsInfinity(scaled) ||
                                 scaled > float.MaxValue || scaled < float.MinValue
                    ? baseline.EndRange
                    : (float)scaled;
            }
        }

        private static void RestoreEffect(SE_Demister effect)
        {
            if (effect == null) return;
            int effectId = effect.GetInstanceID();
            if (EffectBallIds.TryGetValue(effectId, out int ballId))
            {
                RestoreBall(ballId);
                EffectBallIds.Remove(effectId);
                return;
            }

            GameObject ball = GetBall(effect);
            if (ball) RestoreBall(ball.GetInstanceID());
        }

        private static void RestoreBall(int ballId)
        {
            if (!BallStates.TryGetValue(ballId, out BallState state)) return;
            RestoreState(state);
            BallStates.Remove(ballId);

            List<int> staleEffects = null;
            foreach (KeyValuePair<int, int> pair in EffectBallIds)
            {
                if (pair.Value != ballId) continue;
                if (staleEffects == null) staleEffects = new List<int>();
                staleEffects.Add(pair.Key);
            }
            if (staleEffects == null) return;
            foreach (int effectId in staleEffects) EffectBallIds.Remove(effectId);
        }

        private static void RestoreState(BallState state)
        {
            foreach (ForceFieldBaseline baseline in state.ForceFields)
            {
                ParticleSystemForceField field = baseline.Field;
                if (field) field.endRange = baseline.EndRange;
            }

            // The ordinary status effect will resume its own smoothing on the next update, but
            // put the existing local ball back on the avatar immediately so focus loss, toggle,
            // or plugin unload cannot leave a visibly detached mist-clearing effect for a frame.
            Player player = Player.m_localPlayer;
            if (state.Ball && player)
                state.Ball.transform.position = player.GetCenterPoint();
        }

        private static void RestoreAll()
        {
            foreach (BallState state in BallStates.Values)
                RestoreState(state);
            BallStates.Clear();
            EffectBallIds.Clear();
        }

        private static void PruneDestroyedBalls()
        {
            List<int> destroyed = null;
            foreach (KeyValuePair<int, BallState> pair in BallStates)
            {
                if (pair.Value.Ball) continue;
                if (destroyed == null) destroyed = new List<int>();
                destroyed.Add(pair.Key);
            }
            if (destroyed == null) return;
            foreach (int ballId in destroyed) RestoreBall(ballId);
        }

        private static GameObject GetBall(SE_Demister effect)
        {
            if (effect == null || BallInstanceField == null ||
                BallInstanceField.FieldType != typeof(GameObject) ||
                BallInstanceField.IsStatic)
                return null;
            try
            {
                return BallInstanceField.GetValue(effect) as GameObject;
            }
            catch
            {
                return null;
            }
        }

        private static void EnsureSubscribed()
        {
            if (_subscribed) return;
            BuildCameraConfig.Changed += OnConfigurationChanged;
            _subscribed = true;
        }

        private static void OnConfigurationChanged() => RestoreAll();

        private static bool IsFinite(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

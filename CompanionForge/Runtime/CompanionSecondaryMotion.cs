using System;
using UnityEngine;

namespace CompanionForge
{
    [Serializable] public sealed class SecondaryMotionSettings
    {
        public bool enabled;
        public float amount = 1f;
        public float maxTravel = .035f;
    }

    // Two visual-only bones. No colliders, physics queries, RPCs or per-frame allocations.
    [DefaultExecutionOrder(100)]
    public sealed class CompanionSecondaryMotion : MonoBehaviour
    {
        private struct Joint
        {
            internal Transform bone;
            internal Vector3 rest, position, previous, velocity;
        }
        private Joint[] joints;
        private SkinnedMeshRenderer[] renderers;
        private CompanionBed rest;
        private float amount, maxTravel, phase;
        private bool initialized;

        internal void Configure(SecondaryMotionSettings settings, CompanionBed bed)
        {
            if (settings == null || !settings.enabled) { enabled = false; return; }
            rest = bed;
            amount = Mathf.Clamp(settings.amount, 0f, 2f);
            maxTravel = Mathf.Clamp(settings.maxTravel, .005f, .10f);
            renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Transform left = null, right = null;
            foreach (var renderer in renderers)
                foreach (var bone in renderer.bones)
                {
                    if (bone == null) continue;
                    if (bone.name == "LeftBreast") left = bone;
                    if (bone.name == "RightBreast") right = bone;
                }
            if (left == null || right == null) { enabled = false; return; }
            joints = new[] { new Joint { bone = left, rest = left.localPosition }, new Joint { bone = right, rest = right.localPosition } };
            ResetPose();
        }

        private void ResetPose()
        {
            if (joints == null) return;
            for (int i = 0; i < joints.Length; i++)
            {
                ref var joint = ref joints[i];
                if (!joint.bone) continue;
                joint.bone.localPosition = joint.rest;
                joint.position = joint.previous = joint.bone.position;
                joint.velocity = Vector3.zero;
            }
            initialized = false;
        }

        // Isolated arithmetic also used by the focused performance/settling check.
        internal static Vector3 Advance(ref Vector3 position, ref Vector3 velocity, Vector3 previous,
            Vector3 target, Vector3 anchor, float dt, float strength, float travel)
        {
            var anchorVelocity = (target - previous) / dt;
            float step = dt * .25f;
            for (int i = 0; i < 4; i++)
            {
                velocity += ((target - position) * 160f - (velocity - anchorVelocity) * 10f) * step;
                position += velocity * step;
            }
            return Vector3.ClampMagnitude((position - anchor) * strength, travel);
        }

        private void LateUpdate()
        {
            if (joints == null) return;
            bool visible = false;
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] && renderers[i].enabled && renderers[i].isVisible) { visible = true; break; }
            float multiplier = Plugin.ChestBounceAmount != null ? Plugin.ChestBounceAmount.Value : 1f;
            Tick(Time.deltaTime, visible, amount * multiplier, rest != null && rest.InHotTub);
        }

        internal void Tick(float dt, bool visible, float strength, bool floating)
        {
            if (joints == null) return;
            if (!visible || dt <= 0 || dt > .1f || strength <= 0)
            { ResetPose(); return; }
            phase = (phase + dt * (Mathf.PI / 3f)) % (2 * Mathf.PI);
            for (int i = 0; i < joints.Length; i++)
            {
                ref var joint = ref joints[i];
                if (!joint.bone || !joint.bone.parent) continue;
                Vector3 anchor = joint.bone.parent.TransformPoint(joint.rest);
                Vector3 target = anchor;
                if (floating) target += Vector3.up * (maxTravel * .55f + Mathf.Min(.003f, maxTravel * .08f) * Mathf.Sin(phase));
                if (!initialized || (target - joint.previous).sqrMagnitude > .25f)
                { joint.position = joint.previous = target; joint.velocity = Vector3.zero; }
                var offset = Advance(ref joint.position, ref joint.velocity, joint.previous, target, anchor, dt, strength, maxTravel);
                joint.previous = target;
                joint.bone.localPosition = joint.rest + joint.bone.parent.InverseTransformVector(offset);
            }
            initialized = true;
        }
        private void OnDisable() { ResetPose(); }
    }
}

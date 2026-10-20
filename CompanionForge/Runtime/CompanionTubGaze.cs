using UnityEngine;

namespace CompanionForge
{
    // Visual-only attention on the existing humanoid Animator. No pose RPCs or bone scaling.
    public sealed class CompanionTubGaze : MonoBehaviour
    {
        private CompanionBed rest;
        private Animator animator;
        private Transform head;
        private Player partner;
        private System.Random random;
        private float nextResolve, weight, yaw, pitch, glanceIn, glanceLeft, glanceYaw;
        private bool wasEngaged;
        private Vector3 lookPoint;
        private Transform attention;
        private float attentionUntil;

        internal void LookBrieflyAt(Transform target, float seconds)
        { attention = target; attentionUntil = Time.time + seconds; }

        internal void Configure(CompanionBed resting)
        {
            rest = resting;
            animator = GetComponent<Animator>();
            if (rest == null || animator == null || !animator.isHuman || animator.avatar == null || !animator.avatar.isValid)
            { enabled = false; return; }
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            random = new System.Random(GetInstanceID());
            enabled = head != null;
        }

        private void Update()
        {
            bool inTub = rest != null && rest.isActiveAndEnabled && rest.InHotTub;
            if (!inTub) { partner = null; nextResolve = 0f; }
            else if (Time.time >= nextResolve)
            {
                partner = rest.GetHotTubPartner();
                nextResolve = Time.time + .25f;
            }
            bool engaged = inTub && partner != null && !partner.IsDead() && partner.IsAttached();
            bool deliberate = engaged && attention != null && Time.time < attentionUntil;
            Tick(Time.deltaTime, engaged, deliberate ? attention.position : engaged ? partner.GetHeadPoint() + Vector3.up * .06f : Vector3.zero, deliberate);
        }

        internal void Tick(float dt, bool engaged, Vector3 eyePoint, bool deliberate = false)
        {
            if (head == null || rest == null) return;
            dt = Mathf.Clamp(dt, 0f, .1f);
            var local = rest.transform.InverseTransformDirection(eyePoint - head.position);
            float targetYaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float targetPitch = -Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg;
            // A seat behind her must not twist her neck around or strain it vertically.
            // The known touching hand can be close to her cheek/shoulder. Aim down toward
            // it within the same neck-turn limit; keep the stricter bounds for ambient gaze.
            engaged &= local.sqrMagnitude > (deliberate ? .0025f : .04f) && local.sqrMagnitude < 25f &&
                Mathf.Abs(targetYaw) < (deliberate ? 160f : 100f) && Mathf.Abs(targetPitch) < (deliberate ? 85f : 65f);
            if (engaged && !wasEngaged) { glanceIn = 4f + (float)random.NextDouble() * 3f; glanceLeft = 0f; }
            if (engaged)
            {
                if (deliberate) { glanceLeft = 0; glanceIn = 3f; }
                if (glanceLeft > 0f) glanceLeft = Mathf.Max(0f, glanceLeft - dt);
                else if ((glanceIn -= dt) <= 0f)
                {
                    glanceLeft = .8f + (float)random.NextDouble() * .6f;
                    glanceIn = 4f + (float)random.NextDouble() * 3f;
                    glanceYaw = (random.Next(2) == 0 ? -1f : 1f) * (10f + (float)random.NextDouble() * 6f);
                }
                if (glanceLeft > 0f) { targetYaw += glanceYaw; targetPitch += 8f; }
                targetYaw = Mathf.Clamp(targetYaw, -60f, 60f);
                targetPitch = Mathf.Clamp(targetPitch, -20f, deliberate ? 35f : 25f);
            }
            else { targetYaw = 0f; targetPitch = 0f; }
            float blend = 1f - Mathf.Exp(-5f * dt);
            yaw = Mathf.Lerp(yaw, targetYaw, blend);
            pitch = Mathf.Lerp(pitch, targetPitch, blend);
            weight = Mathf.MoveTowards(weight, engaged ? .95f : 0f, dt * 2.5f);
            lookPoint = head.position + rest.transform.TransformDirection(Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward) * 2f;
            wasEngaged = engaged;
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (layerIndex != 0 || animator == null || head == null) return;
            // Keep the torso, arms, hands and seated pose authored by the source clip.
            animator.SetLookAtWeight(weight, 0f, 1f, .65f, .5f);
            if (weight > 0f) animator.SetLookAtPosition(lookPoint);
        }

        private void OnDisable()
        {
            weight = yaw = pitch = 0f;
            partner = null;
            attention = null;
            wasEngaged = false;
        }
    }
}

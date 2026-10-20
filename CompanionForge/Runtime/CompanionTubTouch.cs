using UnityEngine;

namespace CompanionForge
{
    // Local visual gesture, after both authored poses and before secondary motion.
    [DefaultExecutionOrder(80)]
    public sealed class CompanionTubTouch : MonoBehaviour
    {
        internal static CompanionTubTouch Active;
        private Player player;
        private CompanionBed rest;
        private Animator playerAnimator, companionAnimator;
        private CompanionTubGaze gaze;
        private Transform attachment, shoulder, knee, thigh;
        private Arm playerArm, companionArm;
        private float elapsed, playerWeight, companionWeight;
        private bool handGlance, posed;
        private Vector3 shoulderPoint, kneePoint;
        private Quaternion thighPose;

        internal static bool OppositeShoulderIsRight(Transform companion, Vector3 playerPosition)
            => Vector3.Dot(playerPosition - companion.position, companion.right) < 0f;

        internal static void Begin(Player player, CompanionBed rest)
        {
            if (player == null || player != Player.m_localPlayer || rest == null || rest.GetHotTubPartner() != player || !player.IsAttached()) return;
            var pa = player.GetComponentInChildren<Animator>();
            var ca = rest.transform.Find("CompanionForgeVisual")?.GetComponentInChildren<Animator>(true);
            if (pa == null || ca == null || !pa.isHuman || !ca.isHuman)
            { player.Message(MessageHud.MessageType.Center, "This gesture needs humanoid arm bones"); return; }
            if (Active != null && Active.enabled && Active.player == player && Active.rest == rest)
            { Active.elapsed = 0; Active.handGlance = false; return; }
            if (Active != null) Active.Stop();
            var touch = pa.GetComponent<CompanionTubTouch>() ?? pa.gameObject.AddComponent<CompanionTubTouch>();
            touch.enabled = false;
            if (!touch.Bind(player, rest, pa, ca))
            { player.Message(MessageHud.MessageType.Center, "Sit in the seat beside her to touch her shoulder"); return; }
            Active = touch; touch.enabled = true;
        }

        internal bool Bind(Player p, CompanionBed b, Animator pa, Animator ca)
        {
            player = p; rest = b; playerAnimator = pa; companionAnimator = ca;
            attachment = p.GetAttachPoint(); gaze = ca.GetComponent<CompanionTubGaze>();
            bool oppositeRight = OppositeShoulderIsRight(b.transform, p.transform.position);
            shoulder = ca.GetBoneTransform(oppositeRight ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm);
            bool playerLeft = Vector3.Dot(b.transform.position - p.transform.position, p.transform.right) < 0;
            playerArm = new Arm(pa, p.transform, playerLeft);
            companionArm = new Arm(ca, b.transform, oppositeRight);
            var leftKnee = pa.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            var rightKnee = pa.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            if (shoulder == null || leftKnee == null || rightKnee == null || !playerArm.Valid || !companionArm.Valid) return false;
            bool left = (leftKnee.position - companionArm.upper.position).sqrMagnitude < (rightKnee.position - companionArm.upper.position).sqrMagnitude;
            knee = left ? leftKnee : rightKnee;
            thigh = pa.GetBoneTransform(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
            if (thigh == null) return false;
            UpdateTargets();
            if (Vector3.Distance(playerArm.upper.position, shoulderPoint) > playerArm.length + .10f) return false;
            elapsed = playerWeight = companionWeight = 0f; handGlance = posed = false;
            return true;
        }

        private void UpdateTargets()
        {
            // Contact on the front of the shoulder, rather than reaching over its top.
            shoulderPoint = shoulder.position + rest.transform.forward * .085f + rest.transform.up * .025f;
            kneePoint = knee.position + player.transform.up * .065f;
        }

        private void Update()
        {
            if (player == null || rest == null || !rest.isActiveAndEnabled || !rest.InHotTub ||
                !player.IsAttached() || player.GetAttachPoint() != attachment || player.IsDead() ||
                (player.transform.position - rest.transform.position).sqrMagnitude > 2.25f)
            { Stop(); return; }
            Tick(Time.deltaTime);
        }

        internal void Tick(float dt)
        {
            elapsed += Mathf.Clamp(dt, 0f, .1f);
            if (elapsed >= 5f) { Stop(); return; }
            playerWeight = Envelope(elapsed, 0f);
            companionWeight = Envelope(elapsed, .35f);
            if (!handGlance && elapsed >= .9f)
            { gaze?.LookBrieflyAt(playerArm.hand, 1.2f); handGlance = true; }
        }

        private static float Envelope(float time, float delay)
        {
            float reach = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((time - delay) / 1.1f));
            float release = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((5f - time) / .9f));
            return reach * release;
        }

        private void LateUpdate() { ApplyPose(); }
        internal void ApplyPose()
        {
            if (!enabled || player == null || rest == null || !playerArm.Valid || !companionArm.Valid || thigh == null) return;
            playerArm.Capture(); companionArm.Capture(); thighPose = thigh.localRotation; posed = true;
            // Angle the nearby knee toward her within the seated leg's normal range.
            // Her spine and both seated roots remain completely under the authored animation.
            Vector3 current = knee.position - thigh.position;
            Vector3 toward = knee.position + (companionArm.upper.position - knee.position).normalized * .24f;
            var turn = Quaternion.RotateTowards(Quaternion.identity, Quaternion.FromToRotation(current, toward - thigh.position), 30f);
            thigh.rotation = Quaternion.Slerp(Quaternion.identity, turn, companionWeight) * thigh.rotation;
            UpdateTargets();
            playerArm.Apply(shoulderPoint, rest.transform.right * (OppositeShoulderIsRight(rest.transform, player.transform.position) ? 1 : -1), -rest.transform.forward, playerWeight);
            companionArm.Apply(kneePoint, player.transform.forward, Vector3.down, companionWeight);
        }

        internal void Stop()
        {
            playerWeight = companionWeight = 0;
            enabled = false;
            if (Active == this) Active = null;
        }
        private void OnDisable()
        {
            if (posed)
            {
                playerArm?.Restore(); companionArm?.Restore();
                if (thigh != null) thigh.localRotation = thighPose;
                posed = false;
            }
            playerWeight = companionWeight = 0;
            if (Active == this) Active = null;
        }
        private void OnDestroy() { if (Active == this) Active = null; }

        private sealed class Arm
        {
            internal readonly Transform upper, hand;
            private readonly Transform root, forearm;
            internal readonly float length;
            private readonly Vector3 fingerAxis, palmAxis;
            private Quaternion upperPose, forearmPose, handPose;
            internal bool Valid => upper != null && forearm != null && hand != null;
            internal Arm(Animator a, Transform r, bool left)
            {
                root = r;
                upper = a.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                forearm = a.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                hand = a.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                if (!Valid) return;
                length = Vector3.Distance(upper.position, forearm.position) + Vector3.Distance(forearm.position, hand.position);
                var middle = a.GetBoneTransform(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
                var index = a.GetBoneTransform(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
                var little = a.GetBoneTransform(left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
                fingerAxis = hand.InverseTransformDirection(middle != null ? middle.position - hand.position : hand.position - forearm.position).normalized;
                palmAxis = index != null && little != null ? hand.InverseTransformDirection(Vector3.Cross(index.position - hand.position, little.position - hand.position) * (left ? 1 : -1)).normalized : Vector3.forward;
            }
            internal void Capture() { upperPose = upper.localRotation; forearmPose = forearm.localRotation; handPose = hand.localRotation; }
            internal void Restore()
            {
                if (!Valid) return;
                upper.localRotation = upperPose; forearm.localRotation = forearmPose; hand.localRotation = handPose;
            }
            internal void Apply(Vector3 contact, Vector3 fingers, Vector3 palmNormal, float weight)
            {
                if (weight <= 0) return;
                fingers = Vector3.ProjectOnPlane(fingers, palmNormal).normalized;
                var desiredRotation = Quaternion.LookRotation(fingers, palmNormal) * Quaternion.Inverse(Quaternion.LookRotation(fingerAxis, palmAxis));
                var wrist = contact - fingers * .075f - palmNormal * .025f;
                wrist = Vector3.Lerp(hand.position, wrist, weight);
                var handRotation = Quaternion.Slerp(hand.rotation, desiredRotation, weight);
                var a = upper.position; float first = Vector3.Distance(a, forearm.position), second = Vector3.Distance(forearm.position, hand.position);
                var delta = wrist - a; float distance = Mathf.Clamp(delta.magnitude, Mathf.Abs(first - second) + .001f, (first + second) * .985f);
                var direction = delta.normalized;
                wrist = a + direction * distance;
                var bend = Vector3.ProjectOnPlane(root.forward * .3f - Vector3.up * .35f, direction).normalized;
                if (bend.sqrMagnitude < .01f) bend = Vector3.ProjectOnPlane(forearm.position - a, direction).normalized;
                float cosine = Mathf.Clamp((first * first + distance * distance - second * second) / (2 * first * distance), -1, 1);
                var elbow = a + direction * (first * cosine) + bend * (first * Mathf.Sqrt(1 - cosine * cosine));
                upper.rotation = Quaternion.FromToRotation(forearm.position - a, elbow - a) * upper.rotation;
                forearm.rotation = Quaternion.FromToRotation(hand.position - forearm.position, wrist - forearm.position) * forearm.rotation;
                hand.rotation = handRotation;
            }
        }
    }
}

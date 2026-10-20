using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CompanionForge
{
    // Native AI and physics continue normally except while this companion is resting.
    public sealed class CompanionBed : MonoBehaviour
    {
        internal static readonly HashSet<CompanionBed> Active = new HashSet<CompanionBed>();
        private const string RestKey = "CF_resting";
        private const string PlayerKey = "CF_restPlayer";
        private const string BedKey = "CF_restBed";
        private const string SeatKey = "CF_restSeat";
        private const string AnimationKey = "CF_restAnimation";
        private const string ReturnKey = "CF_restReturn";
        private ZNetView view;
        private Character character;
        private MonsterAI ai;
        private Rigidbody body;
        private Collider collider;
        internal bool InHotTub { get; private set; }
        private bool applied, aiEnabled, characterEnabled, colliderEnabled, gravity, kinematic;
        private float nextCheck;
        private static readonly int Sleep = Animator.StringToHash("CF_Sleep");
        private static readonly int SitState = Animator.StringToHash("Base Layer.sit");
        private static readonly int SleepState = Animator.StringToHash("Base Layer.sleep");
        private static readonly int Locomotion = Animator.StringToHash("Base Layer.Locomotion");
        private Animator animator;
        private CompanionPose pose;
        private bool hasMenuParameter;
        internal bool Resting => view != null && view.IsValid() && view.GetZDO().GetBool(RestKey);

        internal Player GetHotTubPartner()
        {
            if (!InHotTub || !Resting || ZNetScene.instance == null) return null;
            var partner = ZNetScene.instance.FindInstance(view.GetZDO().GetZDOID(PlayerKey));
            return partner != null ? partner.GetComponent<Player>() : null;
        }

        internal static CompanionBed WithPlayerInTub(Player player)
        {
            if (player == null || !player.IsAttached()) return null;
            foreach (var companion in Active)
                if (companion && companion.isActiveAndEnabled && companion.InHotTub && companion.GetHotTubPartner() == player) return companion;
            return null;
        }

        private void Awake()
        {
            view = GetComponent<ZNetView>(); character = GetComponent<Character>();
            ai = GetComponent<MonsterAI>(); body = GetComponent<Rigidbody>(); collider = GetComponent<Collider>();
            animator = transform.Find("CompanionForgeVisual")?.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.runtimeAnimatorController == null || !Array.Exists(animator.runtimeAnimatorController.animationClips, c => c.name == "sleep")) { enabled = false; return; }
            pose = GetComponent<CompanionPose>();
            hasMenuParameter = Array.Exists(animator.parameters, p => p.name == "CF_Menu");
            if (view != null && view.IsValid()) view.Register<ZDOID, ZDOID, int>("CF_Bed", OnBedRequest);
        }
        private void Start() { if (view != null && view.IsValid()) Active.Add(this); }
        private bool Follows(Player player)
        {
            return player != null && ((ai != null && ai.GetFollowTarget() == player.gameObject) ||
                view.GetZDO().GetString(ZDOVars.s_follow) == player.GetPlayerName());
        }
        internal static void Join(Player player, Bed bed)
        {
            if (player != Player.m_localPlayer || bed == null || !player.InBed()) return;
            var bedView = bed.GetComponent<ZNetView>(); if (bedView == null || !bedView.IsValid()) return;
            CompanionBed nearest = null; float distance = 8f * 8f;
            foreach (var companion in Active)
            {
                if (!companion || !companion.isActiveAndEnabled || !companion.view.IsValid() || !companion.Follows(player) || companion.Resting) continue;
                float d = (companion.transform.position - bed.transform.position).sqrMagnitude;
                if (d < distance) { distance = d; nearest = companion; }
            }
            if (nearest != null) nearest.view.InvokeRPC("CF_Bed", player.GetZDOID(), bedView.GetZDO().m_uid, -1);
        }
        internal static void Leave(Player player)
        {
            if (player != Player.m_localPlayer) return;
            foreach (var companion in Active)
                if (companion && companion.Resting && companion.view.GetZDO().GetZDOID(PlayerKey) == player.GetZDOID())
                    companion.view.InvokeRPC("CF_Bed", player.GetZDOID(), ZDOID.None, -1);
        }
        private static bool SeatTaken(ZDOID target, int seat)
        {
            foreach (var companion in Active)
                if (companion && companion.isActiveAndEnabled && companion.Resting &&
                    companion.view.GetZDO().GetZDOID(BedKey) == target && companion.view.GetZDO().GetInt(SeatKey, -1) == seat) return true;
            return false;
        }
        private static bool IsTub(Piece piece)
        {
            if (piece == null) return false;
            string name = Utils.GetPrefabName(piece.gameObject) + " " + piece.m_name;
            return name.IndexOf("bathtub", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("hottub", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        internal static void JoinTub(Player player, Transform point)
        {
            if (player != Player.m_localPlayer || point == null || !player.IsAttached()) return;
            var chair = point.GetComponentInParent<Chair>();
            var piece = chair != null ? chair.GetComponentInParent<Piece>() : null;
            if (!IsTub(piece)) return;
            var target = piece.GetComponent<ZNetView>(); if (target == null || !target.IsValid()) return;
            var chairs = piece.GetComponentsInChildren<Chair>(); int seat = -1; float nearestSeat = float.PositiveInfinity;
            for (int i=0; i<chairs.Length; i++)
                if (chairs[i] != chair && chairs[i].m_attachPoint != null && !chairs[i].IsInUse() && !SeatTaken(target.GetZDO().m_uid, i))
                {
                    float distanceToSeat = (chairs[i].m_attachPoint.position - point.position).sqrMagnitude;
                    if (distanceToSeat < nearestSeat) { nearestSeat = distanceToSeat; seat = i; }
                }
            if (seat < 0) return;
            CompanionBed nearest = null; float distance = 64f;
            foreach (var companion in Active)
            {
                if (!companion || !companion.isActiveAndEnabled || !companion.view.IsValid() || !companion.Follows(player) || companion.Resting) continue;
                float d = (companion.transform.position-piece.transform.position).sqrMagnitude;
                if (d < distance) { nearest = companion; distance = d; }
            }
            if (nearest != null) nearest.view.InvokeRPC("CF_Bed", player.GetZDOID(), target.GetZDO().m_uid, seat);
        }
        private void OnBedRequest(long sender, ZDOID playerId, ZDOID bedId, int seat)
        {
            if (!view.IsValid() || !view.IsOwner() || ZNetScene.instance == null) return;
            var playerObject = ZNetScene.instance.FindInstance(playerId);
            var player = playerObject != null ? playerObject.GetComponent<Player>() : null;
            var playerView = player != null ? player.GetComponent<ZNetView>() : null;
            if (playerView == null || !playerView.IsValid() || playerView.GetZDO().GetOwner() != sender) return;
            if (bedId == ZDOID.None)
            {
                if (view.GetZDO().GetZDOID(PlayerKey) == playerId) Wake();
                return;
            }
            var bedObject = ZNetScene.instance.FindInstance(bedId);
            var bed = bedObject != null ? bedObject.GetComponent<Bed>() : null;
            if (seat >= 0)
            {
                var piece = bedObject != null ? bedObject.GetComponent<Piece>() : null;
                var chairs = piece != null ? piece.GetComponentsInChildren<Chair>() : Array.Empty<Chair>();
                if (!IsTub(piece) || seat >= chairs.Length || chairs[seat].m_attachPoint == null || chairs[seat].IsInUse() || SeatTaken(bedId, seat) ||
                    !Follows(player) || character.IsDead() || (ai != null && ai.IsAlerted()) ||
                    (transform.position-piece.transform.position).sqrMagnitude > 64f ||
                    (player.transform.position-piece.transform.position).sqrMagnitude > 25f) return;
                GetComponent<CompanionPose>()?.End();
                var data = view.GetZDO(); if (!Resting) data.Set(ReturnKey, transform.position);
                data.Set(PlayerKey, playerId); data.Set(BedKey, bedId); data.Set(SeatKey, seat);
                data.Set(AnimationKey, chairs[seat].m_attachAnimation); data.Set(RestKey, true);
                nextCheck = Time.time+.5f; Apply(true); transform.SetPositionAndRotation(chairs[seat].m_attachPoint.position, chairs[seat].m_attachPoint.rotation);
                return;
            }
            if (bed == null || bed.m_spawnPoint == null || !Follows(player) || character.IsDead() ||
                (ai != null && ai.IsAlerted()) || (transform.position - bed.transform.position).sqrMagnitude > 64f ||
                (player.transform.position - bed.m_spawnPoint.position).sqrMagnitude > 9f) return;
            // One owner-directed transition. No ownership handoff, forced save, or player wait.
            GetComponent<CompanionPose>()?.End();
            var zdo = view.GetZDO(); zdo.Set(SeatKey, -1);
            if (!Resting) zdo.Set(ReturnKey, transform.position);
            zdo.Set(PlayerKey, playerId); zdo.Set(BedKey, bedId); zdo.Set(RestKey, true);
            nextCheck = Time.time + .5f;
            Apply(true); Place(bed);
        }
        private void Place(Bed bed)
        {
            transform.SetPositionAndRotation(bed.m_spawnPoint.position + bed.m_spawnPoint.right * .48f,
                bed.m_spawnPoint.rotation);
        }
        private void LateUpdate()
        {
            if (view == null || !view.IsValid()) return;
            bool resting = Resting;
            if (resting != applied || (resting && InHotTub != (view.GetZDO().GetInt(SeatKey, -1) >= 0))) Apply(resting);
            if (!resting || !view.IsOwner() || Time.time < nextCheck) return;
            nextCheck = Time.time + .1f;
            var zdo = view.GetZDO();
            var playerObject = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(zdo.GetZDOID(PlayerKey)) : null;
            var bedObject = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(zdo.GetZDOID(BedKey)) : null;
            var player = playerObject != null ? playerObject.GetComponent<Player>() : null;
            var bed = bedObject != null ? bedObject.GetComponent<Bed>() : null;
            if (zdo.GetInt(SeatKey, -1) >= 0)
            {
                var chairs = bedObject != null ? bedObject.GetComponentsInChildren<Chair>() : Array.Empty<Chair>();
                int seat = zdo.GetInt(SeatKey, -1);
                var pa = player != null ? player.GetComponentInChildren<Animator>() : null;
                if (player == null || player.IsDead() || character.IsDead() || seat >= chairs.Length ||
                    chairs[seat].m_attachPoint == null || chairs[seat].IsInUse() || pa == null ||
                    !pa.GetBool(zdo.GetString(AnimationKey, "attach_chair")) ||
                    (player.transform.position-bedObject.transform.position).sqrMagnitude > 25f) { Wake(); return; }
                var point = chairs[seat].m_attachPoint; transform.SetPositionAndRotation(point.position, point.rotation);
                return;
            }
            if (player == null || !player.InBed() || player.IsDead() || character.IsDead() || bed == null || bed.m_spawnPoint == null)
            { Wake(); return; }
            Place(bed);
        }
        private void Wake()
        {
            if (!view.IsValid() || !view.IsOwner() || !Resting) return;
            var zdo = view.GetZDO();
            transform.position = zdo.GetVec3(ReturnKey, transform.position);
            zdo.Set(RestKey, false); zdo.Set(PlayerKey, ZDOID.None); zdo.Set(BedKey, ZDOID.None);
            Apply(false);
        }
        private void Apply(bool resting)
        {
            bool sitting = resting && view.GetZDO().GetInt(SeatKey, -1) >= 0;
            if (resting == applied && sitting == InHotTub) return;
            if (resting) pose?.ReleaseLocalPose();
            // A change between tub and bed changes only the animation. Do not recapture
            // the disabled movement state, which must be restored when she gets up.
            if (resting && !applied)
            {
                aiEnabled = ai != null && ai.enabled; characterEnabled = character.enabled;
                colliderEnabled = collider != null && collider.enabled;
                if (body != null) { gravity = body.useGravity; kinematic = body.isKinematic; body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; body.useGravity = false; body.isKinematic = true; }
                if (ai != null) ai.enabled = false; character.enabled = false;
                if (collider != null) collider.enabled = false;
            }
            else if (!resting && applied)
            {
                if (body != null) { body.isKinematic = kinematic; body.useGravity = gravity; }
                if (collider != null) collider.enabled = colliderEnabled;
                character.enabled = characterEnabled; if (ai != null) ai.enabled = aiEnabled;
                character.SetMoveDir(Vector3.zero);
            }
            InHotTub = sitting;
            if (animator != null)
            {
                animator.SetBool("CF_Pose", false);
                if (hasMenuParameter) animator.SetBool("CF_Menu", false);
                animator.SetBool("CF_Attack", false); animator.ResetTrigger("CF_Wave");
                animator.SetFloat("CF_Speed", 0f);
                animator.SetBool(Sleep, resting && !sitting); animator.SetBool("CF_Sit", sitting);
                int state = resting ? (sitting ? SitState : SleepState) : Locomotion;
                // Fixed-time transitions also release held clips without waiting for clip time.
                if (animator.HasState(0, state)) animator.CrossFadeInFixedTime(state, .12f, 0, 0f);
            }
            applied = resting;
        }
        private void OnDestroy() { Active.Remove(this); }
        private void OnDisable() { if (applied) Apply(false); }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.AttachStart))]
    static class BedJoinPatch
    {
        static void Postfix(Player __instance, GameObject colliderRoot, bool isBed, Transform attachPoint)
        {
            if (isBed && colliderRoot != null) CompanionBed.Join(__instance, colliderRoot.GetComponent<Bed>());
            else if (!isBed) CompanionBed.JoinTub(__instance, attachPoint);
        }
    }
    [HarmonyPatch(typeof(Player), nameof(Player.AttachStop))]
    static class BedLeavePatch
    {
        static void Postfix(Player __instance) { if (!__instance.InBed()) CompanionBed.Leave(__instance); }
    }
}

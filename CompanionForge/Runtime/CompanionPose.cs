using System;
using System.Collections.Generic;
using UnityEngine;

namespace CompanionForge
{
    public sealed class CompanionPose : MonoBehaviour
    {
        private static readonly HashSet<CompanionPose> Active = new HashSet<CompanionPose>();
        private static readonly int Locomotion = Animator.StringToHash("Base Layer.Locomotion");
        private const string PoseKey="CF_pose", ClipKey="CF_poseClip", UntilKey="CF_poseUntil";
        private const string StartedKey="CF_poseStarted", PlayerKey="CF_posePlayer", FacingKey="CF_poseFacing";
        private readonly Dictionary<string,AnimationClip> clips=new Dictionary<string,AnimationClip>();
        private readonly Dictionary<string,MenuAnimation> entries=new Dictionary<string,MenuAnimation>();
        private MenuAnimation[] authoredMenu=Array.Empty<MenuAnimation>();
        private bool reportedMissingMenu;
        private ZNetView view;
        private Character character;
        private MonsterAI ai;
        private Animator animator;
        private CompanionBed rest;
        private bool applied, aiEnabled, hasMenuParameter;
        private float startingHealth, nextDistanceCheck;
        private string appliedClip;
        private long appliedStarted;
        internal MenuAnimation[] Menu { get; private set; }=Array.Empty<MenuAnimation>();
        internal string DisplayName => character != null ? character.GetHoverName() : "Companion";
        internal bool Performing => view != null && view.IsValid() && view.GetZDO().GetBool(PoseKey);
        private void Awake()
        {
            view=GetComponent<ZNetView>(); character=GetComponent<Character>(); ai=GetComponent<MonsterAI>();
            rest=GetComponent<CompanionBed>();
            animator=transform.Find("CompanionForgeVisual")?.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.runtimeAnimatorController == null) { enabled=false; return; }
        }
        private void BuildMenu()
        {
            // Runtime-loaded mod fields do not survive player prefab serialization.
            // Resolve once from the startup cache; never read files on menu interaction.
            authoredMenu=Plugin.Packs.TryGetValue(Utils.GetPrefabName(gameObject),out var pack)
                ? pack.animationMenu ?? Array.Empty<MenuAnimation>() : Array.Empty<MenuAnimation>();
            clips.Clear(); entries.Clear();
            if (animator==null || animator.runtimeAnimatorController==null) return;
            foreach (var clip in animator.runtimeAnimatorController.animationClips) if (clip!=null) clips[clip.name]=clip;
            hasMenuParameter=Array.Exists(animator.parameters,p=>p.name=="CF_Menu");
            var available=new List<MenuAnimation>();
            foreach (var item in authoredMenu)
                if (item != null && !string.IsNullOrEmpty(item.id) && clips.ContainsKey("menu_"+item.id))
                { entries[item.id]=item; available.Add(item); }
            Menu=available.ToArray();
        }
        internal bool EnsureMenu()
        {
            if (Menu.Length>0) return true;
            BuildMenu(); // Local cached assets only, on the first request after a failed initialization.
            if (Menu.Length>0) return true;
            if (!reportedMissingMenu)
            {
                reportedMissingMenu=true;
                Debug.LogWarning("[Companion Forge] Animation menu unavailable for "+gameObject.name+
                    ": authored="+authoredMenu.Length+", loaded clips="+clips.Count+", animator="+(animator!=null));
            }
            return false;
        }
        private void Start()
        {
            BuildMenu();
            if (Menu.Length == 0 && !clips.ContainsKey("pose")) { enabled=false; return; }
            if (!enabled || view == null || !view.IsValid()) return;
            // Network view Awake must finish before registering the two existing commands.
            view.Register<ZDOID>("CF_Pose",(sender,player)=>Request(sender,player,"pose"));
            view.Register<ZDOID,string>("CF_Animation",Request);
            Active.Add(this);
            // A failed menu in 1.2.0 could persist the obsolete fallback pose.
            if (Menu.Length>0 && view.IsOwner() && Performing && view.GetZDO().GetString(ClipKey,"pose")=="pose") End();
        }
        private bool Follows(Player player) => player != null && ((ai != null && ai.GetFollowTarget()==player.gameObject) || view.GetZDO().GetString(ZDOVars.s_follow)==player.GetPlayerName());
        internal static CompanionPose Nearest(Player player)
        {
            CompanionPose nearest=null, performing=null; float distance=64f, performingDistance=64f;
            foreach (var pose in Active)
            {
                if (!pose || !pose.isActiveAndEnabled || !pose.view.IsValid() || !pose.Follows(player) || (pose.rest != null && pose.rest.Resting)) continue;
                float d=(pose.transform.position-player.transform.position).sqrMagnitude;
                if (d<distance) { nearest=pose; distance=d; }
                if (pose.Performing && d<performingDistance) { performing=pose; performingDistance=d; }
            }
            return performing != null ? performing : nearest;
        }
        internal bool CanBrowse(Player player) => player != null && view != null && view.IsValid() && !character.IsDead() && Follows(player) && (rest==null || !rest.Resting) && (transform.position-player.transform.position).sqrMagnitude<=64f;
        internal void Select(Player player,string id)
        {
            if (CanBrowse(player)) view.InvokeRPC("CF_Animation",player.GetZDOID(),id);
        }
        private void Request(long sender,ZDOID playerId,string id)
        {
            if (!view.IsValid() || !view.IsOwner() || ZNetScene.instance==null) return;
            var obj=ZNetScene.instance.FindInstance(playerId); var player=obj != null ? obj.GetComponent<Player>() : null;
            var pv=player != null ? player.GetComponent<ZNetView>() : null;
            if (pv==null || !pv.IsValid() || pv.GetZDO().GetOwner()!=sender || !CanBrowse(player)) return;
            if (id=="") { End(); return; }
            if (character.InAttack() || character.IsStaggering() || (ai != null && ai.IsAlerted())) return;
            MenuAnimation entry=null; string clipName;
            if (id=="pose" && clips.ContainsKey("pose")) clipName="pose";
            else if (entries.TryGetValue(id,out entry)) clipName="menu_"+id;
            else return;
            var clip=clips[clipName]; var data=view.GetZDO();
            var facing=player.transform.position-transform.position; facing.y=0;
            if (facing.sqrMagnitude<.01f) facing=transform.forward;
            long now=(long)(ZNet.instance.GetTimeSeconds()*1000.0);
            data.Set(FacingKey,facing.normalized);
            data.Set(ClipKey,clipName); data.Set(StartedKey,now); data.Set(PlayerKey,playerId);
            data.Set(UntilKey,entry != null && (entry.loop || entry.hold) ? 0L : now+(long)(Mathf.Max(.1f,clip.length)*1000));
            data.Set(PoseKey,true);
            Apply(true); // One owner-directed command; no ownership handoff or retry.
        }
        internal void End()
        {
            if (view != null && view.IsValid() && view.IsOwner()) view.GetZDO().Set(PoseKey,false);
            ReleaseLocalPose();
        }
        // Rest releases the local menu pose before capturing AI state, on owners and observers.
        internal void ReleaseLocalPose()
        {
            if (applied) Apply(false);
            else ReleaseAnimation(); // Stop also recovers a visual pose whose saved flag is already clear.
        }
        private void ReleaseAnimation()
        {
            if (animator == null) return;
            animator.SetBool("CF_Pose",false);
            if (hasMenuParameter) animator.SetBool("CF_Menu",false);
            // A menu Stop returns to standing, while actual bed/tub attachment owns its pose.
            if (rest != null && rest.Resting) return;
            animator.SetBool("CF_Sit",false); animator.SetBool("CF_Sleep",false);
            if (animator.HasState(0,Locomotion)) animator.CrossFadeInFixedTime(Locomotion,.12f,0);
        }
        private void LateUpdate()
        {
            if (view==null || !view.IsValid()) return;
            var data=view.GetZDO(); bool posing=data.GetBool(PoseKey);
            if (rest != null && rest.Resting)
            {
                if (posing && view.IsOwner()) End();
                else if (applied) Apply(false);
                return;
            }
            string clip=data.GetString(ClipKey,"pose"); long started=data.GetLong(StartedKey);
            if (posing!=applied || (posing && (clip!=appliedClip || started!=appliedStarted))) Apply(posing);
            if (!posing || !view.IsOwner()) return;
            long until=data.GetLong(UntilKey);
            if ((until>0 && ZNet.instance.GetTimeSeconds()*1000.0>=until) || character.IsDead() || character.IsStaggering() || character.GetHealth()<startingHealth || character.InAttack() || (ai != null && ai.IsAlerted()) || (rest != null && rest.Resting)) { End(); return; }
            if (Time.time>=nextDistanceCheck)
            {
                nextDistanceCheck=Time.time+.5f;
                var obj=ZNetScene.instance.FindInstance(data.GetZDOID(PlayerKey));
                var player=obj != null ? obj.GetComponent<Player>() : null;
                if (!Follows(player) || (transform.position-player.transform.position).sqrMagnitude>100f) { End(); return; }
            }
            character.SetMoveDir(Vector3.zero); character.SetRun(false);
            var facing=data.GetVec3(FacingKey,transform.forward); character.SetLookDir(facing);
            transform.rotation=Quaternion.RotateTowards(transform.rotation,Quaternion.LookRotation(facing),450f*Time.deltaTime);
        }
        private void Apply(bool posing)
        {
            if (posing)
            {
                var data=view.GetZDO(); string clipName=data.GetString(ClipKey,"pose");
                if (!clips.TryGetValue(clipName,out var clip)) return;
                if (!applied) { aiEnabled=ai != null && ai.enabled; if (ai != null) ai.enabled=false; }
                startingHealth=character.GetHealth(); character.SetMoveDir(Vector3.zero);
                appliedClip=clipName; appliedStarted=data.GetLong(StartedKey);
                double elapsed=Math.Max(0,ZNet.instance.GetTimeSeconds()-appliedStarted/1000.0);
                bool hold=clipName.StartsWith("menu_",StringComparison.Ordinal) && entries.TryGetValue(clipName.Substring(5),out var item) && item.hold;
                animator.SetBool("CF_Pose",true); animator.SetBool("CF_Attack",false); animator.SetFloat("CF_Speed",0f);
                if (hasMenuParameter) animator.SetBool("CF_Menu",clipName!="pose");
                animator.CrossFade(clipName,.12f,0,hold ? 0f : (float)(elapsed/Math.Max(.1f,clip.length)));
            }
            else { ReleaseAnimation(); if (ai != null) ai.enabled=aiEnabled; }
            applied=posing;
        }
        private void OnDisable() { if (applied) Apply(false); Active.Remove(this); }
        private void OnDestroy() { Active.Remove(this); }
    }
}

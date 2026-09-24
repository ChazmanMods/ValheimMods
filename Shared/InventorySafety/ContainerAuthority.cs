using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace RunicAutomation
{
    public static class ContainerAuthority
    {

        private static Dictionary<string, Delegate> Routes => SharedState.Get("authority", () => new Dictionary<string, Delegate>
        {
            ["RegisterPolicy"] = new Action<string, Func<Container, ZDOID, long, long, bool>, Func<Container, bool>>(RegisterPolicyCore),
            ["UnregisterPolicy"] = new Action<string>(UnregisterPolicyCore),
            ["Register"] = new Action<Container>(RegisterCore),
            ["Remove"] = new Action<Container>(RemoveCore),
            ["Clear"] = new Action(ClearCore),
            ["TryAcquire"] = new Func<Container, string, ZDOID, long, bool, bool>(TryAcquireCore),
        });
        public static void RegisterPolicy(string name, Func<Container, ZDOID, long, long, bool> allows, Func<Container, bool> refresh) => ((Action<string, Func<Container, ZDOID, long, long, bool>, Func<Container, bool>>)Routes["RegisterPolicy"])(name, allows, refresh);
        public static void UnregisterPolicy(string name) => ((Action<string>)Routes["UnregisterPolicy"])(name);
        public static void Register(Container chest) => ((Action<Container>)Routes["Register"])(chest);
        public static void Remove(Container chest) => ((Action<Container>)Routes["Remove"])(chest);
        public static void Clear() => ((Action)Routes["Clear"])();
        public static bool TryAcquire(Container chest, string policy, ZDOID context, long principal, bool allowInUse = false) => ((Func<Container, string, ZDOID, long, bool, bool>)Routes["TryAcquire"])(chest, policy, context, principal, allowInUse);

        private const string Rpc = "RunicAutomation_Handoff_v1";
        private const string Receipt = "runic.automation.handoff-receipt";
        private sealed class Policy
        {
            public Func<Container, ZDOID, long, long, bool> Allows;
            public Func<Container, bool> Refresh;
        }
        private sealed class Pending
        {
            public string Policy, Nonce;
            public ZDOID Context;
            public long Principal, Owner;
            public float Retry, Expires, HoldUntil, ReceiveAfter, ContentionUntil, ContentionBackoff;
        }
        private static readonly Dictionary<string, Policy> Policies = new Dictionary<string, Policy>();
        private static readonly Dictionary<Container, Pending> States = new Dictionary<Container, Pending>();
        private static readonly HashSet<Pending> Outstanding = new HashSet<Pending>();
        private static readonly System.Reflection.FieldInfo ViewField = AccessTools.Field(typeof(Container), "m_nview");
        private static int _frame, _requests;
        private static void RegisterPolicyCore(string name, Func<Container, ZDOID, long, long, bool> allows,
            Func<Container, bool> refresh)
        { Policies[name] = new Policy { Allows = allows, Refresh = refresh }; }
        private static void UnregisterPolicyCore(string name) => Policies.Remove(name);
        public static ZNetView View(Container chest) => chest == null ? null :
            chest.m_rootObjectOverride != null ? chest.m_rootObjectOverride : (ZNetView)ViewField.GetValue(chest);
        public static bool Blocked(Container chest)
        {
            ZDO zdo = View(chest)?.GetZDO();
            return chest == null || MutationGate.IsBlocked(chest.GetInventory()) ||
                zdo != null && (MutationGate.IsBlocked(zdo.m_uid) || MutationGate.IsBlocked("valheim.zdo:" + zdo.m_uid));
        }
        public static bool Writable(Container chest, bool allowInUse = false) =>
            chest != null && chest.isActiveAndEnabled && !Blocked(chest) &&
            (allowInUse || !chest.IsInUse() && View(chest)?.GetZDO()?.GetInt(ZDOVars.s_inUse, 0) == 0) &&
            (chest.m_wagon == null || !chest.m_wagon.InUse());
        private static void RegisterCore(Container chest)
        {
            if (chest == null || States.ContainsKey(chest) || States.Count >= 16384) return;
            ZNetView view = View(chest);
            if (view == null || !view.IsValid()) return;
            var state = new Pending();
            States.Add(chest, state);
            view.Register<string, ZDOID, long, string>(Rpc,
                (sender, policy, context, principal, nonce) => Receive(chest, state, sender, policy, context, principal, nonce));
            chest.gameObject.AddComponent<AuthorityLifetime>().Chest = chest;
        }
        private static void RemoveCore(Container chest)
        {
            if (!ReferenceEquals(chest, null) && States.TryGetValue(chest, out Pending state))
            { Outstanding.Remove(state); States.Remove(chest); }
        }
        private static void ClearCore() { States.Clear(); Outstanding.Clear(); _requests = 0; _frame = -1; }

        private static bool TryAcquireCore(Container chest, string policy, ZDOID context, long principal, bool allowInUse = false)
        {
            if (ZNet.instance == null || !Writable(chest, allowInUse) ||
                !Policies.TryGetValue(policy, out Policy rules)) return false;
            Register(chest);
            if (!States.TryGetValue(chest, out Pending pending)) return false;
            ZNetView view = View(chest);
            ZDO zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            if (zdo == null || !rules.Allows(chest, context, principal, ZNet.GetUID()))
                return false;
            float now = Time.realtimeSinceStartup;
            if (pending.Nonce != null && (pending.Context != context || pending.Principal != principal || pending.Policy != policy))
            {
                // A different intent cannot erase an unfinished ownership/data synchronization fence.
                if (view.IsOwner() && zdo.GetString(Receipt, "") != pending.Nonce) return false;
                pending.Nonce = null;
                pending.Retry = 0;
            }
            if (view.IsOwner())
            {
                if (pending.Nonce != null && zdo.GetString(Receipt, "") != pending.Nonce) return false;
                if (pending.Nonce != null)
                {
                    MutationGate.Report((MutationGate.Current?.Id ?? "readiness") + " handoff-receipt=" + pending.Nonce + " endpoint=" + zdo.m_uid);
                    pending.HoldUntil = now + 2.5f; pending.Nonce = null; Outstanding.Remove(pending);
                }
                return zdo.GetOwner() == ZNet.GetUID();
            }
            long owner = zdo.GetOwner();
            if (owner == 0 || ZDOMan.instance == null || !Writable(chest)) return false;
            if (pending.Owner != owner) { pending.Nonce = null; pending.Retry = 0; }
            if (now < pending.Retry) return false;
            Outstanding.RemoveWhere(candidate => candidate.Nonce == null || now >= candidate.Expires);
            if (!Outstanding.Contains(pending) && Outstanding.Count >= 256) return false;
            if (_frame != Time.frameCount) { _frame = Time.frameCount; _requests = 0; }
            if (_requests >= 32) return false;
            _requests++;
            if (pending.Nonce == null || now >= pending.Expires)
            { pending.Nonce = Guid.NewGuid().ToString("N"); pending.Expires = now + 12f; }
            pending.Owner = owner; pending.Policy = policy; pending.Context = context; pending.Principal = principal;
            pending.Retry = now + 3f;
            Outstanding.Add(pending);
            ZDOMan.instance.ForceSendZDO(owner, context);
            view.InvokeRPC(owner, Rpc, policy, context, principal, pending.Nonce);
            return false;
        }

        private static void Receive(Container chest, Pending pending, long sender, string policy, ZDOID context, long principal, string nonce)
        {
            try
            {
                if (MutationGate.Current != null || !Writable(chest) || sender == 0 || sender == ZNet.GetUID() ||
                    context.IsNone() || nonce == null || nonce.Length != 32 || !Guid.TryParseExact(nonce, "N", out var token) ||
                    token == Guid.Empty || token.ToString("N") != nonce ||
                    policy == null || policy.Length > 32 || !Policies.TryGetValue(policy, out Policy rules)) return;
                ZNetView view = View(chest);
                if (view == null || !view.IsValid() || !view.IsOwner()) return;
                if (pending.Nonce != null)
                {
                    if (view.GetZDO().GetString(Receipt, "") != pending.Nonce) return;
                    pending.Nonce = null;
                    Outstanding.Remove(pending);
                }
                float now = Time.realtimeSinceStartup;
                if (now < pending.HoldUntil || now < pending.ReceiveAfter) return;
                pending.ReceiveAfter = now + 0.25f;
                // Prefer the lower-UID collector briefly; never hold another station indefinitely.
                bool collecting = false;
                foreach (var candidate in Outstanding)
                    if (candidate.Nonce != null && now < candidate.Retry && now < candidate.Expires)
                    { collecting = true; break; }
                if (collecting && sender > ZNet.GetUID())
                {
                    if (now >= pending.ContentionBackoff)
                    { pending.ContentionUntil = now + 4f; pending.ContentionBackoff = now + 10f; }
                    if (now < pending.ContentionUntil) return;
                }
                if (!rules.Allows(chest, context, principal, sender) || !rules.Refresh(chest) ||
                    !view.IsOwner() || !Writable(chest) || !rules.Allows(chest, context, principal, sender) ||
                    ZDOMan.instance == null) return;
                ZDO zdo = view.GetZDO();
                zdo.Set(Receipt, nonce);
                zdo.SetOwner(sender);
                ZDOMan.instance.ForceSendZDO(sender, zdo.m_uid);
                pending.HoldUntil = now + 3f;
                MutationGate.Report(nonce + " handoff " + zdo.m_uid + " -> " + sender);
            }
            catch (Exception error) { MutationGate.Report("handoff deferred: " + error.Message); }
        }

        public static Player FindPlayer(ZDOID context, long principal, long sender)
        {
            GameObject obj = ZNetScene.instance?.FindInstance(context);
            Player player = obj == null ? null : obj.GetComponent<Player>();
            ZNetView view = obj == null ? null : obj.GetComponent<ZNetView>();
            return player != null && view != null && view.IsValid() && view.GetZDO().GetOwner() == sender &&
                player.GetPlayerID() == principal ? player : null;
        }
        public static ZDOID PlayerContext(Player player) => player == null ? ZDOID.None :
            player.GetComponent<ZNetView>()?.GetZDO()?.m_uid ?? ZDOID.None;

        public static bool PlayerAccess(Container chest, ZDOID context, long principal, long sender, float range, bool requireWard)
        {
            Player player = FindPlayer(context, principal, sender);
            if (player == null || chest == null || range <= 0 || float.IsNaN(range) ||
                (player.transform.position - chest.transform.position).sqrMagnitude > range * range) return false;
            var access = AccessTools.Method(typeof(Container), "CheckAccess");
            if (access == null || !(bool)access.Invoke(chest, new object[] { principal })) return false;
            if (!requireWard && !chest.m_checkGuardStone) return true;
            var areas = AccessTools.Field(typeof(PrivateArea), "m_allAreas").GetValue(null) as List<PrivateArea>;
            if (areas == null) return false;
            foreach (PrivateArea ward in areas)
            {
                if (ward == null || !(bool)AccessTools.Method(typeof(PrivateArea), "IsEnabled").Invoke(ward, null) ||
                    !(bool)AccessTools.Method(typeof(PrivateArea), "IsInside").Invoke(ward, new object[] { chest.transform.position, 0f })) continue;
                Piece piece = ward.GetComponent<Piece>();
                if (piece == null || piece.GetCreator() == 0) return false;
                if (piece.GetCreator() != principal &&
                    !(bool)AccessTools.Method(typeof(PrivateArea), "IsPermitted").Invoke(ward, new object[] { principal })) return false;
            }
            return true;
        }
    }
    public sealed class AuthorityLifetime : MonoBehaviour
    {
        public Container Chest;
        private void OnDestroy() => ContainerAuthority.Remove(Chest);
    }
}

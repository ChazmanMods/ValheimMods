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
            ["UnregisterPolicy"] = new Action<string>(name => Policies.Remove(name)),
            ["TryAcquire"] = new Func<Container, string, ZDOID, long, bool, bool>(TryAcquireCore)
        });
        public static void RegisterPolicy(string name, Func<Container, ZDOID, long, long, bool> allows, Func<Container, bool> refresh) =>
            ((Action<string, Func<Container, ZDOID, long, long, bool>, Func<Container, bool>>)Routes["RegisterPolicy"])(name, allows, refresh);
        public static void UnregisterPolicy(string name) => ((Action<string>)Routes["UnregisterPolicy"])(name);
        // Existing module callers may register chests; native ownership needs no per-chest RPC or component.
        public static void Register(Container chest) { }
        public static void Clear() { }
        public static bool TryAcquire(Container chest, string policy, ZDOID context, long principal, bool allowInUse = false) =>
            ((Func<Container, string, ZDOID, long, bool, bool>)Routes["TryAcquire"])(chest, policy, context, principal, allowInUse);
        private sealed class Policy
        {
            public Func<Container, ZDOID, long, long, bool> Allows;
            public Func<Container, bool> Refresh;
        }
        private static readonly Dictionary<string, Policy> Policies = new Dictionary<string, Policy>();
        private static readonly System.Reflection.FieldInfo ViewField = AccessTools.Field(typeof(Container), "m_nview");
        private static void RegisterPolicyCore(string name, Func<Container, ZDOID, long, long, bool> allows, Func<Container, bool> refresh) =>
            Policies[name] = new Policy { Allows = allows, Refresh = refresh };
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
        public static string Describe(Container chest) => chest == null ? "container=missing" :
            "endpoint=" + View(chest)?.GetZDO()?.m_uid + "; nativeOwner=" + View(chest)?.IsOwner() +
            "; blocked=" + Blocked(chest) + "; inUse=" + chest.IsInUse();
        private static bool TryAcquireCore(Container chest, string policy, ZDOID context, long principal, bool allowInUse = false)
        {
            if (ZNet.instance == null || !Writable(chest, allowInUse) ||
                !Policies.TryGetValue(policy, out Policy rules)) return false;
            var view = View(chest);
            var zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            if (zdo == null || !rules.Allows(chest, context, principal, ZNet.GetUID())) return false;

            // Foreground actions use native ownership without a custom handoff round trip.
            // This also works when no compatible Runic server is installed.
            // Never replace a live locally owned inventory with an older snapshot.
            if (!view.IsOwner())
            {
                view.ClaimOwnership();
                if (!view.IsOwner() || !rules.Refresh(chest)) return false;
            }
            return view.IsValid() && view.IsOwner() && ReferenceEquals(view.GetZDO(), zdo) &&
                Writable(chest, allowInUse) && rules.Allows(chest, context, principal, ZNet.GetUID());
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
}

using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace RunicSentinelClient.Runtime
{
    /// <summary>
    /// Registers the private direct RPC before either side processes the native handshake. The
    /// patch never gates vanilla and is a no-op when the local process is authoritative.
    /// </summary>
    [HarmonyPatch(typeof(ZNet), "OnNewConnection", new Type[] { typeof(ZNetPeer) })]
    internal static class ClientTransportPatches
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void ZNetOnNewConnectionPrefix(
            ZNet __instance,
            [HarmonyArgument(0)] ZNetPeer peer)
        {
            try { Plugin.ActiveRuntime?.OnConnectionStarted(__instance, peer); }
            catch { }
        }

        internal static bool IsInstalled(string owner)
        {
            if (string.IsNullOrEmpty(owner)) return false;
            MethodInfo original = AccessTools.DeclaredMethod(
                typeof(ZNet), "OnNewConnection", new Type[] { typeof(ZNetPeer) });
            Patches patches = original == null ? null : Harmony.GetPatchInfo(original);
            return patches != null && patches.Prefixes.Any(patch =>
                string.Equals(patch.owner, owner, StringComparison.Ordinal) &&
                patch.priority == Priority.First &&
                patch.PatchMethod?.DeclaringType == typeof(ClientTransportPatches));
        }
    }
}

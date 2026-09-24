using System;
using System.Reflection;
using UnityEngine;

namespace RunicProduction.Integration
{
    // Keep the domain's exact prefab names while matching the installed game's ZDO type.
    internal static class FermenterContentStorage
    {
        private static readonly bool UsesHash = ResolveUsesHash(typeof(Fermenter));

        internal static bool ResolveUsesHash(Type type)
        {
            MethodInfo method = type.GetMethod("GetContent",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
            if (method?.ReturnType == typeof(int)) return true;
            if (method?.ReturnType == typeof(string)) return false;
            throw new MissingMethodException("Unsupported Fermenter.GetContent return type.");
        }

        internal static void VerifySignature() { _ = UsesHash; }

        internal static string Read(ZDO zdo)
        {
            if (zdo == null) return string.Empty;
            if (!UsesHash) return zdo.GetString(ZDOVars.s_content, string.Empty);
            int hash = zdo.GetInt(ZDOVars.s_content, 0);
            if (hash == 0)
            {
                if (!string.IsNullOrEmpty(zdo.GetString(ZDOVars.s_content, string.Empty)))
                    throw new InvalidOperationException(
                        "Fermenter has legacy string content requiring recovery; refusing to consume another base.");
                return string.Empty;
            }
            GameObject prefab = ZNetScene.instance ? ZNetScene.instance.GetPrefab(hash) : null;
            // Unknown occupied slots must never be treated as empty and overwritten.
            if (!prefab || prefab.name.GetStableHashCode() != hash)
                throw new InvalidOperationException("Fermenter content prefab is unavailable.");
            return prefab.name;
        }

        internal static void Write(ZDO zdo, string prefabId)
        {
            string content = prefabId ?? string.Empty;
            if (UsesHash)
                zdo.Set(ZDOVars.s_content, content.Length == 0 ? 0 : content.GetStableHashCode());
            else
                zdo.Set(ZDOVars.s_content, content);
        }
    }
}

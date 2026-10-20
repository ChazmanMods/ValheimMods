using System;
using HarmonyLib;
using UnityEngine;

namespace ArcaneHunter
{
    // Capture the opening hit before vanilla alerts the animal. Kept only on its owner.
    internal sealed class HuntHit : MonoBehaviour
    {
        internal HitData Hit;
        internal float Distance;
        internal bool Clean, Ranged;
    }
    [HarmonyPatch]
    internal static class HuntingPatches
    {
        private const string ReceiptRPC = "ArcaneHunter.Kill.v1";
        private const string AwardedKey = "ArcaneHunter.awarded.v1";

        internal static bool Quarry(Character c) => c && !c.IsPlayer() && !c.IsTamed() && !c.IsBoss() &&
            c.GetComponent<BaseAI>() && c.GetHealth() > 0;
        internal static string Prefab(Character c) => Utils.GetPrefabName(c.gameObject);
        internal static string Biome(Character c) => Heightmap.FindBiome(c.transform.position).ToString();

        [HarmonyPatch(typeof(Character), "RPC_Damage"), HarmonyPrefix]
        private static void BeforeHit(Character __instance, HitData hit)
        {
            if (!Plugin.Settings.Enabled.Value || !Quarry(__instance) || !(hit.GetAttacker() is Player attacker)) return;
            var view = __instance.GetComponent<ZNetView>();
            if (!view || !view.IsValid() || !view.IsOwner()) return;
            var memo = __instance.GetComponent<HuntHit>() ?? __instance.gameObject.AddComponent<HuntHit>();
            memo.Hit = hit;
            memo.Distance = Vector3.Distance(attacker.transform.position, __instance.transform.position);
            memo.Clean = __instance.GetHealth() >= __instance.GetMaxHealth() - 0.01f && !__instance.GetComponent<BaseAI>().IsAlerted();
            memo.Ranged = hit.m_skill == Skills.SkillType.Bows || hit.m_skill == Skills.SkillType.Crossbows;
        }

        [HarmonyPatch(typeof(Player), "Awake"), HarmonyPostfix]
        private static void Register(Player __instance)
        {
            var view = __instance.GetComponent<ZNetView>();
            if (!view || !view.IsValid()) return;
            Player target = __instance;
            view.Register<ZPackage>(ReceiptRPC, (sender, package) => Receive(target, sender, package));
        }

        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath)), HarmonyPrefix]
        private static void Death(Character __instance, HitData ___m_lastHit)
        {
            try
            {
                if (!Plugin.Settings.Enabled.Value || __instance.IsPlayer() || __instance.IsTamed() || __instance.IsBoss() ||
                    !__instance.GetComponent<BaseAI>() || !(___m_lastHit?.GetAttacker() is Player killer)) return;
                var view = __instance.GetComponent<ZNetView>();
                if (!view || !view.IsValid() || !view.IsOwner()) return;
                var zdo = view.GetZDO();
                if (zdo.GetBool(AwardedKey) || zdo.GetBool(ZDOVars.s_cheated)) return;
                var killerView = killer.GetComponent<ZNetView>();
                if (!killerView || !killerView.IsValid()) return;
                zdo.Set(AwardedKey, true);
                var memo = __instance.GetComponent<HuntHit>();
                bool matched = memo && ReferenceEquals(memo.Hit, ___m_lastHit);
                var package = new ZPackage();
                package.Write(ZNet.instance.GetWorldUID().ToString() + ":" + __instance.GetZDOID());
                package.Write(Prefab(__instance));
                package.Write(__instance.GetLevel());
                package.Write(matched && memo.Clean);
                package.Write(matched && memo.Ranged);
                package.Write(matched ? memo.Distance : 0f);
                package.Write(Biome(__instance));
                // Send to the player's network owner, not the creature's owner or everyone.
                killerView.InvokeRPC(ReceiptRPC, package);
            }
            catch (Exception ex) { Plugin.Log.LogWarning("Hunt record skipped: " + ex.Message); }
        }
        private static void Receive(Player target, long sender, ZPackage package)
        {
            if (!Plugin.Instance || !Plugin.Settings.Enabled.Value || target != Player.m_localPlayer) return;
            try
            {
                // These receipts affect personal journal renown only, never inventory/world state.
                string receipt = package.ReadString(), prefab = package.ReadString();
                int level = package.ReadInt();
                bool clean = package.ReadBool(), ranged = package.ReadBool();
                float distance = package.ReadSingle();
                string biome = package.ReadString();
                if (receipt.Length > 160 || prefab.Length > 100 || biome.Length > 100 ||
                    string.IsNullOrEmpty(prefab) || level < 1 || level > 100 || float.IsNaN(distance) || float.IsInfinity(distance)) return;
                var gamePrefab = ZNetScene.instance.GetPrefab(prefab);
                if (!gamePrefab || !gamePrefab.GetComponent<Character>()) return;
                var plugin = Plugin.Instance;
                plugin.EnsureJournal(target);
                plugin.PrepareBoard();
                string beforeRank = Core.HuntingRules.Rank(plugin.Journal.XP);
                int earned = plugin.Journal.RecordKill(receipt, prefab, level, clean, ranged, distance, biome, Plugin.Settings.Commissions.Value);
                if (earned == 0) return;
                var seals = plugin.Journal.UnlockSeals();
                plugin.SaveJournal();
                plugin.Notify($"{Core.Bestiary.Name(prefab)}: +{earned} renown" + (clean ? " | Silent Arrow" : "") +
                    (ranged && distance >= 40 ? $" | Farshot {distance:0}m" : ""));
                string afterRank = Core.HuntingRules.Rank(plugin.Journal.XP);
                if (beforeRank != afterRank) target.Message(MessageHud.MessageType.Center, "Hunter title earned: " + afterRank);
                else if (seals.Count > 0) target.Message(MessageHud.MessageType.Center, "Hunter seal earned: " + string.Join(" / ", seals));
            }
            catch (Exception ex) { Plugin.Log.LogWarning("Invalid hunt receipt ignored: " + ex.Message); }
        }
    }
}

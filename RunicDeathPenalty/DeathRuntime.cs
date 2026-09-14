using System;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using RunicDeathPenalty.Core;
using UnityEngine;

namespace RunicDeathPenalty
{
    internal static class DeathRuntime
    {
        [ThreadStatic] internal static Player PenalizedDeath;
        internal const string GraveKey = "RunicDeathPenalty.restricted";
        internal static readonly int Corpse = "CorpseRun".GetStableHashCode();
        static readonly AccessTools.FieldRef<Player, float> SinceDeath = AccessTools.FieldRefAccess<Player, float>("m_timeSinceDeath");
        static readonly AccessTools.FieldRef<StatusEffect, float> EffectTime = AccessTools.FieldRefAccess<StatusEffect, float>("m_time");
        internal static double Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        internal static bool NoGrace(Player p) => p.m_customData.ContainsKey(Locations.Key("noGrace"));
        internal static void UpdateProtections(Player p)
        {
            if (!Plugin.Active) return;
            var manager = p.GetSEMan();
            if (Locations.Restricted(p))
            {
                var corpse = manager.GetStatusEffect(Corpse);
                if (corpse)
                {
                    p.m_customData[Locations.Key("corpseExpires")] = (Now + corpse.GetRemaningTime()).ToString("R", CultureInfo.InvariantCulture);
                    manager.RemoveStatusEffect(Corpse, true);
                }
                manager.RemoveStatusEffect(SEMan.s_statusEffectSoftDeath, true);
            }
            else if (p.m_customData.TryGetValue(Locations.Key("corpseExpires"), out string expiry))
            {
                p.m_customData.Remove(Locations.Key("corpseExpires"));
                if (double.TryParse(expiry, NumberStyles.Float, CultureInfo.InvariantCulture, out double until) && until > Now)
                {
                    var se = manager.AddStatusEffect(Corpse);
                    if (se) EffectTime(se) = Mathf.Max(0, se.m_ttl - (float)(until - Now));
                }
            }
            if (NoGrace(p)) manager.RemoveStatusEffect(SEMan.s_statusEffectSoftDeath, true);
        }
        internal static void Apply(Skills skills, float factor)
        {
            var p = PenalizedDeath;
            var list = skills.GetSkillList();
            factor = float.IsNaN(factor) || float.IsInfinity(factor) ? 0 : Rules.Clamp(factor,0,1);
            RecoveryBudget budget;
            p.m_customData.TryGetValue(Locations.Key("budget"), out string saved);
            try { budget = RecoveryBudget.Decode(saved); }
            catch (Exception e) { Plugin.Log.LogWarning("Invalid saved recovery budget; beginning a fresh window: " + e.Message); budget = new RecoveryBudget(); }
            if (Plugin.Policy.RecoveryCap) budget.Begin(Now, Plugin.Policy, factor, list.Select(s => new System.Collections.Generic.KeyValuePair<int,float>((int)s.m_info.m_skill, s.m_level)));
            foreach (var skill in list)
            {
                skill.m_level = Mathf.Max(0, skill.m_level - budget.Loss((int)skill.m_info.m_skill, skill.m_level, factor, Plugin.Policy));
                skill.m_accumulator = 0;
            }
            // Keep old fixed-window state when the cap is toggled off; toggling it back cannot reset a live window.
            if (Plugin.Policy.RecoveryCap) p.m_customData[Locations.Key("budget")] = budget.Encode();
            p.Message(MessageHud.MessageType.TopLeft, "Runic: restricted death, " + Plugin.Policy.Multiplier + "x skill loss" + (Plugin.Policy.RecoveryCap ? " (extra-loss cap applied)" : "") + ".");
            Plugin.Log.LogInfo("Restricted death: player=" + p.GetPlayerID() + "; biomeTier=" + Locations.PlayerTier(p) + "; baseRate=" + factor + "; multiplier=" + Plugin.Policy.Multiplier + "; cap=" + Plugin.Policy.RecoveryCap);
        }
        internal static void AdjustSoftDeath(StatusEffect se, Player p)
        {
            if (se && se.NameHash() == SEMan.s_statusEffectSoftDeath) EffectTime(se) = Mathf.Max(EffectTime(se), SinceDeath(p));
        }
    }
    [HarmonyPatch(typeof(Player), nameof(Player.OnDeath))]
    static class PlayerDeath
    {
        [HarmonyPriority(Priority.First)]
        static void Prefix(Player __instance, out Player __state)
        {
            __state = DeathRuntime.PenalizedDeath;
            if (__instance != Player.m_localPlayer || !Plugin.Active) return;
            __instance.m_customData.Remove(Locations.Key("corpseExpires"));
            if (Locations.Restricted(__instance))
            {
                DeathRuntime.PenalizedDeath = __instance;
                __instance.m_customData[Locations.Key("noGrace")] = "1";
                __instance.ClearHardDeath();
            }
            // A normal death earns a fresh vanilla protection period, but only after its death-loss decision.
        }
        static void Postfix(Player __instance)
        {
            if (__instance != Player.m_localPlayer || !Plugin.Active) return;
            if (DeathRuntime.PenalizedDeath == __instance) __instance.ClearHardDeath();
            else __instance.m_customData.Remove(Locations.Key("noGrace"));
        }
        static Exception Finalizer(Player __state, Exception __exception) { DeathRuntime.PenalizedDeath = __state; return __exception; }
    }
    [HarmonyPatch(typeof(Player), "HardDeath")]
    static class HardDeathPatch
    {
        static void Postfix(Player __instance, ref bool __result)
        {
            if (Plugin.Active && (DeathRuntime.NoGrace(__instance) || Locations.Restricted(__instance))) __result = true;
        }
    }
    [HarmonyPatch(typeof(Skills), nameof(Skills.LowerAllSkills))]
    static class DeathSkillLoss
    {
        static bool Prefix(Skills __instance, float factor)
        {
            if (!DeathRuntime.PenalizedDeath || DeathRuntime.PenalizedDeath.GetSkills() != __instance) return true;
            DeathRuntime.Apply(__instance, factor); return false;
        }
    }
    [HarmonyPatch(typeof(TombStone), nameof(TombStone.Setup))]
    static class MarkGrave
    {
        static void Postfix(TombStone __instance, long ownerUID)
        {
            if (DeathRuntime.PenalizedDeath && DeathRuntime.PenalizedDeath.GetPlayerID() == ownerUID)
                __instance.GetComponent<ZNetView>()?.GetZDO()?.Set(DeathRuntime.GraveKey, true);
        }
    }
    [HarmonyPatch(typeof(TombStone), "GiveBoost")]
    static class GraveBoost
    {
        static bool Prefix(TombStone __instance) => !Plugin.Active || !(__instance.GetComponent<ZNetView>()?.GetZDO()?.GetBool(DeathRuntime.GraveKey) ?? false);
    }
    [HarmonyPatch(typeof(SEMan), nameof(SEMan.Update))]
    static class SuppressProtection
    {
        static void Prefix(Character ___m_character)
        {
            if (___m_character is Player p && p == Player.m_localPlayer && !p.IsDead()) DeathRuntime.UpdateProtections(p);
        }
    }
    [HarmonyPatch(typeof(SEMan), nameof(SEMan.AddStatusEffect), new[]{typeof(StatusEffect),typeof(bool),typeof(int),typeof(float),typeof(short)})]
    static class ProtectionAddition
    {
        static bool Prefix(Character ___m_character, StatusEffect statusEffect)
        {
            if (!Plugin.Active || !statusEffect || !(___m_character is Player p)) return true;
            int hash = statusEffect.NameHash();
            if (hash != DeathRuntime.Corpse && hash != SEMan.s_statusEffectSoftDeath) return true;
            return !Locations.Restricted(p) && (hash != SEMan.s_statusEffectSoftDeath || !DeathRuntime.NoGrace(p));
        }
        static void Postfix(Character ___m_character, StatusEffect __result)
        {
            if (Plugin.Active && ___m_character is Player p) DeathRuntime.AdjustSoftDeath(__result, p);
        }
    }
}

using System;
using UnityEngine;

namespace RunicProduction.Integration
{
    internal static class CookingExperience
    {
        private const string Rpc = "RunicProduction_CookingExperience";

        internal static void Register(Player player)
        {
            ZNetView view = player == null ? null : player.GetComponent<ZNetView>();
            if (view == null || !view.IsValid()) return;
            view.Unregister(Rpc);
            view.Register<float>(Rpc, (sender, amount) =>
            {
                if (!(ProductionConfig.Enabled?.Value ?? false) ||
                    player != Player.m_localPlayer || !view.IsOwner() ||
                    !IsNativeAward(amount)) return;
                player.RaiseSkill(Skills.SkillType.Cooking, amount);
            });
        }

        internal static bool IsNativeAward(float amount) => amount == 0.4f || amount == 0.6f;

        // Called only after the inventory/station transaction commits. Player-owned RPCs
        // deliver XP to the character's client even when a dedicated server owns the oven.
        internal static void Award(CookingStation station, long playerId, float amount)
        {
            if (playerId == 0L || station.m_skill != Skills.SkillType.Cooking ||
                !IsNativeAward(amount) || !ValheimAccess.IsNativeOwner(station)) return;
            try
            {
                Player player = Player.GetPlayer(playerId);
                ZNetView view = player == null ? null : player.GetComponent<ZNetView>();
                if (view != null && view.IsValid()) view.InvokeRPC(Rpc, amount);
            }
            catch (Exception exception)
            {
                // Never turn an already committed item transfer into a retry for an XP failure.
                ProductionRuntime.FailHook("cooking experience", exception);
            }
        }
    }
}

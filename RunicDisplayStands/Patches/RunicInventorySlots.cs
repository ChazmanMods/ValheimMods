using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Bootstrap;
using HarmonyLib;

namespace RunicDisplayStands
{
    internal static class RunicInventorySlots
    {
        // Optional integration: read Inventory's own topology instead of guessing
        // where its protected row lives (it may also have quiver rows).
        internal static Dictionary<int, Vector2i> Capture(Player player)
        {
            var result = new Dictionary<int, Vector2i>();
            if (!Chainloader.PluginInfos.TryGetValue("chazman.RunicInventory", out var plugin)) return result;
            try
            {
                var runtime = AccessTools.Property(plugin.Instance.GetType(), "Runtime")?.GetValue(plugin.Instance);
                if (runtime == null) return result;
                var args = new object[] { player.GetPlayerID(), null, null };
                var method = AccessTools.Method(runtime.GetType(), "TryCapture");
                if (method == null || !(bool)method.Invoke(runtime, args) || args[1] == null) return result;
                var snapshot = args[1];
                var type = snapshot.GetType();
                if (Convert.ToInt32(type.GetProperty("AuthorityMode").GetValue(snapshot)) != 1 ||
                    (int)type.GetProperty("Width").GetValue(snapshot) != player.GetInventory().GetWidth() ||
                    (int)type.GetProperty("Height").GetValue(snapshot) != player.GetInventory().GetHeight()) return result;
                foreach (var role in (IEnumerable)type.GetProperty(global::Runic.Localization.RunicText.Get("text_c25337055464")).GetValue(snapshot))
                {
                    var roleType = role.GetType();
                    int index = Convert.ToInt32(roleType.GetProperty(global::Runic.Localization.RunicText.Get("text_14736a2eb9f4")).GetValue(role)) - 1;
                    if (index < ArmorStandSlots.Helmet || index > ArmorStandSlots.Utility ||
                        (bool)roleType.GetProperty(global::Runic.Localization.RunicText.Get("text_a424e33d9093")).GetValue(role)) continue;
                    var coordinate = roleType.GetProperty(global::Runic.Localization.RunicText.Get("text_bdc10f09365d")).GetValue(role);
                    int x = (int)coordinate.GetType().GetProperty("X").GetValue(coordinate);
                    int y = (int)coordinate.GetType().GetProperty("Y").GetValue(coordinate);
                    if (x >= 0 && x < player.GetInventory().GetWidth() && y >= 0 && y < player.GetInventory().GetHeight())
                        result[index] = new Vector2i(x, y);
                }
            }
            catch (Exception error) { Plugin.Log?.LogWarning("Protected equipment slot lookup unavailable: " + error.Message); }
            return result;
        }
    }
}

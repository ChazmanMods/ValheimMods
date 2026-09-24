using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace RunicSigns.Runtime;

internal static class SignAccess
{
    private static readonly System.Reflection.FieldInfo Areas = AccessTools.Field(typeof(PrivateArea), "m_allAreas");
    private static readonly System.Reflection.MethodInfo Permitted = AccessTools.Method(typeof(PrivateArea), "IsPermitted");
    internal static bool Eligible(Sign sign) => sign && sign.GetComponent<Piece>() &&
        sign.GetComponent<ZNetView>() && sign.GetComponent<ZNetView>().IsValid() &&
        Utils.GetPrefabName(sign.gameObject) == "sign";

    private static readonly System.Reflection.FieldInfo Viewable = AccessTools.Field(typeof(Sign), "m_isViewable");
    internal static bool CanView(Sign sign) => sign && Viewable != null && (bool)Viewable.GetValue(sign);
    internal static bool Local(Sign sign) => Eligible(sign) && Player.m_localPlayer &&
        !Player.m_localPlayer.IsDead() && Vector3.Distance(Player.m_localPlayer.transform.position, sign.transform.position) <= 5 &&
        PrivateArea.CheckAccess(sign.transform.position, 0, false);

    internal static bool Sender(Sign sign, long sender)
    {
        if (sender == ZNet.GetUID()) return Local(sign);
        // Resolve the player through the network object's owner; do not trust a supplied character ID.
        var player = Player.GetAllPlayers().FirstOrDefault(p => p &&
            p.GetComponent<ZNetView>()?.GetZDO()?.GetOwner() == sender);
        if (!player || player.IsDead() || Vector3.Distance(player.transform.position, sign.transform.position) > 5) return false;
        if (Areas == null || Permitted == null) return false;
        var areas = Areas.GetValue(null) as List<PrivateArea>;
        if (areas == null) return false;
        bool protectedArea = false;
        foreach (var area in areas)
        {
            var zdo = area ? area.GetComponent<ZNetView>()?.GetZDO() : null;
            if (zdo == null || !zdo.GetBool(ZDOVars.s_enabled) ||
                Utils.DistanceXZ(area.transform.position, sign.transform.position) >= area.m_radius) continue;
            protectedArea = true;
            if (area.GetComponent<Piece>().GetCreator() == player.GetPlayerID() ||
                (bool)Permitted.Invoke(area, new object[] { player.GetPlayerID() })) return true;
        }
        // Match vanilla's overlapping-ward rule: access through any covering ward is sufficient.
        return !protectedArea;
    }
}

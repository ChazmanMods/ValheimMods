using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace RunicDeathPenalty
{
    // HarmonyX executes later prefixes even after another prefix declines vanilla.
    // Guard Runic's preparation methods themselves, before they reserve or move material.
    [HarmonyPatch]
    static class RunicCompatibility
    {
        static readonly HashSet<string> Reported = new HashSet<string>();
        static bool Prepare() => Type.GetType("RunicProduction.Integration.ExactStockInventoryMutation, RunicProduction", false) != null ||
            Type.GetType("RunicCrafting.Integration.CraftingRuntime, RunicCrafting", false) != null;
        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var target in new[]{
                ("RunicProduction.Integration.ExactStockInventoryMutation, RunicProduction", "TryPrepareSourceDetailed"),
                ("RunicProduction.Integration.ExactStockInventoryMutation, RunicProduction", "TryPrepareCompositeSources"),
                ("RunicProduction.Integration.ExactStockInventoryMutation, RunicProduction", "TryPrepareDestination"),
                ("RunicCrafting.Integration.CraftingRuntime, RunicCrafting", "BeforeCraft"),
                ("RunicCrafting.Integration.ManualInteractionRuntime, RunicCrafting", "Pull")})
            {
                var type = Type.GetType(target.Item1, false);
                if (type == null) continue;
                var method = AccessTools.Method(type, target.Item2);
                if (method == null || method.ReturnType != typeof(bool)) throw new MissingMethodException("Unsupported Runic integration: " + target.Item1 + "." + target.Item2);
                if (Reported.Add(type.FullName+"."+method.Name)) Plugin.Log.LogInfo("Guarding direct Runic operation: " + type.Name + "." + method.Name);
                yield return method;
            }
        }
        static bool Prefix(MethodBase __originalMethod, object[] __args)
        {
            if (!Plugin.Active || !Plugin.Policy.Use) return true;
            var parameters = __originalMethod.GetParameters();
            for (int i=0;i<__args.Length;i++)
            {
                if (parameters[i].IsOut) continue;
                object argument=__args[i];
                bool allowed=true;
                if (argument is Recipe recipe)
                {
                    var player=__args.OfType<Player>().FirstOrDefault();
                    var upgrade=__args.OfType<ItemDrop.ItemData>().FirstOrDefault();
                    allowed = player && ItemCatalog.Recipe(recipe, upgrade == null ? 1 : upgrade.m_quality+1, player.GetInventory());
                }
                else if (argument is ItemDrop item) allowed=ItemCatalog.Allowed(item.name);
                else if (argument is ItemDrop.ItemData data) allowed=ItemCatalog.Use(data);
                else if (parameters[i].Name == "requirements" && argument is IEnumerable sequence)
                {
                    foreach (var requirement in sequence) if (!PrefabAllowed(requirement)) { allowed=false;break; }
                }
                else if (parameters[i].Name == "output") allowed=PrefabAllowed(argument);
                if (allowed) continue;
                for(int j=0;j<parameters.Length;j++)
                    if(parameters[j].IsOut && parameters[j].ParameterType==typeof(string).MakeByRefType()) __args[j]="RunicDeathPenalty: locked by group progression";
                return false;
            }
            return true;
        }
        static bool PrefabAllowed(object obj)
        {
            if (obj==null) return true;
            var property=AccessTools.Property(obj.GetType(),"PrefabId");
            return property!=null && ItemCatalog.Allowed(property.GetValue(obj) as string);
        }
    }
}

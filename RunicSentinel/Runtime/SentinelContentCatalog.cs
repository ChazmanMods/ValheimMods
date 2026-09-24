using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace RunicSentinel.Runtime
{
    internal sealed class SentinelContentEntry
    {
        internal string Id, Name, Mod, Kind;
        internal GameObject Prefab;
        internal Sprite Icon;
        internal Piece Piece;
        internal ItemDrop Item;
        internal string Details()
        {
            var lines=new List<string>{"Internal ID: "+Id, "Source: "+Mod, "Type: "+Kind};
            if(Item!=null)
            {
                var s=Item.m_itemData.m_shared;
                lines.Add(Localization.instance.Localize(s.m_description));
                lines.Add($"Health {s.m_food:0.#}  Stamina {s.m_foodStamina:0.#}  Eitr {s.m_foodEitr:0.#}  Duration {s.m_foodBurnTime:0.#}s");
                if(s.m_consumeStatusEffect!=null) lines.Add(Localization.instance.Localize(s.m_consumeStatusEffect.m_name));
                if(ObjectDB.instance!=null)
                    foreach(var recipe in ObjectDB.instance.m_recipes.Where(r=>r!=null && r.m_item!=null && r.m_item.gameObject.name==Id))
                    {
                        lines.Add("Recipe (makes "+recipe.m_amount+"): "+string.Join(", ",(recipe.m_resources??Array.Empty<Piece.Requirement>()).Where(r=>r?.m_resItem!=null)
                            .Select(r=>Localization.instance.Localize(r.m_resItem.m_itemData.m_shared.m_name)+" × "+r.m_amount+(r.m_amountPerLevel>0?"; upgrade per quality level: "+r.m_amountPerLevel:""))));
                        lines.Add("Crafting station: "+(recipe.m_craftingStation==null?"None":Localization.instance.Localize(recipe.m_craftingStation.m_name))+" · minimum station level: "+recipe.m_minStationLevel);
                    }
            }
            if(Piece!=null)
            {
                lines.Add(Localization.instance.Localize(Piece.m_description));
                lines.Add("Build recipe: "+string.Join(", ",(Piece.m_resources??Array.Empty<Piece.Requirement>()).Where(r=>r?.m_resItem!=null)
                    .Select(r=>Localization.instance.Localize(r.m_resItem.m_itemData.m_shared.m_name)+" × "+r.m_amount)));
                lines.Add("Building station: "+(Piece.m_craftingStation==null?"None":Localization.instance.Localize(Piece.m_craftingStation.m_name)));
            }
            return string.Join("\n",lines);
        }
    }

    internal static class SentinelContentCatalog
    {
        private static ZNetScene _scene;
        private static string _language;
        private static IReadOnlyList<SentinelContentEntry> _entries=Array.Empty<SentinelContentEntry>();
        internal static string Diagnostics="";
        internal static IReadOnlyList<SentinelContentEntry> Read(bool refresh=false)
        {
            if(ZNetScene.instance==null) { _scene=null; return Array.Empty<SentinelContentEntry>(); }
            string language=Localization.instance.GetSelectedLanguage();
            if(!refresh && ReferenceEquals(_scene,ZNetScene.instance) && _language==language) return _entries;
            var scene=ZNetScene.instance;
            var sources=JotunnSources();
            AddEmbeddedManagerSources(sources);
            var prefabs=new Dictionary<string,GameObject>(StringComparer.Ordinal);
            foreach(var p in scene.m_prefabs) if(p!=null) prefabs[p.name]=p;
            if(ObjectDB.instance!=null) foreach(var p in ObjectDB.instance.m_items)
            {
                if(p==null)continue;
                prefabs[p.name]=p;
                var table=p.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_buildPieces;
                if(table!=null) foreach(var piece in table.m_pieces) if(piece!=null) prefabs[piece.name]=piece;
            }
            var result=new List<SentinelContentEntry>();
            int skipped=0;
            foreach(var p in prefabs.Values)
            {
                try {
                var item=p.GetComponent<ItemDrop>(); var piece=p.GetComponent<Piece>(); var character=p.GetComponent<Character>();
                if(item==null && piece==null && character==null)continue;
                string kind=piece!=null ? (p.GetComponent<CraftingStation>()!=null ? "Stations" : "Pieces") : character!=null ? "Creatures" : "Items";
                if(item!=null && piece==null)
                {
                    var s=item.m_itemData.m_shared;
                    if(s.m_food>0 || s.m_foodStamina>0 || s.m_foodEitr>0)kind="Food";
                    else if(s.m_consumeStatusEffect!=null)kind="Potions";
                    else if(s.m_itemType==ItemDrop.ItemData.ItemType.Material)kind="Ingredients";
                }
                string name=piece!=null?piece.m_name:item!=null?item.m_itemData.m_shared.m_name:character.m_name;
                result.Add(new SentinelContentEntry{Id=p.name,Name=Localization.instance.Localize(name),Prefab=p,Piece=piece,Item=item,
                    Kind=kind,Mod=sources.TryGetValue(p.name,out var mod)?mod:"Unknown source",
                    Icon=piece!=null?piece.m_icon:item?.m_itemData?.m_shared?.m_icons?.FirstOrDefault()});
                } catch(Exception error) {skipped++;Debug.LogWarning("Sentinel catalog skipped prefab "+p.name+": "+error.GetType().Name);}
            }
            _entries=result.OrderBy(e=>e.Name,StringComparer.OrdinalIgnoreCase).ToArray();
            _scene=scene;_language=language;Diagnostics=result.Count+" entries · "+skipped+" invalid entries skipped";
            return _entries;
        }

        private static Dictionary<string,string> JotunnSources()
        {
            var result=new Dictionary<string,string>(StringComparer.Ordinal);
            // Optional integration: SourceMod is explicit registration metadata. Never infer ownership from prefab names.
            foreach(string managerName in new[]{"PrefabManager","PieceManager","ItemManager","CreatureManager"})
            {
                try
                {
                    var type=AccessTools.TypeByName("Jotunn.Managers."+managerName);
                    object manager=type?.GetProperty("Instance",BindingFlags.Public|BindingFlags.Static|BindingFlags.FlattenHierarchy)?.GetValue(null);
                    if(manager==null)continue;
                    foreach(var field in type.GetFields(BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public))
                    {
                        if(!(field.GetValue(manager) is IDictionary dictionary))continue;
                        foreach(object entity in dictionary.Values)
                        {
                            if(entity==null)continue;
                            var et=entity.GetType();
                            if(!(et.GetProperty("SourceMod")?.GetValue(entity) is BepInPlugin source))continue;
                            foreach(string property in new[]{"Prefab","PiecePrefab","ItemPrefab","CreaturePrefab"})
                                if(et.GetProperty(property)?.GetValue(entity) is GameObject prefab)
                                    result[prefab.name]=source.Name+" ("+source.GUID+")";
                        }
                    }
                }
                catch { /* Unknown source remains explicit if an optional provider changes its API. */ }
            }
            return result;
        }

        private static void AddEmbeddedManagerSources(Dictionary<string,string> result)
        {
            // ItemManager/PieceManager are often embedded in the owning plugin assembly.
            // Only use explicit prefab objects in their registration collections, and only
            // when the assembly has exactly one plugin identity.
            foreach(var group in BepInEx.Bootstrap.Chainloader.PluginInfos.Values.Where(p=>p.Instance!=null)
                        .GroupBy(p=>p.Instance.GetType().Assembly).Where(g=>g.Count()==1))
            {
                var plugin=group.Single();
                foreach(string typeName in new[]{"ItemManager.Item","PieceManager.BuildPiece","CreatureManager.Creature"})
                {
                    try
                    {
                        var type=group.Key.GetType(typeName);if(type==null)continue;
                        foreach(var field in type.GetFields(BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic))
                        {
                            object storage=field.GetValue(null);
                            IEnumerable registered=storage is IDictionary dictionary?dictionary.Values:storage as IEnumerable;
                            if(registered==null || storage is string)continue;
                            int examined=0;
                            foreach(object entity in registered)
                            {
                                if(examined++>=10000)break;
                                if(entity==null || !type.IsInstanceOfType(entity))continue;
                                object value=type.GetProperty("Prefab",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(entity)
                                    ??type.GetField("Prefab",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(entity);
                                if(value is GameObject prefab && !result.ContainsKey(prefab.name))
                                    result[prefab.name]=plugin.Metadata.Name+" ("+plugin.Metadata.GUID+")";
                            }
                        }
                    }
                    catch { /* An unsupported manager keeps the Unknown source designation. */ }
                }
            }
        }
    }
}

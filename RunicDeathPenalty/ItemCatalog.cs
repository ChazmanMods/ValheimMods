using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RunicDeathPenalty.Core;
using UnityEngine;

namespace RunicDeathPenalty
{
    internal static class ItemCatalog
    {
        internal static readonly Dictionary<string, int> Tiers = new Dictionary<string, int>(StringComparer.Ordinal);
        static readonly HashSet<string> Explicit = new HashSet<string>(StringComparer.Ordinal);
        internal static readonly HashSet<string> Unknown = new HashSet<string>(StringComparer.Ordinal);
        static ObjectDB database;
        static string overrides;
        internal static string Prefab(ItemDrop.ItemData item) => item?.m_dropPrefab ? item.m_dropPrefab.name : "";
        internal static void Rebuild()
        {
            Tiers.Clear(); Explicit.Clear(); Unknown.Clear(); database = ObjectDB.instance;
            overrides = Plugin.Policy.ItemOverrides;
            using (var stream = typeof(Plugin).Assembly.GetManifestResourceStream("RunicDeathPenalty.Data.ItemTiers.txt"))
            using (var reader = new StreamReader(stream))
                foreach (var p in Rules.ParseMap(reader.ReadToEnd())) { Tiers[p.Key] = p.Value; Explicit.Add(p.Key); }
            foreach (var p in Rules.ParseMap(overrides)) { Tiers[p.Key] = p.Value; Explicit.Add(p.Key); }
            if (!database) return;
            // Piece requirements and recipes form a finite monotone graph. No guesses based on name substrings.
            var prefabs = ZNetScene.instance ? ZNetScene.instance.m_prefabs : new List<GameObject>();
            for (int pass = 0; pass < 32; pass++)
            {
                bool changed = false;
                foreach (var prefab in prefabs)
                {
                    if (!prefab) continue;
                    var piece = prefab.GetComponent<Piece>();
                    if (piece && piece.m_resources.Length > 0 && TryRequirements(piece.m_resources, out int tier))
                        changed |= Derive(prefab.name, tier);
                    var smelter = prefab.GetComponent<Smelter>();
                    if (smelter) foreach (var c in smelter.m_conversion) changed |= Conversion(c.m_from, c.m_to, prefab.name);
                    var cooking = prefab.GetComponent<CookingStation>();
                    if (cooking) foreach (var c in cooking.m_conversion) changed |= Conversion(c.m_from, c.m_to, prefab.name);
                    var fermenter = prefab.GetComponent<Fermenter>();
                    if (fermenter) foreach (var c in fermenter.m_conversion) changed |= Conversion(c.m_from, c.m_to, prefab.name);
                }
                foreach (var recipe in database.m_recipes)
                {
                    if (!recipe || !recipe.m_item || !TryRequirements(recipe.m_resources, out int tier, recipe.m_requireOnlyOneIngredient, recipe.m_noCraftOnlyUpgrade)) continue;
                    if (recipe.m_craftingStation)
                    {
                        if (!Tiers.TryGetValue(recipe.m_craftingStation.gameObject.name, out int station)) continue;
                        tier = Math.Max(tier, station);
                        if (recipe.m_craftingStation.gameObject.name == "piece_cauldron") tier = Math.Max(tier, Math.Min(7, recipe.m_minStationLevel));
                    }
                    changed |= Derive(recipe.m_item.name, tier);
                }
                if (!changed) break;
            }
            foreach (var item in database.m_items) if (item && !Tiers.ContainsKey(item.name)) Unknown.Add(item.name);
            Plugin.Log.LogInfo("Item catalog: " + Tiers.Count + " mapped prefabs; " + Unknown.Count + " unmapped. Use rdp unmapped to inspect.");
        }
        static bool Conversion(ItemDrop from, ItemDrop to, string station)
        {
            if (!from || !to || !Tiers.TryGetValue(from.name, out int input) || !Tiers.TryGetValue(station, out int st)) return false;
            return Derive(to.name, Math.Max(input, st));
        }
        static bool Derive(string name, int tier)
        {
            if (Explicit.Contains(name)) return false;
            if (Tiers.TryGetValue(name, out int old) && old <= tier) return false;
            Tiers[name] = tier; return true;
        }
        static bool TryRequirements(Piece.Requirement[] requirements, out int tier, bool any = false, bool upgrading = false)
        {
            tier = any ? 8 : 0; bool found = false;
            foreach (var req in requirements)
            {
                if (!req.m_resItem || (upgrading ? req.GetAmount(2) : req.m_amount) <= 0) continue;
                if (!Tiers.TryGetValue(req.m_resItem.name, out int t)) { if (any) continue; return false; }
                found = true; tier = any ? Math.Min(tier, Math.Max(0,t)) : Math.Max(tier,t);
            }
            return !any || found;
        }
        internal static int Tier(string name)
        {
            if (!Plugin.Policy.Enabled) return -1;
            if (database != ObjectDB.instance || overrides != Plugin.Policy.ItemOverrides || Tiers.Count == 0) Rebuild();
            if (Tiers.TryGetValue(name ?? "", out int tier)) return tier;
            if (!string.IsNullOrEmpty(name) && Unknown.Add(name)) Plugin.Log.LogWarning("Unmapped item/piece: " + name);
            return Plugin.Policy.BlockUnknown ? 8 : -1;
        }
        internal static bool Allowed(string name, bool message = true)
        {
            if (!Plugin.Policy.Enabled) return true;
            if (Plugin.WaitingForPolicy) return !message ? false : Plugin.Deny("Waiting for the server's progression policy.");
            int tier = Tier(name);
            return !Plugin.Policy.Restricted(tier) || !message ? !Plugin.Policy.Restricted(tier) :
                Plugin.Deny(name + " is locked. Required: " + Rules.Name(tier) + "; group: " + Rules.Name(Plugin.Policy.EffectiveTier) + ".");
        }
        internal static bool Use(ItemDrop.ItemData item, bool message = true) => !Plugin.Policy.Use || item == null || Allowed(Prefab(item), message);
        internal static bool Requirements(Piece.Requirement[] requirements, bool any = false)
        {
            if (!Plugin.Policy.Use) return true;
            if (any) return requirements.Any(r => r.m_resItem && Allowed(r.m_resItem.name, false));
            return requirements.All(r => !r.m_resItem || r.m_amount <= 0 || Allowed(r.m_resItem.name));
        }
        internal static bool Piece(Piece piece) => !Plugin.Policy.Use || !piece || (Allowed(Utils.GetPrefabName(piece.gameObject)) && Requirements(piece.m_resources));
        internal static bool Recipe(Recipe recipe, int quality, Inventory inventory)
        {
            if (!Plugin.Policy.Use || !recipe) return true;
            if (!Allowed(recipe.m_item.name)) return false;
            if (recipe.m_requireOnlyOneIngredient)
            {
                // Follow vanilla's actual first ingredient choice, not a later permissible alternative.
                foreach (var req in recipe.m_resources)
                    if (req.m_resItem && inventory.CountItems(req.m_resItem.m_itemData.m_shared.m_name) >= req.GetAmount(quality))
                        return Allowed(req.m_resItem.name);
                return recipe.m_resources.All(req => !req.m_resItem || Allowed(req.m_resItem.name));
            }
            return recipe.m_resources.All(r => !r.m_resItem || r.GetAmount(quality) == 0 || Allowed(r.m_resItem.name));
        }
    }
}

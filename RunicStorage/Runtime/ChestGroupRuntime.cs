using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using RunicStorage.Engine;

namespace RunicStorage.Runtime;

internal static class ChestGroupRuntime
{
    private static ConfigEntry<string> _library;
    private static ObjectDB _recipeSource;
    private static int _recipeCount;
    private static readonly HashSet<string> Ingredients = new(StringComparer.Ordinal);
    internal static void Bind(ConfigFile config) => _library = config.Bind("Chest Rules", "CustomGroupLibrary", "",
        global::Runic.Localization.RunicText.Get("text_43f399366f98"));
    internal static bool LoadLibrary(out ChestRules library) => ChestRules.TryDecode(_library?.Value ?? "", out library);
    internal static void SaveLibrary(ChestRules library)
    { if (_library == null) throw new InvalidOperationException("Custom group library is unavailable."); _library.Value = library.Encode(); _library.ConfigFile.Save(); }

    internal static ChestItemFacts Facts(ItemDrop.ItemData item)
    {
        var db = ObjectDB.instance;
        if (db && (db != _recipeSource || db.m_recipes.Count != _recipeCount))
        {
            Ingredients.Clear(); _recipeSource = db; _recipeCount = db.m_recipes.Count;
            foreach (var recipe in db.m_recipes)
                if (recipe && recipe.m_resources != null)
                    foreach (var requirement in recipe.m_resources)
                        if (requirement.m_resItem) Ingredients.Add(ValheimContainerIdentity.ResourceId(requirement.m_resItem.m_itemData));
        }
        var shared = item.m_shared;
        string id = ValheimContainerIdentity.ResourceId(item);
        return new ChestItemFacts { Id = id, Type = shared.m_itemType.ToString(), Ammo = shared.m_ammoType ?? "",
            Health = shared.m_food, Stamina = shared.m_foodStamina, Eitr = shared.m_foodEitr,
            Tool = shared.m_itemType == ItemDrop.ItemData.ItemType.Tool || shared.m_buildPieces != null || shared.m_skillType.ToString() == "Pickaxes" || id == "FishingRod",
            Potion = shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable && shared.m_food <= 0 && shared.m_consumeStatusEffect != null,
            Valuable = shared.m_value > 0 || id == "Coins" || id == "AncientCoin", Ingredient = Ingredients.Contains(id) };
    }
}

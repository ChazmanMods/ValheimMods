using System;
using System.Collections.Generic;
using System.Linq;

namespace RunicStorage.Engine;

internal sealed class ChestItemFacts
{
    internal string Id = "", Type = "", Ammo = "";
    internal float Health, Stamina, Eitr;
    internal bool Tool, Potion, Ingredient, Valuable;
}

internal sealed class ChestCustomGroup
{
    internal string Id = "custom:" + Guid.NewGuid().ToString("N"), Name = "New group";
    internal readonly List<string> Items = new();
    internal ChestCustomGroup Copy()
    { var copy = new ChestCustomGroup { Id = Id, Name = Name }; copy.Items.AddRange(Items); return copy; }
}

internal static class ChestRuleGroups
{
    internal sealed class Definition
    {
        internal string Id, Name;
        internal int Specificity;
        internal Func<ChestItemFacts, bool> Matches;
    }
    internal static readonly Dictionary<string, HashSet<string>> Curated = new(StringComparer.Ordinal);
    internal static readonly List<Definition> All = new();
    internal static IEnumerable<Definition> Biomes => All.Where(g => g.Id.StartsWith("biome:", StringComparison.Ordinal));
    static ChestRuleGroups()
    {
        Add("equipment:weapons", "Weapons", 2, i => !i.Tool && new[] { "OneHandedWeapon", "TwoHandedWeapon", "TwoHandedWeaponLeft", "Bow", "Torch" }.Contains(i.Type));
        Add("equipment:shields", "Shields", 1, i => i.Type == "Shield");
        Add("equipment:armor", "Armor", 2, i => new[] { "Helmet", "Chest", "Legs", "Shoulder" }.Contains(i.Type));
        Add("equipment:capes", "Capes", 1, i => i.Type == "Shoulder");
        Add("equipment:tools", "Tools", 1, i => i.Tool || i.Type == "Tool");
        Add("ammo:all", "All Ammunition", 2, i => i.Type == "Ammo" || i.Type == "AmmoNonEquipable");
        Add("ammo:arrows", "Arrows", 1, i => i.Type == "Ammo" && (i.Ammo.Equals("arrow", StringComparison.OrdinalIgnoreCase) || i.Ammo == "$ammo_arrows"));
        Add("ammo:bolts", "Bolts", 1, i => i.Type == "Ammo" && (i.Ammo.Equals("bolt", StringComparison.OrdinalIgnoreCase) || i.Ammo == "$ammo_bolts"));
        Add("food:all", "Food", 2, i => i.Health > 0);
        Add("food:health", "Health Foods", 1, i => i.Health > 0 && i.Health > i.Stamina);
        Add("food:stamina", "Stamina Foods", 1, i => i.Health > 0 && i.Stamina > i.Health);
        Add("food:balanced", "Balanced Foods", 1, i => i.Health > 0 && i.Health == i.Stamina);
        Add("food:eitr", "Eitr Foods", 1, i => i.Health > 0 && i.Eitr > 0);
        Add("consumable:potions", "Potions", 1, i => i.Potion);
        List("resource:wood", "Wood Materials", 1, "Wood FineWood RoundLog ElderBark YggdrasilWood Blackwood Frostwood BarkaBranch");
        List("resource:stone", "Stone Materials", 1, "Stone StoneRock Flint Obsidian Crystal BlackMarble Grausten Ice");
        List("resource:ores", "Ores", 1, "CopperOre CopperScrap TinOre IronOre IronScrap SilverOre BlackMetalScrap FlametalOre FlametalOreNew GoldOre");
        List("resource:bars", "Metal Bars", 1, "Copper Tin Bronze Iron Silver BlackMetal Flametal FlametalNew Gold");
        List("resource:seeds", "Seeds", 1, "BeechSeeds BirchSeeds Acorn FirCone PineCone CarrotSeeds TurnipSeeds OnionSeeds KaleSeeds OatSeeds PoteitrSeeds FirConeFrost AncientSeed");
        List("resource:crops", "Crops", 1, "Carrot Turnip Onion Barley Flax MushroomJotunPuffs MushroomMagecap Vineberry Kale Oat Poteitr");
        List("resource:animal", "Animal Materials", 1, "LeatherScraps DeerHide TrollHide WolfPelt WolfFang WolfClaw LoxPelt ScaleHide Feathers BoneFragments Bloodbag Entrails Chitin SerpentScale Needle Carapace Mandible Bilebag FreezeGland AskHide AskBladder MorgenHeart MorgenSinew BonemawSerpentScale BonemawSerpentTooth MooseHide MooseSinew SealHide SealBlubber BjornHide BjornPaw CelestialFeather ElakingHairBundle MoleClaws NornThread");
        Add("other:trophies", "Trophies", 1, i => i.Type == "Trophy");
        Add("other:valuables", "Valuables", 1, i => i.Valuable);
        Add("other:ingredients", "Crafting Ingredients", 2, i => i.Ingredient);
        List("biome:meadows", "Meadows", 2, "Wood Stone Flint FineWood Resin LeatherScraps DeerHide RawMeat DeerMeat NeckTail Raspberry Mushroom Dandelion Honey QueenBee BeechSeeds BirchSeeds Acorn Feathers BoneFragments TrophyBoar TrophyDeer TrophyNeck HardAntler TrophyEikthyr CookedMeat CookedDeerMeat NeckTailGrilled FeastMeadows_Material");
        List("biome:black-forest", "Black Forest", 2, "Wood Stone RoundLog CopperOre TinOre Copper Tin Bronze BronzeNails GreydwarfEye TrollHide SurtlingCore Thistle Blueberries Carrot CarrotSeeds FirCone PineCone AncientSeed BoneFragments Resin Mushroom TrophyGreydwarf TrophyGreydwarfBrute TrophyGreydwarfShaman TrophyForestTroll TrophySkeleton TrophySkeletonPoison TrophyTheElder ElderBark FeastBlackforest_Material");
        List("biome:swamp", "Swamp", 2, "Wood Stone ElderBark IronScrap IronOre Iron Chain Guck Ooze Entrails Bloodbag Root Turnip TurnipSeeds Thistle SurtlingCore Coal BoneFragments WitheredBone TrophyDraugr TrophyDraugrElite TrophyDraugrFem TrophyLeech TrophyBlob TrophySurtling TrophyWraith TrophyAbomination TrophyBonemass CryptKey FeastSwamps_Material");
        List("biome:mountains", "Mountains", 2, "Wood Stone SilverOre Silver Obsidian Crystal WolfPelt WolfFang WolfClaw WolfMeat FreezeGland DragonEgg DragonTear Onion OnionSeeds JuteRed WolfHairBundle TrophyWolf TrophyHatchling TrophyFenring TrophySGolem TrophyCultist TrophyDragonQueen FeastMountains_Material");
        List("biome:plains", "Plains", 2, "Wood Stone FineWood BlackMetalScrap BlackMetal Barley BarleyFlour Flax LinenThread LoxPelt LoxMeat Needle Cloudberry Tar GoblinTotem TrophyGoblin TrophyGoblinBrute TrophyGoblinShaman TrophyDeathsquito TrophyLox TrophyGrowth TrophyGoblinKing YagluthDrop FeastPlains_Material");
        List("biome:mistlands", "Mistlands", 2, "Stone BlackMarble YggdrasilWood Sap Eitr Softtissue MushroomJotunPuffs MushroomMagecap DvergrNeedle Bilebag Carapace Mandible ScaleHide HareMeat ChickenMeat ChickenEgg GiantBloodSack Wisp BlackCore MechanicalSpring RoyalJelly TrophySeeker TrophySeekerBrute TrophyGjall TrophyHare TrophyTick TrophyDvergr TrophySeekerQueen QueenDrop FeastMistlands_Material");
        List("biome:ashlands", "Ashlands", 2, "Grausten Blackwood FlametalOreNew FlametalNew AskHide AskBladder MorgenHeart MorgenSinew BonemawSerpentScale BonemawSerpentTooth CharredBone Charredskull Vineberry MushroomSmokePuff AsksvinMeat VoltureMeat BoneMawSerpentMeat SulfurStone ProustitePowder GemstoneBlue GemstoneGreen GemstoneRed FaderEmber MoltenCore TrophyAsksvin TrophyMorgen TrophyVolture TrophyBonemawSerpent TrophyCharredMelee TrophyCharredArcher TrophyCharredMage TrophyFader FeastAshlands_Material");
        List("biome:deep-north", "Deep North", 2, "Frostwood FirConeFrost BarkaBranch Ice GoldOre Gold FrostCore FrozenFuel Ectoplasm HatefulBlood OrbFrostFire OrbThunderBlood MooseHide MooseSinew MooseMeat SealHide SealBlubber Kale KaleSeeds Oat OatSeeds OatFlour PoteitrSeeds WrithanRoots ElakingHairBundle NornThread MoleClaws MemorialCoal CrownJewel AncientCoin TrophyMoose TrophySeal TrophyWrithan TrophyFrostTroll TrophyBlob_Frost ArmorDeepNorthHeavyChest ArmorDeepNorthHeavylegs ArmorDeepNorthMageChest ArmorDeepNorthMagelegs ArmorDeepNorthMediumChest ArmorDeepNorthMediumlegs HelmetDNHeavy HelmetDNMage HelmetDNMediumHood CapeDeepNorth CapeDeepNorthMage ArrowBloodGold BoltBloodGold StaffFrostOrbs StaffThunderBlood CookedMooseMeat CookedSealBlubber SmokedMooseMeat MooseKebab SealSoup KaleChips KaleChipsUncooked OatmealLingonberryJam OatMilk FeastDeepNorth_Material SpiceDeepNorth FishingBaitDeepNorth ShieldGold ShieldGoldBuckler ShieldGoldTower ArrowBloodGold AtgeirGold_BloodLightning AtgeirGold_FrostFire AtgeirGold AtgeirGoldUncooked AxeGold_BloodLightning AxeGold_FrostFire AxeGold AxeGoldUncooked BattleaxeGold_BloodLightning BattleaxeGold_FrostFire BattleaxeGold BattleaxeGoldUncooked BoltBloodGold BowGold_BloodLightning BowGold_FrostFire BowGold BowGoldUncooked CrossbowGold_BloodLightning CrossbowGold_FrostFire CrossbowGold CrossbowGoldUncooked FistGold_BloodLightning FistGold_FrostFire FistGold FistGoldUncooked KnifeGold_BloodLightning KnifeGold_FrostFire KnifeGold KnifeGoldUncooked MaceGold_BloodLightning MaceGold_FrostFire MaceGold MaceGoldUncooked SledgeGold_BloodLightning SledgeGold_FrostFire SledgeGold SledgeGoldUncooked SpearGold_BloodLightning SpearGold_FrostFire SpearGold SpearGoldUncooked SwordGold_BloodLightning SwordGold_FrostFire SwordGold SwordGoldUncooked THSwordGold_BloodLightning THSwordGold_FrostFire THSwordGold THSwordGoldUncooked TrophyBarka TrophyDeerWhite TrophyElaking TrophyJotunWarrior TrophyJotunWitch TrophyMole");
    }
    private static void Add(string id, string name, int rank, Func<ChestItemFacts, bool> match) =>
        All.Add(new Definition { Id = id, Name = name, Specificity = rank, Matches = match });
    private static void List(string id, string name, int rank, string items)
    {
        var set = new HashSet<string>(items.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
        Curated[id] = set; Add(id, name, rank, i => set.Contains(i.Id));
    }
    internal static Definition Find(string id) => All.FirstOrDefault(g => g.Id == id);
    internal static string Canonical(string value) => Find(value)?.Id ?? All.FirstOrDefault(g => g.Name == value)?.Id ?? value;
    internal static string Display(string value) => Find(value)?.Name ?? value;
}

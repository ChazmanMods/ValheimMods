using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RunicCrafting.Domain;
using RunicCrafting.Integration;

namespace RunicCrafting.Tests
{
    internal static class RecipeRegressionTests
    {
        private static Piece.Requirement Cost(string resource,int amount,bool upgrader=false,int perLevel=2)=>
            new Piece.Requirement {m_resItem=new ItemDrop{ResourceName=resource},m_amount=amount,m_amountPerLevel=perLevel,m_upgraderResource=upgrader};
        private static Piece.Requirement[] Axe()=>new[]{Cost("1",4),Cost("2",6),Cost("3",1,true)};
        private static ItemDrop.ItemData Stack(int prefab,int count,int x,float durability=0)=>new ItemDrop.ItemData{Prefab=prefab,m_stack=count,m_gridPos=new Vector2i(x,0),m_durability=durability};
        private static Inventory Bag(params ItemDrop.ItemData[] items){var bag=new Inventory("test",null,8,4);bag.GetAllItems().AddRange(items);return bag;}
        private static byte[] Bytes(Inventory inventory)=>ValheimReflection.SaveInventory(inventory).GetArray();

        internal static void OrdinaryWorkbenchIgnoresUpgraderIngredient()
        {
            var chest=Bag(Stack(1,79,0),Stack(2,34,1));
            var source=new ValheimMaterialSource("chest",chest,MaterialSourceKind.NearbyContainer,1,new[]{"1","2","3"},()=>true);
            var planner=new ExactMaterialPlanner();
            RecipeMaterialRequirements.TryBuild(Axe(),1,1,out var oldRequirements);
            TestAssert.False(planner.TryPlan(oldRequirements,new[]{source.Snapshot()},out _,out _),"Unfiltered fixture must reproduce hidden upgrader shortage.");
            TestAssert.True(RecipeMaterialRequirements.TryBuild(Axe(),1,1,out var requirements,false));
            TestAssert.Equal(2,requirements.Count);
            TestAssert.True(planner.TryPlan(requirements,new[]{source.Snapshot()},out _,out _),"79 wood and 34 flint must satisfy a normal axe recipe.");
            var engine=new ExactMaterialTransactionEngine();
            TestAssert.True(engine.TryBegin(requirements,new[]{source},out var lease,out _));
            lease.Commit();lease.Dispose();
            TestAssert.Equal(75,chest.GetAllItems()[0].m_stack);
            TestAssert.Equal(28,chest.GetAllItems()[1].m_stack);
        }

        internal static void UpgraderStationStillRequiresItsIngredient()
        {
            TestAssert.True(RecipeMaterialRequirements.TryBuild(Axe(),1,1,out var requirements,true));
            TestAssert.Equal(1,requirements.Count);TestAssert.Equal("3",requirements[0].ResourceId);
            var planner=new ExactMaterialPlanner();
            var noToken=new MaterialSourceSnapshot("chest",MaterialSourceKind.NearbyContainer,1,new Dictionary<string,int>{{"1",79},{"2",34}});
            TestAssert.False(planner.TryPlan(requirements,new[]{noToken},out _,out _));
            var token=new MaterialSourceSnapshot("chest",MaterialSourceKind.NearbyContainer,1,new Dictionary<string,int>{{"3",1}});
            TestAssert.True(planner.TryPlan(requirements,new[]{token},out _,out _));
        }

        internal static void UpgradeBatchAndPieceCostsRemainCorrect()
        {
            RecipeMaterialRequirements.TryBuild(Axe(),2,5,out var ordinary,false);
            TestAssert.Equal(2,ordinary.Count);TestAssert.True(ordinary.All(r=>r.Quantity==10));
            RecipeMaterialRequirements.TryBuild(Axe(),2,5,out var upgrader,true);
            TestAssert.Equal(15,upgrader[0].Quantity);
            RecipeMaterialRequirements.TryBuild(Axe(),0,1,out var piece);
            TestAssert.Equal(3,piece.Count,"Piece construction must not silently filter its requirements.");
            TestAssert.False(RecipeMaterialRequirements.TryBuild(Axe(),1,0,out _));
            TestAssert.False(RecipeMaterialRequirements.TryBuild(new[]{Cost("1",int.MaxValue)},1,2,out var overflow,false));
            TestAssert.Equal(0,overflow.Count);
        }

        internal static void WornEquipmentDoesNotBlockConsumptionOrRollback()
        {
            var worn=Stack(4,1,0,10.029f);worn.m_equipped=true;worn.m_customData["lock"]="keep";
            var wood=Stack(1,4,1);var bag=Bag(worn,wood);var before=Bytes(bag);var roundTrip=Bag();roundTrip.Load(new ZPackage(before));
            TestAssert.False(before.SequenceEqual(Bytes(roundTrip)),"Fixture must exercise native durability rounding.");
            var source=new ValheimMaterialSource("player",bag,MaterialSourceKind.PlayerInventory,0,new[]{"1"},()=>true);
            TestAssert.True(source.TryTake("1",4,out var token));
            TestAssert.Equal(1,bag.GetAllItems().Count);
            TestAssert.True(token.Restore());TestAssert.True(token.Restore());
            TestAssert.True(before.SequenceEqual(Bytes(bag)));
            TestAssert.True(ReferenceEquals(worn,bag.GetAllItems()[0])&&ReferenceEquals(wood,bag.GetAllItems()[1]));
            TestAssert.Equal(10.029f,worn.m_durability);TestAssert.Equal("keep",worn.m_customData["lock"]);
            var denied=new ValheimMaterialSource("denied",bag,MaterialSourceKind.NearbyContainer,1,new[]{"1"},()=>false);
            TestAssert.False(denied.TryTake("1",1,out _));
        }

        internal static void ChestPayloadOnlyAllowsNativeDurabilityConversion()
        {
            var bag=Bag(Stack(4,1,0,10.029f),Stack(1,79,1));var before=Bytes(bag);var loaded=Bag();loaded.Load(new ZPackage(before));
            TestAssert.True(CraftingInventoryPayloadComparison.MatchesLoaded(before,Bytes(loaded)));
            TestAssert.True(RunicProduction.Integration.ProductionInventoryPayloadComparison.MatchesLoaded(before,Bytes(loaded)));
            TestAssert.True(RunicAgriculture.Integration.AgricultureInventoryPayloadComparison.MatchesLoaded(before,Bytes(loaded)));
            loaded.GetAllItems()[1].m_stack++;
            TestAssert.False(RunicProduction.Integration.ProductionInventoryPayloadComparison.MatchesLoaded(before,Bytes(loaded)));
            TestAssert.False(RunicAgriculture.Integration.AgricultureInventoryPayloadComparison.MatchesLoaded(before,Bytes(loaded)));
            TestAssert.False(CraftingInventoryPayloadComparison.MatchesLoaded(before,Bytes(loaded)));
            loaded.Load(new ZPackage(before));loaded.GetAllItems()[0].m_durability+=1;
            TestAssert.False(CraftingInventoryPayloadComparison.MatchesLoaded(before,Bytes(loaded)));
        }

        internal static void PreviewAndConsumptionUseSameStationFilter()
        {
            string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","..","..",".."));
            string source=File.ReadAllText(Path.Combine(root,"Integration","CraftingRuntime.cs"));
            string call="TryBuildRequirements(recipe.m_resources, quality, multiplier, out List<MaterialRequirement> requirements,\n                    station != null && station.m_upgrader)";
            TestAssert.Equal(2,source.Replace("\r\n","\n").Split(new[]{call},StringSplitOptions.None).Length-1,
                "Both recipe preview and craft transaction must use the native station filter.");
        }
    }
}

using System;
using System.Linq;
using RunicCrafting.Domain;
using RunicCrafting.Integration;

namespace RunicCrafting.Tests
{
    internal static class ManualInteractionTests
    {
        private static Inventory Bag(int width=8) => new Inventory("manual-test", null, width, 1);
        private static ItemDrop.ItemData Stack(int amount=5) => new ItemDrop.ItemData
            { Prefab=1, m_stack=amount, m_durability=19.753f, m_cheated=true, m_crafterID=123,
              m_customData=new System.Collections.Generic.Dictionary<string,string>{{"fixture","preserved"}} };
        private static bool Move(Inventory source, Inventory target, out string reason) =>
            ManualItemTransfer.TryMoveOne(source, target, "1", ()=>true, _=>true, out reason);

        internal static void MovesExactlyOneRealItemAndPreservesMetadata()
        {
            var source=Bag();var target=Bag();var stack=Stack();source.GetAllItems().Add(stack);
            TestAssert.True(Move(source,target,out string reason));
            TestAssert.Equal("one-item-staged",reason);
            TestAssert.Equal(4,stack.m_stack);TestAssert.Equal(1,target.GetAllItems().Single().m_stack);
            var item=target.GetAllItems().Single();
            TestAssert.Equal("preserved",item.m_customData["fixture"]);
            TestAssert.Equal(stack.m_durability,item.m_durability);
            TestAssert.True(item.m_cheated);TestAssert.Equal(123L,item.m_crafterID);
            TestAssert.False(ReferenceEquals(stack,item));
            // The native operation may consume this real staged item, exactly once.
            TestAssert.True(target.RemoveItem(item,1));TestAssert.Equal(4,stack.m_stack);
            TestAssert.Equal(0,target.GetAllItems().Count);
        }

        internal static void FullProtectedMissingAndChangedSourcesDoNotMove()
        {
            var source=Bag();var target=Bag(1);var stack=Stack();source.GetAllItems().Add(stack);
            target.GetAllItems().Add(Stack());
            TestAssert.False(Move(source,target,out string reason));
            TestAssert.Equal("backpack-space-required",reason);TestAssert.Equal(5,stack.m_stack);
            target.GetAllItems().Clear();
            TestAssert.False(ManualItemTransfer.TryMoveOne(source,target,"1",()=>true,_=>false,out _));
            TestAssert.False(ManualItemTransfer.TryMoveOne(source,target,"1",()=>false,_=>true,out _));
            TestAssert.False(ManualItemTransfer.TryMoveOne(source,target,"2",()=>true,_=>true,out _));
            int reads=0;
            TestAssert.False(ManualItemTransfer.TryMoveOne(source,target,"1",()=>++reads==1,_=>true,out _));
            TestAssert.False(Move(source,source,out _));
            TestAssert.Equal(5,stack.m_stack);TestAssert.Equal(0,target.GetAllItems().Count);
        }

        internal static void FailedAndPartiallyAppliedInsertionRestoresBothInventories()
        {
            foreach(bool partial in new[]{false,true})
            {
                var source=Bag();var target=Bag();var stack=Stack(1);source.GetAllItems().Add(stack);
                if(partial)target.ThrowAfterNextAdd=true;else target.RejectNextAdd=true;
                TestAssert.False(Move(source,target,out string reason));TestAssert.Equal("transfer-restored",reason);
                TestAssert.Equal(0,target.GetAllItems().Count);
                TestAssert.True(ReferenceEquals(stack,source.GetAllItems().Single()));
                TestAssert.Equal(1,stack.m_stack);TestAssert.Equal(19.753f,stack.m_durability);
            }
        }

        internal static void CookingAndFuelGatesPreserveVanillaActions()
        {
            TestAssert.True(ManualInteractionPolicy.ShouldLoadCooking(false,0,true,true,false));
            TestAssert.False(ManualInteractionPolicy.ShouldLoadCooking(true,0,true,true,false));
            TestAssert.False(ManualInteractionPolicy.ShouldLoadCooking(false,-1,true,true,false));
            TestAssert.False(ManualInteractionPolicy.ShouldLoadCooking(false,0,true,false,false));
            TestAssert.False(ManualInteractionPolicy.ShouldLoadCooking(false,0,true,true,true));
            TestAssert.True(ManualInteractionPolicy.ShouldLoadCooking(false,0,false,false,false));
            TestAssert.True(ManualInteractionPolicy.HasFuelRoom(9,10));
            foreach(float fuel in new[]{9.1f,10f,-1f,float.NaN,float.PositiveInfinity})
                TestAssert.False(ManualInteractionPolicy.HasFuelRoom(fuel,10));
            TestAssert.False(ManualInteractionPolicy.ShouldRefuelFire(true,false,false,false,true,5,10,.2f,1));
            TestAssert.True(ManualInteractionPolicy.ShouldRefuelFire(true,false,false,true,true,5,10,.2f,1));
            TestAssert.False(ManualInteractionPolicy.ShouldRefuelFire(true,false,true,false,false,5,10,.2f,.1f));
            TestAssert.True(ManualInteractionPolicy.ShouldRefuelFire(true,false,true,false,false,5,10,.2f,.3f));
            TestAssert.False(ManualInteractionPolicy.ShouldRefuelFire(false,false,false,false,false,0,10,.2f,1));
            TestAssert.False(ManualInteractionPolicy.ShouldRefuelFire(true,true,false,false,false,0,10,.2f,1));
            TestAssert.True(ManualInteractionPolicy.AllowsProtection(false,0));
            TestAssert.True(ManualInteractionPolicy.AllowsProtection(true,1));
            TestAssert.False(ManualInteractionPolicy.AllowsProtection(true,0));
            TestAssert.False(ManualInteractionPolicy.AllowsProtection(true,2));
        }
    }
}

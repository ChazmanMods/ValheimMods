using System;
using System.Linq;
using RunicStorage.Runtime;

internal static class TransferRegressionTests
{
    private static void Check(bool condition, string message) { if(!condition)throw new InvalidOperationException(message); }
    private static ItemDrop.ItemData Item(int prefab, int count, int x, float durability=0f) => new ItemDrop.ItemData { Prefab=prefab,m_stack=count,m_gridPos=new Vector2i(x,0),m_durability=durability };
    private static Inventory Bag(params ItemDrop.ItemData[] items) {var i=new Inventory("test",null,8,4);i.GetAllItems().AddRange(items);return i;}
    private static byte[] Bytes(Inventory i)=>ValheimContainerService.SaveInventory(i).GetArray();

    internal static void DetachedStacksCannotMoveAgain()
    {
        var wood=Item(1,10,0);var source=Bag(wood);var preferred=Bag();var fallback=Bag();
        Check(ValheimContainerService.MoveUpTo(source,preferred,wood,4)==4,"Partial transfer must succeed.");
        Check(wood.m_stack==6&&ValheimContainerService.HasSourceStack(source,wood),"Partial stacks must remain eligible for other destinations.");
        Check(ValheimContainerService.MoveUpTo(source,preferred,wood,6)==6,"Remaining stack must transfer.");
        Check(wood.m_stack==6,"Native full removal retains the detached object's count.");
        Check(!ValheimContainerService.HasSourceStack(source,wood),"Routing must stop after the entire stack leaves inventory.");
        // A callback or another operation may reuse the cell. It is not the old stack.
        var replacement=Item(2,3,0);source.GetAllItems().Add(replacement);
        var before=Bytes(source);int callbacks=0;source.m_onChanged=()=>callbacks++;fallback.m_onChanged=()=>callbacks++;
        Check(ValheimContainerService.MoveUpTo(source,fallback,wood,6,out var failure)==0&&failure==StorageMoveFailure.InvalidInput,"Stale references must be rejected before publication.");
        Check(callbacks==0&&Bytes(source).SequenceEqual(before)&&fallback.GetAllItems().Count==0,"Rejecting a detached stack must not mutate or roll back either inventory.");
        Check(ValheimContainerService.MoveUpTo(source,fallback,replacement,3)==3,"Later inventory items must still transfer.");
        Check(preferred.GetAllItems().Sum(i=>i.m_stack)==10&&fallback.GetAllItems().Sum(i=>i.m_stack)==3,"Transfers must conserve all items.");
    }

    internal static void WornToolDoesNotBlockTransfer()
    {
        var tool=Item(2,1,0,10.029f);tool.m_equipped=true;tool.m_customData["owner"]="unchanged";
        var wood=Item(1,10,1);var source=Bag(tool,wood);var target=Bag(Item(1,20,0));
        var persisted=Bytes(source);var decoded=Bag();decoded.Load(new ZPackage(persisted));
        Check(!persisted.SequenceEqual(Bytes(decoded)),"Fixture must reproduce native save/load rounding.");
        var shadow=ValheimContainerService.CloneInventory(source);
        Check(Bytes(shadow).SequenceEqual(persisted),"Exact memory clone must preserve serialized bytes.");
        Check(shadow.GetAllItems()[0].m_durability==tool.m_durability,"Full float precision must survive cloning.");
        shadow.GetAllItems()[0].m_customData["owner"]="shadow";
        Check(tool.m_customData["owner"]=="unchanged","Custom metadata must not alias.");
        var p=new Player{Inventory=source};Player.m_localPlayer=p;
        Check(StorageMutationLease.TryBegin(p,out var lease),"Lease required.");
        using(lease)
        {
            int moved=ValheimContainerService.MoveUpTo(source,target,wood,10,out var failure,p,lease,null,new Container{Inventory=target});
            Check(moved==10&&failure==StorageMoveFailure.None,"Worn tool must not block wood transfer.");
        }
        Check(target.GetAllItems()[0].m_stack==30,"All wood arrived.");
        Check(ReferenceEquals(source.GetAllItems()[0],tool)&&tool.m_durability==10.029f&&tool.m_equipped,"Equipped tool must not be replaced or changed.");
    }

    internal static void RollbackPreservesReferencesAndMetadata()
    {
        var tool=Item(2,1,0,10.029f);tool.m_equipped=true;tool.m_customData["lock"]="yes";
        var wood=Item(1,10,1);var source=Bag(tool,wood);var existing=Item(1,20,0);var target=Bag(existing);
        var beforeSource=Bytes(source);var beforeTarget=Bytes(target);
        var p=new Player{Inventory=source};Player.m_localPlayer=p;
        int callbacks=0;
        target.m_onChanged=()=>{if(++callbacks==1){tool.m_customData["lock"]="changed";throw new InvalidOperationException("Injected publication failure");}};
        StorageMutationLease.TryBegin(p,out var lease);
        bool threw=false;
        using(lease)try{ValheimContainerService.MoveUpTo(source,target,wood,10,p,lease,null,new Container{Inventory=target});}catch{threw=true;}
        Check(threw,"Failure must propagate after rollback.");
        Check(Bytes(source).SequenceEqual(beforeSource)&&Bytes(target).SequenceEqual(beforeTarget),"Both inventories must roll back exactly.");
        Check(ReferenceEquals(source.GetAllItems()[0],tool)&&ReferenceEquals(source.GetAllItems()[1],wood)&&ReferenceEquals(target.GetAllItems()[0],existing),"Rollback must retain original item references.");
        Check(tool.m_durability==10.029f&&tool.m_equipped&&tool.m_customData["lock"]=="yes","Rollback must retain float precision, equipment and custom metadata.");
    }

    internal static void FullAndOwnershipFailuresAreDistinct()
    {
        var wood=Item(1,10,0);var source=Bag(wood);var full=new Inventory("full",null,1,1);full.GetAllItems().Add(Item(1,50,0));
        Check(ValheimContainerService.MoveUpTo(source,full,wood,10,out var f)==0&&f==StorageMoveFailure.NoCapacity,"Full must mean full.");
        var empty=Bag();var chest=new Container{Owner=false,Inventory=empty};
        Check(ValheimContainerService.MoveUpTo(source,empty,wood,10,out f,null,null,null,chest)==0&&f==StorageMoveFailure.OwnershipChanged,"Ownership must not mean full.");
        Check(wood.m_stack==10&&empty.GetAllItems().Count==0,"Rejected moves must change nothing.");
    }

    internal static void PermissionAndLeaseChecksRemain()
    {
        var source=Bag(Item(1,10,0));var p=new Player{Inventory=source};Player.m_localPlayer=p;bool threw=false;
        var publicChest=new Container{Inventory=Bag(),m_checkGuardStone=true};
        Check(ValheimContainerService.CanDiscover(publicChest,1,true),"Public chest must not need a separate authorization list.");
        PrivateArea.Allowed=false;
        Check(!ValheimContainerService.CanDiscover(publicChest,1,true),"Denied ward must still block discovery.");
        PrivateArea.Allowed=true;publicChest.AccessAllowed=false;
        Check(!ValheimContainerService.CanDiscover(publicChest,1,true),"Native chest access denial must remain enforced.");
        try{ValheimContainerService.MoveUpTo(source,Bag(),source.GetAllItems()[0],5,p);}catch(InvalidOperationException){threw=true;}
        Check(threw,"No player mutation without lease.");
        var other=Bag();var snapshot=new StorageInventorySnapshot(source);threw=false;
        try{snapshot.Restore(other);}catch(InvalidOperationException){threw=true;}
        Check(threw,"Snapshot cannot be applied to a different inventory.");
    }

    internal static void ChestPayloadAllowsOnlyNativeRounding()
    {
        var tool=Item(2,1,0,10.029f);tool.m_customData["lock"]="yes";
        var source=Bag(tool,Item(1,10,1));var before=Bytes(source);var loaded=Bag();loaded.Load(new ZPackage(before));
        var after=Bytes(loaded);
        Check(!before.SequenceEqual(after)&&StorageInventoryPayloadComparison.MatchesLoaded(before,after),"Accept precisely one native codec round trip.");
        loaded.GetAllItems()[1].m_stack++;
        Check(!StorageInventoryPayloadComparison.MatchesLoaded(before,Bytes(loaded)),"Reject quantity change.");
        loaded.Load(new ZPackage(before));loaded.GetAllItems()[0].m_customData["lock"]="bad";
        Check(!StorageInventoryPayloadComparison.MatchesLoaded(before,Bytes(loaded)),"Reject metadata change.");
        loaded.Load(new ZPackage(before));loaded.GetAllItems()[0].m_durability+=1f;
        Check(!StorageInventoryPayloadComparison.MatchesLoaded(before,Bytes(loaded)),"Reject arbitrary durability tolerance.");
        Check(!StorageInventoryPayloadComparison.MatchesLoaded(before,after.Take(after.Length-1).ToArray()),"Reject truncated payload.");
    }

    internal static void PartialStacksAndDifferentMetadataArePreserved()
    {
        var source=Bag(Item(1,10,0));var target=new Inventory("partial",null,1,1);target.GetAllItems().Add(Item(1,47,0));
        Check(ValheimContainerService.MoveUpTo(source,target,source.GetAllItems()[0],10)==3,"Move only available capacity.");
        Check(source.GetAllItems()[0].m_stack==7&&target.GetAllItems()[0].m_stack==50,"No loss or duplication on partial move.");
        var protectedItem=Item(1,1,0);protectedItem.m_customData["special"]="keep";var separate=Bag(protectedItem);
        Check(ValheimContainerService.MoveUpTo(source,separate,source.GetAllItems()[0],7)==7,"Different metadata should use an empty slot.");
        Check(protectedItem.m_stack==1&&protectedItem.m_customData["special"]=="keep"&&separate.GetAllItems().Count==2,"Do not merge away item metadata.");
    }
}

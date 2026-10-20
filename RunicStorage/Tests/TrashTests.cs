using System;
using System.Linq;
using RunicStorage.Runtime;
using RunicAutomation;

internal static class TrashTests
{
    private static void Check(bool value,string why) { if(!value)throw new Exception(why); }
    private static Inventory Bag()
    {
        var inventory=new Inventory("player",null,8,6);
        inventory.GetAllItems().AddRange(new[]{
            new ItemDrop.ItemData{Prefab=1,m_stack=12,m_gridPos=new Vector2i(0,1)},
            new ItemDrop.ItemData{Prefab=2,m_stack=8,m_gridPos=new Vector2i(1,2)},
            new ItemDrop.ItemData{Prefab=3,m_stack=1,m_gridPos=new Vector2i(0,0)},
            new ItemDrop.ItemData{Prefab=4,m_stack=1,m_gridPos=new Vector2i(0,5)} });
        return inventory;
    }
    private static bool Eligible(ItemDrop.ItemData i)=>TrashSelection.CellAllowed(i.m_gridPos.y,6,false,i.m_equipped,i.m_shared.m_questItem,i.m_stack);
    private static void Delete(Player player,TrashSelection selection,Func<ItemDrop.ItemData,bool> eligible=null)
    {
        Check(StorageMutationLease.TryBegin(player,out var lease),"lease");
        using(lease)TrashDeletion.Apply(player,player.Inventory,selection.Entries.ToArray(),eligible??Eligible,lease);
    }
    private static void Throws(Action action) {bool threw=false;try{action();}catch{threw=true;}Check(threw,"Expected refusal");}
    internal static void SelectionAndProtectedCells()
    {
        var inventory=Bag();var s=new TrashSelection();var items=inventory.GetAllItems();
        Check(s.Toggle(inventory,items[0],Eligible)&&s.Toggle(inventory,items[1],Eligible),"Multiple stacks selected");
        Check(s.Quantity==20&&s.Entries.Count==2,"Prompt uses individual item count");
        Check(!s.Toggle(inventory,items[2],Eligible)&&!s.Toggle(inventory,null,Eligible),"Hotbar and empty cells excluded");
        Check(s.Toggle(inventory,items[3],Eligible),"Ordinary bottom row is selectable without RunicInventory");
        s.Toggle(inventory,items[3],Eligible);
        Check(!TrashSelection.CellAllowed(5,6,false,false,false,1,5),"RunicInventory dedicated bottom row excluded");
        Check(!TrashSelection.CellAllowed(4,7,false,false,false,1,4),"Dedicated row remains excluded when rows are added below it");
        Check(TrashSelection.CellAllowed(5,7,false,false,false,1,4)&&TrashSelection.CellAllowed(6,7,false,false,false,1,4),"Ordinary rows below the dedicated row are selectable");
        Check(!TrashSelection.CellAllowed(6,7,true,false,false,1,4),"Locks protect an otherwise eligible bottom row");
        Check(!TrashSelection.CellAllowed(-1,6,false,false,false,1)&&!TrashSelection.CellAllowed(6,6,false,false,false,1),"Out-of-bounds rows excluded");
        Check(!TrashSelection.CellAllowed(2,6,true,false,false,1),"Locked cell excluded");
        Check(!TrashSelection.CellAllowed(2,6,false,true,false,1),"Equipped item excluded");
        Check(!TrashSelection.CellAllowed(2,6,false,false,true,1),"Quest item excluded");
        s.Toggle(inventory,items[0],Eligible);Check(s.Quantity==8,"Repeated chord toggles off");
        items[1].m_gridPos=new Vector2i(2,2);s.Prune(inventory,Eligible);Check(s.Entries.Count==0,"Moving selected item clears selection");
    }
    internal static void ConfirmDeletesOnlySelectedAndPreservesSurvivors()
    {
        MutationGate.EndSession();var inventory=Bag();var player=new Player{Inventory=inventory};Player.m_localPlayer=player;
        var s=new TrashSelection();var originals=inventory.GetAllItems().ToArray();
        s.Toggle(inventory,originals[0],Eligible);s.Toggle(inventory,originals[1],Eligible);
        int callbacks=0;inventory.m_onChanged=()=>callbacks++;
        Delete(player,s);
        Check(callbacks==1,"Exactly one inventory notification");
        Check(inventory.GetAllItems().Count==2&&ReferenceEquals(inventory.GetAllItems()[0],originals[2])&&ReferenceEquals(inventory.GetAllItems()[1],originals[3]),"Survivor references and protected cells unchanged");
        Check(!inventory.GetAllItems().Contains(originals[0]),"Selected items removed");
    }
    internal static void ChangedStacksAndNewLocksRejectWholeConfirmation()
    {
        MutationGate.EndSession();var inventory=Bag();var player=new Player{Inventory=inventory};Player.m_localPlayer=player;
        var s=new TrashSelection();var a=inventory.GetAllItems()[0];var b=inventory.GetAllItems()[1];
        s.Toggle(inventory,a,Eligible);s.Toggle(inventory,b,Eligible);
        a.m_stack++;string before=ValheimContainerService.Fingerprint(inventory);
        Throws(()=>Delete(player,s));Check(ValheimContainerService.Fingerprint(inventory)==before,"Changed count aborts ALL deletion");
        a.m_stack--;before=ValheimContainerService.Fingerprint(inventory);
        Throws(()=>Delete(player,s,i=>!ReferenceEquals(i,b)&&Eligible(i)));Check(ValheimContainerService.Fingerprint(inventory)==before,"Newly locked stack aborts ALL deletion");
        inventory.GetAllItems().Remove(a);var replacement=a.Clone();inventory.GetAllItems().Add(replacement);before=ValheimContainerService.Fingerprint(inventory);
        Throws(()=>Delete(player,s));Check(ValheimContainerService.Fingerprint(inventory)==before,"Replacement in same slot cannot inherit deletion selection");
    }
    internal static void FailureRestoresDeletionAndForeignChangesBlock()
    {
        MutationGate.EndSession();var inventory=Bag();var player=new Player{Inventory=inventory};Player.m_localPlayer=player;
        var s=new TrashSelection();var item=inventory.GetAllItems()[0];s.Toggle(inventory,item,Eligible);
        string before=ValheimContainerService.Fingerprint(inventory);int calls=0;
        inventory.m_onChanged=()=>{if(++calls==1)throw new Exception("injected callback failure");};
        Throws(()=>Delete(player,s));Check(ValheimContainerService.Fingerprint(inventory)==before&&ReferenceEquals(inventory.GetAllItems()[0],item),"Failed deletion restores all original items and references");
        inventory.m_onChanged=()=>inventory.GetAllItems()[0].m_customData["foreign"]="preserve";
        Throws(()=>Delete(player,s));Check(MutationGate.IsBlocked(inventory),"Uncertain foreign write blocks further mutation");
        Check(inventory.GetAllItems()[0].m_customData["foreign"]=="preserve","Unrelated edit not overwritten");MutationGate.EndSession();
    }
}

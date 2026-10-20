using System;
using System.Linq;
using System.Collections.Generic;
using RunicStorage.Engine;
using RunicStorage.Runtime;
using RunicAutomation;

internal static class SmartSortTests
{
    static void Check(bool ok, string why) { if (!ok) throw new Exception(why); }
    static ItemDrop.ItemData Item(int id, int quantity, int slot, string data = "") => new ItemDrop.ItemData {
        Prefab = id, m_stack = quantity, m_gridPos = new Vector2i(slot, 0), m_customData = new Dictionary<string,string> { ["custom"] = data } };
    static Inventory Chest(int slots, params ItemDrop.ItemData[] items) { var c = new Inventory("chest", null, slots, 1); c.GetAllItems().AddRange(items); return c; }
    static string Category(ItemDrop.ItemData i) => i.Prefab.ToString();
    static string Fingerprint(Inventory i) => ValheimContainerService.Fingerprint(i);
    static SmartSortTransaction.Endpoint[] Plan(Inventory[] source)
    {
        var planned = SmartSortPlanner.Build(source, Category);
        return source.Select((c,i) => new SmartSortTransaction.Endpoint { Live=c, Planned=planned[i], Before=new StorageInventorySnapshot(c), Authorized=()=>true, Persisted=()=>true }).ToArray();
    }
    static void Apply(SmartSortTransaction.Endpoint[] endpoints)
    {
        Check(MutationGate.TryBegin("smart-sort-test", out var lease), "lease");
        using (lease) SmartSortTransaction.Apply(endpoints);
    }
    static void Throws(Action action) { bool threw = false; try { action(); } catch { threw=true; } Check(threw,"Expected rejection"); }

    internal static void FullChestsAndConsolidation()
    {
        MutationGate.EndSession();
        var source = new[] { Chest(2,Item(1,50,0),Item(2,50,1)), Chest(2,Item(2,50,0),Item(1,50,1)) };
        var before = source.Select(Fingerprint).ToArray();
        var plan = Plan(source);
        Check(source.Select(Fingerprint).SequenceEqual(before), "Planning must not change live chests");
        Check(plan.All(e=>e.Planned.GetAllItems().Select(Category).Distinct().Count()==1), "Full chests become dedicated without spare slots");
        int callbacks=0;
        foreach(var c in source)c.m_onChanged=()=>{callbacks++; Check(source.All(s=>s.GetAllItems().Select(Category).Distinct().Count()==1),"Observers see whole final state");};
        Apply(plan); Check(callbacks==2,"One publication per chest");
        var again=SmartSortPlanner.Build(source,Category);
        Check(source.Select(Fingerprint).SequenceEqual(again.Select(Fingerprint)),"Already organized is stable");
        var partial=new[]{Chest(2,Item(1,20,0)),Chest(2,Item(1,25,0))};
        var combined=SmartSortPlanner.Build(partial,Category);
        Check(combined.Sum(c=>c.GetAllItems().Count)==1 && combined.SelectMany(c=>c.GetAllItems()).Single().m_stack==45,"Partial stacks consolidate");
    }
    internal static void MetadataCapacityAndRandomizedConservation()
    {
        var items=new[]{Chest(2,Item(1,20,0,"A"),Item(1,20,1,"B")),Chest(1)};
        items[0].GetAllItems()[0].m_durability=10.029f;
        var result=SmartSortPlanner.Build(items,Category);
        Check(result.Sum(c=>c.GetAllItems().Count)==2,"Distinct custom data cannot merge");
        Check(result.SelectMany(c=>c.GetAllItems()).Single(i=>i.m_customData["custom"]=="A").m_durability==10.029f,"Full precision preserved");
        var rng=new Random(7301);
        for(int run=0;run<200;run++)
        {
            var source=Enumerable.Range(0,rng.Next(2,10)).Select(_=> {
                int slots=rng.Next(1,10); return Chest(slots,Enumerable.Range(0,rng.Next(slots+1)).Select(x=>Item(rng.Next(1,9),rng.Next(1,51),x,rng.Next(3).ToString())).ToArray()); }).ToArray();
            var planned=SmartSortPlanner.Build(source,Category);
            SmartSortPlanner.VerifyConservation(source,planned);
            Check(planned.All(i=>i.GetAllItems().Count<=i.GetWidth()*i.GetHeight()),"No capacity overflow");
            Check(planned.All(i=>i.GetAllItems().Select(x=>x.m_gridPos.x).Distinct().Count()==i.GetAllItems().Count),"No slot overlap");
        }
    }
    internal static void StaleDeniedAndInvalidPlans()
    {
        MutationGate.EndSession();
        var source=new[]{Chest(2,Item(1,20,0)),Chest(2,Item(2,20,0))};
        var plan=Plan(source); source[0].GetAllItems()[0].m_stack++;
        var before=source.Select(Fingerprint).ToArray();
        Throws(()=>Apply(plan)); Check(source.Select(Fingerprint).SequenceEqual(before),"Stale preview changes nothing");
        plan=Plan(source); plan[1].Authorized=()=>false;
        Throws(()=>Apply(plan)); Check(source.Select(Fingerprint).SequenceEqual(before),"Access denial changes nothing");
        Throws(()=>SmartSortPlanner.Build(new[]{Chest(2,Item(1,1,0),Item(2,1,0))},Category));
        Throws(()=>SmartSortPlanner.Build(new[]{Chest(2,Item(1,51,0))},Category));
        plan=Plan(source); plan[0].Planned.GetAllItems()[0].m_stack++;
        Throws(()=>Apply(plan)); Check(source.Select(Fingerprint).SequenceEqual(before),"Broken conservation changes nothing");
    }
    internal static void PublicationRollbackAndForeignEdits()
    {
        MutationGate.EndSession();
        var source=new[]{Chest(2,Item(1,20,0)),Chest(2,Item(1,20,0))};
        var plan=Plan(source); var before=source.Select(Fingerprint).ToArray(); var original=source[0].GetAllItems()[0];
        int calls=0; source[1].m_onChanged=()=>{if(++calls==1)throw new Exception("injected failure");};
        Throws(()=>Apply(plan)); Check(source.Select(Fingerprint).SequenceEqual(before),"Rollback restores every chest");
        Check(ReferenceEquals(source[0].GetAllItems()[0],original),"Rollback restores references");
        source[1].m_onChanged=null; plan=Plan(source);
        source[0].m_onChanged=()=>{source[0].GetAllItems()[0].m_customData["external"]="keep";};
        Throws(()=>Apply(plan));
        Check(source.All(MutationGate.IsBlocked),"Uncertain endpoints all blocked");
        Check(source[0].GetAllItems()[0].m_customData["external"]=="keep","Foreign edit never overwritten");
        MutationGate.EndSession();
    }
    internal static void CategoriesAndManualOptOut()
    {
        Check(SmartSortCategories.For(new ChestItemFacts{Id="Wood",Ingredient=true})=="resource:wood","Specific wood beats ingredient");
        Check(SmartSortCategories.For(new ChestItemFacts{Id="Iron"})=="resource:bars","Iron bars");
        Check(SmartSortCategories.For(new ChestItemFacts{Id="ModdedThing"})=="other:misc","Unknown supported");
        Check(!SmartSortCategories.HasManualRules(new ChestRules()),"Untouched chest eligible");
        Check(SmartSortCategories.HasManualRules(new ChestRules{Label="Do not touch"}),"Custom label excluded");
        Check(SmartSortCategories.HasManualRules(new ChestRules{Remember=true}),"Remembered chest excluded");
        var rules=new ChestRules();rules.Excluded.Add("Wood");Check(SmartSortCategories.HasManualRules(rules),"Exclusions preserved");
    }
    internal static void PersistenceAndAuthorityFailure()
    {
        MutationGate.EndSession();
        var source=new[]{Chest(2,Item(1,20,0)),Chest(2,Item(1,20,0))};
        var before=source.Select(Fingerprint).ToArray(); var plan=Plan(source);
        int verified=0; plan[0].Persisted=()=>++verified>1;
        Throws(()=>Apply(plan));
        Check(source.Select(Fingerprint).SequenceEqual(before),"Failed persistence verification restores all inventories");
        plan=Plan(source); bool owned=true;
        plan[1].Authorized=()=>owned; source[0].m_onChanged=()=>owned=false;
        Throws(()=>Apply(plan)); Check(source.All(MutationGate.IsBlocked),"Lost authority blocks all participating inventories");
        MutationGate.EndSession();
    }

    internal static void ConfiguredDestinationsPreserveContentsAndOverflow()
    {
        var pinned = Chest(2, Item(1,40,1,"wood"), Item(9,1,0,"keep-me"));
        var source = new[] { pinned, Chest(3, Item(1,30,0,"wood"),Item(2,25,1)), Chest(2) };
        Func<ItemDrop.ItemData,int>[] rules = { i=>i.Prefab==1?0:int.MaxValue, null, null };
        var before = source.Select(Fingerprint).ToArray();
        var planned = SmartSortPlanner.Build(source,Category,rules);
        Check(source.Select(Fingerprint).SequenceEqual(before),"Destination planning never mutates live inventories");
        Check(planned[0].GetItemAt(1,0).m_stack==50,"Matching destination stack filled first");
        Check(planned[0].GetItemAt(0,0).Prefab==9 && planned[0].GetItemAt(0,0).m_customData["custom"]=="keep-me","Original destination contents and slots preserved even if outside rule");
        Check(planned.Skip(1).SelectMany(i=>i.GetAllItems()).Where(i=>i.Prefab==1).Sum(i=>i.m_stack)==20,"Overflow stays in automatic storage");
        Check(planned[0].GetAllItems().All(i=>i.Prefab!=2),"Rejected item never enters destination");
        SmartSortPlanner.VerifyConservation(source,planned);
    }

    internal static void ConfiguredRulesPriorityAndEmptyDestinations()
    {
        var broad = new ChestRules(); broad.Categories.Add("resource:wood");
        var specific = new ChestRules(); specific.Items.Add("Wood");
        var excluded = new ChestRules(); excluded.Categories.Add("resource:wood"); excluded.Excluded.Add("Wood");
        var remembered = new ChestRules{Remember=true}; remembered.Memory.Add("Iron");
        var source = new[]{Chest(2),Chest(2),Chest(2),Chest(2),Chest(4,Item(1,20,0),Item(2,20,1),Item(3,20,2))};
        ChestItemFacts Facts(ItemDrop.ItemData i)=>new ChestItemFacts{Id=i.Prefab==1?"Wood":i.Prefab==2?"Stone":"Iron"};
        var destinations=new Func<ItemDrop.ItemData,int>[] {
            i=>broad.RoutingScore(Facts(i),false), i=>specific.RoutingScore(Facts(i),false),
            i=>excluded.RoutingScore(Facts(i),false),i=>remembered.RoutingScore(Facts(i),false),null };
        var plan=SmartSortPlanner.Build(source,Category,destinations);
        Check(plan[0].GetAllItems().Count==0 && plan[1].GetAllItems().Single().Prefab==1,"Exact item rule beats category even when empty");
        Check(plan[2].GetAllItems().Count==0,"Never-accept beats category");
        Check(plan[3].GetAllItems().Single().Prefab==3,"Remembered empty destination accepts matching resource");
        Check(plan[4].GetAllItems().Single().Prefab==2,"Unmatched item retained in automatic storage");
        broad.Preferred=true; specific.Items.Clear(); specific.Categories.Add("resource:wood");
        plan=SmartSortPlanner.Build(source,Category,destinations);
        Check(plan[0].GetAllItems().Single().Prefab==1,"Preferred flag wins equal category rules");
    }

    internal static void IncrementalPreviewIsCancellableAndEquivalent()
    {
        var source=new[]{Chest(4,Item(1,20,0),Item(2,30,1)),Chest(4,Item(1,15,0)),Chest(4)};
        var before=source.Select(Fingerprint).ToArray();
        List<Inventory> completed=null;
        using(var steps=SmartSortPlanner.BuildSteps(source,Category,null,result=>completed=result).GetEnumerator())
        {
            Check(steps.MoveNext(),"Preview provides a yield boundary before completion");
            Check(completed==null,"No premature completed preview");
        }
        Check(source.Select(Fingerprint).SequenceEqual(before),"Cancelling preview changes no inventory");
        int boundaries=0;
        foreach(var step in SmartSortPlanner.BuildSteps(source,Category,null,result=>completed=result))boundaries++;
        var synchronous=SmartSortPlanner.Build(source,Category);
        Check(boundaries>=4,"Planning yields across stacks and verification");
        Check(completed.Select(Fingerprint).SequenceEqual(synchronous.Select(Fingerprint)),"Incremental planner preserves exact result");
        Check(source.Select(Fingerprint).SequenceEqual(before),"Completed preview remains read-only");
    }
}

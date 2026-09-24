using System;
using System.Collections.Generic;
using System.Linq;
using RunicCrafting.Domain;
using RunicCrafting.Integration;
using UnityEngine;

internal static class Program
{
    private static Player player;
    private static CraftingStation station;
    private static ContainerQueryRuntime query;
    private static readonly MaterialRequirement[] wood = { new MaterialRequirement("Wood", 4) };
    private static IReadOnlyList<IMutableMaterialSource> Resolve(MaterialRequirement[] requirements = null,
        CraftingStation anchor = null, float radius = 20, bool writable = false, Vector3 origin = default) =>
        query.ResolveSources(player,anchor ?? station,origin,radius,requirements ?? wood,"test",
            false,out _,requireWritable:writable);
    private static void Check(bool condition,string message) { if(!condition) throw new Exception(message); }
    private static void Reset()
    {
        PreviewRefreshRuntime.Reset(); Time.frameCount++;
        UiPreviewCache.Reset(); Time.realtimeSinceStartup=0;
        CachePerformance.UiHits=CachePerformance.UiMisses=CachePerformance.RefreshHits=0;
        PreviewRefreshRuntime.LoadingPreview=null;
        Game.m_worldLevel=0;
        RunicCrafting.Configuration.SafeMaximumCandidates=64;
        RunicCrafting.Configuration.SafeMaximumReturned=32;
        RunicCrafting.Configuration.PullPrefabIds.Value="piece_drawer";
        RunicCrafting.Configuration.Enabled.Value=true;
        PostCraftRefreshRuntime.Reset();
        CraftingRuntime.HasMaterialOperation=false;
        InventoryGui.instance=new InventoryGui(); InventoryGui.Visible=true;
        Runic.Compatibility.ModdedContainerCompatibility.StoragePullIds=null;
        player = Player.m_localPlayer = new Player(); station = new CraftingStation();
        query = new ContainerQueryRuntime(new WorkshopAccessRuntime());
        ContainerSpatialIndex.Queries=ValheimReflection.Reads=ValheimReflection.AccessChecks=ValheimReflection.WritableRefreshes=0;
        ValheimReflection.DuringRead=null; PrivateArea.Allowed=true;
        ContainerSpatialIndex.Containers.Clear();
        player.Inventory.Items.Add(new ItemDrop.ItemData { Resource="Wood",m_stack=2 });
        for(int i=0;i<19;i++)
        {
            var chest=new Container { Id="chest:"+i };
            chest.Inventory.Items.Add(new ItemDrop.ItemData { Resource="Wood",m_stack=50 });
            chest.Inventory.Items.Add(new ItemDrop.ItemData { Resource="Stone",m_stack=10 });
            ContainerSpatialIndex.Containers.Add(chest);
        }
    }
    private static void BurstSharesAllResources()
    {
        long scope=PreviewRefreshRuntime.Begin();
        try
        {
            var first=Resolve();
            for(int i=0;i<500;i++)
            {
                var requirements=new[] { new MaterialRequirement(i%2==0?"Wood":"Stone",4) };
                var next=Resolve(requirements);
                Check(ReferenceEquals(first,next),"Repeated recipe rebuilt the source list");
                Check(new ExactMaterialPlanner().TryPlan(requirements,next.Select(s=>s.Snapshot()),out var plan,out _),"Different resource was omitted");
                if(i%2==0) Check(plan.Lines[0].SourceId=="valheim.player:1" && plan.Lines[0].Quantity==2,"Carried priority changed");
            }
            Check(ContainerSpatialIndex.Queries==1 && ValheimReflection.Reads==19 && ValheimReflection.AccessChecks==19,"Query/read/access counts were not reduced to one pass");
            foreach(var source in first) Check(source is ReadOnlyMaterialSource && !source.TryTake("Wood",1,out _),"Refresh result exposed writable inventory");
        }
        finally { PreviewRefreshRuntime.End(scope); }
        Check(!PreviewRefreshRuntime.Cache.Active,"Refresh scope leaked");
        Resolve(); Resolve();
        Check(ContainerSpatialIndex.Queries==1,"Separate same-frame previews did not share sources");
        Time.frameCount++; Resolve();
        Check(ContainerSpatialIndex.Queries==2,"Next frame reused old sources");
    }
    private static void ScopeAndContextIsolation()
    {
        long scope=PreviewRefreshRuntime.Begin();
        var first=Resolve();
        long nested=PreviewRefreshRuntime.Begin(); Check(ReferenceEquals(first,Resolve()),"Nested refresh did not share");
        PreviewRefreshRuntime.End(nested);
        Resolve(anchor:new CraftingStation()); Resolve(radius:10); Resolve(origin:new Vector3(1,0,0));
        Game.m_worldLevel=1; Resolve(); Game.m_worldLevel=0;
        Check(ContainerSpatialIndex.Queries==5,"Anchor/range/origin/world-level contexts collided");
        PreviewRefreshRuntime.End(scope);
        scope=PreviewRefreshRuntime.Begin(); Resolve(); PreviewRefreshRuntime.End(scope);
        Check(ContainerSpatialIndex.Queries==5,"Same-frame refresh repeated unchanged query");
    }
    private static void MutationAndAccessRevocationInvalidate()
    {
        long scope=PreviewRefreshRuntime.Begin();
        var old=Resolve();
        player.Inventory.Items[0].m_stack=0;
        ContainerSpatialIndex.Containers[0].Inventory.Items[0].m_stack=0;
        ContainerSpatialIndex.Containers[1].Access=false;
        PreviewRefreshRuntime.Invalidate();
        var next=Resolve();
        Check(!ReferenceEquals(old,next),"Mutation returned stale sources");
        Check(next[0].Snapshot().Available("Wood")==0 && next[1].Snapshot().Available("Wood")==0,"Mutation returned stale counts");
        Check(next.All(s=>s.SourceId!="chest:1"),"Denied chest survived invalidation");
        PrivateArea.Allowed=false; PreviewRefreshRuntime.Invalidate();
        Check(Resolve().Count==1,"Ward revocation retained chest counts");
        PreviewRefreshRuntime.End(scope);
    }
    private static void MutationDuringBuildIsNotPublished()
    {
        long scope=PreviewRefreshRuntime.Begin();
        ValheimReflection.DuringRead=PreviewRefreshRuntime.Invalidate;
        Resolve();
        Check(PreviewRefreshRuntime.Cache.Count==0,"Mixed-epoch snapshot was cached");
        ValheimReflection.DuringRead=null;
        Resolve(); Resolve();
        Check(ContainerSpatialIndex.Queries==2,"Stable rebuild not reused");
        PreviewRefreshRuntime.End(scope);
    }
    private static void WritablePathAlwaysResolvesFresh()
    {
        long scope=PreviewRefreshRuntime.Begin(); Resolve();
        var writable=Resolve(writable:true);
        Check(writable.All(s=>s is ValheimMaterialSource),"Writer received read-only cache");
        Resolve(writable:true);
        Check(ContainerSpatialIndex.Queries==3 && ValheimReflection.WritableRefreshes==38,"Writes reused queries");
        Resolve(); Check(ContainerSpatialIndex.Queries==4,"Write did not invalidate earlier preview");
        PreviewRefreshRuntime.End(scope);
    }
    private static void LimitsAndStationlessAccessArePreserved()
    {
        RunicCrafting.Configuration.SafeMaximumCandidates=5;
        RunicCrafting.Configuration.SafeMaximumReturned=2;
        long scope=PreviewRefreshRuntime.Begin();
        Check(Resolve().Count==3,"Returned-container limit changed");
        var denied=query.ResolveSources(player,null,default,20,wood,"stationless",false,out var reason);
        Check(denied.Count==1 && reason=="station-required-for-nearby-materials","Unauthorized stationless materials exposed");
        var allowed=query.ResolveSources(player,null,default,20,wood,"stationless",true,out _);
        Check(allowed.Count==3 && !ReferenceEquals(denied,allowed),"Stationless authorization keys collided");
        station.Access=false; PreviewRefreshRuntime.Invalidate();
        query.ResolveSources(player,station,default,20,wood,"test",false,out reason);
        Check(reason=="station-use:denied","Workshop denial was cached as allowed");
        PreviewRefreshRuntime.End(scope);
    }
    private static void EndRunsAfterExceptionAndLimitsStayBounded()
    {
        var cache=new RefreshQueryCache<int,object>(2);
        object n=new object(),p=new object(),d=new object();
        long scope=cache.Begin(1,n,p,d);
        try { cache.Store(1,new object(),cache.Epoch); throw new InvalidOperationException(); }
        catch(InvalidOperationException) { }
        finally { cache.End(scope); }
        Check(!cache.Active && cache.Count==0,"Exceptional refresh leaked");
        scope=cache.Begin(1,n,p,d);
        cache.Store(1,new object(),cache.Epoch); cache.Store(2,new object(),cache.Epoch); cache.Store(3,new object(),cache.Epoch);
        Check(cache.Count==2,"Cache exceeded bound");
        long fresh=cache.Begin(2,n,p,d); Check(cache.Count==0,"Cross-frame cache leak");
        cache.End(scope); Check(cache.Active,"Stale finalizer closed new scope"); cache.End(fresh);
        foreach(int change in new[]{0,1,2})
        {
            scope=cache.Begin(3,n,p,d); cache.Store(1,new object(),cache.Epoch);
            fresh=cache.Begin(3,change==0?new object():n,change==1?new object():p,change==2?new object():d);
            Check(cache.Count==0,"Session/player/database cache leak"); cache.End(fresh); cache.End(scope);
        }
    }
    private static int plans;
    private static bool UiAnswer(MaterialRequirement[] requirements, Vector3 origin = default, float radius = 20, bool stationless = false)
    {
        CraftingStation anchor=stationless?null:station;
        bool memo = UiPreviewCache.TryKey(player,anchor,origin,radius,requirements,"ui",stationless,out var key);
        if(memo && UiPreviewCache.TryGet(key,out var cached)) return cached.Available;
        long epoch=UiPreviewCache.Epoch;
        var sources=query.ResolveSources(player,anchor,origin,radius,requirements,"ui",stationless,out _,
            allowRefreshCache:true);
        plans++;
        bool available=new ExactMaterialPlanner().TryPlan(requirements,sources.Select(s=>s.Snapshot()),out _,out string reason);
        if(memo) UiPreviewCache.Store(key,available,reason,epoch);
        return available;
    }
    private static void FiveSecondUiWorkloadReusesAnswersAcrossFrames()
    {
        plans=0;
        for(int frame=0;frame<300;frame++)
        {
            Time.frameCount++; Time.realtimeSinceStartup=frame/60f;
            long scope=PreviewRefreshRuntime.Begin();
            try
            {
                for(int piece=0;piece<100;piece++)
                    Check(UiAnswer(new[]{new MaterialRequirement(piece%2==0?"Wood":"Stone",piece%16+1)}),"Available recipe became unavailable");
            }
            finally { PreviewRefreshRuntime.End(scope); }
        }
        Check(ContainerSpatialIndex.Queries>=19 && ContainerSpatialIndex.Queries<=21,"Expected about 20 source queries per five seconds");
        Check(plans<=336 && CachePerformance.UiHits>29600,"Repeated frames reran planning");
        Console.WriteLine("PROFILE-MODEL: queries="+ContainerSpatialIndex.Queries+", chest reads="+ValheimReflection.Reads+
            ", plans="+plans+", memo hits="+CachePerformance.UiHits+" (30,000 UI checks / simulated 5s)");
    }
    private static void AnswerMutationMovementAndActionsStayFresh()
    {
        long scope=PreviewRefreshRuntime.Begin();
        Check(UiAnswer(wood),"Initial availability");
        int original=ContainerSpatialIndex.Queries;
        UiPreviewCache.Changed(new ZDO());
        Check(UiAnswer(wood) && ContainerSpatialIndex.Queries==original,"Unrelated world object flushed answers");
        foreach(var c in ContainerSpatialIndex.Containers) c.Inventory.Items.Clear();
        UiPreviewCache.Changed(ContainerSpatialIndex.Containers[0].View.Data);
        PreviewRefreshRuntime.Invalidate();
        Check(!UiAnswer(wood),"Watched chest change returned stale positive answer");
        player.Inventory.Items[0].m_stack=5;
        UiPreviewCache.Changed(player.Inventory); PreviewRefreshRuntime.Invalidate();
        Check(UiAnswer(wood),"Inventory change retained negative answer");
        original=ContainerSpatialIndex.Queries;
        Check(UiAnswer(wood,new Vector3(0.01f,0,0)) && ContainerSpatialIndex.Queries==original+1,"Position was quantized");
        UiAnswer(wood,radius:10); Check(ContainerSpatialIndex.Queries==original+2,"Range omitted from key");
        PreviewRefreshRuntime.EnterAction();
        try
        {
            original=ContainerSpatialIndex.Queries;
            UiAnswer(wood); UiAnswer(wood);
            Check(ContainerSpatialIndex.Queries==original+1,"Action preview did not share fresh read-only sources");
        }
        finally { PreviewRefreshRuntime.ExitAction(); }
        PreviewRefreshRuntime.End(scope);
        Check(!PreviewRefreshRuntime.InAction,"Action guard leaked");
    }
    private static void FreshCraftDecisionBypassesAllAnswerAndRefreshCaches()
    {
        long scope=PreviewRefreshRuntime.Begin();
        UiAnswer(wood);
        player.Inventory.Items[0].m_stack=0;
        // Even without a change notification, fresh query/planning cannot borrow the cached UI answer.
        foreach(var c in ContainerSpatialIndex.Containers) c.Inventory.Items.Clear();
        var fresh=query.ResolveSources(player,station,default,20,wood,"craft-click",false,out _,allowRefreshCache:false);
        Check(!new ExactMaterialPlanner().TryPlan(wood,fresh.Select(s=>s.Snapshot()),out _,out _),"Fresh craft decision consumed UI cache");
        PreviewRefreshRuntime.End(scope);
    }
    private static void MovingHammerSharesExactOriginWithinRefresh()
    {
        for(int frame=0;frame<60;frame++)
        {
            Time.frameCount++; Time.realtimeSinceStartup=frame/60f;
            long scope=PreviewRefreshRuntime.Begin();
            try
            {
                for(int piece=0;piece<100;piece++)
                    Check(UiAnswer(new[]{new MaterialRequirement(piece%2==0?"Wood":"Stone",piece%16+1)},
                        new Vector3(frame*0.05f,0,0),stationless:true),"Moving hammer preview failed");
            }
            finally { PreviewRefreshRuntime.End(scope); }
        }
        Check(ContainerSpatialIndex.Queries==60 && ValheimReflection.Reads==1140,"Moving hammer repeated candidate walks within a refresh");
    }
    private static void ExactRangeEdgesAndOwnershipAreNotReused()
    {
        player.Inventory.Items.Clear();
        foreach(var c in ContainerSpatialIndex.Containers.Skip(1)) c.isActiveAndEnabled=false;
        ContainerSpatialIndex.Containers[0].transform.position=new Vector3(20,0,0);
        long scope=PreviewRefreshRuntime.Begin();
        Check(UiAnswer(wood,stationless:true),"Boundary chest excluded");
        Check(!UiAnswer(wood,new Vector3(-0.01f,0,0),stationless:true),"Out-of-range chest reused by position bucket");
        player.Owner=false;
        Check(!UiAnswer(wood,stationless:true),"Lost player ownership reused availability");
        PreviewRefreshRuntime.End(scope);
    }
    private static void CraftingOwnsCustomContainerSelection()
    {
        var drawer = ContainerSpatialIndex.Containers[0]; drawer.Drawer = true;
        foreach (bool writable in new[] { false, true }) {
            Runic.Compatibility.ModdedContainerCompatibility.StoragePullIds = "";
            RunicCrafting.Configuration.PullPrefabIds.Value = "other; piece_drawer";
            PreviewRefreshRuntime.Invalidate();
            Check(Resolve(writable:writable).Any(s => s.SourceId == drawer.Id),
                "Storage disabled Crafting's configured drawer");
            Runic.Compatibility.ModdedContainerCompatibility.StoragePullIds = "piece_drawer";
            RunicCrafting.Configuration.PullPrefabIds.Value = "";
            PreviewRefreshRuntime.Invalidate();
            var sources = Resolve(writable:writable);
            Check(!sources.Any(s => s.SourceId == drawer.Id), "Storage enabled a drawer disabled in Crafting");
            Check(sources.Any(s => s.SourceId == ContainerSpatialIndex.Containers[1].Id),
                "Disabling custom adapters disabled ordinary containers");
            RunicCrafting.Configuration.PullPrefabIds.Value = "piece_draw;*";
            PreviewRefreshRuntime.Invalidate();
            Check(!Resolve(writable:writable).Any(s => s.SourceId == drawer.Id), "Unlisted drawer selected");
        }
    }
    private static int Main()
    {
        Action[] tests={SeparateBuildCallsShareUntilMutation,ActionPreviewBurstStillChecksWrites,
            DeferredRefreshCoalescesAndUnwinds,DeferredRefreshCancelsOnContextChange,
            CraftingOwnsCustomContainerSelection,BurstSharesAllResources,ScopeAndContextIsolation,MutationAndAccessRevocationInvalidate,
            MutationDuringBuildIsNotPublished,WritablePathAlwaysResolvesFresh,LimitsAndStationlessAccessArePreserved,
            EndRunsAfterExceptionAndLimitsStayBounded,FiveSecondUiWorkloadReusesAnswersAcrossFrames,
            AnswerMutationMovementAndActionsStayFresh,FreshCraftDecisionBypassesAllAnswerAndRefreshCaches,
            MovingHammerSharesExactOriginWithinRefresh,ExactRangeEdgesAndOwnershipAreNotReused};
        int failures=0;
        foreach(var test in tests) { Reset(); try { test(); Console.WriteLine("PASS "+test.Method.Name); } catch(Exception ex) { failures++; Console.WriteLine("FAIL "+test.Method.Name+": "+ex); } }
        Console.WriteLine((tests.Length-failures)+"/"+tests.Length+" refresh integration tests passed.");
        return failures==0?0:1;
    }

    private static void SeparateBuildCallsShareUntilMutation()
    {
        // Model Show All calling separate preview hooks for hundreds of pieces.
        for (int i=0;i<400;i++)
        {
            long scope=PreviewRefreshRuntime.Begin();
            Resolve(new[] { new MaterialRequirement(i%2==0?"Wood":"Stone",i+1) });
            PreviewRefreshRuntime.End(scope);
        }
        Check(ContainerSpatialIndex.Queries==1,"Build calls repeated scans in the same frame");
        ContainerSpatialIndex.Containers[0].Access=false;
        PreviewRefreshRuntime.Invalidate(); // Between scopes, e.g. multiplayer access change.
        Check(Resolve().All(s=>s.SourceId!="chest:0"),"Between-scope invalidation lost");
        Check(ContainerSpatialIndex.Queries==2,"Access change failed to rebuild");
        Time.frameCount++; Resolve();
        Check(ContainerSpatialIndex.Queries==3,"Frame boundary reused snapshot");
        Player.m_localPlayer=player=new Player(); Resolve();
        Check(ContainerSpatialIndex.Queries==4,"Player change reused snapshot");
        ZNet.instance=new ZNet(); Resolve();
        Check(ContainerSpatialIndex.Queries==5,"Session change reused snapshot");
        ObjectDB.instance=new ObjectDB(); Resolve();
        Check(ContainerSpatialIndex.Queries==6,"Database change reused snapshot");
    }

    private static void ActionPreviewBurstStillChecksWrites()
    {
        Resolve();
        PreviewRefreshRuntime.EnterAction();
        try
        {
            for(int i=0;i<100;i++) Resolve();
            Check(ContainerSpatialIndex.Queries==2,"Craft action preview burst did not share");
            Check(Resolve().All(s=>s is ReadOnlyMaterialSource),"Preview is writable");
            ContainerSpatialIndex.Containers[0].Busy=true;
            var writable=Resolve(writable:true);
            Check(writable.All(s=>s.SourceId!="chest:0"),"Writer reused cached busy chest");
            Resolve(writable:true);
            Check(ContainerSpatialIndex.Queries==4,"Writer cached sources");
            Resolve(); Resolve();
            Check(ContainerSpatialIndex.Queries==5,"Preview not rebuilt after write");
        }
        finally { PreviewRefreshRuntime.ExitAction(); }
        Resolve(); Check(ContainerSpatialIndex.Queries==6,"Transaction exit retained preview");
    }

    private static void DeferredRefreshCoalescesAndUnwinds()
    {
        var gui=InventoryGui.instance;
        PostCraftRefreshRuntime.Schedule(player); PostCraftRefreshRuntime.Schedule(player);
        PostCraftRefreshRuntime.Tick(); Time.frameCount++; PostCraftRefreshRuntime.Tick();
        Check(gui.Refreshes==0,"Refresh ran in craft completion frame");
        Time.frameCount++;
        PreviewRefreshRuntime.EnterAction();
        PostCraftRefreshRuntime.Tick(); Check(gui.Refreshes==0,"Refresh ran inside action");
        PreviewRefreshRuntime.ExitAction();
        CraftingRuntime.HasMaterialOperation=true;
        PostCraftRefreshRuntime.Tick(); Check(gui.Refreshes==0,"Refresh ran before lease completion");
        CraftingRuntime.HasMaterialOperation=false;
        gui.DuringRefresh=()=> {
            Check(PostCraftRefreshRuntime.Refreshing,"Reentry guard missing inside callback");
            PostCraftRefreshRuntime.Schedule(player); PostCraftRefreshRuntime.Tick();
            for(int i=0;i<50;i++) Resolve();
            throw new InvalidOperationException("callback");
        };
        int before=ContainerSpatialIndex.Queries;
        PostCraftRefreshRuntime.Tick();
        Check(gui.Refreshes==1 && ContainerSpatialIndex.Queries==before+1,"Deferred burst did not coalesce/cache");
        Check(!PostCraftRefreshRuntime.Refreshing && !PreviewRefreshRuntime.Cache.Active,"Exception leaked guard/scope");
        Time.frameCount+=3; PostCraftRefreshRuntime.Tick();
        Check(gui.Refreshes==1,"Recursive callback left queued refresh");
        gui.DuringRefresh=null;
        PostCraftRefreshRuntime.Schedule(player); Time.frameCount+=2; PostCraftRefreshRuntime.Tick();
        Check(gui.Refreshes==2,"Later legitimate refresh suppressed");
    }

    private static void DeferredRefreshCancelsOnContextChange()
    {
        Action[] changes={ ()=>InventoryGui.Visible=false,
            ()=>player.Station=new CraftingStation(), ()=>Player.m_localPlayer=new Player(),
            ()=>ZNet.instance=new ZNet(), ()=>InventoryGui.instance=new InventoryGui(),
            ()=>RunicCrafting.Configuration.Enabled.Value=false };
        foreach(var change in changes)
        {
            Reset();
            var gui=InventoryGui.instance;
            PostCraftRefreshRuntime.Schedule(player); change(); Time.frameCount+=2;
            PostCraftRefreshRuntime.Tick();
            Check(gui.Refreshes==0,"Stale UI context refreshed");
        }
    }
}

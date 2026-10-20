using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace RunicPaths;

// Render legacy journals through their unmodified baseline without rewriting the
// world. An interrupted upgrade cannot leave half the old terrain permanently undone.
[HarmonyPatch(typeof(TerrainComp),nameof(TerrainComp.ApplyToHeightmap))]
internal static class PreparedLegacy
{
    private sealed class Cache
    {
        internal uint Revision;
        internal TerrainState? Original,Clean;
    }
    private static readonly ConditionalWeakTable<TerrainComp,Cache> caches=new ConditionalWeakTable<TerrainComp,Cache>();
    internal static TerrainState Clean(TerrainComp compiler)
    {
        var result=new TerrainState(compiler);
        var view=compiler.GetComponent<ZNetView>();
        var position=compiler.transform.position;
        RunicPaths.Core.SurfaceTile? tile=null;
        PreparedRuntime.Active?.Tiles.TryGetValue(RunicPaths.Core.Cell.From(new RunicPaths.Core.Point(position.x,position.z),64),out tile);
        if(view && view.IsValid()) TerrainJournal.Load(view.GetZDO().GetByteArray(TerrainJournal.Key),result.Level.Length).Restore(result,i=>tile!=null && tile.Protected(i));
        return result;
    }
    internal static TerrainState? CachedClean(TerrainComp compiler)
    {
        var view=compiler.GetComponent<ZNetView>();
        if(!view || !view.IsValid()) return null;
        var cache=caches.GetOrCreateValue(compiler);
        if(cache.Clean==null || cache.Revision!=view.GetZDO().DataRevision)
        {cache.Original=new TerrainState(compiler);cache.Clean=Clean(compiler);cache.Revision=view.GetZDO().DataRevision;}
        return cache.Clean;
    }
    internal static byte[] OriginalProtection(TerrainComp compiler)
    {
        const string key="RunicPaths.PreparedOriginalProtection.1";
        var view=compiler.GetComponent<ZNetView>();
        if(!view || !view.IsValid()) return Array.Empty<byte>();
        var saved=view.GetZDO().GetByteArray(key);
        if(saved!=null && saved.Length==529) return saved;
        var clean=Clean(compiler);var bits=new byte[529];
        for(int i=0;i<clean.Changed.Length;i++) if(clean.Changed[i]) bits[i/8]|=(byte)(1<<(i%8));
        if(view.IsOwner()) view.GetZDO().Set(key,bits);
        return bits;
    }
    private static void Prefix(TerrainComp __instance,ref TerrainState? __state)
    {
        if(PreparedRuntime.Active==null) return;
        var view=__instance.GetComponent<ZNetView>();
        if(!view || !view.IsValid()) return;
        var bytes=view.GetZDO().GetByteArray(TerrainJournal.Key);
        if(bytes==null || bytes.Length==0) return;
        var cache=caches.GetOrCreateValue(__instance);
        if(cache.Clean==null || cache.Revision!=view.GetZDO().DataRevision)
        {
            cache.Original=new TerrainState(__instance);cache.Clean=new TerrainState(cache.Original);
            var position=__instance.transform.position;
            PreparedRuntime.Active.Tiles.TryGetValue(RunicPaths.Core.Cell.From(new RunicPaths.Core.Point(position.x,position.z),64),out var tile);
            TerrainJournal.Load(bytes,cache.Clean.Level.Length).Restore(cache.Clean,i=>tile!=null && tile.Protected(i));
            cache.Revision=view.GetZDO().DataRevision;
        }
        __state=cache.Original;cache.Clean.Commit(__instance);
    }
    private static Exception? Finalizer(TerrainComp __instance,TerrainState? __state,Exception? __exception)
    {__state?.Commit(__instance);return __exception;}
}

// Before the first player edit, retire the legacy journal atomically in the
// authoritative compiler so new edits never include an obsolete road delta.
[HarmonyPatch(typeof(TerrainComp),"RPC_ApplyOperation")]
internal static class PreparedLegacyEdit
{
    private static void Prefix(TerrainComp __instance)
    {
        if(PreparedRuntime.Active==null) return;
        var view=__instance.GetComponent<ZNetView>();
        if(!view || !view.IsValid() || !view.IsOwner()) return;
        var old=view.GetZDO().GetByteArray(TerrainJournal.Key);
        if(old==null || old.Length==0) return;
        PreparedLegacy.OriginalProtection(__instance);
        var clean=PreparedLegacy.Clean(__instance);
        // Keep the journal for recovery; native state and ZDO serialize on the same thread.
        view.GetZDO().Set("RunicPaths.RetiredJournal.1",old);
        clean.Commit(__instance);
        view.GetZDO().Set(TerrainJournal.Key,Array.Empty<byte>());
        AccessTools.Method(typeof(TerrainComp),"Save").Invoke(__instance,new object[]{false});
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using RunicPaths.Core;
using UnityEngine;

namespace RunicPaths.Smoke;

public sealed partial class Smoke
{
    private float preparedStableAt;
    private float[]? preparedHeights;
    private void RunPrepared()
    {
        var runtime=typeof(Plugin).Assembly.GetType("RunicPaths.PreparedRuntime");
        var surface=(PreparedSurface?)AccessTools.Field(runtime,"Active").GetValue(null);
        if(surface==null) return;
        if(stage==0)
        {
            // Choose a real, elevated road vertex on a tile boundary.
            var pair=surface.Tiles.SelectMany(kv=>kv.Value.Vertices.Select(v=>(Tile:kv.Key,V:v)))
                .Where(p=>p.V.Index%65==64 && p.V.Index/65>3 && p.V.Index/65<60 && p.V.Grade>200 && p.V.Paint>200 && surface.Tiles.ContainsKey(new Cell(p.Tile.X+1,p.Tile.Z)))
                .OrderBy(p=>Math.Abs(p.Tile.X-12)+Math.Abs(p.Tile.Z-3)).First();
            target=new Point(pair.Tile.X*64+32,pair.Tile.Z*64-32+pair.V.Index/65);
            ForcedReference=new Vector3(target.X,pair.V.Height,target.Z);ZNet.instance.SetReferencePosition(ForcedReference.Value);
            stage=1;preparedStableAt=Time.realtimeSinceStartup;
            Logger.LogInfo("Prepared smoke loading boundary "+target.X+","+target.Z+" with "+surface.Tiles.Count+" cached tiles");return;
        }
        if(stage==1)
        {
            if(Time.realtimeSinceStartup-preparedStableAt<15) return;
            var left=Heightmap.FindHeightmap(new Vector3(target.X-.1f,0,target.Z));
            var right=Heightmap.FindHeightmap(new Vector3(target.X+.1f,0,target.Z));
            if(!left || !right || left==right) return;
            int row=(int)(target.Z-left.transform.position.z+32);
            int rightRow=(int)(target.Z-right.transform.position.z+32);
            float lh=left.GetHeight(64,row),rh=right.GetHeight(0,rightRow);
            if(Math.Abs(lh-rh)>.002f) throw new Exception("Prepared native seam mismatch: "+lh+" / "+rh);
            var tile=surface.Tiles[Cell.From(new Point(left.transform.position.x,left.transform.position.z),64)];
            var vertex=tile.Vertices.First(v=>v.Index==row*65+64);
            if(Math.Abs(lh-vertex.Height)>.01f) throw new Exception("Prepared height not applied before native mesh generation: "+lh+" / "+vertex.Height);
            tracked=left;preparedHeights=AccessTools.Field(typeof(Heightmap),"m_heights").GetValue(left) is List<float> heights?heights.ToArray():throw new Exception("Missing heights");
            if(AccessTools.Field(typeof(Plugin),"paintWork").GetValue(plugin)!=null || AccessTools.Field(typeof(Plugin),"work").GetValue(plugin)!=null) throw new Exception("Legacy work running during prepared gameplay");
            left.Poke(0,false);right.Poke(0,false);
            preparedStableAt=Time.realtimeSinceStartup;stage=2;
            Logger.LogInfo("PASS prepared native boundary matches shared saved target; no legacy planning/painter jobs.");return;
        }
        if(stage==2 && Time.realtimeSinceStartup-preparedStableAt>15)
        {
            var heights=(List<float>)AccessTools.Field(typeof(Heightmap),"m_heights").GetValue(tracked);
            if(!heights.SequenceEqual(preparedHeights!)) throw new Exception("Prepared terrain accumulated/reverted on regeneration");
            var compiler=tracked!.GetAndCreateTerrainCompiler();
            SeedOldSeam(compiler);tracked.Poke(0,false);
            var restored=(List<float>)AccessTools.Field(typeof(Heightmap),"m_heights").GetValue(tracked);
            if(!restored.SequenceEqual(preparedHeights!)) throw new Exception("Legacy journal doubled the prepared road heights");
            var tile=surface.Tiles[Cell.From(new Point(tracked.transform.position.x,tracked.transform.position.z),64)];
            int edited=tile.Vertices.First(v=>v.Index%65>3 && v.Index%65<60 && v.Index/65>3 && v.Index/65<60 && v.Grade>200 && v.Paint>200).Index;
            var levels=(float[])AccessTools.Field(typeof(TerrainComp),"m_levelDelta").GetValue(compiler);
            var flags=(bool[])AccessTools.Field(typeof(TerrainComp),"m_modifiedHeight").GetValue(compiler);
            AccessTools.Method(typeof(Plugin).Assembly.GetType("RunicPaths.PreparedLegacyEdit"),"Prefix").Invoke(null,new object[]{compiler});
            levels=(float[])AccessTools.Field(typeof(TerrainComp),"m_levelDelta").GetValue(compiler);
            flags=(bool[])AccessTools.Field(typeof(TerrainComp),"m_modifiedHeight").GetValue(compiler);
            levels[edited]=.25f;flags[edited]=true;
            var painted=(bool[])AccessTools.Field(typeof(TerrainComp),"m_modifiedPaint").GetValue(compiler);
            var colors=(Color[])AccessTools.Field(typeof(TerrainComp),"m_paintMask").GetValue(compiler);
            painted[edited]=true;colors[edited]=Color.green;
            AccessTools.Method(typeof(TerrainComp),"Save",new[]{typeof(bool)}).Invoke(compiler,new object[]{false});
            tracked.Poke(0,false);
            float modified=tracked.GetHeight(edited%65,edited/65);
            if(Math.Abs(modified-preparedHeights![edited]-.25f)>.002f) throw new Exception("Later player edit did not compose with prepared baseline");
            if(tracked.GetPaintMask(edited%65,edited/65).g<.99f) throw new Exception("Prepared paint overwrote player cultivation");
            if(compiler.GetComponent<ZNetView>().GetZDO().GetByteArray("RunicPaths.TerrainJournal.v1").Length!=0) throw new Exception("Legacy journal was not retired before player edit");
            var origin=tracked.transform.position+new Vector3(edited%65-32,0,edited/65-32);
            origin.y=WorldGenerator.instance.GetHeight(origin.x,origin.z);
            var tree=SpawnObject("Beech_small1",origin,false,true);
            var id=tree.GetZDO().m_uid;tree.GetZDO().Set("RunicPaths.SmokeMarker","preserve-me");
            string output=Path.Combine(BepInEx.Paths.GameRootPath,"prepared-relocation-test");Directory.CreateDirectory(output);
            var relocate=AccessTools.Method(typeof(Plugin).Assembly.GetType("RunicPaths.PreparedObjects"),"Relocate");
            foreach(int _ in (IEnumerable<int>)relocate.Invoke(null,new object[]{surface,output,48f})) {}
            var movedZdo=ZDOMan.instance.GetZDO(id);
            if(movedZdo==null || movedZdo.GetString("RunicPaths.SmokeMarker","")!="preserve-me" || !movedZdo.GetBool("RunicPaths.PlayerObject",false)) throw new Exception("Relocation lost player object identity/data");
            if(Vector3.Distance(movedZdo.GetPosition(),origin)<2 || surface.Cleared(new Point(movedZdo.GetPosition().x,movedZdo.GetPosition().z),2)) throw new Exception("Player tree was not relocated off prepared road");
            if(!File.ReadAllText(Path.Combine(output,"relocations.jsonl")).Contains(id.ToString())) throw new Exception("Relocation recovery record missing");
            Finish("PASS 0.3.0 prepared cache, native generation hook, shared seam elevation, repeat stability, legacy journal migration, player height/cultivation preservation, existing player-tree relocation with identity and recovery log, no legacy player-following work. Headless test; rendered client frame times and multiplayer not measured.");
        }
    }
}


using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;

namespace RunicPaths;

// One-time, world-specific consent captures existing IDs, never future unmarked placements.
internal static class NaturalBaseline
{
    private static readonly System.Reflection.FieldInfo Objects=AccessTools.Field(typeof(ZDOMan),"m_objectsByID");
    internal static void Apply(string file,long world)
    {
        var objects=(Dictionary<ZDOID,ZDO>)Objects.GetValue(ZDOMan.instance);
        string header="RunicPaths natural baseline v1|"+world;
        HashSet<string> ids;
        if(File.Exists(file))
        {
            string[] lines=File.ReadAllLines(file);
            if(lines.Length==0 || lines[0]!=header) throw new InvalidDataException("Natural baseline belongs to another world or version");
            ids=new HashSet<string>(lines.Skip(1),StringComparer.Ordinal);
        }
        else
        {
            ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var pair in objects)
            {
                var zdo=pair.Value;
                var prefab=ZNetScene.instance.GetPrefab(zdo.GetPrefab());
                if(!prefab || !RoadObjects.Movable(prefab) || prefab.GetComponent<Plant>() ||
                    zdo.GetBool(RoadObjects.PlayerKey,false) || zdo.GetLong(ZDOVars.s_creator,0)!=0) continue;
                ids.Add(pair.Key.ToString());
            }
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllLines(file+".tmp",new[]{header}.Concat(ids.OrderBy(x=>x,StringComparer.Ordinal)));
            File.Move(file+".tmp",file);
        }
        int marked=0;
        foreach(var pair in objects)
        {
            var zdo=pair.Value;
            if(!ids.Contains(pair.Key.ToString()) || zdo.GetBool(RoadObjects.PlayerKey,false) || zdo.GetLong(ZDOVars.s_creator,0)!=0) continue;
            zdo.Set(Plugin.NaturalKey,true);marked++;
        }
        Plugin.Report($"Natural baseline: {marked} existing objects confirmed for world {world}. Future unmarked placements are excluded.");
    }
}

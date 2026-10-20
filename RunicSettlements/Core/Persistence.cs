using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;

namespace RunicSettlements.Core;

public static class Persistence
{
    public static WorldPaths Load(string path, long worldId, int seed)
    {
        if (!File.Exists(path)) return new WorldPaths { WorldId = worldId, Seed = seed };
        using var stream = File.OpenRead(path);
        var data = (WorldPaths?)new DataContractJsonSerializer(typeof(WorldPaths)).ReadObject(stream);
        if (data == null || data.Schema != 1 || data.WorldId != worldId || data.Seed != seed || data.Islands == null)
            throw new InvalidDataException("Path save schema or world identity mismatch. Restore the matching backup; automatic regeneration is disabled.");
        foreach (Island island in data.Islands)
        {
            if (island.Cells == null || island.Routes == null || island.Exclusions == null) throw new InvalidDataException("Incomplete island record.");
            foreach (Cell c in island.Cells)
                if (Math.Abs((long)c.X) > 420 || Math.Abs((long)c.Z) > 420) throw new InvalidDataException("Invalid island cell.");
            foreach (Exclusion e in island.Exclusions)
                if (e == null || float.IsNaN(e.Center.X) || float.IsNaN(e.Center.Z) || e.Center.Length > 11000 || !(e.Radius >= 0 && e.Radius < 10000))
                    throw new InvalidDataException("Invalid protected location.");
            foreach (Route route in island.Routes)
            {
                if (route.Points == null || route.Points.Count < 2 || route.Surface != "Dirt") throw new InvalidDataException("Invalid route record.");
                foreach (Point p in route.Points)
                    if (float.IsNaN(p.X) || float.IsNaN(p.Z) || float.IsInfinity(p.X) || float.IsInfinity(p.Z) || p.Length > 10000)
                        throw new InvalidDataException("Invalid route coordinate.");
            }
        }
        return data;
    }

    public static void Save(string path, WorldPaths data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            new DataContractJsonSerializer(typeof(WorldPaths)).WriteObject(stream, data);
            stream.Flush(true);
        }
        if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
        else File.Move(temp, path);
    }
}

public sealed class RouteIndex
{
    private readonly Dictionary<Cell, List<(Point A, Point B)>> zones = new Dictionary<Cell, List<(Point, Point)>>();
    private readonly List<Exclusion> exclusions = new List<Exclusion>();
    public readonly HashSet<Cell> ActivatedCells = new HashSet<Cell>();
    public void Add(Island island)
    {
        foreach (Cell c in island.Cells) ActivatedCells.Add(c);
        exclusions.AddRange(island.Exclusions);
        foreach (Route route in island.Routes)
        for (int i = 1; i < route.Points.Count; i++)
        {
            Point a = route.Points[i - 1], b = route.Points[i];
            // 12 m covers maximum supported trail width plus feathering and vegetation clearance.
            Cell lo = Cell.From(new Point(Math.Min(a.X, b.X) - 12, Math.Min(a.Z, b.Z) - 12), 64);
            Cell hi = Cell.From(new Point(Math.Max(a.X, b.X) + 12, Math.Max(a.Z, b.Z) + 12), 64);
            for (int x = lo.X; x <= hi.X; x++)
            for (int z = lo.Z; z <= hi.Z; z++)
            {
                var c = new Cell(x, z);
                if (!zones.TryGetValue(c, out var list)) zones[c] = list = new List<(Point, Point)>();
                list.Add((a, b));
            }
        }
    }
    public ZonePaths Get(Cell cell, float width, float buildBuffer)
    {
        var result = new ZonePaths { Zone = cell, Width = width, BuildBuffer = buildBuffer };
        if (zones.TryGetValue(cell, out var segments)) result.Segments.AddRange(segments);
        foreach (Exclusion e in exclusions)
            if (Point.Distance(e.Center, cell.At(64)) < e.Radius + 60) result.Exclusions.Add(e);
        result.ComputeSignature();
        return result;
    }
}

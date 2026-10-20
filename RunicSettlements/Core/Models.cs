using System.Collections.Generic;
using System.Runtime.Serialization;

namespace RunicSettlements.Core;

[DataContract]
public sealed class Poi
{
    [DataMember] public string Name = "";
    [DataMember] public Point Position;
    [DataMember] public float Radius;
}

[DataContract]
public sealed class Route
{
    [DataMember] public List<Point> Points = new List<Point>();
    // Keep surface separate from geometry for a future paved-road upgrade.
    [DataMember] public string Surface = "Dirt";
}

[DataContract]
public sealed class Island
{
    [DataMember] public List<PoiOutcome> Outcomes = new List<PoiOutcome>();
    [DataMember] public List<Cell> Cells = new List<Cell>();
    [DataMember] public List<Route> Routes = new List<Route>();
    [DataMember] public List<Exclusion> Exclusions = new List<Exclusion>();
    [DataMember] public int PlanningSignature;
    [DataMember] public int PoiCount;
    [DataMember] public int ConnectedPairs;
}

[DataContract]
public sealed class PoiOutcome
{
    [DataMember] public string Name = "";
    [DataMember] public Point Position;
    [DataMember] public string Status = "";
    [DataMember] public int Component;
}

[DataContract]
public sealed class WorldPaths
{
    [DataMember] public int Schema = 1;
    [DataMember] public long WorldId;
    [DataMember] public int Seed;
    [DataMember] public List<Island> Islands = new List<Island>();
}

public sealed class ZonePaths
{
    public Cell Zone;
    public float Width;
    public float BuildBuffer;
    public float MaximumRaise = 1.5f;
    public float MaximumLower = 1.5f;
    public float RelocationDistance = 24;
    public int ClearanceRevision;
    public readonly List<(Point A, Point B)> Segments = new List<(Point, Point)>();
    public readonly List<Exclusion> Exclusions = new List<Exclusion>();
    public int Signature;
    public bool Protected(Point p, float margin = 0) => Exclusions.Exists(x => x.Contains(p, margin));
    public float Distance(Point p)
    {
        float best = float.MaxValue;
        foreach (var s in Segments) best = System.Math.Min(best, Point.SegmentDistance(p, s.A, s.B));
        return best;
    }
    public float Strength(Point p)
    {
        // Continuous world-space variation keeps the feathered edges consistent at zone seams.
        float variation = 1 + 0.08f * (float)(System.Math.Sin(p.X * 0.17 + p.Z * 0.09) * System.Math.Sin(p.Z * 0.23));
        float radius = Width * 0.5f * variation;
        float t = System.Math.Max(0, System.Math.Min(1, (radius + 0.8f - Distance(p)) / 1.1f));
        return t * t * (3 - 2 * t);
    }
    public void ComputeSignature()
    {
        unchecked
        {
            int h = 17;
            void Add(float v) => h = h * 31 + v.GetHashCode();
            Add(Width); Add(BuildBuffer); Add(MaximumRaise); Add(MaximumLower); Add(RelocationDistance); Add(ClearanceRevision);
            foreach (var s in Segments) { Add(s.A.X); Add(s.A.Z); Add(s.B.X); Add(s.B.Z); }
            foreach (var e in Exclusions) { Add(e.Center.X); Add(e.Center.Z); Add(e.Radius); }
            Signature = h == 0 ? 1 : h;
        }
    }
}



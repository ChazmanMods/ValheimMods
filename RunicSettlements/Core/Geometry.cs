using System;
using System.Runtime.Serialization;

namespace RunicSettlements.Core;

[DataContract]
public struct Point
{
    [DataMember] public float X;
    [DataMember] public float Z;
    public Point(float x, float z) { X = x; Z = z; }
    public static Point operator +(Point a, Point b) => new Point(a.X + b.X, a.Z + b.Z);
    public static Point operator -(Point a, Point b) => new Point(a.X - b.X, a.Z - b.Z);
    public static Point operator *(Point a, float b) => new Point(a.X * b, a.Z * b);
    public float Length => (float)Math.Sqrt(X * X + Z * Z);
    public static float Distance(Point a, Point b) => (a - b).Length;
    public static Point Lerp(Point a, Point b, float t) => a + (b - a) * t;
    public static float SegmentDistance(Point p, Point a, Point b)
    {
        Point d = b - a;
        float denom = d.X * d.X + d.Z * d.Z;
        float t = denom == 0 ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * d.X + (p.Z - a.Z) * d.Z) / denom));
        return Distance(p, Lerp(a, b, t));
    }
}

[DataContract]
public struct Cell : IEquatable<Cell>, IComparable<Cell>
{
    [DataMember] public int X;
    [DataMember] public int Z;
    public Cell(int x, int z) { X = x; Z = z; }
    public Point At(float size) => new Point(X * size, Z * size);
    public static Cell From(Point p, float size) => new Cell((int)Math.Floor(p.X / size + 0.5f), (int)Math.Floor(p.Z / size + 0.5f));
    public bool Equals(Cell b) => X == b.X && Z == b.Z;
    public override bool Equals(object? o) => o is Cell c && Equals(c);
    public override int GetHashCode() => unchecked(X * 73856093 ^ Z * 19349663);
    public int CompareTo(Cell b) { int x = X.CompareTo(b.X); return x == 0 ? Z.CompareTo(b.Z) : x; }
    public override string ToString() => X + "," + Z;
}

[DataContract]
public sealed class Exclusion
{
    [DataMember] public Point Center;
    [DataMember] public float Radius;
    [DataMember] public float HalfX, HalfZ;
    [DataMember] public bool Replaceable;
    public bool Contains(Point p, float margin = 0) => HalfX>0 && HalfZ>0 ?
        Math.Abs(p.X-Center.X)<HalfX+margin && Math.Abs(p.Z-Center.Z)<HalfZ+margin : Point.Distance(p, Center) < Radius + margin;
}

public interface ITerrain
{
    float Height(Point p);
    bool Allowed(Point p);
}

public sealed class PlannerSettings
{
    public const float IslandGrid = 24;
    public const float RouteGrid = 8;
    public float WaterLevel = 30;
    public float ShoreClearance = 1.2f;
    public float Width = 3;
    public float MaxSlope = 0.55f;
    public float MaxConnection = 3000;
    public float MaximumGap;
    public float MaximumRaise=1.5f;
    public float MaximumLower=1.5f;
    public int MaxIslandCells = 80000;
    public int MaxSearchNodes = 16000;
    public int MaxPois = 40;
    public int ExtraLoops;
}


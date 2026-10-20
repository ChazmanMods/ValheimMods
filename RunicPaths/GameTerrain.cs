using RunicPaths.Core;
using UnityEngine;

namespace RunicPaths;

internal sealed class GameTerrain : ITerrain
{
    public float Height(Point p)
    {
        // Match the normal 64 m heightmap's corner-biome blend, including biome boundaries.
        // Do not use Minimap, player exploration, physics scene generation, or Unity Random here.
        var gen = WorldGenerator.instance;
        Cell zone = Cell.From(p, 64);
        float x = zone.X * 64 - 32, z = zone.Z * 64 - 32;
        var a = gen.GetBiome(x, z); var b = gen.GetBiome(x + 64, z);
        var c = gen.GetBiome(x, z + 64); var d = gen.GetBiome(x + 64, z + 64);
        float ha = gen.GetBiomeHeight(a, p.X, p.Z, out _);
        if (a == b && a == c && a == d) return ha;
        float hb = gen.GetBiomeHeight(b, p.X, p.Z, out _);
        float hc = gen.GetBiomeHeight(c, p.X, p.Z, out _);
        float hd = gen.GetBiomeHeight(d, p.X, p.Z, out _);
        float tx = Mathf.SmoothStep(0, 1, (p.X - x) / 64);
        float tz = Mathf.SmoothStep(0, 1, (p.Z - z) / 64);
        return Mathf.Lerp(Mathf.Lerp(ha, hb, tx), Mathf.Lerp(hc, hd, tx), tz);
    }
    public bool Allowed(Point p)
    {
        var biome = WorldGenerator.instance.GetBiome(p.X, p.Z);
        // Ashlands lava and Deep North snow need biome-specific surface handling in a later release.
        return biome == Heightmap.Biome.Meadows || biome == Heightmap.Biome.BlackForest || biome == Heightmap.Biome.Plains ||
               biome == Heightmap.Biome.Swamp || biome == Heightmap.Biome.Mountain || biome == Heightmap.Biome.Mistlands;
    }
    public static Point PointOf(Vector3 p) => new Point(p.x, p.z);
    public static Vector3 VectorOf(Point p, float height = 0) => new Vector3(p.X, height, p.Z);
}

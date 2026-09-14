using System.Numerics;

namespace RunicStorage.Engine;

internal static class ChestLabelFaces
{
    // Valheim piece front is +Z. Left/right are from a player facing that front.
    internal static Vector3 Normal(int side) => side switch {
        1 => -Vector3.UnitZ, 2 => Vector3.UnitX, 3 => -Vector3.UnitX,
        4 => Vector3.UnitY, _ => Vector3.UnitZ
    };
    // On the lid the top of the lettering points away from the player at the front.
    internal static Vector3 Up(int side) => side == 4 ? -Vector3.UnitZ : Vector3.UnitY;
    internal static Vector3 Right(int side) => Vector3.Cross(Up(side), -Normal(side));
    internal static Vector3 Position(int side, Vector3 center, Vector3 extents, float horizontal, float vertical)
    {
        var normal = Normal(side);
        float depth = Vector3.Dot(Vector3.Abs(normal), extents);
        return center + normal * (depth + .025f) + Right(side) * horizontal + Up(side) * vertical;
    }
}

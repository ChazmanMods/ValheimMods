// Focused stand-ins for the game/Harmony APIs. The production patch is compiled
// unchanged into this executable. This is NOT a Unity or transport integration test.
using UnityEngine;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    class HarmonyPatch : Attribute { public HarmonyPatch(Type t, string n, Type a) {} }
    class HarmonyPrefix : Attribute {}
    class HarmonyPostfix : Attribute {}
}

namespace UnityEngine
{
    public readonly struct Vector3 : IEquatable<Vector3>
    {
        public readonly float x, y, z;
        public Vector3(float x, float y, float z) { this.x=x; this.y=y; this.z=z; }
        public bool Equals(Vector3 b) => x.Equals(b.x) && y.Equals(b.y) && z.Equals(b.z);
        public override bool Equals(object b) => b is Vector3 v && Equals(v);
        public override int GetHashCode() => HashCode.Combine(x,y,z);
        public static bool operator ==(Vector3 a, Vector3 b) =>
            (a.x-b.x)*(a.x-b.x)+(a.y-b.y)*(a.y-b.y)+(a.z-b.z)*(a.z-b.z) < 9.99999944E-11f;
        public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);
    }
}

static class HashExtensions
{
    public static int GetStableHashCode(this string s)
    {
        unchecked
        {
            int h1 = 5381, h2 = h1;
            for (int i = 0; i < s.Length && s[i] != '\0'; i += 2)
            {
                h1 = ((h1 << 5) + h1) ^ s[i];
                if (i == s.Length - 1 || s[i + 1] == '\0') break;
                h2 = ((h2 << 5) + h2) ^ s[i + 1];
            }
            return h1 + h2 * 1566083941;
        }
    }
}

class ZoneSystem
{
    public readonly record struct SectorIndex(int X, int Z);
    public static SectorIndex GetSectorIndex(Vector3 p) =>
        new((int)MathF.Floor((p.x + 32f) / 64f), (int)MathF.Floor((p.z + 32f) / 64f));
}

class ZNet
{
    public static ZNet instance;
    public bool Server = true;
    public bool IsServer() => Server;
}

class Peer
{
    public long Owner;
    public Vector3 Position;
    public bool Knows = true;
    public bool Invalidated;
    public bool InArea(Vector3 p) => MathF.Abs(p.x - Position.x) <= 96f && MathF.Abs(p.z - Position.z) <= 96f;
}

class ZDOMan
{
    public static ZDOMan instance;
    public List<Peer> Peers = new();
    public List<Vector3> CheckedPositions = new();
    public void ZDOSectorInvalidated(ZDO zdo)
    {
        CheckedPositions.Add(zdo.GetPosition());
        foreach (var peer in Peers)
            if (zdo.Owner != peer.Owner && peer.Knows && !peer.InArea(zdo.GetPosition()))
            {
                peer.Invalidated = true;
                peer.Knows = false;
            }
    }
}

class ZDO
{
    public int Prefab = "Player".GetStableHashCode();
    public long Owner = 10;
    public Vector3 Position;
    public ZoneSystem.SectorIndex StoredSector;
    public int GetPrefab() => Prefab;
    public Vector3 GetPosition() => Position;
    public ZoneSystem.SectorIndex GetSectorIndex() => ZoneSystem.GetSectorIndex(Position);
    public void InternalSetPosition(Vector3 pos)
    {
        if (Position == pos) return;
        var next = ZoneSystem.GetSectorIndex(pos);
        if (GetSectorIndex() != next)
        {
            StoredSector = next;
            if (ZNet.instance != null && ZNet.instance.IsServer())
                ZDOMan.instance?.ZDOSectorInvalidated(this);
        }
        Position = pos;
    }
}

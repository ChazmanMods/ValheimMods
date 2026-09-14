// Managed fixtures execute the actual AreaRepairRuntime control flow; native repair behavior is
// independently inspected in InstalledRepairContractIsOwnerDirected. These are not Unity tests.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
namespace UnityEngine
{
    public enum CursorLockMode { None, Locked }
    public static class Cursor { public static CursorLockMode lockState = CursorLockMode.Locked; }
    public static class Time { public static float unscaledTime; }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y = 0, float z = 0) { this.x = x; this.y = y; this.z = z; }
        public float sqrMagnitude => x*x+y*y+z*z;
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
    }
    public class Transform { public Vector3 position; }
    public class GameObject
    {
        public string name; public bool activeInHierarchy = true; public Transform transform = new Transform();
        private readonly Dictionary<Type, Component> parts = new Dictionary<Type, Component>();
        public T Add<T>(T part) where T : Component { part.gameObject = this; parts[typeof(T)] = part; return part; }
        public T GetComponent<T>() where T : class => parts.TryGetValue(typeof(T), out var part) ? part as T : null;
    }
    public class Component
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
    }
}
namespace HarmonyLib
{
    public static class AccessTools
    {
        public static MethodInfo Method(Type type, string name, Type[] args) => type.GetMethod(name);
        public static T MethodDelegate<T>(MethodInfo method) where T : Delegate => method.CreateDelegate<T>();
    }
}
public class Piece : UnityEngine.Component
{
    public static List<Piece> Loaded = new List<Piece>();
    public bool m_repairPiece, m_removePiece; public CraftingStation m_craftingStation;
    public static void GetAllPiecesInRadius(UnityEngine.Vector3 origin, float radius, List<Piece> output) =>
        output.AddRange(Loaded.Where(p => (p.transform.position-origin).sqrMagnitude < radius*radius));
}
public class WearNTear : UnityEngine.Component
{
    public float m_health = 100; public int Requests;
    public bool Repair() { Requests++; return true; }
}
public class ZDO { public float Health = 50; public float GetFloat(int key, float fallback) => Health; }
public static class ZDOVars { public static int s_health; }
public class ZNetView : UnityEngine.Component
{
    public bool Valid = true, Owned = true; public ZDO Record = new ZDO();
    public bool IsValid() => Valid; public bool HasOwner() => Owned; public ZDO GetZDO() => Record;
}
public class Container : UnityEngine.Component { public bool Allowed = true; }
public class CraftingStation : UnityEngine.Component
{
    public string m_name = "workbench"; public static CraftingStation Nearby; public bool Allowed = true;
    public static CraftingStation HaveBuildStationInRange(string name, UnityEngine.Vector3 pos) => Nearby;
}
public class Player : UnityEngine.Component
{
    public static Player m_localPlayer; public bool Input = true, Dead, Teleporting, Owner = true;
    public List<string> Messages = new List<string>();
    public bool TakeInput() => Input;
    public bool IsDead() => Dead; public bool IsTeleporting() => Teleporting;
    public bool NoCostCheat() => false; public long GetPlayerID() => 42;
    public void Message(MessageHud.MessageType type, string message) => Messages.Add(message);
}
public class MessageHud { public enum MessageType { TopLeft } }
public class ZNet { public static ZNet instance; }
public class ZoneSystem { public static ZoneSystem instance; public bool GetGlobalKey(GlobalKeys key) => false; }
public enum GlobalKeys { NoWorkbench }
public static class PrivateArea
{
    public static Func<UnityEngine.Vector3, bool> Allows = _ => true;
    public static bool CheckAccess(UnityEngine.Vector3 pos, float r, bool flash) => Allows(pos);
}
public class ObjectDB
{
    public static ObjectDB instance; public UnityEngine.GameObject Hammer;
    public UnityEngine.GameObject GetItemPrefab(string name) => name == "Hammer" ? Hammer : null;
}
public class ItemDrop : UnityEngine.Component
{
    public ItemData m_itemData = new ItemData();
    public class ItemData { public Shared m_shared = new Shared(); }
    public class Shared { public PieceTable m_buildPieces = new PieceTable(); }
}
public class PieceTable { public List<UnityEngine.GameObject> m_pieces = new List<UnityEngine.GameObject>(); }
public static class Game { public static bool Paused; public static bool IsPaused() => Paused; }
public static class InventoryGui { public static bool Visible; public static bool IsVisible() => Visible; }
public static class Minimap { public static bool IsOpen() => false; }
public static class Menu { public static bool IsVisible() => false; }
public static class Console { public static bool IsVisible() => false; }
public static class TextInput { public static bool IsVisible() => false; }
public static class ZInput { public static bool VirtualKeyboardOpen; }
public static class Hud { public static bool IsPieceSelectionVisible() => false; }
public class Chat { public static Chat instance; public bool Focus; public bool HasFocus() => Focus; }
namespace RunicCrafting
{
    public class Setting<T> { public T Value; public Setting(T value) { Value=value; } }
    public class Shortcut { public bool Down; public bool IsDown() { bool result = Down; Down = false; return result; } }
    internal static class Configuration
    {
        internal static Setting<bool> Enabled = new Setting<bool>(true), AreaRepairEnabled = new Setting<bool>(true);
        internal static Setting<float> AreaRepairRadius = new Setting<float>(50);
        internal static Setting<Shortcut> AreaRepairKey = new Setting<Shortcut>(new Shortcut());
    }
    public class Logger { public void LogWarning(string s) { System.Console.WriteLine(s); } }
    public static class Plugin { public static Logger Log = new Logger(); }
}
namespace RunicCrafting.Integration
{
    internal static class ValheimReflection
    {
        internal static bool CanMutateLocalPlayer(Player p) => p != null && p.Owner && ReferenceEquals(p, Player.m_localPlayer);
        internal static string PiecePrefabId(Piece p) => p.gameObject.name.Replace("(Clone)", "");
        internal static bool ContainerAllows(Container c, long player) => c.Allowed;
    }
    internal static class CraftingRuntime
    {
        internal static bool CanUseStation(CraftingStation station, Player player, out string reason)
        { reason = "fixture"; return station.Allowed; }
    }
}

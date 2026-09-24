// In-memory network harness. Runs the production SignRuntime; no Unity or server process is launched.
using System;
using System.Collections.Generic;
using System.Reflection;

public interface IPlaced { void OnPlaced(); }

namespace UnityEngine
{
    public class Object
    {
        public static implicit operator bool(Object o) => o != null;
        public static void Destroy(Object o) { }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() => gameObject.GetComponent<T>();
    }
    public class MonoBehaviour : Component { }
    public class GameObject : Object
    {
        public readonly Dictionary<Type, Component> Components = new();
        public Transform transform;
        public string name;
        public GameObject(string name = "sign", params Type[] types)
        { this.name=name; transform = new RectTransform { gameObject = this }; foreach (var type in types) Add(type); }
        private Component Add(Type type)
        {
            if (type == typeof(RectTransform)) return transform;
            var c = (Component)Activator.CreateInstance(type); c.gameObject = this; Components[type] = c;
            type.GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(c, null); return c;
        }
        public T AddComponent<T>() where T : Component => (T)Add(typeof(T));
        public T GetComponent<T>() => Components.TryGetValue(typeof(T), out var c) ? (T)(object)c : default;
        public bool activeSelf = true;
        public void SetActive(bool active) { activeSelf = active; }
    }
    public class Transform : Component
    {
        public Vector3 localScale = Vector3.one, localPosition;
        public Quaternion localRotation = Quaternion.identity;
        public Transform parent;
        public void SetParent(Transform p, bool world) { parent = p; }
        public void SetSiblingIndex(int i) { }
        public int GetSiblingIndex() => 0;
    }
    public class RectTransform : Transform
    {
        public Vector2 anchoredPosition { get => new(localPosition.x, localPosition.y); set => localPosition = new(value.x, value.y, localPosition.z); }
        public Vector2 anchorMin, anchorMax, pivot, sizeDelta; public Rect rect => new() { size = sizeDelta };
    }
    public struct Rect { public Vector2 size; }
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float a,float b,float c) { x=a; y=b; z=c; }
        public static Vector3 one => new(1,1,1);
        public static Vector3 operator *(Vector3 v,float f) => new(v.x*f,v.y*f,v.z*f);
        public static Vector3 operator +(Vector3 a,Vector3 b) => new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 Scale(Vector3 a,Vector3 b) => new(a.x*b.x,a.y*b.y,a.z*b.z);
    }
    public struct Quaternion
    {
        private System.Numerics.Quaternion value;
        public static Quaternion identity => new() { value=System.Numerics.Quaternion.Identity };
        public static Quaternion Euler(float x,float y,float z) => new() {
            value=System.Numerics.Quaternion.CreateFromYawPitchRoll(y*(float)Math.PI/180,x*(float)Math.PI/180,z*(float)Math.PI/180) };
        public static Vector3 operator *(Quaternion q,Vector3 v) {
            var result=System.Numerics.Vector3.Transform(new System.Numerics.Vector3(v.x,v.y,v.z),q.value);
            return new(result.X,result.Y,result.Z);
        }
    }
    public struct Vector2
    {
        public float x,y; public Vector2(float a,float b) { x=a; y=b; }
        public static Vector2 Scale(Vector2 a,Vector2 b) => new(a.x*b.x,a.y*b.y);
        public static Vector2 operator +(Vector2 a,Vector2 b) => new(a.x+b.x,a.y+b.y);
    }
    public struct Color { public float r,g,b,a; public Color(float r,float g,float b,float a){this.r=r;this.g=g;this.b=b;this.a=a;} public static Color white => new(1,1,1,1); public static Color black => new(0,0,0,1); }
    public class Material : Object {
        public HashSet<string> Keywords = new();
        public Dictionary<string,float> Floats = new();
        public Dictionary<string,Color> Colors = new();
        public Material(Material other){ if(other!=null) { Keywords=new(other.Keywords); Floats=new(other.Floats); Colors=new(other.Colors); } } public bool HasProperty(string s)=>true;
        public void EnableKeyword(string s){Keywords.Add(s);} public void SetFloat(string s,float v){Floats[s]=v;} public void SetColor(string s,Color v){Colors[s]=v;}
    }
    public static class ColorUtility { public static bool TryParseHtmlString(string s,out Color c) { c=new(); return true; } }
    public static class Mathf { public static float Clamp(float x,float a,float b)=>Math.Clamp(x,a,b); public static float Min(float a,float b)=>Math.Min(a,b); public static float Max(float a,float b)=>Math.Max(a,b); }
    public static class Time { public static float unscaledTime; public static int frameCount; }
    public enum KeyCode { LeftBracket, RightBracket, F8 }
    public class CanvasRenderer : Component { }
}
namespace UnityEngine.UI
{
    public class Image : UnityEngine.Component
    { public bool raycastTarget; public UnityEngine.Color color; public UnityEngine.RectTransform rectTransform => (UnityEngine.RectTransform)transform; }
}
namespace TMPro
{
    public interface ITextPreprocessor { string PreprocessText(string text); }
    public enum TextWrappingModes { Normal, NoWrap }
    public struct TMP_CharacterInfo { public bool isVisible; public int materialReferenceIndex, vertexIndex; }
    public struct TMP_MeshInfo { public UnityEngine.Vector3[] vertices; }
    public class TMP_TextInfo { public int characterCount; public TMP_CharacterInfo[] characterInfo; public TMP_MeshInfo[] meshInfo; }
    public enum TextOverflowModes { Overflow, Ellipsis }
    [Flags] public enum FontStyles { Normal=0, Bold=1, Italic=2 }
    public enum TextAlignmentOptions { Left, Center, Right }
    public class TextMeshProUGUI : UnityEngine.Component
    {
        public event Action<TMP_TextInfo> OnPreRenderText;
        public void Generate(TMP_TextInfo info) => OnPreRenderText?.Invoke(info);
        public int GeometryHandlers => OnPreRenderText?.GetInvocationList().Length ?? 0;
        public TextWrappingModes textWrappingMode = TextWrappingModes.Normal;
        public void UpdateMeshPadding(){}
        public ITextPreprocessor textPreprocessor;
        public UnityEngine.Material fontSharedMaterial;
        public bool isOrthographic=true;
        public bool havePropertiesChanged;
        public TextOverflowModes overflowMode;
        public void SetAllDirty(){}
        public float fontSize=20,fontSizeMin=8,fontSizeMax=20;
        public bool enableAutoSizing,overrideColorTags,richText=true;
        public UnityEngine.Color color; public FontStyles fontStyle; public TextAlignmentOptions alignment;
        public string text="Original";
        public UnityEngine.RectTransform rectTransform => (UnityEngine.RectTransform)transform;
    }
}
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.All,AllowMultiple=true)] public class HarmonyPatch : Attribute
    { public HarmonyPatch(){} public HarmonyPatch(Type t,string s, params Type[] types){} }
    public class HarmonyPrefix : Attribute{} public class HarmonyPostfix : Attribute{}
    public static class AccessTools {
        public static MethodInfo Method(Type t,string name)=>t.GetMethod(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
        public static T MethodDelegate<T>(MethodInfo method) where T:Delegate => (T)method.CreateDelegate(typeof(T));
    }
}
namespace BepInEx.Configuration {
    public class ConfigEntry<T> { public T Value; }
    public class ConfigDescription { public ConfigDescription(string s,object range){} }
    public class AcceptableValueRange<T> { public AcceptableValueRange(T min,T max){} }
    public class ConfigFile {
        public Dictionary<string,object> Entries=new();
        public ConfigEntry<T> Bind<T>(string section,string key,T value,object description) {
            var entry=new ConfigEntry<T>{Value=value}; Entries[key]=entry; return entry;
        }
    }
    public struct KeyboardShortcut {
        public static UnityEngine.KeyCode? Pressed;
        private UnityEngine.KeyCode key;
        public KeyboardShortcut(UnityEngine.KeyCode value){key=value;}
        public bool IsDown()=>Pressed==key;
    }
}
public class Player : UnityEngine.Component {
    public static Player m_localPlayer;
    public bool Owner=true, Input=true;
    public bool IsOwner()=>Owner;
    public long GetPlayerID()=>42;
    public void Message(MessageHud.MessageType type,string message,int amount=0,UnityEngine.Object icon=null){}
    private bool TakeInput()=>Input;
}
public class Piece : UnityEngine.Component { public long Creator=42; public long GetCreator()=>Creator; }
public class MessageHud { public enum MessageType { TopLeft } }
public static class Utils { public static string GetPrefabName(UnityEngine.GameObject go)=>go.name; }
public interface TextReceiver { }
public class TextInput { public void RequestText(TextReceiver sign){} }
public class Sign : UnityEngine.Component, TextReceiver
{
    public TMPro.TextMeshProUGUI m_textWidget;
    public string m_defaultText="Original";
    public int Writes;
    public bool RejectWrite;
    public void SetText(string text)
    {
        if (RejectWrite) return;
        Writes++; GetComponent<ZNetView>().GetZDO().Set(ZDOVars.s_text,text);
        // A denied/filtered display must never be overwritten by the mod renderer.
    }
}
public static class ZDOVars { public const string s_text="text"; }
public class ZDO
{
    public int m_uid;
    public long Owner;
    public readonly Dictionary<string,object> Fields=new();
    public long GetOwner()=>Owner;
    public void SetOwner(long owner)=>Owner=owner;
    public string GetString(string key,string fallback="")=>Fields.TryGetValue(key,out var v)?(string)v:fallback;
    public long GetLong(string key,long fallback=0)=>Fields.TryGetValue(key,out var v)?(long)v:fallback;
    public void Set(string key,string value)=>Fields[key]=value;
    public void Set(string key,long value)=>Fields[key]=value;
    public void CopyFrom(ZDO source)
    { Owner=source.Owner; Fields.Clear(); foreach(var entry in source.Fields) Fields[entry.Key]=entry.Value; }
}
public class ZNet
{
    public static ZNet instance=new(); public static long Session; public static double Clock=100;
    public static long GetUID()=>Session;
    public double GetTimeSeconds()=>Clock;
}
public class ZDOMan
{
    public static ZDOMan instance=new();
    public void ForceSendZDO(long target,int id)
    {
        var source=Network.Views[ZNet.Session].Data;
        var copy=new ZDO { m_uid=id }; copy.CopyFrom(source);
        Network.DataQueue.Enqueue(()=>Network.Views[target].Data.CopyFrom(copy));
    }
}
public class ZNetView : UnityEngine.Component
{
    public ZDO Data=new();
    public Dictionary<string,Delegate> Handlers=new();
    public bool Valid=true;
    public bool IsValid()=>Valid;
    public bool IsOwner()=>Data.Owner==ZNet.Session;
    public ZDO GetZDO()=>Data;
    public void Register<T,U,V>(string name,Action<long,T,U,V> f)=>Handlers.Add(name,f);
    public void Register<T,U>(string name,Action<long,T,U> f)=>Handlers.Add(name,f);
    public void Unregister(string name)=>Handlers.Remove(name);
    public void InvokeRPC(long target,string name,params object[] args)
    {
        long sender=ZNet.Session;
        Network.RpcQueue.Enqueue(()=>{
            ZNet.Session=target;
            var values=new object[args.Length+1]; values[0]=sender; Array.Copy(args,0,values,1,args.Length);
            Network.Views[target].Handlers[name].DynamicInvoke(values);
        });
    }
}
public static class Network
{
    public static Dictionary<long,ZNetView> Views=new();
    public static Queue<Action> RpcQueue=new(),DataQueue=new();
    public static bool LocalAccess=true,SenderAccess=true;
}
namespace RunicSigns
{
    internal static class Plugin { public static Logger Log=new(); }
    internal class Logger { public void LogWarning(object o){} public void LogError(object o){} }
}
namespace RunicSigns.Runtime
{
    internal static class SignAccess
    { public static bool Viewable=true; public static bool CanView(Sign s)=>Viewable; public static bool Eligible(Sign s)=>true; public static bool Local(Sign s)=>Network.LocalAccess; public static bool Sender(Sign s,long p)=>Network.SenderAccess; }
    internal static class SignEditor {
        public static bool BlockGameplay; public static int OpenPlacementCalls;
        public static UnityEngine.GameObject PlacementTarget;
        public static void Open(Sign s){}
        public static void OpenPlacement(Sign s){OpenPlacementCalls++;PlacementTarget=s.gameObject;BlockGameplay=true;}
        public static bool IsPlacementTarget(UnityEngine.GameObject ghost)=>BlockGameplay && ghost==PlacementTarget;
    }
}

namespace Runic.Shared { internal static class EmojiRenderer { internal static void Attach(TMPro.TextMeshProUGUI text) { } } }

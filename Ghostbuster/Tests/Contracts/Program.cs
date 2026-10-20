using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;

string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
string managed = Path.Combine(Environment.GetEnvironmentVariable("VALHEIM_INSTALL") ?? @"E:\SteamLibrary\steamapps\common\Valheim", "valheim_Data", "Managed");
string core = Path.Combine(Environment.GetEnvironmentVariable("BEPINEX_PROFILE") ?? @"C:\Users\Charles Sammons\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Default\BepInEx", "core");
AssemblyLoadContext.Default.Resolving += (ctx, name) => {
    foreach (string directory in new[] { managed, core })
    {
        string file = Path.Combine(directory, name.Name + ".dll");
        if (File.Exists(file)) return ctx.LoadFromAssemblyPath(file);
    }
    return null;
};
var game = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(managed, "assembly_valheim.dll"));
var plugin = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(root, "bin/Release/netstandard2.1/Ghostbuster.dll"));
const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
MethodInfo Method(Type type, string name) => type.GetMethods(all).Single(m => m.Name == name);
var opcodes = typeof(OpCodes).GetFields().Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)).ToDictionary(o => unchecked((ushort)o.Value));
List<(OpCode Code, MemberInfo Member)> Read(MethodBase method)
{
    var result = new List<(OpCode, MemberInfo)>();
    byte[] bytes = method.GetMethodBody().GetILAsByteArray();
    for (int i = 0; i < bytes.Length;)
    {
        ushort code = bytes[i++];
        if (code == 0xfe) code = (ushort)(0xfe00 | bytes[i++]);
        var op = opcodes[code]; MemberInfo member = null;
        int size = op.OperandType switch {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineI8 or OperandType.InlineR => 8,
            OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, i),
            _ => 4
        };
        if (op.OperandType is OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineType or OperandType.InlineTok)
            member = method.Module.ResolveMember(BitConverter.ToInt32(bytes, i));
        result.Add((op, member)); i += size;
    }
    return result;
}
int passed = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    Console.WriteLine("PASS " + message); passed++;
}
var zdo = game.GetType("ZDO", true);
var manager = game.GetType("ZDOMan", true);
var movement = Method(zdo, "InternalSetPosition");
var instructions = Read(movement);
int sectorCall = instructions.FindIndex(i => i.Member?.Name == "SetSector");
int positionStore = instructions.FindIndex(i => i.Code == OpCodes.Stfld && i.Member?.Name == "m_position");
Check(sectorCall >= 0 && positionStore > sectorCall, "Installed game has SetSector-before-position ordering");
Check(Read(Method(zdo,"SetSector")).Any(i => i.Member?.Name == "ZDOSectorInvalidated"), "Sector change invokes observer invalidation");
var peer = manager.GetNestedType("ZDOPeer", BindingFlags.NonPublic);
var invalidate = Read(Method(peer, "ZDOSectorInvalidated"));
Check(invalidate.Any(i => i.Member?.Name == "GetPosition") && invalidate.Any(i => i.Member?.Name == "InActiveArea"), "Peer invalidation reads current ZDO position and active area");
Check(Read(Method(manager, "RPC_ZDOData")).Any(i => i.Member?.Name == "InternalSetPosition"), "Incoming network positions use patched method");
Check(Read(Method(game.GetType("ZSyncTransform", true), "OwnerSync")).Any(i => i.Member?.Name == "SetPosition") && Read(Method(zdo, "SetPosition")).Any(i => i.Member?.Name == "InternalSetPosition"), "Host-owned player movement reaches patched method");
var patch = plugin.GetType("Ghostbuster.PlayerSectorPatch", true);
var patches = plugin.GetTypes().Where(t => t.GetCustomAttributesData().Any(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch")).ToArray();
Check(patches.Length == 1 && patches[0] == patch, "Release contains exactly one Harmony patch class");
var target = patch.GetCustomAttributesData().Single(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch");
var argumentTypes = (IList<CustomAttributeTypedArgument>)target.ConstructorArguments[2].Value;
Check((Type)target.ConstructorArguments[0].Value == zdo && (string)target.ConstructorArguments[1].Value == "InternalSetPosition" && argumentTypes.Count == 1 && (Type)argumentTypes[0].Value == movement.GetParameters()[0].ParameterType, "Harmony target matches installed game's exact method signature");
var prefix = Method(patch,"Prefix"); var postfix = Method(patch,"Postfix");
Check(prefix.ReturnType == typeof(void) && prefix.GetParameters().Single(p => p.Name == "__state").IsOut && postfix.GetParameters().Single(p => p.Name == "__state").ParameterType == typeof(bool), "Patch cannot skip original and passes per-call boolean state");
Check(prefix.GetCustomAttributesData().Any(a => a.AttributeType.Name == "HarmonyPrefix") && postfix.GetCustomAttributesData().Any(a => a.AttributeType.Name == "HarmonyPostfix"), "Prefix and postfix are registered with Harmony");
var calls = Read(postfix).Where(i => i.Code == OpCodes.Call || i.Code == OpCodes.Callvirt).Select(i => i.Member).OfType<MethodInfo>().ToArray();
Check(calls.Count(c => c.Name == "ZDOSectorInvalidated") == 1 && !calls.Any(c => c.Name is "SetPosition" or "DestroyZDO" or "TeleportTo" or "ForceSendZDO"), "Postfix only requests existing sector invalidation, never movement or destruction");
Check(!plugin.GetReferencedAssemblies().Any(a => a.Name.StartsWith("Runic")), "Release has no Runic dependencies");
Console.WriteLine($"PASS {passed}/{passed} installed-binary contracts. IL inspection only; no Unity runtime execution.");

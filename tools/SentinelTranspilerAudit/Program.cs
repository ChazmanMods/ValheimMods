#nullable enable
using Mono.Cecil;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using System.Collections;
var folders=args.Skip(1).Append(Path.GetDirectoryName(args[0])!).ToArray();
AssemblyLoadContext.Default.Resolving+=(c,n)=>{foreach(var d in folders){var p=Path.Combine(d,n.Name+".dll");if(File.Exists(p))return c.LoadFromAssemblyPath(p);}return null;};
var assembly=Assembly.LoadFrom(args[0]);var harmony=Assembly.Load("0Harmony");
var processor=harmony.GetType("HarmonyLib.PatchProcessor")!;
var getInstructions=processor.GetMethods(BindingFlags.Static|BindingFlags.Public).First(m=>m.Name=="GetOriginalInstructions"&&m.GetParameters().Length==2);
int count=0,fail=0;
foreach(var t in assembly.GetTypes().Where(t=>t.Namespace?.StartsWith("RunicSentinel.Devcommands")==true))
foreach(var method in t.GetMethods(BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public).Where(m=>m.Name=="Transpiler"||m.CustomAttributes.Any(a=>a.AttributeType.Name=="HarmonyTranspiler"))){
 Type? targetType=null;string? name=null;Type[]? signature=null;
 foreach(var a in t.CustomAttributes.Concat(method.CustomAttributes).Where(a=>a.AttributeType.Name=="HarmonyPatch"))foreach(var arg in a.ConstructorArguments){if(arg.Value is Type type)targetType=type;else if(arg.Value is string text)name=text;else if(arg.ArgumentType==typeof(Type[]))signature=((IEnumerable<CustomAttributeTypedArgument>)arg.Value!).Select(a=>(Type)a.Value!).ToArray();}
 if(targetType==null||name==null)continue;
 count++;try{
  var target=targetType.GetMethods(BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Single(m=>m.Name==name&&(signature==null||m.GetParameters().Select(p=>p.ParameterType).SequenceEqual(signature)));
  var generator=new DynamicMethod("inspect",typeof(void),Type.EmptyTypes).GetILGenerator();
  using var originalAssembly=AssemblyDefinition.ReadAssembly(target.Module.FullyQualifiedName);
  var def=(MethodDefinition)originalAssembly.MainModule.LookupToken(target.MetadataToken);
  var codeType=harmony.GetType("HarmonyLib.CodeInstruction")!;
  var instructions=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(codeType))!;
  var opcodes=typeof(System.Reflection.Emit.OpCodes).GetFields(BindingFlags.Public|BindingFlags.Static).Select(f=>(System.Reflection.Emit.OpCode)f.GetValue(null)!).ToDictionary(o=>o.Value);
  var labels=def.Body.Instructions.ToDictionary(i=>i,i=>generator.DefineLabel());
  foreach(var i in def.Body.Instructions){
   object? operand=i.Operand;
   if(operand is MemberReference member)operand=target.Module.ResolveMember(member.MetadataToken.ToInt32());
   else if(operand is Mono.Cecil.Cil.Instruction branch)operand=labels[branch];
   else if(operand is Mono.Cecil.Cil.Instruction[] branches)operand=branches.Select(b=>labels[b]).ToArray();
   else if(operand is Mono.Cecil.Cil.VariableDefinition local)operand=local.Index;
   else if(operand is ParameterDefinition parameter)operand=parameter.Index+(target.IsStatic?0:1);
   var instruction=Activator.CreateInstance(codeType,new object?[]{opcodes[i.OpCode.Value],operand})!;
   ((IList)codeType.GetField("labels")!.GetValue(instruction)!).Add(labels[i]);instructions.Add(instruction);
  }
  var pars=method.GetParameters().Select(p=>p.ParameterType==typeof(ILGenerator)?(object)generator:typeof(MethodBase).IsAssignableFrom(p.ParameterType)?target:instructions).ToArray();
  var result=(IEnumerable)method.Invoke(null,pars)!;int n=0;foreach(var instruction in result)n++;
  Console.WriteLine($"PASS {t.Name}.{method.Name}: {n} instructions");
 }catch(Exception e){fail++;Console.WriteLine($"FAIL {t.Name}.{method.Name}: {e}");}
}
Console.WriteLine($"{count-fail}/{count} transpilers passed.");return fail==0?0:1;


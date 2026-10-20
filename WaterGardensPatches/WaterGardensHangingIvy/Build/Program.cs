using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;
using var original=AssemblyDefinition.ReadAssembly(args[0]);
using var donor=AssemblyDefinition.ReadAssembly(args[1]);
var module=original.MainModule;
if(original.Name.Version!=new Version(1,1,3,0))throw new Exception("Expected approved WaterGardens 1.1.3");
var snapshots=Types(module.Types).SelectMany(t=>t.Methods).ToDictionary(m=>m.FullName,Fingerprint);
var hashes=module.Resources.OfType<EmbeddedResource>().ToDictionary(r=>r.Name,r=>Convert.ToHexString(SHA256.HashData(r.GetResourceData())));
var fromType=donor.MainModule.GetType("RunicLilyPads.HangingIvy");
var newType=new TypeDefinition(fromType.Namespace,fromType.Name,fromType.Attributes,module.TypeSystem.Object);module.Types.Add(newType);
foreach(var m in fromType.Methods){var to=new MethodDefinition(m.Name,m.Attributes,MapType(m.ReturnType));foreach(var p in m.Parameters)to.Parameters.Add(new ParameterDefinition(p.Name,p.Attributes,MapType(p.ParameterType)));newType.Methods.Add(to);}
foreach(var m in fromType.Methods)CopyBody(m,newType.Methods.Single(x=>x.Name==m.Name));
var register=module.GetType("RunicLilyPads.GardenModels").Methods.Single(m=>m.Name=="RegisterStatics");
snapshots.Remove(register.FullName);
foreach(var ret in register.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ret).ToArray())register.Body.GetILProcessor().InsertBefore(ret,Instruction.Create(OpCodes.Call,newType.Methods.Single(m=>m.Name=="Register")));
var menu=module.GetType("RunicLilyPads.PondMenu").Methods.Single(m=>m.IsConstructor&&m.Parameters.Count>0&&m.Parameters[0].ParameterType.Name=="GardenTree");
snapshots.Remove(menu.FullName);
foreach(var call in menu.Body.Instructions.Where(i=>i.Operand is MethodReference m&&m.DeclaringType.Name=="RunicText"&&m.Name=="Get").ToArray()){
 var il=menu.Body.GetILProcessor();var target=Instruction.Create(OpCodes.Ldarg_1);il.InsertAfter(call,target);il.InsertAfter(target,Instruction.Create(OpCodes.Call,newType.Methods.Single(m=>m.Name=="MenuText")));
}
// Generalize only the interaction label; all size persistence and editor logic stays original.
var hover=module.GetType("RunicLilyPads.GardenTree").Methods.Single(m=>m.Name=="GetHoverText");snapshots.Remove(hover.FullName);
foreach(var call in hover.Body.Instructions.Where(i=>i.Operand is MethodReference m&&m.DeclaringType.Name=="RunicText"&&m.Name=="Get").ToArray()){
 var prev=call.Previous;prev.Operand="\n[<color=yellow><b>E</b></color>] Size";call.OpCode=OpCodes.Nop;call.Operand=null;
}
foreach(var f in Directory.GetFiles(args[3]))module.Resources.Add(new EmbeddedResource("WaterGardens.Models."+Path.GetFileName(f),ManifestResourceAttributes.Private,File.ReadAllBytes(f)));
original.Name.Version=new Version(1,3,0,0);
foreach(var a in original.CustomAttributes.Where(a=>a.AttributeType.Name is "AssemblyFileVersionAttribute" or "AssemblyInformationalVersionAttribute"))a.ConstructorArguments[0]=new CustomAttributeArgument(module.TypeSystem.String,"1.3.0.0");
module.GetType("RunicLilyPads.Plugin").CustomAttributes.Single(a=>a.AttributeType.Name=="BepInPlugin").ConstructorArguments[2]=new CustomAttributeArgument(module.TypeSystem.String,"1.3.0");
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);original.Write(args[2]);
using var check=AssemblyDefinition.ReadAssembly(args[2]);
foreach(var m in Types(check.MainModule.Types).SelectMany(t=>t.Methods))if(snapshots.TryGetValue(m.FullName,out var fingerprint)&&Fingerprint(m)!=fingerprint)throw new Exception("Unexpected baseline change: "+m.FullName);
foreach(var r in check.MainModule.Resources.OfType<EmbeddedResource>())if(hashes.TryGetValue(r.Name,out var hash)&&hash!=Convert.ToHexString(SHA256.HashData(r.GetResourceData())))throw new Exception("Changed resource "+r.Name);
Console.WriteLine($"PASS: {snapshots.Count} original method bodies and {hashes.Count} original assets preserved. Only ivy registration and size-menu labels changed.");
static IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> roots){foreach(var t in roots){yield return t;foreach(var child in Types(t.NestedTypes))yield return child;}}
static string Fingerprint(MethodDefinition m)=>!m.HasBody?"":string.Join("\n",m.Body.Instructions.Select(i=>i.OpCode.Code+":"+(i.Operand switch{Instruction jump=>m.Body.Instructions.IndexOf(jump).ToString(),Instruction[] jumps=>string.Join(",",jumps.Select(j=>m.Body.Instructions.IndexOf(j))),_=>i.Operand?.ToString()??""})));
TypeReference MapType(TypeReference t){
 if(t is GenericParameter)return t;
 if(t is GenericInstanceType g){var result=new GenericInstanceType(MapType(g.ElementType));foreach(var a in g.GenericArguments)result.GenericArguments.Add(MapType(a));return result;}
 if(t is ArrayType array)return new ArrayType(MapType(array.ElementType),array.Rank);
 if(t is ByReferenceType b)return new ByReferenceType(MapType(b.ElementType));
 if(t is PointerType p)return new PointerType(MapType(p.ElementType));
 var own=Types(module.Types).FirstOrDefault(x=>x.FullName==t.FullName);if(own!=null)return own;
 return module.ImportReference(t);
}
MethodReference MapMethod(MethodReference m){
 if(m is GenericInstanceMethod g){var result=new GenericInstanceMethod(MapMethod(g.ElementMethod));foreach(var a in g.GenericArguments)result.GenericArguments.Add(MapType(a));return result;}
 var own=Types(module.Types).FirstOrDefault(t=>t.FullName==m.DeclaringType.FullName);
 if(own!=null)return own.Methods.Single(x=>x.FullName==m.FullName);
 var copy=new MethodReference(m.Name,MapType(m.ReturnType),MapType(m.DeclaringType)){HasThis=m.HasThis,ExplicitThis=m.ExplicitThis,CallingConvention=m.CallingConvention};
 foreach(var gp in m.GenericParameters)copy.GenericParameters.Add(new GenericParameter(gp.Name,copy));
 foreach(var p in m.Parameters)copy.Parameters.Add(new ParameterDefinition(MapType(p.ParameterType)));
 return copy;
}
void CopyBody(MethodDefinition from,MethodDefinition to){
 to.Body=new MethodBody(to){InitLocals=from.Body.InitLocals,MaxStackSize=from.Body.MaxStackSize};
 foreach(var variable in from.Body.Variables)to.Body.Variables.Add(new VariableDefinition(MapType(variable.VariableType)));
 var instructions=from.Body.Instructions.ToDictionary(i=>i,i=>Instruction.Create(OpCodes.Nop));
 foreach(var pair in instructions){pair.Value.OpCode=pair.Key.OpCode;pair.Value.Operand=pair.Key.Operand switch{
  Instruction jump=>instructions[jump],Instruction[] jumps=>jumps.Select(j=>instructions[j]).ToArray(),
  VariableDefinition v=>to.Body.Variables[v.Index],ParameterDefinition p=>to.Parameters[p.Index],
  MethodReference m=>MapMethod(m),FieldReference f=>new FieldReference(f.Name,MapType(f.FieldType),MapType(f.DeclaringType)),TypeReference t=>MapType(t),var value=>value};to.Body.Instructions.Add(pair.Value);}
 foreach(var h in from.Body.ExceptionHandlers)to.Body.ExceptionHandlers.Add(new ExceptionHandler(h.HandlerType){TryStart=instructions[h.TryStart],TryEnd=h.TryEnd==null?null:instructions[h.TryEnd],HandlerStart=instructions[h.HandlerStart],HandlerEnd=h.HandlerEnd==null?null:instructions[h.HandlerEnd],CatchType=h.CatchType==null?null:MapType(h.CatchType),FilterStart=h.FilterStart==null?null:instructions[h.FilterStart]});
}

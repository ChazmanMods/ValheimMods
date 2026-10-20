using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;
using var original=AssemblyDefinition.ReadAssembly(args[0]);
using var donor=AssemblyDefinition.ReadAssembly(args[1]);
var module=original.MainModule;
if(original.Name.Version!=new Version(1,3,0,0))throw new Exception("Expected tested 1.3.0 ivy baseline");
var snapshots=Types(module.Types).SelectMany(t=>t.Methods).ToDictionary(m=>m.FullName,Fingerprint);
var hashes=module.Resources.OfType<EmbeddedResource>().ToDictionary(r=>r.Name,r=>Convert.ToHexString(SHA256.HashData(r.GetResourceData())));
string[] names={"HangingIvy","IvyLights","IvyAttachment"};
foreach(var name in names){var from=donor.MainModule.GetType("RunicLilyPads."+name);var target=module.GetType(from.FullName);
 if(target==null){target=new TypeDefinition(from.Namespace,from.Name,from.Attributes,MapType(from.BaseType));module.Types.Add(target);}else{foreach(var m in target.Methods)snapshots.Remove(m.FullName);target.Fields.Clear();}
 foreach(var i in from.Interfaces)target.Interfaces.Add(new InterfaceImplementation(MapType(i.InterfaceType)));
}
foreach(var name in names){var from=donor.MainModule.GetType("RunicLilyPads."+name);var target=module.GetType(from.FullName);
 foreach(var f in from.Fields){var field=new FieldDefinition(f.Name,f.Attributes,MapType(f.FieldType));if(f.HasConstant)field.Constant=f.Constant;target.Fields.Add(field);}
 foreach(var m in from.Methods){if(target.Methods.Any(x=>x.FullName==m.FullName))continue;var to=new MethodDefinition(m.Name,m.Attributes,MapType(m.ReturnType));foreach(var p in m.Parameters)to.Parameters.Add(new ParameterDefinition(p.Name,p.Attributes,MapType(p.ParameterType)));target.Methods.Add(to);}
}
foreach(var name in names){var from=donor.MainModule.GetType("RunicLilyPads."+name);var target=module.GetType(from.FullName);foreach(var m in from.Methods)if(m.HasBody)CopyBody(m,target.Methods.Single(x=>x.FullName==m.FullName));}
foreach(var f in Directory.GetFiles(args[3],"*.bulbs"))module.Resources.Add(new EmbeddedResource("WaterGardens.Models."+Path.GetFileName(f),ManifestResourceAttributes.Private,File.ReadAllBytes(f)));
original.Name.Version=new Version(1,3,1,0);
foreach(var a in original.CustomAttributes.Where(a=>a.AttributeType.Name is "AssemblyFileVersionAttribute" or "AssemblyInformationalVersionAttribute"))a.ConstructorArguments[0]=new CustomAttributeArgument(module.TypeSystem.String,"1.3.1.0");
module.GetType("RunicLilyPads.Plugin").CustomAttributes.Single(a=>a.AttributeType.Name=="BepInPlugin").ConstructorArguments[2]=new CustomAttributeArgument(module.TypeSystem.String,"1.3.1");
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);original.Write(args[2]);
using var check=AssemblyDefinition.ReadAssembly(args[2]);
foreach(var m in Types(check.MainModule.Types).SelectMany(t=>t.Methods))if(snapshots.TryGetValue(m.FullName,out var fingerprint)&&Fingerprint(m)!=fingerprint)throw new Exception("Unexpected baseline change: "+m.FullName);
foreach(var r in check.MainModule.Resources.OfType<EmbeddedResource>())if(hashes.TryGetValue(r.Name,out var hash)&&hash!=Convert.ToHexString(SHA256.HashData(r.GetResourceData())))throw new Exception("Changed resource "+r.Name);
Console.WriteLine($"PASS: {snapshots.Count} original method bodies and {hashes.Count} original assets preserved. Only new ivy behavior changed.");
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

using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;

if(args.Length is <3 or >4)throw new ArgumentException("Original 0.6.1 DLL, donor DLL, output DLL and optional build-icon directory required");
bool withIcons=args.Length==4;
string releaseVersion=withIcons?"0.6.8":"0.6.7";
using var original=AssemblyDefinition.ReadAssembly(args[0]);
using var donor=AssemblyDefinition.ReadAssembly(args[1]);
var module=original.MainModule;
if(original.Name.Version!=new Version(0,6,1,0))throw new Exception("Expected original 0.6.1 baseline");
var allowed=new HashSet<string>{"ArcaneWaterWorks.Route::AppendFrom","ArcaneWaterWorks.Route::NeedsContinuation","ArcaneWaterWorks.RiverHoe::Extend"};
if(withIcons){allowed.Add("ArcaneWaterWorks.Plugin::Register");allowed.Add("ArcaneWaterWorks.HotSpringPrefabs::Register");}
var methodSnapshots=Types(module.Types).SelectMany(t=>t.Methods).Where(m=>!allowed.Contains(Key(m))).ToDictionary(m=>m.FullName,Fingerprint);
var resources=module.Resources.OfType<EmbeddedResource>().ToDictionary(r=>r.Name,r=>Convert.ToHexString(SHA256.HashData(r.GetResourceData())));
foreach(var (typeName,methodName) in new[]{("ArcaneWaterWorks.Route","NeedsContinuation"),("ArcaneWaterWorks.Route","AppendFrom"),("ArcaneWaterWorks.RiverHoe","Extend")}){
 var from=donor.MainModule.GetType(typeName).Methods.Single(m=>m.Name==methodName);
 var type=module.GetType(typeName);var to=type.Methods.SingleOrDefault(m=>m.Name==methodName);
 if(to==null){to=new MethodDefinition(from.Name,from.Attributes,MapType(from.ReturnType));foreach(var p in from.Parameters)to.Parameters.Add(new ParameterDefinition(p.Name,p.Attributes,MapType(p.ParameterType)));type.Methods.Add(to);}
 CopyBody(from,to);
}
// A distinct classic-maintenance release; no later rendering or resource changes.
original.Name.Version=new Version(releaseVersion+".0");
foreach(var a in original.CustomAttributes.Where(a=>a.AttributeType.Name is "AssemblyFileVersionAttribute" or "AssemblyInformationalVersionAttribute"))a.ConstructorArguments[0]=new CustomAttributeArgument(module.TypeSystem.String,releaseVersion+".0");
var plugin=module.GetType("ArcaneWaterWorks.Plugin");
var metadata=plugin.CustomAttributes.Single(a=>a.AttributeType.Name=="BepInPlugin");
metadata.ConstructorArguments[2]=new CustomAttributeArgument(module.TypeSystem.String,releaseVersion);
var iconHashes=new Dictionary<string,string>();
if(withIcons){
 var from=donor.MainModule.GetType("ArcaneWaterWorks.Plugin").Methods.Single(m=>m.Name=="BuildIcon");
 var helper=new MethodDefinition(from.Name,from.Attributes,MapType(from.ReturnType));
 foreach(var p in from.Parameters)helper.Parameters.Add(new ParameterDefinition(p.Name,p.Attributes,MapType(p.ParameterType)));
 plugin.Methods.Add(helper);CopyBody(from,helper);
 foreach(var key in new[]{"river-source","hot-spring-round","hot-spring-oval","hot-spring-kidney","hot-spring-wild"}){
  var bytes=File.ReadAllBytes(Path.Combine(args[3],key+".png"));var name="ArcaneWaterWorks.BuildIcons."+key+".png";
  module.Resources.Add(new EmbeddedResource(name,ManifestResourceAttributes.Private,bytes));iconHashes.Add(name,Convert.ToHexString(SHA256.HashData(bytes)));
 }
 foreach(var typeName in new[]{"ArcaneWaterWorks.Plugin","ArcaneWaterWorks.HotSpringPrefabs"}){
  var register=module.GetType(typeName).Methods.Single(m=>m.Name=="Register");
  var assignment=register.Body.Instructions.Single(i=>i.OpCode==OpCodes.Stfld&&i.Operand is FieldReference f&&f.DeclaringType.Name=="Piece"&&f.Name=="m_icon");
  var il=register.Body.GetILProcessor();
  var fallback=new VariableDefinition(MapType(from.ReturnType));register.Body.Variables.Add(fallback);register.Body.InitLocals=true;
  // Retain the original sprite if an icon cannot be decoded.
  il.InsertBefore(assignment,Instruction.Create(OpCodes.Stloc,fallback));
  il.InsertBefore(assignment,Instruction.Create(OpCodes.Dup));
  il.InsertBefore(assignment,Instruction.Create(OpCodes.Ldloc,fallback));
  il.InsertBefore(assignment,Instruction.Create(OpCodes.Call,helper));
 }
}
foreach(var method in Types(module.Types).SelectMany(t=>t.Methods))if(method.HasBody)foreach(var instruction in method.Body.Instructions){
 if(instruction.OpCode==OpCodes.Ldstr&&instruction.Operand is string s&&s.Contains("0.6.1")){
  methodSnapshots.Remove(method.FullName);instruction.Operand=s.Replace("0.6.1",releaseVersion);
 }
}
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);original.Write(args[2]);
using var check=AssemblyDefinition.ReadAssembly(args[2]);
foreach(var method in Types(check.MainModule.Types).SelectMany(t=>t.Methods))if(methodSnapshots.TryGetValue(method.FullName,out var expected)&&Fingerprint(method)!=expected)throw new Exception("Unexpected method change: "+method.FullName);
foreach(var r in check.MainModule.Resources.OfType<EmbeddedResource>())if(!(resources.TryGetValue(r.Name,out var hash)||iconHashes.TryGetValue(r.Name,out hash))||hash!=Convert.ToHexString(SHA256.HashData(r.GetResourceData())))throw new Exception("Resource changed: "+r.Name);
if(check.MainModule.Resources.Count!=module.Resources.Count)throw new Exception("Resource count changed");
Console.WriteLine($"PASS: classic {releaseVersion} produced; {methodSnapshots.Count} other method bodies and {resources.Count} original embedded resources preserved; {iconHashes.Count} build icons embedded.");

string Key(MethodDefinition m)=>m.DeclaringType.FullName+"::"+m.Name;
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

using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;
using var original=AssemblyDefinition.ReadAssembly(args[0]);using var donor=AssemblyDefinition.ReadAssembly(args[1]);var module=original.MainModule;
if(original.Name.Version!=new Version(0,2,0,0))throw new Exception("Expected Koi 0.2.0");
var snapshots=Types(module.Types).SelectMany(t=>t.Methods).ToDictionary(m=>m.FullName,Fingerprint);var hashes=module.Resources.OfType<EmbeddedResource>().ToDictionary(r=>r.Name,r=>Convert.ToHexString(SHA256.HashData(r.GetResourceData())));
var from=donor.MainModule.GetType("WaterGardensKoi.KoiMotion");var target=new TypeDefinition(from.Namespace,from.Name,from.Attributes,MapType(from.BaseType));module.Types.Add(target);
foreach(var m in from.Methods){var to=new MethodDefinition(m.Name,m.Attributes,MapType(m.ReturnType));foreach(var p in m.Parameters)to.Parameters.Add(new ParameterDefinition(p.Name,p.Attributes,MapType(p.ParameterType)));target.Methods.Add(to);}
foreach(var m in from.Methods)CopyBody(m,target.Methods.Single(x=>x.Name==m.Name));
var orbit=module.GetType("WaterGardensKoi.KoiSchool").Methods.Single(m=>m.Name=="Orbit");snapshots.Remove(orbit.FullName);CopyBody(donor.MainModule.GetType("WaterGardensKoi.KoiSchool").Methods.Single(m=>m.Name=="Orbit"),orbit);
var school=module.GetType("WaterGardensKoi.KoiSchool");
var position=school.Methods.Single(x=>x.Name=="Position");snapshots.Remove(position.FullName);
foreach(var i in position.Body.Instructions){if(i.OpCode==OpCodes.Ldc_R4){float v=(float)i.Operand;if(v==.015f)i.Operand=-.04f;if(v==.055f)i.Operand=.012f;}if(i.OpCode==OpCodes.Ldc_R8&&(double)i.Operand==9d)i.Operand=3d;}
var update=school.Methods.Single(x=>x.Name=="Update");snapshots.Remove(update.FullName);
foreach(var i in update.Body.Instructions){if(i.OpCode==OpCodes.Ldc_R4){float v=(float)i.Operand;if(v==32f)i.Operand=8f;if(v==1.4f)i.Operand=.8f;}if(i.OpCode==OpCodes.Ldc_R8&&(double)i.Operand==9d)i.Operand=3d;if(i.OpCode==OpCodes.Ldc_I4_S&&(sbyte)i.Operand==12)i.Operand=(sbyte)8;}
// First Position call supplies the actual position; retain the future sample for facing.
var positionCall=update.Body.Instructions.First(i=>i.Operand is MethodReference mr&&mr.Name=="Position");
var indexLoad=positionCall.Previous.Previous.Previous;
// Find the loop index load preceding clock and target arguments.
while(indexLoad.OpCode.Code is not (Code.Ldloc or Code.Ldloc_S or Code.Ldloc_0 or Code.Ldloc_1 or Code.Ldloc_2 or Code.Ldloc_3))indexLoad=indexLoad.Previous;
var il=update.Body.GetILProcessor();var ownerLoad=Instruction.Create(OpCodes.Ldarg_0);il.InsertAfter(positionCall,ownerLoad);
var idx=Instruction.Create(OpCodes.Nop);idx.OpCode=indexLoad.OpCode;idx.Operand=indexLoad.Operand;il.InsertAfter(ownerLoad,idx);
var adjust=Instruction.Create(OpCodes.Call,target.Methods.Single(x=>x.Name=="Adjust"));il.InsertAfter(idx,adjust);
var storedPosition=adjust.Next;
var futureCall=update.Body.Instructions.First(i=>i!=positionCall&&i.Operand is MethodReference mr&&mr.Name=="Position");
var subtract=futureCall.Next;while(!(subtract.Operand is MethodReference mr&&mr.Name=="op_Subtraction"))subtract=subtract.Next;
var dirOwner=Instruction.Create(OpCodes.Ldarg_0);il.InsertAfter(subtract,dirOwner);
var dirIndex=Instruction.Create(OpCodes.Nop);dirIndex.OpCode=idx.OpCode;dirIndex.Operand=idx.Operand;il.InsertAfter(dirOwner,dirIndex);
var posLoad=Instruction.Create(OpCodes.Nop);posLoad.OpCode=storedPosition.OpCode.Code switch{Code.Stloc_0=>OpCodes.Ldloc_0,Code.Stloc_1=>OpCodes.Ldloc_1,Code.Stloc_2=>OpCodes.Ldloc_2,Code.Stloc_3=>OpCodes.Ldloc_3,Code.Stloc_S=>OpCodes.Ldloc_S,_=>OpCodes.Ldloc};posLoad.Operand=storedPosition.Operand;il.InsertAfter(dirIndex,posLoad);
il.InsertAfter(posLoad,Instruction.Create(OpCodes.Call,target.Methods.Single(x=>x.Name=="Direction")));
foreach(var rotation in update.Body.Instructions.Where(i=>i.Operand is MethodReference mr&&mr.Name=="set_rotation").ToArray()){
 var turnOwner=Instruction.Create(OpCodes.Ldarg_0);il.InsertBefore(rotation,turnOwner);
 var turnIndex=Instruction.Create(OpCodes.Nop);turnIndex.OpCode=idx.OpCode;turnIndex.Operand=idx.Operand;il.InsertBefore(rotation,turnIndex);
 il.InsertBefore(rotation,Instruction.Create(OpCodes.Call,target.Methods.Single(x=>x.Name=="Turn")));
}
// Insertions can outgrow the original short branch offsets. Widen before writing.
var longBranches=typeof(OpCodes).GetFields().Where(f=>f.FieldType==typeof(OpCode)).Select(f=>(OpCode)f.GetValue(null)).Where(op=>op.OperandType==OperandType.InlineBrTarget).ToDictionary(op=>op.Name);
foreach(var ins in update.Body.Instructions)if(ins.OpCode.OperandType==OperandType.ShortInlineBrTarget)ins.OpCode=longBranches[ins.OpCode.Name[..^2]];
// KoiMotion accesses these existing members directly; only assembly visibility changes.
school.Fields.Single(x=>x.Name=="fish").IsAssembly=true;school.Methods.Single(x=>x.Name=="KeepInside").IsAssembly=true;
original.Name.Version=new Version(0,2,2,0);foreach(var a in original.CustomAttributes.Where(a=>a.AttributeType.Name is "AssemblyFileVersionAttribute" or "AssemblyInformationalVersionAttribute"))a.ConstructorArguments[0]=new CustomAttributeArgument(module.TypeSystem.String,"0.2.2");module.GetType("WaterGardensKoi.Plugin").CustomAttributes.Single(a=>a.AttributeType.Name=="BepInPlugin").ConstructorArguments[2]=new CustomAttributeArgument(module.TypeSystem.String,"0.2.2");original.Write(args[2]);
using var check=AssemblyDefinition.ReadAssembly(args[2]);foreach(var m in Types(check.MainModule.Types).SelectMany(t=>t.Methods))if(snapshots.TryGetValue(m.FullName,out var fingerprint)&&Fingerprint(m)!=fingerprint)throw new Exception("Unexpected method change "+m.FullName);foreach(var r in check.MainModule.Resources.OfType<EmbeddedResource>())if(hashes[r.Name]!=Convert.ToHexString(SHA256.HashData(r.GetResourceData())))throw new Exception("Asset changed "+r.Name);Console.WriteLine($"PASS: {snapshots.Count} unchanged methods; {hashes.Count} assets preserved. Cruising, feeding animation and visual avoidance patched; food authority preserved.");
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

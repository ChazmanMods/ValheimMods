#nullable enable
using Mono.Cecil;
var resolver=new DefaultAssemblyResolver();resolver.AddSearchDirectory(args[1]);resolver.AddSearchDirectory(Path.GetDirectoryName(args[0])!);resolver.AddSearchDirectory(args[2]);foreach(var path in args.Skip(3))resolver.AddSearchDirectory(path);
using var assembly=AssemblyDefinition.ReadAssembly(args[0],new ReaderParameters{AssemblyResolver=resolver});
int total=0,fail=0;
(TypeReference? type,string? name,int kind,List<string>? parameters) Read(IEnumerable<CustomAttribute> attrs,(TypeReference? type,string? name,int kind,List<string>? parameters) target){
 foreach(var attr in attrs.Where(a=>a.AttributeType.FullName=="HarmonyLib.HarmonyPatch"))foreach(var a in attr.ConstructorArguments){
  if(a.Value is TypeReference tr)target.type=tr;
  else if(a.Type.FullName=="System.String")target.name=(string)a.Value;
  else if(a.Type.FullName=="HarmonyLib.MethodType")target.kind=Convert.ToInt32(a.Value);
  else if(a.Value is CustomAttributeArgument[] values&&a.Type.FullName=="System.Type[]")target.parameters=values.Select(v=>((TypeReference)v.Value).FullName).ToList();
 }return target;
}
foreach(var t in assembly.MainModule.Types.Where(t=>t.Namespace.StartsWith("RunicSentinel.Devcommands"))){
 var baseTarget=Read(t.CustomAttributes,(null,null,0,null));
 foreach(var patch in t.Methods){
  if(!patch.CustomAttributes.Any(a=>new[]{"HarmonyPrefix","HarmonyPostfix","HarmonyTranspiler","HarmonyFinalizer"}.Contains(a.AttributeType.Name))&&!new[]{"Prefix","Postfix","Transpiler","Finalizer"}.Contains(patch.Name))continue;
  var target=Read(patch.CustomAttributes,baseTarget);if(target.type==null)continue;
  var name=target.kind==3?".ctor":target.kind==1?"get_"+target.name:target.kind==2?"set_"+target.name:target.name;
  var methods=target.type.Resolve().Methods.Where(m=>m.Name==name).Where(m=>target.parameters==null||m.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(target.parameters)).ToArray();total++;
  if(methods.Length!=1){Console.WriteLine($"FAIL {t.Name}.{patch.Name} -> {target.type.Name}.{name}: {methods.Length} matching targets");fail++;continue;}
  var original=methods[0];
  foreach(var p in patch.Parameters.Where(p=>p.Name=="__instance")){
   var instanceType=p.ParameterType is ByReferenceType byRef?byRef.ElementType:p.ParameterType;
   var current=original.DeclaringType;bool compatible=instanceType.FullName=="System.Object";
   while(current!=null){if(current.FullName==instanceType.FullName){compatible=true;break;}try{current=current.BaseType?.Resolve();}catch{break;}}
   if(!compatible){Console.WriteLine($"FAIL {t.Name}.{patch.Name}: __instance {instanceType.FullName} cannot receive {original.DeclaringType.FullName}");fail++;}
  }
  foreach(var p in patch.Parameters){if(p.Name.StartsWith("__")||p.ParameterType.FullName.Contains("CodeInstruction")||p.ParameterType.FullName.Contains("ILGenerator")||p.ParameterType.FullName.Contains("MethodBase"))continue;
   if((patch.Name.Contains("Postfix")||patch.CustomAttributes.Any(a=>a.AttributeType.Name=="HarmonyPostfix"))&&!patch.ReturnType.FullName.Equals("System.Void")&&p==patch.Parameters[0]&&p.ParameterType.FullName==patch.ReturnType.FullName)continue;
   if(!original.Parameters.Any(x=>x.Name==p.Name)){Console.WriteLine($"FAIL {t.Name}.{patch.Name} parameter {p.Name}; target params: {string.Join(",",original.Parameters.Select(p=>p.Name))}");fail++;}
  }
 }
}
foreach(var type in assembly.MainModule.Types.Where(t=>t.Namespace.StartsWith("RunicSentinel.Devcommands")))foreach(var method in type.Methods.Where(m=>m.HasBody))foreach(var i in method.Body.Instructions){
 if(i.Operand is MemberReference mr&&mr.FullName.Contains("GuiInput"))Console.WriteLine("GUI DEP: "+type.Name+"."+method.Name+" "+mr.FullName);
 if(i.Operand is MemberReference tr&&(tr.DeclaringType?.Namespace?.Contains("TMPro")==true||tr.DeclaringType?.Namespace?.Contains("GUIFramework")==true))Console.WriteLine("GUI DEP: "+type.Name+"."+method.Name+" "+tr.FullName);
}
Console.WriteLine($"Audited {total} patch targets, {fail} failures.");return fail==0?0:1;

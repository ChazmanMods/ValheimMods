using Mono.Cecil;
var output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
foreach(var path in Directory.GetFiles(args[0],"*.dll")) {
 if(!new[]{"assembly_valheim","assembly_utils","assembly_guiutils","gui_framework"}.Contains(Path.GetFileNameWithoutExtension(path)))continue;
 var resolver=new DefaultAssemblyResolver();resolver.AddSearchDirectory(args[0]);
 using var assembly=AssemblyDefinition.ReadAssembly(path,new ReaderParameters{AssemblyResolver=resolver});
 void Visit(TypeDefinition t){if(t.IsNested)t.IsNestedPublic=true;else t.IsPublic=true;foreach(var f in t.Fields)f.IsPublic=true;foreach(var m in t.Methods)m.IsPublic=true;foreach(var n in t.NestedTypes)Visit(n);}
 foreach(var t in assembly.MainModule.Types)Visit(t);
 assembly.Write(Path.Combine(output,Path.GetFileName(path)));Console.WriteLine(Path.GetFileName(path));
}
if(args.Length>2){
 using var a=AssemblyDefinition.ReadAssembly(args[2]);
 foreach(var t in a.MainModule.GetTypeReferences())if(t.Namespace=="System.Reflection.Emit"&&(t.Name=="ILGenerator"||t.Name=="LocalBuilder")) {
 var scope=new AssemblyNameReference("System.Reflection.Emit.ILGeneration",new Version(4,0,0,0)){PublicKeyToken=new byte[]{0xb0,0x3f,0x5f,0x7f,0x11,0xd5,0x0a,0x3a}};
 a.MainModule.AssemblyReferences.Add(scope);t.Scope=scope;
 }
 a.Write(Path.Combine(output,"0Harmony.dll"));
}

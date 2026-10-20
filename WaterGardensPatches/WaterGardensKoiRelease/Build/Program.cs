using Mono.Cecil;using Mono.Cecil.Cil;using System.Security.Cryptography;
using var a=AssemblyDefinition.ReadAssembly(args[0]);var m=a.MainModule;
IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> roots){foreach(var t in roots){yield return t;foreach(var n in Types(t.NestedTypes))yield return n;}}
string Body(MethodDefinition x)=>x.HasBody?string.Join("\n",x.Body.Instructions.Select(i=>i.ToString())):"";
var code=Types(m.Types).SelectMany(t=>t.Methods).ToDictionary(x=>x.FullName,Body);
var hashes=m.Resources.OfType<EmbeddedResource>().ToDictionary(r=>r.Name,r=>Convert.ToHexString(SHA256.HashData(r.GetResourceData())));
var plugin=m.GetType("WaterGardensKoi.Plugin");var attr=plugin.CustomAttributes.Single(x=>x.AttributeType.Name=="BepInPlugin");
if((string)attr.ConstructorArguments[2].Value!="0.2.2")throw new Exception("Expected tested KoiFish 0.2.2");
var guid=(string)attr.ConstructorArguments[0].Value;var deps=plugin.CustomAttributes.Where(x=>x.AttributeType.Name!="BepInPlugin").Select(x=>x.AttributeType.FullName+Convert.ToHexString(x.GetBlob())).ToArray();
attr.ConstructorArguments[2]=new CustomAttributeArgument(m.TypeSystem.String,"1.0.0");a.Name.Version=new Version(1,0,0,0);
foreach(var c in a.CustomAttributes.Where(c=>c.AttributeType.Name is "AssemblyFileVersionAttribute" or "AssemblyInformationalVersionAttribute"))c.ConstructorArguments[0]=new CustomAttributeArgument(m.TypeSystem.String,"1.0.0");
a.Write(args[1]);using var output=AssemblyDefinition.ReadAssembly(args[1]);
foreach(var x in Types(output.MainModule.Types).SelectMany(t=>t.Methods))if(code[x.FullName]!=Body(x))throw new Exception("Code changed "+x.FullName);
foreach(var r in output.MainModule.Resources.OfType<EmbeddedResource>())if(hashes[r.Name]!=Convert.ToHexString(SHA256.HashData(r.GetResourceData())))throw new Exception("Asset changed "+r.Name);
var check=output.MainModule.GetType("WaterGardensKoi.Plugin");var tag=check.CustomAttributes.Single(x=>x.AttributeType.Name=="BepInPlugin");
if((string)tag.ConstructorArguments[0].Value!=guid||(string)tag.ConstructorArguments[2].Value!="1.0.0"||output.Name.Version!=new Version(1,0,0,0))throw new Exception("Metadata mismatch");
if(!deps.SequenceEqual(check.CustomAttributes.Where(x=>x.AttributeType.Name!="BepInPlugin").Select(x=>x.AttributeType.FullName+Convert.ToHexString(x.GetBlob()))))throw new Exception("Dependency/network attributes changed");
Console.WriteLine($"PASS 1.0.0: {code.Count} methods, {hashes.Count} assets, GUID {guid}, dependencies and network attributes preserved.");

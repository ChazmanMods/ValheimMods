using System.Text.Json;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

string root=Path.GetFullPath(args[0]);
var candidates=JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(File.ReadAllText(Path.Combine(root,"CANDIDATES.json")));
int checks=0;
void Check(bool pass,string label){checks++;if(!pass)throw new Exception(label);}
IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> types){foreach(var t in types){yield return t;foreach(var n in Types(t.NestedTypes))yield return n;}}
foreach(var entry in candidates)
{
    string module=entry.Key, assembly=entry.Value.GetProperty("assembly").GetString(),version=entry.Value.GetProperty("version").GetString();
    string folder=Path.Combine(root,module);
    var english=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(folder,"Translations",assembly,"English.json")));
    using var binary=AssemblyDefinition.ReadAssembly(Path.Combine(folder,"bin","Release","netstandard2.1",assembly+".dll"));
    var types=Types(binary.MainModule.Types).ToArray();
    var plugin=types.SelectMany(t=>t.CustomAttributes).Single(a=>a.AttributeType.FullName=="BepInEx.BepInPlugin");
    Check((string)plugin.ConstructorArguments[2].Value==version,module+" plugin version");
    Check(binary.Name.Version.ToString(3)==version,module+" assembly version");
    Check(!binary.MainModule.AssemblyReferences.Any(r=>r.Name.Contains("RunicAutomation")||r.Name=="Runic.Localization"),module+" no new localization dependency");
    var embedded=(EmbeddedResource)binary.MainModule.Resources.Single(r=>r.Name=="Runic.Localization.English.json");
    var fallback=JsonSerializer.Deserialize<Dictionary<string,string>>(embedded.GetResourceData());
    Check(english.Count==fallback.Count&&english.All(e=>fallback.GetValueOrDefault(e.Key)==e.Value),module+" shipped and embedded English match");
    foreach(var method in types.SelectMany(t=>t.Methods).Where(m=>m.HasBody))
    {
        foreach(var instruction in method.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ldstr))
            if(instruction.Operand is string key && Regex.IsMatch(key,@"^text_[a-f0-9]{12}$"))Check(english.ContainsKey(key),module+" missing key "+key);
        if(module=="RunicPortals" && method.Name=="CompletePicker")
        {
            Check(!method.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="ShowPointOnMap"),"delayed directory does not recenter");
            Check(!method.Body.Instructions.Any(i=>i.Operand is FieldReference f && f.Name.Contains("Zoom")),"delayed directory does not set zoom");
        }
        if(module=="RunicLilyPads" && method.DeclaringType.Name=="GardenPrefabs" && method.Name=="Root")
            Check(method.Body.Instructions.Any(i=>i.OpCode==OpCodes.Stfld && i.Operand is FieldReference f && f.Name=="m_usage" && i.Previous.OpCode==OpCodes.Ldc_I4_0),"all garden roots clear inherited usage tags");
    }
    foreach(var item in entry.Value.GetProperty("compile_files").EnumerateArray())
    {
        string path=Path.Combine(root,item.GetString());if(!File.Exists(path)||path.Contains("\\obj\\"))continue;
        var tree=CSharpSyntaxTree.ParseText(File.ReadAllText(path));var syntax=tree.GetRoot();
        foreach(var assignment in syntax.DescendantNodes().OfType<AssignmentExpressionSyntax>().Where(a=>a.Left.ToString()=="_commandLine"))
            Check(!assignment.Right.ToString().Contains("RunicText."),module+" executable commands are never translated");
        foreach(var call in syntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            string name=call.Expression.ToString();string simple=name.Split('.').Last().Split('<')[0];
            if(name.StartsWith("ZInput.") || name.StartsWith("Input."))
                Check(!call.ArgumentList.ToString().Contains("RunicText."),module+" input identifiers are never localized");
            if(name=="GardenLanguage.Token")
            {
                Check(call.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax token && english.ContainsKey(token.Token.ValueText),"native piece token has English entry");
                Check(call.Parent is not BinaryExpressionSyntax,"native piece token is not corrupted by a numeric suffix");
            }
            if(simple.StartsWith("Bind"))
            {
                var declaration=syntax.DescendantNodes().OfType<MethodDeclarationSyntax>().FirstOrDefault(m=>m.Identifier.ValueText==simple&&m.ParameterList.Parameters.Count==call.ArgumentList.Arguments.Count);
                for(int i=0;i<call.ArgumentList.Arguments.Count;i++)
                {
                    bool description=declaration!=null?declaration.ParameterList.Parameters[i].Identifier.ValueText=="description":i>=3;
                    if(!description)Check(!call.ArgumentList.Arguments[i].ToString().Contains("RunicText."),module+" config identity/default must be stable: "+call);
                }
            }
            if(name.EndsWith("RunicText.Format")&&call.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax key)
            {
                string template=english[key.Token.ValueText];
                foreach(Match match in Regex.Matches(template,@"(?<!\{)\{(\d+)(?:[,:}])"))
                    Check(int.Parse(match.Groups[1].Value)<call.ArgumentList.Arguments.Count-1,module+" format arguments "+key);
            }
        }
    }
    Console.WriteLine("PASS "+module+" "+version+" ("+english.Count+" English entries)");
}
Console.WriteLine("PASS "+checks+" binary/catalog/configuration checks");

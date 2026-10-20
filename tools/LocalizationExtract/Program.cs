using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

string root = Path.GetFullPath(args[0]);
var modules = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(Path.Combine(root,"SOURCE-BASELINE.json")));
var report = new List<object>();
foreach (string module in modules.Keys)
{
    string assembly = module == "RunicLilyPads" ? "ArcaneDecorWaterGardens" : module;
    string existing=Path.Combine(root,module,"Translations",assembly,"English.json");
    var entries = File.Exists(existing) ? JsonSerializer.Deserialize<SortedDictionary<string,string>>(File.ReadAllText(existing)) : new SortedDictionary<string,string>();
    var locations = new List<object>();
    foreach (string path in Directory.GetFiles(Path.Combine(root,module),"*.cs",SearchOption.AllDirectories))
    {
        string relative=Path.GetRelativePath(Path.Combine(root,module),path);
        if(relative.Split(Path.DirectorySeparatorChar).Any(p=>p is "Tests" or "bin" or "obj")) continue;
        var tree=CSharpSyntaxTree.ParseText(File.ReadAllText(path));
        var rewriter=new Extractor(relative,entries,locations);
        var changed=rewriter.Visit(tree.GetRoot());
        if(rewriter.Count>0) File.WriteAllText(path,changed.ToFullString(),new UTF8Encoding(false));
    }
    string dir=Path.Combine(root,module,"Translations",assembly);Directory.CreateDirectory(dir);
    File.WriteAllText(Path.Combine(dir,"English.json"),JsonSerializer.Serialize(entries,new JsonSerializerOptions{WriteIndented=true})+"\n");
    report.Add(new{module,entries=entries.Count,locations});
    Console.WriteLine(module+": "+entries.Count+" English entries");
}
File.WriteAllText(Path.Combine(root,"LOCALIZATION-EXTRACTION.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));

sealed class Extractor : CSharpSyntaxRewriter
{
    readonly string file; readonly SortedDictionary<string,string> entries; readonly List<object> locations;
    public int Count;
    public Extractor(string file,SortedDictionary<string,string> entries,List<object> locations){this.file=file;this.entries=entries;this.locations=locations;}
    bool NativePieceText(SyntaxNode node)
    {
        if(file is not ("GardenPrefabs.cs" or "GardenModels.cs" or "GardenFlora.cs")) return false;
        var variable=node.Ancestors().OfType<VariableDeclaratorSyntax>().FirstOrDefault();
        if(variable?.Identifier.ValueText=="names")return true;
        var assignment=node.Ancestors().OfType<AssignmentExpressionSyntax>().FirstOrDefault();
        if(assignment?.Left.ToString() is "Name" or "Description")return true;
        var argument=node.Ancestors().OfType<ArgumentSyntax>().FirstOrDefault();
        if(argument?.Parent is not ArgumentListSyntax list || list.Parent is not InvocationExpressionSyntax call)return false;
        var name=call.Expression.ToString().Split('.').Last(); int i=list.Arguments.IndexOf(argument);
        return ((name is "Decor" or "Plant") && i==1) || (name=="Static" && i==2) || ((name is "Root" or "Register") && (i==1||i==2));
    }
    bool IsDisplay(SyntaxNode node)
    {
        if(file.EndsWith("SentinelPlayerReports.cs") || file.EndsWith("SentinelContentCatalog.cs") || file.EndsWith("SentinelModChoices.cs")) return false;
        if(node.Ancestors().OfType<AssignmentExpressionSyntax>().Any(a=>a.Left.ToString()=="_commandLine")) return false;
        if(NativePieceText(node))return true;
        var property=node.Ancestors().OfType<PropertyDeclarationSyntax>().FirstOrDefault();
        if(property?.Identifier.ValueText is "RoleLabels" or "Titles" || (file=="Transfers\\ArmorStandSlots.cs" && property?.Identifier.ValueText=="Labels"))return true;
        var enclosing=node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if(Regex.IsMatch(file,@"DraftExporter|AttestationPolicy|NetworkMapWriter|OperatorCommands") ||
           Regex.IsMatch(enclosing?.Identifier.ValueText??"",@"Canonical|Serialize|Encode|WritePolicy"))return false;
        if(node.Ancestors().Any(n=>n is AttributeSyntax or CaseSwitchLabelSyntax or ConstantPatternSyntax)) return false;
        var field=node.Ancestors().OfType<FieldDeclarationSyntax>().FirstOrDefault();
        if(field!=null) return false; // persistent keys and static arrays are handled explicitly.
        var local=node.Ancestors().OfType<LocalDeclarationStatementSyntax>().FirstOrDefault();
        if(local?.Modifiers.Any(SyntaxKind.ConstKeyword)==true)return false;
        foreach(var parent in node.Ancestors().TakeWhile(n=>n is not AnonymousFunctionExpressionSyntax && n is not BaseMethodDeclarationSyntax))
        {
            if(parent is InvocationExpressionSyntax call)
            {
                string name=call.Expression.ToString();
                if(name.StartsWith("ZInput.") || name.StartsWith("Input."))return false;
                string simple=name.Split('.').Last().Split('<')[0];
                var arg=node.Ancestors().OfType<ArgumentSyntax>().FirstOrDefault(a=>a.Parent==call.ArgumentList);
                int index=arg==null?-1:call.ArgumentList.Arguments.IndexOf(arg);
                var declaration=node.SyntaxTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
                    .FirstOrDefault(m=>m.Identifier.ValueText==simple && m.ParameterList.Parameters.Count==call.ArgumentList.Arguments.Count);
                string parameter=declaration!=null && index>=0 ? declaration.ParameterList.Parameters[index].Identifier.ValueText : "";
                if(Regex.IsMatch(parameter,@"^(name|id|key|section|defaultValue|path|source|asset)$"))return false;
                if(simple.StartsWith("Bind") && declaration!=null)return parameter=="description";
                if(parameter is "caption" or "description" or "label" or "tooltip" or "hint" or "friendly")return true;
                if((simple is "Fail" or "Error" or "Invalid") && node.ToString().Contains(' '))return true;
                if(name=="_panels.Set")return true;
                if(Regex.IsMatch(name,@"Log|Debug\.|Diagnostics\.|AccessTools\.|GetMethod|GetField|InvokeRPC|\.SetString$|\.GetString$|\.Equals$|\.Contains$|\.StartsWith$|\.EndsWith$|\.Replace$|\.IndexOf$|\.Split$|\.TryGetValue$|\.GetComponent|\.Find$|\.AddComponent"))return false;
                if(name.EndsWith(".Bind") || name=="Bind")
                {
                    var argument=node.Ancestors().OfType<ArgumentSyntax>().FirstOrDefault(a=>a.Parent==call.ArgumentList);
                    return argument!=null && call.ArgumentList.Arguments.IndexOf(argument)>=3;
                }
            }
            if(parent is ObjectCreationExpressionSyntax creation && Regex.IsMatch(creation.Type.ToString(),@"GameObject|Exception|Regex|ConfigDefinition|Harmony|KeyboardShortcut|ConfigEntry|Dictionary"))return false;
            if(parent is BinaryExpressionSyntax compare && (compare.IsKind(SyntaxKind.EqualsExpression)||compare.IsKind(SyntaxKind.NotEqualsExpression)))return false;
            if(parent is ElementAccessExpressionSyntax)return false;
        }
        var method=node.Ancestors().OfType<BaseMethodDeclarationSyntax>().FirstOrDefault();
        string methodName=(method as MethodDeclarationSyntax)?.Identifier.ValueText??"";
        var assignment=node.Ancestors().OfType<AssignmentExpressionSyntax>().FirstOrDefault();
        string assigned=assignment?.Left.ToString()??"";
        if(Regex.IsMatch(assigned,@"\.text$|__result$|[Tt]ooltip|[Hh]int|[Mm]essage|[Dd]etail|[Ss]tatus|[Ee]xplanation|[Ll]abel|[Ff]ailure|[Ee]rror|[Tt]itle|[Hh]elp|^[Rr]eason$"))return true;
        if(assigned.EndsWith(".name") || assigned=="name")return false;
        foreach(var call in node.Ancestors().OfType<InvocationExpressionSyntax>())
            if(Regex.IsMatch(call.Expression.ToString(),@"(^GUI\.|^GUILayout\.|Message$|ShowMessage$|SetTooltip$|SetText$|Queue.*Notice|CancelMapPicker$|Notify$|Notice$|Feedback$|Friendly$|Reject$|Label$|Button$|Toggle$|Help$|Hint$|Tooltip$|DrawRow$|Text$|\.Append$|\.AppendLine$)"))return true;
        if(Regex.IsMatch(file,@"Panel|Editor|View\.cs|Hover|Hud|Tooltip|Hint|Help|Formatter|SetupGuide|Popup|ErrorMessages|Clock/|Clock\\") || Regex.IsMatch(methodName,@"Hover|Tooltip|Hint|Display|Describe|Draw|OnGUI|StatusText|ReasonText|FormatStatus|Explain|Help|Message|Label|Description|BuildStatus|ControlSummary|PreviewText|PickupPreview|Friendly|Capture"))return true;
        if(method is MethodDeclarationSyntax m && m.ReturnType.ToString()=="string" &&
           !Regex.IsMatch(methodName,@"Id$|Key|Path|Serialize|Encode|Token|Prefab|Hash|Command|Normalize|Sanitize") &&
           node.Ancestors().Any(n=>n is ReturnStatementSyntax or ArrowExpressionClauseSyntax))return true;
        return false;
    }
    static bool Human(string text)
    {
        if(text.Contains('"') || Regex.IsMatch(text,@"(^|\n)[a-z-]+=") || text.StartsWith("RUNIC-") || text.Contains(":\\"))return false;
        if(string.IsNullOrWhiteSpace(text)||!text.Any(char.IsLetter)||text.StartsWith("$")||text.StartsWith("http"))return false;
        if(Regex.IsMatch(text,@"^(?:[a-z0-9_]+[.:/_-])+[a-zA-Z0-9_./:-]*$"))return false;
        if(Regex.IsMatch(text,@"^(?:[A-Za-z0-9_]+\.)+[A-Za-z0-9_]+$"))return false;
        if(text.Contains("<") && !Regex.IsMatch(text,@"[A-Za-z]{2,}\s+[A-Za-z]"))return false;
        return text.Any(char.IsWhiteSpace)||Regex.IsMatch(text,@"^[A-Z][a-z]+(?:[ !?:…]*)$")||text is "ON" or "OFF";
    }
    string Key(string value)
    {
        string key="text_"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).Substring(0,12).ToLowerInvariant();
        entries[key]=value;return key;
    }
    SyntaxNode Replace(ExpressionSyntax node,string template,List<string> args)
    {
        string key=Key(template);Count++;
        locations.Add(new{file,line=node.GetLocation().GetLineSpan().StartLinePosition.Line+1,key,english=template});
        string expression=NativePieceText(node)?$"GardenLanguage.Token(\"{key}\")":args.Count==0?$"global::Runic.Localization.RunicText.Get(\"{key}\")":$"global::Runic.Localization.RunicText.Format(\"{key}\", {string.Join(", ",args)})";
        return SyntaxFactory.ParseExpression(expression).WithTriviaFrom(node);
    }
    public override SyntaxNode VisitLiteralExpression(LiteralExpressionSyntax node)
    {
        if(node.IsKind(SyntaxKind.StringLiteralExpression)&&Human(node.Token.ValueText)&&IsDisplay(node))return Replace(node,node.Token.ValueText,new());
        return base.VisitLiteralExpression(node);
    }
    public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        if(node.Expression.ToString()=="global::Runic.Localization.RunicText.Get" && node.ArgumentList.Arguments.Count==1 &&
           node.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax key && entries.TryGetValue(key.Token.ValueText,out var english) && (!IsDisplay(node) || !Human(english)))
        { Count++; return SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression,SyntaxFactory.Literal(english)).WithTriviaFrom(node); }
        return base.VisitInvocationExpression(node);
    }
    public override SyntaxNode VisitInterpolatedStringExpression(InterpolatedStringExpressionSyntax node)
    {
        if(!IsDisplay(node)||!node.Contents.OfType<InterpolatedStringTextSyntax>().Any(n=>Human(n.TextToken.ValueText)))return base.VisitInterpolatedStringExpression(node);
        var sb=new StringBuilder();var args=new List<string>();
        foreach(var content in node.Contents)
        {
            if(content is InterpolatedStringTextSyntax text)sb.Append(text.TextToken.ValueText);
            else if(content is InterpolationSyntax i)
            {
                sb.Append('{').Append(args.Count);
                if(i.AlignmentClause!=null)sb.Append(',').Append(i.AlignmentClause.Value.ToString());
                if(i.FormatClause!=null)sb.Append(':').Append(i.FormatClause.FormatStringToken.ValueText);
                sb.Append('}');args.Add(i.Expression.ToString());
            }
        }
        return Replace(node,sb.ToString(),args);
    }
}

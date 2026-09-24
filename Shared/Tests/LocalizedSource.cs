using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Runic.Tests
{
    // Source contract tests inspect resolved English UI literals after extraction,
    // while continuing to inspect the real control flow and localization key references.
    internal static class LocalizedSource
    {
        internal static string ReadAllText(string path)
        {
            string text=File.ReadAllText(path);
            if(!path.EndsWith(".cs",StringComparison.OrdinalIgnoreCase))return text;
            var folder=new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path)));
            while(folder!=null && !Directory.Exists(Path.Combine(folder.FullName,"Translations")))folder=folder.Parent;
            if(folder==null)return text;
            string catalog=Directory.GetFiles(Path.Combine(folder.FullName,"Translations"),"English.json",SearchOption.AllDirectories).Single();
            var entries=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(catalog));
            return Regex.Replace(text,"global::Runic\\.Localization\\.RunicText\\.Get\\(\"([^\"]+)\"\\)",m=>
                JsonSerializer.Serialize(entries[m.Groups[1].Value],new JsonSerializerOptions{Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
        }
    }
}

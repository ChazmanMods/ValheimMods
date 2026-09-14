using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Local = RunicSentinel.Contracts;

namespace RunicSentinel.Runtime
{
    internal static class SentinelDraftExporter
    {
        internal const string FileName = "RunicSentinel.current-profile.json";

        internal static void TryWrite(
            string configRoot,
            Local.AttestationSnapshot snapshot)
        {
            try
            {
                if (snapshot == null || string.IsNullOrEmpty(configRoot)) return;
                string path = Path.Combine(Path.GetFullPath(configRoot), FileName);
                string temporary = path + ".tmp";
                byte[] bytes = new UTF8Encoding(false).GetBytes(Build(snapshot));
                using (var stream = new FileStream(
                           temporary,
                           FileMode.Create,
                           FileAccess.Write,
                           FileShare.None,
                           65536,
                           FileOptions.WriteThrough))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            catch
            {
                // Export is operator convenience only and never changes the admission decision.
            }
        }

        private static string Build(
            Local.AttestationSnapshot snapshot)
        {
            var builder = new StringBuilder(4096 + snapshot.Plugins.Count * 160);
            builder.Append("{\n  \"profile\": \"runic-suite\",\n  \"sequence\": 1,\n  \"issued\": ")
                .Append(DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture))
                .Append(",\n  \"expires\": 0,\n  \"unknownMods\": \"Forbidden\",\n")
                .Append("  \"requiredMods\": [\n");
            for (int index = 0; index < snapshot.Plugins.Count; index++)
            {
                Local.AttestedPlugin plugin = snapshot.Plugins[index];
                builder.Append("    { \"id\": \"").Append(Json(plugin.Id))
                    .Append("\", \"version\": \"").Append(Json(plugin.Version))
                    .Append("\", \"sha256\": \"").Append(plugin.Sha256).Append("\" }")
                    .Append(index + 1 == snapshot.Plugins.Count ? "\n" : ",\n");
            }
            builder.Append("  ],\n  \"optionalMods\": [],\n  \"grayListMods\": [],\n")
                .Append("  \"forbiddenMods\": [],\n  \"modules\": [],\n")
                .Append("  \"administrators\": [],\n  \"bannedUsers\": []\n}\n");
            return builder.ToString();
        }

        private static string Json(string value)
        {
            var builder = new StringBuilder(value?.Length ?? 0);
            foreach (char character in value ?? string.Empty)
            {
                switch (character)
                {
                    case '\\': builder.Append("\\\\"); break;
                    case '"': builder.Append("\\\""); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (character < 0x20)
                            builder.Append("\\u").Append(((int)character).ToString("x4"));
                        else builder.Append(character);
                        break;
                }
            }
            return builder.ToString();
        }
    }
}

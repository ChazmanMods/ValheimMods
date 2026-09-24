using System;
using System.IO;
using System.Reflection;

namespace Runic.Localization
{
    // Compiled into each mod; no separate localization plugin or runtime assembly.
    internal static class RunicText
    {
        private static readonly Assembly Owner = typeof(RunicText).Assembly;
        private static readonly string Module = Owner.GetName().Name;
        private static readonly LanguageCatalog Catalog = Create();
        private static LanguageCatalog Create()
        {
            using (var stream = Owner.GetManifestResourceStream("Runic.Localization.English.json"))
            using (var reader = stream == null ? null : new StreamReader(stream))
                return new LanguageCatalog(reader?.ReadToEnd() ?? "{}", ReadLanguage);
        }
        private static string ReadLanguage(string language)
        {
            string directory = Path.Combine(Path.GetDirectoryName(Owner.Location), "Translations", Module);
            string path = Path.Combine(directory, language + ".json");
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        private static void SelectLanguage()
        {
            string language = "English";
            try { language = global::Localization.instance?.GetSelectedLanguage() ?? "English"; }
            catch { /* English remains available during early startup and on servers. */ }
            Catalog.Select(language);
        }
        internal static string Get(string key) { SelectLanguage(); return Catalog.Get(key); }
        internal static string English(string key) => Catalog.English(key);
        // Only for fixed UI labels also used as stable helper identifiers; never for player text.
        internal static string TranslateEnglish(string text) { SelectLanguage(); return Catalog.TranslateEnglish(text); }
        internal static string Format(string key, params object[] args) { SelectLanguage(); return Catalog.Format(key, args); }
    }
}

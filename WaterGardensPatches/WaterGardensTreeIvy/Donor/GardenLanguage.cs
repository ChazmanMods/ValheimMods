using System;
using System.IO;
using Jotunn.Managers;
using Runic.Localization;

namespace RunicLilyPads
{
    internal static class GardenLanguage
    {
        internal static string Token(string key) => "$arcanedecor_watergardens_" + key;

        internal static void Load()
        {
            var owner = typeof(Plugin).Assembly;
            string english;
            using (var stream = owner.GetManifestResourceStream("Runic.Localization.English.json"))
            using (var reader = new StreamReader(stream)) english = reader.ReadToEnd();
            var entries = LanguageCatalog.Parse(english);
            var directory = Path.Combine(Path.GetDirectoryName(owner.Location), "Translations", owner.GetName().Name);
            var catalog = new LanguageCatalog(english, language => File.ReadAllText(Path.Combine(directory, language + ".json")));
            var localization = LocalizationManager.Instance.GetLocalization();
            void Add(string language)
            {
                catalog.Select(language);
                foreach (var entry in entries)
                    localization.AddTranslation(language, Token(entry.Key).Substring(1), catalog.Get(entry.Key));
                localization.AddTranslation(language, "jotunn_cat_water_garden", catalog.Get("menu_water_garden"));
            }
            Add("English");
            if (Directory.Exists(directory))
                foreach (var path in Directory.GetFiles(directory, "*.json")) Add(Path.GetFileNameWithoutExtension(path));
        }
    }
}

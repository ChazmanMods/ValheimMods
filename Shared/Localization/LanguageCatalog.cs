using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Runic.Localization
{
    // No game dependency: the caller supplies Valheim's selected language.
    internal sealed class LanguageCatalog
    {
        private readonly Dictionary<string, string> _english;
        private Dictionary<string, string> _selected = new Dictionary<string, string>();
        private readonly Func<string, string> _read;
        private string _language;
        internal LanguageCatalog(string english, Func<string, string> read)
        {
            _english = Parse(english);
            _read = read;
        }
        internal static Dictionary<string, string> Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>();
            if (json.Length > 2 * 1024 * 1024) throw new InvalidDataException("Language file exceeds 2 MiB.");
            var serializer = new DataContractJsonSerializer(typeof(Dictionary<string, string>),
                new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json.TrimStart('\uFEFF'))))
                return (Dictionary<string, string>)serializer.ReadObject(stream) ?? new Dictionary<string, string>();
        }
        internal void Select(string language)
        {
            language = string.IsNullOrWhiteSpace(language) ? "English" : language;
            if (string.Equals(language, _language, StringComparison.Ordinal)) return;
            _language = language;
            _selected = new Dictionary<string, string>();
            if (language.IndexOfAny(new[] { '/', '\\', ':', '\0' }) >= 0 || language == "." || language == "..") return;
            try { _selected = Parse(_read(language)); }
            catch (Exception) { /* A missing/broken optional translation must never disable gameplay. */ }
        }
        internal string Get(string key)
        {
            _english.TryGetValue(key, out string english);
            if (_selected.TryGetValue(key, out string translated) && !string.IsNullOrWhiteSpace(translated) &&
                (english == null || SameFields(english, translated))) return translated;
            return english ?? key;
        }
        internal string English(string key) => _english.TryGetValue(key, out var value) ? value : key;
        internal string TranslateEnglish(string text)
        {
            foreach (var entry in _english) if (entry.Value == text) return Get(entry.Key);
            return text;
        }
        internal string Format(string key, params object[] args)
        {
            _english.TryGetValue(key, out string english);
            string translated = Get(key);
            // A translation may reorder placeholders, but must not lose or invent them.
            if (english != null && !SameFields(english, translated)) translated = english;
            try { return string.Format(CultureInfo.CurrentCulture, translated, args); }
            catch (FormatException)
            {
                try { return string.Format(CultureInfo.CurrentCulture, english ?? key, args); }
                catch (FormatException) { return english ?? key; }
            }
        }
        private static bool SameFields(string a, string b)
        {
            try { return Fields(a).SetEquals(Fields(b)); }
            catch (FormatException) { return false; }
        }
        private static HashSet<int> Fields(string text)
        {
            var result = new HashSet<int>();
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '}')
                {
                    if (i + 1 < text.Length && text[i + 1] == '}') { i++; continue; }
                    throw new FormatException();
                }
                if (text[i] != '{') continue;
                if (i + 1 < text.Length && text[i + 1] == '{') { i++; continue; }
                int start = ++i;
                while (i < text.Length && char.IsDigit(text[i])) i++;
                if (start == i || !int.TryParse(text.Substring(start, i - start), out int index)) throw new FormatException();
                result.Add(index);
                while (i < text.Length && text[i] != '}')
                {
                    if (text[i] == '{') throw new FormatException();
                    i++;
                }
                if (i == text.Length) throw new FormatException();
            }
            return result;
        }
    }
}

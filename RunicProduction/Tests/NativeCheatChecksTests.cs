using System;
using System.Collections.Generic;
using RunicProduction.Core;

namespace RunicProduction.Tests
{
    internal static class NativeCheatChecksTests
    {
        internal static IReadOnlyList<KeyValuePair<string, Action>> Cases() =>
            new[]
            {
                new KeyValuePair<string, Action>("native cheat flag supports legacy field without caching its value", Field),
                new KeyValuePair<string, Action>("native cheat flag supports computed property without caching its value", Property),
                new KeyValuePair<string, Action>("native cheat flag rejects missing or wrongly typed contracts", Invalid),
                new KeyValuePair<string, Action>("installed PlayerProfile cheat getter binds successfully", Installed)
            };

        private static void Field()
        {
            Func<bool> read = NativeCheatChecks.CreateReader(typeof(Legacy));
            Legacy.s_bypassCheatChecks = false;
            if (read()) throw new Exception("Expected native false.");
            Legacy.s_bypassCheatChecks = true;
            if (!read()) throw new Exception("Native field value was cached.");
        }

        private static void Property()
        {
            Func<bool> read = NativeCheatChecks.CreateReader(typeof(Current));
            Current.Value = false;
            if (read()) throw new Exception("Expected native false.");
            Current.Value = true;
            if (!read()) throw new Exception("Native property value was cached.");
        }

        private static void Invalid()
        {
            foreach (Type type in new[] { typeof(string), typeof(Wrong), null })
            {
                try { NativeCheatChecks.CreateReader(type); }
                catch (MissingMemberException) { continue; }
                throw new Exception("Invalid native contract accepted.");
            }
        }

        private static void Installed()
        {
            if (NativeCheatChecks.CreateReader(typeof(PlayerProfile)) == null)
                throw new Exception("Installed native cheat contract did not bind.");
        }

        public static class Legacy { public static bool s_bypassCheatChecks; }
        public static class Current
        {
            public static bool Value;
            public static bool s_bypassCheatChecks => Value;
        }
        public static class Wrong { public static string s_bypassCheatChecks => "true"; }
    }
}

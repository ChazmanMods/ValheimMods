using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace RunicSafety.Integration
{
    internal static class LocalizationBridge
    {
        private static readonly MethodInfo AddWord = typeof(Localization).GetMethod(
            "AddWord",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(string), typeof(string) },
            null);

        private static readonly IReadOnlyDictionary<string, string> English =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["runicsafety_confirm_container"] = "Runic Safety: repeat the same removal to destroy this occupied container.",
                ["runicsafety_confirm_vehicle"] = "Runic Safety: repeat the same removal to destroy this ship or cart.",
                ["runicsafety_confirm_portal"] = "Runic Safety: submit the same tag again to overwrite this portal.",
                ["runicsafety_confirm_rare"] = "Runic Safety: repeat the same action to use the protected rare item.",
                ["runicsafety_protected_denied"] = "Runic Safety blocked a protected item from this destination.",
                ["runicsafety_provider_missing"] = "Runic Safety blocked the action because the installed inventory protection provider is unavailable.",
                ["runicsafety_recovery_unsafe"] = "Runic Safety could not verify expanded death recovery; see the correlated log before migrating topology."
            };

        internal static bool Validate(out string problem)
        {
            if (AddWord == null)
            {
                problem = "Localization.AddWord(string,string) is missing.";
                return false;
            }
            problem = string.Empty;
            return true;
        }

        internal static void Install(Localization localization)
        {
            if (localization == null || AddWord == null) return;
            foreach (KeyValuePair<string, string> word in English)
                AddWord.Invoke(localization, new object[] { word.Key, word.Value });
        }
    }

    [HarmonyPatch(typeof(Localization), nameof(Localization.SetupLanguage), new[] { typeof(string) })]
    internal static class LocalizationSetupLanguagePatch
    {
        [HarmonyPostfix]
        private static void Postfix(Localization __instance) => LocalizationBridge.Install(__instance);
    }
}

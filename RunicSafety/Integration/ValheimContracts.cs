using System;
using System.Reflection;
using HarmonyLib;

namespace RunicSafety.Integration
{
    internal static class ValheimContracts
    {
        internal const string AuditedGameVersion = "1.0.12";
        internal static bool IsSupportedVersion(string version) =>
            string.Equals(version, AuditedGameVersion, StringComparison.Ordinal) ||
            string.Equals(version, "1.0.7", StringComparison.Ordinal);
        private const BindingFlags AllMethods = BindingFlags.Public | BindingFlags.NonPublic |
                                                BindingFlags.Instance | BindingFlags.Static;

        internal static readonly FieldInfo IncineratorNetView = typeof(Incinerator).GetField(
            "m_nview", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        internal static bool Initialize(out string problem)
        {
            if (!Require(typeof(Player), "RemovePiece", Type.EmptyTypes, out problem) ||
                !Require(typeof(Player), nameof(Player.CreateTombStone), Type.EmptyTypes, out problem) ||
                !Require(typeof(TeleportWorld), nameof(TeleportWorld.SetText), new[] { typeof(string) }, out problem) ||
                !Require(typeof(Incinerator), "OnIncinerate",
                    new[] { typeof(Switch), typeof(Humanoid), typeof(ItemDrop.ItemData) }, out problem) ||
                !Require(typeof(Incinerator), "RPC_RequestIncinerate",
                    new[] { typeof(long), typeof(long) }, out problem) ||
                !Require(typeof(Smelter), "OnAddOre",
                    new[] { typeof(Switch), typeof(Humanoid), typeof(ItemDrop.ItemData) }, out problem) ||
                !Require(typeof(Smelter), "OnAddFuel",
                    new[] { typeof(Switch), typeof(Humanoid), typeof(ItemDrop.ItemData) }, out problem) ||
                !Require(typeof(CookingStation), "OnAddFuelSwitch",
                    new[] { typeof(Switch), typeof(Humanoid), typeof(ItemDrop.ItemData) }, out problem) ||
                !Require(typeof(CookingStation), "OnUseItem",
                    new[] { typeof(Humanoid), typeof(ItemDrop.ItemData) }, out problem) ||
                !Require(typeof(Fermenter), "AddItem",
                    new[] { typeof(Humanoid), typeof(ItemDrop.ItemData) }, out problem) ||
                !Require(typeof(ItemStand), nameof(ItemStand.UseItem),
                    new[] { typeof(Humanoid), typeof(ItemDrop.ItemData) }, out problem) ||
                !Require(typeof(Localization), nameof(Localization.SetupLanguage),
                    new[] { typeof(string) }, out problem)) return false;
            if (IncineratorNetView == null || IncineratorNetView.FieldType != typeof(ZNetView))
            {
                problem = "Incinerator.m_nview:ZNetView is missing.";
                return false;
            }
            if (!LocalizationBridge.Validate(out problem)) return false;
            string version = ReadGameVersion();
            if (!IsSupportedVersion(version))
            {
                problem = "Runic Safety was audited for Valheim 1.0.7 or " + AuditedGameVersion +
                          " but the loaded assembly reports " + (version.Length == 0 ? "unknown" : version) + ".";
                return false;
            }
            problem = string.Empty;
            return true;
        }

        internal static string ReadGameVersion()
        {
            try
            {
                Type versionType = typeof(Player).Assembly.GetType("Version", false);
                PropertyInfo property = versionType?.GetProperty("CurrentVersion",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                object value = property?.GetValue(null, null);
                return value?.ToString() ?? string.Empty;
            }
            catch (Exception) { return string.Empty; }
        }

        internal static void SendIncineratorFailure(Incinerator incinerator, long receiver)
        {
            try
            {
                var view = IncineratorNetView?.GetValue(incinerator) as ZNetView;
                if (view != null && view.IsValid())
                    view.InvokeRPC(receiver, "RPC_IncinerateRespons", 0);
            }
            catch (Exception) { }
        }

        private static bool Require(Type type, string name, Type[] parameters, out string problem)
        {
            MethodInfo method = type.GetMethod(name, AllMethods, null, parameters, null);
            if (method == null)
            {
                problem = type.FullName + "." + name + "(" + string.Join(",", (object[])parameters) + ") is missing.";
                return false;
            }
            problem = string.Empty;
            return true;
        }
    }
}

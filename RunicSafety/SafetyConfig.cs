using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using RunicSafety.Api;

namespace RunicSafety
{
    internal static class SafetyConfig
    {
        private const string DefaultRarePrefabs =
            "DragonEgg,DvergrKey,DvergrKeyFragment,QueenDrop,Sealbreaker,Wishbone,TrophyTheQueen,TrophySeekerQueen";
        private static readonly object Sync = new object();
        private static HashSet<string> _rarePrefabs = new HashSet<string>(StringComparer.Ordinal);
        private static ConfigFile _config;

        internal static event Action Changed;

        internal static ConfigEntry<bool> Enabled { get; private set; }
        internal static ConfigEntry<bool> ConfirmRareSacrifice { get; private set; }
        internal static ConfigEntry<bool> ConfirmOccupiedContainer { get; private set; }
        internal static ConfigEntry<bool> ConfirmVehicleDestruction { get; private set; }
        internal static ConfigEntry<bool> ConfirmPortalOverwrite { get; private set; }
        internal static ConfigEntry<float> ConfirmationWindowSeconds { get; private set; }
        internal static ConfigEntry<bool> ProtectedDestinations { get; private set; }
        internal static ConfigEntry<bool> AdministratorBypass { get; private set; }
        internal static ConfigEntry<string> RarePrefabNames { get; private set; }
        internal static ConfigEntry<string> BackupRoot { get; private set; }
        internal static ConfigEntry<int> BackupRetention { get; private set; }
        internal static ConfigEntry<int> BackupMaximumFiles { get; private set; }
        internal static ConfigEntry<int> BackupMaximumMiB { get; private set; }

        internal static void Bind(ConfigFile config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            Unbind();
            _config = config;
            Enabled = Bind(config, "General", "Enabled", true,
                global::Runic.Localization.RunicText.Get("text_6d6dc4701ff3"));
            ConfirmRareSacrifice = Bind(config, "Confirmations", "RareItemSacrifice", true,
                global::Runic.Localization.RunicText.Get("text_db8fc97fcad8"));
            ConfirmOccupiedContainer = Bind(config, "Confirmations", "OccupiedContainerDestruction", true,
                global::Runic.Localization.RunicText.Get("text_dcaeba799395"));
            ConfirmVehicleDestruction = Bind(config, "Confirmations", "ShipOrCartDestruction", true,
                global::Runic.Localization.RunicText.Get("text_63c9d8a90511"));
            ConfirmPortalOverwrite = Bind(config, "Confirmations", "PortalOverwrite", true,
                global::Runic.Localization.RunicText.Get("text_ff3cbd217b26"));
            ConfirmationWindowSeconds = Bind(config, "Confirmations", "RepeatWindowSeconds", 4f,
                new ConfigDescription(global::Runic.Localization.RunicText.Get("text_a70672868f2a"),
                    new AcceptableValueRange<float>(1f, 15f)));
            ProtectedDestinations = Bind(config, "Protected Items", "Enabled", true,
                global::Runic.Localization.RunicText.Get("text_5760922ac7e7"));
            AdministratorBypass = Bind(config, "Protected Items", "AdministratorBypass", false,
                global::Runic.Localization.RunicText.Get("text_e0ae37ad90b0"));
            RarePrefabNames = Bind(config, "Protected Items", "RarePrefabNames", DefaultRarePrefabs,
                global::Runic.Localization.RunicText.Get("text_bd4cbf5571a4"));
            BackupRoot = Bind(config, "Migration Backups", "RootDirectory",
                Path.Combine(Paths.ConfigPath, "RunicSafety", "backups"),
                global::Runic.Localization.RunicText.Get("text_b26307eac06d"));
            BackupRetention = Bind(config, "Migration Backups", "RetentionCount", 5,
                new ConfigDescription(global::Runic.Localization.RunicText.Get("text_c3e2c13bb9da"),
                    new AcceptableValueRange<int>(1, 128)));
            BackupMaximumFiles = Bind(config, "Migration Backups", "MaximumFiles", 32,
                new ConfigDescription(global::Runic.Localization.RunicText.Get("text_65a8f954ff45"),
                    new AcceptableValueRange<int>(1, 1024)));
            BackupMaximumMiB = Bind(config, "Migration Backups", "MaximumTotalMiB", 2048,
                new ConfigDescription(global::Runic.Localization.RunicText.Get("text_8611e5a3c6d4"),
                    new AcceptableValueRange<int>(1, 65536)));
            RebuildRarePrefabs();
            config.SettingChanged += OnSettingChanged;
        }

        internal static void Unbind()
        {
            if (_config != null) _config.SettingChanged -= OnSettingChanged;
            _config = null;
            lock (Sync) _rarePrefabs = new HashSet<string>(StringComparer.Ordinal);
        }

        internal static bool IsRarePrefab(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return false;
            lock (Sync) return _rarePrefabs.Contains(prefabName);
        }

        internal static TimeSpan ConfirmationWindow =>
            TimeSpan.FromSeconds(ConfirmationWindowSeconds?.Value ?? 4f);

        internal static string SynchronizedRulesHash()
        {
            string[] rare;
            lock (Sync) rare = new List<string>(_rarePrefabs)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            string canonical = string.Join("|", new[]
            {
                (Enabled?.Value ?? true).ToString(CultureInfo.InvariantCulture),
                (ConfirmRareSacrifice?.Value ?? true).ToString(CultureInfo.InvariantCulture),
                (ConfirmOccupiedContainer?.Value ?? true).ToString(CultureInfo.InvariantCulture),
                (ConfirmVehicleDestruction?.Value ?? true).ToString(CultureInfo.InvariantCulture),
                (ConfirmPortalOverwrite?.Value ?? true).ToString(CultureInfo.InvariantCulture),
                (ConfirmationWindowSeconds?.Value ?? 4f).ToString("R", CultureInfo.InvariantCulture),
                (ProtectedDestinations?.Value ?? true).ToString(CultureInfo.InvariantCulture),
                (AdministratorBypass?.Value ?? false).ToString(CultureInfo.InvariantCulture),
                string.Join(",", rare)
            });
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical));
                var builder = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash) builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }

        private static ConfigEntry<T> Bind<T>(
            ConfigFile config,
            string section,
            string key,
            T value,
            string description) =>
            Bind(config, section, key, value, new ConfigDescription(description));

        private static ConfigEntry<T> Bind<T>(
            ConfigFile config,
            string section,
            string key,
            T value,
            ConfigDescription description)
        {
            ConfigEntry<T> entry = config.Bind(section, key, value, description);
            return entry;
        }

        private static void OnSettingChanged(object sender, EventArgs eventArgs)
        {
            RebuildRarePrefabs();
            Changed?.Invoke();
        }

        private static void RebuildRarePrefabs()
        {
            string raw = RarePrefabNames?.Value ?? DefaultRarePrefabs;
            var parsed = new HashSet<string>(StringComparer.Ordinal);
            string[] entries = raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int index = 0; index < entries.Length && parsed.Count < 256; index++)
            {
                string value = entries[index].Trim();
                if (value.Length != 0 && value.Length <= 96 && IsSafePrefabName(value)) parsed.Add(value);
            }
            lock (Sync) _rarePrefabs = parsed;
        }

        private static bool IsSafePrefabName(string value)
        {
            foreach (char character in value)
                if (!char.IsLetterOrDigit(character) && character != '_' && character != '-') return false;
            return true;
        }
    }
}

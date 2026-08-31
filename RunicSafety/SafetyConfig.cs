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
                "Master runtime gate. False preserves vanilla behavior while public audit/backup services remain discoverable.");
            ConfirmRareSacrifice = Bind(config, "Confirmations", "RareItemSacrifice", true,
                "Require the same rare-item sacrifice action twice inside the confirmation window.");
            ConfirmOccupiedContainer = Bind(config, "Confirmations", "OccupiedContainerDestruction", true,
                "Require a second hammer removal for a piece containing a non-empty container.");
            ConfirmVehicleDestruction = Bind(config, "Confirmations", "ShipOrCartDestruction", true,
                "Require a second hammer removal for a ship or cart piece.");
            ConfirmPortalOverwrite = Bind(config, "Confirmations", "PortalOverwrite", true,
                "Require a second commit when replacing an existing portal tag with a different tag.");
            ConfirmationWindowSeconds = Bind(config, "Confirmations", "RepeatWindowSeconds", 4f,
                new ConfigDescription("Seconds allowed for the identical repeat action.",
                    new AcceptableValueRange<float>(1f, 15f)));
            ProtectedDestinations = Bind(config, "Protected Items", "Enabled", true,
                "Apply equipped, quest, lock-provider, and configured-rare policy at audited vanilla destination boundaries.");
            AdministratorBypass = Bind(config, "Protected Items", "AdministratorBypass", false,
                "Allow a verified host/admin to bypass protected-item policy. Every bypass is recorded without item contents.");
            RarePrefabNames = Bind(config, "Protected Items", "RarePrefabNames", DefaultRarePrefabs,
                "Comma/semicolon separated exact prefab names. At most 256 bounded entries are accepted.");
            BackupRoot = Bind(config, "Migration Backups", "RootDirectory",
                Path.Combine(Paths.ConfigPath, "RunicSafety", "backups"),
                "Default same-volume root offered to migration clients. Safety only removes marked backups within this root.");
            BackupRetention = Bind(config, "Migration Backups", "RetentionCount", 5,
                new ConfigDescription("Committed backups retained after a successful new commit.",
                    new AcceptableValueRange<int>(1, 128)));
            BackupMaximumFiles = Bind(config, "Migration Backups", "MaximumFiles", 32,
                new ConfigDescription("Maximum source files in one backup transaction.",
                    new AcceptableValueRange<int>(1, 1024)));
            BackupMaximumMiB = Bind(config, "Migration Backups", "MaximumTotalMiB", 2048,
                new ConfigDescription("Maximum total source bytes in one backup transaction.",
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

using System.Collections.Generic;

namespace RunicProduction.Integration
{
    // Only the host has a complete peer list. A client's missing peer is not proof
    // that an owner is offline. This observer never claims ownership or edits items.
    internal static class ProductionOwnershipDiagnostics
    {
        private static readonly Dictionary<string, string> Warnings = new Dictionary<string, string>();

        internal static void Clear() => Warnings.Clear();

        internal static string OfflineOwnerWarning(ZDO zdo, string endpoint)
        {
            ZNet network = ZNet.instance;
            if (zdo == null || network == null || !network.IsServer()) return null;
            long owner = zdo.GetOwner();
            if (owner == 0L || owner == ZNet.GetUID() || network.GetPeer(owner) != null) return null;
            return global::Runic.Localization.RunicText.Get("text_869ccce1e9f4") + endpoint + global::Runic.Localization.RunicText.Get("text_78ec48810d11") + zdo.m_uid +
                   global::Runic.Localization.RunicText.Get("text_725bc2c19db1") + owner + global::Runic.Localization.RunicText.Get("text_35d4924f3360") +
                   global::Runic.Localization.RunicText.Get("text_2e5ac4baea10") +
                   global::Runic.Localization.RunicText.Get("text_840eefeafdbe") +
                   global::Runic.Localization.RunicText.Get("text_c3c5d84f2704");
        }

        internal static void Report(string stationId, string warning)
        {
            if (warning == null) { Warnings.Remove(stationId); return; }
            if (Warnings.TryGetValue(stationId, out string previous) && previous == warning) return;
            // Bound memory without clearing existing entries and spamming unchanged warnings.
            if (Warnings.Count >= 4096 && !Warnings.ContainsKey(stationId)) return;
            Warnings[stationId] = warning;
            ProductionDiagnostics.Warning(warning);
        }
    }
}

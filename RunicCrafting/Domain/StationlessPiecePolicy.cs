using System;
using System.Collections.Generic;

namespace RunicCrafting.Domain
{
    internal sealed class StationlessPiecePolicy
    {
        internal const string DefaultVanillaAllowList =
            "blackmarble_pile,blackwood_stack,bone_stack,bonfire,coal_pile,fire_pit,fire_pit_iron," +
            "grausten_pile,guard_stone,piece_artisanstation,piece_cookingstation,piece_groundtorch_mist," +
            "piece_logbench01,piece_pot1,piece_pot1_cracked,piece_pot1_red,piece_pot2,piece_pot2_cracked," +
            "piece_pot2_red,piece_pot3,piece_pot3_cracked,piece_pot3_red,piece_workbench,sapling_barley," +
            "sapling_carrot,sapling_flax,sapling_jotunpuffs,sapling_magecap,sapling_onion,sapling_seedcarrot," +
            "sapling_seedonion,sapling_seedturnip,sapling_turnip,sign,skull_pile,stone_pile,treasure_pile," +
            "treasure_stack,wood_core_stack,wood_fine_stack,wood_stack,wood_yggdrasil_stack";

        private const int MaximumRuleTextLength = 16384;
        private const int MaximumRulesPerList = 256;
        private const int MaximumPrefabIdLength = 128;
        private static readonly char[] Separators = { ',', ';', '\r', '\n' };

        private readonly HashSet<string> _allowed;
        private readonly HashSet<string> _denied;
        private readonly bool _allowAll;

        private StationlessPiecePolicy(
            HashSet<string> allowed,
            HashSet<string> denied,
            bool allowAll)
        {
            _allowed = allowed;
            _denied = denied;
            _allowAll = allowAll;
        }

        internal int AllowedRuleCount => _allowed.Count;
        internal int DeniedRuleCount => _denied.Count;
        internal bool AllowsWildcard => _allowAll;

        internal static StationlessPiecePolicy Parse(string allowList, string denyList)
        {
            bool allowValid = TryParseRules(allowList, out HashSet<string> allowed, out bool allowAll);
            bool denyValid = TryParseRules(denyList, out HashSet<string> denied, out bool denyAll);
            // Configuration errors must never broaden the mutation surface. An invalid allow list
            // therefore permits nothing, while an invalid deny list conservatively denies all.
            if (!allowValid)
            {
                allowed.Clear();
                allowAll = false;
            }
            if (!denyValid) denyAll = true;
            if (denyAll) denied.Add("*");
            return new StationlessPiecePolicy(allowed, denied, allowAll);
        }

        internal static StationlessPiecePolicy VanillaDefaults() =>
            Parse(DefaultVanillaAllowList, string.Empty);

        internal StationlessPieceDecision Evaluate(string prefabId)
        {
            string normalized = NormalizePrefabId(prefabId);
            if (normalized.Length == 0 || normalized.Length > MaximumPrefabIdLength)
                return new StationlessPieceDecision(false, "stationless-prefab-id-unavailable");
            if (_denied.Contains("*") || _denied.Contains(normalized))
                return new StationlessPieceDecision(false, "stationless-prefab-denied:" + normalized);
            if (_allowAll || _allowed.Contains(normalized))
                return new StationlessPieceDecision(true, "stationless-prefab-allowed:" + normalized);
            return new StationlessPieceDecision(false, "stationless-prefab-not-allowed:" + normalized);
        }

        private static bool TryParseRules(
            string text,
            out HashSet<string> result,
            out bool wildcard)
        {
            wildcard = false;
            result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(text)) return true;
            if (text.Length > MaximumRuleTextLength) return false;

            string[] values = text.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            if (values.Length > MaximumRulesPerList) return false;
            foreach (string value in values)
            {
                string rule = NormalizePrefabId(value);
                if (rule.Length == 0 || rule.Length > MaximumPrefabIdLength) return false;
                if (string.Equals(rule, "*", StringComparison.Ordinal)) wildcard = true;
                else result.Add(rule);
            }
            return true;
        }

        private static string NormalizePrefabId(string value)
        {
            value = (value ?? string.Empty).Trim();
            const string cloneSuffix = "(Clone)";
            if (value.EndsWith(cloneSuffix, StringComparison.OrdinalIgnoreCase))
                value = value.Substring(0, value.Length - cloneSuffix.Length).TrimEnd();
            return value;
        }
    }

    internal readonly struct StationlessPieceDecision
    {
        internal StationlessPieceDecision(bool allowed, string reasonCode)
        {
            Allowed = allowed;
            ReasonCode = reasonCode ?? string.Empty;
        }

        internal bool Allowed { get; }
        internal string ReasonCode { get; }
    }
}

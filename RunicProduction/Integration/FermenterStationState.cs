using System;
using RunicProduction.Core;

namespace RunicProduction.Integration
{
    /// <summary>
    /// Canonical, bounded representation of the only replicated Fermenter fields automation owns.
    /// It excludes cover and elapsed-time derivatives: vanilla remains their authority.
    /// </summary>
    internal sealed class FermenterStationState
    {
        private const int SchemaVersion = 1;
        private const int MaximumEncodedCharacters = 512;

        private FermenterStationState(string inputPrefabId, long startTicks)
        {
            InputPrefabId = inputPrefabId;
            StartTicks = startTicks;
        }

        internal string InputPrefabId { get; }
        internal long StartTicks { get; }
        internal bool IsEmpty => InputPrefabId.Length == 0;

        internal static FermenterStationState Empty { get; } =
            new FermenterStationState(string.Empty, 0L);

        internal static bool TryCreate(
            string inputPrefabId,
            long startTicks,
            out FermenterStationState state)
        {
            state = null;
            string prefab = inputPrefabId ?? string.Empty;
            if (prefab.Length == 0)
            {
                if (startTicks != 0L) return false;
                state = Empty;
                return true;
            }
            if (!StockDomainValidation.IsExactPrefabId(prefab) || startTicks <= 0L)
                return false;
            try
            {
                DateTime value = new DateTime(startTicks);
                if (value.Kind == DateTimeKind.Local) return false;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
            state = new FermenterStationState(prefab, startTicks);
            return true;
        }

        internal static bool TryCapture(Fermenter station, out FermenterStationState state)
        {
            state = null;
            return station != null && TryCreate(
                ValheimAccess.FermenterContent(station),
                ValheimAccess.FermenterStartTicks(station),
                out state);
        }

        internal static bool TryCapture(ZDO zdo, out FermenterStationState state)
        {
            state = null;
            return zdo != null && zdo.IsValid() && TryCreate(
                zdo.GetString(ZDOVars.s_content, string.Empty),
                zdo.GetLong(ZDOVars.s_startTime, 0L),
                out state);
        }

        internal string Serialize()
        {
            var package = new ZPackage();
            package.Write(SchemaVersion);
            package.Write(InputPrefabId);
            package.Write(StartTicks);
            return Convert.ToBase64String(package.GetArray());
        }

        internal static bool TryParse(string encoded, out FermenterStationState state)
        {
            state = null;
            if (string.IsNullOrEmpty(encoded) || encoded.Length > MaximumEncodedCharacters)
                return false;
            try
            {
                byte[] bytes = Convert.FromBase64String(encoded);
                if (bytes.Length == 0 || bytes.Length > 256) return false;
                var package = new ZPackage(bytes);
                if (package.ReadInt() != SchemaVersion) return false;
                string input = package.ReadString();
                long ticks = package.ReadLong();
                if (package.GetPos() != package.Size()) return false;
                return TryCreate(input, ticks, out state) &&
                       string.Equals(state.Serialize(), encoded, StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        internal bool Matches(Fermenter station) =>
            station != null &&
            string.Equals(InputPrefabId, ValheimAccess.FermenterContent(station), StringComparison.Ordinal) &&
            StartTicks == ValheimAccess.FermenterStartTicks(station);

        internal void Apply(Fermenter station)
        {
            if (station == null) throw new ArgumentNullException(nameof(station));
            ValheimAccess.SetFermenterState(station, InputPrefabId, StartTicks);
        }

        internal void ApplyToZdo(ZDO zdo)
        {
            if (zdo == null || !zdo.IsValid())
                throw new ArgumentNullException(nameof(zdo));
            zdo.Set(ZDOVars.s_content, InputPrefabId);
            zdo.Set(ZDOVars.s_startTime, StartTicks);
        }
    }
}

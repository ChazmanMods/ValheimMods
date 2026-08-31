using System;

namespace RunicProduction.Integration
{
    internal static class FermenterProducerSignature
    {
        private const int SchemaVersion = 1;

        internal static string ProducerId(
            FermenterDescriptor station,
            FermenterConversionDescriptor conversion)
        {
            if (station == null) throw new ArgumentNullException(nameof(station));
            if (conversion == null) throw new ArgumentNullException(nameof(conversion));
            return "fermenter:" + station.PrefabId + ":" + conversion.InputPrefabId;
        }

        internal static byte[] Create(
            FermenterDescriptor station,
            FermenterConversionDescriptor conversion)
        {
            if (station == null) throw new ArgumentNullException(nameof(station));
            if (conversion == null) throw new ArgumentNullException(nameof(conversion));
            return StockProducerSignature.Create(writer =>
            {
                writer.WriteString("RunicProduction.Fermenter");
                writer.WriteInt32(SchemaVersion);
                writer.WriteString(station.PrefabId);
                writer.WriteSingle(2400f);
                writer.WriteString(conversion.InputPrefabId);
                writer.WriteString(conversion.OutputPrefabId);
                writer.WriteInt32(conversion.OutputAmount);
            });
        }
    }
}

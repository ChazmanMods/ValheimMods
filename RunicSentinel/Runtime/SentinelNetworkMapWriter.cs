using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace RunicSentinel.Runtime
{
    /// <summary>
    /// Produces an on-demand server snapshot. Only authenticated administrators can
    /// request or download this diagnostic; it never runs from Update.
    /// </summary>
    internal static class SentinelNetworkMapWriter
    {
        private const int MaximumZdos = 16384;
        private const int MaximumEdges = 2048;
        private static readonly FieldInfo ObjectsField =
            AccessTools.Field(typeof(ZDOMan), "m_objectsByID");
        private static readonly string[] ProductionRoles =
            { "input", "fuel", "output", "replenishment" };

        internal static bool TryAppend(StringBuilder builder, out string failure)
        {
            failure = string.Empty;
            if (builder == null) { failure = "builder-missing"; return false; }
            ZNet network = ZNet.instance;
            ZDOMan manager = ZDOMan.instance;
            if (network == null || !network.IsServer() || manager == null)
            { failure = "server-console-required"; return false; }
            if (ObjectsField?.GetValue(manager) is not Dictionary<ZDOID, ZDO> objects)
            { failure = "world-index-unavailable"; return false; }

            int inspected = 0;
            int portalCount = 0;
            int productionEdges = 0;
            builder.Append("network-map=server-local-snapshot\n");
            foreach (KeyValuePair<ZDOID, ZDO> pair in objects)
            {
                if (inspected++ >= MaximumZdos) break;
                ZDO zdo = pair.Value;
                if (zdo == null || !zdo.IsValid()) continue;

                string portalRecord = zdo.GetString("runic.portals.record", string.Empty);
                string portalNetwork = Safe(zdo.GetString("runic.portals.network", string.Empty), 64);
                if (!string.IsNullOrEmpty(portalRecord) || !string.IsNullOrEmpty(portalNetwork))
                {
                    portalCount++;
                    builder.Append("portal=").Append(Id(pair.Key)).Append('|')
                        .Append(portalNetwork).Append('|')
                        .Append(Safe(zdo.GetString("runic.portals.name", string.Empty), 64)).Append('|')
                        .Append(zdo.GetInt("runic.portals.networkKind", 0).ToString(CultureInfo.InvariantCulture)).Append('|')
                        .Append(Safe(zdo.GetString("runic.portals.group", string.Empty), 64)).Append('|')
                        .Append(Position(zdo.GetPosition())).Append('\n');
                }

                for (int role = 0; role < ProductionRoles.Length && productionEdges < MaximumEdges; role++)
                {
                    string roleName = ProductionRoles[role];
                    string record = zdo.GetString(
                        "runic.production." + roleName + ".record", string.Empty);
                    if (string.IsNullOrEmpty(record) || record == "!") continue;
                    productionEdges++;
                    string target;
                    if (!TryReadProductionTarget(record, role, out string linkId, out target))
                        target = "record-invalid";
                    builder.Append("production-edge=").Append(Id(pair.Key)).Append('|')
                        .Append(ZNetScene.instance?.GetPrefab(zdo.GetPrefab())?.name??zdo.GetPrefab().ToString(CultureInfo.InvariantCulture)).Append('|')
                        .Append(roleName).Append('|').Append(Safe(linkId, 128)).Append('|')
                        .Append(Safe(target, 160)).Append('|').Append(Position(zdo.GetPosition())).Append('\n');
                }
            }
            builder.Append("network-map-summary=zdos:")
                .Append(Math.Min(inspected, MaximumZdos).ToString(CultureInfo.InvariantCulture))
                .Append(",portals:").Append(portalCount.ToString(CultureInfo.InvariantCulture))
                .Append(",production-edges:").Append(productionEdges.ToString(CultureInfo.InvariantCulture))
                .Append(",truncated:").Append((objects.Count > MaximumZdos || productionEdges >= MaximumEdges) ? "true" : "false")
                .Append('\n');
            return true;
        }

        private static bool TryReadProductionTarget(
            string encoded,
            int expectedRole,
            out string linkId,
            out string target)
        {
            linkId = string.Empty;
            target = string.Empty;
            try
            {
                if (encoded.Length > 2048) return false;
                byte[] envelopeBytes = Convert.FromBase64String(encoded);
                if (envelopeBytes.Length == 0 || envelopeBytes.Length > 1536) return false;
                var envelope = new ZPackage(envelopeBytes);
                byte[] payloadBytes = envelope.ReadByteArray();
                byte[] digest = envelope.ReadByteArray();
                if (envelope.GetPos() != envelope.Size() || payloadBytes == null ||
                    payloadBytes.Length == 0 || payloadBytes.Length > 1536 || digest?.Length != 32)
                    return false;
                using (SHA256 sha = SHA256.Create())
                {
                    byte[] actual = sha.ComputeHash(payloadBytes);
                    int difference = 0;
                    for (int index = 0; index < actual.Length; index++) difference |= actual[index] ^ digest[index];
                    if (difference != 0) return false;
                }
                var payload = new ZPackage(payloadBytes);
                if (payload.ReadInt() != 1 || payload.ReadInt() != expectedRole) return false;
                linkId = payload.ReadString();
                target = payload.ReadString();
                return payload.GetPos() <= payload.Size() && linkId.Length <= 128 && target.Length <= 160;
            }
            catch
            {
                linkId = string.Empty;
                target = string.Empty;
                return false;
            }
        }

        private static string Id(ZDOID id) => Safe(id.ToString(), 96);

        private static string Position(Vector3 value) =>
            value.x.ToString("F1", CultureInfo.InvariantCulture) + "," +
            value.y.ToString("F1", CultureInfo.InvariantCulture) + "," +
            value.z.ToString("F1", CultureInfo.InvariantCulture);

        private static string Safe(string value, int maximum)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.Length > maximum) value = value.Substring(0, maximum);
            for (int index = 0; index < value.Length; index++)
                if (char.IsControl(value[index]) || value[index] == '|') return "invalid-text";
            return value;
        }
    }
}

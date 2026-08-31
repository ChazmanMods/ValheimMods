using System;
using System.Globalization;

namespace RunicTransactions.Contracts
{
    /// <summary>Canonical stable-ID formatting shared by Valheim-facing Runic adapters.</summary>
    public static class ValheimIdentityIds
    {
        public const string PlayerPrefix = "valheim.player:";
        public const string ZdoEndpointPrefix = "valheim.zdo:";
        public const string WorldObjectEndpointPrefix = "valheim.world-object:";
        public const string InstanceEndpointPrefix = "valheim.instance:";

        public static PrincipalId Player(long playerId) =>
            new PrincipalId(PlayerPrefix + playerId.ToString(CultureInfo.InvariantCulture));

        public static EndpointId ZdoEndpoint(string zdoId)
        {
            string value = StableIdentifier.Require(zdoId, nameof(zdoId), 120);
            return new EndpointId(ZdoEndpointPrefix + value);
        }

        public static EndpointId WorldObjectEndpoint(string token)
        {
            string value = StableIdentifier.Require(token, nameof(token), 32);
            if (value.Length != 32 || !Guid.TryParseExact(value, "N", out Guid parsed) ||
                parsed == Guid.Empty ||
                !string.Equals(parsed.ToString("N"), value, StringComparison.Ordinal))
                throw new ArgumentException(
                    "A canonical lowercase GUID-N world-object token is required.",
                    nameof(token));
            return new EndpointId(WorldObjectEndpointPrefix + value);
        }

        public static EndpointId InstanceEndpoint(int instanceId) =>
            new EndpointId(InstanceEndpointPrefix + instanceId.ToString(CultureInfo.InvariantCulture));

        public static bool TryGetPlayerId(PrincipalId principalId, out long playerId)
        {
            playerId = 0;
            return principalId.IsValid && principalId.Value.StartsWith(PlayerPrefix, StringComparison.Ordinal) &&
                   long.TryParse(
                       principalId.Value.Substring(PlayerPrefix.Length),
                       NumberStyles.Integer,
                       CultureInfo.InvariantCulture,
                       out playerId);
        }
    }
}

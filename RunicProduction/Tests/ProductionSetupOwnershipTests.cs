using System;
using System.Collections.Generic;
using RunicProduction.Core;

namespace RunicProduction.Tests
{
    internal static class ProductionSetupOwnershipTests
    {
        internal static IReadOnlyList<KeyValuePair<string, Action>> Cases() =>
            new[]
            {
                Case("setup accepts all four initial station/chest ownership combinations", InitialOwners),
                Case("denied access cannot claim either object", DeniedAccess),
                Case("access revoked during station claim prevents chest claim", RevokedAccess),
                Case("failed station claim prevents chest claim", FailedStation),
                Case("failed chest claim prevents publication", FailedChest),
                Case("ownership lost during chest claim prevents publication", LostStation),
                Case("access revoked during chest claim prevents publication", RevokedAfterChest),
                Case("installed native ownership claim is parameterless and returns void", NativeClaimContract)
            };

        private static KeyValuePair<string, Action> Case(string name, Action test) =>
            new KeyValuePair<string, Action>(name, test);
        private static void Require(bool condition)
        {
            if (!condition) throw new InvalidOperationException("Setup ownership invariant failed.");
        }

        private static void InitialOwners()
        {
            foreach (bool initialStation in new[] { false, true })
            foreach (bool initialChest in new[] { false, true })
            {
                bool station = initialStation, chest = initialChest;
                int stationClaims = 0, chestClaims = 0;
                Require(ProductionSetupOwnership.TryAcquire(() => true,
                    () => station, () => { stationClaims++; station = true; },
                    () => chest, () => { chestClaims++; chest = true; }));
                Require(stationClaims == (initialStation ? 0 : 1));
                Require(chestClaims == (initialChest ? 0 : 1));
            }
        }
        private static void DeniedAccess()
        {
            int claims = 0;
            Require(!ProductionSetupOwnership.TryAcquire(() => false,
                () => false, () => claims++, () => false, () => claims++));
            Require(claims == 0);
        }
        private static void RevokedAccess()
        {
            bool allowed = true, station = false;
            int chestClaims = 0;
            Require(!ProductionSetupOwnership.TryAcquire(() => allowed,
                () => station, () => { station = true; allowed = false; },
                () => false, () => chestClaims++));
            Require(chestClaims == 0);
        }
        private static void FailedStation()
        {
            int chestClaims = 0;
            Require(!ProductionSetupOwnership.TryAcquire(() => true,
                () => false, () => { }, () => false, () => chestClaims++));
            Require(chestClaims == 0);
        }
        private static void FailedChest() => Require(!ProductionSetupOwnership.TryAcquire(
            () => true, () => true, () => { }, () => false, () => { }));
        private static void LostStation()
        {
            bool station = true, chest = false;
            Require(!ProductionSetupOwnership.TryAcquire(() => true,
                () => station, () => { }, () => chest, () => { chest = true; station = false; }));
        }
        private static void RevokedAfterChest()
        {
            bool allowed = true, chest = false;
            Require(!ProductionSetupOwnership.TryAcquire(() => allowed,
                () => true, () => { }, () => chest, () => { chest = true; allowed = false; }));
        }

        private static void NativeClaimContract()
        {
            var method = typeof(ZNetView).GetMethod("ClaimOwnership", Type.EmptyTypes);
            Require(method != null && method.ReturnType == typeof(void));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;

namespace RunicProduction.Tests
{
    internal static class SharedAuthorityContractTests
    {
        internal static IReadOnlyList<KeyValuePair<string, Action>> Cases() => new[]
        {
            new KeyValuePair<string, Action>("snapshot recovery bindings match installed native transport", () =>
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                if (typeof(ZNet).GetMethod("Start", flags, null, Type.EmptyTypes, null) == null)
                    throw new Exception("Network lifecycle hook missing");
                if (typeof(ZNet).GetMethod("GetServerPeer", flags, null, Type.EmptyTypes, null) == null ||
                    typeof(ZDO).GetProperty("OwnerRevision").PropertyType != typeof(ushort) ||
                    typeof(ZDO).GetProperty("DataRevision").PropertyType != typeof(uint) ||
                    typeof(ZDOMan).GetMethod("GetZDO", flags, null, new[] { typeof(ZDOID) }, null) == null)
                    throw new Exception("Snapshot transport contract changed");
                if (typeof(ZPackage).GetMethod("Size", flags, null, Type.EmptyTypes, null) == null)
                    throw new Exception("Package bound check missing");
            }),
            new KeyValuePair<string, Action>("native chest open and close hooks match installed Valheim", () =>
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var request = typeof(Container).GetMethod("RPC_RequestOpen", flags, null, new[] { typeof(long), typeof(long) }, null);
                if (request == null || request.ReturnType != typeof(void) || request.GetParameters()[0].Name != "uid" ||
                    request.GetParameters()[1].Name != "playerID") throw new Exception("Native request hook mismatch");
                var close = typeof(Container).GetMethod("SetInUse", flags, null, new[] { typeof(bool) }, null);
                if (close == null || close.ReturnType != typeof(void)) throw new Exception("Native close hook mismatch");
            })
        };
    }
}

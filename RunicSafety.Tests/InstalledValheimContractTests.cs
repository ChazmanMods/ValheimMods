using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RunicSafety.Integration;
using UnityEngine;

namespace RunicSafety.Tests
{
    internal static class InstalledValheimContractTests
    {
        private const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        internal static void Register()
        {
            TestRunner.Run("installed Valheim contract audit succeeds", FullAuditSucceeds);
            TestRunner.Run("installed Valheim version is exactly 1.0.12", VersionExact);
            TestRunner.Run("vanilla tombstone calls MoveInventoryToGrave", TombstoneUsesVanillaMove);
            TestRunner.Run("vanilla grave adopts original topology dimensions", GraveAdoptsDimensions);
            TestRunner.Run("vanilla grave preserves quest and equipped source items", GravePreservesExcludedItems);
            TestRunner.Run("incinerator request is exact owner-side boundary", IncineratorRequestBoundary);
            TestRunner.Run("incinerator lever delegates by RPC", IncineratorLeverUsesRpc);
            TestRunner.Run("station item callbacks retain exact installed signatures", StationSignaturesExact);
            TestRunner.Run("portal text commit remains an RPC outcome", PortalCommitIsRpc);
            TestRunner.Run("hammer removal retains ward and CanBeRemoved checks", RemovalRetainsChecks);
            TestRunner.Run("container occupied private removal gate remains installed", ContainerGateInstalled);
            TestRunner.Run("item stand retains owner request before delayed attach", ItemStandOwnerRequestInstalled);
            TestRunner.Run("installed save and network protocol constants are exact", ProtocolConstantsExact);
            TestRunner.Run("installed ZNet exposes sender peer and admin lookup", AdminLookupExact);
            TestRunner.Run("installed inventory serialization contract is exact", InventorySerializationExact);
        }

        private static void FullAuditSucceeds() => TestAssert.True(
            ValheimContracts.Initialize(out string problem), problem);

        private static void VersionExact()
        {
            TestAssert.True(ValheimContracts.IsSupportedVersion("1.0.7"));
            TestAssert.True(ValheimContracts.IsSupportedVersion("1.0.12"));
            foreach (string unsupported in new[] { "1.0.8", "1.0.13", "l-1.0.12", "1.0.120", "", null })
                TestAssert.True(!ValheimContracts.IsSupportedVersion(unsupported));
            TestAssert.Equal("1.0.12", ValheimContracts.ReadGameVersion());
            TestAssert.Equal(ValheimContracts.AuditedGameVersion, ValheimContracts.ReadGameVersion());
        }

        private static void TombstoneUsesVanillaMove()
        {
            MethodInfo create = Exact(typeof(Player), nameof(Player.CreateTombStone));
            TestAssert.True(IlReader.Calls(create, typeof(Inventory), nameof(Inventory.MoveInventoryToGrave)));
            TestAssert.True(IlReader.Calls(create, typeof(UnityEngine.Object), nameof(UnityEngine.Object.Instantiate)));
        }

        private static void GraveAdoptsDimensions()
        {
            MethodInfo move = Exact(typeof(Inventory), nameof(Inventory.MoveInventoryToGrave), typeof(Inventory));
            TestAssert.True(IlReader.AccessesField(move, typeof(Inventory), "m_width"));
            TestAssert.True(IlReader.AccessesField(move, typeof(Inventory), "m_height"));
            TestAssert.True(IlReader.AccessesField(move, typeof(Inventory), "m_inventory"));
        }

        private static void GravePreservesExcludedItems()
        {
            MethodInfo move = Exact(typeof(Inventory), nameof(Inventory.MoveInventoryToGrave), typeof(Inventory));
            TestAssert.True(IlReader.AccessesField(move, typeof(ItemDrop.ItemData), "m_equipped"));
            TestAssert.True(IlReader.AccessesField(move, typeof(ItemDrop.ItemData.SharedData), "m_questItem"));
        }

        private static void IncineratorRequestBoundary()
        {
            MethodInfo rpc = Exact(typeof(Incinerator), "RPC_RequestIncinerate", typeof(long), typeof(long));
            TestAssert.Equal(typeof(void), rpc.ReturnType);
            TestAssert.True(IlReader.Calls(rpc, typeof(ZNetView), nameof(ZNetView.IsOwner)));
            TestAssert.True(IlReader.Calls(rpc, typeof(Container), nameof(Container.IsInUse)));
            TestAssert.True(IlReader.Calls(rpc, typeof(Inventory), nameof(Inventory.NrOfItems)));
        }

        private static void IncineratorLeverUsesRpc()
        {
            MethodInfo method = Exact(typeof(Incinerator), "OnIncinerate",
                typeof(Switch), typeof(Humanoid), typeof(ItemDrop.ItemData));
            TestAssert.Equal(typeof(bool), method.ReturnType);
            TestAssert.True(IlReader.Calls(method, typeof(ZNetView), nameof(ZNetView.InvokeRPC)));
        }

        private static void StationSignaturesExact()
        {
            foreach (MethodInfo method in new[]
                     {
                         Exact(typeof(Smelter), "OnAddOre", typeof(Switch), typeof(Humanoid), typeof(ItemDrop.ItemData)),
                         Exact(typeof(Smelter), "OnAddFuel", typeof(Switch), typeof(Humanoid), typeof(ItemDrop.ItemData)),
                         Exact(typeof(CookingStation), "OnAddFuelSwitch", typeof(Switch), typeof(Humanoid), typeof(ItemDrop.ItemData)),
                         Exact(typeof(CookingStation), "OnUseItem", typeof(Humanoid), typeof(ItemDrop.ItemData)),
                         Exact(typeof(Fermenter), "AddItem", typeof(Humanoid), typeof(ItemDrop.ItemData)),
                         Exact(typeof(ItemStand), nameof(ItemStand.UseItem), typeof(Humanoid), typeof(ItemDrop.ItemData))
                     })
                TestAssert.Equal(typeof(bool), method.ReturnType, method.DeclaringType?.Name + "." + method.Name);
        }

        private static void PortalCommitIsRpc()
        {
            MethodInfo set = Exact(typeof(TeleportWorld), nameof(TeleportWorld.SetText), typeof(string));
            TestAssert.True(IlReader.Calls(set, typeof(ZNetView), nameof(ZNetView.InvokeRPC)));
            TestAssert.False(IlReader.Calls(set, typeof(ZDO), nameof(ZDO.Set)));
        }

        private static void RemovalRetainsChecks()
        {
            MethodInfo remove = Exact(typeof(Player), "RemovePiece");
            TestAssert.True(IlReader.Calls(remove, typeof(PrivateArea), nameof(PrivateArea.CheckAccess)));
            TestAssert.True(IlReader.Calls(remove, typeof(Piece), nameof(Piece.CanBeRemoved)));
            TestAssert.True(IlReader.Calls(remove, typeof(ZNetScene), nameof(ZNetScene.Destroy)));
        }

        private static void ContainerGateInstalled()
        {
            MethodInfo method = Exact(typeof(Container), nameof(Container.CanBeRemoved));
            TestAssert.True(IlReader.AccessesField(method, typeof(Container), "m_privacy"));
            TestAssert.True(IlReader.Calls(method, typeof(Inventory), nameof(Inventory.NrOfItems)));
        }

        private static void ItemStandOwnerRequestInstalled()
        {
            MethodInfo method = Exact(typeof(ItemStand), nameof(ItemStand.UseItem),
                typeof(Humanoid), typeof(ItemDrop.ItemData));
            TestAssert.True(IlReader.Calls(method, typeof(ItemStand), nameof(ItemStand.HaveAttachment)));
            TestAssert.True(IlReader.Calls(method, typeof(ZNetView), nameof(ZNetView.IsOwner)));
            TestAssert.True(IlReader.Calls(method, typeof(ZNetView), nameof(ZNetView.InvokeRPC)));
        }

        private static void ProtocolConstantsExact()
        {
            Type version = typeof(Player).Assembly.GetType("Version", true);
            TestAssert.Equal(40u, (uint)version.GetField("c_networkVersion").GetRawConstantValue());
            TestAssert.Equal(109, Convert.ToInt32(version.GetField("c_ItemDataVersion").GetRawConstantValue()));
            TestAssert.Equal(46, Convert.ToInt32(version.GetField("c_PlayerVersion").GetRawConstantValue()));
            TestAssert.Equal(41, Convert.ToInt32(version.GetField("c_WorldVersion").GetRawConstantValue()));
            TestAssert.Equal(33, Convert.ToInt32(version.GetField("c_PlayerDataVersion").GetRawConstantValue()));
        }

        private static void AdminLookupExact()
        {
            TestAssert.NotNull(typeof(ZNet).GetMethod(nameof(ZNet.GetPeer),
                BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(long) }, null));
            TestAssert.NotNull(typeof(ZNet).GetMethod(nameof(ZNet.IsAdmin),
                BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null));
            TestAssert.NotNull(typeof(ZNetPeer).GetField("m_socket", BindingFlags.Public | BindingFlags.Instance));
        }

        private static void InventorySerializationExact()
        {
            MethodInfo save = Exact(typeof(Inventory), nameof(Inventory.Save), typeof(ZPackage));
            TestAssert.Equal(typeof(void), save.ReturnType);
            TestAssert.NotNull(typeof(ZPackage).GetMethod(nameof(ZPackage.Size), Type.EmptyTypes));
            TestAssert.NotNull(typeof(ZPackage).GetMethod(nameof(ZPackage.GetArray), Type.EmptyTypes));
        }

        private static MethodInfo Exact(Type type, string name, params Type[] parameters) =>
            TestAssert.NotNull(type.GetMethod(name, Instance, null, parameters, null),
                type.FullName + "." + name + "(" +
                string.Join(",", parameters.Select(item => item.Name)) + ") missing.");
    }
}

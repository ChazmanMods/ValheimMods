using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RunicCrafting.Domain;

namespace RunicCrafting.Integration
{
    internal static class WorkshopAccessCommands
    {
        private static Terminal.ConsoleCommand _command;

        internal static void Initialize()
        {
            if (_command != null) return;
            _command = new Terminal.ConsoleCommand(
                "runiccrafting_access",
                "Show or configure the owned nearby station: show | station <policy> | materials <policy> | group <canonicalGroupUuid> | approve <playerId> | unapprove <playerId>",
                OnCommand);
        }

        private static void OnCommand(Terminal.ConsoleEventArgs args)
        {
            Player player = Player.m_localPlayer;
            if (!ValheimReflection.CanMutateLocalPlayer(player))
            {
                args.Context.AddString("Runic Crafting: workshop configuration requires the local player owner.");
                return;
            }
            CraftingStation station = player.GetCurrentCraftingStation() ??
                                      CraftingStation.GetCraftingStation(player.transform.position);
            if (station == null)
            {
                args.Context.AddString("Runic Crafting: stand at or use the station you want to configure.");
                return;
            }
            ZNetView view = ValheimReflection.GetView(station);
            ZDO zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            if (zdo == null)
            {
                args.Context.AddString("Runic Crafting: the authoritative station record is unavailable.");
                return;
            }

            string verb = args.Args.Length > 1 ? args.Args[1].Trim().ToLowerInvariant() : "show";
            if (verb == "show")
            {
                Show(args.Context, zdo);
                return;
            }
            Piece piece = station.GetComponent<Piece>();
            if (piece == null || piece.GetCreator() != player.GetPlayerID())
            {
                args.Context.AddString("Runic Crafting: only the station creator can change Workshop Access.");
                return;
            }
            if (!view.IsOwner())
            {
                args.Context.AddString("Runic Crafting: this station is controlled by another peer. Its access settings were not changed; try again when ownership settles.");
                return;
            }
            if (verb == "station" || verb == "materials")
            {
                if (args.Args.Length < 3 || !WorkshopAccessRuntime.TryParsePolicy(args.Args[2], out WorkshopPolicyKind policy))
                {
                    args.Context.AddString("Policy must be everyone, approved, owner, nobody, ward, ward.exceptions, or group.");
                    return;
                }
                string key = verb == "station"
                    ? WorkshopAccessRuntime.StationUseKey
                    : WorkshopAccessRuntime.LocalMaterialsKey;
                zdo.Set(key, WorkshopAccessRuntime.PolicyWireName(policy));
                args.Context.AddString($"Runic Crafting: {verb} policy set to {WorkshopAccessRuntime.PolicyWireName(policy)}.");
                return;
            }
            if (verb == "approve" || verb == "unapprove")
            {
                if (args.Args.Length < 3 ||
                    !long.TryParse(args.Args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long playerId) ||
                    playerId == 0L)
                {
                    args.Context.AddString("Runic Crafting: supply a non-zero stable Valheim player ID.");
                    return;
                }
                var approved = new SortedSet<long>();
                foreach (string value in zdo.GetString(WorkshopAccessRuntime.ApprovedKey, string.Empty)
                             .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                    if (long.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long existing))
                        approved.Add(existing);
                if (verb == "approve") approved.Add(playerId);
                else approved.Remove(playerId);
                zdo.Set(
                    WorkshopAccessRuntime.ApprovedKey,
                    string.Join(",", approved.Select(value => value.ToString(CultureInfo.InvariantCulture))));
                args.Context.AddString($"Runic Crafting: player {playerId} {(verb == "approve" ? "approved" : "removed from approvals")}.");
                return;
            }
            if (verb == "group")
            {
                string groupId = args.Args.Length > 2 ? args.Args[2] : string.Empty;
                if (!WorkshopAccessRuntime.IsCanonicalGroupId(groupId))
                {
                    args.Context.AddString(
                        "Runic Crafting: supply the exact 32-character canonical group UUID shown by runic_group list.");
                    return;
                }
                zdo.Set(WorkshopAccessRuntime.GroupIdKey, groupId);
                args.Context.AddString("Runic Crafting: workshop group set to " + groupId + ".");
                return;
            }
            args.Context.AddString("Usage: runiccrafting_access show | station <policy> | materials <policy> | group <canonicalGroupUuid> | approve <playerId> | unapprove <playerId>");
        }

        private static void Show(Terminal terminal, ZDO zdo)
        {
            string stationPolicy = zdo.GetString(
                WorkshopAccessRuntime.StationUseKey,
                WorkshopAccessRuntime.PolicyWireName(Configuration.DefaultStationUse.Value));
            string materialPolicy = zdo.GetString(
                WorkshopAccessRuntime.LocalMaterialsKey,
                WorkshopAccessRuntime.PolicyWireName(Configuration.DefaultLocalMaterialUse.Value));
            string approved = zdo.GetString(WorkshopAccessRuntime.ApprovedKey, string.Empty);
            string group = WorkshopAccessRuntime.ReadGroupId(zdo);
            terminal.AddString($"Runic Crafting Workshop Access: station={stationPolicy}; materials={materialPolicy}; group={(group.Length == 0 ? "none" : group)}; approved={(approved.Length == 0 ? "none" : approved)}");
        }
    }
}

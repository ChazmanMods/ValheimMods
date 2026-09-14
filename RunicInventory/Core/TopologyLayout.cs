using System;
using System.Collections.Generic;
using RunicInventory.Api;

namespace RunicInventory.Core
{
    internal enum InventoryItemCategory
    {
        Unknown = 0,
        Material = 1,
        Consumable = 2,
        Helmet = 3,
        Chest = 4,
        Legs = 5,
        Cape = 6,
        Utility = 7,
        Tool = 8,
        Other = 9,
        Ammunition = 10
    }

    internal sealed class TopologyLayout
    {
        internal const int RequiredWidth = 8;
        internal const int MinimumHeight = 4;
        internal const int RoleCount = 8;
        private readonly InventorySlotCoordinate[] _coordinates;

        private TopologyLayout(int width, int height)
        {
            Width = width;
            Height = height;
            SpecialRow = height - 1;
            _coordinates = new InventorySlotCoordinate[RoleCount];
            for (int index = 0; index < RoleCount; index++)
                _coordinates[index] = new InventorySlotCoordinate(index, SpecialRow);
        }

        internal int Width { get; }
        internal int Height { get; }
        internal int SpecialRow { get; }
        internal int TotalSlots => Width * Height;

        internal bool MatchesNativeDimensions(int width, int height) =>
            width == Width && height == Height && width == RequiredWidth &&
            height >= MinimumHeight && width * height <= InventoryTopologySnapshot.MaximumNativeSlots;

        internal InventorySlotCoordinate Coordinate(InventoryRoleKind role)
        {
            int index = (int)role - 1;
            if (index < 0 || index >= _coordinates.Length) throw new ArgumentOutOfRangeException(nameof(role));
            return _coordinates[index];
        }

        internal bool TryRoleAt(int x, int y, out InventoryRoleKind role)
        {
            if (y == SpecialRow && x >= 0 && x < RoleCount)
            {
                role = (InventoryRoleKind)(x + 1);
                return true;
            }
            role = default;
            return false;
        }

        internal bool InBounds(InventorySlotCoordinate coordinate) =>
            coordinate.X >= 0 && coordinate.X < Width && coordinate.Y >= 0 && coordinate.Y < Height;

        internal static bool TryCreate(int width, int height, out TopologyLayout layout, out string reasonCode)
        {
            layout = null;
            if (width != RequiredWidth)
            {
                reasonCode = "topology.width-not-eight";
                return false;
            }
            if (height < MinimumHeight)
            {
                reasonCode = "topology.height-too-small";
                return false;
            }
            if (height > InventoryTopologySnapshot.MaximumNativeSlots / RequiredWidth)
            {
                reasonCode = "topology.slot-bound-exceeded";
                return false;
            }
            var candidate = new TopologyLayout(width, height);
            var unique = new HashSet<InventorySlotCoordinate>();
            for (int index = 0; index < RoleCount; index++)
            {
                InventorySlotCoordinate coordinate = candidate._coordinates[index];
                if (!candidate.InBounds(coordinate) || !unique.Add(coordinate))
                {
                    reasonCode = "topology.role-proof-failed";
                    return false;
                }
            }
            layout = candidate;
            reasonCode = "ok";
            return true;
        }

        internal static bool Accepts(InventoryRoleKind role, InventoryItemCategory category)
        {
            switch (role)
            {
                case InventoryRoleKind.Head: return category == InventoryItemCategory.Helmet;
                case InventoryRoleKind.Chest: return category == InventoryItemCategory.Chest;
                case InventoryRoleKind.Legs: return category == InventoryItemCategory.Legs;
                case InventoryRoleKind.Cape: return category == InventoryItemCategory.Cape;
                case InventoryRoleKind.Utility: return category == InventoryItemCategory.Utility;
                case InventoryRoleKind.Quick1:
                case InventoryRoleKind.Quick2:
                case InventoryRoleKind.Quick3:
                    return category == InventoryItemCategory.Consumable ||
                           category == InventoryItemCategory.Tool ||
                           category == InventoryItemCategory.Utility;
                default: return false;
            }
        }

        internal static bool TryEquipmentRole(InventoryItemCategory category, out InventoryRoleKind role)
        {
            switch (category)
            {
                case InventoryItemCategory.Helmet: role = InventoryRoleKind.Head; return true;
                case InventoryItemCategory.Chest: role = InventoryRoleKind.Chest; return true;
                case InventoryItemCategory.Legs: role = InventoryRoleKind.Legs; return true;
                case InventoryItemCategory.Cape: role = InventoryRoleKind.Cape; return true;
                case InventoryItemCategory.Utility: role = InventoryRoleKind.Utility; return true;
                default: role = default; return false;
            }
        }
    }
}

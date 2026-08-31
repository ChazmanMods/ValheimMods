using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace RunicInventory.Api
{
    public enum InventoryAuthorityMode
    {
        Unavailable = 0,
        AuthoritativeLocal = 1,
        RemoteDedicatedCompatibility = 2,
        MigrationSafeCompatibility = 3,
        Disabled = 4,
        BatchInert = 5
    }

    public enum InventoryRoleKind
    {
        Head = 1,
        Chest = 2,
        Legs = 3,
        Cape = 4,
        Utility = 5,
        Quick1 = 6,
        Quick2 = 7,
        Quick3 = 8
    }

    public readonly struct InventorySlotCoordinate : IEquatable<InventorySlotCoordinate>, IComparable<InventorySlotCoordinate>
    {
        public InventorySlotCoordinate(int x, int y)
        {
            if (x < 0 || x >= 128) throw new ArgumentOutOfRangeException(nameof(x));
            if (y < 0 || y >= 128) throw new ArgumentOutOfRangeException(nameof(y));
            X = x;
            Y = y;
        }

        public int X { get; }
        public int Y { get; }

        public int CompareTo(InventorySlotCoordinate other)
        {
            int row = Y.CompareTo(other.Y);
            return row != 0 ? row : X.CompareTo(other.X);
        }

        public bool Equals(InventorySlotCoordinate other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is InventorySlotCoordinate other && Equals(other);
        public override int GetHashCode() => unchecked((Y * 397) ^ X);
        public override string ToString() => X + "," + Y;
    }

    public sealed class InventoryRoleSnapshot
    {
        public InventoryRoleSnapshot(
            InventoryRoleKind role,
            InventorySlotCoordinate coordinate,
            bool occupied,
            bool equipped,
            bool locked,
            int stack,
            string prefabId,
            string itemFingerprint)
        {
            if (!Enum.IsDefined(typeof(InventoryRoleKind), role))
                throw new ArgumentOutOfRangeException(nameof(role));
            if (stack < 0) throw new ArgumentOutOfRangeException(nameof(stack));
            PrefabId = Bounded(prefabId, nameof(prefabId), 128);
            ItemFingerprint = Bounded(itemFingerprint, nameof(itemFingerprint), 64);
            if (!occupied && (stack != 0 || PrefabId.Length != 0 || ItemFingerprint.Length != 0 || equipped))
                throw new ArgumentException("An empty role cannot disclose item state.");
            if (occupied && (stack <= 0 || ItemFingerprint.Length != 64))
                throw new ArgumentException("An occupied role requires a positive stack and SHA-256 fingerprint.");
            if (ItemFingerprint.Length != 0 && !IsLowerHex(ItemFingerprint))
                throw new ArgumentException("Item fingerprint must be lowercase SHA-256 hex.", nameof(itemFingerprint));
            Role = role;
            Coordinate = coordinate;
            Occupied = occupied;
            Equipped = equipped;
            Locked = locked;
            Stack = stack;
        }

        public InventoryRoleKind Role { get; }
        public InventorySlotCoordinate Coordinate { get; }
        public bool Occupied { get; }
        public bool Equipped { get; }
        public bool Locked { get; }
        public int Stack { get; }
        public string PrefabId { get; }
        public string ItemFingerprint { get; }

        private static string Bounded(string value, string name, int maximum)
        {
            string text = value ?? string.Empty;
            if (text.Length > maximum) throw new ArgumentOutOfRangeException(name);
            for (int index = 0; index < text.Length; index++)
                if (char.IsControl(text[index]) || text[index] == '<' || text[index] == '>')
                    throw new ArgumentException("Control and rich-text delimiter characters are not permitted.", name);
            return text;
        }

        private static bool IsLowerHex(string value)
        {
            for (int index = 0; index < value.Length; index++)
                if (!((value[index] >= '0' && value[index] <= '9') ||
                      (value[index] >= 'a' && value[index] <= 'f'))) return false;
            return true;
        }
    }

    public sealed class InventoryTopologySnapshot
    {
        public const int MaximumNativeSlots = 128;
        private readonly ReadOnlyCollection<InventoryRoleSnapshot> _roles;
        private readonly ReadOnlyCollection<InventorySlotCoordinate> _lockedSlots;

        public InventoryTopologySnapshot(
            string providerId,
            string protocolVersion,
            long generation,
            InventoryAuthorityMode authorityMode,
            int width,
            int height,
            int occupiedNativeSlots,
            bool serializationVerified,
            IEnumerable<InventoryRoleSnapshot> roles,
            IEnumerable<InventorySlotCoordinate> lockedSlots,
            string topologyHash)
        {
            ProviderId = Bounded(providerId, nameof(providerId), 64, required: true);
            ProtocolVersion = Bounded(protocolVersion, nameof(protocolVersion), 16, required: true);
            if (generation < 0) throw new ArgumentOutOfRangeException(nameof(generation));
            if (!Enum.IsDefined(typeof(InventoryAuthorityMode), authorityMode))
                throw new ArgumentOutOfRangeException(nameof(authorityMode));
            if (width != 8 || height < 4 || width * height > MaximumNativeSlots)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (occupiedNativeSlots < 0 || occupiedNativeSlots > width * height)
                throw new ArgumentOutOfRangeException(nameof(occupiedNativeSlots));

            var roleCopy = new List<InventoryRoleSnapshot>();
            var roleKinds = new HashSet<InventoryRoleKind>();
            var roleCoordinates = new HashSet<InventorySlotCoordinate>();
            if (roles != null)
            {
                foreach (InventoryRoleSnapshot role in roles)
                {
                    if (role == null || !roleKinds.Add(role.Role) || !roleCoordinates.Add(role.Coordinate))
                        throw new ArgumentException("Topology roles must be non-null and unique.", nameof(roles));
                    if (role.Coordinate.X >= width || role.Coordinate.Y >= height)
                        throw new ArgumentOutOfRangeException(nameof(roles));
                    roleCopy.Add(role);
                }
            }
            if (roleCopy.Count != 8) throw new ArgumentException("Exactly eight topology roles are required.", nameof(roles));
            roleCopy.Sort((left, right) => ((int)left.Role).CompareTo((int)right.Role));
            for (int index = 0; index < roleCopy.Count; index++)
                if ((int)roleCopy[index].Role != index + 1 || roleCopy[index].Coordinate.X != index ||
                    roleCopy[index].Coordinate.Y != height - 1)
                    throw new ArgumentException("Every canonical topology role is required exactly once.", nameof(roles));

            var lockCopy = new List<InventorySlotCoordinate>();
            var lockSet = new HashSet<InventorySlotCoordinate>();
            if (lockedSlots != null)
            {
                foreach (InventorySlotCoordinate coordinate in lockedSlots)
                {
                    if (coordinate.X >= width || coordinate.Y >= height)
                        throw new ArgumentOutOfRangeException(nameof(lockedSlots));
                    if (!lockSet.Add(coordinate)) throw new ArgumentException("Duplicate locked slot.", nameof(lockedSlots));
                    lockCopy.Add(coordinate);
                }
            }
            if (lockCopy.Count > MaximumNativeSlots) throw new ArgumentOutOfRangeException(nameof(lockedSlots));
            lockCopy.Sort();
            int occupiedRoles = 0;
            foreach (InventoryRoleSnapshot role in roleCopy)
            {
                if (role.Occupied) occupiedRoles++;
                if (role.Locked != lockSet.Contains(role.Coordinate))
                    throw new ArgumentException("Role lock facts must match the canonical locked-slot set.", nameof(lockedSlots));
            }
            if (occupiedNativeSlots < occupiedRoles)
                throw new ArgumentOutOfRangeException(nameof(occupiedNativeSlots));

            TopologyHash = Bounded(topologyHash, nameof(topologyHash), 64, required: true);
            if (TopologyHash.Length != 64) throw new ArgumentException("Topology hash must be SHA-256 hex.", nameof(topologyHash));
            for (int index = 0; index < TopologyHash.Length; index++)
                if (!((TopologyHash[index] >= '0' && TopologyHash[index] <= '9') ||
                      (TopologyHash[index] >= 'a' && TopologyHash[index] <= 'f')))
                    throw new ArgumentException("Topology hash must be lowercase SHA-256 hex.", nameof(topologyHash));
            Generation = generation;
            AuthorityMode = authorityMode;
            Width = width;
            Height = height;
            OccupiedNativeSlots = occupiedNativeSlots;
            SerializationVerified = serializationVerified;
            _roles = roleCopy.AsReadOnly();
            _lockedSlots = lockCopy.AsReadOnly();
        }

        public string ProviderId { get; }
        public string ProtocolVersion { get; }
        public long Generation { get; }
        public InventoryAuthorityMode AuthorityMode { get; }
        public int Width { get; }
        public int Height { get; }
        public int TotalNativeSlots => Width * Height;
        public int OccupiedNativeSlots { get; }
        public bool SerializationVerified { get; }
        public IReadOnlyList<InventoryRoleSnapshot> Roles => _roles;
        public IReadOnlyList<InventorySlotCoordinate> LockedSlots => _lockedSlots;
        public string TopologyHash { get; }

        private static string Bounded(string value, string name, int maximum, bool required)
        {
            string text = value ?? string.Empty;
            if (required && text.Length == 0) throw new ArgumentException("A value is required.", name);
            if (text.Length > maximum) throw new ArgumentOutOfRangeException(name);
            for (int index = 0; index < text.Length; index++)
                if (char.IsControl(text[index]) || text[index] == '<' || text[index] == '>')
                    throw new ArgumentException("Control and rich-text delimiter characters are not permitted.", name);
            return text;
        }
    }

    public interface IInventoryTopologyService
    {
        string ProviderId { get; }
        bool TryCapture(long playerId, out InventoryTopologySnapshot snapshot, out string failureCode);
    }

    public interface IInventoryProtectionService
    {
        bool TryIsLocked(long playerId, InventorySlotCoordinate coordinate, out bool locked, out string failureCode);
    }

    public sealed class InventoryFeatureStatus
    {
        public InventoryFeatureStatus(InventoryAuthorityMode mode, bool topologyActive, string reasonCode)
        {
            if (!Enum.IsDefined(typeof(InventoryAuthorityMode), mode)) throw new ArgumentOutOfRangeException(nameof(mode));
            if (string.IsNullOrWhiteSpace(reasonCode) || reasonCode.Length > 96)
                throw new ArgumentException("A bounded reason code is required.", nameof(reasonCode));
            for (int index = 0; index < reasonCode.Length; index++)
                if (!(char.IsLetterOrDigit(reasonCode[index]) || reasonCode[index] == '.' || reasonCode[index] == '-'))
                    throw new ArgumentException("Reason code contains an unsafe character.", nameof(reasonCode));
            Mode = mode;
            TopologyActive = topologyActive;
            ReasonCode = reasonCode;
        }

        public InventoryAuthorityMode Mode { get; }
        public bool TopologyActive { get; }
        public string ReasonCode { get; }
    }

    public interface IInventoryStatusService
    {
        InventoryFeatureStatus Snapshot();
    }
}

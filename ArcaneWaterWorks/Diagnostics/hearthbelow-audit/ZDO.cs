using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnityEngine;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public class ZDO : IEquatable<ZDO>
{
	[Flags]
	private enum ExtraDataFlags : ushort
	{
		None = 0,
		Connections = 1,
		Floats = 2,
		Vec3 = 4,
		Quaternions = 8,
		Ints = 0x10,
		Longs = 0x20,
		Strings = 0x40,
		ByteArrays = 0x80,
		AnyLow8Bits = 0xFF,
		Persistent = 0x100,
		Distant = 0x200,
		Type = 0xC00,
		Rotation = 0x1000,
		SmallPosition = 0x2000
	}

	[Flags]
	private enum DataFlags : byte
	{
		None = 0,
		Type = 3,
		Persistent = 4,
		Distant = 8,
		Created = 0x10,
		Owner = 0x20,
		Owned = 0x40,
		OutsideZones = 0x80
	}

	public enum ObjectType : byte
	{
		Default,
		Prioritized,
		Solid,
		Terrain
	}

	private const int c_PrefabInvalid = -1;

	private const int c_PrefabNull = 0;

	public ZDOID m_uid = ZDOID.None;

	private Vector3 m_rotation = Quaternion.identity.eulerAngles;

	private Vector3 m_position = Vector3.zero;

	public float m_tempSortValue;

	private int m_prefab = -1;

	private DataFlags m_dataFlags;

	private byte m_tempRemoveEarmark = byte.MaxValue;

	public bool Persistent
	{
		get
		{
			return (m_dataFlags & DataFlags.Persistent) != 0;
		}
		set
		{
			if (value)
			{
				m_dataFlags |= DataFlags.Persistent;
			}
			else
			{
				m_dataFlags &= ~DataFlags.Persistent;
			}
		}
	}

	public bool Distant
	{
		get
		{
			return (m_dataFlags & DataFlags.Distant) != 0;
		}
		set
		{
			if (value)
			{
				m_dataFlags |= DataFlags.Distant;
			}
			else
			{
				m_dataFlags &= ~DataFlags.Distant;
			}
		}
	}

	private bool Owner
	{
		get
		{
			return (m_dataFlags & DataFlags.Owner) != 0;
		}
		set
		{
			if (value)
			{
				m_dataFlags |= DataFlags.Owner;
			}
			else
			{
				m_dataFlags &= ~DataFlags.Owner;
			}
		}
	}

	private bool Owned
	{
		get
		{
			return (m_dataFlags & DataFlags.Owned) != 0;
		}
		set
		{
			if (value)
			{
				m_dataFlags |= DataFlags.Owned;
			}
			else
			{
				m_dataFlags &= ~DataFlags.Owned;
			}
		}
	}

	private bool OutsideZones
	{
		get
		{
			return (m_dataFlags & DataFlags.OutsideZones) != 0;
		}
		set
		{
			if (value)
			{
				m_dataFlags |= DataFlags.OutsideZones;
			}
			else
			{
				m_dataFlags &= ~DataFlags.OutsideZones;
			}
		}
	}

	public bool Created
	{
		get
		{
			return (m_dataFlags & DataFlags.Created) != 0;
		}
		set
		{
			if (value)
			{
				m_dataFlags |= DataFlags.Created;
			}
			else
			{
				m_dataFlags &= ~DataFlags.Created;
			}
		}
	}

	public ObjectType Type
	{
		get
		{
			return (ObjectType)(m_dataFlags & DataFlags.Type);
		}
		set
		{
			m_dataFlags = (DataFlags)((uint)(m_dataFlags & ~DataFlags.Type) | (uint)(value & ObjectType.Terrain));
		}
	}

	private bool SaveClone
	{
		get
		{
			return m_tempSortValue < 0f;
		}
		set
		{
			if (value)
			{
				m_tempSortValue = -1f;
			}
			else
			{
				m_tempSortValue = 0f;
			}
		}
	}

	private bool Valid
	{
		get
		{
			return m_prefab != -1;
		}
		set
		{
			m_prefab = ((!value) ? (-1) : 0);
		}
	}

	public byte TempRemoveEarmark
	{
		get
		{
			return m_tempRemoveEarmark;
		}
		set
		{
			m_tempRemoveEarmark = value;
		}
	}

	public ushort OwnerRevision { get; set; }

	public uint DataRevision { get; set; }

	public void Initialize(ZDOID id, Vector3 position)
	{
		m_uid = id;
		m_position = position;
		ZoneSystem.SectorIndex sectorIndex = ZoneSystem.GetSectorIndex(m_position);
		ZDOMan.instance.AddToSector(this, sectorIndex);
		m_dataFlags = DataFlags.None;
		Valid = true;
	}

	public void Init()
	{
		m_dataFlags = DataFlags.None;
		Valid = true;
	}

	public override string ToString()
	{
		return m_uid.ToString();
	}

	public bool IsValid()
	{
		return m_prefab != -1;
	}

	public override int GetHashCode()
	{
		return m_uid.GetHashCode();
	}

	public bool Equals(ZDO other)
	{
		if (other != null)
		{
			return m_uid == other.m_uid;
		}
		return false;
	}

	public void Reset()
	{
		if (!SaveClone)
		{
			ZDOExtraData.Release(this, m_uid);
		}
		m_uid = ZDOID.None;
		m_dataFlags = DataFlags.None;
		OwnerRevision = 0;
		DataRevision = 0u;
		m_tempSortValue = 0f;
		Valid = false;
		m_position = Vector3.zero;
		m_rotation = Quaternion.identity.eulerAngles;
	}

	public ZDO Clone()
	{
		ZDO obj = MemberwiseClone() as ZDO;
		obj.SaveClone = true;
		return obj;
	}

	public void Set(string name, ZDOID id)
	{
		Set(GetHashZDOID(name), id);
	}

	public void Set(KeyValuePair<int, int> hashPair, ZDOID id)
	{
		Set(hashPair.Key, id.UserID);
		Set(hashPair.Value, id.ID);
	}

	public static KeyValuePair<int, int> GetHashZDOID(string name)
	{
		return new KeyValuePair<int, int>((name + "_u").GetStableHashCode(), (name + "_i").GetStableHashCode());
	}

	public ZDOID GetZDOID(string name)
	{
		return GetZDOID(GetHashZDOID(name));
	}

	public ZDOID GetZDOID(KeyValuePair<int, int> hashPair)
	{
		long num = GetLong(hashPair.Key, 0L);
		uint num2 = (uint)GetLong(hashPair.Value, 0L);
		if (num == 0L || num2 == 0)
		{
			return ZDOID.None;
		}
		return new ZDOID(num, num2);
	}

	public void AddSessionHash(int hash)
	{
		ZDOExtraData.AddSessionHash(hash);
	}

	public void Set(string name, float value)
	{
		Set(name.GetStableHashCode(), value);
	}

	public void Set(int hash, float value)
	{
		if (ZDOExtraData.Set(m_uid, hash, value))
		{
			IncreaseDataRevision();
		}
	}

	public void Set(string name, Vector3 value)
	{
		Set(name.GetStableHashCode(), value);
	}

	public void Set(int hash, Vector3 value)
	{
		if (ZDOExtraData.Set(m_uid, hash, value))
		{
			IncreaseDataRevision();
		}
	}

	public void Update(int hash, Vector3 value)
	{
		if (ZDOExtraData.Update(m_uid, hash, value))
		{
			IncreaseDataRevision();
		}
	}

	public void Set(string name, Quaternion value)
	{
		Set(name.GetStableHashCode(), value);
	}

	public void Set(int hash, Quaternion value)
	{
		if (ZDOExtraData.Set(m_uid, hash, value))
		{
			IncreaseDataRevision();
		}
	}

	public void Set(string name, int value)
	{
		Set(name.GetStableHashCode(), value);
	}

	public void Set(int hash, int value, bool okForNotOwner = false)
	{
		if (ZDOExtraData.Set(m_uid, hash, value))
		{
			IncreaseDataRevision();
		}
	}

	public void SetConnection(ZDOExtraData.ConnectionType connectionType, ZDOID zid)
	{
		if (ZDOExtraData.SetConnection(m_uid, connectionType, zid))
		{
			IncreaseDataRevision();
			if (connectionType == ZDOExtraData.ConnectionType.Portal)
			{
				ZDOMan.instance.SetDirtyPortals();
			}
		}
	}

	public void UpdateConnection(ZDOExtraData.ConnectionType connectionType, ZDOID zid)
	{
		if (ZDOExtraData.UpdateConnection(m_uid, connectionType, zid))
		{
			IncreaseDataRevision();
			if (connectionType == ZDOExtraData.ConnectionType.Portal)
			{
				ZDOMan.instance.SetDirtyPortals();
			}
		}
	}

	public void Set(string name, bool value)
	{
		Set(name.GetStableHashCode(), value);
	}

	public void Set(int hash, bool value)
	{
		Set(hash, value ? 1 : 0);
	}

	public void Set(string name, long value)
	{
		Set(name.GetStableHashCode(), value);
	}

	public void Set(int hash, long value)
	{
		if (ZDOExtraData.Set(m_uid, hash, value))
		{
			IncreaseDataRevision();
		}
	}

	public void Set(string name, byte[] bytes)
	{
		Set(name.GetStableHashCode(), bytes);
	}

	public void Set(int hash, byte[] bytes)
	{
		if (ZDOExtraData.Set(m_uid, hash, bytes))
		{
			IncreaseDataRevision();
		}
	}

	public void Set(string name, string value)
	{
		Set(name.GetStableHashCode(), value);
	}

	public void Set(int hash, string value)
	{
		if (ZDOExtraData.Set(m_uid, hash, value))
		{
			IncreaseDataRevision();
		}
	}

	public void SetPosition(Vector3 pos)
	{
		InternalSetPosition(pos);
	}

	public void InternalSetPosition(Vector3 pos)
	{
		if (!(m_position == pos))
		{
			SetSector(ZoneSystem.GetSectorIndex(pos));
			m_position = pos;
			if (IsOwner())
			{
				IncreaseDataRevision();
			}
		}
	}

	public void InvalidateSector()
	{
		SetSector(ZoneSystem.SectorZero);
	}

	private void SetSector(ZoneSystem.SectorIndex sectorIndex)
	{
		if (Game.instance.PortalPrefabHash.Contains(GetPrefab()))
		{
			return;
		}
		ZoneSystem.SectorIndex sectorIndex2 = (OutsideZones ? ZoneSystem.SectorZero : GetSectorIndex());
		if (!(sectorIndex2 == sectorIndex))
		{
			ZDOMan.instance.RemoveFromSector(this, sectorIndex2);
			OutsideZones = sectorIndex.Sector == 0;
			ZDOMan.instance.AddToSector(this, sectorIndex);
			if (ZNet.instance.IsServer())
			{
				ZDOMan.instance.ZDOSectorInvalidated(this);
			}
		}
	}

	public Vector2s GetSector()
	{
		return ZoneSystem.GetZone(m_position);
	}

	public ZoneSystem.SectorIndex GetSectorIndex()
	{
		return ZoneSystem.GetSectorIndex(m_position);
	}

	public void SetRotation(Quaternion rot)
	{
		if (!(m_rotation == rot.eulerAngles))
		{
			m_rotation = rot.eulerAngles;
			IncreaseDataRevision();
		}
	}

	public void SetType(ObjectType type)
	{
		if (Type != type)
		{
			Type = type;
			IncreaseDataRevision();
		}
	}

	public void SetDistant(bool distant)
	{
		if (Distant != distant)
		{
			Distant = distant;
			IncreaseDataRevision();
		}
	}

	public void SetPrefab(int prefab)
	{
		if (m_prefab != prefab)
		{
			m_prefab = prefab;
			IncreaseDataRevision();
		}
	}

	public int GetPrefab()
	{
		return m_prefab;
	}

	public Vector3 GetPosition()
	{
		return m_position;
	}

	public Quaternion GetRotation()
	{
		return Quaternion.Euler(m_rotation);
	}

	private void IncreaseDataRevision()
	{
		DataRevision++;
		if (!ZNet.instance.IsServer())
		{
			ZDOMan.instance.ClientChanged(m_uid);
		}
		ZDOMan.instance.SetDirtySector(this);
	}

	private void IncreaseOwnerRevision()
	{
		OwnerRevision++;
		if (!ZNet.instance.IsServer())
		{
			ZDOMan.instance.ClientChanged(m_uid);
		}
		ZDOMan.instance.SetDirtySector(this);
	}

	public float GetFloat(string name, float defaultValue = 0f)
	{
		return GetFloat(name.GetStableHashCode(), defaultValue);
	}

	public float GetFloat(int hash, float defaultValue = 0f)
	{
		return ZDOExtraData.GetFloat(m_uid, hash, defaultValue);
	}

	public bool GetFloat(string name, out float value)
	{
		return GetFloat(name.GetStableHashCode(), out value);
	}

	public bool GetFloat(int hash, out float value)
	{
		return ZDOExtraData.GetFloat(m_uid, hash, out value);
	}

	public Vector3 GetVec3(string name, Vector3 defaultValue)
	{
		return GetVec3(name.GetStableHashCode(), defaultValue);
	}

	public Vector3 GetVec3(int hash, Vector3 defaultValue)
	{
		return ZDOExtraData.GetVec3(m_uid, hash, defaultValue);
	}

	public bool GetVec3(string name, out Vector3 value)
	{
		return GetVec3(name.GetStableHashCode(), out value);
	}

	public bool GetVec3(int hash, out Vector3 value)
	{
		return ZDOExtraData.GetVec3(m_uid, hash, out value);
	}

	public Quaternion GetQuaternion(string name, Quaternion defaultValue)
	{
		return GetQuaternion(name.GetStableHashCode(), defaultValue);
	}

	public Quaternion GetQuaternion(int hash, Quaternion defaultValue)
	{
		return ZDOExtraData.GetQuaternion(m_uid, hash, defaultValue);
	}

	public bool GetQuaternion(string name, out Quaternion value)
	{
		return GetQuaternion(name.GetStableHashCode(), out value);
	}

	public bool GetQuaternion(int hash, out Quaternion value)
	{
		return ZDOExtraData.GetQuaternion(m_uid, hash, out value);
	}

	public int GetInt(string name, int defaultValue = 0)
	{
		return GetInt(name.GetStableHashCode(), defaultValue);
	}

	public int GetInt(int hash, int defaultValue = 0)
	{
		return ZDOExtraData.GetInt(m_uid, hash, defaultValue);
	}

	public bool GetInt(string name, out int value)
	{
		return GetInt(name.GetStableHashCode(), out value);
	}

	public bool GetInt(int hash, out int value)
	{
		return ZDOExtraData.GetInt(m_uid, hash, out value);
	}

	public bool GetBool(string name, bool defaultValue = false)
	{
		return GetBool(name.GetStableHashCode(), defaultValue);
	}

	public bool GetBool(int hash, bool defaultValue = false)
	{
		return ZDOExtraData.GetInt(m_uid, hash, defaultValue ? 1 : 0) != 0;
	}

	public bool GetBool(string name, out bool value)
	{
		return GetBool(name.GetStableHashCode(), out value);
	}

	public bool GetBool(int hash, out bool value)
	{
		return ZDOExtraData.GetBool(m_uid, hash, out value);
	}

	public long GetLong(string name, long defaultValue = 0L)
	{
		return GetLong(name.GetStableHashCode(), defaultValue);
	}

	public long GetLong(int hash, long defaultValue = 0L)
	{
		return ZDOExtraData.GetLong(m_uid, hash, defaultValue);
	}

	public string GetString(string name, string defaultValue = "")
	{
		return GetString(name.GetStableHashCode(), defaultValue);
	}

	public string GetString(int hash, string defaultValue = "")
	{
		return ZDOExtraData.GetString(m_uid, hash, defaultValue);
	}

	public bool GetString(string name, out string value)
	{
		return GetString(name.GetStableHashCode(), out value);
	}

	public bool GetString(int hash, out string value)
	{
		return ZDOExtraData.GetString(m_uid, hash, out value);
	}

	public byte[] GetByteArray(string name, byte[] defaultValue = null)
	{
		return GetByteArray(name.GetStableHashCode(), defaultValue);
	}

	public byte[] GetByteArray(int hash, byte[] defaultValue = null)
	{
		return ZDOExtraData.GetByteArray(m_uid, hash, defaultValue);
	}

	public bool GetByteArray(string name, out byte[] value)
	{
		return GetByteArray(name.GetStableHashCode(), out value);
	}

	public bool GetByteArray(int hash, out byte[] value)
	{
		return ZDOExtraData.GetByteArray(m_uid, hash, out value);
	}

	public ZDOID GetConnectionZDOID(ZDOExtraData.ConnectionType type)
	{
		return ZDOExtraData.GetConnectionZDOID(m_uid, type);
	}

	public ZDOExtraData.ConnectionType GetConnectionType()
	{
		return ZDOExtraData.GetConnectionType(m_uid);
	}

	public ZDOConnection GetConnection()
	{
		return ZDOExtraData.GetConnection(m_uid);
	}

	public ZDOConnectionHashData GetConnectionHashData(ZDOExtraData.ConnectionType type)
	{
		return ZDOExtraData.GetConnectionHashData(m_uid, type);
	}

	public bool RemoveInt(string name)
	{
		return RemoveInt(name.GetStableHashCode());
	}

	public bool RemoveInt(int hash)
	{
		return ZDOExtraData.RemoveInt(m_uid, hash);
	}

	public bool RemoveBool(int hash)
	{
		return ZDOExtraData.RemoveInt(m_uid, hash);
	}

	public bool RemoveLong(int hash)
	{
		return ZDOExtraData.RemoveLong(m_uid, hash);
	}

	public bool RemoveFloat(string name)
	{
		return RemoveFloat(name.GetStableHashCode());
	}

	public bool RemoveFloat(int hash)
	{
		return ZDOExtraData.RemoveFloat(m_uid, hash);
	}

	public bool RemoveVec3(string name)
	{
		return RemoveVec3(name.GetStableHashCode());
	}

	public bool RemoveVec3(int hash)
	{
		return ZDOExtraData.RemoveVec3(m_uid, hash);
	}

	public bool RemoveQuaternion(string name)
	{
		return RemoveQuaternion(name.GetStableHashCode());
	}

	public bool RemoveQuaternion(int hash)
	{
		return ZDOExtraData.RemoveQuaternion(m_uid, hash);
	}

	public bool RemoveString(int hash)
	{
		return ZDOExtraData.RemoveString(m_uid, hash);
	}

	public bool RemoveByteArray(int hash)
	{
		return ZDOExtraData.RemoveByteArray(m_uid, hash);
	}

	public bool Remove(int hash)
	{
		return ZDOExtraData.Remove(m_uid, hash);
	}

	public void RemoveZDOID(string name)
	{
		KeyValuePair<int, int> hashZDOID = GetHashZDOID(name);
		ZDOExtraData.RemoveLong(m_uid, hashZDOID.Key);
		ZDOExtraData.RemoveLong(m_uid, hashZDOID.Value);
	}

	public void RemoveZDOID(KeyValuePair<int, int> hashes)
	{
		ZDOExtraData.RemoveLong(m_uid, hashes.Key);
		ZDOExtraData.RemoveLong(m_uid, hashes.Value);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool IsFlagSet(ExtraDataFlags dataFlags, ExtraDataFlags flag)
	{
		return (dataFlags & flag) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void SetFlag(ref ExtraDataFlags dataFlags, ExtraDataFlags flag)
	{
		dataFlags |= flag;
	}

	public void Serialize(ZPackage pkg)
	{
		ZDOExtraData.GetData(m_uid, out var floats, out var vec3s, out var quats, out var ints, out var longs, out var strings, out var byteArray, out var connectionData);
		ExtraDataFlags dataFlags = ExtraDataFlags.None;
		if (connectionData != null && connectionData.m_type != ZDOExtraData.ConnectionType.None)
		{
			SetFlag(ref dataFlags, ExtraDataFlags.Connections);
		}
		if (floats.Count > 0)
		{
			SetFlag(ref dataFlags, ExtraDataFlags.Floats);
		}
		if (vec3s.Count > 0)
		{
			SetFlag(ref dataFlags, ExtraDataFlags.Vec3);
		}
		if (quats.Count > 0)
		{
			SetFlag(ref dataFlags, ExtraDataFlags.Quaternions);
		}
		if (ints.Count > 0)
		{
			SetFlag(ref dataFlags, ExtraDataFlags.Ints);
		}
		if (longs.Count > 0)
		{
			SetFlag(ref dataFlags, ExtraDataFlags.Longs);
		}
		if (strings.Count > 0)
		{
			SetFlag(ref dataFlags, ExtraDataFlags.Strings);
		}
		if (byteArray.Count > 0)
		{
			SetFlag(ref dataFlags, ExtraDataFlags.ByteArrays);
		}
		bool num = m_rotation != Quaternion.identity.eulerAngles;
		if (Persistent)
		{
			SetFlag(ref dataFlags, ExtraDataFlags.Persistent);
		}
		if (Distant)
		{
			SetFlag(ref dataFlags, ExtraDataFlags.Distant);
		}
		SetFlag(ref dataFlags, (ExtraDataFlags)((uint)Type << 10));
		if (num)
		{
			SetFlag(ref dataFlags, ExtraDataFlags.Rotation);
		}
		pkg.Write((ushort)dataFlags);
		pkg.Write(m_prefab);
		if (num)
		{
			pkg.Write(m_rotation);
		}
		if (IsFlagSet(dataFlags, ExtraDataFlags.AnyLow8Bits))
		{
			if (IsFlagSet(dataFlags, ExtraDataFlags.Connections))
			{
				pkg.Write((byte)connectionData.m_type);
				pkg.Write(connectionData.m_target);
			}
			ZDODataHelper.WriteData(pkg, floats, delegate(float value)
			{
				pkg.Write(value);
			});
			ZDODataHelper.WriteData(pkg, vec3s, delegate(Vector3 value)
			{
				pkg.Write(value);
			});
			ZDODataHelper.WriteData(pkg, quats, delegate(Quaternion value)
			{
				pkg.Write(value);
			});
			ZDODataHelper.WriteData(pkg, ints, delegate(int value)
			{
				pkg.Write(value);
			});
			ZDODataHelper.WriteData(pkg, longs, delegate(long value)
			{
				pkg.Write(value);
			});
			ZDODataHelper.WriteData(pkg, strings, delegate(string value)
			{
				pkg.Write(value);
			});
			ZDODataHelper.WriteData(pkg, byteArray, delegate(byte[] value)
			{
				pkg.Write(value);
			});
		}
	}

	public void Deserialize(ZPackage pkg)
	{
		ExtraDataFlags extraDataFlags = (ExtraDataFlags)pkg.ReadUShort();
		Persistent = IsFlagSet(extraDataFlags, ExtraDataFlags.Persistent);
		Distant = IsFlagSet(extraDataFlags, ExtraDataFlags.Distant);
		Type = (ObjectType)(((int)extraDataFlags >> 10) & 3);
		m_prefab = pkg.ReadInt();
		if (IsFlagSet(extraDataFlags, ExtraDataFlags.Rotation))
		{
			m_rotation = pkg.ReadVector3();
		}
		if (IsFlagSet(extraDataFlags, ExtraDataFlags.AnyLow8Bits))
		{
			bool num = IsFlagSet(extraDataFlags, ExtraDataFlags.Connections);
			bool read = IsFlagSet(extraDataFlags, ExtraDataFlags.Floats);
			bool read2 = IsFlagSet(extraDataFlags, ExtraDataFlags.Vec3);
			bool read3 = IsFlagSet(extraDataFlags, ExtraDataFlags.Quaternions);
			bool read4 = IsFlagSet(extraDataFlags, ExtraDataFlags.Ints);
			bool read5 = IsFlagSet(extraDataFlags, ExtraDataFlags.Longs);
			bool read6 = IsFlagSet(extraDataFlags, ExtraDataFlags.Strings);
			bool read7 = IsFlagSet(extraDataFlags, ExtraDataFlags.ByteArrays);
			if (num)
			{
				ZDOExtraData.ConnectionType connectionType = (ZDOExtraData.ConnectionType)pkg.ReadByte();
				ZDOID target = pkg.ReadZDOID();
				ZDOExtraData.SetConnection(m_uid, connectionType, target);
			}
			ZDODataHelper.ReadData(read, pkg, ZDOExtraData.Type.Float, m_uid, pkg.ReadSingle, ZDOExtraData.Add);
			ZDODataHelper.ReadData(read2, pkg, ZDOExtraData.Type.Vec3, m_uid, pkg.ReadVector3, ZDOExtraData.Add);
			ZDODataHelper.ReadData(read3, pkg, ZDOExtraData.Type.Quat, m_uid, pkg.ReadQuaternion, ZDOExtraData.Add);
			ZDODataHelper.ReadData(read4, pkg, ZDOExtraData.Type.Int, m_uid, pkg.ReadInt, ZDOExtraData.Add);
			ZDODataHelper.ReadData(read5, pkg, ZDOExtraData.Type.Long, m_uid, pkg.ReadLong, ZDOExtraData.Add);
			ZDODataHelper.ReadData(read6, pkg, ZDOExtraData.Type.String, m_uid, pkg.ReadString, ZDOExtraData.Add);
			ZDODataHelper.ReadData(read7, pkg, ZDOExtraData.Type.ByteArray, m_uid, pkg.ReadByteArray, ZDOExtraData.Add);
		}
	}

	public void Save(ZPackage pkg)
	{
		ZDOExtraData.GetSaveData(m_uid, out var floats, out var vec3s, out var quats, out var ints, out var longs, out var strings, out var byteArray, out var connectionData);
		ExtraDataFlags dataFlags = ExtraDataFlags.None;
		if (connectionData != null && connectionData.m_type != ZDOExtraData.ConnectionType.None)
		{
			SetFlag(ref dataFlags, ExtraDataFlags.Connections);
		}
		if (floats.Count > 0)
		{
			SetFlag(ref dataFlags, ExtraDataFlags.Floats);
		}
		if (vec3s.Count > 0)
		{
			SetFlag(ref dataFlags, ExtraDataFlags.Vec3);
		}
		if (quats.Count > 0)
		{
			SetFlag(ref dataFlags, ExtraDataFlags.Quaternions);
		}
		if (ints.Count > 0)
		{
			SetFlag(ref dataFlags, ExtraDataFlags.Ints);
		}
		if (longs.Count > 0)
		{
			SetFlag(ref dataFlags, ExtraDataFlags.Longs);
		}
		if (strings.Count > 0)
		{
			SetFlag(ref dataFlags, ExtraDataFlags.Strings);
		}
		if (byteArray.Count > 0)
		{
			SetFlag(ref dataFlags, ExtraDataFlags.ByteArrays);
		}
		bool num = !m_rotation.CloseToZero();
		var (flag, v) = m_position.SmallPosition();
		if (Persistent)
		{
			SetFlag(ref dataFlags, ExtraDataFlags.Persistent);
		}
		if (Distant)
		{
			SetFlag(ref dataFlags, ExtraDataFlags.Distant);
		}
		SetFlag(ref dataFlags, (ExtraDataFlags)((uint)Type << 10));
		if (num)
		{
			SetFlag(ref dataFlags, ExtraDataFlags.Rotation);
		}
		if (flag)
		{
			SetFlag(ref dataFlags, ExtraDataFlags.SmallPosition);
		}
		pkg.Write((ushort)dataFlags);
		if (flag)
		{
			pkg.Write(v);
		}
		else
		{
			pkg.Write(m_position);
		}
		pkg.Write(m_prefab);
		if (num)
		{
			pkg.WriteSmallRotation(m_rotation);
		}
		if (IsFlagSet(dataFlags, ExtraDataFlags.AnyLow8Bits))
		{
			if (IsFlagSet(dataFlags, ExtraDataFlags.Connections))
			{
				pkg.Write((byte)connectionData.m_type);
				pkg.Write(connectionData.m_hash);
			}
			ZDODataHelper.WriteToDisk(pkg, floats, delegate(float value)
			{
				pkg.Write(value);
			});
			ZDODataHelper.WriteToDisk(pkg, vec3s, delegate(Vector3 value)
			{
				pkg.Write(value);
			});
			ZDODataHelper.WriteToDisk(pkg, quats, delegate(Quaternion value)
			{
				pkg.Write(value);
			});
			ZDODataHelper.WriteToDisk(pkg, ints, delegate(int value)
			{
				pkg.Write(value);
			});
			ZDODataHelper.WriteToDisk(pkg, longs, delegate(long value)
			{
				pkg.Write(value);
			});
			ZDODataHelper.WriteToDisk(pkg, strings, delegate(string value)
			{
				pkg.Write(value);
			});
			ZDODataHelper.WriteToDisk(pkg, byteArray, delegate(byte[] value)
			{
				pkg.Write(value);
			});
		}
	}

	private static bool Strip(int key)
	{
		return ZDOHelper.s_stripOldData.Contains(key);
	}

	private static bool StripLong(int key)
	{
		return ZDOHelper.s_stripOldLongData.Contains(key);
	}

	private static bool Strip(int key, long data)
	{
		return StripLong(key);
	}

	private static bool Strip(int key, int data)
	{
		return Strip(key);
	}

	private static bool Strip(int key, Quaternion data)
	{
		return data == Quaternion.identity;
	}

	private static bool Strip(int key, string data)
	{
		return string.IsNullOrEmpty(data);
	}

	private static bool Strip(int key, byte[] data)
	{
		return data.Length == 0;
	}

	private static bool StripConvert(ZDOID zid, int key, long data)
	{
		if (key == ZDOVars.s_SpawnTime__DontUse || key == ZDOVars.s_spawn_time__DontUse)
		{
			ZDOExtraData.Set(zid, ZDOVars.s_spawnTime, data);
			return true;
		}
		return false;
	}

	private static bool StripConvert(ZDOID zid, int key, Vector3 data)
	{
		if (key == ZDOVars.s_SpawnPoint__DontUse)
		{
			ZDOExtraData.Set(zid, ZDOVars.s_spawnPoint, data);
			return true;
		}
		return false;
	}

	public void LoadOldFormat(ZPackage pkg, Version.World version)
	{
		pkg.ReadUInt();
		pkg.ReadUInt();
		Persistent = pkg.ReadBool();
		pkg.ReadLong();
		long timeCreated = pkg.ReadLong();
		ZDOExtraData.SetTimeCreated(m_uid, timeCreated);
		pkg.ReadInt();
		if (version >= Version.World.SupportPriority && version < Version.World.StopSupportPriority)
		{
			pkg.ReadInt();
		}
		if (version >= Version.World.ZDOType)
		{
			Type = (ObjectType)(pkg.ReadSByte() & 3);
		}
		if (version >= Version.World.SupportDistant)
		{
			Distant = pkg.ReadBool();
		}
		if (version < Version.World.SupportZoneSystem2)
		{
			pkg.ReadChar();
			pkg.ReadChar();
		}
		if (version >= Version.World.PrefabInZDO)
		{
			m_prefab = pkg.ReadInt();
		}
		pkg.ReadVector2i();
		m_position = pkg.ReadVector3();
		m_rotation = pkg.ReadQuaternion().eulerAngles;
		int num = pkg.ReadChar();
		if (num > 0)
		{
			for (int i = 0; i < num; i++)
			{
				int hash = pkg.ReadInt();
				float value = pkg.ReadSingle();
				ZDOExtraData.Set(m_uid, hash, value);
			}
		}
		int num2 = pkg.ReadChar();
		if (num2 > 0)
		{
			for (int j = 0; j < num2; j++)
			{
				int num3 = pkg.ReadInt();
				Vector3 vector = pkg.ReadVector3();
				if (!StripConvert(m_uid, num3, vector))
				{
					ZDOExtraData.Set(m_uid, num3, vector);
				}
			}
		}
		int num4 = pkg.ReadChar();
		if (num4 > 0)
		{
			for (int k = 0; k < num4; k++)
			{
				int num5 = pkg.ReadInt();
				Quaternion quaternion = pkg.ReadQuaternion();
				if (!Strip(num5, quaternion))
				{
					ZDOExtraData.Set(m_uid, num5, quaternion);
				}
			}
		}
		int num6 = pkg.ReadChar();
		if (num6 > 0)
		{
			for (int l = 0; l < num6; l++)
			{
				int num7 = pkg.ReadInt();
				int num8 = pkg.ReadInt();
				if (!Strip(num7, num8))
				{
					ZDOExtraData.Set(m_uid, num7, num8);
				}
			}
		}
		int num9 = pkg.ReadChar();
		if (num9 > 0)
		{
			for (int m = 0; m < num9; m++)
			{
				int num10 = pkg.ReadInt();
				long num11 = pkg.ReadLong();
				if (!StripConvert(m_uid, num10, num11))
				{
					ZDOExtraData.Set(m_uid, num10, num11);
				}
			}
		}
		int num12 = pkg.ReadChar();
		if (num12 > 0)
		{
			for (int n = 0; n < num12; n++)
			{
				int num13 = pkg.ReadInt();
				string text = pkg.ReadString();
				if (!Strip(num13, text))
				{
					ZDOExtraData.Set(m_uid, num13, text);
				}
			}
		}
		if (version >= Version.World.SupportByteArray)
		{
			int num14 = pkg.ReadChar();
			if (num14 > 0)
			{
				for (int num15 = 0; num15 < num14; num15++)
				{
					int num16 = pkg.ReadInt();
					byte[] array = pkg.ReadByteArray();
					if (!Strip(num16, array))
					{
						ZDOExtraData.Set(m_uid, num16, array);
					}
				}
			}
		}
		if (version < Version.World.PrefabInZDO)
		{
			m_prefab = GetInt("prefab".GetStableHashCode());
		}
	}

	public static int ReadNumItems(ZPackage pkg, Version.World version)
	{
		if (version < Version.World.NumItems)
		{
			return pkg.ReadByte();
		}
		return pkg.ReadNumItems();
	}

	public static int ReadNumItems(ZPackage pkg)
	{
		return pkg.ReadNumItems();
	}

	public void Load(ZPackage pkg, Version.World version)
	{
		bool flag = version >= Version.World.ChunkedSave;
		m_uid.SetID(++ZDOID.m_loadID);
		OwnerRevision = 0;
		DataRevision = 0u;
		Owned = false;
		Owner = false;
		SaveClone = false;
		ExtraDataFlags extraDataFlags = (ExtraDataFlags)pkg.ReadUShort();
		Persistent = IsFlagSet(extraDataFlags, ExtraDataFlags.Persistent);
		Distant = IsFlagSet(extraDataFlags, ExtraDataFlags.Distant);
		Type = (ObjectType)(((int)extraDataFlags >> 10) & 3);
		bool num = IsFlagSet(extraDataFlags, ExtraDataFlags.SmallPosition);
		bool flag2 = IsFlagSet(extraDataFlags, ExtraDataFlags.Rotation);
		if (!flag)
		{
			pkg.ReadVector2s();
		}
		if (num)
		{
			Vector2s vector2s = pkg.ReadVector2s();
			m_position = new Vector3(vector2s.x, 0f, vector2s.y);
		}
		else
		{
			m_position = pkg.ReadVector3();
		}
		m_prefab = pkg.ReadInt();
		if (flag2)
		{
			m_rotation = (flag ? pkg.ReadSmallRotation() : pkg.ReadVector3());
		}
		if (IsFlagSet(extraDataFlags, ExtraDataFlags.AnyLow8Bits))
		{
			bool num2 = IsFlagSet(extraDataFlags, ExtraDataFlags.Connections);
			bool read = IsFlagSet(extraDataFlags, ExtraDataFlags.Floats);
			bool read2 = IsFlagSet(extraDataFlags, ExtraDataFlags.Vec3);
			bool read3 = IsFlagSet(extraDataFlags, ExtraDataFlags.Quaternions);
			bool read4 = IsFlagSet(extraDataFlags, ExtraDataFlags.Ints);
			bool read5 = IsFlagSet(extraDataFlags, ExtraDataFlags.Longs);
			bool read6 = IsFlagSet(extraDataFlags, ExtraDataFlags.Strings);
			bool read7 = IsFlagSet(extraDataFlags, ExtraDataFlags.ByteArrays);
			if (num2)
			{
				ZDOExtraData.ConnectionType connectionType = (ZDOExtraData.ConnectionType)pkg.ReadByte();
				int hash = pkg.ReadInt();
				ZDOExtraData.SetConnectionData(m_uid, connectionType, hash);
			}
			ZDODataHelper.ReadFromDisk(read, pkg, version, ZDOExtraData.Type.Float, m_uid, pkg.ReadSingle, ZDOExtraData.Add);
			ZDODataHelper.ReadFromDisk(read2, pkg, version, ZDOExtraData.Type.Vec3, m_uid, pkg.ReadVector3, ZDOExtraData.Add);
			ZDODataHelper.ReadFromDisk(read3, pkg, version, ZDOExtraData.Type.Quat, m_uid, pkg.ReadQuaternion, ZDOExtraData.Add);
			ZDODataHelper.ReadFromDisk(read4, pkg, version, ZDOExtraData.Type.Int, m_uid, pkg.ReadInt, ZDOExtraData.Add);
			ZDODataHelper.ReadFromDisk(read5, pkg, version, ZDOExtraData.Type.Long, m_uid, pkg.ReadLong, ZDOExtraData.Add);
			ZDODataHelper.ReadFromDisk(read6, pkg, version, ZDOExtraData.Type.String, m_uid, pkg.ReadString, ZDOExtraData.Add);
			ZDODataHelper.ReadFromDisk(read7, pkg, version, ZDOExtraData.Type.ByteArray, m_uid, pkg.ReadByteArray, ZDOExtraData.Add);
		}
	}

	public long GetOwner()
	{
		if (!Owned)
		{
			return 0L;
		}
		return ZDOExtraData.GetOwner(m_uid);
	}

	public bool IsOwner()
	{
		return Owner;
	}

	public bool HasOwner()
	{
		return Owned;
	}

	public void SetOwner(long uid)
	{
		if (ZDOExtraData.GetOwner(m_uid) != uid)
		{
			SetOwnerInternal(uid);
			IncreaseOwnerRevision();
		}
	}

	public void SetOwnerInternal(long uid)
	{
		if (uid == 0L)
		{
			ZDOExtraData.ReleaseOwner(m_uid);
			Owned = false;
			Owner = false;
		}
		else
		{
			ushort ownerKey = ZDOID.AddUser(uid);
			ZDOExtraData.SetOwner(m_uid, ownerKey);
			Owned = true;
			Owner = uid == ZDOMan.GetSessionID();
		}
	}
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;

public class ZDOMan
{
	private class ZDOPeer
	{
		public struct PeerZDOInfo
		{
			public readonly uint m_dataRevision;

			public readonly ushort m_ownerRevision;

			public readonly float m_syncTime;

			public PeerZDOInfo(uint dataRevision, ushort ownerRevision, float syncTime)
			{
				m_dataRevision = dataRevision;
				m_ownerRevision = ownerRevision;
				m_syncTime = syncTime;
			}
		}

		public ZNetPeer m_peer;

		public readonly Dictionary<ZDOID, PeerZDOInfo> m_zdos = new Dictionary<ZDOID, PeerZDOInfo>();

		public readonly HashSet<ZDOID> m_forceSend = new HashSet<ZDOID>();

		public readonly HashSet<ZDOID> m_invalidSector = new HashSet<ZDOID>();

		public int m_sendIndex;

		public void ZDOSectorInvalidated(ZDO zdo)
		{
			if (zdo.GetOwner() != m_peer.m_uid && m_zdos.ContainsKey(zdo.m_uid) && !ZNetScene.InActiveArea(zdo.GetPosition(), m_peer.GetRefPos()))
			{
				m_invalidSector.Add(zdo.m_uid);
				m_zdos.Remove(zdo.m_uid);
			}
		}

		public void ForceSendZDO(ZDOID id)
		{
			m_forceSend.Add(id);
		}

		public bool ShouldSend(ZDO zdo)
		{
			if (m_zdos.TryGetValue(zdo.m_uid, out var value))
			{
				if (zdo.OwnerRevision <= value.m_ownerRevision)
				{
					return zdo.DataRevision > value.m_dataRevision;
				}
				return true;
			}
			return true;
		}
	}

	private class SaveData
	{
		public List<Tuple<ZoneSystem.ChunkIndex, List<ZDO>>> m_objectsByChunk = new List<Tuple<ZoneSystem.ChunkIndex, List<ZDO>>>();

		public int m_numFiles;

		public int m_dirtyFiles;
	}

	private enum SaveState
	{
		NotSaving,
		CurrentlySaving,
		NUM_STATES
	}

	public Action<ZDO> m_onZDODestroyed;

	private readonly long m_sessionID = Utils.GenerateUID();

	private uint m_nextUid = 1u;

	private readonly Dictionary<ZoneSystem.SectorIndex, List<ZDO>> m_portalObjects = new Dictionary<ZoneSystem.SectorIndex, List<ZDO>>();

	private readonly List<ZDOPeer> m_peers = new List<ZDOPeer>();

	private readonly Dictionary<ZDOID, long> m_deadZDOs = new Dictionary<ZDOID, long>();

	private readonly List<ZDOID> m_destroySendList = new List<ZDOID>();

	private readonly HashSet<ZDOID> m_clientChangeQueue = new HashSet<ZDOID>();

	private readonly Dictionary<ZDOID, ZDO> m_objectsByID = new Dictionary<ZDOID, ZDO>();

	private ChunkSaveMapping m_chunkSaveMapping = new ChunkSaveMapping();

	private ChunkSaveMapping m_chunkSaveMappingCurrent;

	private SaveState m_currentSaveState;

	private List<ZDO>[] m_objectsBySector;

	private readonly HashSet<ZoneSystem.ChunkIndex>[] m_dirtyChunks = new HashSet<ZoneSystem.ChunkIndex>[2]
	{
		new HashSet<ZoneSystem.ChunkIndex>(),
		new HashSet<ZoneSystem.ChunkIndex>()
	};

	private bool[] m_dirtyPortalObjects = new bool[2];

	private const float c_SendFPS = 20f;

	private readonly int m_width;

	private float m_sendTimer;

	private float m_releaseZDOTimer;

	private int m_zdosSent;

	private int m_zdosRecv;

	private int m_zdosSentLastSec;

	private int m_zdosRecvLastSec;

	private float m_statTimer;

	private SaveData m_saveData;

	private int m_nextSendPeer = -1;

	private readonly List<ZDO> m_tempToSync = new List<ZDO>();

	private readonly List<ZDO> m_tempToSyncDistant = new List<ZDO>();

	private readonly List<ZDO> m_tempNearObjects = new List<ZDO>();

	private readonly List<ZDOID> m_tempRemoveList = new List<ZDOID>();

	private readonly List<ZDO> m_tempSectorObjects = new List<ZDO>();

	private readonly HashSet<ZoneSystem.SectorIndex> m_visitedSectorIndices = new HashSet<ZoneSystem.SectorIndex>();

	private static ZDOMan s_instance;

	private static long s_compareReceiver = 0L;

	private const int c_MaxNumberOfZDOsPerBiggerChunks = 100000;

	private bool? m_endSave;

	private static readonly List<int> s_brokenPrefabsToFilterOut = new List<int> { 1332933305, -1334479845 };

	public static ZDOMan instance => s_instance;

	public int NumSaveFiles
	{
		get
		{
			if (m_saveData != null)
			{
				return m_saveData.m_numFiles;
			}
			return 0;
		}
	}

	public int NumDirtySaveFiles
	{
		get
		{
			if (m_saveData != null)
			{
				return m_saveData.m_dirtyFiles;
			}
			return 0;
		}
	}

	private HashSet<ZoneSystem.ChunkIndex> DirtyChunks => m_dirtyChunks[(int)m_currentSaveState];

	private bool DirtyPortalObjects
	{
		get
		{
			return m_dirtyPortalObjects[(int)m_currentSaveState];
		}
		set
		{
			m_dirtyPortalObjects[(int)m_currentSaveState] = value;
		}
	}

	public ZDOMan(int width)
	{
		SaveSystem.SetSaveNumber(0u);
		s_instance = this;
		ZRoutedRpc.instance.Register<ZPackage>("DestroyZDO", RPC_DestroyZDO);
		ZRoutedRpc.instance.Register<ZDOID>("RequestZDO", RPC_RequestZDO);
		m_width = width;
		ZDOID.Reset();
		ResetSectorArray();
		ZDOExtraData.Init();
	}

	private void ResetSectorArray()
	{
		m_objectsBySector = new List<ZDO>[m_width * m_width];
	}

	private void ResetBeforeLoad()
	{
		m_objectsByID.Clear();
		m_portalObjects.Clear();
		ResetSectorArray();
		m_currentSaveState = SaveState.NotSaving;
		ZDOExtraData.Init();
		m_endSave = null;
	}

	public void ShutDown()
	{
		if (!ZNet.instance.IsServer())
		{
			FlushClientObjects();
		}
		ZDOPool.Release(m_objectsByID);
		m_objectsByID.Clear();
		m_portalObjects.Clear();
		m_tempToSync.Clear();
		m_tempToSyncDistant.Clear();
		m_tempNearObjects.Clear();
		m_tempRemoveList.Clear();
		m_peers.Clear();
		ResetSectorArray();
		ZDOExtraData.Reset();
		Game.instance.CollectResources();
	}

	public void PrepareSave()
	{
		m_saveData = new SaveData();
		m_saveData.m_objectsByChunk = GetSaveClonePerChunk();
		BeginSave();
		Stopwatch stopwatch = Stopwatch.StartNew();
		ZDOExtraData.PrepareSave();
		ZLog.Log("PrepareSave: ZDOExtraData.PrepareSave done [" + stopwatch.ElapsedMilliseconds.ToString("N0") + "ms]");
	}

	private void BeginSave()
	{
		m_currentSaveState = SaveState.CurrentlySaving;
	}

	private void UpdateSaveState()
	{
		if (m_endSave.HasValue)
		{
			if (m_endSave.Value)
			{
				m_dirtyChunks[0].Clear();
				m_dirtyChunks[0].UnionWith(m_dirtyChunks[1]);
				m_dirtyChunks[1].Clear();
				m_dirtyPortalObjects[0] = m_dirtyPortalObjects[1];
				m_dirtyPortalObjects[1] = false;
			}
			else
			{
				m_dirtyChunks[0].UnionWith(m_dirtyChunks[1]);
				m_dirtyChunks[1].Clear();
				ref bool reference = ref m_dirtyPortalObjects[0];
				reference |= m_dirtyPortalObjects[1];
				m_dirtyPortalObjects[1] = false;
			}
			m_currentSaveState = SaveState.NotSaving;
			m_endSave = null;
		}
	}

	public void EndSave(bool saveState)
	{
		m_endSave = saveState;
	}

	private bool SaveChunk(List<ZDO> zdos, string fullPath, FileHelpers.FileSource fileSource)
	{
		SaveFileHelper.SaveWrite saveWrite = SaveFileHelper.CreateZPackageForWriting(fullPath, fileSource);
		saveWrite.Pkg.Write((short)41);
		saveWrite.Pkg.Write(zdos.Count);
		foreach (ZDO zdo in zdos)
		{
			zdo.Save(saveWrite.Pkg);
		}
		return saveWrite.Finish();
	}

	public bool SaveChunks(string path, FileHelpers.FileSource fileSource)
	{
		m_chunkSaveMappingCurrent = m_chunkSaveMapping.Clone();
		ZoneSystem.ChunkIndex item;
		List<ZDO> item2;
		foreach (Tuple<ZoneSystem.ChunkIndex, List<ZDO>> item3 in m_saveData.m_objectsByChunk)
		{
			item3.Deconstruct(out item, out item2);
			ZoneSystem.ChunkIndex chunkIndex = item;
			m_chunkSaveMappingCurrent.CreateOrUpdate(chunkIndex);
		}
		bool flag = true;
		foreach (Tuple<ZoneSystem.ChunkIndex, List<ZDO>> item4 in m_saveData.m_objectsByChunk)
		{
			item4.Deconstruct(out item, out item2);
			ZoneSystem.ChunkIndex chunkIndex2 = item;
			List<ZDO> list = item2;
			string chunkFilename = m_chunkSaveMappingCurrent.GetChunkFilename(chunkIndex2);
			if (string.IsNullOrEmpty(chunkFilename))
			{
				UnityEngine.Debug.LogError("This shouldn't happen: GetChunkFilename returned null");
				UnityEngine.Debug.Break();
				continue;
			}
			string fullPath = path + chunkFilename;
			m_chunkSaveMappingCurrent.Get(chunkIndex2).m_numZDOs = list.Count;
			flag = SaveChunk(list, fullPath, fileSource);
			if (flag)
			{
				continue;
			}
			break;
		}
		return flag && m_chunkSaveMappingCurrent.Save(path, fileSource);
	}

	public void DeleteCurrentChunksAndRevert(string path, FileHelpers.FileSource fileSource)
	{
		if (m_chunkSaveMappingCurrent == null)
		{
			return;
		}
		foreach (Tuple<ZoneSystem.ChunkIndex, List<ZDO>> item3 in m_saveData.m_objectsByChunk)
		{
			item3.Deconstruct(out var item, out var _);
			ZoneSystem.ChunkIndex chunkIndex = item;
			string chunkFilename = m_chunkSaveMappingCurrent.GetChunkFilename(chunkIndex);
			if (string.IsNullOrEmpty(chunkFilename))
			{
				ushort chunk = chunkIndex.Chunk;
				UnityEngine.Debug.LogError("DeleteCurrentChunksAndRevert, failed to get ChunkInfo for Chunk: " + chunk);
			}
			else
			{
				FileHelpers.Delete(path + chunkFilename, fileSource);
				m_chunkSaveMappingCurrent.RevertChunkVersion(chunkIndex);
			}
		}
	}

	public void DeleteOldChunks(string path, FileHelpers.FileSource fileSource)
	{
		if (m_chunkSaveMapping.GetNumChunks() > 0)
		{
			foreach (Tuple<ZoneSystem.ChunkIndex, List<ZDO>> item3 in m_saveData.m_objectsByChunk)
			{
				item3.Deconstruct(out var item, out var _);
				ZoneSystem.ChunkIndex chunkIndex = item;
				string chunkOldFilename = m_chunkSaveMapping.GetChunkOldFilename(chunkIndex);
				if (!string.IsNullOrEmpty(chunkOldFilename))
				{
					FileHelpers.Delete(path + chunkOldFilename, fileSource);
				}
			}
		}
		m_chunkSaveMapping = m_chunkSaveMappingCurrent;
		m_chunkSaveMappingCurrent = null;
	}

	public void SaveCleanup()
	{
		foreach (var (_, list2) in m_saveData.m_objectsByChunk)
		{
			foreach (ZDO item in list2)
			{
				item.Reset();
			}
			list2.Clear();
		}
		m_saveData.m_objectsByChunk.Clear();
		m_saveData = null;
		ZDOExtraData.ClearSave();
		m_chunkSaveMappingCurrent = null;
	}

	private static void FilterZDO(bool newVersion, ZDO zdo, ref List<ZDO> zdos, ref List<ZDO> warningZDOs, ref List<ZDO> brokenZDOs)
	{
		if (newVersion)
		{
			if (s_brokenPrefabsToFilterOut.Contains(zdo.GetPrefab()))
			{
				brokenZDOs.Add(zdo);
				return;
			}
			if (!ZNetScene.instance.HasPrefab(zdo.GetPrefab()))
			{
				warningZDOs.Add(zdo);
			}
		}
		zdos.Add(zdo);
	}

	private static void WarnAndRemoveBrokenZDOs(List<ZDO> warningZDOs, List<ZDO> brokenZDOs, int totalNumZDOs, int numZDOs)
	{
		Dictionary<int, int> dictionary = new Dictionary<int, int>();
		int key;
		int value;
		if (warningZDOs.Count > 0)
		{
			value = warningZDOs.Count;
			ZLog.LogWarning("Found " + value + " ZDOs with unknown prefabs. Will load anyway.");
			foreach (ZDO warningZDO in warningZDOs)
			{
				int prefab = warningZDO.GetPrefab();
				if (!dictionary.TryAdd(prefab, 1))
				{
					value = prefab;
					key = dictionary[value]++;
				}
			}
			foreach (KeyValuePair<int, int> item in dictionary)
			{
				item.Deconstruct(out key, out value);
				int num = key;
				int num2 = value;
				ZLog.LogWarning("    Hash " + num + " appeared " + num2 + " times.");
			}
		}
		if (brokenZDOs.Count <= 0)
		{
			return;
		}
		string[] obj = new string[7] { "Found ", null, null, null, null, null, null };
		value = brokenZDOs.Count;
		obj[1] = value.ToString();
		obj[2] = " ZDOs with prefabs not supported. Removing. ";
		obj[3] = totalNumZDOs.ToString();
		obj[4] = " => ";
		obj[5] = numZDOs.ToString();
		obj[6] = " ZDOs loaded.";
		ZLog.LogError(string.Concat(obj));
		dictionary.Clear();
		foreach (ZDO brokenZDO in brokenZDOs)
		{
			int prefab2 = brokenZDO.GetPrefab();
			if (!dictionary.TryAdd(prefab2, 1))
			{
				value = prefab2;
				key = dictionary[value]++;
			}
			ZDOPool.Release(brokenZDO);
		}
		foreach (KeyValuePair<int, int> item2 in dictionary)
		{
			item2.Deconstruct(out key, out value);
			int num3 = key;
			int num4 = value;
			ZLog.LogError("    Hash " + num3 + " filtered out " + num4 + " times.");
		}
	}

	public void LoadChunks(string saveDirectory, FileHelpers.FileSource fileSource, Version.World version)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		ZDOPool.Release(m_objectsByID);
		ResetBeforeLoad();
		m_chunkSaveMapping = new ChunkSaveMapping();
		m_chunkSaveMapping.Load(saveDirectory, fileSource);
		int numZDOs = m_chunkSaveMapping.GetNumZDOs();
		int numChunks = m_chunkSaveMapping.GetNumChunks();
		string[] obj = new string[11]
		{
			"ZDOMan.LoadChunks - Starting to load ",
			numZDOs.ToString("N0"),
			" zdos from ",
			numChunks.ToString("N0"),
			" Chunks. SessionID: ",
			null,
			null,
			null,
			null,
			null,
			null
		};
		long sessionID = m_sessionID;
		obj[5] = sessionID.ToString();
		obj[6] = ", WorldVersion: ";
		int num = (int)version;
		obj[7] = num.ToString();
		obj[8] = " [";
		obj[9] = version.ToString();
		obj[10] = "]";
		ZLog.Log(string.Concat(obj));
		List<ZDO> zdos = new List<ZDO>();
		zdos.Capacity = numZDOs;
		List<ZDO> brokenZDOs = new List<ZDO>();
		List<ZDO> warningZDOs = new List<ZDO>();
		List<string> chunkFilesForSave = SaveSystem.GetChunkFilesForSave(saveDirectory);
		foreach (KeyValuePair<ZoneSystem.ChunkIndex, ChunkSaveMapping.ChunkInfo> chunk2 in m_chunkSaveMapping.Chunks)
		{
			chunk2.Deconstruct(out var key, out var value);
			ZoneSystem.ChunkIndex chunkIndex = key;
			ChunkSaveMapping.ChunkInfo chunkInfo = value;
			string chunkFilename = m_chunkSaveMapping.GetChunkFilename(chunkIndex);
			if (string.IsNullOrEmpty(chunkFilename))
			{
				string[] obj2 = new string[6] { "Error: LoadChunks failed to load chunk: ", null, null, null, null, null };
				ushort chunk = chunkIndex.Chunk;
				obj2[1] = chunk.ToString();
				obj2[2] = ", size: ";
				byte chunkSize = chunkIndex.m_chunkSize;
				obj2[3] = chunkSize.ToString();
				obj2[4] = ", version: ";
				obj2[5] = chunkInfo.m_version.ToString();
				UnityEngine.Debug.LogError(string.Concat(obj2));
				continue;
			}
			string path = saveDirectory + chunkFilename;
			using SaveFileHelper.SaveReader saveReader = SaveFileHelper.CreateZPackageForReading(path, fileSource);
			ZPackage pkg = saveReader.Pkg;
			Version.World world = (Version.World)pkg.ReadShort();
			int num2 = pkg.ReadInt();
			bool newVersion = world != version;
			chunkFilesForSave.Remove(path.EnsureSlashes());
			if (chunkIndex.Equals(ZoneSystem.ChunkPortal))
			{
				for (int i = 0; i < num2; i++)
				{
					ZDO zDO = ZDOPool.Create();
					zDO.Load(pkg, world);
					m_portalObjects.AddItemToList(zDO.GetSectorIndex(), zDO);
					m_objectsByID.Add(zDO.m_uid, zDO);
				}
			}
			else
			{
				for (int j = 0; j < num2; j++)
				{
					ZDO zDO2 = ZDOPool.Create();
					zDO2.Load(pkg, world);
					FilterZDO(newVersion, zDO2, ref zdos, ref warningZDOs, ref brokenZDOs);
				}
			}
		}
		ZLog.Log("ZDOMan.LoadChunks => Loading ZDOs done [" + stopwatch.ElapsedMilliseconds.ToString("N0") + "ms]");
		foreach (string item in chunkFilesForSave)
		{
			ZLog.LogError("Removing orphan CHUNK file: " + item);
			FileHelpers.Delete(item, fileSource);
		}
		WarnAndRemoveBrokenZDOs(warningZDOs, brokenZDOs, numZDOs, zdos.Count);
		Stopwatch stopwatch2 = Stopwatch.StartNew();
		m_objectsByID.EnsureCapacity(m_objectsByID.Count + zdos.Count);
		foreach (ZDO item2 in zdos)
		{
			m_objectsByID.Add(item2.m_uid, item2);
		}
		ZLog.Log("ZDOMan.LoadChunks => Adding ZDOs to Dictionary done [" + stopwatch2.ElapsedMilliseconds.ToString("N0") + "ms]");
		stopwatch2.Restart();
		foreach (ZDO item3 in zdos)
		{
			InitialAddToSector(item3, ZoneSystem.GetSectorIndex(item3.GetPosition()));
		}
		ZLog.Log("ZDOMan.LoadChunks => Adding ZDOs to Sectors done [" + stopwatch2.ElapsedMilliseconds.ToString("N0") + "ms]");
		stopwatch2.Restart();
		ConnectPortals();
		ZLog.Log("ZDOMan.LoadChunks => Connecting Portals done [" + stopwatch2.ElapsedMilliseconds.ToString("N0") + "ms]");
		stopwatch2.Restart();
		ConnectSpawners();
		ZLog.Log("ZDOMan.LoadChunks => Connecting Spawners done [" + stopwatch2.ElapsedMilliseconds.ToString("N0") + "ms]");
		stopwatch2.Restart();
		ConnectSyncTransforms();
		ZLog.Log("ZDOMan.LoadChunks => Connecting SyncTransforms done [" + stopwatch2.ElapsedMilliseconds.ToString("N0") + "ms]");
		Game.instance.ConnectPortals();
		m_deadZDOs.Clear();
		m_nextUid = ZDOID.m_loadID + 1;
		DirtyChunks.Clear();
		DirtyPortalObjects = false;
		ZLog.Log("ZDOMan.LoadChunks done [" + stopwatch.ElapsedMilliseconds.ToString("N0") + "ms]");
	}

	public void Load(BinaryReader reader, Version.World version)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		reader.ReadInt64();
		uint nextUid = reader.ReadUInt32();
		int num = reader.ReadInt32();
		ZDOPool.Release(m_objectsByID);
		ResetBeforeLoad();
		DirtyPortalObjects = true;
		m_chunkSaveMapping = new ChunkSaveMapping();
		string[] obj = new string[9]
		{
			"ZDOMan.Load - Starting to load ",
			num.ToString("N0"),
			" zdos. SessionID: ",
			null,
			null,
			null,
			null,
			null,
			null
		};
		long sessionID = m_sessionID;
		obj[3] = sessionID.ToString();
		obj[4] = ", WorldVersion: ";
		int num2 = (int)version;
		obj[5] = num2.ToString();
		obj[6] = " [";
		obj[7] = version.ToString();
		obj[8] = "]";
		ZLog.Log(string.Concat(obj));
		List<ZDO> zdos = new List<ZDO>();
		zdos.Capacity = num;
		List<ZDO> brokenZDOs = new List<ZDO>();
		List<ZDO> warningZDOs = new List<ZDO>();
		Stopwatch stopwatch2 = Stopwatch.StartNew();
		ZPackage zPackage = new ZPackage();
		if (version < Version.World.NewSaveFormat)
		{
			for (int i = 0; i < num; i++)
			{
				ZDO zDO = ZDOPool.Create();
				zDO.m_uid = new ZDOID(reader);
				int count = reader.ReadInt32();
				byte[] data = reader.ReadBytes(count);
				zPackage.Load(data);
				zDO.LoadOldFormat(zPackage, version);
				zDO.SetOwner(0L);
				if (Game.instance.PortalPrefabHash.Contains(zDO.GetPrefab()))
				{
					m_objectsByID.Add(zDO.m_uid, zDO);
					m_portalObjects.AddItemToList(zDO.GetSectorIndex(), zDO);
				}
				else
				{
					FilterZDO(newVersion: true, zDO, ref zdos, ref warningZDOs, ref brokenZDOs);
				}
			}
			ZLog.Log("ZDOMan.Load :: Loading old ZDOs done [" + stopwatch2.ElapsedMilliseconds.ToString("N0") + "ms]");
		}
		else
		{
			zPackage.SetReader(reader);
			for (int j = 0; j < num; j++)
			{
				ZDO zDO2 = ZDOPool.Create();
				zDO2.Load(zPackage, version);
				if (Game.instance.PortalPrefabHash.Contains(zDO2.GetPrefab()))
				{
					m_objectsByID.Add(zDO2.m_uid, zDO2);
					m_portalObjects.AddItemToList(zDO2.GetSectorIndex(), zDO2);
				}
				else
				{
					FilterZDO(newVersion: true, zDO2, ref zdos, ref warningZDOs, ref brokenZDOs);
				}
			}
			nextUid = (uint)(zdos.Count + 1);
			ZLog.Log("ZDOMan.Load :: Loading ZDOs done [" + stopwatch2.ElapsedMilliseconds.ToString("N0") + "ms]");
		}
		WarnAndRemoveBrokenZDOs(warningZDOs, brokenZDOs, num, zdos.Count);
		ZLog.Log("Adding to Dictionary");
		foreach (ZDO item in zdos)
		{
			m_objectsByID.Add(item.m_uid, item);
		}
		ZLog.Log("Adding to Sectors");
		foreach (ZDO item2 in zdos)
		{
			AddToSector(item2, item2.GetSectorIndex());
		}
		if (version < Version.World.NewSaveFormat)
		{
			stopwatch2.Restart();
			ZLog.Log("Converting Ships & Fishing-rods ownership");
			ConvertOwnerships(zdos);
			ZLog.Log("Converting & mapping CreationTime");
			ConvertCreationTime(zdos);
			ZLog.Log("Converting portals");
			ConvertPortals();
			ZLog.Log("Converting spawners");
			ConvertSpawners();
			ZLog.Log("Converting ZSyncTransforms");
			ConvertSyncTransforms();
			ZLog.Log("Converting ItemSeeds");
			ConvertSeed();
			ZLog.Log("Converting Dungeons");
			ConvertDungeonRooms(zdos);
			ZLog.Log("ZDOMan.Load :: Converting old data done [" + stopwatch2.ElapsedMilliseconds.ToString("N0") + "ms]");
		}
		if (version < Version.World.ChunkedSave)
		{
			stopwatch2.Restart();
			ZLog.Log("Converting inventories & containers");
			ConvertContainers(zdos, version);
			ZLog.Log("Converting prefab-strings");
			ConvertPrefabStrings(zdos);
			ZLog.Log("Removing obsolete data");
			RemoveObsoleteData(zdos);
			ZLog.Log("ZDOMan.Load :: Converting/filtering data done [" + stopwatch2.ElapsedMilliseconds.ToString("N0") + "ms]");
		}
		if (version >= Version.World.NewSaveFormat)
		{
			stopwatch2.Restart();
			ZLog.Log("Connecting Portals, Spawners & ZSyncTransforms");
			ConnectPortals();
			ConnectSpawners();
			ConnectSyncTransforms();
			ZLog.Log("ZDOMan.Load :: Connecting ZDOs done [" + stopwatch2.ElapsedMilliseconds.ToString("N0") + "ms]");
		}
		Game.instance.ConnectPortals();
		m_deadZDOs.Clear();
		if (version < Version.World.NewSaveFormat)
		{
			int num3 = reader.ReadInt32();
			for (int k = 0; k < num3; k++)
			{
				reader.ReadInt64();
				reader.ReadUInt32();
				reader.ReadInt64();
			}
		}
		m_nextUid = nextUid;
		ZLog.Log("ZDOMan.Load is done [" + stopwatch.ElapsedMilliseconds.ToString("N0") + "ms]");
	}

	public ZDO CreateNewZDO(Vector3 position, int prefabHash)
	{
		ZDOID zDOID = new ZDOID(m_sessionID, m_nextUid++);
		while (GetZDO(zDOID) != null)
		{
			zDOID = new ZDOID(m_sessionID, m_nextUid++);
		}
		return CreateNewZDO(zDOID, position, prefabHash);
	}

	private ZDO CreateNewZDO(ZDOID uid, Vector3 position, int prefabHashIn = 0)
	{
		ZDO zDO = ZDOPool.Create(uid, position);
		zDO.SetOwnerInternal(m_sessionID);
		int prefabHash = ((prefabHashIn != 0) ? prefabHashIn : zDO.GetPrefab());
		AddIfPortal(zDO, prefabHash);
		m_objectsByID.Add(uid, zDO);
		return zDO;
	}

	public void SetDirtySector(ZDO zdo)
	{
		if (zdo.Persistent)
		{
			SetDirtyChunks(zdo.GetSectorIndex());
		}
	}

	public void SetDirtyPortals()
	{
		DirtyPortalObjects = true;
	}

	private void SetDirtyChunks(ZoneSystem.SectorIndex sectorIndex)
	{
		ZoneSystem.ChunkIndex zonesChunk = ZoneSystem.GetZonesChunk(sectorIndex);
		DirtyChunks.Add(zonesChunk);
		zonesChunk = ZoneSystem.ChunkIndexFromIndexAndSize(zonesChunk, 1);
		if (DirtyChunks.Add(zonesChunk))
		{
			zonesChunk = ZoneSystem.ChunkIndexFromIndexAndSize(zonesChunk, 2);
			if (DirtyChunks.Add(zonesChunk))
			{
				zonesChunk = ZoneSystem.ChunkIndexFromIndexAndSize(zonesChunk, 3);
				DirtyChunks.Add(zonesChunk);
			}
		}
	}

	public void AddToSector(ZDO zdo, ZoneSystem.SectorIndex sectorIndex)
	{
		if (zdo.Persistent)
		{
			SetDirtyChunks(sectorIndex);
		}
		Utils.AddItemToList(ref m_objectsBySector[sectorIndex.Sector], zdo);
	}

	private void InitialAddToSector(ZDO zdo, ZoneSystem.SectorIndex sectorIndex)
	{
		Utils.AddItemToList(ref m_objectsBySector[sectorIndex.Sector], zdo);
	}

	public void ZDOSectorInvalidated(ZDO zdo)
	{
		foreach (ZDOPeer peer in m_peers)
		{
			peer.ZDOSectorInvalidated(zdo);
		}
	}

	public void RemoveFromSector(ZDO zdo, ZoneSystem.SectorIndex sectorIndex)
	{
		if (zdo.Persistent)
		{
			SetDirtyChunks(sectorIndex);
		}
		m_objectsBySector[sectorIndex.Sector]?.Remove(zdo);
	}

	public ZDO GetZDO(ZDOID id)
	{
		if (id == ZDOID.None)
		{
			return null;
		}
		if (m_objectsByID.TryGetValue(id, out var value))
		{
			return value;
		}
		return null;
	}

	public void AddPeer(ZNetPeer netPeer)
	{
		ZDOPeer zDOPeer = new ZDOPeer();
		zDOPeer.m_peer = netPeer;
		m_peers.Add(zDOPeer);
		zDOPeer.m_peer.m_rpc.Register<ZPackage>("ZDOData", RPC_ZDOData);
	}

	public void RemovePeer(ZNetPeer netPeer)
	{
		ZDOPeer zDOPeer = FindPeer(netPeer);
		if (zDOPeer != null)
		{
			m_peers.Remove(zDOPeer);
			if (ZNet.instance.IsServer())
			{
				RemoveOrphanNonPersistentZDOS();
			}
		}
	}

	private ZDOPeer FindPeer(ZNetPeer netPeer)
	{
		foreach (ZDOPeer peer in m_peers)
		{
			if (peer.m_peer == netPeer)
			{
				return peer;
			}
		}
		return null;
	}

	private ZDOPeer FindPeer(ZRpc rpc)
	{
		foreach (ZDOPeer peer in m_peers)
		{
			if (peer.m_peer.m_rpc == rpc)
			{
				return peer;
			}
		}
		return null;
	}

	public void Update(float dt)
	{
		UpdateSaveState();
		if (ZNet.instance.IsServer())
		{
			ReleaseZDOS(dt);
		}
		SendZDOToPeers2(dt);
		SendDestroyed();
		UpdateStats(dt);
	}

	private void UpdateStats(float dt)
	{
		m_statTimer += dt;
		if (m_statTimer >= 1f)
		{
			m_statTimer = 0f;
			m_zdosSentLastSec = m_zdosSent;
			m_zdosRecvLastSec = m_zdosRecv;
			m_zdosRecv = 0;
			m_zdosSent = 0;
		}
	}

	private void SendZDOToPeers2(float dt)
	{
		if (m_peers.Count == 0)
		{
			return;
		}
		m_sendTimer += dt;
		if (m_nextSendPeer < 0)
		{
			if (m_sendTimer > 0.05f)
			{
				m_nextSendPeer = 0;
				m_sendTimer = 0f;
			}
			return;
		}
		if (m_nextSendPeer < m_peers.Count)
		{
			SendZDOs(m_peers[m_nextSendPeer], flush: false);
		}
		m_nextSendPeer++;
		if (m_nextSendPeer >= m_peers.Count)
		{
			m_nextSendPeer = -1;
		}
	}

	private void FlushClientObjects()
	{
		foreach (ZDOPeer peer in m_peers)
		{
			SendAllZDOs(peer);
		}
	}

	private void ReleaseZDOS(float dt)
	{
		m_releaseZDOTimer += dt;
		if (!(m_releaseZDOTimer > 2f))
		{
			return;
		}
		m_releaseZDOTimer = 0f;
		ReleaseNearbyZDOS(ZNet.instance.GetReferencePosition(), m_sessionID);
		foreach (ZDOPeer peer in m_peers)
		{
			ReleaseNearbyZDOS(peer.m_peer.m_refPos, peer.m_peer.m_uid);
		}
	}

	private bool IsInPeerActiveArea(Vector3 point, long uid)
	{
		if (uid == m_sessionID)
		{
			return ZNetScene.InActiveArea(point, ZNet.instance.GetReferencePosition());
		}
		ZNetPeer peer = ZNet.instance.GetPeer(uid);
		if (peer == null)
		{
			return false;
		}
		return ZNetScene.InActiveArea(point, peer.GetRefPos());
	}

	private void ReleaseNearbyZDOS(Vector3 refPosition, long uid)
	{
		Vector2s zone = ZoneSystem.GetZone(refPosition);
		SimulationDistance syncedSimulationDistance = ZNet.instance.GetSyncedSimulationDistance();
		m_tempNearObjects.Clear();
		SimulationDistance simulationDistance = new SimulationDistance(syncedSimulationDistance.NearSimulationDistance, 0, syncedSimulationDistance.IsClassic);
		FindSectorObjects(zone, simulationDistance, m_tempNearObjects);
		foreach (ZDO tempNearObject in m_tempNearObjects)
		{
			if (!tempNearObject.Persistent)
			{
				continue;
			}
			Vector3 position = tempNearObject.GetPosition();
			if (tempNearObject.GetOwner() == uid)
			{
				if (!ZNetScene.InActiveArea(position, zone))
				{
					tempNearObject.SetOwner(0L);
				}
			}
			else if ((!tempNearObject.HasOwner() || !IsInPeerActiveArea(position, tempNearObject.GetOwner())) && ZNetScene.InActiveArea(position, zone))
			{
				tempNearObject.SetOwner(uid);
			}
		}
	}

	public void DestroyZDO(ZDO zdo)
	{
		if (zdo.IsOwner())
		{
			m_destroySendList.Add(zdo.m_uid);
		}
	}

	private void SendDestroyed()
	{
		if (m_destroySendList.Count == 0)
		{
			return;
		}
		ZPackage zPackage = new ZPackage();
		zPackage.Write(m_destroySendList.Count);
		foreach (ZDOID destroySend in m_destroySendList)
		{
			zPackage.Write(destroySend);
		}
		m_destroySendList.Clear();
		ZRoutedRpc.instance.InvokeRoutedRPC(0L, "DestroyZDO", zPackage);
	}

	private void RPC_DestroyZDO(long sender, ZPackage pkg)
	{
		int num = pkg.ReadInt();
		for (int i = 0; i < num; i++)
		{
			ZDOID uid = pkg.ReadZDOID();
			HandleDestroyedZDO(uid);
		}
	}

	private void HandleDestroyedZDO(ZDOID uid)
	{
		if (uid.UserID == m_sessionID && uid.ID >= m_nextUid)
		{
			m_nextUid = uid.ID + 1;
		}
		ZDO zDO = GetZDO(uid);
		if (zDO == null)
		{
			return;
		}
		ZoneSystem.SectorIndex sectorIndex = zDO.GetSectorIndex();
		if (m_onZDODestroyed != null)
		{
			m_onZDODestroyed(zDO);
		}
		RemoveFromSector(zDO, sectorIndex);
		if (Game.instance.PortalPrefabHash.Contains(zDO.GetPrefab()))
		{
			if (m_portalObjects.TryGetValue(sectorIndex, out var value) && value.Remove(zDO) && value.Count == 0)
			{
				m_portalObjects.Remove(sectorIndex);
			}
			DirtyPortalObjects = true;
		}
		m_objectsByID.Remove(zDO.m_uid);
		ZDOPool.Release(zDO);
		foreach (ZDOPeer peer in m_peers)
		{
			peer.m_zdos.Remove(uid);
		}
		if (ZNet.instance.IsServer())
		{
			long ticks = ZNet.instance.GetTime().Ticks;
			m_deadZDOs[uid] = ticks;
		}
	}

	private void SendAllZDOs(ZDOPeer peer)
	{
		while (SendZDOs(peer, flush: true))
		{
		}
	}

	private bool SendZDOs(ZDOPeer peer, bool flush)
	{
		int sendQueueSize = peer.m_peer.m_socket.GetSendQueueSize();
		if (!flush && sendQueueSize > 10240)
		{
			return false;
		}
		int num = 10240 - sendQueueSize;
		if (num < 2048)
		{
			return false;
		}
		m_tempToSync.Clear();
		CreateSyncList(peer, m_tempToSync);
		if (m_tempToSync.Count == 0 && peer.m_invalidSector.Count == 0)
		{
			return false;
		}
		ZPackage zPackage = new ZPackage();
		bool flag = false;
		if (peer.m_invalidSector.Count > 0)
		{
			flag = true;
			zPackage.Write(peer.m_invalidSector.Count);
			foreach (ZDOID item in peer.m_invalidSector)
			{
				zPackage.Write(item);
			}
			peer.m_invalidSector.Clear();
		}
		else
		{
			zPackage.Write(0);
		}
		float time = Time.time;
		ZPackage zPackage2 = new ZPackage();
		bool flag2 = false;
		foreach (ZDO item2 in m_tempToSync)
		{
			if (zPackage.Size() > num)
			{
				break;
			}
			peer.m_forceSend.Remove(item2.m_uid);
			if (!ZNet.instance.IsServer())
			{
				m_clientChangeQueue.Remove(item2.m_uid);
			}
			zPackage.Write(item2.m_uid);
			zPackage.Write(item2.OwnerRevision);
			zPackage.Write(item2.DataRevision);
			zPackage.Write(item2.GetOwner());
			zPackage.Write(item2.GetPosition());
			zPackage2.Clear();
			item2.Serialize(zPackage2);
			zPackage.Write(zPackage2);
			peer.m_zdos[item2.m_uid] = new ZDOPeer.PeerZDOInfo(item2.DataRevision, item2.OwnerRevision, time);
			flag2 = true;
			m_zdosSent++;
		}
		zPackage.Write(ZDOID.None);
		if (flag2 || flag)
		{
			peer.m_peer.m_rpc.Invoke("ZDOData", zPackage);
		}
		return flag2 || flag;
	}

	private void RPC_ZDOData(ZRpc rpc, ZPackage pkg)
	{
		ZDOPeer zDOPeer = FindPeer(rpc);
		if (zDOPeer == null)
		{
			ZLog.Log("ZDO data from unkown host, ignoring");
			return;
		}
		float time = Time.time;
		int num = 0;
		ZPackage pkg2 = new ZPackage();
		int num2 = pkg.ReadInt();
		for (int i = 0; i < num2; i++)
		{
			ZDOID id = pkg.ReadZDOID();
			GetZDO(id)?.InvalidateSector();
		}
		while (true)
		{
			ZDOID zDOID = pkg.ReadZDOID();
			if (zDOID.IsNone())
			{
				break;
			}
			num++;
			ushort num3 = pkg.ReadUShort();
			uint num4 = pkg.ReadUInt();
			long ownerInternal = pkg.ReadLong();
			Vector3 vector = pkg.ReadVector3();
			pkg.ReadPackage(ref pkg2);
			ZDO zDO = GetZDO(zDOID);
			bool flag = false;
			if (zDO != null)
			{
				if (num4 <= zDO.DataRevision)
				{
					if (num3 > zDO.OwnerRevision)
					{
						zDO.SetOwnerInternal(ownerInternal);
						zDO.OwnerRevision = num3;
						zDOPeer.m_zdos[zDOID] = new ZDOPeer.PeerZDOInfo(num4, num3, time);
					}
					continue;
				}
			}
			else
			{
				zDO = CreateNewZDO(zDOID, vector);
				flag = true;
			}
			zDO.OwnerRevision = num3;
			zDO.DataRevision = num4;
			zDO.SetOwnerInternal(ownerInternal);
			zDO.InternalSetPosition(vector);
			zDOPeer.m_zdos[zDOID] = new ZDOPeer.PeerZDOInfo(zDO.DataRevision, zDO.OwnerRevision, time);
			zDO.Deserialize(pkg2);
			AddIfPortal(zDO, zDO.GetPrefab());
			if (ZNet.instance.IsServer() && flag && m_deadZDOs.ContainsKey(zDOID))
			{
				zDO.SetOwner(m_sessionID);
				DestroyZDO(zDO);
			}
		}
		m_zdosRecv += num;
	}

	public void FindSectorObjects(Vector2s sector, SimulationDistance simulationDistance, List<ZDO> sectorObjects, List<ZDO> distantSectorObjects = null)
	{
		m_visitedSectorIndices.Clear();
		FindObjects(sector, sectorObjects, m_visitedSectorIndices);
		for (int i = 1; i <= simulationDistance.NearSimulationDistance; i++)
		{
			for (short num = (short)(sector.x - i); num <= sector.x + i; num++)
			{
				Vector2s vector2s = new Vector2s(num, sector.y - i);
				Vector2s vector2s2 = new Vector2s(num, sector.y + i);
				if (ZoneSystem.instance.ZonesWithinRadius(sector, vector2s, simulationDistance.NearSimulationDistance) || simulationDistance.IsClassic)
				{
					FindObjects(vector2s, sectorObjects, m_visitedSectorIndices);
				}
				if (ZoneSystem.instance.ZonesWithinRadius(sector, vector2s2, simulationDistance.NearSimulationDistance) || simulationDistance.IsClassic)
				{
					FindObjects(vector2s2, sectorObjects, m_visitedSectorIndices);
				}
			}
			for (int j = sector.y - i + 1; j <= sector.y + i - 1; j++)
			{
				Vector2s vector2s3 = new Vector2s(sector.x - i, j);
				Vector2s vector2s4 = new Vector2s(sector.x + i, j);
				if (ZoneSystem.instance.ZonesWithinRadius(sector, vector2s3, simulationDistance.NearSimulationDistance) || simulationDistance.IsClassic)
				{
					FindObjects(vector2s3, sectorObjects, m_visitedSectorIndices);
				}
				if (ZoneSystem.instance.ZonesWithinRadius(sector, vector2s4, simulationDistance.NearSimulationDistance) || simulationDistance.IsClassic)
				{
					FindObjects(vector2s4, sectorObjects, m_visitedSectorIndices);
				}
			}
		}
		List<ZDO> objects = distantSectorObjects ?? sectorObjects;
		int num2 = 1;
		if (simulationDistance.IsClassic)
		{
			num2 += simulationDistance.NearSimulationDistance;
		}
		for (int k = num2; k <= simulationDistance.TotalSimulationDistance; k++)
		{
			for (int l = sector.x - k; l <= sector.x + k; l++)
			{
				Vector2s vector2s5 = new Vector2s(l, sector.y - k);
				Vector2s sector2 = new Vector2s(l, sector.y + k);
				if (ZoneSystem.instance.ZonesWithinRadius(sector, vector2s5, simulationDistance.TotalSimulationDistance, ghostZone: true) || simulationDistance.IsClassic)
				{
					FindDistantObjects(vector2s5, objects, m_visitedSectorIndices);
				}
				if (ZoneSystem.instance.ZonesWithinRadius(sector, vector2s5, simulationDistance.TotalSimulationDistance, ghostZone: true) || simulationDistance.IsClassic)
				{
					FindDistantObjects(sector2, objects, m_visitedSectorIndices);
				}
			}
			for (int m = sector.y - k + 1; m <= sector.y + k - 1; m++)
			{
				Vector2s vector2s6 = new Vector2s(sector.x - k, m);
				Vector2s sector3 = new Vector2s(sector.x + k, m);
				if (ZoneSystem.instance.ZonesWithinRadius(sector, vector2s6, simulationDistance.TotalSimulationDistance, ghostZone: true) || simulationDistance.IsClassic)
				{
					FindDistantObjects(vector2s6, objects, m_visitedSectorIndices);
				}
				if (ZoneSystem.instance.ZonesWithinRadius(sector, vector2s6, simulationDistance.TotalSimulationDistance, ghostZone: true) || simulationDistance.IsClassic)
				{
					FindDistantObjects(sector3, objects, m_visitedSectorIndices);
				}
			}
		}
	}

	private void CreateSyncList(ZDOPeer peer, List<ZDO> toSync)
	{
		if (ZNet.instance.IsServer())
		{
			Vector3 refPos = peer.m_peer.GetRefPos();
			Vector2s zone = ZoneSystem.GetZone(refPos);
			m_tempSectorObjects.Clear();
			m_tempToSyncDistant.Clear();
			FindSectorObjects(zone, peer.m_peer.m_simulationDistance, m_tempSectorObjects, m_tempToSyncDistant);
			foreach (ZDO tempSectorObject in m_tempSectorObjects)
			{
				if (peer.ShouldSend(tempSectorObject))
				{
					toSync.Add(tempSectorObject);
				}
			}
			ServerSortSendZDOS(toSync, refPos, peer);
			if (toSync.Count < 10)
			{
				foreach (ZDO item in m_tempToSyncDistant)
				{
					if (peer.ShouldSend(item))
					{
						toSync.Add(item);
					}
				}
			}
			AddForceSendZdos(peer, toSync);
			return;
		}
		m_tempRemoveList.Clear();
		foreach (ZDOID item2 in m_clientChangeQueue)
		{
			ZDO zDO = GetZDO(item2);
			if (zDO != null && peer.ShouldSend(zDO))
			{
				toSync.Add(zDO);
			}
			else
			{
				m_tempRemoveList.Add(item2);
			}
		}
		foreach (ZDOID tempRemove in m_tempRemoveList)
		{
			m_clientChangeQueue.Remove(tempRemove);
		}
		ClientSortSendZDOS(toSync, peer);
		AddForceSendZdos(peer, toSync);
	}

	private void AddForceSendZdos(ZDOPeer peer, List<ZDO> syncList)
	{
		if (peer.m_forceSend.Count <= 0)
		{
			return;
		}
		m_tempRemoveList.Clear();
		foreach (ZDOID item in peer.m_forceSend)
		{
			ZDO zDO = GetZDO(item);
			if (zDO != null && peer.ShouldSend(zDO))
			{
				syncList.Insert(0, zDO);
			}
			else
			{
				m_tempRemoveList.Add(item);
			}
		}
		foreach (ZDOID tempRemove in m_tempRemoveList)
		{
			peer.m_forceSend.Remove(tempRemove);
		}
	}

	private static int ServerSendCompare(ZDO x, ZDO y)
	{
		bool flag = x.Type == ZDO.ObjectType.Prioritized && x.HasOwner() && x.GetOwner() != s_compareReceiver;
		bool flag2 = y.Type == ZDO.ObjectType.Prioritized && y.HasOwner() && y.GetOwner() != s_compareReceiver;
		if (flag && flag2)
		{
			return Utils.CompareFloats(x.m_tempSortValue, y.m_tempSortValue);
		}
		if (flag != flag2)
		{
			if (!flag)
			{
				return 1;
			}
			return -1;
		}
		if (x.Type == y.Type)
		{
			return Utils.CompareFloats(x.m_tempSortValue, y.m_tempSortValue);
		}
		return ((int)y.Type).CompareTo((int)x.Type);
	}

	private void ServerSortSendZDOS(List<ZDO> objects, Vector3 refPos, ZDOPeer peer)
	{
		float time = Time.time;
		foreach (ZDO @object in objects)
		{
			Vector3 position = @object.GetPosition();
			@object.m_tempSortValue = Vector3.Distance(position, refPos);
			float num = 100f;
			if (peer.m_zdos.TryGetValue(@object.m_uid, out var value))
			{
				num = Mathf.Clamp(time - value.m_syncTime, 0f, 100f);
			}
			@object.m_tempSortValue -= num * 1.5f;
		}
		s_compareReceiver = peer.m_peer.m_uid;
		objects.Sort(ServerSendCompare);
	}

	private static int ClientSendCompare(ZDO x, ZDO y)
	{
		if (x.Type == y.Type)
		{
			return Utils.CompareFloats(x.m_tempSortValue, y.m_tempSortValue);
		}
		if (x.Type == ZDO.ObjectType.Prioritized)
		{
			return -1;
		}
		if (y.Type == ZDO.ObjectType.Prioritized)
		{
			return 1;
		}
		return Utils.CompareFloats(x.m_tempSortValue, y.m_tempSortValue);
	}

	private void ClientSortSendZDOS(List<ZDO> objects, ZDOPeer peer)
	{
		float time = Time.time;
		foreach (ZDO @object in objects)
		{
			@object.m_tempSortValue = 0f;
			float num = 100f;
			if (peer.m_zdos.TryGetValue(@object.m_uid, out var value))
			{
				num = Mathf.Clamp(time - value.m_syncTime, 0f, 100f);
			}
			@object.m_tempSortValue -= num * 1.5f;
		}
		objects.Sort(ClientSendCompare);
	}

	public static long GetSessionID()
	{
		return s_instance.m_sessionID;
	}

	private void FindObjects(Vector2s sector, List<ZDO> objects, HashSet<ZoneSystem.SectorIndex> visitedSectorIndices)
	{
		ZoneSystem.SectorIndex sectorIndex = ZoneSystem.SectorToIndex(sector);
		if (!visitedSectorIndices.Contains(sectorIndex))
		{
			if (m_objectsBySector[sectorIndex.Sector] != null)
			{
				objects.AddRange(m_objectsBySector[sectorIndex.Sector]);
			}
			if (m_portalObjects.TryGetValue(sectorIndex, out var value))
			{
				objects.AddRange(value);
			}
			visitedSectorIndices.Add(sectorIndex);
		}
	}

	private void FindDistantObjects(Vector2s sector, List<ZDO> objects, HashSet<ZoneSystem.SectorIndex> visitedSectorIndices)
	{
		ZoneSystem.SectorIndex item = ZoneSystem.SectorToIndex(sector);
		if (visitedSectorIndices.Contains(item))
		{
			return;
		}
		List<ZDO> list = m_objectsBySector[item.Sector];
		if (list == null)
		{
			return;
		}
		foreach (ZDO item2 in list)
		{
			if (item2.Distant)
			{
				objects.Add(item2);
			}
		}
		visitedSectorIndices.Add(item);
	}

	private void RemoveOrphanNonPersistentZDOS()
	{
		foreach (KeyValuePair<ZDOID, ZDO> item in m_objectsByID)
		{
			ZDO value = item.Value;
			if (!value.Persistent && (!value.HasOwner() || !IsPeerConnected(value.GetOwner())))
			{
				ZDOID uid = value.m_uid;
				ZLog.Log("Destroying abandoned non persistent zdo " + uid.ToString() + " owner " + value.GetOwner());
				value.SetOwner(m_sessionID);
				DestroyZDO(value);
			}
		}
	}

	private bool IsPeerConnected(long uid)
	{
		if (m_sessionID == uid)
		{
			return true;
		}
		foreach (ZDOPeer peer in m_peers)
		{
			if (peer.m_peer.m_uid == uid)
			{
				return true;
			}
		}
		return false;
	}

	private static bool InvalidZDO(ZDO zdo)
	{
		return !zdo.IsValid();
	}

	public bool GetAllZDOsWithPrefabIterative(string prefab, List<ZDO> zdos, ref int index)
	{
		int stableHashCode = prefab.GetStableHashCode();
		if (index >= m_objectsBySector.Length)
		{
			if (m_objectsBySector[0] != null)
			{
				foreach (ZDO item in m_objectsBySector[0])
				{
					if (item.GetPrefab() == stableHashCode)
					{
						zdos.Add(item);
					}
				}
			}
			if (m_portalObjects != null)
			{
				foreach (List<ZDO> value in m_portalObjects.Values)
				{
					foreach (ZDO item2 in value)
					{
						if (item2.GetPrefab() == stableHashCode)
						{
							zdos.Add(item2);
						}
					}
				}
			}
			zdos.RemoveAll(InvalidZDO);
			return true;
		}
		int num = 0;
		while (index < m_objectsBySector.Length)
		{
			List<ZDO> list = m_objectsBySector[index];
			if (list != null)
			{
				foreach (ZDO item3 in list)
				{
					if (item3.GetPrefab() == stableHashCode)
					{
						zdos.Add(item3);
					}
				}
				num++;
				if (num > 400)
				{
					break;
				}
			}
			index++;
		}
		return false;
	}

	private void DecideChunkSize(uint size, byte chunkSize, ref int[] numZDOPerChunk0, ref int[] numZDOPerChunk1)
	{
		byte size2 = (byte)(chunkSize - 1);
		uint num = 64 / size;
		int num2 = 0;
		if (m_chunkSaveMapping != null)
		{
			for (uint num3 = 0u; num3 < size; num3 += 2)
			{
				for (uint num4 = 0u; num4 < size; num4 += 2)
				{
					uint num5 = num4 * num;
					uint num6 = num3 * num;
					if (m_chunkSaveMapping.GetNumZDOs(num5, num6, size2) + m_chunkSaveMapping.GetNumZDOs(num5 + num, num6, size2) + m_chunkSaveMapping.GetNumZDOs(num5, num6 + num, size2) + m_chunkSaveMapping.GetNumZDOs(num5 + num, num6 + num, size2) > 0)
					{
						numZDOPerChunk1[num2] = -100000000;
					}
					num2++;
				}
			}
		}
		num2 = 0;
		for (uint num7 = 0u; num7 < size; num7 += 2)
		{
			uint num8 = num7 * size;
			for (uint num9 = 0u; num9 < size; num9 += 2)
			{
				if (numZDOPerChunk1[num2] >= 0)
				{
					int num10 = numZDOPerChunk0[num8 + num9] + numZDOPerChunk0[1 + num8 + num9] + numZDOPerChunk0[size + num8 + num9] + numZDOPerChunk0[size + 1 + num8 + num9];
					if (num9 != 0 && num7 != 0 && num10 > 0 && num10 < 100000)
					{
						numZDOPerChunk1[num2] = num10;
						numZDOPerChunk0[num8 + num9] = 0;
						numZDOPerChunk0[num8 + num9 + 1] = 0;
						numZDOPerChunk0[num8 + size + num9] = 0;
						numZDOPerChunk0[num8 + size + num9 + 1] = 0;
					}
					else
					{
						numZDOPerChunk1[num2] = -100000000;
					}
				}
				num2++;
			}
		}
	}

	private void AddObjectsPerChunk(int size, byte chunkSize, int[] numZDOPerChunk, ref List<Tuple<ZoneSystem.ChunkIndex, List<ZDO>>> objectsPerChunk)
	{
		int num = 64 / size;
		for (int i = 0; i < numZDOPerChunk.Length; i++)
		{
			if (numZDOPerChunk[i] <= 0)
			{
				continue;
			}
			int x = i % size * num;
			uint y = (uint)(i / size * num);
			ZoneSystem.ChunkIndex chunkIndex = ZoneSystem.ChunkIndexFromXY((uint)x, y, chunkSize);
			if (!m_dirtyChunks[0].Contains(chunkIndex))
			{
				continue;
			}
			Tuple<ZoneSystem.ChunkIndex, List<ZDO>> tuple = new Tuple<ZoneSystem.ChunkIndex, List<ZDO>>(chunkIndex, new List<ZDO>());
			(int x, int y) zoneFromChunk = ZoneSystem.GetZoneFromChunk(chunkIndex);
			int item = zoneFromChunk.x;
			int item2 = zoneFromChunk.y;
			for (int j = item2; j < item2 + 8 * num; j++)
			{
				for (int k = item; k < item + 8 * num; k++)
				{
					uint sector = ZoneSystem.SectorToIndex(k, j).Sector;
					foreach (ZDO item3 in m_objectsBySector[sector].AsNotNull())
					{
						if (item3.Persistent && !Game.instance.PortalPrefabHash.Contains(item3.GetPrefab()))
						{
							tuple.Item2.Add(item3.Clone());
						}
					}
				}
			}
			objectsPerChunk.Add(tuple);
		}
	}

	private List<Tuple<ZoneSystem.ChunkIndex, List<ZDO>>> GetSaveClonePerChunk()
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		int[] numZDOPerChunk = new int[4096];
		int[] numZDOPerChunk2 = new int[1024];
		int[] numZDOPerChunk3 = new int[256];
		int[] numZDOPerChunk4 = new int[64];
		int num = 0;
		for (uint num2 = 0u; num2 < 512; num2 += 8)
		{
			for (uint num3 = 0u; num3 < 512; num3 += 8)
			{
				for (uint num4 = 0u; num4 < 8; num4++)
				{
					for (uint num5 = 0u; num5 < 8; num5++)
					{
						uint sector = ZoneSystem.IndicesToIndex(num3 + num4, num2 + num5).Sector;
						if (m_objectsBySector[sector] != null)
						{
							numZDOPerChunk[num] += m_objectsBySector[sector].Count;
						}
					}
				}
				num++;
			}
		}
		DecideChunkSize(64u, 1, ref numZDOPerChunk, ref numZDOPerChunk2);
		DecideChunkSize(32u, 2, ref numZDOPerChunk2, ref numZDOPerChunk3);
		DecideChunkSize(16u, 3, ref numZDOPerChunk3, ref numZDOPerChunk4);
		int num6 = numZDOPerChunk.Aggregate(0, (int x, int acc) => ((acc > 0) ? 1 : 0) + x);
		num6 += numZDOPerChunk2.Aggregate(0, (int x, int acc) => ((acc > 0) ? 1 : 0) + x);
		num6 += numZDOPerChunk3.Aggregate(0, (int x, int acc) => ((acc > 0) ? 1 : 0) + x);
		num6 += numZDOPerChunk4.Aggregate(0, (int x, int acc) => ((acc > 0) ? 1 : 0) + x);
		List<Tuple<ZoneSystem.ChunkIndex, List<ZDO>>> objectsPerChunk = new List<Tuple<ZoneSystem.ChunkIndex, List<ZDO>>>();
		AddObjectsPerChunk(64, 0, numZDOPerChunk, ref objectsPerChunk);
		AddObjectsPerChunk(32, 1, numZDOPerChunk2, ref objectsPerChunk);
		AddObjectsPerChunk(16, 2, numZDOPerChunk3, ref objectsPerChunk);
		AddObjectsPerChunk(8, 3, numZDOPerChunk4, ref objectsPerChunk);
		if (DirtyPortalObjects)
		{
			Tuple<ZoneSystem.ChunkIndex, List<ZDO>> tuple = new Tuple<ZoneSystem.ChunkIndex, List<ZDO>>(ZoneSystem.ChunkPortal, new List<ZDO>());
			foreach (KeyValuePair<ZoneSystem.SectorIndex, List<ZDO>> portalObject in m_portalObjects)
			{
				foreach (ZDO item in portalObject.Value)
				{
					tuple.Item2.Add(item.Clone());
				}
			}
			objectsPerChunk.Add(tuple);
		}
		if (m_portalObjects.Count > 0 && DirtyPortalObjects)
		{
			num6++;
		}
		m_saveData.m_numFiles = num6 + 4;
		m_saveData.m_dirtyFiles = objectsPerChunk.Count + 4;
		ZLog.Log("GetSaveClonePerChunk. Calculated number of actual chunk files: " + num6 + "  Number of dirty chunks to save: " + objectsPerChunk.Count + " [" + stopwatch.ElapsedMilliseconds + "ms]");
		return objectsPerChunk;
	}

	public Dictionary<ZoneSystem.SectorIndex, List<ZDO>> GetPortals()
	{
		return m_portalObjects;
	}

	public List<ZDO> GetPortalList()
	{
		List<ZDO> list = new List<ZDO>();
		foreach (var (_, collection) in m_portalObjects)
		{
			list.AddRange(collection);
		}
		return list;
	}

	public int NrOfObjects()
	{
		return m_objectsByID.Count;
	}

	public int GetSentZDOs()
	{
		return m_zdosSentLastSec;
	}

	public int GetRecvZDOs()
	{
		return m_zdosRecvLastSec;
	}

	public int GetClientChangeQueue()
	{
		return m_clientChangeQueue.Count;
	}

	public void GetAverageStats(out float sentZdos, out float recvZdos)
	{
		sentZdos = (float)m_zdosSentLastSec / 20f;
		recvZdos = (float)m_zdosRecvLastSec / 20f;
	}

	public void RequestZDO(ZDOID id)
	{
		ZRoutedRpc.instance.InvokeRoutedRPC("RequestZDO", id);
	}

	private void RPC_RequestZDO(long sender, ZDOID id)
	{
		GetPeer(sender)?.ForceSendZDO(id);
	}

	private ZDOPeer GetPeer(long uid)
	{
		foreach (ZDOPeer peer in m_peers)
		{
			if (peer.m_peer.m_uid == uid)
			{
				return peer;
			}
		}
		return null;
	}

	public void ForceSendZDO(ZDOID id)
	{
		foreach (ZDOPeer peer in m_peers)
		{
			peer.ForceSendZDO(id);
		}
	}

	public void ForceSendZDO(long peerID, ZDOID id)
	{
		if (ZNet.instance.IsServer())
		{
			GetPeer(peerID)?.ForceSendZDO(id);
			return;
		}
		foreach (ZDOPeer peer in m_peers)
		{
			peer.ForceSendZDO(id);
		}
	}

	public void ClientChanged(ZDOID id)
	{
		m_clientChangeQueue.Add(id);
	}

	private void AddIfPortal(ZDO zdo, int prefabHash)
	{
		if (!Game.instance.PortalPrefabHash.Contains(prefabHash))
		{
			return;
		}
		if (m_portalObjects.TryGetValue(zdo.GetSectorIndex(), out var value))
		{
			if (value.Contains(zdo))
			{
				return;
			}
			value.Add(zdo);
		}
		else
		{
			m_portalObjects.AddItemToList(zdo.GetSectorIndex(), zdo);
		}
		RemoveFromSector(zdo, zdo.GetSectorIndex());
		DirtyPortalObjects = true;
	}

	private void ConvertOwnerships(List<ZDO> zdos)
	{
		foreach (ZDO zdo in zdos)
		{
			ZDOID zDOID = zdo.GetZDOID(ZDOVars.s_zdoidUser);
			if (zDOID != ZDOID.None)
			{
				zdo.SetOwnerInternal(GetSessionID());
				zdo.Set(ZDOVars.s_user, zDOID.UserID);
			}
			ZDOID zDOID2 = zdo.GetZDOID(ZDOVars.s_zdoidRodOwner);
			if (zDOID2 != ZDOID.None)
			{
				zdo.SetOwnerInternal(GetSessionID());
				zdo.Set(ZDOVars.s_rodOwner, zDOID2.UserID);
			}
		}
	}

	private void ConvertCreationTime(List<ZDO> zdos)
	{
		if (!ZDOExtraData.HasTimeCreated())
		{
			return;
		}
		List<int> list = new List<int>
		{
			"cultivate".GetStableHashCode(),
			"raise".GetStableHashCode(),
			"path".GetStableHashCode(),
			"paved_road".GetStableHashCode(),
			"HeathRockPillar".GetStableHashCode(),
			"HeathRockPillar_frac".GetStableHashCode(),
			"ship_construction".GetStableHashCode(),
			"replant".GetStableHashCode(),
			"digg".GetStableHashCode(),
			"mud_road".GetStableHashCode(),
			"LevelTerrain".GetStableHashCode(),
			"digg_v2".GetStableHashCode()
		};
		int num = 0;
		foreach (ZDO zdo in zdos)
		{
			if (list.Contains(zdo.GetPrefab()))
			{
				num++;
				long timeCreated = ZDOExtraData.GetTimeCreated(zdo.m_uid);
				zdo.SetOwner(GetSessionID());
				zdo.Set(ZDOVars.s_terrainModifierTimeCreated, timeCreated);
			}
		}
		if (num > 0)
		{
			UnityEngine.Debug.Log("Converted " + num + " Creation Times.");
		}
		ZDOExtraData.ClearTimeCreated();
	}

	private void ConvertPortals()
	{
		UnityEngine.Debug.Log("ConvertPortals => Make sure all " + m_portalObjects.Count + " portals are in a good state.");
		List<ZDO> portalList = GetPortalList();
		int num = 0;
		foreach (ZDO item in portalList)
		{
			string text = item.GetString(ZDOVars.s_tag);
			ZDOID zDOID = item.GetZDOID(ZDOVars.s_toRemoveTarget);
			item.RemoveZDOID(ZDOVars.s_toRemoveTarget);
			if (zDOID == ZDOID.None)
			{
				continue;
			}
			ZDO zDO = GetZDO(zDOID);
			if (zDO != null)
			{
				ZDOID zDOID2 = zDO.GetZDOID(ZDOVars.s_toRemoveTarget);
				string text2 = zDO.GetString(ZDOVars.s_tag);
				zDO.RemoveZDOID(ZDOVars.s_toRemoveTarget);
				if (text == text2 && zDOID == zDO.m_uid && zDOID2 == item.m_uid)
				{
					item.SetOwner(GetSessionID());
					zDO.SetOwner(GetSessionID());
					num++;
					item.SetConnection(ZDOExtraData.ConnectionType.Portal, zDO.m_uid);
					zDO.SetConnection(ZDOExtraData.ConnectionType.Portal, item.m_uid);
					instance.ForceSendZDO(item.m_uid);
					instance.ForceSendZDO(zDO.m_uid);
				}
			}
		}
		if (num > 0)
		{
			UnityEngine.Debug.Log("ConvertPortals => fixed " + num + " portals.");
		}
	}

	private void ConnectPortals()
	{
		List<ZDOID> allConnectionZDOIDs = ZDOExtraData.GetAllConnectionZDOIDs(ZDOExtraData.ConnectionType.Portal);
		List<ZDOID> allConnectionZDOIDs2 = ZDOExtraData.GetAllConnectionZDOIDs(ZDOExtraData.ConnectionType.Portal | ZDOExtraData.ConnectionType.Target);
		int num = 0;
		foreach (ZDOID item in allConnectionZDOIDs)
		{
			ZDO zDO = GetZDO(item);
			if (zDO == null)
			{
				continue;
			}
			ZDOConnectionHashData connectionHashData = zDO.GetConnectionHashData(ZDOExtraData.ConnectionType.Portal);
			if (connectionHashData == null)
			{
				continue;
			}
			foreach (ZDOID item2 in allConnectionZDOIDs2)
			{
				if (item2 == item || ZDOExtraData.GetConnectionType(item2) != ZDOExtraData.ConnectionType.None)
				{
					continue;
				}
				ZDO zDO2 = GetZDO(item2);
				if (zDO2 != null)
				{
					ZDOConnectionHashData connectionHashData2 = ZDOExtraData.GetConnectionHashData(item2, ZDOExtraData.ConnectionType.Portal | ZDOExtraData.ConnectionType.Target);
					if (connectionHashData2 != null && connectionHashData.m_hash == connectionHashData2.m_hash)
					{
						num++;
						zDO.SetOwner(GetSessionID());
						zDO2.SetOwner(GetSessionID());
						zDO.SetConnection(ZDOExtraData.ConnectionType.Portal, item2);
						zDO2.SetConnection(ZDOExtraData.ConnectionType.Portal, item);
						break;
					}
				}
			}
		}
		if (num > 0)
		{
			UnityEngine.Debug.Log("ConnectPortals => Connected " + num + " portals.");
		}
	}

	private void ConvertSpawners()
	{
		List<ZDOID> allZDOIDsWithHash = ZDOExtraData.GetAllZDOIDsWithHash(ZDOExtraData.Type.Long, "spawn_id_u".GetStableHashCode());
		if (allZDOIDsWithHash.Count > 0)
		{
			UnityEngine.Debug.Log("ConvertSpawners => Will try and convert " + allZDOIDsWithHash.Count + " spawners.");
		}
		int num = 0;
		int num2 = 0;
		foreach (ZDO item in allZDOIDsWithHash.Select((ZDOID id) => GetZDO(id)))
		{
			item.SetOwner(GetSessionID());
			ZDOID zDOID = item.GetZDOID(ZDOVars.s_toRemoveSpawnID);
			item.RemoveZDOID(ZDOVars.s_toRemoveSpawnID);
			ZDO zDO = GetZDO(zDOID);
			if (zDO != null)
			{
				num++;
				item.SetConnection(ZDOExtraData.ConnectionType.Spawned, zDO.m_uid);
			}
			else
			{
				num2++;
				item.SetConnection(ZDOExtraData.ConnectionType.Spawned, ZDOID.None);
			}
		}
		if (num > 0 || num2 > 0)
		{
			UnityEngine.Debug.Log("ConvertSpawners => Converted " + num + " spawners, and " + num2 + " 'done' spawners.");
		}
	}

	private void ConnectSpawners()
	{
		List<ZDOID> allConnectionZDOIDs = ZDOExtraData.GetAllConnectionZDOIDs(ZDOExtraData.ConnectionType.Spawned);
		List<ZDOID> allConnectionZDOIDs2 = ZDOExtraData.GetAllConnectionZDOIDs(ZDOExtraData.ConnectionType.Spawned | ZDOExtraData.ConnectionType.Target);
		int num = 0;
		int num2 = 0;
		foreach (ZDOID item in allConnectionZDOIDs)
		{
			ZDO zDO = GetZDO(item);
			if (zDO == null)
			{
				continue;
			}
			zDO.SetOwner(GetSessionID());
			bool flag = false;
			ZDOConnectionHashData connectionHashData = zDO.GetConnectionHashData(ZDOExtraData.ConnectionType.Spawned);
			if (connectionHashData != null)
			{
				ZDOID zDOID = ZDOID.None;
				foreach (ZDOID item2 in allConnectionZDOIDs2)
				{
					if (!(item2 == item))
					{
						ZDOConnectionHashData connectionHashData2 = ZDOExtraData.GetConnectionHashData(item2, ZDOExtraData.ConnectionType.Spawned | ZDOExtraData.ConnectionType.Target);
						if (connectionHashData2 != null && connectionHashData.m_hash == connectionHashData2.m_hash)
						{
							zDOID = item2;
							flag = true;
							num++;
							zDO.SetConnection(ZDOExtraData.ConnectionType.Spawned, item2);
							break;
						}
					}
				}
				if (zDOID != ZDOID.None)
				{
					allConnectionZDOIDs2.Remove(zDOID);
				}
			}
			if (!flag)
			{
				num2++;
				zDO.SetConnection(ZDOExtraData.ConnectionType.Spawned, ZDOID.None);
			}
		}
		foreach (ZDOID item3 in allConnectionZDOIDs2)
		{
			ZDOExtraData.RemoveConnectionHashData(item3, ZDOExtraData.ConnectionType.Spawned | ZDOExtraData.ConnectionType.Target);
		}
		if (num > 0 || num2 > 0 || allConnectionZDOIDs2.Count > 0)
		{
			UnityEngine.Debug.Log("ConnectSpawners => Connected " + num + " spawners and " + num2 + " 'done' spawners. Removed connection from " + allConnectionZDOIDs2.Count + " orphan spawn:s.");
		}
	}

	private void ConvertSyncTransforms()
	{
		List<ZDOID> allZDOIDsWithHash = ZDOExtraData.GetAllZDOIDsWithHash(ZDOExtraData.Type.Long, "parentID_u".GetStableHashCode());
		if (allZDOIDsWithHash.Count > 0)
		{
			UnityEngine.Debug.Log("ConvertSyncTransforms => Will try and convert " + allZDOIDsWithHash.Count + " SyncTransforms.");
		}
		int num = 0;
		foreach (ZDO item in allZDOIDsWithHash.Select(GetZDO))
		{
			item.SetOwner(GetSessionID());
			ZDOID zDOID = item.GetZDOID(ZDOVars.s_toRemoveParentID);
			item.RemoveZDOID(ZDOVars.s_toRemoveParentID);
			ZDO zDO = GetZDO(zDOID);
			if (zDO != null)
			{
				num++;
				item.SetConnection(ZDOExtraData.ConnectionType.SyncTransform, zDO.m_uid);
			}
		}
		if (num > 0)
		{
			UnityEngine.Debug.Log("ConvertSyncTransforms => Converted " + num + " SyncTransforms.");
		}
	}

	private void ConvertSeed()
	{
		List<ZDOID> allZDOIDsWithHash = ZDOExtraData.GetAllZDOIDsWithHash(ZDOExtraData.Type.Int, ZDOVars.s_leftItem);
		int num = 0;
		foreach (ZDO item in allZDOIDsWithHash.Select(GetZDO))
		{
			num++;
			item.Set(value: item.m_uid.GetHashCode(), hash: ZDOVars.s_seed, okForNotOwner: true);
		}
		if (num > 0)
		{
			UnityEngine.Debug.Log("ConvertSeed => Converted " + num + " ZDOs.");
		}
	}

	private void ConvertDungeonRooms(List<ZDO> zdos)
	{
		MemoryStream memoryStream = new MemoryStream();
		BinaryWriter binaryWriter = new BinaryWriter(memoryStream);
		int num = 0;
		int num2 = 0;
		foreach (ZDO zdo in zdos)
		{
			int num3 = zdo.GetInt(ZDOVars.s_rooms);
			zdo.RemoveInt(ZDOVars.s_rooms);
			if (num3 != 0)
			{
				memoryStream.SetLength(0L);
				binaryWriter.Write(num3);
				for (int i = 0; i < num3; i++)
				{
					string text = "room" + i;
					int value = zdo.GetInt(text);
					Vector3 vec = zdo.GetVec3(text + "_pos", Vector3.zero);
					Quaternion quaternion = zdo.GetQuaternion(text + "_rot", Quaternion.identity);
					zdo.RemoveInt(text);
					zdo.RemoveVec3(text + "_pos");
					zdo.RemoveQuaternion(text + "_rot");
					zdo.RemoveInt(text + "_seed");
					binaryWriter.Write(value);
					binaryWriter.Write(vec);
					binaryWriter.Write(quaternion);
				}
				zdo.Set(ZDOVars.s_roomData, memoryStream.ToArray());
				num++;
				num2 += num3;
			}
		}
		if (num2 > 0)
		{
			ZLog.Log($"Cleaned up old dungeon data format for {num2} rooms in {num} ZDOs.");
		}
	}

	private void ConvertContainers(List<ZDO> zdos, Version.World worldVersion)
	{
		List<ZDOID> allZDOIDsWithHash = ZDOExtraData.GetAllZDOIDsWithHash(ZDOExtraData.Type.Int, ZDOVars.s_stack);
		int num = 0;
		for (int i = -1; i < 14; i++)
		{
			allZDOIDsWithHash = ZDOExtraData.GetAllZDOIDsWithHash(ZDOExtraData.Type.Int, GetConvertHash(i, "stack", ZDOVars.s_stack));
			num += ConvertInventories(allZDOIDsWithHash, worldVersion, i);
		}
		if (num > 0)
		{
			UnityEngine.Debug.Log("ConvertInventories => Converted " + num + " ZDOs.");
		}
		allZDOIDsWithHash = ZDOExtraData.GetAllZDOIDsWithHash(ZDOExtraData.Type.String, ZDOVars.s_items);
		int num2 = 0;
		foreach (ZDO item in allZDOIDsWithHash.Select(GetZDO))
		{
			num2++;
			string text = item.GetString(ZDOVars.s_items);
			if (!string.IsNullOrEmpty(text))
			{
				ZPackage zPackage = new ZPackage(text);
				if (zPackage.Size() == 0)
				{
					continue;
				}
				Inventory inventory = new Inventory(_: false);
				inventory.Load(zPackage);
				zPackage = new ZPackage();
				inventory.Save(zPackage);
				item.Set(ZDOVars.s_items, zPackage.GetArray());
			}
			item.RemoveString(ZDOVars.s_items);
		}
		if (num2 > 0)
		{
			UnityEngine.Debug.Log("ConvertContainers => Converted " + num2 + " ZDOs.");
		}
	}

	private int ConvertInventories(List<ZDOID> zdoIds, Version.World worldVersion, int index = -1)
	{
		string arg = ((index == -1) ? "" : $"{index}_");
		int num = 0;
		foreach (ZDO item in zdoIds.Select(GetZDO))
		{
			num++;
			ItemDrop.ItemData itemData = new ItemDrop.ItemData();
			itemData.m_durability = item.GetFloat(GetConvertHash(index, "durability", ZDOVars.s_durability), itemData.m_durability);
			itemData.m_stack = item.GetInt(GetConvertHash(index, "stack", ZDOVars.s_stack), itemData.m_stack);
			itemData.m_quality = item.GetInt(GetConvertHash(index, "quality", ZDOVars.s_quality), itemData.m_quality);
			itemData.m_variant = item.GetInt(GetConvertHash(index, "variant", ZDOVars.s_variant), itemData.m_variant);
			itemData.m_crafterID = item.GetLong(GetConvertHash(index, "crafterID", ZDOVars.s_crafterID), itemData.m_crafterID);
			itemData.m_crafterName = item.GetString(GetConvertHash(index, "crafterName", ZDOVars.s_crafterName), itemData.m_crafterName);
			int num2 = item.GetInt(GetConvertHash(index, "dataCount", ZDOVars.s_dataCount));
			itemData.m_customData.Clear();
			for (int i = 0; i < num2; i++)
			{
				itemData.m_customData[item.GetString($"{arg}data_{i}")] = item.GetString($"{arg}data__{i}");
			}
			itemData.m_worldLevel = (byte)item.GetInt(GetConvertHash(index, "worldLevel", ZDOVars.s_worldLevel), itemData.m_worldLevel);
			itemData.m_pickedUp = item.GetBool(GetConvertHash(index, "pickedUp", ZDOVars.s_pickedUp), itemData.m_pickedUp);
			if (worldVersion == Version.World.DeepNorthNoChunk || worldVersion == Version.World.DeepNorthNoChunk2)
			{
				itemData.m_cheated = item.GetBool(ZDOVars.s_cheated, itemData.m_cheated);
			}
			ItemDrop.SaveToZDO(itemData, item, index);
			item.RemoveFloat(ZDOVars.s_durability);
			item.RemoveInt(ZDOVars.s_stack);
			if (itemData.m_quality == 1)
			{
				item.RemoveInt(ZDOVars.s_quality);
			}
			if (itemData.m_variant == 0)
			{
				item.RemoveInt(ZDOVars.s_variant);
			}
			item.RemoveLong(ZDOVars.s_crafterID);
			item.RemoveString(ZDOVars.s_crafterName);
			item.RemoveInt(ZDOVars.s_dataCount);
			for (int j = 0; j < num2; j++)
			{
				item.RemoveString($"{arg}data_{j}".GetStableHashCode());
				item.RemoveString($"{arg}data__{j}".GetStableHashCode());
			}
			item.RemoveInt(ZDOVars.s_worldLevel);
			item.RemoveBool(ZDOVars.s_pickedUp);
			item.RemoveBool(ZDOVars.s_cheated);
		}
		return num;
	}

	private int GetConvertHash(int index, string name, int defaultHash)
	{
		if (index != -1)
		{
			return $"{index}_{name}".GetStableHashCode();
		}
		return defaultHash;
	}

	private void ConvertPrefabStrings(List<ZDO> zdos)
	{
		int num = ConvertPrefabs(ZDOVars.s_content);
		num += ConvertPrefabs(ZDOVars.s_item);
		num += ConvertPrefabs("0_item".GetStableHashCode());
		num += ConvertPrefabs("1_item".GetStableHashCode());
		num += ConvertPrefabs("2_item".GetStableHashCode());
		num += ConvertPrefabs("3_item".GetStableHashCode());
		num += ConvertPrefabs("4_item".GetStableHashCode());
		num += ConvertPrefabs("5_item".GetStableHashCode());
		if (num > 0)
		{
			UnityEngine.Debug.Log("ConvertPrefabStrings => Converted " + num + " strings to hashes.");
		}
		static bool ConvertPrefab(ZDO zdo, int prefabHash)
		{
			string text = zdo.GetString(prefabHash);
			zdo.RemoveString(prefabHash);
			if (!string.IsNullOrEmpty(text))
			{
				zdo.Set(prefabHash, text.GetStableHashCode(), okForNotOwner: true);
				return true;
			}
			return false;
		}
		int ConvertPrefabs(int hash)
		{
			int num2 = 0;
			foreach (ZDO item in ZDOExtraData.GetAllZDOIDsWithHash(ZDOExtraData.Type.String, hash).Select(GetZDO))
			{
				if (ConvertPrefab(item, hash))
				{
					num2++;
				}
			}
			return num2;
		}
	}

	private void RemoveObsoleteData(List<ZDO> zdos)
	{
		int num = 0;
		int num2 = 0;
		int hash = Animator.StringToHash("anim_speed");
		foreach (ZDO item in ZDOExtraData.GetAllZDOIDsWithHash(ZDOExtraData.Type.Float, hash).Select(GetZDO))
		{
			if (item.RemoveFloat(hash))
			{
				num++;
			}
		}
		num += ZDOExtraData.RemoveFloats(ZDOHelper.s_stripOldFloats);
		int num3 = ZDOExtraData.RemoveVec3s(ZDOHelper.s_stripOldVecs);
		int num4 = ZDOExtraData.RemoveQuaternions(ZDOHelper.s_stripOldQuats);
		num2 += ZDOExtraData.RemoveInts(ZDOHelper.s_stripOldInts);
		int num5 = ZDOExtraData.RemoveLongs(ZDOHelper.s_stripOldLongData);
		num5 += ZDOExtraData.RemoveLongs(ZDOHelper.s_stripOldUnknownLongData);
		int num6 = ZDOExtraData.RemoveStrings(ZDOHelper.s_stripOldStringData);
		int num7 = ZDOExtraData.RemoveByteArrays(ZDOHelper.s_stripOldDataByteArray);
		int num8 = ZDOExtraData.Remove(ZDOHelper.s_stripOldData);
		int num9 = ZDOExtraData.RemoveInts(ZDOHelper.s_stripAnimationIntData);
		num9 += ZDOExtraData.RemoveFloats(ZDOHelper.s_stripAnimationFloatData);
		int num10 = num + num3 + num4 + num2 + num5 + num6 + num7 + num8 + num9;
		if (num10 > 0)
		{
			UnityEngine.Debug.Log("Found obsolete data => Removed a total of " + num10 + " values. Breakdown:\n\tFloats:     " + num + "\n\tVec3s:      " + num3 + "\n\tQuats:      " + num4 + "\n\tInts:       " + num2 + "\n\tLongs:      " + num5 + "\n\tByteArrays: " + num7 + "\n\tStrings:    " + num6 + "\n\tAnimSalt:   " + num9 + "\n\tMisc:       " + num8);
		}
	}

	private void ConnectSyncTransforms()
	{
		List<ZDOID> allConnectionZDOIDs = ZDOExtraData.GetAllConnectionZDOIDs(ZDOExtraData.ConnectionType.SyncTransform);
		List<ZDOID> allConnectionZDOIDs2 = ZDOExtraData.GetAllConnectionZDOIDs(ZDOExtraData.ConnectionType.SyncTransform | ZDOExtraData.ConnectionType.Target);
		int num = 0;
		foreach (ZDOID item in allConnectionZDOIDs)
		{
			ZDOConnectionHashData connectionHashData = ZDOExtraData.GetConnectionHashData(item, ZDOExtraData.ConnectionType.SyncTransform);
			if (connectionHashData == null)
			{
				continue;
			}
			foreach (ZDOID item2 in allConnectionZDOIDs2)
			{
				ZDOConnectionHashData connectionHashData2 = ZDOExtraData.GetConnectionHashData(item2, ZDOExtraData.ConnectionType.SyncTransform | ZDOExtraData.ConnectionType.Target);
				if (connectionHashData2 != null && connectionHashData.m_hash == connectionHashData2.m_hash)
				{
					num++;
					ZDOExtraData.SetConnection(item, ZDOExtraData.ConnectionType.SyncTransform, item2);
					break;
				}
			}
		}
		if (num > 0)
		{
			UnityEngine.Debug.Log("ConnectSyncTransforms => Connected " + num + " SyncTransforms.");
		}
	}
}

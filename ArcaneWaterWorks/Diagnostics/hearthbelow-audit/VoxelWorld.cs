using System;
using System.Collections.Generic;
using System.Diagnostics;
using HearthBelow.Digging;
using HearthBelow.Util;
using UnityEngine;

namespace HearthBelow.VoxelMagic;

public static class VoxelWorld
{
	private sealed class OriginScope : IDisposable
	{
		private readonly byte _previous;

		public OriginScope(byte origin)
		{
			_previous = _origin;
			_origin = origin;
		}

		public void Dispose()
		{
			_origin = _previous;
		}
	}

	private readonly struct RevisionStamp
	{
		public readonly ZDOID Uid;

		public readonly uint Revision;

		public RevisionStamp(ZDOID uid, uint revision)
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0002: Unknown result type (might be due to invalid IL or missing references)
			Uid = uid;
			Revision = revision;
		}
	}

	private sealed class ZoneOps
	{
		public byte[]? Payload;

		public bool Unreadable;

		public readonly List<CarveOp> Operations = new List<CarveOp>();

		public readonly HashSet<CarveOp.Key> Keys = new HashSet<CarveOp.Key>();

		public void Take(List<CarveOp> operations)
		{
			Unreadable = false;
			Operations.Clear();
			Keys.Clear();
			foreach (CarveOp operation in operations)
			{
				if (Keys.Add(operation.DedupKey))
				{
					Operations.Add(operation);
				}
			}
		}
	}

	private readonly struct SectorProbe
	{
		public readonly float Time;

		public readonly bool HasData;

		public SectorProbe(float time, bool hasData)
		{
			Time = time;
			HasData = hasData;
		}
	}

	public const float CarvedGroundGap = 0.35f;

	internal static readonly Dictionary<Heightmap, VoxelZone> Zones = new Dictionary<Heightmap, VoxelZone>();

	private static readonly List<Heightmap> TemporaryHeightmaps = new List<Heightmap>();

	private static readonly List<Heightmap> PumpList = new List<Heightmap>();

	private const float MinDigRadius = 0.25f;

	private const float MaxDigRadius = 8f;

	public const float HardMaxDigRadius = 8f;

	private const float MinPlaneRadius = 0.5f;

	private const float MaxPlaneRadius = 6f;

	private const float MinRaiseDelta = 0.1f;

	private const float MaxRaiseDelta = 8f;

	private const float MinScoopDepth = 0.25f;

	private const float MaxScoopDepth = 2f;

	private const float MaxRaisePower = 8f;

	private static byte _origin = 0;

	private const float PrunePad = 8f;

	private const float AutoPruneRadius = 12f;

	public static float LastSwingPrunedVolume;

	private static readonly List<Bounds> PruneBoxes = new List<Bounds>();

	private const float NoEffectQuiet = 2.5f;

	private static float _lastEffectTime = -99f;

	private const int MaxApplyAttempts = 600;

	private static readonly Dictionary<long, RevisionStamp> LastRev = new Dictionary<long, RevisionStamp>();

	private static readonly Dictionary<long, int> FailedAttempts = new Dictionary<long, int>();

	private static readonly Dictionary<long, ZoneOps> OpCache = new Dictionary<long, ZoneOps>();

	private static readonly Dictionary<long, SectorProbe> SectorProbes = new Dictionary<long, SectorProbe>();

	private static readonly List<ZDO> SectorZdos = new List<ZDO>();

	private const float SectorCacheSeconds = 0.5f;

	private static readonly SimulationDistance ThisSectorOnly = new SimulationDistance(0, 0, false);

	private const int MaxCachedSectors = 512;

	private const float NearestDigFallback = 8f;

	public static string DebugMode { get; private set; } = "all";

	public static bool DebugChunksVisible
	{
		get
		{
			string debugMode = DebugMode;
			if (debugMode == "all" || debugMode == "voxel")
			{
				return true;
			}
			return false;
		}
	}

	public static bool DebugVanillaVisible => DebugMode == "vanilla";

	public static bool OverworldVisible { get; private set; } = true;

	public static VoxelZone? GetZone(Heightmap? heightmap)
	{
		if (!((Object)(object)heightmap != (Object)null) || !Zones.TryGetValue(heightmap, out VoxelZone value))
		{
			return null;
		}
		return value;
	}

	public static VoxelZone? EnsureZone(Heightmap heightmap)
	{
		//IL_004c: Unknown result type (might be due to invalid IL or missing references)
		if (Zones.TryGetValue(heightmap, out VoxelZone value))
		{
			return value;
		}
		VoxelZone voxelZone = new VoxelZone();
		long timestamp = Stopwatch.GetTimestamp();
		if (!voxelZone.Build(heightmap))
		{
			return null;
		}
		Zones[heightmap] = voxelZone;
		HearthBelowPlugin.HearthBelowLogger.LogDebug((object)$"Voxelized zone at {((Component)heightmap).transform.position}: grid {voxelZone.NX}x{voxelZone.NY}x{voxelZone.NZ}, data built in {ElapsedMs(timestamp):F1} ms");
		NotifyNeighbors(heightmap);
		return voxelZone;
	}

	private static double ElapsedMs(long fromTimestamp)
	{
		return (double)(Stopwatch.GetTimestamp() - fromTimestamp) * 1000.0 / (double)Stopwatch.Frequency;
	}

	public static void RemoveZone(Heightmap heightmap, bool restoreHeightmap)
	{
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		if (Zones.TryGetValue(heightmap, out VoxelZone value))
		{
			value.Dispose(restoreHeightmap);
			Zones.Remove(heightmap);
			ForgetZone(((Component)heightmap).transform.position);
			DistantLod.RefreshAt(heightmap);
			NotifyNeighbors(heightmap);
		}
	}

	public static VoxelZone? GetActiveZoneAt(Vector3 position)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		VoxelZone zone = GetZone(Heightmap.FindHeightmap(position));
		if (zone == null || !zone.IsActive)
		{
			return null;
		}
		return zone;
	}

	public static bool IsCarvedZoneAt(Vector3 position)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		return GetActiveZoneAt(position)?.HasCarvedGeometry ?? false;
	}

	public static void NotifyNeighbors(Heightmap heightmap)
	{
		ForEachNeighborZone(heightmap, delegate(VoxelZone zone)
		{
			zone.OnNeighborChanged();
		});
	}

	public static void DirtyNeighborsForOp(Heightmap heightmap, CarveOp operation)
	{
		ForEachNeighborZone(heightmap, delegate(VoxelZone zone)
		{
			zone.MarkOpDirty(operation);
		});
	}

	public static void MeshNeighborsForOpNow(Heightmap heightmap, CarveOp operation)
	{
		ForEachNeighborZone(heightmap, delegate(VoxelZone zone)
		{
			zone.MeshOpNow(operation);
		});
	}

	public static void DirtyNeighborRims(Heightmap heightmap)
	{
		ForEachNeighborZone(heightmap, delegate(VoxelZone zone)
		{
			zone.MarkRimDirty();
		});
	}

	private static void ForEachNeighborZone(Heightmap heightmap, Action<VoxelZone> action)
	{
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		foreach (Vector3 item in NeighborPositions(heightmap))
		{
			VoxelZone zone = GetZone(Heightmap.FindHeightmap(item));
			if (zone != null)
			{
				action(zone);
			}
		}
	}

	private static IEnumerable<Vector3> NeighborPositions(Heightmap heightmap)
	{
		float size = (float)heightmap.m_width * heightmap.m_scale;
		Vector3 center = ((Component)heightmap).transform.position;
		int zOffset = -1;
		while (zOffset <= 1)
		{
			int num;
			for (int xOffset = -1; xOffset <= 1; xOffset = num)
			{
				if (xOffset != 0 || zOffset != 0)
				{
					yield return center + new Vector3((float)xOffset * size, 0f, (float)zOffset * size);
				}
				num = xOffset + 1;
			}
			num = zOffset + 1;
			zOffset = num;
		}
	}

	public static void SetDebugView(string mode, Vector3 center, float radius)
	{
		//IL_0076: Unknown result type (might be due to invalid IL or missing references)
		DebugMode = mode;
		bool flag = ((mode == "all" || mode == "voxel") ? true : false);
		bool chunksVisible = flag;
		flag = ((mode == "all" || mode == "vanilla") ? true : false);
		bool flag2 = flag;
		flag = ((mode == "all" || mode == "lod") ? true : false);
		bool visible = flag;
		TemporaryHeightmaps.Clear();
		Heightmap.FindHeightmap(center, radius, TemporaryHeightmaps);
		foreach (Heightmap temporaryHeightmap in TemporaryHeightmaps)
		{
			VoxelZone zone = GetZone(temporaryHeightmap);
			zone?.SetChunksVisible(chunksVisible);
			if (!((Object)(object)temporaryHeightmap.m_meshRenderer == (Object)null))
			{
				bool flag3 = !(zone?.IsActive ?? false);
				((Renderer)temporaryHeightmap.m_meshRenderer).enabled = ((mode == "all") ? flag3 : flag2);
			}
		}
		DistantLod.SetVisible(visible);
	}

	public static void OnHeightmapDestroyed(Heightmap heightmap)
	{
		RemoveZone(heightmap, restoreHeightmap: false);
	}

	public static void OnHeightmapRegenerated(Heightmap heightmap)
	{
		if (!((Object)(object)heightmap == (Object)null) && !heightmap.IsDistantLod)
		{
			GetZone(heightmap)?.Rebuild();
			EnsureZone(heightmap);
			NotifyNeighbors(heightmap);
		}
	}

	private static void SyncOverworldVisibility()
	{
		bool flag = RenderGroupSystem.IsGroupActive((RenderGroup)1);
		if (flag == OverworldVisible)
		{
			return;
		}
		OverworldVisible = flag;
		foreach (VoxelZone value in Zones.Values)
		{
			value.SetOverworldVisible(flag);
		}
	}

	public static void PumpAllZones()
	{
		//IL_00a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_0096: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c7: Unknown result type (might be due to invalid IL or missing references)
		DistantLod.Tick();
		SyncOverworldVisibility();
		if (Zones.Count == 0)
		{
			if (DebugMode != "all")
			{
				DebugMode = "all";
				DistantLod.SetVisible(visible: true);
			}
			return;
		}
		PumpList.Clear();
		foreach (Heightmap key in Zones.Keys)
		{
			PumpList.Add(key);
		}
		Player localPlayer = Player.m_localPlayer;
		Vector3 eyePosition = (((Object)(object)localPlayer != (Object)null) ? ((Component)localPlayer).transform.position : Vector3.zero);
		bool haveEye = (Object)(object)localPlayer != (Object)null;
		Heightmap standingOn = (haveEye ? Heightmap.FindHeightmap(eyePosition) : null);
		PumpList.Sort(delegate(Heightmap heightmapA, Heightmap heightmapB)
		{
			//IL_0090: Unknown result type (might be due to invalid IL or missing references)
			//IL_0096: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b8: Unknown result type (might be due to invalid IL or missing references)
			//IL_00be: Unknown result type (might be due to invalid IL or missing references)
			bool flag = (Object)(object)heightmapA == (Object)(object)standingOn;
			bool flag2 = (Object)(object)heightmapB == (Object)(object)standingOn;
			if (flag != flag2)
			{
				if (!flag)
				{
					return 1;
				}
				return -1;
			}
			VoxelZone value2;
			bool flag3 = (Object)(object)heightmapA != (Object)null && Zones.TryGetValue(heightmapA, out value2) && value2.IsSwapPending;
			VoxelZone value3;
			bool flag4 = (Object)(object)heightmapB != (Object)null && Zones.TryGetValue(heightmapB, out value3) && value3.IsSwapPending;
			if (flag3 != flag4)
			{
				if (!flag3)
				{
					return 1;
				}
				return -1;
			}
			if (!haveEye)
			{
				return 0;
			}
			float num = (((Object)(object)heightmapA == (Object)null) ? float.MaxValue : Utils.DistanceSqr(((Component)heightmapA).transform.position, eyePosition));
			float value4 = (((Object)(object)heightmapB == (Object)null) ? float.MaxValue : Utils.DistanceSqr(((Component)heightmapB).transform.position, eyePosition));
			return num.CompareTo(value4);
		});
		foreach (Heightmap pump in PumpList)
		{
			if ((Object)(object)pump != (Object)null && Zones.TryGetValue(pump, out VoxelZone value))
			{
				value.Pump();
			}
		}
	}

	public static bool SuppressGroundClamp(Vector3 position)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		VoxelZone zone = GetZone(Heightmap.FindHeightmap(position));
		if (zone != null && zone.HasCarvedGeometry)
		{
			return position.y > zone.Origin.y + 1f;
		}
		return false;
	}

	public static bool IsGroundPendingAt(Vector3 position)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		Heightmap val = Heightmap.FindHeightmap(position);
		if ((Object)(object)val == (Object)null)
		{
			return HasVoxelDataInSector(position);
		}
		VoxelZone zone = GetZone(val);
		if (zone != null && zone.IsActive)
		{
			return zone.IsGroundMissingAt(position);
		}
		float num = default(float);
		if (Heightmap.GetHeight(position, ref num) && position.y < num && position.y > num - HearthBelowPlugin.CaveDepth.Value - 1f)
		{
			return HasVoxelDataInSector(position);
		}
		return false;
	}

	public static bool TrySampleSolid(Vector3 position, out bool solid)
	{
		//IL_0003: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		solid = false;
		return GetZone(Heightmap.FindHeightmap(position))?.TryGetSolid(position, out solid) ?? false;
	}

	public static bool TryRelocateOutOfSolid(Vector3 position, float probeUp, float headroom, out Vector3 target)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		target = position;
		return GetActiveZoneAt(position)?.TryLiftOutOfSolid(position, probeUp, headroom, out target) ?? false;
	}

	public static bool SuppressTerrainCheck(Vector3 position)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		if (!SuppressGroundClamp(position))
		{
			return IsGroundPendingAt(position);
		}
		return true;
	}

	public static bool IsUndergroundAt(Vector3 position)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_000a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		if (!IsCarvedZoneAt(position))
		{
			return false;
		}
		float num = default(float);
		if (Heightmap.GetHeight(position, ref num))
		{
			return position.y < num - 1f;
		}
		return false;
	}

	public static bool IsCarvedGroundAt(Vector3 position)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_000a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		if (!IsCarvedZoneAt(position))
		{
			return false;
		}
		float num = default(float);
		if (Heightmap.GetHeight(position, ref num))
		{
			return position.y < num - 0.35f;
		}
		return false;
	}

	public static bool IsInCaveAt(Vector3 position, float minDepth = 1f)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_000a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0040: Unknown result type (might be due to invalid IL or missing references)
		if (!IsCarvedZoneAt(position))
		{
			return false;
		}
		float num = default(float);
		if (!Heightmap.GetHeight(position, ref num) || position.y > num - Mathf.Max(1f, minDepth))
		{
			return false;
		}
		return Physics.Raycast(position + Vector3.up * 0.5f, Vector3.up, 200f, Layers.Terrain);
	}

	public static string GetInfo(Vector3 position)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		Heightmap val = Heightmap.FindHeightmap(position);
		if ((Object)(object)val == (Object)null)
		{
			return "No heightmap at this position.";
		}
		VoxelZone zone = GetZone(val);
		int num = 0;
		int num2 = 0;
		foreach (VoxelZone value in Zones.Values)
		{
			if (value.IsSwapPending)
			{
				num++;
				num2 += value.DirtyChunkCount;
			}
		}
		string text = $"{Zones.Count} zone(s) voxelized in total, {num} still showing vanilla ({num2} chunk(s) queued).";
		if (zone != null)
		{
			return string.Format("Zone voxelized{0}: {1} carve op(s), grid {2}x{3}x{4}. {5}", zone.IsSwapPending ? $" (STILL SHOWING VANILLA, {zone.DirtyChunkCount} chunk(s) queued)" : "", zone.Operations.Count, zone.NX, zone.NY, zone.NZ, text);
		}
		return "Zone not voxelized. " + text;
	}

	private static int NewOpId()
	{
		return Guid.NewGuid().GetHashCode();
	}

	public static IDisposable ConsoleOps()
	{
		return new OriginScope(1);
	}

	private static byte ShapeOf(bool square)
	{
		if (!square)
		{
			return 0;
		}
		return 1;
	}

	private static byte ShapeOf(HearthBelowPlugin.DigShape shape)
	{
		return ShapeOf(shape == HearthBelowPlugin.DigShape.Cube);
	}

	private static CarveOp NewOp(VoxelOpType type, byte shape, Vector3 point, float radius)
	{
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		return new CarveOp
		{
			Id = NewOpId(),
			Type = (byte)type,
			Shape = shape,
			Point = point,
			Radius = radius,
			FloorHeight = float.NegativeInfinity
		};
	}

	public static CarveOp BuildCarveOp(Vector3 point, DigProfile profile, Vector3? digDirection, float toolDepthCap)
	{
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		//IL_007b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Unknown result type (might be due to invalid IL or missing references)
		//IL_0084: Unknown result type (might be due to invalid IL or missing references)
		//IL_0089: Unknown result type (might be due to invalid IL or missing references)
		float radius = Mathf.Clamp(profile.Radius, 0.25f, 8f);
		float floorHeight = float.NegativeInfinity;
		float num = default(float);
		if (!float.IsPositiveInfinity(toolDepthCap) && Heightmap.GetHeight(point, ref num))
		{
			floorHeight = num - Mathf.Max(1f, toolDepthCap);
		}
		int num2;
		int type;
		if (digDirection.HasValue)
		{
			num2 = ((profile.Mode == HearthBelowPlugin.DigStyle.Gradual) ? 1 : 0);
			if (num2 != 0)
			{
				type = 3;
				goto IL_0058;
			}
		}
		else
		{
			num2 = 0;
		}
		type = 0;
		goto IL_0058;
		IL_0058:
		CarveOp result = NewOp((VoxelOpType)type, ShapeOf(profile.Shape), point, radius);
		result.FloorHeight = floorHeight;
		if (num2 == 0)
		{
			return result;
		}
		Vector3 value = digDirection.Value;
		result.Direction = ((Vector3)(ref value)).normalized;
		result.Depth = Mathf.Clamp(HearthBelowPlugin.DigDepthPerHit.Value, 0.25f, 2f);
		return result;
	}

	public static bool CarveAt(Vector3 point, float radius)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		return CarveAt(point, new DigProfile(radius, HearthBelowPlugin.CarveShape.Value, HearthBelowPlugin.DigMode.Value), null);
	}

	public static bool CarveAt(Vector3 point, DigProfile profile, Vector3? digDirection, bool quiet = false, float toolDepthCap = float.PositiveInfinity)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		CarveOp carveOp = BuildCarveOp(point, profile, digDirection, toolDepthCap);
		bool flag = !quiet && !float.IsNegativeInfinity(carveOp.FloorHeight) && point.y - carveOp.Radius < carveOp.FloorHeight + 0.5f;
		LastSwingPrunedVolume = 0f;
		bool num = ApplyPlayerOp(carveOp, quiet ? null : (flag ? "$hearthbelow_pickaxe_depth_limit" : "$hearthbelow_nothing_to_dig"));
		if (num)
		{
			PruneAround(carveOp);
		}
		return num;
	}

	private static void PruneAround(CarveOp source)
	{
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00be: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e3: Unknown result type (might be due to invalid IL or missing references)
		//IL_0102: Unknown result type (might be due to invalid IL or missing references)
		//IL_0107: Unknown result type (might be due to invalid IL or missing references)
		//IL_0140: Unknown result type (might be due to invalid IL or missing references)
		if (!HearthBelowPlugin.DropUnsupportedTerrain.IsOn())
		{
			return;
		}
		float num = Mathf.Min(source.Reach + 8f, 12f);
		PruneBoxes.Clear();
		int samples = 0;
		TemporaryHeightmaps.Clear();
		Heightmap.FindHeightmap(source.Point, num + 3f, TemporaryHeightmaps);
		foreach (Heightmap temporaryHeightmap in TemporaryHeightmaps)
		{
			EnsureZone(temporaryHeightmap)?.CollectFloatingIslands(source.Point, num, PruneBoxes, ref samples);
		}
		if (PruneBoxes.Count == 0)
		{
			return;
		}
		foreach (Bounds pruneBox in PruneBoxes)
		{
			Bounds current = pruneBox;
			CarveOp operation = NewOp(VoxelOpType.Carve, 2, ((Bounds)(ref current)).center, Mathf.Max(((Bounds)(ref current)).extents.x, Mathf.Max(((Bounds)(ref current)).extents.y, ((Bounds)(ref current)).extents.z)));
			operation.Direction = ((Bounds)(ref current)).extents;
			ApplyPlayerOp(operation, null);
		}
		LastSwingPrunedVolume = samples;
		HearthBelowPlugin.HearthBelowLogger.LogDebug((object)$"Prune at {source.Point} dropped {samples} sample(s) of unsupported rock as {PruneBoxes.Count} carve(s).");
	}

	public static bool FillAt(Vector3 point, float radius)
	{
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		return ApplyPlayerOp(NewOp(VoxelOpType.Fill, ShapeOf(HearthBelowPlugin.CarveShape.Value), point, Mathf.Clamp(radius, 0.25f, 8f)), null);
	}

	public static bool RaiseAt(Vector3 point, float radius, float delta, float power, bool square = false)
	{
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		if (delta <= 0f)
		{
			return FlattenAt(point, radius, square);
		}
		CarveOp operation = NewOp(VoxelOpType.Raise, ShapeOf(square), point, Mathf.Clamp(radius, 0.5f, 6f));
		operation.Depth = Mathf.Clamp(delta, 0.1f, 8f);
		operation.Power = Mathf.Clamp(power, 0f, 8f);
		return ApplyPlayerOp(operation, null);
	}

	public static bool FlattenAt(Vector3 point, float radius, bool square = false)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		return ApplyPlayerOp(NewOp(VoxelOpType.Flatten, ShapeOf(square), point, Mathf.Clamp(radius, 0.5f, 6f)), null);
	}

	public static bool SmoothAt(Vector3 point, float radius, float power = 3f)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		CarveOp operation = NewOp(VoxelOpType.Smooth, ShapeOf(square: false), point, Mathf.Clamp(radius, 0.5f, 6f));
		operation.Power = Mathf.Clamp(power, 0f, 8f);
		return ApplyPlayerOp(operation, null);
	}

	private static float WriteExtent(CarveOp operation)
	{
		return operation.Reach + 3f;
	}

	private static bool ApplyPlayerOp(CarveOp operation, string? noEffectMessage)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_0176: Unknown result type (might be due to invalid IL or missing references)
		//IL_00dd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e4: Expected O, but got Unknown
		if (!PrivateArea.CheckAccess(operation.Point, operation.Radius, true, false))
		{
			return false;
		}
		if (Location.IsInsideNoBuildLocation(operation.Point))
		{
			return false;
		}
		TemporaryHeightmaps.Clear();
		Heightmap.FindHeightmap(operation.Point, WriteExtent(operation), TemporaryHeightmaps);
		if (TemporaryHeightmaps.Count == 0)
		{
			return false;
		}
		bool flag = false;
		foreach (Heightmap temporaryHeightmap in TemporaryHeightmaps)
		{
			VoxelZone voxelZone = EnsureZone(temporaryHeightmap);
			if (voxelZone != null && !voxelZone.WouldChange(operation))
			{
				continue;
			}
			flag = true;
			TerrainComp val = TerrainCompDedup.Acquire(temporaryHeightmap);
			if ((Object)(object)val == (Object)null)
			{
				continue;
			}
			ZNetView nview = val.m_nview;
			if (!((Object)(object)nview != (Object)null) || !nview.IsValid())
			{
				continue;
			}
			if (nview.IsOwner())
			{
				if (AppendAndSave(val, operation, out byte[] savedPayload) && ApplyLocal(temporaryHeightmap, operation))
				{
					MarkSynced(val, savedPayload);
				}
			}
			else
			{
				ZPackage val2 = new ZPackage();
				val2.Write(_origin);
				operation.Write(val2);
				nview.InvokeRPC("HearthBelow_Carve", new object[1] { val2 });
			}
		}
		if (flag)
		{
			_lastEffectTime = Time.time;
		}
		else if (noEffectMessage != null)
		{
			float num = Time.time - _lastEffectTime;
			if (num > 2.5f)
			{
				Player localPlayer = Player.m_localPlayer;
				if (localPlayer != null)
				{
					((Character)localPlayer).Message((MessageType)2, noEffectMessage, 0, (Sprite)null, false);
				}
			}
			else
			{
				HearthBelowPlugin.HearthBelowLogger.LogDebug((object)$"Carve at {operation.Point} changed nothing {num * 1000f:F0} ms after one that did - extra op from the same swing?");
			}
		}
		return flag;
	}

	private static bool ApplyLocal(Heightmap heightmap, CarveOp operation)
	{
		VoxelZone voxelZone = EnsureZone(heightmap);
		if (voxelZone == null)
		{
			return false;
		}
		if (!voxelZone.ApplyOp(operation))
		{
			return true;
		}
		voxelZone.MeshOpNow(operation);
		DirtyNeighborsForOp(heightmap, operation);
		MeshNeighborsForOpNow(heightmap, operation);
		OnOpApplied(operation);
		return true;
	}

	private static void OnOpApplied(CarveOp operation)
	{
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		ClutterSystem instance = ClutterSystem.instance;
		if (instance != null)
		{
			instance.ResetGrass(operation.Point, operation.Radius + 2f);
		}
		if (operation.IsCarve || operation.Type == 6)
		{
			MineRockSupport.ShakeLoose(operation.Point, operation.Reach);
		}
		else
		{
			Rescue.EjectBuried(operation.Point, operation.Radius + 1f);
		}
	}

	public static long ZoneKey(Vector3 position)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		Vector2s zone = ZoneSystem.GetZone(position);
		return ((long)zone.x << 32) ^ (uint)zone.y;
	}

	private static ZoneOps Cache(long zoneKey)
	{
		if (OpCache.TryGetValue(zoneKey, out ZoneOps value))
		{
			return value;
		}
		ZoneOps zoneOps = new ZoneOps();
		OpCache[zoneKey] = zoneOps;
		return zoneOps;
	}

	private static ZoneOps Cache(TerrainComp terrainCompiler)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		return Cache(ZoneKey(((Component)terrainCompiler).transform.position));
	}

	public static void ForgetZone(Vector3 position)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		long key = ZoneKey(position);
		LastRev.Remove(key);
		FailedAttempts.Remove(key);
		OpCache.Remove(key);
	}

	private static ZNetView? ValidNView(TerrainComp? terrainCompiler)
	{
		ZNetView val = (((Object)(object)terrainCompiler != (Object)null) ? terrainCompiler.m_nview : null);
		if (!((Object)(object)val != (Object)null) || !val.IsValid())
		{
			return null;
		}
		return val;
	}

	private static ZDO? OwnedZdo(TerrainComp? terrainCompiler)
	{
		ZNetView val = ValidNView(terrainCompiler);
		if (!((Object)(object)val != (Object)null) || !val.IsOwner())
		{
			return null;
		}
		return val.GetZDO();
	}

	public static bool HasSavedOpsAt(Vector3 position)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		ZNetView val = ValidNView(TerrainComp.FindTerrainCompiler(position));
		if ((Object)(object)val == (Object)null)
		{
			return false;
		}
		byte[] byteArray = val.GetZDO().GetByteArray(CarveData.ZdoKey, (byte[])null);
		if (byteArray != null)
		{
			return byteArray.Length > 8;
		}
		return false;
	}

	public static bool HasVoxelDataInSector(Vector3 position)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		if (HasSavedOpsAt(position))
		{
			return true;
		}
		if (ZDOMan.instance == null || (Object)(object)ZoneSystem.instance == (Object)null)
		{
			return false;
		}
		Vector2s zone = ZoneSystem.GetZone(position);
		long key = ((long)zone.x << 32) ^ (uint)zone.y;
		if (SectorProbes.TryGetValue(key, out var value) && Time.time - value.Time < 0.5f)
		{
			return value.HasData;
		}
		SectorZdos.Clear();
		ZDOMan.instance.FindSectorObjects(zone, ThisSectorOnly, SectorZdos, (List<ZDO>)null);
		bool flag = false;
		foreach (ZDO sectorZdo in SectorZdos)
		{
			byte[] byteArray = sectorZdo.GetByteArray(CarveData.ZdoKey, (byte[])null);
			if (byteArray != null && byteArray.Length > 8)
			{
				flag = true;
				break;
			}
		}
		SectorZdos.Clear();
		if (SectorProbes.Count >= 512)
		{
			SectorProbes.Clear();
		}
		SectorProbes[key] = new SectorProbe(Time.time, flag);
		return flag;
	}

	public static void PollComp(TerrainComp terrainCompiler)
	{
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		//IL_0069: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e6: Unknown result type (might be due to invalid IL or missing references)
		ZNetView val = ValidNView(terrainCompiler);
		if ((Object)(object)val == (Object)null)
		{
			return;
		}
		GetZone(terrainCompiler.m_hmap)?.Pump();
		ZDO zDO = val.GetZDO();
		long num = ZoneKey(((Component)terrainCompiler).transform.position);
		RevisionStamp value = new RevisionStamp(zDO.m_uid, zDO.DataRevision);
		if (LastRev.TryGetValue(num, out var value2) && value2.Uid == value.Uid && value2.Revision == value.Revision)
		{
			return;
		}
		if (TryApplyData(terrainCompiler, num))
		{
			LastRev[num] = value;
			FailedAttempts.Remove(num);
			return;
		}
		int value3;
		int num2 = ((!FailedAttempts.TryGetValue(num, out value3)) ? 1 : (value3 + 1));
		FailedAttempts[num] = num2;
		if (num2 > 600)
		{
			HearthBelowPlugin.HearthBelowLogger.LogWarning((object)$"Giving up applying voxel data for zone at {((Component)terrainCompiler).transform.position} (heightmap never became ready)");
			LastRev[num] = value;
			FailedAttempts.Remove(num);
		}
	}

	private static bool TryApplyData(TerrainComp terrainCompiler, long zoneKey)
	{
		byte[] byteArray = terrainCompiler.m_nview.GetZDO().GetByteArray(CarveData.ZdoKey, (byte[])null);
		ZoneOps zoneOps = Cache(zoneKey);
		if (SamePayload(zoneOps.Payload, byteArray))
		{
			return true;
		}
		if (!ApplyData(terrainCompiler, byteArray, zoneOps))
		{
			return false;
		}
		zoneOps.Payload = byteArray;
		return true;
	}

	private static bool SamePayload(byte[]? firstPayload, byte[]? secondPayload)
	{
		if (firstPayload == secondPayload)
		{
			return true;
		}
		if (firstPayload == null || secondPayload == null || firstPayload.Length != secondPayload.Length)
		{
			return false;
		}
		for (int i = 0; i < firstPayload.Length; i++)
		{
			if (firstPayload[i] != secondPayload[i])
			{
				return false;
			}
		}
		return true;
	}

	private static bool ApplyData(TerrainComp terrainCompiler, byte[]? bytes, ZoneOps cache)
	{
		//IL_0118: Unknown result type (might be due to invalid IL or missing references)
		Heightmap hmap = terrainCompiler.m_hmap;
		if ((Object)(object)hmap == (Object)null)
		{
			return false;
		}
		List<CarveOp> operations;
		CarveDataState num = CarveData.Read(bytes, out operations);
		VoxelZone voxelZone = GetZone(hmap);
		if (num == CarveDataState.Unreadable)
		{
			cache.Unreadable = true;
			return true;
		}
		cache.Take(operations);
		if (operations.Count == 0)
		{
			if (voxelZone == null || voxelZone.Operations.Count == 0)
			{
				return true;
			}
			voxelZone.ResetOps(operations);
			DirtyNeighborRims(hmap);
			return true;
		}
		if (voxelZone != null && HasOpsMissingFrom(voxelZone, cache.Keys))
		{
			voxelZone.ResetOps(operations);
			DirtyNeighborRims(hmap);
			return true;
		}
		if (voxelZone == null)
		{
			voxelZone = EnsureZone(hmap);
		}
		if (voxelZone == null)
		{
			return false;
		}
		long timestamp = Stopwatch.GetTimestamp();
		int num2 = 0;
		foreach (CarveOp item in operations)
		{
			if (voxelZone.ApplyOp(item))
			{
				num2++;
				OnOpApplied(item);
			}
		}
		if (num2 > 0)
		{
			DirtyNeighborRims(hmap);
		}
		if (num2 > 1)
		{
			HearthBelowPlugin.HearthBelowLogger.LogDebug((object)$"Replayed {num2}/{operations.Count} saved dig op(s) for zone at {((Component)terrainCompiler).transform.position} in {ElapsedMs(timestamp):F1} ms");
		}
		return true;
	}

	private static bool HasOpsMissingFrom(VoxelZone zone, HashSet<CarveOp.Key> incoming)
	{
		foreach (CarveOp.Key appliedKey in zone.AppliedKeys)
		{
			if (!incoming.Contains(appliedKey))
			{
				return true;
			}
		}
		return false;
	}

	private static bool AppendAndSave(TerrainComp terrainCompiler, CarveOp operation, out byte[]? savedPayload)
	{
		ZDO zDO = terrainCompiler.m_nview.GetZDO();
		byte[] byteArray = zDO.GetByteArray(CarveData.ZdoKey, (byte[])null);
		ZoneOps zoneOps = Cache(terrainCompiler);
		savedPayload = byteArray;
		if (!SamePayload(zoneOps.Payload, byteArray))
		{
			List<CarveOp> operations;
			CarveDataState num = CarveData.Read(byteArray, out operations);
			zoneOps.Payload = byteArray;
			if (num == CarveDataState.Unreadable)
			{
				zoneOps.Unreadable = true;
				return false;
			}
			zoneOps.Take(operations);
		}
		if (zoneOps.Unreadable)
		{
			return false;
		}
		if (zoneOps.Keys.Contains(operation.DedupKey))
		{
			return true;
		}
		if (zoneOps.Operations.Count >= HearthBelowPlugin.MaxOpsPerZone.Value)
		{
			HearthBelowPlugin.HearthBelowLogger.LogWarning((object)"Carve limit reached for this zone");
			return false;
		}
		zoneOps.Keys.Add(operation.DedupKey);
		zoneOps.Operations.Add(operation);
		savedPayload = CarveData.Serialize(zoneOps.Operations);
		zDO.Set(CarveData.ZdoKey, savedPayload);
		return true;
	}

	private static void MarkSynced(TerrainComp terrainCompiler, byte[]? savedPayload)
	{
		Cache(terrainCompiler).Payload = savedPayload;
	}

	private static float MaxRadiusFor(CarveOp operation)
	{
		if (operation.IsBox)
		{
			return 14f;
		}
		if (!operation.IsPlane)
		{
			return Mathf.Clamp(HearthBelowPlugin.MaxPlayerCarveRadius.Value, 0.25f, 8f);
		}
		return 6f;
	}

	public static void RPC_Carve(TerrainComp terrainCompiler, long sender, ZPackage package)
	{
		//IL_0040: Unknown result type (might be due to invalid IL or missing references)
		//IL_005a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_008d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0092: Unknown result type (might be due to invalid IL or missing references)
		if (OwnedZdo(terrainCompiler) != null)
		{
			package.ReadByte();
			CarveOp operation = CarveOp.Read(package);
			operation.Radius = Mathf.Clamp(operation.Radius, 0.25f, MaxRadiusFor(operation));
			if (operation.IsBox)
			{
				operation.Direction = new Vector3(Mathf.Clamp(operation.Direction.x, 0f, 12f), Mathf.Clamp(operation.Direction.y, 0f, 12f), Mathf.Clamp(operation.Direction.z, 0f, 12f));
			}
			if (operation.Type == 3)
			{
				operation.Depth = Mathf.Clamp(operation.Depth, 0.25f, 2f);
			}
			else if (operation.Type == 5)
			{
				operation.Depth = Mathf.Clamp(operation.Depth, 0.1f, 8f);
				operation.Power = Mathf.Clamp(operation.Power, 0f, 8f);
			}
			else if (operation.Type == 4)
			{
				operation.Power = Mathf.Clamp(operation.Power, 0f, 8f);
			}
			if (AppendAndSave(terrainCompiler, operation, out byte[] savedPayload) && (Object)(object)terrainCompiler.m_hmap != (Object)null && ApplyLocal(terrainCompiler.m_hmap, operation))
			{
				MarkSynced(terrainCompiler, savedPayload);
			}
		}
	}

	public static void RPC_Clear(TerrainComp terrainCompiler, long sender)
	{
		ZDO? obj = OwnedZdo(terrainCompiler);
		if (obj != null)
		{
			obj.Set(CarveData.ZdoKey, Array.Empty<byte>());
		}
	}

	public static void RequestClear(Heightmap heightmap)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c1: Unknown result type (might be due to invalid IL or missing references)
		ZNetView val = ValidNView(TerrainComp.FindTerrainCompiler(((Component)heightmap).transform.position));
		if ((Object)(object)val == (Object)null)
		{
			return;
		}
		HashSet<int> hashSet = new HashSet<int>();
		CarveData.Read(val.GetZDO().GetByteArray(CarveData.ZdoKey, (byte[])null), out List<CarveOp> operations);
		foreach (CarveOp item in operations)
		{
			hashSet.Add(item.Id);
		}
		if (val.IsOwner())
		{
			val.GetZDO().Set(CarveData.ZdoKey, Array.Empty<byte>());
		}
		else
		{
			val.InvokeRPC("HearthBelow_Clear", Array.Empty<object>());
		}
		if (hashSet.Count == 0)
		{
			return;
		}
		foreach (Vector3 item2 in NeighborPositions(heightmap))
		{
			TerrainComp val2 = TerrainComp.FindTerrainCompiler(item2);
			if ((Object)(object)val2 != (Object)null)
			{
				RequestRemoveOps(val2, hashSet);
			}
		}
	}

	public static int RestoreAt(Vector3 point, float radius)
	{
		//IL_000a: Unknown result type (might be due to invalid IL or missing references)
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d7: Unknown result type (might be due to invalid IL or missing references)
		TemporaryHeightmaps.Clear();
		Heightmap.FindHeightmap(point, radius + 8f, TemporaryHeightmaps);
		if (TemporaryHeightmaps.Count == 0)
		{
			return 0;
		}
		List<TerrainComp> list = new List<TerrainComp>();
		HashSet<int> hashSet = new HashSet<int>();
		int item = 0;
		bool flag = false;
		float num = float.PositiveInfinity;
		foreach (Heightmap temporaryHeightmap in TemporaryHeightmaps)
		{
			TerrainComp val = TerrainComp.FindTerrainCompiler(((Component)temporaryHeightmap).transform.position);
			ZNetView val2 = ValidNView(val);
			if ((Object)(object)val2 == (Object)null)
			{
				continue;
			}
			list.Add(val);
			if (CarveData.Read(val2.GetZDO().GetByteArray(CarveData.ZdoKey, (byte[])null), out List<CarveOp> operations) != CarveDataState.Ok)
			{
				continue;
			}
			foreach (CarveOp item2 in operations)
			{
				float num2 = Mathf.Max(radius, item2.Reach);
				Vector3 val3 = item2.Point - point;
				float magnitude = ((Vector3)(ref val3)).magnitude;
				if (magnitude <= num2)
				{
					hashSet.Add(item2.Id);
				}
				else if (magnitude - item2.Reach < num)
				{
					num = magnitude - item2.Reach;
					item = item2.Id;
					flag = true;
				}
			}
		}
		if (hashSet.Count == 0 && flag && num <= 8f)
		{
			hashSet.Add(item);
		}
		if (hashSet.Count == 0)
		{
			return 0;
		}
		foreach (TerrainComp item3 in list)
		{
			RequestRemoveOps(item3, hashSet);
		}
		return hashSet.Count;
	}

	private static void RequestRemoveOps(TerrainComp terrainCompiler, HashSet<int> operationIds)
	{
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Expected O, but got Unknown
		ZNetView val = ValidNView(terrainCompiler);
		if ((Object)(object)val == (Object)null)
		{
			return;
		}
		if (val.IsOwner())
		{
			RemoveOps(terrainCompiler, operationIds);
			return;
		}
		ZPackage val2 = new ZPackage();
		val2.Write(operationIds.Count);
		foreach (int operationId in operationIds)
		{
			val2.Write(operationId);
		}
		val.InvokeRPC("HearthBelow_RemoveOps", new object[1] { val2 });
	}

	private static void RemoveOps(TerrainComp terrainCompiler, HashSet<int> operationIds)
	{
		ZDO zDO = terrainCompiler.m_nview.GetZDO();
		if (CarveData.Read(zDO.GetByteArray(CarveData.ZdoKey, (byte[])null), out List<CarveOp> operations) == CarveDataState.Ok && operations.RemoveAll((CarveOp operation) => operationIds.Contains(operation.Id)) != 0)
		{
			zDO.Set(CarveData.ZdoKey, (operations.Count == 0) ? Array.Empty<byte>() : CarveData.Serialize(operations));
		}
	}

	public static void RPC_RemoveOps(TerrainComp terrainCompiler, long sender, ZPackage package)
	{
		if (OwnedZdo(terrainCompiler) == null)
		{
			return;
		}
		int num = package.ReadInt();
		if (num > 0 && num <= HearthBelowPlugin.MaxOpsPerZone.Value)
		{
			HashSet<int> hashSet = new HashSet<int>();
			for (int i = 0; i < num; i++)
			{
				hashSet.Add(package.ReadInt());
			}
			RemoveOps(terrainCompiler, hashSet);
		}
	}
}

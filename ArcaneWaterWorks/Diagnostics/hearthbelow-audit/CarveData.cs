using System;
using System.Collections.Generic;

namespace HearthBelow.VoxelMagic;

public static class CarveData
{
	public const int Version = 8;

	public static readonly int ZdoKey = StringExtensionMethods.GetStableHashCode("HearthBelow_VoxelOps");

	private static readonly HashSet<int> WarnedPayloads = new HashSet<int>();

	public static byte[] Serialize(List<CarveOp> operations)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Expected O, but got Unknown
		ZPackage val = new ZPackage();
		val.Write(8);
		val.Write(operations.Count);
		foreach (CarveOp operation in operations)
		{
			operation.Write(val);
		}
		return Utils.Compress(val.GetArray());
	}

	public static CarveDataState Read(byte[]? bytes, out List<CarveOp> operations)
	{
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Expected O, but got Unknown
		operations = new List<CarveOp>();
		if (bytes == null || bytes.Length == 0)
		{
			return CarveDataState.Empty;
		}
		try
		{
			ZPackage val = new ZPackage(Utils.Decompress(bytes));
			int num = val.ReadInt();
			if (num > 8)
			{
				WarnOnce(bytes, $"Voxel data version {num} is newer than this build supports. " + "Leaving this zone's data untouched - digging here is disabled until you run a build that understands it.");
				return CarveDataState.Unreadable;
			}
			int num2 = val.ReadInt();
			List<CarveOp> list = new List<CarveOp>(num2);
			for (int i = 0; i < num2; i++)
			{
				list.Add(CarveOp.Read(val, num));
			}
			operations = list;
			return CarveDataState.Ok;
		}
		catch (Exception ex)
		{
			WarnOnce(bytes, "Failed to parse voxel data (" + ex.Message + "). Leaving this zone's data untouched.");
			return CarveDataState.Unreadable;
		}
	}

	private static void WarnOnce(byte[] payload, string message)
	{
		int num = payload.Length;
		for (int i = 0; i < payload.Length; i += 64)
		{
			num = num * 31 + payload[i];
		}
		if (WarnedPayloads.Add(num))
		{
			HearthBelowPlugin.HearthBelowLogger.LogWarning((object)message);
		}
	}
}

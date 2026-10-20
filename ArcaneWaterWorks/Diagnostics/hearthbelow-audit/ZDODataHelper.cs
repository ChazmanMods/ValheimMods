using System;
using System.Collections.Generic;
using UnityEngine;

public static class ZDODataHelper
{
	public static void WriteToDisk<TType>(ZPackage pkg, List<KeyValuePair<int, TType>> data, Action<TType> writeFunc)
	{
		if (data.Count == 0)
		{
			return;
		}
		int key;
		if (data.Count > 100)
		{
			string[] obj = new string[5]
			{
				"Writing a lot of data <",
				typeof(TType)?.ToString(),
				">; ",
				null,
				null
			};
			key = data.Count;
			obj[3] = key.ToString();
			obj[4] = " items, is not optimal. Perhaps use a byte array or two instead?";
			Debug.LogWarning(string.Concat(obj));
		}
		pkg.WriteNumItems(data.Count);
		foreach (KeyValuePair<int, TType> datum in data)
		{
			datum.Deconstruct(out key, out var value);
			int data2 = key;
			TType obj2 = value;
			pkg.Write(data2);
			writeFunc(obj2);
		}
	}

	public static void ReadFromDisk<TType>(bool read, ZPackage pkg, Version.World version, ZDOExtraData.Type type, ZDOID uid, Func<TType> readFunc, Action<ZDOID, int, TType> addFunc)
	{
		if (read)
		{
			int num = ZDO.ReadNumItems(pkg, version);
			ZDOExtraData.Reserve(uid, type, num);
			for (int i = 0; i < num; i++)
			{
				int arg = pkg.ReadInt();
				TType arg2 = readFunc();
				addFunc(uid, arg, arg2);
			}
			ZDOExtraData.RemoveIfEmpty(uid, type);
		}
	}

	public static void WriteData<TType>(ZPackage pkg, List<KeyValuePair<int, TType>> data, Action<TType> writeFunc)
	{
		if (data.Count == 0)
		{
			return;
		}
		pkg.WriteNumItems(data.Count);
		foreach (var (data2, obj) in data)
		{
			pkg.Write(data2);
			writeFunc(obj);
		}
	}

	public static void ReadData<TType>(bool read, ZPackage pkg, ZDOExtraData.Type type, ZDOID uid, Func<TType> readFunc, Action<ZDOID, int, TType> addFunc)
	{
		if (read)
		{
			int num = ZDO.ReadNumItems(pkg);
			ZDOExtraData.Reserve(uid, type, num);
			for (int i = 0; i < num; i++)
			{
				int arg = pkg.ReadInt();
				TType arg2 = readFunc();
				addFunc(uid, arg, arg2);
			}
		}
	}
}

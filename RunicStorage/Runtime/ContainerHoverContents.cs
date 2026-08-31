using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using RunicStorage.Engine;
using UnityEngine;
using ItemData = ItemDrop.ItemData;

namespace RunicStorage.Runtime;

internal static class ContainerHoverContents
{
	private delegate bool CheckAccessDelegate(Container container, long playerId);

	private delegate string TranslateDelegate(Localization localization, string key);

	private delegate bool WardStateDelegate(PrivateArea area);

	private delegate bool WardContainsDelegate(PrivateArea area, Vector3 point, float radius);

	private sealed class CacheEntry
	{
		private WeakReference<string> _persistedReference;

		internal HoverPersistedEvidence Evidence;

		internal bool HasEvidence;

		internal long ConfigurationGeneration;

		internal float NextRetryAt;

		internal bool Verified;

		internal string Suffix;

		internal string BaseHoverText;

		internal string CombinedHoverText;

		internal long LastAccess;

		internal bool TryGetPersistedReference(out string persisted)
		{
			persisted = null;
			if (_persistedReference != null)
			{
				return _persistedReference.TryGetTarget(out persisted);
			}
			return false;
		}

		internal void SetPersistedReference(string persisted)
		{
			if (persisted == null)
			{
				_persistedReference = null;
			}
			else if (_persistedReference == null)
			{
				_persistedReference = new WeakReference<string>(persisted);
			}
			else
			{
				_persistedReference.SetTarget(persisted);
			}
		}
	}

	private const int MaximumCacheEntries = 512;

	private const int MaximumWardAreasExamined = 4096;

	private static readonly CheckAccessDelegate CheckAccess = ResolveCheckAccess();

	private static readonly TranslateDelegate Translate = ResolveTranslate();

	private static readonly AccessTools.FieldRef<Container, bool> Loading = ResolveLoading();

	private static readonly List<PrivateArea> WardAreas = ResolveWardAreas();

	private static readonly WardStateDelegate WardEnabled = ResolveWardState("IsEnabled");

	private static readonly WardStateDelegate WardLocalAccess = ResolveWardState("HaveLocalAccess");

	private static readonly WardContainsDelegate WardContains = ResolveWardContains();

	private static readonly Dictionary<Container, CacheEntry> Cache = new Dictionary<Container, CacheEntry>();

	private static long _configurationGeneration = 1L;

	private static long _accessSequence;

	internal static bool IsSupported
	{
		get
		{
			if (CheckAccess != null && Translate != null && Loading != null && WardAreas != null && WardEnabled != null && WardLocalAccess != null)
			{
				return WardContains != null;
			}
			return false;
		}
	}

	internal static void Append(Container container, ref string hoverText)
	{
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00db: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_0108: Unknown result type (might be due to invalid IL or missing references)
		if (!IsSupported || (Object)(object)container == (Object)null || !HoverBaseTextPolicy.Allows(hoverText))
		{
			return;
		}
		Player localPlayer = Player.m_localPlayer;
		ConfigEntry<bool> enabled = PluginConfig.Enabled;
		bool flag = enabled != null && enabled.Value && (PluginConfig.ShowContentsOnHover?.Value ?? false);
		if (!flag || (Object)(object)localPlayer == (Object)null || !((Behaviour)container).isActiveAndEnabled || (int)container.m_privacy == 0 || IsBusy(container, null))
		{
			return;
		}
		ZNetView val = ValheimContainerIdentity.NetworkView(container);
		ZDO val2 = (((Object)(object)val != (Object)null && val.IsValid()) ? val.GetZDO() : null);
		if (val2 == null || Loading.Invoke(container) || IsBusy(container, val2))
		{
			return;
		}
		Vector3 position = ((Component)container).transform.position;
		Vector3 val3 = position - ((Component)localPlayer).transform.position;
		bool flag2 = HoverRangePolicy.IsWithinPhysicalReach((val3).sqrMagnitude, localPlayer.m_maxInteractDistance);
		if (!flag2)
		{
			return;
		}
		bool flag3 = !container.m_checkGuardStone || HasStrictWardAccess(position);
		if (!flag3)
		{
			return;
		}
		long playerID = localPlayer.GetPlayerID();
		bool flag4;
		try
		{
			flag4 = CheckAccess(container, playerID);
		}
		catch
		{
			return;
		}
		if (!flag4)
		{
			return;
		}
		bool durableClaimAbsent = true;
		if (!HoverDisclosurePolicy.AllowsBeforeSynchronization(new HoverDisclosureFacts(flag, localPlayerAvailable: true, containerActive: true, networkObjectValid: true, nonPrivate: true, flag3, flag4, flag2, closed: true, mutationIdle: true, durableClaimAbsent, synchronized: false)))
		{
			return;
		}
		CacheEntry orCreate = GetOrCreate(container);
		orCreate.LastAccess = ++_accessSequence;
		string text = val2.GetString(ZDOVars.s_items, string.Empty);
		bool samePersistedEvidence;
		HoverPersistedEvidence expectedEvidence = CaptureEvidence(orCreate, text, out samePersistedEvidence);
		float realtimeSinceStartup = Time.realtimeSinceStartup;
		switch (HoverCachePolicy.Decide(orCreate.Verified, samePersistedEvidence, orCreate.ConfigurationGeneration, _configurationGeneration, orCreate.NextRetryAt, realtimeSinceStartup))
		{
		case HoverCacheDecision.SuppressUntilRetry:
			return;
		case HoverCacheDecision.Refresh:
			if (!TryRefresh(container, val, val2, text, expectedEvidence, realtimeSinceStartup, orCreate, localPlayer, playerID))
			{
				return;
			}
			break;
		}
		if (HoverDisclosurePolicy.Allows(new HoverDisclosureFacts(flag, localPlayerAvailable: true, containerActive: true, networkObjectValid: true, nonPrivate: true, flag3, flag4, flag2, closed: true, mutationIdle: true, durableClaimAbsent, orCreate.Verified)) && !string.IsNullOrEmpty(orCreate.Suffix))
		{
			if (!string.Equals(orCreate.BaseHoverText, hoverText, StringComparison.Ordinal))
			{
				orCreate.BaseHoverText = hoverText;
				orCreate.CombinedHoverText = hoverText + orCreate.Suffix;
			}
			hoverText = orCreate.CombinedHoverText;
		}
	}

	internal static void Invalidate(Container container)
	{
		if (container != null)
		{
			Cache.Remove(container);
		}
	}

	internal static void InvalidateConfiguration()
	{
		_configurationGeneration++;
		if (_configurationGeneration == 0L)
		{
			_configurationGeneration = 1L;
		}
	}

	internal static void Reset()
	{
		Cache.Clear();
		_configurationGeneration = 1L;
		_accessSequence = 0L;
	}

	internal static bool TryGetSynchronizedReadSnapshot(Container container, out Inventory inventory, out long revision)
	{
		inventory = null;
		revision = 0L;
		if ((Object)(object)container == (Object)null || Loading == null)
		{
			return false;
		}
		try
		{
			ZNetView val = ValheimContainerIdentity.NetworkView(container);
			ZDO val2 = (((Object)(object)val != (Object)null && val.IsValid()) ? val.GetZDO() : null);
			if (val2 == null || Loading.Invoke(container) || IsBusy(container, val2))
			{
				return false;
			}
			string text = val2.GetString(ZDOVars.s_items, string.Empty);
			HoverPersistedEvidence evidence = HoverPersistedEvidence.Capture(text, PluginConfig.HoverMaximumSnapshotCharacters.Value);
			if (!TryGetExactInventory(container, text, evidence, out var inventory2))
			{
				return false;
			}
			ZNetView val3 = ValheimContainerIdentity.NetworkView(container);
			ZDO val4 = (((Object)(object)val3 != (Object)null && val3.IsValid()) ? val3.GetZDO() : null);
			string a = ((val4 != null) ? val4.GetString(ZDOVars.s_items, string.Empty) : null);
			if (val3 != val || val4 != val2 || val4 == null || Loading.Invoke(container) || IsBusy(container, val4) || !string.Equals(a, text, StringComparison.Ordinal))
			{
				return false;
			}
			inventory = inventory2;
			revision = val4.DataRevision;
			return true;
		}
		catch
		{
			inventory = null;
			revision = 0L;
			return false;
		}
	}

	private static bool TryRefresh(Container container, ZNetView expectedView, ZDO expectedZdo, string expectedPersistedItems, HoverPersistedEvidence expectedEvidence, float now, CacheEntry entry, Player expectedPlayer, long expectedPlayerId)
	{
		//IL_0250: Unknown result type (might be due to invalid IL or missing references)
		//IL_02b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ba: Unknown result type (might be due to invalid IL or missing references)
		//IL_02bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c5: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ca: Unknown result type (might be due to invalid IL or missing references)
		//IL_02cf: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ee: Unknown result type (might be due to invalid IL or missing references)
		entry.Verified = false;
		entry.Evidence = expectedEvidence;
		entry.HasEvidence = true;
		entry.SetPersistedReference(expectedEvidence.IsAdmissible ? expectedPersistedItems : null);
		entry.ConfigurationGeneration = _configurationGeneration;
		entry.NextRetryAt = now + Mathf.Clamp(PluginConfig.HoverRefreshIntervalSeconds.Value, 0.1f, 5f);
		entry.Suffix = string.Empty;
		entry.BaseHoverText = null;
		entry.CombinedHoverText = null;
		try
		{
			if (!expectedEvidence.IsAdmissible || !TryGetExactInventory(container, expectedPersistedItems, expectedEvidence, out var inventory))
			{
				return false;
			}
			List<ItemData> allItems = inventory.GetAllItems();
			int num = Math.Min(allItems.Count, Mathf.Clamp(PluginConfig.HoverMaximumStacksExamined.Value, 16, 1024));
			int num2 = allItems.Count - num;
			List<HoverContentEntry> list = new List<HoverContentEntry>(num);
			for (int i = 0; i < num; i++)
			{
				ItemData val = allItems[i];
				if (val != null && val.m_stack > 0)
				{
					string text = ValheimContainerIdentity.ResourceId(val);
					if (text.Length == 0 || text.Length > 256)
					{
						num2++;
					}
					else
					{
						list.Add(new HoverContentEntry(text, DisplayName(val, text), val.m_stack));
					}
				}
			}
			string suffix = ContainerHoverSummaryFormatter.Format(list, Mathf.Clamp(PluginConfig.HoverMaximumItemKinds.Value, 1, 24), Mathf.Clamp(PluginConfig.HoverItemsPerLine.Value, 1, 4), Mathf.Clamp(PluginConfig.HoverMaximumCharacters.Value, 64, 1024), num2);
			ZNetView val2 = ValheimContainerIdentity.NetworkView(container);
			ZDO val3 = (((Object)(object)val2 != (Object)null && val2.IsValid()) ? val2.GetZDO() : null);
			string text2 = ((val3 != null) ? val3.GetString(ZDOVars.s_items, string.Empty) : null);
			HoverPersistedEvidence hoverPersistedEvidence = HoverPersistedEvidence.Capture(text2, PluginConfig.HoverMaximumSnapshotCharacters.Value);
			if (val2 == expectedView && val3 == expectedZdo && val3 != null && hoverPersistedEvidence.Equals(expectedEvidence) && string.Equals(text2, expectedPersistedItems, StringComparison.Ordinal))
			{
				ConfigEntry<bool> enabled = PluginConfig.Enabled;
				if (enabled != null && enabled.Value)
				{
					ConfigEntry<bool> showContentsOnHover = PluginConfig.ShowContentsOnHover;
					if (showContentsOnHover != null && showContentsOnHover.Value && !((Object)(object)Player.m_localPlayer != (Object)(object)expectedPlayer) && !((Object)(object)expectedPlayer == (Object)null) && ((Behaviour)container).isActiveAndEnabled && (int)container.m_privacy != 0 && !Loading.Invoke(container) && !IsBusy(container, val3))
					{
						Vector3 position = ((Component)container).transform.position;
						Vector3 val4 = position - ((Component)expectedPlayer).transform.position;
						if (!HoverRangePolicy.IsWithinPhysicalReach((val4).sqrMagnitude, expectedPlayer.m_maxInteractDistance) || (container.m_checkGuardStone && !HasStrictWardAccess(position)) || !CheckAccess(container, expectedPlayerId))
						{
							return false;
						}
						entry.Suffix = suffix;
						entry.Verified = true;
						entry.NextRetryAt = 0f;
						return true;
					}
				}
			}
			return false;
		}
		catch
		{
			return false;
		}
	}

	private static bool TryGetExactInventory(Container container, string persisted, HoverPersistedEvidence evidence, out Inventory inventory)
	{
		//IL_0087: Unknown result type (might be due to invalid IL or missing references)
		//IL_008d: Expected O, but got Unknown
		inventory = null;
		if ((Object)(object)container == (Object)null || !evidence.IsAdmissible || Loading.Invoke(container))
		{
			return false;
		}
		inventory = container.GetInventory();
		if (inventory == null)
		{
			return false;
		}
		List<ItemData> allItems = inventory.GetAllItems();
		if (string.IsNullOrEmpty(persisted))
		{
			return allItems.Count == 0;
		}
		if (!HoverSnapshotBounds.AllowsEnvelope(allItems.Count, persisted.Length, PluginConfig.HoverMaximumSnapshotCharacters.Value))
		{
			return false;
		}
		int maximumSerializedBytes = HoverSnapshotBounds.SerializedByteCeiling(PluginConfig.HoverMaximumSnapshotCharacters.Value);
		if (!HasBoundedSerializedShape(allItems, maximumSerializedBytes))
		{
			return false;
		}
		ZPackage val = new ZPackage();
		inventory.Save(val);
		if (!HoverSnapshotBounds.MatchesExactSerializedSize(val.Size(), persisted.Length, PluginConfig.HoverMaximumSnapshotCharacters.Value))
		{
			return false;
		}
		return string.Equals(val.GetBase64(), persisted, StringComparison.Ordinal);
	}

	private static bool IsBusy(Container container, ZDO zdo)
	{
		if ((Object)(object)container == (Object)null || container.IsInUse() || ((Object)(object)container.m_wagon != (Object)null && container.m_wagon.InUse()))
		{
			return true;
		}
		return zdo != null && zdo.GetBool(ZDOVars.s_inUse, false);
	}

	private static bool HasBoundedSerializedShape(List<ItemData> items, int maximumSerializedBytes)
	{
		if (items == null || items.Count > 1024 || maximumSerializedBytes <= 0)
		{
			return false;
		}
		long nextBytes = 8L;
		int num = 0;
		for (int i = 0; i < items.Count; i++)
		{
			ItemData val = items[i];
			if (val == null || (Object)(object)val.m_dropPrefab == (Object)null)
			{
				return false;
			}
			nextBytes += 64;
			if (!HoverSnapshotBounds.TryAddStringEstimate(nextBytes, ((Object)val.m_dropPrefab).name?.Length ?? 0, maximumSerializedBytes, out nextBytes) || !HoverSnapshotBounds.TryAddStringEstimate(nextBytes, val.m_crafterName?.Length ?? 0, maximumSerializedBytes, out nextBytes))
			{
				return false;
			}
			Dictionary<string, string> customData = val.m_customData;
			int num2 = customData?.Count ?? 0;
			if (!HoverSnapshotBounds.AllowsCustomDataAddition(num, num2))
			{
				return false;
			}
			num += num2;
			if (customData == null)
			{
				continue;
			}
			foreach (KeyValuePair<string, string> item in customData)
			{
				if (!HoverSnapshotBounds.TryAddStringEstimate(nextBytes, item.Key?.Length ?? 0, maximumSerializedBytes, out nextBytes) || !HoverSnapshotBounds.TryAddStringEstimate(nextBytes, item.Value?.Length ?? 0, maximumSerializedBytes, out nextBytes))
				{
					return false;
				}
			}
		}
		return nextBytes <= maximumSerializedBytes;
	}

	private static bool HasStrictWardAccess(Vector3 position)
	{
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			int count = WardAreas.Count;
			if (count > 4096)
			{
				return false;
			}
			for (int i = 0; i < count; i++)
			{
				PrivateArea val = WardAreas[i];
				if (!((Object)(object)val == (Object)null))
				{
					bool num = WardEnabled(val);
					bool flag = num && WardContains(val, position, 0f);
					bool localAccess = !flag || WardLocalAccess(val);
					if (StrictWardDisclosurePolicy.IsHostileOverlap(num, flag, localAccess))
					{
						return false;
					}
				}
			}
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static string DisplayName(ItemData item, string fallback)
	{
		if (!HoverLabelPolicy.TryPrepareLocalizationToken(item?.m_shared?.m_name, out var token))
		{
			return HoverLabelPolicy.NormalizeDisplayLabel(fallback, "Item");
		}
		try
		{
			Localization instance = Localization.instance;
			string key;
			return HoverLabelPolicy.NormalizeDisplayLabel((instance != null && HoverLabelPolicy.TryGetTranslationKey(token, out key)) ? Translate(instance, key) : HoverLabelPolicy.WithoutLocalizationMarker(token), fallback);
		}
		catch
		{
			return HoverLabelPolicy.NormalizeDisplayLabel(HoverLabelPolicy.WithoutLocalizationMarker(token), fallback);
		}
	}

	private static HoverPersistedEvidence CaptureEvidence(CacheEntry entry, string persisted, out bool samePersistedEvidence)
	{
		if (persisted == null)
		{
			persisted = string.Empty;
		}
		string persisted2 = null;
		if (entry.HasEvidence && entry.TryGetPersistedReference(out persisted2) && (object)persisted2 == persisted)
		{
			samePersistedEvidence = true;
			return entry.Evidence;
		}
		HoverPersistedEvidence hoverPersistedEvidence = HoverPersistedEvidence.Capture(persisted, PluginConfig.HoverMaximumSnapshotCharacters.Value);
		samePersistedEvidence = entry.HasEvidence && entry.Evidence.Equals(hoverPersistedEvidence);
		if (samePersistedEvidence && hoverPersistedEvidence.IsAdmissible)
		{
			samePersistedEvidence = persisted2 != null && string.Equals(persisted2, persisted, StringComparison.Ordinal);
		}
		if (samePersistedEvidence && hoverPersistedEvidence.IsAdmissible)
		{
			entry.SetPersistedReference(persisted);
		}
		return hoverPersistedEvidence;
	}

	private static CacheEntry GetOrCreate(Container container)
	{
		if (Cache.TryGetValue(container, out var value))
		{
			return value;
		}
		if (Cache.Count >= 512)
		{
			EvictOldest();
		}
		value = new CacheEntry();
		Cache.Add(container, value);
		return value;
	}

	private static void EvictOldest()
	{
		Container val = null;
		long num = long.MaxValue;
		foreach (KeyValuePair<Container, CacheEntry> item in Cache)
		{
			if (item.Value.LastAccess < num)
			{
				val = item.Key;
				num = item.Value.LastAccess;
			}
		}
		if (val != null)
		{
			Cache.Remove(val);
		}
	}

	private static CheckAccessDelegate ResolveCheckAccess()
	{
		try
		{
			MethodInfo methodInfo = AccessTools.Method(typeof(Container), "CheckAccess", new Type[1] { typeof(long) }, (Type[])null);
			return (methodInfo == null) ? null : AccessTools.MethodDelegate<CheckAccessDelegate>(methodInfo, (object)null, true);
		}
		catch
		{
			return null;
		}
	}

	private static TranslateDelegate ResolveTranslate()
	{
		try
		{
			MethodInfo methodInfo = AccessTools.Method(typeof(Localization), "Translate", new Type[1] { typeof(string) }, (Type[])null);
			return (methodInfo == null || methodInfo.IsStatic || methodInfo.ReturnType != typeof(string)) ? null : AccessTools.MethodDelegate<TranslateDelegate>(methodInfo, (object)null, true);
		}
		catch
		{
			return null;
		}
	}

	private static AccessTools.FieldRef<Container, bool> ResolveLoading()
	{
		try
		{
			return AccessTools.FieldRefAccess<Container, bool>("m_loading");
		}
		catch
		{
			return null;
		}
	}

	private static List<PrivateArea> ResolveWardAreas()
	{
		try
		{
			return AccessTools.Field(typeof(PrivateArea), "m_allAreas")?.GetValue(null) as List<PrivateArea>;
		}
		catch
		{
			return null;
		}
	}

	private static WardStateDelegate ResolveWardState(string methodName)
	{
		try
		{
			MethodInfo methodInfo = AccessTools.Method(typeof(PrivateArea), methodName, Type.EmptyTypes, (Type[])null);
			return (methodInfo == null) ? null : AccessTools.MethodDelegate<WardStateDelegate>(methodInfo, (object)null, true);
		}
		catch
		{
			return null;
		}
	}

	private static WardContainsDelegate ResolveWardContains()
	{
		try
		{
			MethodInfo methodInfo = AccessTools.Method(typeof(PrivateArea), "IsInside", new Type[2]
			{
				typeof(Vector3),
				typeof(float)
			}, (Type[])null);
			return (methodInfo == null) ? null : AccessTools.MethodDelegate<WardContainsDelegate>(methodInfo, (object)null, true);
		}
		catch
		{
			return null;
		}
	}
}

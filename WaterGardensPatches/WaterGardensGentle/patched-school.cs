using System;
using System.Collections.Generic;
using System.Reflection;
using RunicLilyPads;
using UnityEngine;
using UnityEngine.Rendering;

namespace WaterGardensKoi;

public sealed class KoiSchool : MonoBehaviour
{
	internal static readonly List<KoiSchool> Schools = new List<KoiSchool>();

	internal readonly List<PondFood> Food = new List<PondFood>();

	private static readonly FieldInfo SettingsField = typeof(GardenPiece).GetField("Settings", BindingFlags.Instance | BindingFlags.NonPublic);

	private static readonly float[] Amplitudes = new float[6] { 0f, 1f, 3f, 7f, 14f, 23f };

	private static readonly Func<Vector3, float> Surface = (Func<Vector3, float>)Delegate.CreateDelegate(typeof(Func<Vector3, float>), typeof(GardenPiece).Assembly.GetType("RunicLilyPads.PondNetwork").GetMethod("WaterLevel", BindingFlags.Static | BindingFlags.NonPublic));

	internal GardenPiece Pond;

	internal float Width = 6f;

	internal float Depth = 1.2f;

	internal float Water;

	internal KoiSettings Options = new KoiSettings();

	private ZNetView view;

	internal Transform[] fish;

	private Transform[][] bones;

	private Vector3[] returnFrom;

	private float next;

	private double seed;

	private double returnStart = -10.0;

	private bool near;

	private string lastOptions = "";

	private KoiSettings pending;

	private float pendingUntil;

	private PondFood previousFood;

	private bool previousFed;

	private KoiSplash splash;

	internal int Count => Options.Total;

	internal static double Clock
	{
		get
		{
			if (!ZNet.instance)
			{
				return Time.timeAsDouble;
			}
			return (double)ZNet.instance.GetTime().Ticks / 10000000.0;
		}
	}

	internal bool Live
	{
		get
		{
			if ((bool)Pond && (bool)view)
			{
				return view.IsValid();
			}
			return false;
		}
	}

	private void Awake()
	{
		Pond = GetComponent<GardenPiece>();
		view = GetComponent<ZNetView>();
		seed = (double)base.transform.position.x * 0.163 + (double)base.transform.position.z * 0.087;
		Schools.Add(this);
	}

	private void OnDestroy()
	{
		Schools.Remove(this);
		foreach (PondFood item in Food)
		{
			if ((bool)item)
			{
				item.School = null;
			}
		}
	}

	internal void QueueOptions(KoiSettings settings)
	{
		if (settings.Valid)
		{
			pending = settings.Copy();
			pendingUntil = Time.time + 5f;
		}
	}

	internal bool Inside(Vector3 point, float margin = 1f)
	{
		return KoiMath.Distance(Pond.Shape, Local(point).x, Local(point).z, Width) < (double)margin;
	}

	private Vector3 Local(Vector3 point)
	{
		return Quaternion.Inverse(Quaternion.Euler(0f, base.transform.eulerAngles.y, 0f)) * (point - base.transform.position);
	}

	internal static KoiSchool At(Vector3 point)
	{
		KoiSchool koiSchool = null;
		foreach (KoiSchool school in Schools)
		{
			if ((bool)school && school.Live && school.Inside(point) && point.y <= school.Water + 2f && point.y >= school.Water - school.Depth - 0.5f && (!koiSchool || school.Water > koiSchool.Water))
			{
				koiSchool = school;
			}
		}
		return koiSchool;
	}

	internal void ReadSettings()
	{
		GardenSettings gardenSettings = (GardenSettings)SettingsField.GetValue(Pond);
		Width = gardenSettings.Width;
		Depth = gardenSettings.Depth;
		float num = Surface(base.transform.position);
		Water = (float.IsNegativeInfinity(num) ? (base.transform.position.y - 0.15f) : num);
		string text = view.GetZDO().GetString("koi_settings_v1");
		if (text != lastOptions && KoiSettings.TryDecode(text, out var value))
		{
			Options = value;
			lastOptions = text;
			Rebuild();
		}
	}

	internal Vector3 KeepInside(Vector3 point, int index)
	{
		Vector3 vector = Local(point);
		float num = (float)KoiMath.Distance(Pond.Shape, vector.x, vector.z, Width);
		float num2 = Mathf.Clamp(0.95f - Length(index) * 0.55f / (Width * 0.5f * (float)KoiMath.Aspect(Pond.Shape) * ((Pond.Shape == 2) ? 0.6f : ((Pond.Shape == 3) ? 0.8f : 1f))), 0.15f, 0.9f);
		if (num > num2)
		{
			vector.x *= num2 / num;
			vector.z *= num2 / num;
			Vector3 result = base.transform.position + Quaternion.Euler(0f, base.transform.eulerAngles.y, 0f) * vector;
			result.y = point.y;
			return result;
		}
		return point;
	}

	internal Vector3 Orbit(int index, double time)
	{
		KoiMotion.Wander(Pond.Shape, Width, time, index, seed, out var x, out var z);
		Vector3 vector = KeepInside(base.transform.position + Quaternion.Euler(0f, base.transform.eulerAngles.y, 0f) * new Vector3((float)x, 0f, (float)z), index);
		float num = Surface(vector);
		if (float.IsNegativeInfinity(num))
		{
			num = Water;
		}
		float a = Mathf.Min(Depth * 0.55f, 0.65f);
		float b = Mathf.Min(0.025f * Options.Scale, Depth * 0.15f);
		vector.y = num - Mathf.Lerp(a, b, (float)KoiMotion.SurfaceBlend(time, index, seed));
		return vector;
	}

	internal PondFood Target()
	{
		PondFood pondFood = null;
		foreach (PondFood item in Food)
		{
			if ((bool)item && item.StartTicks > 0 && (!pondFood || item.StartTicks < pondFood.StartTicks || (item.StartTicks == pondFood.StartTicks && item.transform.position.x < pondFood.transform.position.x)))
			{
				pondFood = item;
			}
		}
		return pondFood;
	}

	internal float Length(int index)
	{
		return (0.44f + (float)(index % 3) * 0.025f) * Options.Scale;
	}

	private Vector3 Destination(int index, PondFood food)
	{
		float f = (float)index * MathF.PI * 2f / (float)Mathf.Max(1, Count);
		float num = Mathf.Min(0.25f * Options.Scale, Width * 0.12f);
		Vector3 point = food.transform.position + new Vector3(Mathf.Cos(f), 0f, Mathf.Sin(f)) * num;
		point.y = Water - 0.04f;
		return KeepInside(point, index);
	}

	internal double GatherDuration(PondFood food)
	{
		if (!food)
		{
			return 0.0;
		}
		double time = (double)food.StartTicks / 10000000.0;
		double num = 1.5;
		for (int i = 0; i < Count; i++)
		{
			num = Math.Max(num, (double)Vector3.Distance(Orbit(i, time), Destination(i, food)) / (0.65 * (double)Options.Scale));
		}
		return num;
	}

	internal double FeedingElapsed(PondFood food, double now)
	{
		return now - (double)food.StartTicks / 10000000.0 - food.GatherSeconds;
	}

	internal bool Feeding(PondFood food, double now)
	{
		if ((bool)food && Count > 0 && FeedingElapsed(food, now) >= 0.0)
		{
			return FeedingElapsed(food, now) < 3.0;
		}
		return false;
	}

	internal Vector3 Position(int index, double time, PondFood food = null)
	{
		if (!food)
		{
			food = Target();
		}
		if (!food)
		{
			Vector3 vector = Orbit(index, time);
			if (returnFrom != null && index < returnFrom.Length && time - returnStart < 1.5)
			{
				return Vector3.Lerp(returnFrom[index], vector, (float)KoiMath.Blend(time - returnStart, 1.5));
			}
			return vector;
		}
		double num = (double)food.StartTicks / 10000000.0;
		Vector3 b = Destination(index, food);
		Vector3 point = Vector3.Lerp(Orbit(index, num), b, (float)KoiMath.Blend(time - num, food.GatherSeconds));
		double num2 = FeedingElapsed(food, time);
		if (num2 >= 0.0 && num2 < 3.0)
		{
			point.y = Water + (-0.04f + 0.012f * (float)Math.Sin(num2 * 3.0 + (double)index * 1.7)) * Options.Scale;
		}
		return KeepInside(point, index);
	}

	internal bool ReadyToEat(PondFood food, double now)
	{
		if ((bool)food && Target() == food && Count > 0)
		{
			return FeedingElapsed(food, now) >= 3.0;
		}
		return false;
	}

	private void Rebuild()
	{
		if (fish != null)
		{
			Transform[] array = fish;
			foreach (Transform transform in array)
			{
				if ((bool)transform)
				{
					UnityEngine.Object.Destroy(transform.gameObject);
				}
			}
		}
		fish = null;
		bones = null;
		returnFrom = null;
		previousFood = null;
		previousFed = false;
		if (near && Count > 0)
		{
			Build();
		}
	}

	private void Build()
	{
		fish = new Transform[Count];
		bones = new Transform[Count][];
		returnFrom = new Vector3[Count];
		for (int i = 0; i < Count; i++)
		{
			fish[i] = new GameObject("pond_koi_" + KoiAssets.Names[Options.Variety(i)]).transform;
			fish[i].SetParent(base.transform, worldPositionStays: false);
			fish[i].localScale = Vector3.one * Length(i);
			bones[i] = KoiAssets.Build(fish[i], Options.Variety(i));
		}
		if (!splash)
		{
			splash = base.gameObject.AddComponent<KoiSplash>();
		}
	}

	private void Update()
	{
		if (!Live)
		{
			return;
		}
		if (pending != null)
		{
			if (Time.time > pendingUntil)
			{
				pending = null;
			}
			else if (view.IsOwner())
			{
				view.GetZDO().Set("koi_settings_v1", pending.Encode());
				pending = null;
				next = 0f;
			}
		}
		if (Time.time >= next)
		{
			next = Time.time + 0.5f;
			ReadSettings();
			bool flag = (near = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null && (bool)Player.m_localPlayer && (Player.m_localPlayer.transform.position - base.transform.position).sqrMagnitude < 3600f);
			if (flag && fish == null && Count > 0)
			{
				Build();
			}
			if (fish != null)
			{
				Transform[] array = fish;
				foreach (Transform transform in array)
				{
					if (transform.gameObject.activeSelf != flag)
					{
						transform.gameObject.SetActive(flag);
					}
				}
			}
		}
		if (!near || fish == null)
		{
			if ((bool)splash)
			{
				splash.Stop();
			}
			return;
		}
		double clock = Clock;
		PondFood pondFood = Target();
		bool flag2 = Feeding(pondFood, clock);
		if (previousFed && !flag2)
		{
			for (int j = 0; j < fish.Length; j++)
			{
				returnFrom[j] = fish[j].position;
			}
			returnStart = clock;
		}
		if (pondFood != previousFood && (bool)previousFood)
		{
			for (int k = 0; k < fish.Length; k++)
			{
				returnFrom[k] = fish[k].position;
			}
			returnStart = clock;
		}
		previousFood = pondFood;
		previousFed = flag2;
		if ((bool)splash)
		{
			if (flag2)
			{
				splash.Show(pondFood.transform.position, Water, Options.Scale, Count);
			}
			else
			{
				splash.Stop();
			}
		}
		for (int l = 0; l < Count; l++)
		{
			Vector3 vector = KoiMotion.Adjust(Position(l, clock, pondFood), this, l);
			Vector3 vector2 = KoiMotion.Direction(Position(l, clock + 0.08, pondFood) - vector, this, l, vector);
			if ((bool)pondFood && vector2.sqrMagnitude < 1E-05f)
			{
				vector2 = pondFood.transform.position - vector;
				vector2.y = 0f;
			}
			if (flag2)
			{
				vector2.y = 0f;
				float x = (float)Math.Sin(clock * 3.0 + (double)l * 1.7) * 8f;
				fish[l].rotation = KoiMotion.Turn(Quaternion.LookRotation((vector2.sqrMagnitude > 1E-05f) ? vector2 : Vector3.forward, Vector3.up) * Quaternion.Euler(x, 0f, 0f), this, l);
			}
			else if (vector2.sqrMagnitude > 1E-06f)
			{
				fish[l].rotation = KoiMotion.Turn(Quaternion.LookRotation(vector2, Vector3.up), this, l);
			}
			fish[l].position = vector;
			for (int m = 0; m < 6; m++)
			{
				bones[l][m].localRotation = Quaternion.Euler(0f, (float)Math.Sin(clock * (double)(flag2 ? 8 : 7) - (double)l * 0.8 - (double)m * 0.7) * Amplitudes[m] * (flag2 ? 0.8f : 1f), 0f);
			}
		}
	}
}
You are not using the latest version of the tool, please update.
Latest version is '11.1.0.9782' (yours is '9.1.0.7988')

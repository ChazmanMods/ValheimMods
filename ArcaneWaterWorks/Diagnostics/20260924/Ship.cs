using System;
using System.Collections.Generic;
using System.Linq;
using MagicaCloth2;
using UnityEngine;

public class Ship : MonoBehaviour, IMonoUpdater
{
	public enum Speed
	{
		Stop,
		Back,
		Slow,
		Half,
		Full
	}

	private bool m_forwardPressed;

	private bool m_backwardPressed;

	private float m_sendRudderTime;

	private float m_ashDamageMsgTimer;

	public float m_ashDamageMsgTime = 10f;

	[Header("Objects")]
	public GameObject m_sailObject;

	public GameObject m_mastObject;

	public GameObject m_rudderObject;

	public ShipControlls m_shipControlls;

	public Transform m_controlGuiPos;

	public GameObject m_ashdamageEffects;

	[Header("Misc")]
	public BoxCollider m_floatCollider;

	public float m_waterLevelOffset;

	public float m_forceDistance = 1f;

	public float m_force = 0.5f;

	public float m_damping = 0.05f;

	public float m_dampingSideway = 0.05f;

	public float m_dampingForward = 0.01f;

	public float m_angularDamping = 0.01f;

	public float m_disableLevel = -0.5f;

	public float m_sailForceOffset;

	public float m_sailForceFactor = 0.1f;

	public float m_rudderSpeed = 0.5f;

	public float m_stearForceOffset = -10f;

	public float m_stearForce = 0.5f;

	public float m_stearVelForceFactor = 0.1f;

	public float m_backwardForce = 50f;

	public float m_rudderRotationMax = 30f;

	public float m_minWaterImpactForce = 2.5f;

	public float m_minWaterImpactInterval = 2f;

	public float m_waterImpactDamage = 10f;

	public float m_upsideDownDmgInterval = 1f;

	public float m_upsideDownDmg = 20f;

	public EffectList m_waterImpactEffect = new EffectList();

	public EffectList m_changeSailPosEffect = new EffectList();

	public bool m_ashlandsReady;

	[Header("Sail")]
	public bool m_hasSail;

	public MagicaCloth m_sailCloth;

	public AnimationCurve m_sailBlendWeightCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

	public Transform m_sailBottomTransform;

	public Transform m_sailFurledPosition;

	public Transform m_sailMidfurledPosition;

	public Transform m_sailUnfurledPosition;

	private bool m_sailWasInPosition;

	private Vector3 m_windChangeVelocity = Vector3.zero;

	private Speed m_speed;

	private float m_rudder;

	private float m_rudderValue;

	private Vector3 m_sailForce = Vector3.zero;

	private readonly List<Player> m_players = new List<Player>();

	private List<AudioSource> m_ashlandsFxAudio;

	private WaterVolume m_previousCenter;

	private WaterVolume m_previousLeft;

	private WaterVolume m_previousRight;

	private WaterVolume m_previousForward;

	private WaterVolume m_previousBack;

	private static readonly List<Ship> s_currentShips = new List<Ship>();

	private Rigidbody m_body;

	private ZNetView m_nview;

	private IDestructible m_destructible;

	private float m_lastDepth = -9999f;

	private float m_lastWaterImpactTime;

	private float m_upsideDownDmgTimer;

	private float m_ashlandsDmgTimer;

	private float m_rudderPaddleTimer;

	private float m_sailPosition;

	private float m_lastUpdateWaterForceTime;

	public static List<IMonoUpdater> Instances { get; } = new List<IMonoUpdater>();


	private void Awake()
	{
		m_nview = GetComponent<ZNetView>();
		m_body = GetComponent<Rigidbody>();
		m_destructible = GetComponent<IDestructible>();
		WearNTear component = GetComponent<WearNTear>();
		if ((bool)component)
		{
			component.m_onDestroyed = (Action)Delegate.Combine(component.m_onDestroyed, new Action(OnDestroyed));
		}
		if (m_nview.GetZDO() == null)
		{
			base.enabled = false;
		}
		m_body.maxDepenetrationVelocity = 2f;
		Heightmap.ForceGenerateAll();
		if ((bool)m_ashdamageEffects)
		{
			m_ashdamageEffects.SetActive(value: false);
			m_ashlandsFxAudio = m_ashdamageEffects.GetComponentsInChildren<AudioSource>().ToList();
		}
	}

	private void OnEnable()
	{
		Instances.Add(this);
	}

	private void OnDisable()
	{
		Instances.Remove(this);
	}

	public bool CanBeRemoved()
	{
		return m_players.Count == 0;
	}

	private void Start()
	{
		m_nview.Register("Stop", RPC_Stop);
		m_nview.Register("Forward", RPC_Forward);
		m_nview.Register("Backward", RPC_Backward);
		m_nview.Register<float>("Rudder", RPC_Rudder);
		InvokeRepeating("UpdateOwner", 2f, 2f);
	}

	private void PrintStats()
	{
		if (m_players.Count != 0)
		{
			ZLog.Log("Vel:" + m_body.linearVelocity.magnitude.ToString("0.0"));
		}
	}

	public void ApplyControlls(Vector3 dir)
	{
		bool flag = (double)dir.z > 0.5;
		bool flag2 = (double)dir.z < -0.5;
		if (flag && !m_forwardPressed)
		{
			Forward();
		}
		if (flag2 && !m_backwardPressed)
		{
			Backward();
		}
		float fixedDeltaTime = Time.fixedDeltaTime;
		float num = Mathf.Lerp(0.5f, 1f, Mathf.Abs(m_rudderValue));
		m_rudder = dir.x * num;
		m_rudderValue += m_rudder * m_rudderSpeed * fixedDeltaTime;
		m_rudderValue = Utils.Clamp(m_rudderValue, -1f, 1f);
		if (Time.time - m_sendRudderTime > 0.2f)
		{
			m_sendRudderTime = Time.time;
			m_nview.InvokeRPC("Rudder", m_rudderValue);
		}
		m_forwardPressed = flag;
		m_backwardPressed = flag2;
	}

	public void Forward()
	{
		m_nview.InvokeRPC("Forward");
	}

	public void Backward()
	{
		m_nview.InvokeRPC("Backward");
	}

	public void Rudder(float rudder)
	{
		m_nview.Invoke("Rudder", rudder);
	}

	private void RPC_Rudder(long sender, float value)
	{
		m_rudderValue = value;
	}

	public void Stop()
	{
		m_nview.InvokeRPC("Stop");
	}

	private void RPC_Stop(long sender)
	{
		m_speed = Speed.Stop;
	}

	private void RPC_Forward(long sender)
	{
		switch (m_speed)
		{
		case Speed.Stop:
			m_speed = Speed.Slow;
			break;
		case Speed.Slow:
			m_speed = Speed.Half;
			break;
		case Speed.Half:
			m_speed = Speed.Full;
			break;
		case Speed.Back:
			m_speed = Speed.Stop;
			break;
		case Speed.Full:
			break;
		}
	}

	private void RPC_Backward(long sender)
	{
		switch (m_speed)
		{
		case Speed.Stop:
			m_speed = Speed.Back;
			break;
		case Speed.Slow:
			m_speed = Speed.Stop;
			break;
		case Speed.Half:
			m_speed = Speed.Slow;
			break;
		case Speed.Full:
			m_speed = Speed.Half;
			break;
		case Speed.Back:
			break;
		}
	}

	public void CustomFixedUpdate(float fixedDeltaTime)
	{
		bool flag = HaveControllingPlayer();
		UpdateControlls(fixedDeltaTime);
		UpdateSail(fixedDeltaTime);
		UpdateRudder(fixedDeltaTime, flag);
		if ((bool)m_nview && !m_nview.IsOwner())
		{
			return;
		}
		UpdateUpsideDmg(fixedDeltaTime);
		TakeAshlandsDamage(fixedDeltaTime);
		if (m_players.Count == 0)
		{
			m_speed = Speed.Stop;
			m_rudderValue = 0f;
		}
		if (!flag && (m_speed == Speed.Slow || m_speed == Speed.Back))
		{
			m_speed = Speed.Stop;
		}
		Vector3 worldCenterOfMass = m_body.worldCenterOfMass;
		Transform obj = m_floatCollider.transform;
		Vector3 size = m_floatCollider.size;
		Vector3 position = obj.position;
		Vector3 forward = obj.forward;
		Vector3 right = obj.right;
		Vector3 vector = position + forward * size.z / 2f;
		Vector3 vector2 = position - forward * size.z / 2f;
		Vector3 vector3 = position - right * size.x / 2f;
		Vector3 vector4 = position + right * size.x / 2f;
		Transform obj2 = base.transform;
		Vector3 forward2 = obj2.forward;
		Vector3 right2 = obj2.right;
		float waterLevel = Floating.GetWaterLevel(worldCenterOfMass, ref m_previousCenter);
		float waterLevel2 = Floating.GetWaterLevel(vector3, ref m_previousLeft);
		float waterLevel3 = Floating.GetWaterLevel(vector4, ref m_previousRight);
		float waterLevel4 = Floating.GetWaterLevel(vector, ref m_previousForward);
		float waterLevel5 = Floating.GetWaterLevel(vector2, ref m_previousBack);
		float num = (waterLevel + waterLevel2 + waterLevel3 + waterLevel4 + waterLevel5) / 5f;
		float num2 = worldCenterOfMass.y - num - m_waterLevelOffset;
		if (!(num2 > m_disableLevel))
		{
			m_body.WakeUp();
			UpdateWaterForce(num2, Time.time);
			Vector3 vector5 = new Vector3(vector3.x, waterLevel2, vector3.z);
			Vector3 vector6 = new Vector3(vector4.x, waterLevel3, vector4.z);
			Vector3 vector7 = new Vector3(vector.x, waterLevel4, vector.z);
			Vector3 vector8 = new Vector3(vector2.x, waterLevel5, vector2.z);
			float num3 = fixedDeltaTime * 50f;
			Vector3 vector9 = m_body.linearVelocity;
			_ = vector9.magnitude;
			float num4 = Utils.Clamp01(Utils.Abs(num2) / m_forceDistance);
			Vector3 vector10 = m_force * num4 * Vector3.up;
			m_body.AddForceAtPosition(vector10 * (m_body.mass * num3), worldCenterOfMass, ForceMode.Impulse);
			float num5 = Vector3.Dot(vector9, forward2);
			float num6 = Vector3.Dot(vector9, right2);
			float num7 = vector9.y * vector9.y * Utils.Sign(vector9.y) * m_damping * num4;
			float num8 = num5 * num5 * Utils.Sign(num5) * m_dampingForward * num4;
			float num9 = num6 * num6 * Utils.Sign(num6) * m_dampingSideway * num4;
			vector9.y -= Utils.Clamp(num7, -1f, 1f);
			vector9 -= base.transform.forward * Utils.Clamp(num8, -1f, 1f);
			vector9 -= base.transform.right * Utils.Clamp(num9, -1f, 1f);
			if (vector9.magnitude > m_body.linearVelocity.magnitude)
			{
				vector9 = vector9.normalized * m_body.linearVelocity.magnitude;
			}
			if (m_players.Count == 0)
			{
				vector9.x *= 0.1f;
				vector9.z *= 0.1f;
			}
			m_body.linearVelocity = vector9;
			m_body.angularVelocity -= m_body.angularVelocity * (m_angularDamping * num4);
			float num10 = 0.15f;
			float num11 = 0.5f;
			float num12 = Utils.Clamp((vector7.y - vector.y) * num10, 0f - num11, num11);
			float num13 = Utils.Clamp((vector8.y - vector2.y) * num10, 0f - num11, num11);
			float num14 = Utils.Clamp((vector5.y - vector3.y) * num10, 0f - num11, num11);
			float num15 = Utils.Clamp((vector6.y - vector4.y) * num10, 0f - num11, num11);
			num12 = Utils.Sign(num12) * Utils.Abs(num12 * num12);
			num13 = Utils.Sign(num13) * Utils.Abs(num13 * num13);
			num14 = Utils.Sign(num14) * Utils.Abs(num14 * num14);
			num15 = Utils.Sign(num15) * Utils.Abs(num15 * num15);
			m_body.AddForceAtPosition(m_body.mass * num12 * num3 * Vector3.up, vector, ForceMode.Impulse);
			m_body.AddForceAtPosition(m_body.mass * num13 * num3 * Vector3.up, vector2, ForceMode.Impulse);
			m_body.AddForceAtPosition(m_body.mass * num14 * num3 * Vector3.up, vector3, ForceMode.Impulse);
			m_body.AddForceAtPosition(m_body.mass * num15 * num3 * Vector3.up, vector4, ForceMode.Impulse);
			float sailSize = 0f;
			if (m_speed == Speed.Full)
			{
				sailSize = 1f;
			}
			else if (m_speed == Speed.Half)
			{
				sailSize = 0.5f;
			}
			Vector3 sailForce = GetSailForce(sailSize, fixedDeltaTime);
			Vector3 position2 = worldCenterOfMass + base.transform.up * m_sailForceOffset;
			m_body.AddForceAtPosition(sailForce * m_body.mass, position2, ForceMode.Impulse);
			Vector3 position3 = base.transform.position + forward2 * m_stearForceOffset;
			float num16 = num5 * m_stearVelForceFactor;
			m_body.AddForceAtPosition(m_body.mass * num16 * (0f - m_rudderValue) * fixedDeltaTime * right2, position3, ForceMode.Impulse);
			Vector3 zero = Vector3.zero;
			switch (m_speed)
			{
			case Speed.Slow:
				zero += forward2 * (m_backwardForce * (1f - Utils.Abs(m_rudderValue)));
				break;
			case Speed.Back:
				zero += -forward2 * (m_backwardForce * (1f - Utils.Abs(m_rudderValue)));
				break;
			}
			if (m_speed == Speed.Back || m_speed == Speed.Slow)
			{
				float num17 = ((m_speed != Speed.Back) ? 1 : (-1));
				zero += base.transform.right * (m_stearForce * (0f - m_rudderValue) * num17);
			}
			m_body.AddForceAtPosition(zero * (m_body.mass * fixedDeltaTime), position3, ForceMode.Impulse);
			ApplyEdgeForce(fixedDeltaTime);
		}
	}

	private void UpdateUpsideDmg(float dt)
	{
		if (!(base.transform.up.y >= 0f))
		{
			m_upsideDownDmgTimer += dt;
			if (!(m_upsideDownDmgTimer <= m_upsideDownDmgInterval))
			{
				m_upsideDownDmgTimer = 0f;
				HitData hitData = new HitData();
				hitData.m_damage.m_blunt = m_upsideDownDmg;
				hitData.m_point = base.transform.position;
				hitData.m_dir = Vector3.up;
				m_destructible.Damage(hitData);
			}
		}
	}

	private void TakeAshlandsDamage(float dt)
	{
		if (m_ashlandsReady)
		{
			return;
		}
		float ashlandsOceanGradient = WorldGenerator.GetAshlandsOceanGradient(base.transform.position);
		if ((bool)m_ashdamageEffects)
		{
			if (ashlandsOceanGradient < 0f)
			{
				m_ashdamageEffects.SetActive(value: false);
				{
					foreach (AudioSource item in m_ashlandsFxAudio)
					{
						item.Stop();
					}
					return;
				}
			}
			m_ashdamageEffects.SetActive(value: true);
		}
		if (m_ashDamageMsgTimer <= 0f && (bool)ZoneSystem.instance && (bool)Player.m_localPlayer)
		{
			ZoneSystem.instance.SetGlobalKey(GlobalKeys.AshlandsOcean);
			m_ashDamageMsgTimer = m_ashDamageMsgTime;
		}
		else
		{
			m_ashDamageMsgTimer -= Time.fixedDeltaTime;
		}
		m_ashlandsDmgTimer += dt;
		if (!((double)m_ashlandsDmgTimer <= 1.0))
		{
			m_ashlandsDmgTimer = 0f;
			ashlandsOceanGradient = Utils.Clamp(ashlandsOceanGradient, 0f, 3f);
			HitData hitData = new HitData();
			hitData.m_damage.m_blunt = Mathf.Floor(Mathf.Lerp(1f, 30f, ashlandsOceanGradient));
			hitData.m_hitType = HitData.HitType.AshlandsOcean;
			hitData.m_point = base.transform.position;
			hitData.m_dir = Vector3.up;
			m_destructible.Damage(hitData);
		}
	}

	private Vector3 GetSailForce(float sailSize, float dt)
	{
		Vector3 windDir = EnvMan.instance.GetWindDir();
		float windIntensity = EnvMan.instance.GetWindIntensity();
		float num = Mathf.Lerp(0.25f, 1f, windIntensity);
		float windAngleFactor = GetWindAngleFactor();
		windAngleFactor *= num;
		Vector3 target = Vector3.Normalize(windDir + base.transform.forward) * (windAngleFactor * m_sailForceFactor * sailSize);
		m_sailForce = Vector3.SmoothDamp(m_sailForce, target, ref m_windChangeVelocity, 1f, 99f);
		return m_sailForce;
	}

	public float GetWindAngleFactor()
	{
		float num = Vector3.Dot(EnvMan.instance.GetWindDir(), -base.transform.forward);
		float num2 = Mathf.Lerp(0.7f, 1f, 1f - Utils.Abs(num));
		float num3 = 1f - Utils.LerpStep(0.75f, 0.8f, num);
		return num2 * num3;
	}

	private void UpdateWaterForce(float depth, float time)
	{
		float num = depth - m_lastDepth;
		float num2 = time - m_lastUpdateWaterForceTime;
		m_lastDepth = depth;
		m_lastUpdateWaterForceTime = time;
		float num3 = num / num2;
		if (!(num3 > 0f) && Utils.Abs(num3) > m_minWaterImpactForce && time - m_lastWaterImpactTime > m_minWaterImpactInterval)
		{
			m_lastWaterImpactTime = time;
			m_waterImpactEffect.Create(base.transform.position, base.transform.rotation);
			if (m_players.Count > 0)
			{
				HitData hitData = new HitData();
				hitData.m_damage.m_blunt = m_waterImpactDamage;
				hitData.m_point = base.transform.position;
				hitData.m_dir = Vector3.up;
				m_destructible.Damage(hitData);
			}
		}
	}

	private void ApplyEdgeForce(float dt)
	{
		float magnitude = base.transform.position.magnitude;
		float num = 10420f;
		if (magnitude > num)
		{
			Vector3 vector = Vector3.Normalize(base.transform.position);
			float num2 = Utils.LerpStep(num, 10500f, magnitude) * 8f;
			Vector3 vector2 = vector * num2;
			m_body.AddForce(vector2 * dt, ForceMode.VelocityChange);
		}
	}

	private void FixTilt()
	{
		float num = Mathf.Asin(base.transform.right.y);
		float num2 = Mathf.Asin(base.transform.forward.y);
		if (Utils.Abs(num) > MathF.PI / 6f)
		{
			if (num > 0f)
			{
				base.transform.RotateAround(base.transform.position, base.transform.forward, (0f - Time.fixedDeltaTime) * 20f);
			}
			else
			{
				base.transform.RotateAround(base.transform.position, base.transform.forward, Time.fixedDeltaTime * 20f);
			}
		}
		if (Utils.Abs(num2) > MathF.PI / 6f)
		{
			if (num2 > 0f)
			{
				base.transform.RotateAround(base.transform.position, base.transform.right, (0f - Time.fixedDeltaTime) * 20f);
			}
			else
			{
				base.transform.RotateAround(base.transform.position, base.transform.right, Time.fixedDeltaTime * 20f);
			}
		}
	}

	private void UpdateControlls(float dt)
	{
		if (m_nview.IsOwner())
		{
			m_nview.GetZDO().Set(ZDOVars.s_forward, (int)m_speed);
			m_nview.GetZDO().Set(ZDOVars.s_rudder, m_rudderValue);
			return;
		}
		m_speed = (Speed)m_nview.GetZDO().GetInt(ZDOVars.s_forward);
		if (Time.time - m_sendRudderTime > 1f)
		{
			m_rudderValue = m_nview.GetZDO().GetFloat(ZDOVars.s_rudder);
		}
	}

	public bool IsSailUp()
	{
		if (m_speed != Speed.Half)
		{
			return m_speed == Speed.Full;
		}
		return true;
	}

	private void UpdateSail(float dt)
	{
		UpdateSailSize(dt);
		Vector3 windDir = EnvMan.instance.GetWindDir();
		windDir = Vector3.Cross(Vector3.Cross(windDir, base.transform.up), base.transform.up);
		if (m_speed == Speed.Full || m_speed == Speed.Half)
		{
			float t = 0.5f + Vector3.Dot(base.transform.forward, windDir) * 0.5f;
			Quaternion to = Quaternion.LookRotation(-Vector3.Lerp(windDir, Vector3.Normalize(windDir - base.transform.forward), t), base.transform.up);
			m_mastObject.transform.rotation = Quaternion.RotateTowards(m_mastObject.transform.rotation, to, 30f * dt);
		}
		else if (m_speed == Speed.Back)
		{
			Quaternion from = Quaternion.LookRotation(-base.transform.forward, base.transform.up);
			Quaternion to2 = Quaternion.LookRotation(-windDir, base.transform.up);
			to2 = Quaternion.RotateTowards(from, to2, 80f);
			m_mastObject.transform.rotation = Quaternion.RotateTowards(m_mastObject.transform.rotation, to2, 30f * dt);
		}
	}

	private void UpdateRudder(float dt, bool haveControllingPlayer)
	{
		if (!m_rudderObject)
		{
			return;
		}
		Quaternion b = Quaternion.Euler(0f, m_rudderRotationMax * (0f - m_rudderValue), 0f);
		if (haveControllingPlayer)
		{
			if (m_speed == Speed.Slow)
			{
				m_rudderPaddleTimer += dt;
				b *= Quaternion.Euler(0f, Mathf.Sin(m_rudderPaddleTimer * 6f) * 20f, 0f);
			}
			else if (m_speed == Speed.Back)
			{
				m_rudderPaddleTimer += dt;
				b *= Quaternion.Euler(0f, Mathf.Sin(m_rudderPaddleTimer * -3f) * 40f, 0f);
			}
		}
		m_rudderObject.transform.localRotation = Quaternion.Slerp(m_rudderObject.transform.localRotation, b, 0.5f);
	}

	private void UpdateSailSize(float dt)
	{
		if (m_hasSail)
		{
			float num = 0f;
			switch (m_speed)
			{
			case Speed.Half:
				num = 0.5f;
				break;
			case Speed.Full:
				num = 1f;
				break;
			}
			m_sailPosition = Mathf.MoveTowards(m_sailPosition, num, dt);
			bool flag = Mathf.Approximately(m_sailPosition, num);
			Vector3 position = m_sailFurledPosition.position;
			Vector3 position2 = m_sailMidfurledPosition.position;
			if (m_sailPosition >= 0.5f)
			{
				position = m_sailMidfurledPosition.position;
				position2 = m_sailUnfurledPosition.position;
			}
			if (!flag && m_sailWasInPosition)
			{
				ZDOID zDOID = ((Player.m_localPlayer.GetPlayerID() == m_shipControlls.GetUser()) ? Player.m_localPlayer.GetZDOID() : ZDOID.None);
				Debug.Log($"now swapping sailpos, using {m_changeSailPosEffect.m_effectPrefabs[0].m_prefab.name}, for this local player: {zDOID}");
				m_changeSailPosEffect.Create(base.transform.position, Quaternion.identity, null, 1f, -1, zDOID);
			}
			float t = Utils.Frac(Mathf.Clamp(m_sailPosition, 0f, 0.999f) * 2f);
			m_sailBottomTransform.position = Vector3.Lerp(position, position2, t);
			float blendWeight = m_sailBlendWeightCurve.Evaluate(m_sailPosition);
			m_sailCloth.SerializeData.blendWeight = blendWeight;
			m_sailCloth.SetParameterChange();
			m_sailWasInPosition = flag;
		}
	}

	private void UpdateOwner()
	{
		if (m_nview.IsValid() && m_nview.IsOwner() && !(Player.m_localPlayer == null) && m_players.Count > 0 && !IsPlayerInBoat(Player.m_localPlayer))
		{
			RefreshPlayerList();
			long newOwnerID = GetNewOwnerID();
			m_nview.GetZDO().SetOwner(newOwnerID);
			ZLog.Log("Changing ship owner to " + newOwnerID);
		}
	}

	private long GetNewOwnerID()
	{
		long num = 0L;
		for (int i = 0; i < m_players.Count; i++)
		{
			num = m_players[i].GetOwner();
			if (num != 0L)
			{
				break;
			}
		}
		if (num == 0L)
		{
			num = ZDOMan.GetSessionID();
		}
		return num;
	}

	private void RefreshPlayerList()
	{
		for (int i = 0; i < m_players.Count; i++)
		{
			if (m_players[i].GetOwner() == 0L)
			{
				m_players.RemoveAt(i);
			}
		}
	}

	private void OnTriggerEnter(Collider collider)
	{
		Player component = collider.GetComponent<Player>();
		if ((bool)component)
		{
			m_players.Add(component);
			ZLog.Log("Player onboard, total onboard " + m_players.Count);
			if (component == Player.m_localPlayer)
			{
				s_currentShips.Add(this);
			}
		}
		Character component2 = collider.GetComponent<Character>();
		if ((bool)component2)
		{
			component2.InNumShipVolumes++;
		}
	}

	private void OnTriggerExit(Collider collider)
	{
		Player component = collider.GetComponent<Player>();
		if ((bool)component)
		{
			m_players.Remove(component);
			ZLog.Log("Player over board, players left " + m_players.Count);
			if (component == Player.m_localPlayer)
			{
				s_currentShips.Remove(this);
			}
		}
		Character component2 = collider.GetComponent<Character>();
		if ((bool)component2)
		{
			component2.InNumShipVolumes--;
		}
	}

	public bool IsPlayerInBoat(ZDOID zdoid)
	{
		foreach (Player player in m_players)
		{
			if (player.GetZDOID() == zdoid)
			{
				return true;
			}
		}
		return false;
	}

	public bool IsPlayerInBoat(Player player)
	{
		return m_players.Contains(player);
	}

	public bool IsPlayerInBoat(long playerID)
	{
		foreach (Player player in m_players)
		{
			if (player.GetPlayerID() == playerID)
			{
				return true;
			}
		}
		return false;
	}

	public bool HasPlayerOnboard()
	{
		return m_players.Count > 0;
	}

	private void OnDestroyed()
	{
		if (m_nview.IsValid() && m_nview.IsOwner())
		{
			Gogan.LogEvent("Game", "ShipDestroyed", base.gameObject.name, 0L);
		}
		s_currentShips.Remove(this);
	}

	public bool IsWindControllActive()
	{
		foreach (Player player in m_players)
		{
			if (player.GetSEMan().HaveStatusAttribute(StatusEffect.StatusAttribute.SailingPower))
			{
				return true;
			}
		}
		return false;
	}

	public static Ship GetLocalShip()
	{
		if (s_currentShips.Count != 0)
		{
			return s_currentShips[s_currentShips.Count - 1];
		}
		return null;
	}

	private bool HaveControllingPlayer()
	{
		if (m_players.Count != 0)
		{
			return m_shipControlls.HaveValidUser();
		}
		return false;
	}

	public bool IsOwner()
	{
		if (m_nview.IsValid())
		{
			return m_nview.IsOwner();
		}
		return false;
	}

	public float GetSpeed()
	{
		return Vector3.Dot(m_body.linearVelocity, base.transform.forward);
	}

	public Speed GetSpeedSetting()
	{
		return m_speed;
	}

	public float GetRudder()
	{
		return m_rudder;
	}

	public float GetRudderValue()
	{
		return m_rudderValue;
	}

	public float GetShipYawAngle()
	{
		Camera mainCamera = Utils.GetMainCamera();
		if (mainCamera == null)
		{
			return 0f;
		}
		return 0f - Utils.YawFromDirection(mainCamera.transform.InverseTransformDirection(base.transform.forward));
	}

	public float GetWindAngle()
	{
		Vector3 windDir = EnvMan.instance.GetWindDir();
		return 0f - Utils.YawFromDirection(base.transform.InverseTransformDirection(windDir));
	}

	private void OnDrawGizmosSelected()
	{
		Gizmos.color = Color.red;
		Gizmos.DrawWireSphere(base.transform.position + base.transform.forward * m_stearForceOffset, 0.25f);
		Gizmos.color = Color.yellow;
		Gizmos.DrawWireSphere(base.transform.position + base.transform.up * m_sailForceOffset, 0.25f);
	}
}

using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Companions;
using UnityEngine;

namespace WifeMod
{
    public enum WifeState { Idle, Following, Cooking, TendingFire, Breeding }

    public sealed class WifeCompanion : MonoBehaviour
    {
        private static readonly MethodInfo MoveToMethod = AccessTools.Method(typeof(BaseAI), "MoveTo", new[] { typeof(float), typeof(Vector3), typeof(float), typeof(bool) });
        private static readonly MethodInfo IsItemAllowedMethod = AccessTools.Method(typeof(CookingStation), "IsItemAllowed", new[] { typeof(ItemDrop.ItemData) });
        private static readonly MethodInfo CookItemMethod = AccessTools.Method(typeof(CookingStation), "CookItem", new[] { typeof(Humanoid), typeof(ItemDrop.ItemData) });
        private static readonly MethodInfo CurrentDayMethod = AccessTools.Method(typeof(EnvMan), "GetCurrentDay", System.Type.EmptyTypes);
        private static readonly MethodInfo StopSleepingMethod = AccessTools.Method(typeof(CompanionRest), "StopSleeping", System.Type.EmptyTypes);
        private static readonly MethodInfo WakeupMethod = AccessTools.Method(typeof(CompanionAI), "Wakeup", System.Type.EmptyTypes);
        private const string OwnerKey = "WifeMod_owner";
        private const string FollowKey = "WifeMod_follow";
        private const string ChildKey = "WifeMod_child";
        private const string PregnantKey = "WifeMod_pregnant";
        private const string DueDayKey = "WifeMod_dueDay";
        public float FollowDistance = 3f;
        public float FollowSpeed = 4f;
        public WifeState State { get; internal set; }

        private Character _character;
        private Humanoid _humanoid;
        private BaseAI _ai;
        private MonsterAI _monsterAi;
        private ZNetView _view;
        private float _workTimer;
        private Coroutine _cookRoutine;
        private float _pregnancyTimer;

        private void Awake()
        {
            _character = GetComponent<Character>();
            _humanoid = GetComponent<Humanoid>();
            _ai = GetComponent<BaseAI>();
            _monsterAi = GetComponent<MonsterAI>();
            _view = GetComponent<ZNetView>();
            ApplyFemalePlayerModel();
            if (_character != null) _character.SetTamed(true);
            if (_monsterAi != null) _monsterAi.enabled = false;
        }

        public bool Initialize(long owner, bool child)
        {
            if (_view == null || _view.GetZDO() == null) return false;
            _view.ClaimOwnership();
            ZDO zdo = _view.GetZDO();
            zdo.Set(OwnerKey, owner);
            zdo.Set(FollowKey, !child);
            zdo.Set(ChildKey, child);
            State = child ? WifeState.Idle : WifeState.Following;
            ApplyFemalePlayerModel();
            ApplyChildScale();
            return true;
        }

        private void Start()
        {
            ApplyChildScale();
            StartCoroutine(RecoverFromPersistedSleep());
        }

        private IEnumerator RecoverFromPersistedSleep()
        {
            yield return new WaitForSeconds(1f);
            ForceWake();
        }

        internal void ForceWake()
        {
            Character character = GetComponent<Character>();
            if (character != null && character.IsAttached()) character.AttachStop();
            CompanionRest rest = GetComponent<CompanionRest>();
            if (rest != null && StopSleepingMethod != null) StopSleepingMethod.Invoke(rest, null);
            CompanionAI companionAi = GetComponent<CompanionAI>();
            if (companionAi != null && WakeupMethod != null) WakeupMethod.Invoke(companionAi, null);
        }

        private void ApplyFemalePlayerModel()
        {
            VisEquipment equipment = GetComponent<VisEquipment>();
            if (equipment != null)
            {
                equipment.SetModel(1);
                equipment.SetBeardItem(string.Empty);
            }
        }

        private void ApplyChildScale()
        {
            if (_view == null || _view.GetZDO() == null || Plugin.WifeScale == null) return;
            float scale = Plugin.WifeScale.Value;
            if (_view.GetZDO().GetBool(ChildKey, false)) scale *= 0.65f;
            transform.localScale = Vector3.one * scale;
        }

        private void Update()
        {
            if (_view == null || !_view.IsOwner() || State == WifeState.Breeding || State == WifeState.TendingFire) return;
            CheckPregnancy();
            if (_cookRoutine != null) return;
            Player owner = FindOwner();
            bool following = GetZDO()?.GetBool(FollowKey, true) ?? true;
            State = following ? WifeState.Following : WifeState.Idle;
            if (following && owner != null) Follow(owner.transform.position);

            _workTimer -= Time.deltaTime;
            if (_workTimer <= 0f)
            {
                _workTimer = 15f;
                TryStartCooking();
            }
        }

        internal void MoveTo(Vector3 target, float stopDistance)
        {
            if (_ai != null && MoveToMethod != null) MoveToMethod.Invoke(_ai, new object[] { Time.deltaTime, target, stopDistance, true });
            else if (_character != null)
            {
                Vector3 direction = target - transform.position;
                direction.y = 0f;
                _character.SetMoveDir(direction.sqrMagnitude > 0.01f ? direction.normalized : Vector3.zero);
            }
        }

        private void Follow(Vector3 target)
        {
            if (Vector3.Distance(transform.position, target) > FollowDistance) MoveTo(target, FollowDistance);
            else if (_character != null) _character.SetMoveDir(Vector3.zero);
        }

        private Player FindOwner()
        {
            long ownerId = GetZDO()?.GetLong(OwnerKey, 0L) ?? 0L;
            foreach (Player player in Player.GetAllPlayers()) if (player.GetPlayerID() == ownerId) return player;
            return null;
        }

        private void TryStartCooking()
        {
            if (_humanoid == null || State == WifeState.TendingFire) return;
            Inventory inventory = _humanoid.GetInventory();
            if (inventory == null || inventory.NrOfItems() == 0) return;
            CookingStation bestStation = null;
            float bestDistance = 10f;
            foreach (Collider hit in Physics.OverlapSphere(transform.position, 10f))
            {
                CookingStation station = hit.GetComponentInParent<CookingStation>();
                if (station == null) continue;
                float distance = Vector3.Distance(transform.position, station.transform.position);
                if (distance < bestDistance) { bestDistance = distance; bestStation = station; }
            }
            if (bestStation == null) return;
            if (IsItemAllowedMethod == null || CookItemMethod == null) return;
            ItemDrop.ItemData selected = null;
            foreach (ItemDrop.ItemData item in new List<ItemDrop.ItemData>(inventory.GetAllItems()))
            {
                bool allowed = (bool)IsItemAllowedMethod.Invoke(bestStation, new object[] { item });
                if (allowed) { selected = item; break; }
            }
            if (selected != null) _cookRoutine = StartCoroutine(CookRoutine(bestStation, selected));
        }

        private IEnumerator CookRoutine(CookingStation station, ItemDrop.ItemData item)
        {
            WifeState previous = State;
            State = WifeState.Cooking;
            float timeout = 20f;
            while (station != null && Vector3.Distance(transform.position, station.transform.position) > 1.8f && timeout > 0f)
            {
                timeout -= Time.deltaTime;
                MoveTo(station.transform.position, 1.5f);
                yield return null;
            }

            if (station != null && timeout > 0f && _humanoid.GetInventory().ContainsItem(item))
            {
                bool allowed = (bool)IsItemAllowedMethod.Invoke(station, new object[] { item });
                bool cooked = allowed && (bool)CookItemMethod.Invoke(station, new object[] { _humanoid, item });
                if (cooked) Plugin.Log.LogDebug("Companion started cooking " + item.m_shared.m_name + ".");
            }
            State = previous == WifeState.Idle ? WifeState.Idle : WifeState.Following;
            _cookRoutine = null;
        }

        public string GetHoverName() => GetZDO()?.GetBool(ChildKey, false) == true ? "Child" : "Companion";
        public string GetHoverText() => GetHoverName() + "\n[<color=yellow><b>$KEY_Use</b></color>] Follow / stay\n[<color=yellow><b>1-8</b></color>] Give selected item";

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || !(user is Player player) || !IsOwnedBy(player)) return false;
            ZDO zdo = GetZDO();
            if (zdo == null || !_view.IsOwner()) return false;
            bool follow = !zdo.GetBool(FollowKey, true);
            zdo.Set(FollowKey, follow);
            player.Message(MessageHud.MessageType.Center, follow ? "Following" : "Staying here");
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
            if (!(user is Player player) || item == null || !IsOwnedBy(player) || _humanoid == null || !_view.IsOwner()) return false;
            ItemDrop.ItemData gift = item.Clone();
            gift.m_stack = 1;
            if (!_humanoid.GetInventory().CanAddItem(gift, 1)) { player.Message(MessageHud.MessageType.Center, "Companion inventory is full"); return true; }
            if (!player.GetInventory().RemoveItem(item, 1)) return false;
            _humanoid.GetInventory().AddItem(gift);
            player.Message(MessageHud.MessageType.Center, "Item transferred");
            return true;
        }

        private bool IsOwnedBy(Player player) => GetZDO()?.GetLong(OwnerKey, 0L) == player.GetPlayerID();
        private ZDO GetZDO() => _view != null ? _view.GetZDO() : null;
        public bool IsReadyToBreed() { ZDO zdo = GetZDO(); return zdo != null && !zdo.GetBool(ChildKey, false) && !zdo.GetBool(PregnantKey, false); }
        internal long OwnerId => GetZDO()?.GetLong(OwnerKey, 0L) ?? 0L;
        internal bool IsPregnant => GetZDO()?.GetBool(PregnantKey, false) ?? false;

        internal bool TryConceive(int currentDay, float chancePercent, int gestationDays)
        {
            ZDO zdo = GetZDO();
            if (zdo == null || !_view.IsOwner() || zdo.GetBool(ChildKey, false) || zdo.GetBool(PregnantKey, false)) return false;
            if (UnityEngine.Random.value * 100f >= Mathf.Clamp(chancePercent, 0f, 100f)) return false;
            zdo.Set(PregnantKey, true);
            zdo.Set(DueDayKey, currentDay + Mathf.Max(1, gestationDays));
            return true;
        }

        private void CheckPregnancy()
        {
            _pregnancyTimer -= Time.deltaTime;
            if (_pregnancyTimer > 0f) return;
            _pregnancyTimer = 5f;
            ZDO zdo = GetZDO();
            if (zdo == null || !zdo.GetBool(PregnantKey, false) || EnvMan.instance == null) return;
            int dueDay = zdo.GetInt(DueDayKey, int.MaxValue);
            if (GetCurrentDay() < dueDay) return;
            Player owner = FindOwner();
            if (owner == null) return;
            WifeCompanion child = WifePrefab.SpawnFor(owner, true);
            if (child == null) return;
            zdo.Set(PregnantKey, false);
            zdo.Set(DueDayKey, 0);
            owner.Message(MessageHud.MessageType.Center, "Your child has been born");
        }

        internal static int GetCurrentDay()
        {
            return EnvMan.instance != null && CurrentDayMethod != null ? (int)CurrentDayMethod.Invoke(EnvMan.instance, null) : 0;
        }

        internal int DueDay => GetZDO()?.GetInt(DueDayKey, 0) ?? 0;

        internal static void ShowNearestPregnancyStatus(Player player)
        {
            WifeCompanion nearest = null;
            float nearestDistance = 30f;
            foreach (Collider hit in Physics.OverlapSphere(player.transform.position, 30f))
            {
                WifeCompanion wife = hit.GetComponentInParent<WifeCompanion>();
                if (wife == null || wife.OwnerId != player.GetPlayerID()) continue;
                float distance = Vector3.Distance(player.transform.position, wife.transform.position);
                if (distance < nearestDistance) { nearestDistance = distance; nearest = wife; }
            }
            if (nearest == null) { player.Message(MessageHud.MessageType.Center, "No wife found nearby"); return; }
            if (!nearest.IsPregnant) { player.Message(MessageHud.MessageType.Center, "She is not pregnant"); return; }
            int remaining = Mathf.Max(0, nearest.DueDay - GetCurrentDay());
            player.Message(MessageHud.MessageType.Center, remaining == 0 ? "The child is due now" : $"She is pregnant — due in {remaining} day{(remaining == 1 ? "" : "s")}");
        }
    }
}

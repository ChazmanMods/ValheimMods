using System;
using System.Collections.Generic;
using HarmonyLib;
using RunicCrafting.Domain;
using UnityEngine;

namespace RunicCrafting.Integration
{
    internal static class AreaRepairRuntime
    {
        private delegate bool InputGate(Player player);
        private static InputGate _takeInput;
        private static bool _inputResolved;
        private static readonly AreaRepairBatch<Piece> Pending = new AreaRepairBatch<Piece>();
        private static readonly HashSet<string> HammerPieces = new HashSet<string>(StringComparer.Ordinal);
        private static Player _player;
        private static ZNet _network;
        private static Vector3 _origin;
        private static float _radius, _nextPulse, _nextStart;
        private static int _submitted;
        private static EffectList _repairSound;
        private static float _nextSound;

        internal static void Reset()
        {
            Pending.Clear(); HammerPieces.Clear(); _player = null; _network = null;
            _submitted = 0; _nextPulse = _nextStart = 0f;
            _repairSound = null; _nextSound = 0f;
        }

        internal static void Tick()
        {
            try
            {
                if (!Configuration.Enabled.Value || !Configuration.AreaRepairEnabled.Value)
                { Reset(); return; }
                Player player = Player.m_localPlayer;
                if (!ValheimReflection.CanMutateLocalPlayer(player) || ZNet.instance == null ||
                    player.IsDead() || player.IsTeleporting()) { Reset(); return; }
                if (Pending.Count > 0 && (!ReferenceEquals(_player, player) || !ReferenceEquals(_network, ZNet.instance))) Reset();
                if (!TakesGameplayInput(player)) return;
                if (Pending.Count == 0 && Configuration.AreaRepairKey.Value.IsDown() && Time.unscaledTime >= _nextStart)
                    Start(player);
                if (Pending.Count == 0 || Time.unscaledTime < _nextPulse) return;
                // Moving out of the starting area cannot expand a queued request's repair reach.
                if ((player.transform.position - _origin).sqrMagnitude > 4f ||
                    Math.Abs(AreaRepairPolicy.Radius(Configuration.AreaRepairRadius.Value) - _radius) > 0.01f)
                { Message(player, global::Runic.Localization.RunicText.Get("text_510736362d5b")); Reset(); return; }
                _nextPulse = Time.unscaledTime + 0.05f;
                int repaired = Pending.Step(piece => Repair(player, piece));
                _submitted += repaired;
                if (repaired > 0) PlayRepairFeedback(player);
                if (Pending.Count == 0)
                {
                    Message(player, _submitted > 0 ? global::Runic.Localization.RunicText.Get("text_f3a2af40e71b") + _submitted + global::Runic.Localization.RunicText.Get("text_8debecd1e1de") :
                        global::Runic.Localization.RunicText.Get("text_757c04af9403"));
                    HammerPieces.Clear(); _player = null; _network = null;
                    _nextStart = Time.unscaledTime + 1f;
                }
            }
            catch (Exception error)
            {
                Reset();
                Plugin.Log?.LogWarning("Area repair stopped: " + error.Message);
                Message(Player.m_localPlayer, global::Runic.Localization.RunicText.Get("text_f1ff0afcea50"));
            }
        }

        private static bool TakesGameplayInput(Player player)
        {
            if (!_inputResolved)
            {
                _inputResolved = true;
                var method = AccessTools.Method(typeof(Player), "TakeInput", Type.EmptyTypes);
                if (method != null) _takeInput = AccessTools.MethodDelegate<InputGate>(method);
            }
            return _takeInput != null && _takeInput(player) && !Game.IsPaused() &&
                !InventoryGui.IsVisible() && !Minimap.IsOpen() && !Menu.IsVisible() &&
                !global::Console.IsVisible() && !TextInput.IsVisible() && !ZInput.VirtualKeyboardOpen &&
                !Hud.IsPieceSelectionVisible() && (Chat.instance == null || !Chat.instance.HasFocus()) &&
                Cursor.lockState == CursorLockMode.Locked;
        }

        private static void Start(Player player)
        {
            HammerPieces.Clear();
            _repairSound = null; _nextSound = 0f;
            // Resolve the registered Hammer table at each keypress, including mod-added hammer pieces.
            PieceTable table = ObjectDB.instance?.GetItemPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_buildPieces;
            if (table == null) { Message(player, global::Runic.Localization.RunicText.Get("text_355726b64c1b")); return; }
            foreach (GameObject prefab in table.m_pieces)
            {
                Piece piece = prefab != null ? prefab.GetComponent<Piece>() : null;
                if (piece != null && !piece.m_repairPiece && !piece.m_removePiece)
                    HammerPieces.Add(ValheimReflection.PiecePrefabId(piece));
                if (piece != null && ValheimReflection.PiecePrefabId(piece) == "woodwall")
                {
                    // Native manual repair uses the piece's placement effects. Keep just the
                    // wooden wall's sound effects, without spawning dust or debris at the player.
                    var sounds = new List<EffectList.EffectData>();
                    foreach (var effect in piece.m_placeEffect?.m_effectPrefabs ?? Array.Empty<EffectList.EffectData>())
                        if (effect != null && effect.m_enabled && effect.m_prefab != null && effect.m_prefab.GetComponent<ZSFX>() != null)
                            sounds.Add(new EffectList.EffectData { m_prefab = effect.m_prefab });
                    _repairSound = new EffectList { m_effectPrefabs = sounds.ToArray() };
                }
            }
            _player = player; _network = ZNet.instance; _origin = player.transform.position;
            _radius = AreaRepairPolicy.Radius(Configuration.AreaRepairRadius.Value); _submitted = 0;
            var pieces = new List<Piece>();
            Piece.GetAllPiecesInRadius(_origin, _radius, pieces);
            bool truncated = false;
            foreach (Piece piece in pieces)
            {
                if (piece == null || !HammerPieces.Contains(ValheimReflection.PiecePrefabId(piece))) continue;
                var wear = piece.GetComponent<WearNTear>();
                var view = piece.GetComponent<ZNetView>();
                if (wear == null || view == null || !view.IsValid() ||
                    view.GetZDO().GetFloat(ZDOVars.s_health, wear.m_health) >= wear.m_health) continue;
                if (!Pending.Add(piece)) { truncated = true; break; }
            }
            _nextStart = Time.unscaledTime + 1f;
            Message(player, Pending.Count == 0 ? global::Runic.Localization.RunicText.Get("text_9901ef28015a") :
                global::Runic.Localization.RunicText.Get("text_2800cf37e65d") + _radius + global::Runic.Localization.RunicText.Get("text_ec75af7e5f7c") +
                (truncated ? global::Runic.Localization.RunicText.Get("text_cf32903b99fe") : ""));
        }

        private static bool Repair(Player player, Piece piece)
        {
            if (piece == null || !piece.gameObject.activeInHierarchy) return false;
            var view = piece.GetComponent<ZNetView>();
            var wear = piece.GetComponent<WearNTear>();
            if (wear == null || view == null || !view.IsValid() || !view.HasOwner()) return false;
            Vector3 position = piece.transform.position;
            if ((position - _origin).sqrMagnitude > _radius * _radius ||
                (position - player.transform.position).sqrMagnitude > _radius * _radius) return false;
            if (!PrivateArea.CheckAccess(position, 0f, false)) return false;
            var container = piece.GetComponent<Container>();
            bool containerAccess = container == null || ValheimReflection.ContainerAllows(container, player.GetPlayerID());
            // Station coverage is checked at the remote structure, so a covered house can be
            // repaired from one spot without granting repairs to unprotected remote stations.
            bool stationAccess = piece.m_craftingStation == null || player.NoCostCheat() ||
                (ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoWorkbench));
            if (!stationAccess)
            {
                var station = CraftingStation.HaveBuildStationInRange(piece.m_craftingStation.m_name, position);
                stationAccess = station != null && CraftingRuntime.CanUseStation(station, player, out _);
            }
            if (!AreaRepairPolicy.Eligible(HammerPieces.Contains(ValheimReflection.PiecePrefabId(piece)),
                    true, true, true, containerAccess, stationAccess)) return false;
            // Native owner-directed RPC: no health/ZDO writes, ownership claims, or inventory mutations.
            return wear.Repair();
        }

        private static void PlayRepairFeedback(Player player)
        {
            if (_repairSound == null || Time.unscaledTime < _nextSound) return;
            _nextSound = Time.unscaledTime + .2f;
            try { _repairSound.Create(player.transform.position, Quaternion.identity); }
            catch (Exception error) { Plugin.Log?.LogWarning("Area repair sound unavailable: " + error.Message); }
        }

        private static void Message(Player player, string message) =>
            player?.Message(MessageHud.MessageType.TopLeft, global::Runic.Localization.RunicText.Get("text_3ebccbaa12dc") + message);
    }
}

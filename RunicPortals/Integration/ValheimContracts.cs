using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace RunicPortals.Integration
{
    internal enum WardState
    {
        None = 0,
        Allows = 1,
        Denies = 2,
        OverlappingHostile = 3,
        Ambiguous = 4
    }

    internal sealed class WardContext
    {
        internal WardContext(WardState state)
        {
            State = state;
        }

        internal WardState State { get; }
        internal static WardContext NoWard { get; } = new WardContext(WardState.None);
    }

    internal static class ValheimContracts
    {
        internal const string AuditedGameVersion = "1.0.15";
        internal const int MaximumPortalObjectsScanned = 4096;

        private static FieldInfo _portalObjectsField;
        private static FieldInfo _privateAreasField;
        private static MethodInfo _privateAreaEnabled;
        private static MethodInfo _privateAreaInside;
        private static MethodInfo _privateAreaPermitted;
        private static MethodInfo _zonePokeLocal;

        internal static bool Initialize(out string problem)
        {
            problem = string.Empty;
            try
            {
                RequireMethod(typeof(TeleportWorld), "Awake", Type.EmptyTypes, false, typeof(void));
                RequireMethod(typeof(TeleportWorld), nameof(TeleportWorld.Interact),
                    new[] { typeof(Humanoid), typeof(bool), typeof(bool) }, true, typeof(bool));
                RequireMethod(typeof(TeleportWorld), nameof(TeleportWorld.GetHoverText),
                    Type.EmptyTypes, true, typeof(string));
                RequireMethod(typeof(TeleportWorld), nameof(TeleportWorld.SetText),
                    new[] { typeof(string) }, true, typeof(void));
                RequireMethod(typeof(TeleportWorld), nameof(TeleportWorld.Teleport),
                    new[] { typeof(Player) }, true, typeof(void));
                RequireMethod(typeof(TeleportWorld), "HaveTarget", Type.EmptyTypes, false, typeof(bool));
                RequireMethod(typeof(TeleportWorld), "TargetFound", Type.EmptyTypes, false, typeof(bool));
                RequireMethod(typeof(Player), nameof(Player.IsTeleportable),
                    new[] { typeof(bool) }, true, typeof(bool));
                RequireMethod(typeof(Player), nameof(Player.TeleportTo),
                    new[] { typeof(Vector3), typeof(Quaternion), typeof(bool) }, true, typeof(bool));
                RequireMethod(typeof(Player), nameof(Player.GetPlayerID), Type.EmptyTypes, true, typeof(long));
                RequireMethod(typeof(Game), "FindRandomUnconnectedPortal",
                    new[] { typeof(List<ZDO>), typeof(ZDO), typeof(string) }, false, typeof(ZDO));
                RequireMethod(typeof(Piece), nameof(Piece.GetCreator), Type.EmptyTypes, true, typeof(long));
                RequireMethod(typeof(ZNetView), nameof(ZNetView.IsValid), Type.EmptyTypes, true, typeof(bool));
                RequireMethod(typeof(ZNetView), nameof(ZNetView.GetZDO), Type.EmptyTypes, true, typeof(ZDO));
                RequireMethod(typeof(ZNetView), nameof(ZNetView.IsOwner), Type.EmptyTypes, true, typeof(bool));
                RequireMethod(typeof(ZDO), nameof(ZDO.IsValid), Type.EmptyTypes, true, typeof(bool));
                RequireMethod(typeof(ZDO), nameof(ZDO.GetPosition), Type.EmptyTypes, true, typeof(Vector3));
                RequireMethod(typeof(ZDO), nameof(ZDO.GetRotation), Type.EmptyTypes, true, typeof(Quaternion));
                RequireMethod(typeof(ZDO), nameof(ZDO.GetConnectionZDOID),
                    new[] { typeof(ZDOExtraData.ConnectionType) }, true, typeof(ZDOID));
                RequireMethod(typeof(ZNet), nameof(ZNet.IsServer), Type.EmptyTypes, true, typeof(bool));
                RequireMethod(typeof(ZNet), nameof(ZNet.GetWorldUID), Type.EmptyTypes, true, typeof(long));
                RequireMethod(typeof(ZNet), nameof(ZNet.GetPeer), new[] { typeof(long) }, true, typeof(ZNetPeer));
                RequireMethod(typeof(ZDOMan), nameof(ZDOMan.GetZDO),
                    new[] { typeof(ZDOID) }, true, typeof(ZDO));
                RequireMethod(typeof(ZDOMan), nameof(ZDOMan.ForceSendZDO),
                    new[] { typeof(long), typeof(ZDOID) }, true, typeof(void));
                RequireMethod(typeof(ZNetScene), nameof(ZNetScene.GetPrefab),
                    new[] { typeof(int) }, true, typeof(GameObject));
                RequireMethod(typeof(TextInput), nameof(TextInput.RequestText),
                    new[] { typeof(TextReceiver), typeof(string), typeof(int) }, true, typeof(void));
                RequireMethod(typeof(TextInput), nameof(TextInput.Hide), Type.EmptyTypes, true, typeof(void));
                RequireStaticMethod(typeof(ZInput), nameof(ZInput.IsGamepadActive),
                    Type.EmptyTypes, true, typeof(bool));
                RequireMethod(typeof(ZInput), nameof(ZInput.GetBoundKeyString),
                    new[] { typeof(string), typeof(bool) }, true, typeof(string));
                RequireMethod(typeof(ZInput), nameof(ZInput.GetButtonDef),
                    new[] { typeof(string) }, true, typeof(ZInput.ButtonDef));
                RequireMethod(typeof(ZInput.ButtonDef), nameof(ZInput.ButtonDef.GetActionPath),
                    new[] { typeof(bool) }, true, typeof(string));
                RequireMethod(typeof(Localization), nameof(Localization.Localize),
                    new[] { typeof(string) }, true, typeof(string));
                RequireMethod(typeof(Game), nameof(Game.IncrementPlayerStat),
                    new[] { typeof(PlayerStatType), typeof(float), typeof(bool) }, true, typeof(void));
                RequireMethod(typeof(ZoneSystem), nameof(ZoneSystem.GetGlobalKey),
                    new[] { typeof(GlobalKeys) }, true, typeof(bool));
                RequireMethod(typeof(ZoneSystem), nameof(ZoneSystem.GetGlobalKey),
                    new[] { typeof(GlobalKeys), typeof(float).MakeByRefType() }, true, typeof(bool));
                RequireMethod(typeof(ZoneSystem), nameof(ZoneSystem.IsZoneLoaded),
                    new[] { typeof(Vector3) }, true, typeof(bool));
                _zonePokeLocal = RequireMethod(typeof(ZoneSystem), "PokeLocalZone",
                    new[] { typeof(Vector2s) }, false, typeof(bool));
                RequireMethod(typeof(RandEventSystem), nameof(RandEventSystem.GetBossEvent),
                    Type.EmptyTypes, true, typeof(string));

                RequireField(typeof(TeleportWorld), nameof(TeleportWorld.m_allowAllItems), typeof(bool), true);
                RequireField(typeof(TeleportWorld), nameof(TeleportWorld.m_exitDistance), typeof(float), true);
                RequireField(typeof(ZDO), nameof(ZDO.m_uid), typeof(ZDOID), true);
                RequireField(typeof(ZNetPeer), nameof(ZNetPeer.m_uid), typeof(long), true);
                RequireField(typeof(ZNetPeer), nameof(ZNetPeer.m_characterID), typeof(ZDOID), true);

                _portalObjectsField = typeof(ZDOMan).GetField(
                    "m_portalObjects", BindingFlags.Instance | BindingFlags.NonPublic) ??
                    throw new MissingFieldException(typeof(ZDOMan).FullName, "m_portalObjects");
                _privateAreasField = typeof(PrivateArea).GetField(
                    "m_allAreas", BindingFlags.Static | BindingFlags.NonPublic) ??
                    throw new MissingFieldException(typeof(PrivateArea).FullName, "m_allAreas");
                _privateAreaEnabled = RequireMethod(typeof(PrivateArea), "IsEnabled",
                    Type.EmptyTypes, false, typeof(bool));
                _privateAreaInside = RequireMethod(typeof(PrivateArea), "IsInside",
                    new[] { typeof(Vector3), typeof(float) }, false, typeof(bool));
                _privateAreaPermitted = RequireMethod(typeof(PrivateArea), "IsPermitted",
                    new[] { typeof(long) }, false, typeof(bool));
                return true;
            }
            catch (Exception exception)
            {
                problem = exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        internal static List<ZDO> PortalObjects()
        {
            if (ZDOMan.instance == null) return null;
            object source = _portalObjectsField?.GetValue(ZDOMan.instance);
            if (source is List<ZDO> legacy) return legacy;
            if (!(source is IDictionary sectors)) return null;

            var portals = new List<ZDO>();
            foreach (DictionaryEntry sector in sectors)
            {
                if (!(sector.Value is IEnumerable members)) return null;
                foreach (object member in members)
                {
                    if (!(member is ZDO portal)) return null;
                    portals.Add(portal);
                    // Return a deliberately over-limit result so every caller follows its
                    // existing bounded-snapshot rejection path without walking the world.
                    if (portals.Count > MaximumPortalObjectsScanned) return portals;
                }
            }
            return portals;
        }

        internal static bool IsServer => ZNet.instance != null && ZNet.instance.IsServer();

        internal static bool HasLocalPlayerAuthority
        {
            get
            {
                Player player = Player.m_localPlayer;
                ZNetView view = player == null ? null : player.GetComponent<ZNetView>();
                return player != null && view != null && view.IsValid() && view.IsOwner();
            }
        }

        internal static string ReadGameVersion()
        {
            Type version = typeof(Player).Assembly.GetType("Version", true);
            PropertyInfo property = version.GetProperty(
                "CurrentVersion", BindingFlags.Public | BindingFlags.Static);
            return property?.GetValue(null, null)?.ToString() ?? string.Empty;
        }

        internal static WardContext ResolveWard(Vector3 position, long playerId)
        {
            if (playerId == 0L || ZoneSystem.instance == null ||
                !ZoneSystem.instance.IsZoneLoaded(position))
                return new WardContext(WardState.Ambiguous);
            var areas = _privateAreasField?.GetValue(null) as List<PrivateArea>;
            if (areas == null) return new WardContext(WardState.Ambiguous);
            bool found = false;
            bool allowed = false;
            bool denied = false;
            for (int index = 0; index < areas.Count; index++)
            {
                PrivateArea area = areas[index];
                if (area == null || !(bool)_privateAreaEnabled.Invoke(area, null) ||
                    !(bool)_privateAreaInside.Invoke(area, new object[] { position, 0f })) continue;
                found = true;
                Piece piece = area.GetComponent<Piece>();
                long creator = piece == null ? 0L : piece.GetCreator();
                if (creator == 0L) return new WardContext(WardState.Ambiguous);
                bool permitted = creator == playerId ||
                                 (bool)_privateAreaPermitted.Invoke(area, new object[] { playerId });
                allowed |= permitted;
                denied |= !permitted;
            }
            if (!found) return WardContext.NoWard;
            if (denied) return new WardContext(
                allowed ? WardState.OverlappingHostile : WardState.Denies);
            return new WardContext(WardState.Allows);
        }

        internal static bool WardAllows(WardContext ward) =>
            ward != null && (ward.State == WardState.None || ward.State == WardState.Allows);

        /// <summary>
        /// Resolves strict source-portal ward authority. A not-yet-loaded source zone remains
        /// denied for this attempt, but the authoritative server is asked to load that exact
        /// nearby zone so a bounded client retry can obtain real ward evidence. A known hostile
        /// ward is never converted into a transient result.
        /// </summary>
        internal static bool SourceWardAllows(
            Vector3 position,
            long playerId,
            out bool evidencePending)
        {
            WardContext ward = ResolveWard(position, playerId);
            evidencePending = ward != null && ward.State == WardState.Ambiguous;
            if (!evidencePending) return WardAllows(ward);
            RequestSourceZone(position);
            return false;
        }

        private static void RequestSourceZone(Vector3 position)
        {
            ZoneSystem zones = ZoneSystem.instance;
            if (zones == null || !IsServer || zones.IsZoneLoaded(position)) return;
            try
            {
                _zonePokeLocal?.Invoke(
                    zones,
                    new object[] { ZoneSystem.GetZone(position) });
            }
            catch
            {
                // The caller still fails closed and the bounded retry eventually reports that
                // source ward evidence remained unavailable.
            }
        }

        /// <summary>
        /// A remote portal normally belongs to an unloaded zone, so the client cannot obtain a
        /// native PrivateArea instance for it before teleporting. Unknown remote ward evidence is
        /// therefore not itself a denial. A loaded, known hostile ward remains authoritative and
        /// fails closed. Source portals and edits continue to use the stricter WardAllows check.
        /// </summary>
        internal static bool DestinationWardAllows(WardContext ward) =>
            ward != null && ward.State != WardState.Denies &&
            ward.State != WardState.OverlappingHostile;

        private static MethodInfo RequireMethod(
            Type type,
            string name,
            Type[] parameters,
            bool isPublic,
            Type returnType)
        {
            BindingFlags flags = BindingFlags.Instance |
                                 (isPublic ? BindingFlags.Public : BindingFlags.NonPublic);
            MethodInfo method = type.GetMethod(name, flags, null, parameters, null);
            if (method == null || method.ReturnType != returnType)
                throw new MissingMethodException(type.FullName, name);
            return method;
        }

        private static MethodInfo RequireStaticMethod(
            Type type,
            string name,
            Type[] parameters,
            bool isPublic,
            Type returnType)
        {
            BindingFlags flags = BindingFlags.Static |
                                 (isPublic ? BindingFlags.Public : BindingFlags.NonPublic);
            MethodInfo method = type.GetMethod(name, flags, null, parameters, null);
            if (method == null || method.ReturnType != returnType)
                throw new MissingMethodException(type.FullName, name);
            return method;
        }

        private static FieldInfo RequireField(
            Type type,
            string name,
            Type fieldType,
            bool isPublic)
        {
            BindingFlags flags = BindingFlags.Instance |
                                 (isPublic ? BindingFlags.Public : BindingFlags.NonPublic);
            FieldInfo field = type.GetField(name, flags);
            if (field == null || field.FieldType != fieldType)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }
    }
}

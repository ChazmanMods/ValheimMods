using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ArcaneSniper
{
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "chazman.ArcaneSniper", Name = "ArcaneSniper", Version = "0.3.0";
        internal const float OneMile = 1609.344f;
        internal static Plugin Instance;
        private Harmony harmony;
        private ConfigEntry<bool> enabledMod, killTamed, invertY;
        private ConfigEntry<KeyboardShortcut> toggle;
        private ConfigEntry<float> speed, boost, radius, range, sensitivity, dwell, cooldown, volume;
        private ConfigEntry<float> size, gap, thickness;
        private ConfigEntry<Color> color, hitColor;
        private ConfigEntry<ReticleStyle> style;
        private ConfigEntry<bool> autoPickup;
        private ConfigEntry<float> pickupRange;
        private ConfigEntry<bool> scopeEnabled;
        private ConfigEntry<KeyboardShortcut> scopeKey;
        private ConfigEntry<float> scopeZoom, scopeMaxZoom, scopeSensitivity;
        private float currentZoom;
        private bool scoped;
        private readonly PickupCollector collector = new PickupCollector();
        private readonly AimGate gate = new AimGate();
        private readonly HashSet<int> pending = new HashSet<int>();
        private GameCamera flightCamera;
        private Camera cameraComponent;
        private Player pilot;
        private Vector3 position;
        private float yaw, pitch, flashUntil, armAt;
        private AudioSource sound;
        private bool warnedSound;
        private bool active;
        private float savedFarClip;
        private ConfigEntry<bool> streamWorld;
        private ConfigEntry<KeyboardShortcut> dungeonKey, returnKey;
        private Vector3 tetherAnchor, entrancePosition;
        private bool remoteInterior;
        private readonly Collider[] entranceColliders = new Collider[32];
        internal void ReportWorldError(string context, Exception error) => Logger.LogWarning(context + ": " + error.Message);
        public enum ReticleStyle { Cross, Dot, Circle, CrossAndDot }

        private ConfigEntry<float> Number(string section, string key, float value, float min, float max, string help) =>
            Config.Bind(section, key, value, new ConfigDescription(help, new AcceptableValueRange<float>(min, max)));

        private void Awake()
        {
            Instance = this;
            enabledMod = Config.Bind("General", "Enabled", true, "Enable ArcaneSniper.");
            toggle = Config.Bind("General", "ToggleKey", new KeyboardShortcut(KeyCode.F9), "Enter/leave sniper camera. Escape also exits.");
            speed = Number("Camera", "Speed", 15, 1, 100, "Flight speed in meters per second. WASD moves; Space rises; Ctrl descends.");
            boost = Number("Camera", "SprintMultiplier", 3, 1, 10, "Hold Shift for faster flight.");
            radius = Number("Camera", "MaxDistanceFromPlayer", OneMile, 5, OneMile, "Camera tether in meters, up to one mile horizontally from your body. Dungeon interiors use their own vertical origin. Your body stays in place.");
            streamWorld = Config.Bind("World", "EnableDistantActivation", true, "Load terrain, creatures and normal spawns around the camera while keeping your body area loaded. Multiplayer requires ArcaneSniper on the server and using clients.");
            dungeonKey = Config.Bind("World", "DungeonInteractKey", new KeyboardShortcut(KeyCode.E), "Press near a dungeon entrance or exit to move only the camera through its linked passage.");
            returnKey = Config.Bind("World", "ReturnFromDungeonKey", new KeyboardShortcut(KeyCode.Backspace), "Return the camera to the entrance used to enter a remote dungeon.");
            sensitivity = Number("Camera", "MouseSensitivity", 2, 0.1f, 10, "Mouse-look sensitivity.");
            invertY = Config.Bind("Camera", "InvertY", false, "Invert vertical mouse look.");
            range = Number("Targeting", "Range", OneMile, 1, OneMile, "Maximum firing range in meters, up to one mile. Left-click fires at loaded creatures; scenery blocks shots.");
            dwell = Number("Targeting", "AimDelay", 0, 0, 5, "Optional steady-aim time before a click can fire. Zero means click immediately. An early click is discarded; it never fires later by itself.");
            cooldown = Number("Targeting", "Cooldown", 0.15f, 0.05f, 5, "Minimum interval between targets.");
            killTamed = Config.Bind("Targeting", "KillTamedCreatures", false, "Allow targeting tame creatures. Players are always excluded.");
            autoPickup = Config.Bind("Auto Pickup", "Enabled", true, "Collect eligible loaded loose loot directly into inventory. Respects vanilla pickup toggle, carry weight and inventory space. ArcaneHunter is not required.");
            pickupRange = Number("Auto Pickup", "Range", OneMile, 2, OneMile, "Pickup range in meters. Centered on the sniper camera while active, otherwise your body. Loaded drops only; no containers or tombstones.");
            scopeEnabled = Config.Bind("Scope", "Enabled", true, "Enable hold-to-zoom scope in sniper mode.");
            scopeKey = Config.Bind("Scope", "ZoomKey", new KeyboardShortcut(KeyCode.Mouse1), "Hold this key to zoom. Default: right mouse button. Left mouse button always fires.");
            scopeZoom = Number("Scope", "Magnification", 4, 1, 40, "Starting optical magnification when entering sniper mode. Scroll while scoped to change zoom.");
            scopeMaxZoom = Number("Scope", "MaximumMagnification", 20, 1, 40, "Maximum wheel zoom. Minimum is 1x.");
            scopeSensitivity = Number("Scope", "SensitivityMultiplier", 1, 0.1f, 3, "Scoped sensitivity, additionally divided by magnification for precise aiming.");
            var configRevision = Config.Bind("General", "ConfigRevision", 0, "Internal settings migration version.");
            if (configRevision.Value < 2)
            {
                // Upgrade only the previous defaults; retain custom ranges and cosmetic settings.
                if (radius.Value == 100f) radius.Value = OneMile;
                if (range.Value == 150f) range.Value = OneMile;
                configRevision.Value = 2;
            }
            volume = Number("Audio", "Volume", 0.8f, 0, 1, "Local bird-death sound volume for confirmed kills.");
            style = Config.Bind("Reticle", "Style", ReticleStyle.CrossAndDot, "Cross, Dot, Circle, or CrossAndDot.");
            size = Number("Reticle", "Size", 24, 4, 120, "Reticle diameter in pixels.");
            gap = Number("Reticle", "Gap", 5, 0, 40, "Cross center gap in pixels.");
            thickness = Number("Reticle", "Thickness", 2, 1, 10, "Line/dot thickness in pixels.");
            color = Config.Bind("Reticle", "Color", new Color(0.2f, 1f, 0.9f, 1), "Normal reticle RGBA color.");
            hitColor = Config.Bind("Reticle", "KillColor", new Color(1f, 0.25f, 0.3f, 1), "Reticle flash color after a confirmed kill.");
            sound = gameObject.AddComponent<AudioSource>();
            sound.playOnAwake = false;
            sound.spatialBlend = 0;
            harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(Plugin).Assembly);
            Logger.LogInfo(Name + " " + Version + " loaded. F9 toggles sniper camera.");
        }

        private static bool Busy() => !Application.isFocused ||
            (Chat.instance && Chat.instance.HasFocus()) || Console.IsVisible() || TextInput.IsVisible() ||
            StoreGui.IsVisible() || InventoryGui.IsVisible() || Menu.IsVisible() || Minimap.IsOpen() ||
            (TextViewer.instance && TextViewer.instance.IsVisible()) || Hud.InRadial() || Hud.IsPieceSelectionVisible() ||
            UnifiedPopup.IsVisible() || PlayerCustomizaton.IsBarberGuiVisible();

        private static bool Usable(Player p) => p && !p.IsDead() && !p.IsTeleporting() && !p.InCutscene();

        private void Update()
        {
            if (active && (!enabledMod.Value || !Usable(pilot) || pilot != Player.m_localPlayer ||
                !flightCamera || GameCamera.instance != flightCamera || !GameCamera.InFreeFly())) StopFlight();
            if (active && Input.GetKeyDown(KeyCode.Escape)) { StopFlight(); return; }
            CameraWorld.Tick(active && enabledMod.Value && streamWorld.Value, position);
            if (!enabledMod.Value || Busy() || !toggle.Value.IsDown()) return;
            if (active) { StopFlight(); return; }
            if (!Usable(Player.m_localPlayer) || !GameCamera.instance) return;
            if (GameCamera.InFreeFly())
            {
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, "Exit the other free camera before starting ArcaneSniper.");
                return;
            }
            StartFlight();
        }

        private void StartFlight()
        {
            pilot = Player.m_localPlayer;
            flightCamera = GameCamera.instance;
            cameraComponent = flightCamera.GetComponent<Camera>();
            savedFarClip = cameraComponent.farClipPlane;
            cameraComponent.farClipPlane = Mathf.Max(savedFarClip, OneMile + 100);
            currentZoom = Mathf.Clamp(scopeZoom.Value, 1, scopeMaxZoom.Value);
            scoped = false;
            position = flightCamera.transform.position;
            tetherAnchor = pilot.transform.position;
            remoteInterior = false;
            yaw = flightCamera.transform.eulerAngles.y;
            pitch = Mathf.DeltaAngle(0, flightCamera.transform.eulerAngles.x);
            gate.Clear();
            armAt = Time.unscaledTime + 0.3f;
            flightCamera.ToggleFreeFly();
            active = true;
            pilot.SetControls(Vector3.zero, false, false, false, false, false, false, false, false, false, false);
            pilot.Message(MessageHud.MessageType.Center, "ArcaneSniper: left-click fires | RMB scopes | " + dungeonKey.Value + " at dungeon passages | F9 or Esc exits");
        }

        private void StopFlight()
        {
            if (!active) return;
            active = false;
            CameraWorld.Stop();
            remoteInterior = false;
            scoped = false;
            if (cameraComponent)
            {
                cameraComponent.farClipPlane = savedFarClip;
                if (flightCamera) cameraComponent.fieldOfView = flightCamera.m_fov;
            }
            if (flightCamera && flightCamera.m_skyCamera) flightCamera.m_skyCamera.fieldOfView = flightCamera.m_fov;
            if (flightCamera && GameCamera.instance == flightCamera && GameCamera.InFreeFly()) flightCamera.ToggleFreeFly();
            gate.Clear();
            pending.Clear();
            StopAllCoroutines();
            flightCamera = null;
            cameraComponent = null;
            pilot = null;
        }

        private void Fly(GameCamera camera, float dt)
        {
            if (Busy() || Time.timeScale == 0 || Cursor.lockState != CursorLockMode.Locked)
            {
                gate.Clear();
                scoped = false;
                ApplyFieldOfView(camera);
                camera.transform.SetPositionAndRotation(position, Quaternion.Euler(pitch, yaw, 0));
                return;
            }
            scoped = scopeEnabled.Value && scopeKey.Value.IsPressed();
            if (scoped)
            {
                float wheel = ZInput.GetMouseScrollWheel();
                currentZoom = Mathf.Clamp(currentZoom + (wheel > 0 ? 1 : wheel < 0 ? -1 : 0), 1, scopeMaxZoom.Value);
            }
            Vector2 look = ZInput.GetMouseDelta();
            float lookSensitivity = sensitivity.Value * (scoped ? scopeSensitivity.Value / currentZoom : 1);
            yaw += look.x * lookSensitivity;
            pitch = Mathf.Clamp(pitch + look.y * lookSensitivity * (invertY.Value ? 1 : -1), -89, 89);
            var rotation = Quaternion.Euler(pitch, yaw, 0);
            if (streamWorld.Value && CameraWorld.Ready)
            {
                if (remoteInterior && returnKey.Value.IsDown())
                {
                    if (!DungeonExitBlocked())
                    {
                        position = entrancePosition; remoteInterior = false; tetherAnchor = pilot.transform.position; gate.Clear();
                    }
                }
                else if (dungeonKey.Value.IsDown()) TravelDungeon();
            }
            var motion = new Vector3((Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0), 0,
                (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0));
            motion = rotation * motion + Vector3.up * ((Input.GetKey(KeyCode.Space) ? 1 : 0) - (Input.GetKey(KeyCode.LeftControl) ? 1 : 0));
            position += Vector3.ClampMagnitude(motion, 1) * speed.Value * (Input.GetKey(KeyCode.LeftShift) ? boost.Value : 1) * Mathf.Min(dt, 0.05f);
            if (!remoteInterior) tetherAnchor = pilot.transform.position;
            position = tetherAnchor + Vector3.ClampMagnitude(position - tetherAnchor, radius.Value);
            var horizontal = position - pilot.transform.position; horizontal.y = 0;
            var limited = Vector3.ClampMagnitude(horizontal, radius.Value);
            position.x = pilot.transform.position.x + limited.x;
            position.z = pilot.transform.position.z + limited.z;
            camera.transform.SetPositionAndRotation(position, rotation);
            ApplyFieldOfView(camera);
            if (Time.unscaledTime >= armAt) Aim();
        }

        private void TravelDungeon()
        {
            // Fixed-size local overlap only on an interaction press, never a scene scan.
            int count = Physics.OverlapSphereNonAlloc(position, 3, entranceColliders, Physics.AllLayers, QueryTriggerInteraction.Collide);
            Teleport passage = null; float closest = 9;
            for (int i = 0; i < count; i++)
            {
                var collider = entranceColliders[i]; entranceColliders[i] = null;
                var candidate = collider.GetComponentInParent<Teleport>();
                if (!candidate || !candidate.m_targetPoint) continue;
                float distance = (collider.ClosestPoint(position) - position).sqrMagnitude;
                if (distance > closest) continue;
                passage = candidate; closest = distance;
            }
            if (!passage) return;
            // Keep the game's boss-exit restriction. This changes no player stats or teleport state.
            if (DungeonExitBlocked()) return;
            var target = passage.m_targetPoint.transform;
            var destination = target.position + target.forward - target.up;
            if (!WorldGeometry.ValidFocus(destination.x, destination.y, destination.z, pilot.transform.position.x, pilot.transform.position.z, radius.Value)) return;
            bool separated = Mathf.Abs(destination.y - pilot.transform.position.y) > 1000;
            if (separated && !remoteInterior) entrancePosition = position;
            remoteInterior = separated;
            tetherAnchor = separated ? destination : pilot.transform.position;
            position = destination;
            yaw = target.eulerAngles.y; pitch = 0; gate.Clear();
        }

        private bool DungeonExitBlocked()
        {
            if (!Character.InInterior(position) || !ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoBossPortals) || !Location.IsInsideActiveBossDungeon(position)) return false;
            pilot.Message(MessageHud.MessageType.Center, "$msg_blockedbyboss"); return true;
        }

        private void ApplyFieldOfView(GameCamera camera)
        {
            cameraComponent.fieldOfView = scoped ? ScopeMath.FieldOfView(camera.m_fov, currentZoom) : camera.m_fov;
            if (camera.m_skyCamera) camera.m_skyCamera.fieldOfView = cameraComponent.fieldOfView;
        }

        private void Aim()
        {
            // Use the actual camera center and the closest solid collider: no aiming through walls.
            var ray = cameraComponent.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
            var hits = Physics.RaycastAll(ray, range.Value, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                if (hit.collider.transform.IsChildOf(pilot.transform)) continue;
                Character character = hit.collider.GetComponentInParent<Character>();
                RandomFlyingBird bird = hit.collider.GetComponentInParent<RandomFlyingBird>();
                Destructible destructible = bird ? bird.GetComponent<Destructible>() : null;
                if (character && (character.IsPlayer() || character.IsDead() || character.IsTeleporting() || character.InCutscene() ||
                    (!killTamed.Value && character.IsTamed()))) { gate.Clear(); return; }
                if (!character && !destructible) { gate.Clear(); return; }
                Component target = character ? (Component)character : destructible;
                ZNetView view = target.GetComponent<ZNetView>();
                if (!view || !view.IsValid()) { gate.Clear(); return; }
                int id = target.GetInstanceID();
                if (pending.Contains(id)) return;
                if (!gate.Ready(id, Time.unscaledTime, dwell.Value, cooldown.Value, Input.GetMouseButtonDown(0))) return;
                var damage = new HitData
                {
                    m_point = hit.point, m_dir = ray.direction, m_hitCollider = hit.collider,
                    m_dodgeable = false, m_blockable = false, m_toolTier = short.MaxValue
                };
                // Raw damage uses vanilla ownership/RPC and death/drop handling, including on remote-owned creatures.
                damage.m_damage.m_damage = 1e9f;
                damage.SetAttacker(pilot);
                pending.Add(id);
                if (character) character.Damage(damage); else destructible.Damage(damage);
                StartCoroutine(ConfirmKill(target, character, view.GetZDO(), id));
                return;
            }
            gate.Clear();
        }

        private IEnumerator ConfirmKill(Component target, Character character, ZDO zdo, int id)
        {
            float deadline = Time.unscaledTime + 3;
            while (target && (!character || !character.IsDead()) && zdo.GetFloat(ZDOVars.s_health, 1) > 0 && Time.unscaledTime < deadline)
                yield return null;
            if (active && (!target || (character && character.IsDead()) || zdo.GetFloat(ZDOVars.s_health, 1) <= 0))
            {
                flashUntil = Time.unscaledTime + 0.2f;
                PlayBirdSound();
            }
            pending.Remove(id);
        }

        private void PlayBirdSound()
        {
            var prefab = ZNetScene.instance ? ZNetScene.instance.GetPrefab("sfx_crow_death") : null;
            var effect = prefab ? prefab.GetComponent<ZSFX>() : null;
            if (effect && effect.m_audioClips.Length > 0 && effect.m_audioClips[0])
            {
                sound.outputAudioMixerGroup = prefab.GetComponent<AudioSource>()?.outputAudioMixerGroup;
                sound.pitch = UnityEngine.Random.Range(0.9f, 1.1f);
                sound.PlayOneShot(effect.m_audioClips[0], volume.Value);
            }
            else if (!warnedSound) { warnedSound = true; Logger.LogWarning("Could not find vanilla sfx_crow_death audio."); }
        }

        private void OnGUI()
        {
            if (!active || Busy() || Event.current.type != EventType.Repaint) return;
            Color previous = GUI.color;
            GUI.color = Time.unscaledTime < flashUntil ? hitColor.Value : color.Value;
            float x = Screen.width / 2f, y = Screen.height / 2f, t = thickness.Value, r = size.Value / 2f;
            float g = Mathf.Min(gap.Value / 2f, r - 1);
            if (style.Value == ReticleStyle.Cross || style.Value == ReticleStyle.CrossAndDot)
            {
                Box(x - r, y - t / 2, r - g, t); Box(x + g, y - t / 2, r - g, t);
                Box(x - t / 2, y - r, t, r - g); Box(x - t / 2, y + g, t, r - g);
            }
            if (style.Value == ReticleStyle.Dot || style.Value == ReticleStyle.CrossAndDot) Box(x - t / 2, y - t / 2, t, t);
            if (style.Value == ReticleStyle.Circle)
                for (int i = 0; i < 96; i++)
                {
                    float a = i * Mathf.PI * 2 / 96;
                    Box(x + Mathf.Cos(a) * r - t / 2, y + Mathf.Sin(a) * r - t / 2, t, t);
                }
            GUI.color = Color.white;
            GUI.Label(new Rect(x - 300, y + r + 12, 650, 26), "ARCANE SNIPER — left-click fires | " + dungeonKey.Value + " dungeon passage | " + toggle.Value + " / Esc exits");
            GUI.Label(new Rect(x - 180, y + r + 38, 440, 26), scoped ?
                $"SCOPE {currentZoom:0.#}x | wheel adjusts zoom" : $"Hold {scopeKey.Value} for scope zoom");
            if (remoteInterior) GUI.Label(new Rect(x - 180, y + r + 64, 440, 26), returnKey.Value + " returns camera to entrance");
            else if (streamWorld.Value && !CameraWorld.Ready) GUI.Label(new Rect(x - 250, y + r + 64, 600, 26), "Distant activation requires ArcaneSniper on the server.");
            GUI.color = previous;
        }
        private static void Box(float x, float y, float w, float h) => GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
        private void FixedUpdate()
        {
            var player = Player.m_localPlayer;
            if (!enabledMod.Value || !autoPickup.Value || !Usable(player)) return;
            collector.Tick(player, active ? position : player.transform.position + Vector3.up, pickupRange.Value);
        }
        private void OnDisable() => StopFlight();
        private void OnDestroy()
        {
            StopFlight();
            harmony?.UnpatchSelf();
            if (sound) Destroy(sound);
            Instance = null;
        }

        [HarmonyPatch(typeof(GameCamera), "UpdateCamera")]
        private static class CameraPatch
        {
            private static bool Prefix(GameCamera __instance, float dt)
            {
                if (!Instance || !Instance.active || Instance.flightCamera != __instance) return true;
                if (!Usable(Instance.pilot)) { Instance.StopFlight(); return true; }
                Instance.Fly(__instance, dt);
                return false;
            }
        }
        [HarmonyPatch(typeof(Hud), "UpdateCrosshair")]
        private static class CrosshairPatch
        {
            private static void Postfix(Hud __instance)
            {
                if (!Instance || !Instance.active) return;
                __instance.m_crosshair.gameObject.SetActive(false);
                __instance.m_crosshairBow.gameObject.SetActive(false);
            }
        }

        // Vanilla free fly binds right-click to screenshots. Consume that action when it is the scope key.
        [HarmonyPatch(typeof(GameCamera), "ScreenShot")]
        private static class ScopeScreenshotPatch
        {
            private static bool Prefix() => !Instance || !Instance.active || !Instance.scopeEnabled.Value ||
                Instance.scopeKey.Value.MainKey != KeyCode.Mouse1 || !Input.GetMouseButtonDown(1);
        }
    }
}

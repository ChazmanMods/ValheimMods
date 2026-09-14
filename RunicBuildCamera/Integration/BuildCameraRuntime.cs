using BepInEx.Configuration;
using RunicBuildCamera.Core;
using UnityEngine;

namespace RunicBuildCamera.Integration
{
    /// <summary>Coordinates one local, detached, player-bounded camera session.</summary>
    internal static class BuildCameraRuntime
    {
        private static readonly BuildCameraSession Session = new BuildCameraSession();

        private static bool _initialized;
        private static bool _focused = true;

        internal static bool IsActive => _initialized && Session.IsActive;

        internal static void Initialize()
        {
            Session.End();
            _focused = Application.isFocused;
            _initialized = true;
        }

        internal static void Shutdown()
        {
            Stop();
            _initialized = false;
        }

        /// <summary>Called from the plugin's Update method.</summary>
        internal static void Tick()
        {
            if (!_initialized) return;

            Player player = Player.m_localPlayer;
            if (!ConfigEnabled() || !_focused)
            {
                Stop();
                return;
            }

            if (Session.IsActive &&
                (!SessionBelongsToUsablePlayer(player) || GameCamera.instance == null))
            {
                Stop();
                return;
            }

            KeyboardShortcut toggle = BuildCameraConfig.ToggleShortcut.Value;
            bool togglePressed = toggle.MainKey != KeyCode.None && toggle.IsDown();
            if (togglePressed)
            {
                if (Session.IsActive)
                    Stop();
                else if (ValheimAdapter.CanTakeInput(player))
                    TryStart(player);
                return;
            }

            if (!Session.IsActive) return;

            if (!ValheimAdapter.IsBuildToolEquipped(player))
            {
                Stop();
                return;
            }

            Session.Reanchor(player.transform.position);
            Session.UpdateRange(Positive(BuildCameraConfig.CameraRange.Value, 1f));
        }

        internal static void OnApplicationFocus(bool focused)
        {
            _focused = focused;
            if (!focused) Stop();
        }

        internal static void OnConfigurationChanged()
        {
            if (!_initialized) return;
            if (!ConfigEnabled())
            {
                Stop();
                return;
            }

            if (Session.IsActive)
                Session.UpdateRange(Positive(BuildCameraConfig.CameraRange.Value, 1f));
        }

        internal static void OnLocalPlayerAssigned(Player player)
        {
            if (Session.IsActive &&
                (player == null || !Session.BelongsTo(player.GetInstanceID())))
            {
                Stop();
            }
        }

        internal static bool ShouldFreezePlayer(Player player) =>
            IsActive && player != null && Session.BelongsTo(player.GetInstanceID()) &&
            ValheimAdapter.IsLocalPlayer(player);

        internal static ValheimAdapter.RangeLease EnterRemoteActionRange(Player player)
        {
            if (!ShouldFreezePlayer(player)) return null;
            float configuredRange = Positive(
                BuildCameraConfig.RemoteActionDistance.Value,
                1f);
            return ValheimAdapter.EnterRemoteActionRange(
                player,
                configuredRange,
                configuredRange);
        }

        internal static bool IsWithinRemoteActionLimit(Player player, Vector3 target)
        {
            if (!ShouldFreezePlayer(player) || player.m_eye == null || !IsFinite(target))
                return false;

            float limit = Positive(BuildCameraConfig.RemoteActionDistance.Value, 1f);
            Vector3 delta = target - player.m_eye.position;
            return IsFinite(delta) && (double)delta.sqrMagnitude <= (double)limit * limit;
        }

        /// <summary>
        /// Called by the GameCamera prefix. True means this runtime supplied the complete camera
        /// pose and the original GameCamera.UpdateCamera method must not run.
        /// </summary>
        internal static bool TryUpdateCamera(GameCamera camera, float deltaTime)
        {
            if (!IsActive || camera == null) return false;

            Player player = Player.m_localPlayer;
            if (!SessionBelongsToUsablePlayer(player))
            {
                Stop();
                return false;
            }

            Session.Reanchor(player.transform.position);
            bool inputOpen = _focused && ValheimAdapter.CanTakeInput(player) &&
                             !Console.IsVisible() &&
                             (Hud.instance == null || !Hud.IsPieceSelectionVisible());
            if (inputOpen)
                Session.SetPose(StepCamera(Session, deltaTime));

            camera.transform.SetPositionAndRotation(Session.Position, Session.Rotation);
            return true;
        }

        internal static bool TryGetActiveContext(
            out Player player,
            out Vector3 cameraPosition,
            out Quaternion cameraRotation)
        {
            player = Player.m_localPlayer;
            if (!IsActive || !SessionBelongsToUsablePlayer(player))
            {
                cameraPosition = Vector3.zero;
                cameraRotation = Quaternion.identity;
                return false;
            }

            cameraPosition = Session.Position;
            cameraRotation = Session.Rotation;
            return true;
        }

        internal static void ForceStop() => Stop();

        internal static bool ExitInputRequested()
        {
            if (CameraExitInput.HotbarRequested(ZInput.GetButtonDown)) return true;
            Player player = Player.m_localPlayer;
            bool shortRelease = !Hud.InRadial() && ZInput.GetButtonUp("JoyHide") &&
                                ZInput.GetButtonLastPressedTimer("JoyHide") < 0.33f;
            return CameraExitInput.HideRequested(
                ZInput.GetButtonDown("Hide"),
                (int)ZInput.InputLayout != 0 && ZInput.IsGamepadActive(),
                shortRelease, player.InPlaceMode(), ZInput.GetButton("JoyAltKeys"));
        }

        private static void TryStart(Player player)
        {
            if (!ValheimAdapter.IsBuildToolEquipped(player)) return;
            GameCamera camera = GameCamera.instance;
            if (camera == null) return;

            float range = Positive(BuildCameraConfig.CameraRange.Value, 1f);
            Session.Begin(
                player.GetInstanceID(),
                player.transform.position,
                camera.transform.position,
                camera.transform.rotation,
                range);
        }

        private static CameraPose StepCamera(BuildCameraSession session, float deltaTime)
        {
            float right = ButtonAxis("Right", "Left") + ZInput.GetJoyLeftStickX();
            float forward = ButtonAxis("Forward", "Backward") - ZInput.GetJoyLeftStickY();
            float up =
                ((ZInput.GetButton("Jump") || ZInput.GetButton("JoyJump")) ? 1f : 0f) -
                ((ZInput.GetButton("Crouch") || ZInput.GetButton("JoyCrouch")) ? 1f : 0f);

            float mouseHorizontal = Input.GetAxis("Mouse X") * PlayerController.m_mouseSens;
            float mouseVertical = Input.GetAxis("Mouse Y") * PlayerController.m_mouseSens;
            float controllerHorizontal = ZInput.GetJoyRightStickX() * 110f * deltaTime;
            float controllerVertical = ZInput.GetJoyRightStickY() * 110f * deltaTime;

            if (BuildCameraConfig.InvertMouseHorizontal.Value) mouseHorizontal = -mouseHorizontal;
            if (PlayerController.m_invertMouse) mouseVertical = -mouseVertical;
            if (BuildCameraConfig.InvertMouseVertical.Value) mouseVertical = -mouseVertical;
            if (BuildCameraConfig.InvertControllerHorizontal.Value)
                controllerHorizontal = -controllerHorizontal;
            if (BuildCameraConfig.InvertControllerVertical.Value)
                controllerVertical = -controllerVertical;

            CameraMotionInput input = new CameraMotionInput(
                right,
                up,
                forward,
                mouseHorizontal + controllerHorizontal,
                -mouseVertical + controllerVertical,
                ZInput.GetButton("Run") || ZInput.GetButton("JoyRun"));

            CameraPose pose = CameraMotion.Step(
                session.Position,
                session.Yaw,
                session.Pitch,
                session.Anchor,
                session.Range,
                in input,
                deltaTime,
                Positive(BuildCameraConfig.MoveSpeed.Value, 0f),
                Positive(BuildCameraConfig.FastMoveMultiplier.Value, 1f),
                BuildCameraConfig.WorldRelativeMovement.Value);

            if (ZoneSystem.instance != null &&
                ZoneSystem.instance.GetGroundHeight(pose.Position, out float groundHeight) &&
                pose.Position.y < groundHeight)
            {
                Vector3 position = pose.Position;
                position.y = groundHeight;
                position = CameraMotion.ClampToAnchor(position, session.Anchor, session.Range);
                pose = new CameraPose(position, pose.Yaw, pose.Pitch);
            }

            return pose;
        }

        private static float ButtonAxis(string positive, string negative) =>
            (ZInput.GetButton(positive) ? 1f : 0f) -
            (ZInput.GetButton(negative) ? 1f : 0f);

        private static bool SessionBelongsToUsablePlayer(Player player) =>
            player != null && Session.BelongsTo(player.GetInstanceID()) &&
            ValheimAdapter.IsUsableLocalPlayer(player);

        private static bool ConfigEnabled() =>
            BuildCameraConfig.Enabled != null && BuildCameraConfig.ToggleShortcut != null &&
            BuildCameraConfig.CameraRange != null && BuildCameraConfig.MoveSpeed != null &&
            BuildCameraConfig.FastMoveMultiplier != null &&
            BuildCameraConfig.WorldRelativeMovement != null &&
            BuildCameraConfig.RemoteActionDistance != null &&
            BuildCameraConfig.InvertMouseHorizontal != null &&
            BuildCameraConfig.InvertMouseVertical != null &&
            BuildCameraConfig.InvertControllerHorizontal != null &&
            BuildCameraConfig.InvertControllerVertical != null &&
            BuildCameraConfig.Enabled.Value;

        private static float Positive(float value, float fallback) =>
            !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f ? value : fallback;

        private static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);

        private static void Stop()
        {
            if (Session.IsActive)
                Session.End();
            ValheimAdapter.ForceRestoreRanges();
        }
    }
}

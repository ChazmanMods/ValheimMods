using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RunicHallOfHeroes.Echoes
{
    internal sealed class EchoSaga
    {
        internal const float FrameInterval = 0.2f;
        internal string HeroName;
        internal readonly List<Texture2D> Frames = new List<Texture2D>();
    }

    internal static class EchoRecorder
    {
        private const int CaptureWidth = 480;
        private const int CaptureHeight = 270;
        private const int MaxFrames = 50;
        private static readonly Queue<Texture2D> RollingFrames = new Queue<Texture2D>();

        internal static EchoSaga LastSaga { get; private set; }

        internal static IEnumerator CaptureLoop()
        {
            var endOfFrame = new WaitForEndOfFrame();
            var delay = new WaitForSecondsRealtime(EchoSaga.FrameInterval);
            while (true)
            {
                yield return delay;
                yield return endOfFrame;
                if (Player.m_localPlayer == null || Player.m_localPlayer.IsDead()) continue;
                Texture2D frame = CaptureFrame();
                if (frame == null) continue;
                RollingFrames.Enqueue(frame);
                while (RollingFrames.Count > MaxFrames)
                    Object.Destroy(RollingFrames.Dequeue());
            }
        }

        private static Texture2D CaptureFrame()
        {
            Texture2D full = null;
            RenderTexture scaled = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                full = ScreenCapture.CaptureScreenshotAsTexture();
                if (full == null) return null;
                scaled = RenderTexture.GetTemporary(
                    CaptureWidth, CaptureHeight, 0, RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.sRGB);
                Graphics.Blit(full, scaled);
                RenderTexture.active = scaled;
                var result = new Texture2D(
                    CaptureWidth, CaptureHeight, TextureFormat.RGB24, false, false);
                result.name = "RunicSagaFrame";
                result.ReadPixels(new Rect(0, 0, CaptureWidth, CaptureHeight), 0, 0, false);
                result.Apply(false, false);
                return result;
            }
            catch (System.Exception error)
            {
                Plugin.Log.LogWarning("Could not capture saga frame: " + error.Message);
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                if (scaled != null) RenderTexture.ReleaseTemporary(scaled);
                if (full != null) Object.Destroy(full);
            }
        }

        internal static void SealDeath(Player player)
        {
            if (player == null || player != Player.m_localPlayer || RollingFrames.Count < 2) return;
            DestroySaga(LastSaga);
            var saga = new EchoSaga { HeroName = player.GetPlayerName() };
            while (RollingFrames.Count > 0) saga.Frames.Add(RollingFrames.Dequeue());
            LastSaga = saga;
            Plugin.Log.LogInfo($"Sealed a {saga.Frames.Count}-frame viewpoint saga for {saga.HeroName}.");
        }

        private static void DestroySaga(EchoSaga saga)
        {
            if (saga == null) return;
            foreach (Texture2D frame in saga.Frames)
                if (frame != null) Object.Destroy(frame);
        }
    }
}

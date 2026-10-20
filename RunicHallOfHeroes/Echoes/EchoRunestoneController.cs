using System.Collections;
using UnityEngine;

namespace RunicHallOfHeroes.Echoes
{
    internal sealed class EchoRunestoneController : MonoBehaviour, Hoverable, Interactable
    {
        private Coroutine _playback;
        private GameObject _projection;

        public string GetHoverName() => "Echo Runestone";

        public string GetHoverText()
        {
            EchoSaga saga = EchoRecorder.LastSaga;
            return saga == null
                ? "<color=#70d8ff>Echo Runestone</color>\nNo captured saga is held in memory."
                : $"<color=#70d8ff>Echo Runestone</color>\n[<color=yellow><b>$KEY_Use</b></color>] Watch {saga.HeroName}'s final viewpoint";
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || user != Player.m_localPlayer || EchoRecorder.LastSaga == null) return false;
            StopProjection();
            _playback = StartCoroutine(PlayClip(EchoRecorder.LastSaga, user.transform));
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        private IEnumerator PlayClip(EchoSaga saga, Transform viewer)
        {
            _projection = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _projection.name = "RunicSagaProjection";
            Destroy(_projection.GetComponent<Collider>());
            Vector3 towardViewer = viewer.position - transform.position;
            towardViewer.y = 0f;
            if (towardViewer.sqrMagnitude < .1f) towardViewer = transform.forward;
            towardViewer.Normalize();
            _projection.transform.position = transform.position + towardViewer * 1.35f + Vector3.up * 1.65f;
            _projection.transform.rotation = Quaternion.LookRotation(-towardViewer, Vector3.up);
            _projection.transform.localScale = new Vector3(3.2f, 1.8f, 1f);

            Renderer renderer = _projection.GetComponent<Renderer>();
            Shader shader = Shader.Find("Unlit/Texture") ?? Shader.Find("Sprites/Default");
            renderer.material = new Material(shader);
            renderer.material.color = Color.white;

            var light = _projection.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(.2f, .65f, 1f);
            light.range = 4f;
            light.intensity = .8f;

            for (int i = 0; i < saga.Frames.Count; i++)
            {
                if (_projection == null) yield break;
                renderer.material.mainTexture = saga.Frames[i];
                yield return new WaitForSecondsRealtime(EchoSaga.FrameInterval);
            }

            yield return new WaitForSecondsRealtime(1.25f);
            StopProjection();
        }

        private void StopProjection()
        {
            if (_playback != null)
            {
                StopCoroutine(_playback);
                _playback = null;
            }
            if (_projection != null)
            {
                Destroy(_projection);
                _projection = null;
            }
        }

        private void OnDestroy() => StopProjection();
    }
}

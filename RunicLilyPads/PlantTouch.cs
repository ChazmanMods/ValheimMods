using UnityEngine;
namespace RunicLilyPads
{
    // Visual-only spring motion: the networked placement anchor never moves.
    public sealed class PlantTouch:MonoBehaviour
    {
        Vector3 rest,offset,velocity;Quaternion rotation;bool initialized;
        public float Radius=.85f,Travel=.32f;
        void Start(){rest=transform.localPosition;rotation=transform.localRotation;initialized=true;}
        void LateUpdate(){
            if(!initialized)return;
            Vector3 target=Vector3.zero;var center=transform.parent?transform.parent.TransformPoint(rest):rest;
            foreach(var player in Player.GetAllPlayers()){
                var delta=center-player.transform.position;if(Mathf.Abs(delta.y)>2)continue;delta.y=0;
                float distance=delta.magnitude;if(distance<Radius){if(distance<.02f)delta=player.transform.right;else delta/=distance;target+=delta*(1-distance/Radius)*Travel;}
            }
            target=Vector3.ClampMagnitude(target,Travel);if(transform.parent)target=transform.parent.InverseTransformVector(target);
            offset=Vector3.SmoothDamp(offset,target,ref velocity,.18f);transform.localPosition=rest+offset;
            transform.localRotation=rotation*Quaternion.Euler(offset.z*18,0,-offset.x*18);
        }
    }
}

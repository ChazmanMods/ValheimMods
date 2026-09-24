using UnityEngine;
namespace RunicLilyPads
{
    public sealed class GardenFloat:MonoBehaviour
    {
        // 0 rock (always floats), 1 lantern, 2 fountain, 3 statue.
        public int Kind;Transform visual;Vector3 rest;float rockCenter;
        void Awake(){visual=transform.Find("garden_visual");if(visual){rest=visual.localPosition;
                float low=float.PositiveInfinity,high=float.NegativeInfinity;
                foreach(var f in visual.GetComponentsInChildren<MeshFilter>(true)){
                    if(!f.sharedMesh)continue;var b=f.sharedMesh.bounds;
                    for(int i=0;i<8;i++){var v=b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));float y=transform.InverseTransformPoint(f.transform.TransformPoint(v)).y;low=Mathf.Min(low,y);high=Mathf.Max(high,y);}
                }
                if(!float.IsInfinity(low))rockCenter=(low+high)*.5f;
            }}
        internal bool Floating(GardenPiece pond){var s=pond?PondNetwork.GroupSettings(pond):new GardenSettings();return Kind==0||Kind==1&&s.FloatLanterns||Kind==2&&s.FloatFountains||Kind==3&&s.FloatStatues;}
        internal float Height(){var pond=PondNetwork.At(transform.position);float water=LilyFloat.WaterAt(transform.position);
            bool hasGround=Heightmap.GetHeight(transform.position,out var ground);
            if(water>-1000&&(!hasGround||water>ground+.02f)&&Floating(pond))return Kind==0?water-rockCenter*transform.lossyScale.y:water+.015f;
            if(Heightmap.GetHeight(transform.position,out var height))return height;return transform.position.y;
        }
        internal void PlaceGhost(){var p=transform.position;p.y=Height();transform.position=p;if(visual)visual.localPosition=rest;}
        void LateUpdate(){if(!visual||!ZoneSystem.instance)return;var p=visual.position;p.y=Height()+rest.y;visual.position=p;}
    }
    public sealed class GardenFlowerScale:MonoBehaviour
    {
        Transform visual;Vector3 scale;float next;
        void Start(){visual=transform.Find("lily_visual/floating_lily");if(!visual)visual=transform.Find("garden_visual");if(visual)scale=visual.localScale;}
        void Update(){if(!visual||Time.time<next)return;next=Time.time+.5f;var p=PondNetwork.At(transform.position);visual.localScale=scale*(p?PondNetwork.GroupSettings(p).FlowerScale:.5f);}
    }
}

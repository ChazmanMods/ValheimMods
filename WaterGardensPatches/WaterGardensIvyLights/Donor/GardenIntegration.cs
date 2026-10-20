using UnityEngine;

namespace RunicLilyPads
{
    // Optional pool controllers share the existing terrain, water and decoration implementation.
    public interface IGardenController
    {
        string PoolName { get; }
        void ConfigureDefaults(GardenSettings settings);
        bool OpenMenu();
        void BuildLiquid(Transform surface,MeshRenderer renderer);
    }

    public static class GardenIntegration
    {
        public static GardenSettings Settings(GardenPiece pond) => pond.Settings.Copy();
        public static GardenSettings GroupSettings(GardenPiece pond) => PondNetwork.GroupSettings(pond).Copy();
        public static bool IsLive(GardenPiece pond) => pond && pond.Live;
        public static bool CanEdit(GardenPiece pond) => pond && pond.CanEdit();
        public static bool Save(GardenPiece pond, GardenSettings settings) => pond && pond.Save(settings);
        public static bool Save(GardenPiece pond,GardenSettings settings,string controllerData){
            if(controllerData==null||controllerData.Length>256||!Save(pond,settings))return false;
            pond.PendingControllerData=controllerData;return true;
        }
        public static float WaterHeight(GardenPiece pond) => pond.WaterHeight;
        public static float WaterLevel(Vector3 point)=>PondNetwork.WaterLevel(point);
        public static float Distance(GardenPiece pond, Vector3 world) {
            var p=pond.transform.InverseTransformPoint(world);
            return (float)PondMath.Distance(pond.Shape,p.x,p.z,pond.Settings.Width);
        }
        public static Vector3 Boundary(int shape,float width,float angle,float fraction) {
            float radius=(float)PondMath.Radius(shape,angle)*width*.5f*fraction;
            return new Vector3(Mathf.Cos(angle)*radius,0,Mathf.Sin(angle)*radius*(float)PondMath.Aspect(shape));
        }
        public static GameObject WhiteSittingStone(Transform parent) => GardenModels.Model("rock_001",parent,1,false);
        public static void CloseEditors(){GardenEditor.Close();FeatureEditor.Close();GardenTreeEditor.Close();}
    }
}

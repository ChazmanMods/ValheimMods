using System;
using System.IO;
using UnityEngine;
public partial class Smoke
{
    System.Collections.IEnumerator CaptureLivePond(GameObject piece,string filename="live_spillway.png"){
        var camera=Camera.main;var position=camera.transform.position;var rotation=camera.transform.rotation;float fov=camera.fieldOfView;
        var controller=GameCamera.instance;bool wasEnabled=controller.enabled;controller.enabled=false;
        var hidden=new System.Collections.Generic.List<Renderer>();
        foreach(var r in UnityEngine.Object.FindObjectsOfType<Renderer>()){
            if(!r.enabled||r.GetComponentInParent<Heightmap>())continue;
            bool garden=false;foreach(var b in r.GetComponentsInParent<MonoBehaviour>())if(b&&b.GetType().Namespace=="RunicLilyPads")garden=true;
            if(!garden){r.enabled=false;hidden.Add(r);}
        }
        var focus=piece.transform.position+new Vector3(2,-.5f,0);camera.transform.position=focus+new Vector3(6,1.6f,-8);camera.transform.LookAt(focus);camera.fieldOfView=45;
        yield return new WaitForSecondsRealtime(.5f);
        var prior=camera.targetTexture;var active=RenderTexture.active;var rt=new RenderTexture(1600,900,24);
        camera.targetTexture=rt;camera.depthTextureMode|=DepthTextureMode.Depth;camera.Render();RenderTexture.active=rt;
        var picture=new Texture2D(1600,900,TextureFormat.RGB24,false);picture.ReadPixels(new Rect(0,0,1600,900),0,0);picture.Apply();
        File.WriteAllBytes(Path.GetFullPath(Path.Combine(BepInEx.Paths.BepInExRootPath,"../captures/"+filename)),ImageConversion.EncodeToPNG(picture));
        camera.targetTexture=prior;RenderTexture.active=active;UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(picture);
        yield return new WaitForSecondsRealtime(.5f);
        foreach(var r in hidden)if(r)r.enabled=true;camera.transform.SetPositionAndRotation(position,rotation);camera.fieldOfView=fov;controller.enabled=wasEnabled;
    }
    void CaptureFeature(GameObject piece,int kind){
        Camera camera=null;RenderTexture rt=null;Texture2D image=null;Light light=null;var prior=RenderTexture.active;
        var hidden=new System.Collections.Generic.List<Renderer>();
        try{
            if(kind>=7)foreach(var renderer in UnityEngine.Object.FindObjectsOfType<Renderer>()){
                if(!renderer.enabled||renderer.GetComponentInParent<Heightmap>())continue;
                bool garden=false;foreach(var behaviour in renderer.GetComponentsInParent<MonoBehaviour>())if(behaviour&&behaviour.GetType().Namespace=="RunicLilyPads")garden=true;
                if(!garden){hidden.Add(renderer);renderer.enabled=false;}
            }
            var visual=piece.transform.Find("garden_visual");var focus=visual.position+Vector3.up*1.5f;
            camera=new GameObject("test_capture_camera").AddComponent<Camera>();camera.CopyFrom(Camera.main);camera.enabled=false;camera.fieldOfView=40;
            if(kind>=7)focus=piece.transform.position+new Vector3(2,-.5f,0);
            camera.transform.position=focus+(kind>=7?new Vector3(0,10,-18):new Vector3(3,1.3f,kind==5?5:-5));camera.transform.LookAt(focus);
            light=new GameObject("test_capture_light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1;light.transform.rotation=Quaternion.Euler(40,-25,0);
            rt=new RenderTexture(768,768,24);camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
            image=new Texture2D(768,768,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,768,768),0,0);image.Apply();
            string folder=Path.GetFullPath(Path.Combine(BepInEx.Paths.BepInExRootPath,"../captures"));Directory.CreateDirectory(folder);File.WriteAllBytes(Path.Combine(folder,"feature_"+kind+".png"),ImageConversion.EncodeToPNG(image));
            Logger.LogInfo("FEATURE PASS captured model "+kind);
        }catch(Exception e){worldErrors++;Logger.LogError("FEATURE FAIL capture "+e);}
        finally{foreach(var r in hidden)if(r)r.enabled=true;RenderTexture.active=prior;if(camera)UnityEngine.Object.Destroy(camera.gameObject);if(light)UnityEngine.Object.Destroy(light.gameObject);if(rt)UnityEngine.Object.Destroy(rt);if(image)UnityEngine.Object.Destroy(image);}
    }
}


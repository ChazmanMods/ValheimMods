using System;
using System.Collections;
using System.IO;
using System.Linq;
using ArcaneWaterWorks;
using Jotunn.Managers;
using UnityEngine;
public sealed partial class StandaloneSmoke {
    IEnumerator WakeVisualTest(Vector3 origin){
        EnvMan.instance.m_debugTimeOfDay=true;EnvMan.instance.m_debugTime=.5f;EnvMan.instance.m_debugEnv="Clear";
        foreach(var tree in UnityEngine.Object.FindObjectsOfType<TreeBase>())if(Vector3.Distance(tree.transform.position,origin)<50)tree.gameObject.SetActive(false);
        var route=new Route{Width=8,Depth=1.2f};route.Points.Add(new Node(0,-.35f,0));route.Points.Add(new Node(0,-.35f,6));route.Points.Add(new Node(0,-1.35f,12));route.Points.Add(new Node(0,-3,18));
        var rock=GameObject.CreatePrimitive(PrimitiveType.Sphere);rock.name="wake obstruction fixture";rock.layer=LayerMask.NameToLayer("static_solid");rock.transform.position=origin+new Vector3(.7f,-.9f,10);rock.transform.localScale=new Vector3(1.4f,2,2);
        var go=Instantiate(PrefabManager.Instance.GetPrefab("piece_arcanewaterworks_spring"),origin,Quaternion.identity);go.GetComponent<ZNetView>().GetZDO().Set("aww_route_v1",route.Encode());yield return Settle();
        var spring=go.GetComponent<Spring>();
        var stoneMaterial=go.transform.Find("spring_stones").GetComponentInChildren<MeshRenderer>().sharedMaterial;rock.GetComponent<MeshRenderer>().sharedMaterial=stoneMaterial;
        var surface=go.transform.Find("waterworks_river/river_surface");var renderer=surface.GetComponent<MeshRenderer>();var mesh=surface.GetComponent<MeshFilter>().sharedMesh;
        Check(renderer.sharedMaterial.shader.name=="ArcaneWaters/RiverFlow"&&renderer.sharedMaterial.shader.isSupported,"flowing surface shader loaded and supported");
        var ocean=WaterVolume.Instances.FirstOrDefault(w=>w&&w.m_heightmap&&!w.m_heightmap.IsDistantLod);
        Check(ocean&&renderer.sharedMaterial.GetTexture("_Normal")==ocean.m_waterSurface.sharedMaterial.GetTexture("_Normal"),"river borrows live ocean normal texture");
        Check(go.GetComponentsInChildren<ParticleSystem>().Length==0,"river contains no sliding foam sprites");
        Check(mesh.colors.Any(c=>c.b>.02f),"river mesh carries nonzero wave displacement");
        Check(mesh.uv2.Any(v=>v.magnitude>.7f),"river surface has downstream velocity");
        var sample=origin+new Vector3(0,0,8);spring.Surface(0,8,out _,out float mean,out _);mean+=origin.y;
        float first=RiverSurface.Level(spring,sample,mean);yield return new WaitForSecondsRealtime(.4f);
        Check(Mathf.Abs(RiverSurface.Level(spring,sample,mean)-first)>.0001f,"buoyancy wave height advances with surface time");
        var frameRoot=Path.Combine(BepInEx.Paths.BepInExRootPath,"../surface-flow-frames");Directory.CreateDirectory(frameRoot);
        float time=RiverSurface.Clock;
        for(int frame=0;frame<60;frame++){
            renderer.sharedMaterial.SetFloat("_RiverTime",time+frame/10f);
            CaptureWake("surface-flow-frames/flow-"+frame.ToString("000")+".png",origin+new Vector3(7,5,10),origin+new Vector3(0,-.5f,7));
            yield return null;
        }
        CaptureWake("river-surface-flow.png",origin+new Vector3(7,5,10),origin+new Vector3(0,-.5f,7));
        var noFoam=spring.Route.Copy();noFoam.Whitewater=0;Check(spring.Save(noFoam),"zero-whitewater save accepted");yield return Settle();
        mesh=go.transform.Find("waterworks_river/river_surface").GetComponent<MeshFilter>().sharedMesh;
        Check(mesh.colors.All(c=>c.g==0)&&mesh.colors.Any(c=>c.b>.02f),"whitewater zero keeps the water itself moving");
        ZNetScene.instance.Destroy(go);Destroy(rock);
    }
    void CaptureWake(string name,Vector3 position,Vector3 focus){
        var camera=GameCamera.instance.GetComponent<Camera>();var oldPosition=camera.transform.position;var oldRotation=camera.transform.rotation;
        var profile=camera.GetComponent<UnityEngine.PostProcessing.PostProcessingBehaviour>()?.profile;
        if(profile){profile.motionBlur.enabled=false;profile.antialiasing.enabled=false;profile.depthOfField.enabled=false;}
        camera.transform.position=position;camera.transform.LookAt(focus);var target=new RenderTexture(1280,720,24);camera.targetTexture=target;camera.Render();var oldActive=RenderTexture.active;RenderTexture.active=target;
        var image=new Texture2D(1280,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(Path.Combine(BepInEx.Paths.BepInExRootPath,"../"+name),image.EncodeToPNG());
        camera.targetTexture=null;RenderTexture.active=oldActive;Destroy(image);Destroy(target);camera.transform.position=oldPosition;camera.transform.rotation=oldRotation;
    }
}

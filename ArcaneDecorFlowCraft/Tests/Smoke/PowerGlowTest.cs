using System.Collections;
using System.Collections.Generic;
using ArcaneDecorFlowCraft.Plumbing;
using Jotunn.Managers;
using UnityEngine;

public sealed partial class StandaloneSmoke
{
    IEnumerator PowerGlowTest(Vector3 origin){
        EnvMan.instance.m_debugTimeOfDay=true;EnvMan.instance.m_debugTime=.5f;EnvMan.instance.m_debugEnv="Clear";
        if(PipeEnvironment.SampleGround(origin,out float ground))origin.y=ground+28;
        var created=new List<GameObject>();var pipes=new List<PipePiece>();var colors=new[]{Color.red,Color.blue,Color.green};
        var kinds=new[]{PipeKind.Powered,PipeKind.Powered2,PipeKind.PoweredNarrow,PipeKind.Powered2Narrow,PipeKind.Powered4,PipeKind.Powered4Narrow};
        for(int row=0;row<kinds.Length;row++)for(int column=0;column<3;column++){
            GameObject go;ZNetView.m_forceDisableInit=true;
            try{go=Instantiate(PrefabManager.Instance.GetPrefab(PipePrefabs.Id(kinds[row])),origin+new Vector3((column-1)*2.6f,(2.5f-row)*1.1f,0),Quaternion.Euler(0,-90,0));}finally{ZNetView.m_forceDisableInit=false;}
            created.Add(go);var pipe=go.GetComponent<PipePiece>();pipes.Add(pipe);pipe.PowerColor=colors[column];pipe.Running=true;pipe.UpdatePowerIndicator(true);
            Check(pipe.PowerHalo&&pipe.PowerHalo.gameObject.activeSelf,"visible power halo "+kinds[row]+" color "+column);
            var renderer=go.transform.Find("plumbing_visual").GetComponent<MeshRenderer>();var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block,1);
            Check(block.GetColor("_EmissionColor")==colors[column]*1.8f,"emissive core preserves segment hue "+kinds[row]+" color "+column);
            Check(pipe.PowerLight.enabled&&pipe.PowerLight.color==colors[column],"local light matches orb hue");
        }
        yield return new WaitForSecondsRealtime(4);
        CapturePlumbing("power-glow-day.png",origin+new Vector3(0,.2f,11),origin,true);
        CapturePlumbing("power-glow-no-bloom.png",origin+new Vector3(0,.2f,11),origin);
        CapturePlumbing("power-glow-oblique.png",origin+new Vector3(5,3,7),origin,true);
        EnvMan.instance.m_debugTime=.0f;yield return new WaitForSecondsRealtime(4);
        CapturePlumbing("power-glow-night.png",origin+new Vector3(0,.2f,11),origin,true);
        foreach(var pipe in pipes){pipe.Running=false;pipe.UpdatePowerIndicator(true);Check(!pipe.PowerHalo.gameObject.activeSelf&&!pipe.PowerLight.enabled,"stopped flow extinguishes halo and light");}
        CapturePlumbing("power-glow-off.png",origin+new Vector3(0,.2f,11),origin,true);
        foreach(var go in created)Destroy(go);
    }
}

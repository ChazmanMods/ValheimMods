import UnityPy,json
from pathlib import Path
env=UnityPy.load(r'E:/SteamLibrary/steamapps/common/Valheim/valheim_Data/StreamingAssets/SoftRef/Bundles/c4210710')
report={}
for o in env.objects:
 if o.type.name=='GameObject':
  d=o.read()
  if d.m_Name in ['piece_bed02','Blackwood','Frostwood']:
   cs=[]
   for c in d.m_Component:
    ptr=c[1] if isinstance(c,tuple) else c.component
    obj=ptr.deref()
    try: tree=obj.read_typetree()
    except Exception as e: tree={'error':str(e)}
    cs.append({'type':obj.type.name,'data':tree})
   report[d.m_Name]=cs
Path('ArcaneDecorNorseFurnishings/Assets/vanilla-components.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2)[:17000])

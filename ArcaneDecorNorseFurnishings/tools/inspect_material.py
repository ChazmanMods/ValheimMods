import UnityPy,json
from pathlib import Path
e=UnityPy.load(r'E:/SteamLibrary/steamapps/common/Valheim/valheim_Data/StreamingAssets/SoftRef/Bundles/c4210710')
p=next(v for k,v in e.container.items() if k.endswith('/piece_bed02.prefab'));f=p.deref().assets_file
for o in f.objects.values():
 if o.type.name=='Material':
  d=o.read_typetree()
  if 'bed' in d.get('m_Name','').lower():print(json.dumps(d,indent=2))

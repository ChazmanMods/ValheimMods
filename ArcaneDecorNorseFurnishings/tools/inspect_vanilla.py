from pathlib import Path
import UnityPy,json
base=Path(r'E:/SteamLibrary/steamapps/common/Valheim/valheim_Data/StreamingAssets/SoftRef')
env=UnityPy.load(str(base/'Bundles/c4210710'))
report={}
for o in env.objects:
 if o.type.name=='GameObject':
  d=o.read()
  if d.m_Name in ['piece_bed02','BlackWood','TimberWood','Timberwood','WoodDeepNorth']:
   report[d.m_Name]=d.read_typetree() if hasattr(d,'read_typetree') else o.read_typetree()
   print(d.m_Name, o.path_id)
Path('ArcaneDecorNorseFurnishings/Assets/vanilla-objects.json').write_text(json.dumps(report,indent=2))

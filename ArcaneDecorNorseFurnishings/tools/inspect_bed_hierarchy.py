import UnityPy,json
from pathlib import Path
e=UnityPy.load(r'E:/SteamLibrary/steamapps/common/Valheim/valheim_Data/StreamingAssets/SoftRef/Bundles/c4210710')
ptr=next(v for k,v in e.container.items() if k.endswith('/piece_bed02.prefab'));f=ptr.deref().assets_file
root=ptr.deref().read_typetree(); comps=json.loads(Path('ArcaneDecorNorseFurnishings/Assets/vanilla-components.json').read_text())['piece_bed02']
for c in comps:
 if 'm_spawnPoint' in c['data']:
  d=f.objects[c['data']['m_spawnPoint']['m_PathID']].read_typetree();print('spawn',d)
# native hierarchy with transforms and mesh bounds for source-specific orientation check
allobjs={o.path_id:o for o in e.objects}
def visit(tid,depth=0):
 t=f.objects[tid].read_typetree();g=f.objects[t['m_GameObject']['m_PathID']].read_typetree();print(' '*depth,g['m_Name'],t['m_LocalPosition'],t['m_LocalRotation'],t['m_LocalScale'])
 for cid in g['m_Component']:
  o=f.objects[cid['component']['m_PathID']]
  if o.type.name=='MeshFilter':
   d=o.read_typetree();print(' '*depth,'mesh',d['m_Mesh'])
 for ch in t['m_Children']:visit(ch['m_PathID'],depth+1)
visit(root['m_Component'][0]['component']['m_PathID'])

import bpy,json,numpy as np
from pathlib import Path
r=Path('E:/Valheim Mods/WaterGardensHangingIvy')
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False);bpy.ops.import_scene.fbx(filepath=str(next((r/'SourceAsset').glob('*.fbx'))))
img=bpy.data.images.load(str(r/'Assets/hanging_ivy.jpg'));pix=np.array(img.pixels[:]).reshape(img.size[1],img.size[0],4)
for o in bpy.context.scene.objects:
 if o.type!='MESH':continue
 m=o.data;adj=[set() for v in m.vertices]
 for e in m.edges:a,b=e.vertices;adj[a].add(b);adj[b].add(a)
 seen=set();comps=[]
 for v in range(len(adj)):
  if v in seen:continue
  todo=[v];seen.add(v);c=[]
  while todo:
   n=todo.pop();c.append(n)
   for j in adj[n]:
    if j not in seen:seen.add(j);todo.append(j)
  comps.append(c)
 result=[]
 for c in comps:
  cs=set(c);uv=[m.uv_layers.active.data[l.index].uv[:] for l in m.loops if l.vertex_index in cs];samples=np.array([pix[min(int(v*img.size[1]),img.size[1]-1),min(int(u*img.size[0]),img.size[0]-1),:3] for u,v in uv]);coords=np.array([(o.matrix_world@m.vertices[v].co)[:] for v in c]);color=samples.mean(axis=0)
  result.append(dict(count=len(c),center=coords.mean(axis=0).tolist(),size=(coords.max(axis=0)-coords.min(axis=0)).tolist(),color=color.tolist(),yellow=float(((samples[:,0]>.6)&(samples[:,1]>.5)&(samples[:,2]<.55)).mean())))
 Path('E:/Valheim Mods/WaterGardensIvyLights/components.json').write_text(json.dumps(result,indent=2));print('COMPONENTS',len(comps));print([x for x in result if x['yellow']>.5])

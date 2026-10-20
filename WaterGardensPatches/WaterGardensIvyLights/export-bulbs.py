import bpy,numpy as np,struct,json
from pathlib import Path
root=Path('E:/Valheim Mods/WaterGardensHangingIvy');out=Path('E:/Valheim Mods/WaterGardensIvyLights/Assets')
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False);bpy.ops.import_scene.fbx(filepath=str(next((root/'SourceAsset').glob('*.fbx'))))
img=bpy.data.images.load(str(root/'Assets/hanging_ivy.jpg'));pix=np.array(img.pixels[:]).reshape(img.size[1],img.size[0],4)
records=[];centers=[]
for o in [o for o in bpy.context.scene.objects if o.type=='MESH']:
 m=o.data;m.calc_loop_triangles();yellow=[]
 for t in m.loop_triangles:
  colors=[]
  for l in t.loops:
   u,v=m.uv_layers.active.data[l].uv;colors.append(pix[min(int(v*img.size[1]),img.size[1]-1),min(int(u*img.size[0]),img.size[0]-1),:3])
  c=np.mean(colors,axis=0);yellow.append(c[0]>.55 and c[1]>.4 and c[0]>c[1]*1.06 and c[2]<c[1]*.55)
 # Connected yellow triangles isolate globes from the attached vines.
 adjacent={};ys={i for i,b in enumerate(yellow) if b}
 for i in ys:
  for v in m.loop_triangles[i].vertices:adjacent.setdefault(v,set()).add(i)
 groups=[]
 while ys:
  seed=ys.pop();todo=[seed];g=[seed]
  while todo:
   for v in m.loop_triangles[todo.pop()].vertices:
    for j in adjacent[v]&ys:ys.remove(j);todo.append(j);g.append(j)
  if len(g)>=12:groups.append(g)
 info=[]
 for g in groups:
  points=np.array([(o.matrix_world@m.vertices[v].co)[:] for i in g for v in m.loop_triangles[i].vertices]);center=(points.min(axis=0)+points.max(axis=0))/2;info.append((center,g))
 info.sort(key=lambda x:-x[0][2]);lookup={}
 for j,(c,g) in enumerate(info):
  centers.append(c.tolist())
  for i in g:lookup[i]=(j%3,c)
 for i,t in enumerate(m.loop_triangles):
  group,c=lookup.get(i,(-1,np.zeros(3)))
  for l in t.loops:records.append((group,float(c[0]),float(c[2]-.99951171875),float(-c[1])))
with (out/'hanging_ivy.bulbs').open('wb') as f:
 f.write(struct.pack('<i',len(records)))
 for r in records:f.write(struct.pack('<i3f',*r))
print('Bulbs:',len(centers),'groups:',[sum(1 for r in records if r[0]==i)//3 for i in range(3)],'vertices:',len(records));print(centers)

import bpy
from pathlib import Path
bpy.ops.wm.open_mainfile(filepath=str(Path(__file__).parent/'SyncFountain/prepared.blend'))
o=next(o for o in bpy.context.scene.objects if o.type=='MESH');v=[o.matrix_world@p.co for p in o.data.vertices]
for a,b in [(.2,.25),(.25,.3),(.3,.35),(.35,.4),(.4,.45),(.45,.5),(.5,.55),(.55,.6)]:
 s=[p for p in v if a<=p.z<b];print(a,len(s),[(min(p[i] for p in s),max(p[i] for p in s),sum(p[i] for p in s)/len(s)) for i in range(2)])

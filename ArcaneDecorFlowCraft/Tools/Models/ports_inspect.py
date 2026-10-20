import bpy,json
from pathlib import Path
root=Path(__file__).resolve().parent
for folder in sorted(p for p in root.glob('AW_*') if p.is_dir()):
 bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False);bpy.ops.import_scene.fbx(filepath=str(next(folder.glob('*.fbx'))))
 pts=[o.matrix_world@v.co for o in bpy.context.scene.objects if o.type=='MESH' for v in o.data.vertices]
 print(folder.name)
 for axis,side in [(0,-1),(0,1),(2,-1),(2,1)]:
  end=(min if side<0 else max)(p[axis] for p in pts);near=[p for p in pts if abs(p[axis]-end)<.015]
  print(axis,side,'end',round(end,4),'slice bounds',[[round(min(p[i] for p in near),4),round(max(p[i] for p in near),4)] for i in range(3)])


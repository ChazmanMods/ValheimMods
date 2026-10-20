import bpy
from pathlib import Path
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=r'E:/Valheim Mods/ArcaneDecorFlowCraft/Tools/Models/SyncFountain/FC_Sync_Fountain.fbx')
for o in bpy.context.scene.objects:
 if o.type=='MESH':
  from mathutils import Vector
  v=[o.matrix_world@Vector(c) for c in o.bound_box]
  print('MODEL',o.name,len(o.data.vertices),'bounds',[(min(p[a] for p in v),max(p[a] for p in v)) for a in range(3)])

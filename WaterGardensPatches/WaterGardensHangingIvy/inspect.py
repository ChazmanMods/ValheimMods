import bpy,json
from pathlib import Path
bpy.ops.object.select_all(action="SELECT");bpy.ops.object.delete(use_global=False)
p=Path(r"E:/Valheim Mods/WaterGardensHangingIvy/SourceAsset")
bpy.ops.import_scene.fbx(filepath=str(next(p.glob("*.fbx"))))
for o in bpy.context.scene.objects:
 if o.type=="MESH":print("IVY",o.name,"dimensions",list(o.dimensions),"verts",len(o.data.vertices),"bounds",[list(o.matrix_world@__import__('mathutils').Vector(c)) for c in o.bound_box])

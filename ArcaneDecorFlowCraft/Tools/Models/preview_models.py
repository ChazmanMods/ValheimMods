import bpy, math
from pathlib import Path
from mathutils import Vector
root=Path(__file__).resolve().parent
for folder in sorted(root.glob('AW_*')):
    bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.fbx(filepath=str(next(folder.glob('*.fbx'))))
    objects=[o for o in bpy.context.scene.objects if o.type=='MESH']
    points=[o.matrix_world@Vector(p) for o in objects for p in o.bound_box]
    center=sum(points,Vector())/len(points)
    texture=next(folder.glob('**/*.jpg'))
    for obj in objects:
        mat=bpy.data.materials.new('Copper');mat.use_nodes=True
        bs=mat.node_tree.nodes.get('Principled BSDF');bs.inputs['Metallic'].default_value=.55;bs.inputs['Roughness'].default_value=.4
        image=mat.node_tree.nodes.new('ShaderNodeTexImage');image.image=bpy.data.images.load(str(texture))
        mat.node_tree.links.new(image.outputs['Color'],bs.inputs['Base Color']);obj.data.materials.clear();obj.data.materials.append(mat)
    bpy.ops.object.camera_add(location=center+Vector((2,-3,1.7)))
    camera=bpy.context.object;camera.rotation_euler=(center-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.type='ORTHO';camera.data.ortho_scale=1.5
    bpy.context.scene.camera=camera
    for loc,power,size in [((1,-2,3),500,3),((-2,-1,1),250,2),((0,2,2),350,2)]:
        bpy.ops.object.light_add(type='AREA',location=center+Vector(loc));light=bpy.context.object;light.data.energy=power;light.data.shape='DISK';light.data.size=size;light.rotation_euler=(center-light.location).to_track_quat('-Z','Y').to_euler()
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=24;scene.render.resolution_x=512;scene.render.resolution_y=512;scene.render.resolution_percentage=100
    scene.render.film_transparent=True;scene.world.color=(.2,.2,.2);scene.render.image_settings.file_format='PNG';scene.render.filepath=str(root/(folder.name+'.png'))
    bpy.ops.render.render(write_still=True)

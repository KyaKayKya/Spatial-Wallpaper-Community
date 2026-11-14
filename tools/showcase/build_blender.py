import bpy, math, json
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Prototype/Assets/Showcase/Models'
OUT.mkdir(parents=True,exist_ok=True)
# This script owns a fresh background Blender scene; never clears a user's open scene.
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
scene=bpy.context.scene
scene.unit_settings.system='METRIC'
scene.unit_settings.scale_length=1
def mat(name,color,rough):
 m=bpy.data.materials.new(name);m.use_nodes=True
 p=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
 p.inputs['Base Color'].default_value=(*color,1);p.inputs['Roughness'].default_value=rough
 return m
white=mat('WarmWhitePaint',(.70,.68,.63),.68)
back=mat('MatteBacking',(.60,.59,.55),.79)
ceramic=mat('IvoryCeramic',(.75,.69,.59),.43)
def uv(obj):
 bpy.context.view_layer.objects.active=obj
 bpy.ops.object.select_all(action='DESELECT');obj.select_set(True)
 bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
 bpy.ops.uv.smart_project(island_margin=.04)
 bpy.ops.object.mode_set(mode='OBJECT')
 obj.data.uv_layers.new(name='LightmapUV')
def box(name,loc,size,material):
 bpy.ops.mesh.primitive_cube_add(size=1,location=loc);o=bpy.context.object;o.name=name
 o.dimensions=size;bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
 o.data.materials.append(material)
 b=o.modifiers.new('Real_0p8mm_edge','BEVEL');b.width=.0008;b.segments=3
 bpy.context.view_layer.objects.active=o;bpy.ops.object.modifier_apply(modifier=b.name)
 for p in o.data.polygons:p.use_smooth=True
 n=o.modifiers.new('Weighted_Planar_Normals','WEIGHTED_NORMAL');n.keep_sharp=True
 bpy.ops.object.modifier_apply(modifier=n.name);uv(o)
 return o
# Blender X right, Y into cabinet, Z up. FBX axis conversion produces Unity X/Y/Z.
shell=[
box('Cabinet_Left',(-.271,.095,0),(.012,.19,.324),white),
box('Cabinet_Right',(.271,.095,0),(.012,.19,.324),white),
box('Cabinet_Top',(0,.095,.156),(.554,.19,.012),white),
box('Cabinet_Base',(0,.095,-.156),(.554,.19,.012),white),
box('Cabinet_Back',(0,.186,0),(.554,.012,.324),back),
box('Shelf',(0,.1075,-.093),(.534,.145,.012),white)]
# Nested outer/inner outlines produce a continuous rounded, non-intersecting shell.
verts=[];faces=[];N=128;M=32
for i in range(N):
 t=2*math.pi*i/N
 ox=.055*math.sin(t)*(1-.10*math.cos(t))+.006*math.cos(t)
 oz=.065+.065*math.cos(t)+.025*math.exp(-((t-math.pi)/.35)**2)-.010
 ix=.018*math.sin(t)+.004
 iz=.069+.025*math.cos(t)
 for j in range(M):
  a=2*math.pi*j/M
  q=(1+math.cos(a))*.5
  verts.append((ix*(1-q)+ox*q,.023*math.sin(a),max(0,iz*(1-q)+oz*q-.003)))
for i in range(N):
 for j in range(M):
  faces.append((i*M+j,((i+1)%N)*M+j,((i+1)%N)*M+(j+1)%M,i*M+(j+1)%M))
mesh=bpy.data.meshes.new('ContinuousCeramicMesh');mesh.from_pydata(verts,[],faces);mesh.update()
obj=bpy.data.objects.new('Ceramic_Study',mesh);scene.collection.objects.link(obj)
for polygon in mesh.polygons:polygon.use_smooth=True
obj.data.materials.append(ceramic);uv(obj)
# Origin at support plane. Display placement happens in Unity.
def export(name,objects):
 bpy.ops.object.select_all(action='DESELECT')
 for o in objects:o.select_set(True)
 bpy.context.view_layer.objects.active=objects[0]
 bpy.ops.export_scene.fbx(filepath=str(OUT/name),use_selection=True,object_types={'MESH'},add_leaf_bones=False,axis_forward='-Z',axis_up='Y',apply_unit_scale=True,use_mesh_modifiers=True,bake_anim=False)
export('Cabinet.fbx',shell);export('Ceramic.fbx',[obj])
obj.location=(0,.100,-.087)
scene.world.color=(.18,.18,.18)
bpy.ops.object.light_add(type='AREA',location=(-.26,-.28,.38))
light=bpy.context.object;light.name='Authoring_Softbox';light.data.energy=8;light.data.shape='DISK';light.data.size=.28
light.rotation_euler=(Vector((0,.10,-.02))-light.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(location=(0,-.60,0))
cam=bpy.context.object;cam.rotation_euler=(Vector((0,.1,0))-cam.location).to_track_quat('-Z','Y').to_euler()
cam.data.lens=40;scene.camera=cam
scene.render.resolution_x=1280;scene.render.resolution_y=720;scene.render.resolution_percentage=100
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'art/showcase/showcase.blend'))
stats={o.name:{'vertices':len(o.data.vertices),'triangles':sum(len(p.vertices)-2 for p in o.data.polygons),'uv_sets':len(o.data.uv_layers)} for o in shell+[obj]}
(ROOT/'Validation/Showcase/geometry.json').write_text(json.dumps(stats,indent=2))
print(json.dumps(stats))

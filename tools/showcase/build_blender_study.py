"""Build a local Cycles study from the two explicitly selected free Blendkit assets."""
import bpy, math, json, sys
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'art/showcase/blender-study'
EVIDENCE=ROOT/'Validation/Showcase/blender-study'
EVIDENCE.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(OUT/'source-assets/studio.blend'),load_ui=False,use_scripts=False)
scene=bpy.context.scene
scene.unit_settings.system='METRIC'
scene.unit_settings.scale_length=1.0
# Retain the authored three-light rig and adapt its scale/placement for a recessed display.
rig=[o for o in scene.objects if o.type=='LIGHT']
for o in list(scene.objects):
 if o.type!='LIGHT':bpy.data.objects.remove(o,do_unlink=True)
with bpy.data.libraries.load(str(ROOT/'art/showcase/showcase.blend'),link=False) as (a,b):
 b.objects=[n for n in a.objects if n.startswith('Cabinet_') or n=='Shelf']
cabinet=[]
for o in b.objects:
 scene.collection.objects.link(o);cabinet.append(o)
with bpy.data.libraries.load(str(OUT/'source-assets/organic.blend'),link=False) as (a,b):
 b.objects=a.objects
prop=b.objects[0];scene.collection.objects.link(prop)
prop.name='Organic_Sculpture__Flareworks_Studio'
# Keep the author's silhouette, topology, subdivisions and UVs.
bpy.context.view_layer.update()
deps=bpy.context.evaluated_depsgraph_get()
ev=prop.evaluated_get(deps);mesh=ev.to_mesh()
pts=[ev.matrix_world@v.co for v in mesh.vertices]
lo=Vector(tuple(min(p[i] for p in pts) for i in range(3)))
hi=Vector(tuple(max(p[i] for p in pts) for i in range(3)))
ev.to_mesh_clear()
factor=.134/(hi.z-lo.z)
prop.scale*=factor
prop.location=Vector((-factor*(lo.x+hi.x)/2,.095-factor*(lo.y+hi.y)/2,-.087-factor*lo.z))
prop['source_asset_base_id']='02d42f4c-1502-4d42-a1ef-a1d23ef47b07'
prop['source_author']='Flareworks Studio (formerly Daniel Lee)'
prop['modification']='Uniform scale and placement; original geometry and UV retained.'
def painted_material(name,color,roughness):
 m=bpy.data.materials.new(name);m.use_nodes=True
 n=m.node_tree.nodes;l=m.node_tree.links
 p=next(x for x in n if x.type=='BSDF_PRINCIPLED')
 p.inputs['Base Color'].default_value=(*color,1)
 p.inputs['Roughness'].default_value=roughness
 noise=n.new('ShaderNodeTexNoise');noise.inputs['Scale'].default_value=850
 noise.inputs['Detail'].default_value=2
 coords=n.new('ShaderNodeTexCoord');l.new(coords.outputs['Object'],noise.inputs['Vector'])
 bump=n.new('ShaderNodeBump');bump.inputs['Strength'].default_value=.14;bump.inputs['Distance'].default_value=.000015
 l.new(noise.outputs['Fac'],bump.inputs['Height']);l.new(bump.outputs['Normal'],p.inputs['Normal'])
 return m
white=painted_material('Warm_white_satin_lacquer',(.79,.765,.715),.36)
back=painted_material('Warm_white_matte_back',(.745,.725,.680),.60)
for o in cabinet:
 o.data.materials.clear();o.data.materials.append(back if o.name=='Cabinet_Back' else white)
# Convert the source's plaster-like color to ivory ceramic, retaining its authored maps/UV.
m=prop.data.materials[0].copy();m.name='Organic_ivory_ceramic_from_source';prop.data.materials[0]=m
n=m.node_tree.nodes;l=m.node_tree.links;p=next(x for x in n if x.type=='BSDF_PRINCIPLED')
for link in list(p.inputs['Base Color'].links):l.remove(link)
p.inputs['Base Color'].default_value=(.72,.665,.565,1)
# Preserve roughness and normal texture if wired; reduce excessive plaster bump to ceramic scale.
for x in n:
 if x.type=='NORMAL_MAP':x.inputs['Strength'].default_value=.16
 if x.type=='BUMP':x.inputs['Strength'].default_value=.15
p.inputs['Roughness'].default_value=.42
def point(o,target):o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler()
settings=[
 ('Template_Key',(-.25,-.25,.24),(0,.12,-.025),8.0,.24,.22,(1,.94,.84)),
 ('Template_Fill',(.32,-.30,.08),(0,.10,-.025),2.0,.35,.30,(.90,.95,1)),
 ('Template_Top',(-.03,-.08,.145),(0,.11,-.055),1.4,.30,.08,(1,.97,.91))]
for o,(name,loc,target,power,sx,sy,color) in zip(sorted(rig,key=lambda x:x.name),settings):
 o['source_template']='119b8857-66d7-4495-b115-c8979aaa3710'
 o.name=name;o.location=loc;o.scale=(1,1,1);point(o,target)
 o.data.type='AREA';o.data.shape='RECTANGLE';o.data.size=sx;o.data.size_y=sy;o.data.energy=power;o.data.color=color
scene.world=bpy.data.worlds.new('Neutral_studio_environment');scene.world.use_nodes=True
bg=next(n for n in scene.world.node_tree.nodes if n.type=='BACKGROUND')
bg.inputs['Color'].default_value=(.72,.78,.86,1);bg.inputs['Strength'].default_value=.14
scene.render.engine='CYCLES';scene.cycles.samples=96;scene.cycles.use_denoising=True
scene.cycles.max_bounces=10;scene.cycles.diffuse_bounces=6;scene.cycles.glossy_bounces=4
prefs=bpy.context.preferences.addons['cycles'].preferences
try:
 prefs.compute_device_type='OPTIX';prefs.get_devices()
 for dev in prefs.devices:dev.use=('5070' in dev.name)
 if any(d.use for d in prefs.devices):
  scene.cycles.device='GPU'
 else:
  for d in prefs.devices:d.use=d.type!='CPU'
  scene.cycles.device='GPU'
except Exception as e:print('GPU_SETUP',str(e));scene.cycles.device='CPU'
scene.view_settings.view_transform='AgX'
looks=[x.identifier for x in scene.view_settings.bl_rna.properties['look'].enum_items]
try:scene.view_settings.look='AgX - Medium High Contrast'
except Exception:pass
scene.view_settings.exposure=0
scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGB'
scene.render.resolution_x=1440;scene.render.resolution_y=810;scene.render.resolution_percentage=65
scene.render.film_transparent=False
bpy.ops.object.camera_add()
cam=bpy.context.object;cam.name='Cabinet_Front_Camera';cam.location=(0,-.60,0);point(cam,(0,0,0))
cam.data.type='PERSP';cam.data.lens=40.75;cam.data.sensor_width=36;cam.data.clip_start=.005;cam.data.clip_end=20
scene.camera=cam
bpy.context.view_layer.update()
ev=prop.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=ev.to_mesh()
points=[ev.matrix_world@v.co for v in mesh.vertices]
bottom=min(p.z for p in points)
support=[p for p in points if p.z<bottom+.0005]
stats={'source':'Organic Sculpture; original mesh retained','render_vertices':len(mesh.vertices),'bottom_z_m':bottom,'shelf_top_m':-.087,'support_within_0_5mm_vertices':len(support),'support_span_x_m':max(p.x for p in support)-min(p.x for p in support),'support_span_y_m':max(p.y for p in support)-min(p.y for p in support),'render_device':scene.cycles.device,'lights_from_template':len(rig)}
ev.to_mesh_clear()
(EVIDENCE/'geometry-check.json').write_text(json.dumps(stats,indent=2))
bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'showcase-organic-study.blend'))
scene.render.filepath=str(EVIDENCE/'front-draft.png');bpy.ops.render.render(write_still=True)
# Off-centre side observation: same vertical display plane, no camera toe-in.
cam.name='Cabinet_Oblique_Camera';cam.location=(.16,-.60,.025)
cam.data.shift_x=-.16/.53;cam.data.shift_y=-.025/.53
scene.render.filepath=str(EVIDENCE/'oblique-draft.png');bpy.ops.render.render(write_still=True)
print('STUDY_READY',json.dumps(stats))

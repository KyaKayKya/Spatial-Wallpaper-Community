import bpy,json,shutil,numpy as np
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Prototype/Assets/Showcase/BakedStudy';OUT.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'art/showcase/blender-study/showcase-pinlights.blend'),load_ui=False,use_scripts=False)
sc=bpy.context.scene
prop=bpy.data.objects['Organic_Sculpture__Flareworks_Studio']
cab=[o for o in sc.objects if o.type=='MESH' and (o.name.startswith('Cabinet_') or o.name=='Shelf')]
lamps=sorted([o for o in sc.objects if o.type=='MESH' and (o.name.startswith('3w-led') or o.name.startswith('CeilingPinlight'))],key=lambda o:o.location.x)
if len(lamps)!=2:raise RuntimeError('Approved scene must contain two ceiling pinlights')
for o,name in zip(lamps,['CeilingPinlight_Left','CeilingPinlight_Right']):o.name=name
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'art/showcase/blender-study/showcase-pinlights.blend'))
fixed=cab+lamps
objects=fixed+[prop]
prefs=bpy.context.preferences.addons['cycles'].preferences
prefs.compute_device_type='OPTIX';prefs.get_devices()
for d in prefs.devices:d.use='5070' in d.name
sc.cycles.device='GPU';sc.cycles.samples=512;sc.cycles.use_denoising=True
sc.render.bake.margin=12;sc.render.bake.use_clear=True
sc.render.image_settings.file_format='OPEN_EXR';sc.render.image_settings.color_depth='16'
# Keep material source textures on their original UV while baking to separate non-overlapping UVs.
for obj in objects:
 bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
 for mod in list(obj.modifiers):bpy.ops.object.modifier_apply(modifier=mod.name)
 if not obj.data.uv_layers:obj.data.uv_layers.new(name='UVMap')
 original=obj.data.uv_layers[0].name
 for mat in obj.data.materials:
  nodes=mat.node_tree.nodes;links=mat.node_tree.links
  uv=nodes.new('ShaderNodeUVMap');uv.uv_map=original
  for node in list(nodes):
   if node.type=='TEX_IMAGE' and not node.inputs['Vector'].is_linked:links.new(uv.outputs['UV'],node.inputs['Vector'])
 while len(obj.data.uv_layers)>1:obj.data.uv_layers.remove(obj.data.uv_layers[-1])
 obj.data.uv_layers.new(name='BakeUV');obj.data.uv_layers.active_index=1
 bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.smart_project(angle_limit=1.15,island_margin=.025);bpy.ops.object.mode_set(mode='OBJECT')
 # Each object needs its own active bake target, even when source materials were shared.
 for i,mat in enumerate(obj.data.materials):obj.data.materials[i]=mat.copy()
def bake(variant,targets):
 images=[]
 bpy.ops.object.select_all(action='DESELECT')
 for obj in targets:
  size=256 if obj in lamps else (1024 if obj==prop or 'Back' in obj.name else 512)
  img=bpy.data.images.new(obj.name+'_'+variant,width=size,height=size,alpha=False,float_buffer=True)
  img.colorspace_settings.name='Non-Color';img.file_format='OPEN_EXR';img.filepath_raw=str(OUT/(obj.name+'_'+variant+'.exr'))
  for mat in obj.data.materials:
   nodes=mat.node_tree.nodes
   for node in nodes:node.select=False
   node=nodes.new('ShaderNodeTexImage');node.image=img;node.select=True;nodes.active=node
  images.append(img);obj.select_set(True)
 bpy.context.view_layer.objects.active=targets[0]
 bpy.ops.object.bake(type='DIFFUSE',pass_filter={'COLOR','DIRECT','INDIRECT'})
 # Tiny fixture textures contain diffuse plus emission; no camera-dependent specular bake.
 lamp_images=[(obj,img) for obj,img in zip(targets,images) if obj in lamps]
 if lamp_images:
  diffuse={obj.name:np.array(img.pixels[:],dtype=np.float32) for obj,img in lamp_images}
  bpy.ops.object.select_all(action='DESELECT')
  for obj,img in lamp_images:obj.select_set(True)
  bpy.context.view_layer.objects.active=lamp_images[0][0]
  bpy.ops.object.bake(type='EMIT')
  for obj,img in lamp_images:
   pixels=np.array(img.pixels[:],dtype=np.float32)+diffuse[obj.name];pixels[3::4]=1;img.pixels.foreach_set(pixels)
 raw=ROOT/'Validation/Showcase/bake-raw';raw.mkdir(parents=True,exist_ok=True)
 for img in images:
  img.save_render(img.filepath_raw,scene=sc)
  shutil.copy2(img.filepath_raw,raw/Path(img.filepath_raw).name)
 print('BAKED',variant,flush=True)
bake('occupied',objects)
prop.hide_render=True
bake('empty',fixed)
prop.hide_render=False
def export(name,targets):
 # The baked atlas already combines trim and diffuser. Export one surface material.
 for obj in targets:
  if obj in lamps:
   for poly in obj.data.polygons:poly.material_index=0
   while len(obj.data.materials)>1:obj.data.materials.pop(index=len(obj.data.materials)-1)
 bpy.ops.object.select_all(action='DESELECT')
 for obj in targets:obj.select_set(True)
 bpy.context.view_layer.objects.active=targets[0]
 bpy.ops.export_scene.fbx(filepath=str(OUT/name),use_selection=True,object_types={'MESH'},add_leaf_bones=False,axis_forward='-Z',axis_up='Y',apply_unit_scale=True,use_mesh_modifiers=True,bake_anim=False)
export('CabinetBaked.fbx',cab)
export('PinlightsBaked.fbx',lamps)
shift=Vector((0,.095,-.087))
for v in prop.data.vertices:v.co=prop.matrix_world@v.co-shift
prop.matrix_world.identity()
export('OrganicBaked.fbx',[prop])
(OUT/'bake-manifest.json').write_text(json.dumps({'renderer':'Cycles OptiX','samples':512,'type':'Diffuse COLOR DIRECT INDIRECT','variants':['empty','occupied'],'prop_mesh':prop.name,'maps':[p.name for p in OUT.glob('*.exr')]},indent=2))
print('BAKE_COMPLETE',flush=True)

"""Bake view-independent diffuse transport from the approved source, preserving UV0."""
import bpy, json, math
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Prototype/Assets/Pool/Baked'
OUT.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'.local-assets/pool/approved_pool.blend'),load_ui=False,use_scripts=False)
sc=bpy.context.scene
sc.render.engine='CYCLES'
prefs=bpy.context.preferences.addons['cycles'].preferences
prefs.compute_device_type='OPTIX';prefs.get_devices()
for d in prefs.devices:d.use=d.type!='CPU'
sc.cycles.device='GPU';sc.cycles.samples=192
sc.cycles.max_bounces=10;sc.cycles.diffuse_bounces=6
sc.render.bake.margin=16
sc.render.image_settings.file_format='OPEN_EXR';sc.render.image_settings.color_depth='16'
for o in sc.objects:
 if o.name.startswith('Optional water'):o.hide_render=True
meshes=[o for o in sc.objects if o.type=='MESH' and not o.hide_render]
# Coplanar butt joints expose exterior texels and sub-pixel sky cracks. Extend
# concealed wall/roof edges into each other, keeping all visible room planes.
for o in meshes:
 # Fit the room's actual front to 11.2 x 6.3 metres (16:9), preserving
 # floor, water, stairs and ring dimensions rather than stretching the scene.
 if o.name.startswith('Ceiling') or o.name.startswith('Skylight'):
  o.location.z-=.3
 if 'wall' in o.name.lower():
  inv=o.matrix_world.inverted()
  for v in o.data.vertices:
   p=o.matrix_world@v.co
   if p.z>5.09:p.z-=.10
   if o.name=='Back wall' and abs(p.x)>5.59:p.x+=.08 if p.x>0 else -.08
   v.co=inv@p
 if o.name.startswith('Ceiling'):
  inv=o.matrix_world.inverted()
  for v in o.data.vertices:
   p=o.matrix_world@v.co
   if abs(p.x)>5.59:p.x+=.04 if p.x>0 else -.04
   if p.y>8.99:p.y+=.04
   v.co=inv@p
# A broad skylight supplies warm diffuse bounce to the staircase, while the
# closed masonry preserves occlusion behind the column.
light=bpy.data.lights.new('Skylight diffuse bounce','AREA');light.energy=1000;light.color=(1,.72,.43);light.shape='DISK';light.size=3.0
lamp=bpy.data.objects.new('Skylight diffuse bounce',light);sc.collection.objects.link(lamp);lamp.location=(.9,5.1,6.5)
for o in meshes:
 if 'ring' in o.name.lower():o.hide_render=True
print('SOURCE',[(o.name,tuple(o.dimensions)) for o in meshes],flush=True)
print('LIGHTS',[(o.name,o.data.energy,tuple(o.data.color)) for o in sc.objects if o.type=='LIGHT'],flush=True)
# Preserve the source's golden sunlight and physically occluded diffuse bounce.
for o in sc.objects:
 if o.type=='LIGHT' and o.data.type=='SUN':o.data.color=(1,.57,.20);o.data.energy=4;o.data.angle=math.radians(1.0)
targets=[o for o in meshes if 'ring' not in o.name.lower()]
for o in meshes:
 bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
 for mod in list(o.modifiers):bpy.ops.object.modifier_apply(modifier=mod.name)
 if o not in targets:continue
 original=o.data.uv_layers[0].name
 for i,mat in enumerate(o.data.materials):
  mat=mat.copy();o.data.materials[i]=mat
  uv=mat.node_tree.nodes.new('ShaderNodeUVMap');uv.uv_map=original
  for node in list(mat.node_tree.nodes):
   if node.type=='TEX_IMAGE' and not node.inputs['Vector'].is_linked:mat.node_tree.links.new(uv.outputs['UV'],node.inputs['Vector'])
 while len(o.data.uv_layers)>1:o.data.uv_layers.remove(o.data.uv_layers[-1])
 o.data.uv_layers.new(name='BakeUV');o.data.uv_layers.active_index=1
 bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.smart_project(angle_limit=1.15,island_margin=.025);bpy.ops.object.mode_set(mode='OBJECT')
 size=1024 if max(o.dimensions)>5 else 512
 img=bpy.data.images.new(o.name+'_Diffuse',width=size,height=size,alpha=False,float_buffer=True)
 img.colorspace_settings.name='Non-Color'
 for mat in o.data.materials:
  nodes=mat.node_tree.nodes
  for n in nodes:n.select=False
  node=nodes.new('ShaderNodeTexImage');node.image=img;node.select=True;nodes.active=node
 bpy.ops.object.bake(type='DIFFUSE',pass_filter={'COLOR','DIRECT','INDIRECT'})
 img.save_render(str(OUT/(o.name+'.exr')),scene=sc)
 print('BAKED',o.name,flush=True)
bpy.ops.object.select_all(action='DESELECT')
for o in meshes:o.hide_render=False;o.select_set(True)
bpy.context.view_layer.objects.active=meshes[0]
bpy.ops.export_scene.fbx(filepath=str(OUT/'pool_baked.fbx'),use_selection=True,object_types={'MESH'},add_leaf_bones=False,axis_forward='-Z',axis_up='Y',apply_unit_scale=True,use_mesh_modifiers=True,bake_anim=False)
(OUT/'manifest.json').write_text(json.dumps({'renderer':'Cycles OptiX','samples':192,'transport':'Diffuse color + direct + indirect, no view-dependent specular','maps':[o.name for o in targets]},indent=2))
print('BAKE_COMPLETE',flush=True)

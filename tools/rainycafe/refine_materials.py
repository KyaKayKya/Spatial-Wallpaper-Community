"""Split photograph, foliage cutouts and street-lamp glass without rebaking the room."""
import bpy,bmesh,json,hashlib
from pathlib import Path
R=Path(__file__).resolve().parents[2];O=R/'Prototype/Assets/RainyCafe/Generated'
bpy.ops.wm.open_mainfile(filepath=str(R/'art/RainyCafe/rainycafe_bake.blend'),load_ui=False,use_scripts=False)
s=bpy.context.scene;s.cycles.samples=32
p=bpy.context.preferences.addons['cycles'].preferences;p.compute_device_type='OPTIX';p.get_devices()
for d in p.devices:d.use=d.type!='CPU'
s.cycles.device='GPU';s.render.bake.margin=4
manifest=json.loads((O/'manifest.json').read_text());changed=set();new=[]
base_names=[x['name'] for x in manifest['objects'] if bpy.data.objects.get(x['name'])]
def kind(m,owner):
 if not m:return None
 name=m.name.lower()
 if 'marville public-domain' in name:return 'Photographs'
 if 'street_lamp_01_glass' in name:return 'StreetGlass'
 if 'single warm street bulb' in name:return 'StreetBulb'
 foliage_name=any(k in name for k in ('plant_leaf','leaves','plant01','plant02','ivy'))
 plant_cutout=owner=='Plants' and m.use_nodes and any(n.type=='BSDF_TRANSPARENT' for n in m.node_tree.nodes)
 if foliage_name or plant_cutout:return 'Foliage'
 return None
for group in ('Photographs','StreetGlass','StreetBulb','Foliage'):
 parts=[]
 for obj in list(s.objects):
  if obj.type!='MESH' or obj.name not in [x['name'] for x in manifest['objects']]:continue
  indices={i for i,m in enumerate(obj.data.materials) if kind(m,obj.name)==group}
  if not indices or not any(f.material_index in indices for f in obj.data.polygons):continue
  dup=obj.copy();dup.data=obj.data.copy();s.collection.objects.link(dup)
  for target,keep in ((dup,True),(obj,False)):
   bm=bmesh.new();bm.from_mesh(target.data)
   bmesh.ops.delete(bm,geom=[f for f in bm.faces if (f.material_index in indices)!=keep],context='FACES')
   bm.to_mesh(target.data);bm.free();target.data.update()
  changed.add(obj.name);parts.append(dup)
 if not parts:continue
 bpy.ops.object.select_all(action='DESELECT')
 for obj in parts:obj.select_set(True)
 bpy.context.view_layer.objects.active=parts[0]
 if len(parts)>1:bpy.ops.object.join()
 obj=bpy.context.object;obj.name=group;new.append(obj)
 bpy.ops.object.material_slot_remove_unused()
 if group!='StreetGlass':
  obj.data.uv_layers.active_index=obj.data.uv_layers.find('BakeUV')
  bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.smart_project(angle_limit=1.15,island_margin=.0007);bpy.ops.object.mode_set(mode='OBJECT')
 print('SPLIT',group,len(obj.data.vertices),flush=True)

def opacity(mat):
 n=mat.node_tree.nodes;l=mat.node_tree.links
 def scalar(value):
  a=n.new('ShaderNodeValue');a.outputs[0].default_value=value;return a.outputs[0]
 def follow(node):
  if node.type=='BSDF_TRANSPARENT':return scalar(0)
  if node.type=='BSDF_PRINCIPLED':
   a=node.inputs['Alpha'];return a.links[0].from_socket if a.is_linked else scalar(a.default_value)
  if node.type=='MIX_SHADER':
   a=follow(node.inputs[1].links[0].from_node) if node.inputs[1].is_linked else scalar(0)
   b=follow(node.inputs[2].links[0].from_node) if node.inputs[2].is_linked else scalar(0)
   mix=n.new('ShaderNodeMixRGB');l.new(a,mix.inputs[1]);l.new(b,mix.inputs[2])
   fac=node.inputs[0]
   if fac.is_linked:l.new(fac.links[0].from_socket,mix.inputs[0])
   else:mix.inputs[0].default_value=fac.default_value
   return mix.outputs[0]
  return scalar(1)
 output=next(x for x in n if x.type=='OUTPUT_MATERIAL' and x.is_active_output)
 return follow(output.inputs['Surface'].links[0].from_node),output

for obj in new:
 if obj.name=='StreetGlass':continue
 bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
 resolution=4096 if obj.name=='Foliage' else 1024
 types=[('Lighting','COMBINED'),('Albedo','DIFFUSE'),('Roughness','ROUGHNESS'),('Normal','NORMAL')]
 if obj.name=='Foliage':types.append(('Opacity','EMIT'))
 for suffix,typ in types:
  path=O/(obj.name+suffix+'.png')
  image=bpy.data.images.new(obj.name+suffix,width=resolution,height=resolution,float_buffer=True)
  if suffix in ('Roughness','Normal','Opacity'):image.colorspace_settings.name='Non-Color'
  changed_links=[]
  for mat in {obj.data.materials[f.material_index] for f in obj.data.polygons}:
   if not mat or not mat.use_nodes:continue
   nodes=mat.node_tree.nodes;links=mat.node_tree.links
   if suffix=='Opacity':
    socket,output=opacity(mat);old=output.inputs['Surface'].links[0].from_socket
    emit=nodes.new('ShaderNodeEmission');links.new(socket,emit.inputs['Color']);links.new(emit.outputs[0],output.inputs['Surface']);changed_links.append((mat,output,old,emit))
   node=nodes.new('ShaderNodeTexImage');node.image=image;nodes.active=node
  s.render.bake.use_pass_direct=typ!='DIFFUSE';s.render.bake.use_pass_indirect=typ!='DIFFUSE';s.render.bake.use_pass_color=True
  bpy.ops.object.bake(type=typ)
  path=O/(obj.name+suffix+'.png')
  if suffix=='Lighting':image.save_render(str(path),scene=s)
  else:image.filepath_raw=str(path);image.file_format='PNG';image.save()
  for mat,output,old,emit in changed_links:mat.node_tree.links.new(old,output.inputs['Surface']);mat.node_tree.nodes.remove(emit)
  for mat in set(obj.data.materials):
   if mat and mat.use_nodes:
    for node in list(mat.node_tree.nodes):
     if node.type=='TEX_IMAGE' and node.image==image:mat.node_tree.nodes.remove(node)
  bpy.data.images.remove(image);manifest['files'].append(path.name)
  print('BAKED',obj.name,suffix,flush=True)

for obj in new+[bpy.data.objects[name] for name in base_names]:
 bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
 if obj.name!='StreetGlass':
  while len(obj.data.uv_layers)>1:obj.data.uv_layers.remove(obj.data.uv_layers[0])
 points=[(v.co.x,v.co.z-.008-3.0375/2,v.co.y) for v in obj.data.vertices]
 entry=dict(name=obj.name,bounds=[[min(v[i] for v in points) for i in range(3)],[max(v[i] for v in points) for i in range(3)]],vertices=len(points))
 manifest['objects']=[x for x in manifest['objects'] if x['name']!=obj.name]+[entry]
 for face in obj.data.polygons:face.material_index=0
 obj.data.materials.clear();obj.data.materials.append(bpy.data.materials.new(obj.name+'Runtime'))
 path=O/(obj.name+'.fbx');bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',bake_anim=False,add_leaf_bones=False)
 if path.name not in manifest['files']:manifest['files'].append(path.name)
manifest['files']=list(dict.fromkeys(manifest['files']))
manifest['sha256']={f:hashlib.sha256((O/f).read_bytes()).hexdigest() for f in manifest['files']}
manifest['bake']='48 samples combined room lighting; 32 samples dedicated photograph, foliage cutout and street bulb atlases; candles excluded'
(O/'manifest.json').write_text(json.dumps(manifest,indent=2))
print('REFINEMENT_DONE',flush=True)

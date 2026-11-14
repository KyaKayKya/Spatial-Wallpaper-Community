import bpy,json,numpy as np
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2]
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'art/showcase/blender-study/showcase-organic-final.blend'),load_ui=False,use_scripts=False)
out=ROOT/'Prototype/Assets/Showcase/Models/Organic';out.mkdir(parents=True,exist_ok=True)
prop=bpy.data.objects['Organic_Sculpture__Flareworks_Studio']
bpy.ops.object.select_all(action='DESELECT');prop.select_set(True);bpy.context.view_layer.objects.active=prop
bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
# Origin at shelf contact, so the Unity slot can position a replacement independently.
shift=Vector((0,.095,-.087))
for v in prop.data.vertices:v.co=prop.matrix_world@v.co-shift
prop.matrix_world.identity()
bpy.ops.export_scene.fbx(filepath=str(out/'OrganicSculpture.fbx'),use_selection=True,object_types={'MESH'},add_leaf_bones=False,axis_forward='-Z',axis_up='Y',apply_unit_scale=True,use_mesh_modifiers=True,bake_anim=False)
nodes=prop.data.materials[0].node_tree.nodes
info=[]
for node in nodes:
 if node.type=='TEX_IMAGE' and node.image:
  img=node.image
  info.append({'node':node.name,'image':img.name,'space':img.colorspace_settings.name,'size':list(img.size)})
  target='Normal' if 'normal' in (img.name+node.name).lower() else 'Roughness' if 'rough' in (img.name+node.name).lower() else 'Albedo'
  a=np.array(img.pixels[:],dtype=np.float32).reshape(-1,4)
  if target=='Albedo':
   # Same linear color mix as the accepted Cycles material.
   a[:,:3]=.74*np.array([.76,.70,.61])+.26*a[:,:3]*np.array([.80,.73,.61])
  elif target=='Roughness':
   smooth=1-(.34+.14*a[:,0]);a[:,:3]=0;a[:,3]=smooth;target='MetallicSmoothness'
  temp=bpy.data.images.new(target,width=img.size[0],height=img.size[1],alpha=True,float_buffer=True)
  temp.colorspace_settings.name='Linear Rec.709' if target=='Albedo' else 'Non-Color'
  temp.pixels.foreach_set(a.ravel());temp.filepath_raw=str(out/(target+'.png'));temp.file_format='PNG';temp.save()
(ROOT/'Validation/Showcase/unity-export.json').write_text(json.dumps(info,indent=2))
print('EXPORTED',json.dumps(info))

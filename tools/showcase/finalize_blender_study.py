import bpy, json
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'art/showcase/blender-study';EVID=ROOT/'Validation/Showcase/blender-study'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'showcase-organic-refined.blend'),load_ui=False,use_scripts=False)
sc=bpy.context.scene
prop=bpy.data.objects['Organic_Sculpture__Flareworks_Studio']
bpy.ops.object.select_all(action='DESELECT');prop.select_set(True);bpy.context.view_layer.objects.active=prop
for mod in list(prop.modifiers):
 if mod.type=='SUBSURF':bpy.ops.object.modifier_apply(modifier=mod.name)
bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
# Slice only the bottom plane. Unlike solid Boolean subtraction this preserves
# the source asset's disconnected upper component.
import bmesh
cut=-.087+.0015
bm=bmesh.new();bm.from_mesh(prop.data)
plane=prop.matrix_world.inverted()@Vector((0,0,cut))
result=bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),dist=.0000001,plane_co=plane,plane_no=Vector((0,0,1)),clear_inner=True,clear_outer=False)
boundary=[e for e in result['geom_cut'] if isinstance(e,bmesh.types.BMEdge) and e.is_boundary]
bmesh.ops.holes_fill(bm,edges=boundary,sides=0)
bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
bm.to_mesh(prop.data);bm.free();prop.data.update();prop.location.z-=.0015
for poly in prop.data.polygons:
 if poly.normal.z<-.999:poly.use_smooth=False
bev=prop.modifiers.new('Soft_base_edge_0p15mm','BEVEL');bev.width=.00015;bev.segments=3;bev.limit_method='ANGLE';bev.angle_limit=.7
prop['modification']='Original author geometry; 1.5 mm bottom trimmed flat at display scale, 0.15 mm rim bevel; ivory ceramic material adaptation.'
m=prop.data.materials[0];n=m.node_tree.nodes;l=m.node_tree.links
p=next(x for x in n if x.type=='BSDF_PRINCIPLED')
previous=p.inputs['Base Color'].links[0].from_socket
mix=n.new('ShaderNodeMixRGB');mix.name='Subtle_original_surface_variation';mix.blend_type='MIX';mix.inputs[0].default_value=.26;mix.inputs[1].default_value=(.76,.70,.61,1)
l.new(previous,mix.inputs[2]);l.new(mix.outputs[0],p.inputs['Base Color'])
p.inputs['Coat Weight'].default_value=.12;p.inputs['Coat Roughness'].default_value=.32
for x in n:
 if x.type=='MAP_RANGE':x.inputs['To Min'].default_value=.34;x.inputs['To Max'].default_value=.48
def point(o,target):o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler()
key=bpy.data.objects['Template_Key'];key.location=(-.24,-.22,.205);key.data.energy=5.8;key.data.size=.10;key.data.size_y=.08;key.data.color=(1,.91,.78);point(key,(.02,.12,-.045))
fill=bpy.data.objects['Template_Fill'];fill.location=(.31,-.28,-.025);fill.data.energy=.65;point(fill,(0,.13,-.08))
top=bpy.data.objects['Template_Top'];top.data.energy=.4
sc.world.node_tree.nodes.get('Background').inputs['Strength'].default_value=.055
sc.view_settings.exposure=-.25
sc.cycles.samples=256;sc.cycles.adaptive_threshold=.006;sc.cycles.use_denoising=True
# Background processes do not persist device preferences; select the installed render GPU.
prefs=bpy.context.preferences.addons['cycles'].preferences
try:
 prefs.compute_device_type='OPTIX';prefs.get_devices()
 for d in prefs.devices:d.use='5070' in d.name
 sc.cycles.device='GPU'
except Exception:sc.cycles.device='CPU'
sc.render.resolution_x=1600;sc.render.resolution_y=900;sc.render.resolution_percentage=100
sc.render.use_compositing=False;sc.render.use_sequencer=False
front=sc.camera;front.name='View_01_Front';front.location=(0,-.60,0);point(front,(0,0,0));front.data.shift_x=0;front.data.shift_y=0
front.data.lens=40.75
side=front.copy();side.data=front.data.copy();side.name='View_02_Oblique';sc.collection.objects.link(side)
side.location=(.22,-.60,.035);side.data.shift_x=-.22/.53;side.data.shift_y=-.035/.53
profile=front.copy();profile.data=front.data.copy();profile.name='View_03_Prop_Profile';sc.collection.objects.link(profile)
profile.location=(.38,.095,-.009);point(profile,(0,.095,-.020));profile.data.type='ORTHO';profile.data.ortho_scale=.18;profile.data.shift_x=0;profile.data.shift_y=0
detail=front.copy();detail.data=front.data.copy();detail.name='View_04_Prop_Front';sc.collection.objects.link(detail)
detail.location=(0,-.35,-.011);point(detail,(0,.095,-.020));detail.data.type='ORTHO';detail.data.ortho_scale=.18;detail.data.shift_x=0;detail.data.shift_y=0
bpy.context.view_layer.update()
ev=prop.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=ev.to_mesh();pts=[ev.matrix_world@v.co for v in mesh.vertices]
lo=min(v.z for v in pts);base=[v for v in pts if abs(v.z-lo)<.00005]
audit={'asset':'Organic Sculpture','author':'Flareworks Studio','source_variant':'2K','base_trim_m':.0015,'base_bevel_m':.00015,'bottom_z_m':lo,'shelf_top_m':-.087,'contact_gap_m':lo+.087,'contact_vertices':len(base),'contact_span_x_m':max(v.x for v in base)-min(v.x for v in base),'contact_span_y_m':max(v.y for v in base)-min(v.y for v in base),'render_triangles':sum(len(p.vertices)-2 for p in mesh.polygons),'images_packed':all(i.packed_file for i in bpy.data.images if i.source=='FILE'),'renderer':'Cycles / OptiX / RTX 5070 Ti','samples_max':256}
ev.to_mesh_clear()
(EVID/'final-geometry-check.json').write_text(json.dumps(audit,indent=2))
assert abs(audit['contact_gap_m'])<.00005
assert audit['contact_span_x_m']>.035 and audit['contact_span_y_m']>.015
bpy.ops.file.pack_all()
sc.camera=front;sc.render.filepath=str(EVID/'01-front.png')
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'showcase-organic-final.blend'))
for camera,name in [(front,'01-front'),(side,'02-oblique')]:
 sc.camera=camera;sc.render.filepath=str(EVID/(name+'.png'));bpy.ops.render.render(write_still=True)
# Profile inspection hides the cabinet shell so the sculpture's thickness is visible.
hidden=[o for o in sc.objects if o.name.startswith('Cabinet_')]
for o in hidden:o.hide_render=True
sc.render.resolution_x=1000;sc.render.resolution_y=1100
for camera,name in [(profile,'03-prop-profile'),(detail,'04-prop-front')]:
 sc.camera=camera;sc.render.filepath=str(EVID/(name+'.png'));bpy.ops.render.render(write_still=True)
print('FINAL_AUDIT',json.dumps(audit))

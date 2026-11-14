"""Read-only v3 conversion. Bake final geometry; never write the source blend."""
import bpy, bmesh, json, hashlib, math, sys
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2]
SOURCE=ROOT/'art/Train/train_compartment_v3.blend'
if not SOURCE.exists():SOURCE=ROOT.parents[1]/'art/Train/train_compartment_v3.blend'
OUT=ROOT/'Prototype/Assets/Train/Generated'
OUT.mkdir(parents=True,exist_ok=True)
(OUT/'manifest.json').unlink(missing_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(SOURCE),load_ui=False,use_scripts=False)
sys.path.insert(0,str(Path(__file__).parent))
from complete_compartment import rebuild
rebuild()
sc=bpy.context.scene
W,H,FRONT,DEPTH=4.266666666666667,2.4,-1.7,6.0
groups={k:[] for k in ['Architecture','Furnishings','Details','Lamps']}
original=list(sc.objects)
material_cache={}
floor_mat=bpy.data.materials.get('CC0 Carpet009 dark navy and ochre pile')
for object_index,o in enumerate(original):
    if object_index%50==0:print('PREPARING',object_index,len(original),flush=True)
    if o.type not in {'MESH','CURVE','SURFACE','FONT'}:continue
    if o.hide_render or 'snow' in o.name.lower() or 'panorama' in o.name.lower() or 'window glass' in o.name.lower() or 'carpet floor' in o.name.lower():
        bpy.data.objects.remove(o,do_unlink=True);continue
    world=[o.matrix_world@Vector(v) for v in o.bound_box]
    if min(v.y for v in world)>2.8:
        bpy.data.objects.remove(o,do_unlink=True);continue
    bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
    bpy.ops.object.convert(target='MESH')
    # Freeze generated/object coordinates before joining, retaining original UVs and point attributes.
    mesh=o.data
    if not mesh.uv_layers:mesh.uv_layers.new(name='UVMap')
    mesh.uv_layers[0].name='SourceUV'
    low=Vector([min(v.co[i] for v in mesh.vertices) for i in range(3)])
    high=Vector([max(v.co[i] for v in mesh.vertices) for i in range(3)])
    ga=mesh.attributes.new('train_generated','FLOAT_VECTOR','POINT')
    oa=mesh.attributes.new('train_object','FLOAT_VECTOR','POINT')
    ra=mesh.attributes.new('train_random','FLOAT','POINT')
    if len(ga.data)!=len(mesh.vertices):
        print('ATTRIBUTE_REFERENCE',o.name,'retained',len(ga.data),'fresh',len(mesh.attributes[ga.name].data),'vertices',len(mesh.vertices),flush=True)
    ga=mesh.attributes['train_generated'];oa=mesh.attributes['train_object'];ra=mesh.attributes['train_random']
    assert len(ga.data)==len(mesh.vertices),(o.name,len(ga.data),len(mesh.vertices))
    for v in mesh.vertices:
        ga.data[v.index].vector=[(v.co[i]-low[i])/max(high[i]-low[i],1e-8) for i in range(3)]
        oa.data[v.index].vector=v.co
        ra.data[v.index].value=int(hashlib.sha256(o.name.encode()).hexdigest()[:8],16)/4294967295
    for index,mat in enumerate(list(mesh.materials)):
        if not mat or not mat.use_nodes:continue
        original_mat=mat
        if original_mat.name in material_cache:
            mesh.materials[index]=material_cache[original_mat.name];continue
        mat=mat.copy();material_cache[original_mat.name]=mat;mesh.materials[index]=mat;n=mat.node_tree.nodes;l=mat.node_tree.links
        uv=n.new('ShaderNodeUVMap');uv.uv_map='SourceUV'
        for node in list(n):
            if node.type=='TEX_IMAGE' and not node.inputs['Vector'].is_linked:l.new(uv.outputs[0],node.inputs['Vector'])
            if node.type=='TEX_COORD':
                for socket,attr in [('Generated','train_generated'),('Object','train_object')]:
                    a=n.new('ShaderNodeAttribute');a.attribute_name=attr
                    for link in list(node.outputs[socket].links):l.new(a.outputs['Vector'],link.to_socket)
                for link in list(node.outputs['UV'].links):l.new(uv.outputs[0],link.to_socket)
            if node.type=='OBJECT_INFO':
                a=n.new('ShaderNodeAttribute');a.attribute_name='train_random'
                for link in list(node.outputs['Random'].links):l.new(a.outputs['Fac'],link.to_socket)
    key='Lamps' if any(s in o.name.lower() for s in ['shade','bulb']) else 'Furnishings' if any(s in o.name.lower() for s in ['seat','backrest','pillow','blanket','cushion','curtain']) else 'Architecture' if any(s in o.name.lower() for s in ['wall','panel','rail','stile','ceiling','moulding','post','sill']) else 'Details'
    groups[key].append(o)
# Finite floor contains the actual display lower corners after conversion to screen coordinates.
mesh=bpy.data.meshes.new('TrainFloor');mesh.from_pydata([(-W/2,FRONT,0),(W/2,FRONT,0),(W/2,FRONT+DEPTH,0),(-W/2,FRONT+DEPTH,0)],[],[(0,1,2,3)])
floor=bpy.data.objects.new('TrainFloor',mesh);sc.collection.objects.link(floor);mesh.materials.append(floor_mat)
mesh.uv_layers.new(name='SourceUV')
for loop in mesh.loops:
    v=mesh.vertices[loop.vertex_index].co;mesh.uv_layers[0].data[loop.index].uv=(v.x*.4,v.y*.4)
groups['Architecture'].append(floor)
# Save the expanded, individually editable Blender geometry before atlas joining.
blend_out=ROOT/'art/Train/train_compartment_complete.blend'
blend_out.parent.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=str(blend_out))
prefs=bpy.context.preferences.addons['cycles'].preferences
try:
    prefs.compute_device_type='OPTIX';prefs.get_devices()
    for device in prefs.devices:device.use='5070' in device.name
    sc.cycles.device='GPU'
except Exception as e:print('GPU unavailable:',e)
sc.render.engine='CYCLES';sc.cycles.samples=128
sc.render.bake.margin=8;sc.render.bake.use_clear=True
sc.render.image_settings.file_format='PNG';sc.render.image_settings.color_mode='RGBA';sc.render.image_settings.color_depth='8'
joined=[]
for name,objects in groups.items():
    bpy.ops.object.select_all(action='DESELECT')
    for o in objects:o.select_set(True)
    bpy.context.view_layer.objects.active=objects[0];bpy.ops.object.join()
    o=bpy.context.object;o.name=name
    bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    # Clip hidden underside/overhang to the contract, rather than lying with bounds.
    bm=bmesh.new();bm.from_mesh(o.data)
    for co,no in [((-W/2,0,0),(-1,0,0)),((W/2,0,0),(1,0,0)),((0,0,0),(0,0,-1)),((0,0,H),(0,0,1)),((0,FRONT,0),(0,-1,0)),((0,FRONT+DEPTH,0),(0,1,0))]:
        bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),dist=.000001,plane_co=co,plane_no=no,clear_outer=True,clear_inner=False)
    bm.to_mesh(o.data);bm.free()
    o.data.uv_layers.new(name='BakeUV');o.data.uv_layers.active_index=len(o.data.uv_layers)-1
    bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.smart_project(angle_limit=1.15,island_margin=.004);bpy.ops.object.mode_set(mode='OBJECT')
    joined.append(o)
    print('PREPARED',name,len(o.data.vertices),flush=True)
# Bake while all static geometry remains present for occlusion and bounced light.
files=[];records=[];floor_vertices=[]
denoise_scene=bpy.data.scenes.new('Train bake denoising')
denoise_scene.render.engine='BLENDER_EEVEE';denoise_scene.render.resolution_percentage=100
camera=bpy.data.objects.new('Denoise camera',bpy.data.cameras.new('Denoise camera'));denoise_scene.collection.objects.link(camera);denoise_scene.camera=camera
denoise_scene.view_settings.view_transform=sc.view_settings.view_transform
denoise_scene.view_settings.look=sc.view_settings.look
denoise_scene.view_settings.exposure=sc.view_settings.exposure
denoise_scene.view_settings.gamma=sc.view_settings.gamma
denoise_scene.render.image_settings.file_format='PNG';denoise_scene.render.image_settings.color_mode='RGB'
tree=bpy.data.node_groups.new('TrainDenoise','CompositorNodeTree');denoise_scene.compositing_node_group=tree
tree.interface.new_socket(name='Image',in_out='OUTPUT',socket_type='NodeSocketColor')
source_node=tree.nodes.new('CompositorNodeImage');denoise_node=tree.nodes.new('CompositorNodeDenoise');output_node=tree.nodes.new('NodeGroupOutput')
tree.links.new(source_node.outputs['Image'],denoise_node.inputs['Image']);tree.links.new(denoise_node.outputs['Image'],output_node.inputs[0])
for o in joined:
    bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
    resolution=1024 if o.name=='Lamps' else 4096
    img=bpy.data.images.new(o.name+'Baked',width=resolution,height=resolution,alpha=False,float_buffer=True)
    for mat in o.data.materials:
        if not mat or not mat.use_nodes:continue
        node=mat.node_tree.nodes.new('ShaderNodeTexImage');node.image=img;mat.node_tree.nodes.active=node
    bpy.ops.object.bake(type='COMBINED')
    path=OUT/(o.name+'.png')
    source_node.image=img;denoise_scene.render.resolution_x=resolution;denoise_scene.render.resolution_y=resolution;denoise_scene.render.filepath=str(path)
    bpy.ops.render.render(write_still=True,scene=denoise_scene.name);files.append(path.name)
    print('BAKED',o.name,flush=True)
for o in joined:
    bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
    # Do not move or replace materials until ALL groups have baked with identical occluders.
    # BakeUV becomes UV0; geometry is converted into Unity's +Z depth, +Y up convention.
    while len(o.data.uv_layers)>1:o.data.uv_layers.remove(o.data.uv_layers[0])
    for v in o.data.vertices:v.co.y-=FRONT
    points=[(v.co.x,v.co.z-H/2,v.co.y) for v in o.data.vertices]
    if o.name=='Architecture':floor_vertices=[v for v in points if abs(v[1]+H/2)<1e-7]
    records.append(dict(name=o.name,bounds=[[min(v[i] for v in points) for i in range(3)],[max(v[i] for v in points) for i in range(3)]],vertices=len(points)))
    for poly in o.data.polygons:poly.material_index=0
    o.data.materials.clear()
    placeholder=bpy.data.materials.new(o.name+'Baked');o.data.materials.append(placeholder)
    # Unity FBX importer maps Blender Z-up to Y-up; builder applies the floor-centre offset.
    fbx=OUT/(o.name+'.fbx')
    bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',bake_anim=False,add_leaf_bones=False)
    files.append(fbx.name)
(OUT/'manifest.json').write_text(json.dumps(dict(source_sha256=hashlib.sha256(SOURCE.read_bytes()).hexdigest(),volume=[W,H,DEPTH],objects=records,files=files,sha256={f:hashlib.sha256((OUT/f).read_bytes()).hexdigest() for f in files},floor_vertices=floor_vertices,bake='Cycles combined, 128 samples, OIDN compositor, 3x4096 + dedicated 1024 lamp atlas; view-dependent reflections baked',source=str(SOURCE)),indent=2))
print('TRAIN_EXPORT_DONE',flush=True)

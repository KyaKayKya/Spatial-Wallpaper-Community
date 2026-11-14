"""Bake a separate RainyCafe production copy; never save the supplied master."""
import bpy, json, hashlib, math
from pathlib import Path
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

ROOT=Path(__file__).resolve().parents[2]
SOURCE=ROOT/'art/RainyCafe/RainyCafe_Complete_Project/cafe_refinement_v2.blend'
OUT=ROOT/'Prototype/Assets/RainyCafe/Generated'
OUT.mkdir(parents=True,exist_ok=True)
W,H,SCALE,FLOOR=5.4,3.0375,.53/5.4,.008
bpy.ops.wm.open_mainfile(filepath=str(SOURCE),load_ui=False,use_scripts=False)
sc=bpy.context.scene
# Equal suspension lengths; rear ceiling attachment is higher on the sloping roof.
for part in ('Outside bulb','Light bulb','Lamp'):
    front=bpy.data.objects['didier_pendant | didier_pendant__'+part]
    rear=bpy.data.objects['didier_pendant_rear_shortened | didier_pendant__'+part]
    rear.data=front.data.copy();rear.parent=None
    rear.matrix_world=Matrix.Translation((0,2.5,.278532505))@front.matrix_world
bpy.data.objects['V2 central pendant downlight 2'].location.z-=.3
# Reuse the supplied holder on an empty part of the upper shelf.
for original in list(sc.objects):
    if original.type=='MESH' and original.name.startswith('glass_pillar_candle |'):
        candle=original.copy();candle.data=original.data.copy();sc.collection.objects.link(candle)
        candle.name='Upper shelf candle '+original.name
        world=original.matrix_world.copy();candle.parent=None
        candle.matrix_world=Matrix.Translation((-4.81,1.01,1.762))@world
# Source jamb ends at -2.3975 while the pier ends at -2.425: a 27.5 mm slit.
# A same-material reveal closes it without moving the window or furniture.
bpy.ops.mesh.primitive_cube_add(size=1, location=(-2.4125,4.685,2.025))
seal=bpy.context.object;seal.name='V2 rear left window reveal seam closure'
seal.dimensions=(.045,.22,2.47)
seal.data.materials.append(bpy.data.materials['V2 warm lime plaster | grey_plaster CC0'])
bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
for o in list(sc.objects):
    name=o.name.lower()
    if name.startswith(('individual window raindrop','window coalesced rain run')) or 'mist' in name or 'fog' in name:
        bpy.data.objects.remove(o,do_unlink=True)
    elif o.type=='LIGHT' and 'candle' in name:
        o.hide_render=True
    elif o.type=='LIGHT' and 'front room bounce' in name:
        o.data.energy=18
    elif o.type=='LIGHT' and 'front pendant warm ceiling bounce' in name:
        o.hide_render=True

groups={k:[] for k in ('Floor','Walls','Ceiling','Counter','Furniture','Plants','Props','Lamps','Exterior','Glass','SmallGlass')}
cache={}
deps=bpy.context.evaluated_depsgraph_get()
for index,o in enumerate(list(sc.objects)):
    if o.type not in {'MESH','CURVE','SURFACE','FONT'} or o.hide_render: continue
    name=o.name.lower()
    mats=[m.name.lower() for m in o.data.materials if m]
    if any(m in ('material.004','lampion - flamme') for m in mats):
        # Flame is recreated with a shared runtime clock, not frozen into the bake.
        bpy.data.objects.remove(o,do_unlink=True);continue
    bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
    bpy.ops.object.convert(target='MESH')
    mesh=o.data
    if len(mesh.vertices)==0:continue
    if len(mesh.vertices)>120000:
        dec=o.modifiers.new('Runtime reduction','DECIMATE');dec.ratio=.45
        bpy.ops.object.modifier_apply(modifier=dec.name)
    if not mesh.uv_layers:mesh.uv_layers.new(name='SourceUV')
    original_uv=mesh.uv_layers.active.name
    mesh.uv_layers.active.name='SourceUV'
    lo=Vector([min(v.co[i] for v in mesh.vertices) for i in range(3)])
    hi=Vector([max(v.co[i] for v in mesh.vertices) for i in range(3)])
    for attr,kind in [('cafe_generated','FLOAT_VECTOR'),('cafe_object','FLOAT_VECTOR'),('cafe_random','FLOAT')]:
        mesh.attributes.new(attr,kind,'POINT')
    ga=mesh.attributes['cafe_generated'];oa=mesh.attributes['cafe_object'];ra=mesh.attributes['cafe_random']
    seed=int(hashlib.sha256(o.name.encode()).hexdigest()[:8],16)/4294967295
    for v in mesh.vertices:
        ga.data[v.index].vector=[(v.co[i]-lo[i])/max(hi[i]-lo[i],1e-8) for i in range(3)]
        oa.data[v.index].vector=v.co;ra.data[v.index].value=seed
    for slot,original in enumerate(list(mesh.materials)):
        if not original or not original.use_nodes:continue
        if original.name not in cache:
            mat=original.copy();cache[original.name]=mat;n=mat.node_tree.nodes;l=mat.node_tree.links
            uv=n.new('ShaderNodeUVMap');uv.uv_map='SourceUV'
            for node in list(n):
                if node.type=='UVMAP' and node.uv_map==original_uv:node.uv_map='SourceUV'
                if node.type=='TEX_IMAGE' and not node.inputs['Vector'].is_linked:l.new(uv.outputs[0],node.inputs['Vector'])
                if node.type=='TEX_COORD':
                    for socket,attr in [('Generated','cafe_generated'),('Object','cafe_object')]:
                        a=n.new('ShaderNodeAttribute');a.attribute_name=attr
                        for link in list(node.outputs[socket].links):l.new(a.outputs['Vector'],link.to_socket)
                    for link in list(node.outputs['UV'].links):l.new(uv.outputs[0],link.to_socket)
                if node.type=='OBJECT_INFO':
                    a=n.new('ShaderNodeAttribute');a.attribute_name='cafe_random'
                    for link in list(node.outputs['Random'].links):l.new(a.outputs['Fac'],link.to_socket)
        mesh.materials[slot]=cache[original.name]
    bounds=[o.matrix_world@Vector(v) for v in o.bound_box]
    if any('rain-softened' in m or 'rain glass' in m for m in mats):key='Glass'
    elif mats and all(any(s in m for s in ('glass','cristal','verre')) for m in mats):key='SmallGlass'
    elif min(v.y for v in bounds)>4.8 or 'street' in name or 'facade' in name or 'exterior' in name:key='Exterior'
    elif 'floor' in name:key='Floor'
    elif 'ceiling' in name:key='Ceiling'
    elif any(t in name for t in ('wall','skirting','entry','shaker','window frame','rear pier','rear header','rear lintel')):key='Walls'
    elif any(t in name for t in ('cabinet','counter','shelf','backsplash','sill')):key='Counter'
    elif any(t in name for t in ('chair','table','bench')):key='Furniture'
    elif any(t in name for t in ('plant','leaf','leaves','ivy','olive','calathea','spider','pot')):key='Plants'
    elif any(t in name for t in ('pendant','bulb','diffuser')):key='Lamps'
    else:key='Props'
    groups[key].append(o)
    if index%100==0:print('PREPARED',index,flush=True)

# Verify the real enclosing geometry across the required 27 eye combinations.
verts=[];faces=[]
for key in groups:
    for o in groups[key]:
        name=o.name.lower()
        if not any(s in name for s in ('wall','floor','ceiling','rear','window','shaker','glaz','door')):continue
        if any(s in name for s in ('plant','vase','latte','candle','table','chair')):continue
        base=len(verts);verts.extend(o.matrix_world@v.co for v in o.data.vertices)
        faces.extend(tuple(base+i for i in p.vertices) for p in o.data.polygons)
bvh=BVHTree.FromPolygons(verts,faces)
misses=0;rays=0
for x in (-.2,0,.2):
 for y in (-.1,0,.1):
  for z in (-.35,-.6,-.95):
   eye=Vector((x/SCALE,z/SCALE,y/SCALE+H/2+FLOOR))
   for ix in range(41):
    for iy in range(25):
     target=Vector(((-.5+ix/40)*W*.9999,0,FLOOR+(iy/24*.9999+.00005)*H))
     hit=bvh.ray_cast(eye,(target-eye).normalized(),100)
     rays+=1
     if hit[0] is None:misses+=1
checks=dict(eye_count=27,rays=rays,misses=misses)
(ROOT/'Validation/RainyCafe/source-closure.json').write_text(json.dumps(checks,indent=2))
print('CLOSURE',checks,flush=True)
authoring=ROOT/'art/RainyCafe/rainycafe_production.blend'
bpy.ops.wm.save_as_mainfile(filepath=str(authoring))

prefs=bpy.context.preferences.addons['cycles'].preferences
prefs.compute_device_type='OPTIX';prefs.get_devices()
for device in prefs.devices:device.use=device.type!='CPU'
sc.render.engine='CYCLES';sc.cycles.device='GPU';sc.cycles.samples=48
sc.cycles.use_denoising=True
sc.render.bake.margin=12;sc.render.bake.use_clear=True
sc.render.bake.use_pass_glossy=True;sc.render.bake.use_pass_transmission=True
sc.render.image_settings.file_format='PNG';sc.render.image_settings.color_mode='RGB';sc.render.image_settings.color_depth='8'
joined=[]
for key,items in groups.items():
    if not items:continue
    bpy.ops.object.select_all(action='DESELECT')
    for o in items:o.select_set(True)
    bpy.context.view_layer.objects.active=items[0];bpy.ops.object.join();o=bpy.context.object;o.name=key
    world=o.matrix_world.copy();o.parent=None;o.matrix_world=world
    bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    if key not in ('Glass','SmallGlass'):
        o.data.uv_layers.new(name='BakeUV');o.data.uv_layers.active_index=len(o.data.uv_layers)-1
        bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.uv.smart_project(angle_limit=1.15,island_margin=.004)
        bpy.ops.object.mode_set(mode='OBJECT')
    joined.append(o)
    print('JOINED',key,len(o.data.vertices),flush=True)

bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'art/RainyCafe/rainycafe_bake.blend'))
files=[];records=[]
for o in joined:
    if o.name in ('Glass','SmallGlass'):continue
    bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
    resolution=4096 if o.name in ('Floor','Counter','Props','Furniture') else 2048
    for suffix,kind in [('Lighting','COMBINED'),('Albedo','DIFFUSE'),('Roughness','ROUGHNESS'),('Normal','NORMAL')]:
        img=bpy.data.images.new(o.name+suffix,width=resolution,height=resolution,alpha=False,float_buffer=True)
        if suffix in ('Roughness','Normal'):img.colorspace_settings.name='Non-Color'
        for mat in o.data.materials:
            if not mat or not mat.use_nodes:continue
            node=mat.node_tree.nodes.new('ShaderNodeTexImage');node.image=img;mat.node_tree.nodes.active=node
        if kind=='DIFFUSE':
            sc.render.bake.use_pass_direct=False;sc.render.bake.use_pass_indirect=False;sc.render.bake.use_pass_color=True
        else:
            sc.render.bake.use_pass_direct=True;sc.render.bake.use_pass_indirect=True
        bpy.ops.object.bake(type=kind)
        path=OUT/(o.name+suffix+'.png')
        if suffix=='Lighting':img.save_render(str(path),scene=sc)
        else:img.filepath_raw=str(path);img.file_format='PNG';img.save()
        files.append(path.name)
        for mat in o.data.materials:
            if mat and mat.use_nodes:
                for node in list(mat.node_tree.nodes):
                    if node.type=='TEX_IMAGE' and node.image==img:mat.node_tree.nodes.remove(node)
        bpy.data.images.remove(img)
        print('BAKED',o.name,suffix,flush=True)

for o in joined:
    bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
    if o.name not in ('Glass','SmallGlass'):
        while len(o.data.uv_layers)>1:o.data.uv_layers.remove(o.data.uv_layers[0])
    points=[(v.co.x,v.co.z-FLOOR-H/2,v.co.y) for v in o.data.vertices]
    records.append(dict(name=o.name,bounds=[[min(v[i] for v in points) for i in range(3)],[max(v[i] for v in points) for i in range(3)]],vertices=len(points)))
    for p in o.data.polygons:p.material_index=0
    o.data.materials.clear();o.data.materials.append(bpy.data.materials.new(o.name+'Runtime'))
    fbx=OUT/(o.name+'.fbx')
    bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',bake_anim=False,add_leaf_bones=False)
    files.append(fbx.name)
data=dict(source_sha256=hashlib.sha256(SOURCE.read_bytes()).hexdigest(),volume=[W,H,33],floor=FLOOR,candle_baked=False,geometry_checks=checks,objects=records,files=files,sha256={f:hashlib.sha256((OUT/f).read_bytes()).hexdigest() for f in files},candles=[[2.3107,1.09,.869],[1.91,3,.785],[-2.4993,2.1,2.631]],cups=[[1.9548,.85997,.834],[1.984,2.696,.839]],bake='48 sample Cycles static combined diffuse, glossy, transmission and emission; separate albedo roughness and tangent normal; candle sources excluded')
(OUT/'manifest.json').write_text(json.dumps(data,indent=2))
print('RAINYCAFE_EXPORT_DONE',flush=True)

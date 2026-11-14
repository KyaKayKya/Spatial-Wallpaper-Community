"""Read source FBX, simplify isolated copies, never save an original scene."""
import bpy,sys,json,hashlib
from pathlib import Path
args=sys.argv[sys.argv.index('--')+1:];source=Path(args[0]);target=Path(args[1]);target.mkdir(parents=True,exist_ok=True)
ratios=({name:float(ratio) for name,ratio in (item.split(':') for item in args[2].split(','))} if len(args)>2 else {'Architecture':1,'Furnishings':.025,'Details':.006,'Lamps':.05})
report=[]
for name,ratio in ratios.items():
    original=source/(name+'.fbx');output=target/original.name
    bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=str(original),use_anim=False)
    meshes=[o for o in bpy.context.scene.objects if o.type=='MESH'];before=sum(len(o.data.vertices) for o in meshes)
    for obj in meshes:
        if ratio<1:
            bpy.context.view_layer.objects.active=obj;modifier=obj.modifiers.new('SDK preview geometry budget','DECIMATE');modifier.ratio=ratio;modifier.use_collapse_triangulate=True;bpy.ops.object.modifier_apply(modifier=modifier.name)
    after=sum(len(o.data.vertices) for o in meshes)
    bpy.ops.export_scene.fbx(filepath=str(output),object_types={'MESH','EMPTY'},use_selection=False,bake_anim=False,add_leaf_bones=False,axis_forward='-Z',axis_up='Y',path_mode='STRIP')
    item={'name':name,'source':str(original),'source_sha256':hashlib.sha256(original.read_bytes()).hexdigest(),'output_sha256':hashlib.sha256(output.read_bytes()).hexdigest(),'source_vertices':before,'output_vertices':after,'decimate_ratio':ratio};report.append(item);print(json.dumps(item),flush=True)
(target/'geometry-source.json').write_text(json.dumps(report,indent=2))

import bpy,json
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'art/showcase/blender-study'
EVID=ROOT/'Validation/Showcase/blender-study'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'showcase-organic-study.blend'),load_ui=False,use_scripts=False)
sc=bpy.context.scene
for o in sc.objects:
 if o.type=='LIGHT':
  o.data.energy={'Template_Key':4.4,'Template_Fill':.40,'Template_Top':.60}[o.name]
sc.world.node_tree.nodes.get('Background').inputs['Strength'].default_value=.05
prop=bpy.data.objects['Organic_Sculpture__Flareworks_Studio']
for mod in prop.modifiers:
 if mod.type=='SUBSURF':mod.levels=mod.render_levels
bpy.context.view_layer.update()
ev=prop.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=ev.to_mesh()
pts=[ev.matrix_world@v.co for v in mesh.vertices];z=min(v.z for v in pts)
print('ORIGINAL_BASE',json.dumps({str(h):{'count':len(q:=[p for p in pts if p.z<z+h]),'x':max(p.x for p in q)-min(p.x for p in q),'y':max(p.y for p in q)-min(p.y for p in q)} for h in [.0001,.0005,.001,.0015,.002]}))
ev.to_mesh_clear()
prop.location.z+=-.087-z
m=prop.data.materials[0];n=m.node_tree.nodes;l=m.node_tree.links
p=next(x for x in n if x.type=='BSDF_PRINCIPLED')
albedo=next(x for x in n if x.type=='TEX_IMAGE' and 'albedo' in x.image.name)
mix=n.new('ShaderNodeMixRGB');mix.blend_type='MULTIPLY';mix.inputs[0].default_value=1;mix.inputs[2].default_value=(.80,.73,.61,1)
l.new(albedo.outputs['Color'],mix.inputs[1]);l.new(mix.outputs[0],p.inputs['Base Color'])
rough=next(x for x in n if x.type=='TEX_IMAGE' and 'roughness' in x.image.name)
remap=n.new('ShaderNodeMapRange');remap.inputs['To Min'].default_value=.32;remap.inputs['To Max'].default_value=.52
l.new(rough.outputs['Color'],remap.inputs['Value']);l.new(remap.outputs[0],p.inputs['Roughness'])
p.inputs['Coat Weight'].default_value=.08;p.inputs['Coat Roughness'].default_value=.30
for x in n:
 if x.type=='NORMAL_MAP':x.inputs['Strength'].default_value=.28
sc.render.use_compositing=False;sc.render.use_sequencer=False
sc.render.resolution_percentage=80;sc.cycles.samples=128
bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'showcase-organic-refined.blend'))
sc.render.filepath=str(EVID/'front-refined.png');bpy.ops.render.render(write_still=True)

import bpy,json
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'art/showcase/blender-study'
info={}
for tag in ['organic','studio']:
 bpy.ops.wm.open_mainfile(filepath=str(OUT/'source-assets'/ (tag+'.blend')),load_ui=False,use_scripts=False)
 sc=bpy.context.scene
 rows=[]
 for o in sc.objects:
  r={'name':o.name,'type':o.type,'location':list(o.location),'rotation':list(o.rotation_euler),'scale':list(o.scale),'dimensions':list(o.dimensions)}
  if o.type=='MESH':
   r['polygons']=len(o.data.polygons);r['materials']=[m.name if m else '' for m in o.data.materials]
   r['modifiers']=[{'type':m.type,'name':m.name,'levels':getattr(m,'levels',None),'render_levels':getattr(m,'render_levels',None)} for m in o.modifiers]
  if o.type=='LIGHT':r['light']={'type':o.data.type,'power':o.data.energy,'color':list(o.data.color),'size':getattr(o.data,'size',None)}
  rows.append(r)
 info[tag]={'objects':rows,'world':sc.world.name if sc.world else None,'engine':sc.render.engine,'images':[{'name':i.name,'packed':bool(i.packed_file),'size':list(i.size),'path':i.filepath} for i in bpy.data.images]}
(OUT/'inspection.json').write_text(json.dumps(info,indent=2))
print(json.dumps(info))

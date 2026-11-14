import bpy,json
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'art/showcase/blender-study/showcase-organic-study.blend'),load_ui=False,use_scripts=False)
m=bpy.data.objects['Organic_Sculpture__Flareworks_Studio'].data.materials[0]
for n in m.node_tree.nodes:
 print(n.name,n.type, n.image.name if n.type=='TEX_IMAGE' else '', [(i.name,str(i.default_value)) for i in n.inputs if hasattr(i,'default_value') and not i.is_linked])
print('LINKS',[(l.from_node.name,l.from_socket.name,l.to_node.name,l.to_socket.name) for l in m.node_tree.links])
print('COMPOSITOR',bpy.context.scene.use_nodes)
print('color',bpy.context.scene.view_settings.view_transform,bpy.context.scene.view_settings.look,bpy.context.scene.view_settings.exposure)
print('LIGHTS',[(o.name,o.data.energy) for o in bpy.context.scene.objects if o.type=='LIGHT'])

sc=bpy.context.scene
nt=getattr(sc,'compositing_node_group',None) or getattr(sc,'node_tree',None)
print('COMPOSITE',[(n.name,n.type) for n in nt.nodes] if nt else None)
print('props',sc.render.use_compositing,sc.render.use_sequencer,sc.render.filter_size,sc.render.resolution_percentage,sc.camera.data.dof.use_dof)

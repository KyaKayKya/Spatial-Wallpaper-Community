import bpy
from pathlib import Path
r=Path(__file__).resolve().parents[2]
bpy.ops.wm.open_mainfile(filepath=str(r/'art/limpool/source/Scene/Liminal_Pool.blend'))
for m in bpy.data.materials:
 if any(x in m.name.lower() for x in ['wall','plaster','concrete']):
  print('MATERIAL',m.name)
  if m.node_tree:
   for n in m.node_tree.nodes:
    print(n.name,n.type,[(v.name,list(v.default_value) if hasattr(v.default_value,'__len__') else str(v.default_value)) for v in n.inputs if hasattr(v,'default_value')],getattr(getattr(n,'image',None),'filepath',''))
   for l in m.node_tree.links:print('LINK',l.from_node.name,l.from_socket.name,'->',l.to_node.name,l.to_socket.name)

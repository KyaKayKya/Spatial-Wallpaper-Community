"""Inspect original illumination without modifying the source model."""
from pathlib import Path
import bpy
root=Path(__file__).resolve().parents[2]
source=root/'art/Train/train_compartment_v3.blend'
if not source.exists():source=root.parents[1]/'art/Train/train_compartment_v3.blend'
bpy.ops.wm.open_mainfile(filepath=str(source),load_ui=False,use_scripts=False)
for obj in bpy.context.scene.objects:
 if obj.type=='LIGHT':print('LIGHT',obj.name,obj.data.type,obj.data.energy,tuple(obj.location),tuple(obj.data.color),flush=True)
world=bpy.context.scene.world
if world and world.use_nodes:
 for node in world.node_tree.nodes:
  if node.type=='BACKGROUND':print('WORLD',node.name,node.inputs['Strength'].default_value,flush=True)
print('EXPOSURE',bpy.context.scene.view_settings.exposure,flush=True)

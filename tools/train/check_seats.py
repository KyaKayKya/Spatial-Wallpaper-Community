"""Keep the initial Blender seat lengths, without extending them with the walls."""
import bpy,json
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2]
names=['Left continuous channel-stitched seat','Right continuous channel-stitched seat','Left walnut seat apron','Right walnut seat apron']
def dimensions(path):
 bpy.ops.wm.open_mainfile(filepath=str(path),load_ui=False,use_scripts=False)
 result={};deps=bpy.context.evaluated_depsgraph_get()
 for name in names:
  o=bpy.data.objects[name].evaluated_get(deps);mesh=o.to_mesh();ys=[(o.matrix_world@v.co).y for v in mesh.vertices];o.to_mesh_clear()
  result[name]=[min(ys),max(ys)]
 return result
source=ROOT/'art/Train/train_compartment_v3.blend'
if not source.exists():source=ROOT.parents[1]/'art/Train/train_compartment_v3.blend'
original=dimensions(source)
complete=dimensions(ROOT/'art/Train/train_compartment_complete.blend')
result={'original_y_extents':original,'complete_y_extents':complete,'pass':all(abs(original[n][i]-complete[n][i])<.00001 for n in names for i in (0,1))}
(ROOT/'Validation/Train/seat-lengths.json').write_text(json.dumps(result,indent=2));print(json.dumps(result))
assert result['pass'],'Seat length changed from the initial Blender scene'

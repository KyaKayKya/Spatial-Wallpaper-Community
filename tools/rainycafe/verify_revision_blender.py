"""Verify revised source dimensions without touching the supplied master."""
import bpy,json
from pathlib import Path
from mathutils import Vector
r=Path(__file__).resolve().parents[2]
bpy.ops.wm.open_mainfile(filepath=str(r/'art/RainyCafe/rainycafe_production.blend'),load_ui=False,use_scripts=False)
def bounds(prefix):
 points=[o.matrix_world@Vector(v) for o in bpy.context.scene.objects if o.type=='MESH' and o.name.startswith(prefix) for v in o.bound_box]
 return [[min(v[i] for v in points) for i in range(3)],[max(v[i] for v in points) for i in range(3)]]
front=bounds('didier_pendant |');rear=bounds('didier_pendant_rear_shortened |');candle=bounds('Upper shelf candle ')
lengths=[b[1][2]-b[0][2] for b in [front,rear]]
assert abs(lengths[0]-lengths[1])<.0001,lengths
assert abs(candle[0][2]-2.522)<.001,candle
result=dict(front_bounds=front,rear_bounds=rear,suspension_lengths_m=lengths,upper_candle_bounds=candle)
(r/'Validation/RainyCafe/revision-dimensions.json').write_text(json.dumps(result,indent=2))
print('PASS: equal suspension length and candle rests on upper shelf',lengths)
eye=Vector((0,-.6/(.53/5.4),.008+3.0375/2));target=Vector((-2.4993,2.1,2.631))
hit=bpy.context.scene.ray_cast(bpy.context.evaluated_depsgraph_get(),eye,(target-eye).normalized(),distance=(target-eye).length)
print('CAMERA_TO_UPPER_CANDLE',hit[0],hit[4].name if hit[0] else '',tuple(hit[1]))
assert not hit[0] or hit[4].name.startswith('Upper shelf candle '), 'Upper candle blocked by another object'

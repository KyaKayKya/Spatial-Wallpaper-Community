import bpy,json
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Prototype/Assets/Showcase/BakedStudy'
sc=bpy.context.scene
sc.render.engine='BLENDER_EEVEE'
sc.render.resolution_x=16;sc.render.resolution_y=16;sc.render.resolution_percentage=100
sc.render.use_compositing=True
sc.render.image_settings.file_format='OPEN_EXR';sc.render.image_settings.color_depth='16'
sc.compositing_node_group=bpy.data.node_groups.new('DenoiseStaticRadiance','CompositorNodeTree')
tree=sc.compositing_node_group;tree.nodes.clear()
tree.interface.new_socket(name='Image',in_out='OUTPUT',socket_type='NodeSocketColor')
source=tree.nodes.new('CompositorNodeImage')
denoise=tree.nodes.new('CompositorNodeDenoise')
output=tree.nodes.new('NodeGroupOutput')
tree.links.new(source.outputs['Image'],denoise.inputs['Image'])
tree.links.new(denoise.outputs['Image'],output.inputs[0])
print('DENOISE_INPUTS',[(i.name,i.default_value if hasattr(i,'default_value') else '') for i in denoise.inputs],flush=True)
if 'HDR' in denoise.inputs:denoise.inputs['HDR'].default_value=True
for path in sorted((ROOT/'Validation/Showcase/bake-raw').glob('*_*.exr')):
 if not (path.stem.endswith('_empty') or path.stem.endswith('_occupied')):continue
 image=bpy.data.images.load(str(path),check_existing=False)
 source.image=image
 sc.render.resolution_x=image.size[0];sc.render.resolution_y=image.size[1]
 sc.render.filepath=str(OUT/path.name)
 bpy.ops.render.render(write_still=True)
 print('DENOISED',path.name,flush=True)

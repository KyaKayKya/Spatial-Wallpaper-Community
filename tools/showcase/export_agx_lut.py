import bpy,numpy as np
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'art/showcase/blender-study/showcase-organic-final.blend'),load_ui=False,use_scripts=False)
sc=bpy.context.scene
n=64
# Strip layout: red horizontal within a tile, blue selects tile, green vertical.
rgb=np.zeros((n,n*n,4),np.float32)
for g in range(n):
 for b in range(n):
  for r in range(n):rgb[g,b*n+r]=[2**(-10+r/(n-1)*16),2**(-10+g/(n-1)*16),2**(-10+b/(n-1)*16),1]
img=bpy.data.images.new('Accepted_AgX_Log2_LUT',width=n*n,height=n,alpha=True,float_buffer=True)
img.colorspace_settings.name='Linear Rec.709';img.pixels.foreach_set(rgb.ravel())
sc.render.image_settings.file_format='PNG';sc.render.image_settings.color_depth='16';sc.render.image_settings.color_mode='RGB'
img.save_render(str(ROOT/'Prototype/Assets/Showcase/BakedStudy/AcceptedAgX.png'),scene=sc)
print('LUT',sc.view_settings.view_transform,sc.view_settings.look,sc.view_settings.exposure,flush=True)

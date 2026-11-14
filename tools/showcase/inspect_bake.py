import bpy,numpy as np
from pathlib import Path
p=Path(__file__).resolve().parents[2]/'Prototype/Assets/Showcase/BakedStudy'
for f in p.glob('*occupied.exr'):
 i=bpy.data.images.load(str(f)); a=np.array(i.pixels[:]).reshape(-1,4)[:,:3];print(f.name,'max',a.max(),'percentiles',np.percentile(a,[10,50,90,99]))

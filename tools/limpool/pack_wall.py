from pathlib import Path
from PIL import Image,ImageOps
r=Path(__file__).resolve().parents[2]/'Prototype/Assets/Limpool/Generated'
rough=Image.open(r/'beige_wall_001_rough_4k.jpg').convert('L')
black=Image.new('L',rough.size,0)
Image.merge('RGBA',(black,black,black,ImageOps.invert(rough))).save(r/'Wall_MetallicSmoothness.png')

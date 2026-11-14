"""Pack source metallic/roughness into Unity metallic/smoothness, preserving linear data."""
from pathlib import Path
from PIL import Image, ImageOps
p=Path(__file__).resolve().parents[2]/'Prototype/Assets/Parking/Generated'
for stem,metal,rough in [
 ('CRTV','CRTTV_Metalness_png.png','CRTTV_Roughness_png.png'),
 ('CRTVHS','CRTVHSTV_Metalness_png.png','CRTVHSTV_Roughness_png.png'),
 ('Television_01','Television_01_metallic_png.png','Television_01_roughness_png.png'),
 ('television_02','television_02_metal-television_02_rough.png',None)]:
    a=Image.open(p/metal).convert('RGB')
    m=a.getchannel('R')
    r=Image.open(p/rough).convert('L').resize(a.size) if rough else a.getchannel('G')
    zero=Image.new('L',a.size,0)
    Image.merge('RGBA',(m,zero,zero,ImageOps.invert(r))).save(p/(stem+'_UnityMetalSmooth.png'))
    print(stem,a.size)

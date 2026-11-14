"""Generate controlled dimensional blueprints; these are test structures, not community artwork."""
from pathlib import Path
import gzip,struct,json,argparse
parser=argparse.ArgumentParser(description=__doc__)
for name in ['cottage','town','arena']:parser.add_argument('--'+name,type=Path)
args=parser.parse_args()
r=Path(__file__).resolve().parents[2];out=r/'Validation/Blueprint/framing-inputs';out.mkdir(parents=True,exist_ok=True)
def text(s):b=s.encode('utf8');return struct.pack('>H',len(b))+b
def tag(t,name,value):return bytes([t])+text(name)+value
def integer(name,value):return tag(3,name,struct.pack('>i',value))
def short(name,value):return tag(2,name,struct.pack('>h',value))
palette=['minecraft:air','minecraft:stone_bricks','minecraft:oak_planks','minecraft:glass','minecraft:lantern[hanging=false,waterlogged=false]']
def save(name,w,h,l,block):
 data=bytes(block(x,y,z) for y in range(h) for z in range(l) for x in range(w))
 p=out/(name+'.schem');payload=integer('Version',2)+integer('DataVersion',3465)+short('Width',w)+short('Height',h)+short('Length',l)+integer('PaletteMax',len(palette))+tag(10,'Palette',b''.join(integer(state,i) for i,state in enumerate(palette))+b'\0')+tag(7,'BlockData',struct.pack('>i',len(data))+data)+b'\0'
 p.write_bytes(gzip.compress(tag(10,'Schematic',payload),mtime=0));return {'name':name,'path':str(p),'width':w,'height':h,'length':l,'blocks':sum(v!=0 for v in data),'kind':'controlled','expected':'supported'}
def cabin(x,y,z):
 if y==0 or y==6:return 2
 if x in [0,8] or z in [0,8]:return 3 if y in [2,3,4] and (x%4==0 or z%4==0) else 2
 return 0
rows=[save('micro-pavilion',5,5,5,lambda x,y,z:2 if y in [0,4] or x in [0,4] and z in [0,4] else 0),save('wide-gallery',161,14,21,lambda x,y,z:2 if y in [0,13] or (z in [0,20] and x%12==0) or (x in [0,160] and z in [0,20]) else 3 if z in [0,20] and y in [4,5,6,7,8] else 0),save('tall-tower',13,129,13,lambda x,y,z:1 if y in [0,128] or (x in [0,12] or z in [0,12]) and (y%12 in [0,1,11] or x in [0,12] and z in [0,12]) else 3 if x in [0,12] or z in [0,12] else 0),save('long-bridge',17,12,193,lambda x,y,z:1 if y==3 or y<3 and z%24 in [0,1] or y in [4,5] and x in [0,16] else 4 if y==6 and x in [0,16] and z%24==0 else 0),save('large-courtyard',129,33,129,lambda x,y,z:1 if y==0 or (x in [0,128] or z in [0,128]) and y<14 or (x<9 or x>119) and (z<9 or z>119) and (x%120 in [0,8] or z%120 in [0,8] or y==32) else 0),save('padded-offset-cabin',161,64,129,lambda x,y,z:cabin(x-120,y-40,z-92) if 120<=x<=128 and 40<=y<=46 and 92<=z<=100 else 0),save('empty-selection',5,5,5,lambda x,y,z:0)]
rows[-1]['expected']='rejected-empty'
for key,name,expected in [('town','chinese-town','supported'),('cottage','cherry-cottage','supported'),('arena','arena-with-banners','rejected-unsupported')]:
 source=getattr(args,key)
 if source:
  source=source.resolve(strict=True);row={'name':name,'path':str(source),'kind':'private-local','expected':expected}
  if key=='arena':rows.append(row)
  else:rows.insert(0,row)
(out/'cases.json').write_text(json.dumps(rows,indent=2),encoding='utf8');print(json.dumps([{k:v for k,v in row.items() if k!='path'} for row in rows],indent=2))

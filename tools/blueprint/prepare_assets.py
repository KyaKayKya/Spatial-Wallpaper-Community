"""Prepare only block colour textures from the pinned, attributed resource pack."""
from pathlib import Path
import zipfile,io,json,hashlib,math
from PIL import Image
root=Path(__file__).resolve().parents[2];source=root/'.local-assets/blueprint-upstream/pixel-perfection.zip'
expected='cf0926e2357a8cb75aa938895476949c54bc6d11a3cd6742d98999dce70c78b3'
if hashlib.sha256(source.read_bytes()).hexdigest()!=expected:raise SystemExit('Run fetch_sources.py: pinned pack missing or changed')
out=root/'Prototype/Assets/Blueprint/Resources/Blueprint';out.mkdir(parents=True,exist_ok=True)
textures={};tile=16;pad=3;stride=tile+2*pad;width=1024;columns=width//stride
with zipfile.ZipFile(source) as z:
 prefix='assets/minecraft/textures/block/'
 names=sorted(n for n in z.namelist() if n.startswith(prefix) and n.endswith('.png') and '/' not in n[len(prefix):] and not Path(n).stem.endswith(('_n','_s','_e')))
 height=2**math.ceil(math.log2(math.ceil(len(names)/columns)*stride));atlas=Image.new('RGBA',(width,height),(0,0,0,0))
 entries=[]
 for i,n in enumerate(names):
  im=Image.open(io.BytesIO(z.read(n))).convert('RGBA');w,h=im.size
  # Animated strips use their first frame; water/fire motion is supplied by the template.
  im=im.crop((0,0,min(w,h),min(w,h))).resize((tile,tile),Image.Resampling.NEAREST)
  x=i%columns*stride+pad;y=i//columns*stride+pad;atlas.paste(im,(x,y))
  for d in range(1,pad+1):
   atlas.paste(im.crop((0,0,1,tile)),(x-d,y));atlas.paste(im.crop((tile-1,0,tile,tile)),(x+tile-1+d,y))
   atlas.paste(im.crop((0,0,tile,1)),(x,y-d));atlas.paste(im.crop((0,tile-1,tile,tile)),(x,y+tile-1+d))
  for dx in range(-pad,tile+pad):
   for dy in list(range(-pad,0))+list(range(tile,tile+pad)):
    atlas.putpixel((x+dx,y+dy),im.getpixel((max(0,min(tile-1,dx)),max(0,min(tile-1,dy)))))
  key=Path(n).stem
  if key in ['grass_block_top','snow','dirt']:
   (out/'Terrain').mkdir(exist_ok=True);im.save(out/'Terrain'/(key+'.png'),optimize=True)
  textures[key]={'u0':(x+.5)/width,'v0':1-(y+tile-.5)/height,'u1':(x+tile-.5)/width,'v1':1-(y+.5)/height}
  entries.append({'name':key,'source_path':n,'source_sha256':hashlib.sha256(z.read(n)).hexdigest(),'source_size':[w,h]})
 atlas.save(out/'Blocks.png',optimize=True)
catalog={'version':'pixel-perfection-13.21.3-atlas-v1','atlas_sha256':hashlib.sha256((out/'Blocks.png').read_bytes()).hexdigest(),'textureWidth':width,'textureHeight':height,'textures':textures}
(out/'Catalog.json').write_text(json.dumps(catalog,separators=(',',':')),encoding='utf8')
provenance={'title':'Pixel Perfection Legacy 13.21.3 block colour subset','authors':['XSSheep','Nova_Wostra','Pixel Perfection community contributors'],'url':'https://modrinth.com/resourcepack/pixel-perfection-legacy/version/8HUXdaIC','source_url':'https://cdn.modrinth.com/data/6w3F4SEu/versions/8HUXdaIC/Pixel%20Perfection%20Legacy%2013.21.3.zip','source_sha256':expected,'license':'CC-BY-SA-4.0','license_url':'https://creativecommons.org/licenses/by-sa/4.0/','license_evidence':['https://www.curseforge.com/minecraft/texture-packs/pixel-perfection-legacy','https://github.com/Athemis/PixelPerfectionCE'],'changes':'Selected root block colour PNGs only; first square animation frame; nearest resized to 16px; packed into an atlas with edge padding; grass_block_top, dirt and snow also exported as separate mipmapped terrain tiles. No audio, GUI, Minecraft credits text, paintings, PBR maps, addons or game code included. Derived atlas retains CC-BY-SA-4.0.','upstream_metadata_note':'Modrinth labels CC-BY-4.0 but links BY-SA-4.0; use the stricter author-stated BY-SA-4.0 terms.','textures':entries}
(out/'SOURCES.json').write_text(json.dumps(provenance,ensure_ascii=False,indent=2),encoding='utf8')
(out/'LICENSE.txt').write_text('Pixel Perfection Legacy block textures\nCopyright XSSheep, Nova_Wostra and community contributors.\nSource: https://modrinth.com/resourcepack/pixel-perfection-legacy/version/8HUXdaIC\nAdapted atlas: selected colour textures, first animation frame, resized and packed; three separate terrain colour tiles.\nLicensed under Creative Commons Attribution-ShareAlike 4.0 International.\nhttps://creativecommons.org/licenses/by-sa/4.0/\nThis license applies to the texture assets, not to unrelated application code.\n',encoding='utf8')
print('Prepared',len(textures),'colour tiles;',width,'x',height,'atlas;',sum(p.stat().st_size for p in out.iterdir() if p.is_file()),'bytes total')

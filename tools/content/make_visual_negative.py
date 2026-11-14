"""Create an internally valid visual regression negative, never an acceptance artifact."""
import argparse,hashlib,json,zipfile
from build_apps import OUT

def main():
 parser=argparse.ArgumentParser();parser.add_argument('--scene',choices=['train','pool'],default='train');parser.add_argument('--kind',choices=['frozen','lighting','reflection','ambient'],default='frozen');args=parser.parse_args()
 with zipfile.ZipFile(OUT/(args.scene.title()+'.spatial')) as archive:files={name:archive.read(name) for name in archive.namelist()}
 scene=json.loads(files['scene.json']);count=0
 if args.scene=='train':
  assert args.kind=='frozen'
  for effect in scene.get('effects',[]):
   if effect['kind']=='signal':
    for binding in effect['materials']:
     if binding['property']=='_JourneyTime':binding['gain']=0;count+=1
  assert count==6,'Expected actual Train window clocks'
 elif args.kind=='lighting':
  for node in scene['nodes']:node.pop('layer',None)
  for effect in scene['effects']:
   if effect['kind']=='light':effect.pop('mask',None);count+=1
  assert count>0,'Expected Pool real-time fill light'
 else:
  assert args.kind in ['reflection','ambient'];effects=scene['effects'];removed='planar-reflection' if args.kind=='reflection' else 'ambient-gradient';scene['effects']=[effect for effect in effects if effect['kind']!=removed];assert len(effects)-len(scene['effects'])==1
 files['scene.json']=json.dumps(scene,separators=(',',':')).encode()
 manifest=json.loads(files['manifest.json']);manifest['files']['scene.json']={'size':len(files['scene.json']),'sha256':hashlib.sha256(files['scene.json']).hexdigest()};manifest.pop('build_id');manifest['build_id']=hashlib.sha256(json.dumps(manifest,sort_keys=True,separators=(',',':'),ensure_ascii=False).encode()).hexdigest();files['manifest.json']=json.dumps(manifest,separators=(',',':'),ensure_ascii=False).encode()
 path=OUT/(args.scene+'-'+args.kind+'-negative.spatial')
 with zipfile.ZipFile(path,'w') as archive:
  for name,data in files.items():archive.writestr(name,data)
 print('Created visual negative:',path.name,flush=True)
if __name__=='__main__':main()

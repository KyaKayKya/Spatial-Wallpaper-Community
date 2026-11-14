"""Record reproducible source, package and unchanged installation fingerprints."""
from pathlib import Path
import hashlib,json,zipfile,subprocess
from build_apps import ROOT,OUT,PROJECT,validate_receipt
def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def closure(directory):
 h=hashlib.sha256()
 for path in sorted(directory.rglob('*')):
  if path.is_file():h.update(path.relative_to(directory).as_posix().encode()+b'\0');h.update(hashlib.sha256(path.read_bytes()).digest())
 return h.hexdigest()
def main():
 validate_receipt();records=[]
 for relative in ['apps/player','apps/manager']:
  path=OUT/relative;records.append({'path':path.relative_to(ROOT).as_posix(),'closure_sha256':closure(path)})
 packages=[]
 for path in [OUT/'Parking.spatial',OUT/'Pool.spatial',OUT/'Train.spatial',OUT/'static-validation/sample.spatial']:
  with zipfile.ZipFile(path) as archive:manifest=json.loads(archive.read('manifest.json'));packages.append({'path':path.relative_to(ROOT).as_posix(),'sha256':digest(path),'size':path.stat().st_size,'build_id':manifest['build_id'],'schema':manifest['schema'],'sdk':manifest['sdk'],'color_space':manifest['color_space']})
 inputs=[]
 for role,project in [('platform',PROJECT),('parking-author',OUT/'parking-author'),('pool-author',OUT/'pool-author'),('train-author',OUT/'train-author'),('static-author',OUT/'static-validation/author-project')]:
  for name in ['Packages/manifest.json','Packages/packages-lock.json','ProjectSettings/ProjectVersion.txt','ProjectSettings/ProjectSettings.asset']:
   p=project/name;inputs.append({'role':role,'path':p.relative_to(ROOT).as_posix(),'sha256':digest(p)})
 # Verify the SDK author actually consumed the current compiled input files;
 # documentation changes do not invalidate binary/source correspondence.
 author_inputs={}
 for source in (ROOT/'Packages/com.spatialwallpaper.sdk').rglob('*'):
  if source.is_file() and source.suffix in ['.cs','.meta','.shader','.asmdef','.json'] and 'Samples~' not in source.parts:
   relative=source.relative_to(ROOT/'Packages/com.spatialwallpaper.sdk');consumed=OUT/'parking-author/Packages/com.spatialwallpaper.sdk'/relative;assert consumed.is_file() and digest(consumed)==digest(source),'Parking author SDK inputs stale: '+str(relative);author_inputs[source.relative_to(ROOT).as_posix()]=digest(source)
 source=ROOT/'Prototype/Assets/Parking/Editor/ParkingContentExport.cs';assert digest(source)==digest(OUT/'parking-author/Assets/Editor/ParkingContentExport.cs');author_inputs[source.relative_to(ROOT).as_posix()]=digest(source)
 report={'git_head':subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),'scope':'Machine software evidence; no native drag/human/clean Windows environment claim','installations':records,'packages':packages,'resolved_build_inputs':inputs,'parking_author_compiled_inputs':author_inputs,'platform_receipt_sha256':digest(OUT/'reports/platform-source-receipt.json')};(OUT/'reports/fingerprint.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps({'installations':records,'packages':packages},indent=2))
if __name__=='__main__':main()

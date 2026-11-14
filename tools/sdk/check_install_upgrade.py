"""Exercise portable client upgrade, broken dependencies and program removal with retained content. Not RC or content uninstall."""
from pathlib import Path
import argparse,hashlib,json,shutil,subprocess,sys,uuid,zipfile
from check_contracts import ROOT,DATA
from windows_framework import references
from toolchain_process import run_toolchain
from provenance import sources
from verify_delivery import verify_candidate,verify_inventory

def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest()
CHECK_FILES=['Supervisor/Tests/InstallUpgradeChecks.cs','Prototype/Assets/ContentRuntime/Runtime/ContentHostClient.cs','Prototype/Assets/Tracking/Core/TrackingWire.cs','Prototype/Assets/Tracking/Core/RuntimeLease.cs','Packages/com.spatialwallpaper.sdk/Runtime/Hosting/Windows/PinnedProcessOwner.cs']
def check_sources():return {**sources(),**{name:digest(ROOT/name) for name in CHECK_FILES}}
def extract(archive,destination):
 destination=destination.resolve();destination.mkdir()
 with zipfile.ZipFile(archive) as source:
  for entry in source.infolist():
   path=(destination/entry.filename).resolve()
   if not path.is_relative_to(destination) or '\\' in entry.filename or ':' in entry.filename or ((entry.external_attr>>16)&0o170000)==0o120000:raise ValueError('unsafe client ZIP')
   if entry.is_dir():path.mkdir(parents=True,exist_ok=True)
   else:path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(source.read(entry))

def make_old_probe(source,target,quaternion=False):
 # Preserve the actual old candidate identity while using a small original author
 # program to leave real Broker storage. No author DLL or trusted callback injection.
 with zipfile.ZipFile(source) as archive:manifest=json.loads(archive.read('manifest.json'))
 program={'classes':[{'id':'InstallProbe','fields':[],'methods':[{'id':'Start','returns':'void','parameters':[],'locals':[],'body':[
  {'op':'ldstr','value':'install-upgrade-proof'},{'op':'ldstr','value':'retained-from-old-package'},{'op':'ldstr','value':'0'},
  {'op':'call','value':'SpatialWallpaper.Sdk::Storage::BeginWriteText(System.String,System.String,System.String)->System.Int32'},
  {'op':'pop','value':None},{'op':'ret','value':None}]}]}]}
 content={'frame':[.6,.3375,.5],'resources':[],'prefabs':[],'nodes':[{'id':'n0','parent':-1,'components':[
  {'kind':'transform','config':{'position':[0,0,0],'rotation':[0,0,0,1] if quaternion else [0,0,0],'scale':[1,1,1]},'fields':{},'callbacks':[]},
  {'kind':'script','config':{'class':'InstallProbe'},'fields':{},'callbacks':['Start']}]}]}
 canonical=lambda value:json.dumps(value,sort_keys=True,separators=(',',':'),ensure_ascii=False).encode()
 files={'program.json':canonical(program),'content.json':canonical(content)}
 manifest['files']={name:{'size':len(value),'sha256':hashlib.sha256(value).hexdigest()} for name,value in files.items()}
 manifest['licenses']=[{'name':'Original installation persistence probe','source':'tools/sdk/check_install_upgrade.py','license':'MIT','attribution':'SpatialWallpaper contributors'}]
 manifest.pop('build_id');manifest['build_id']=hashlib.sha256(canonical(manifest)).hexdigest();files['manifest.json']=canonical(manifest)
 with zipfile.ZipFile(target,'w',zipfile.ZIP_DEFLATED) as archive:
  for name,value in files.items():archive.writestr(name,value)
 return manifest['build_id']

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--delivery',type=Path,required=True);p.add_argument('--previous-delivery',type=Path,required=True);p.add_argument('--previous-package',type=Path,required=True);p.add_argument('--package',type=Path,required=True);args=p.parse_args()
 current=args.delivery.resolve();previous=args.previous_delivery.resolve();verify_candidate(current)
 old=json.loads((previous/'assembly-report.json').read_text());verify_inventory(previous,old['files'],exclude=['assembly-report.json'])
 run=uuid.uuid4().hex;out=ROOT/'Validation/Content/sdk-runtime/G/install-upgrade'/run;out.mkdir(parents=True);print(out,flush=True)
 data=(ROOT/'Validation/Sdk'/('gu'+run[:10])).resolve();data.mkdir();(data/'install-upgrade-owner.json').write_text(json.dumps({'owner':'sdk-install-upgrade','output':str(out)}))
 app=data/'app';idle=True;receipt=check_sources();report={'status':'failed','scope':__doc__,'sources':receipt,'data':str(data),'application':str(app),'steps':[],'current_manifest_sha256':digest(current/'assembly-report.json'),'previous_manifest_sha256':digest(previous/'assembly-report.json')}
 def move(source,target):
  source=source.resolve();target=target.resolve()
  owned=lambda path:any(path!=base and path.is_relative_to(base) for base in [out.resolve(),data])
  if not idle or not owned(source) or not owned(target) or target.exists():raise ValueError('installation mutation outside idle owned output')
  shutil.move(str(source),str(target))
 def phase(name,mode,expected=''):
  nonlocal idle
  folder=out/name;folder.mkdir();process={};idle=False
  try:
   with (folder/'checks.log').open('w') as log:run_toolchain([out/'Checks.exe',folder,app/'player/ContentPlayer.exe',app/'player/sdk-runtime',data,mode,expected],ROOT,log,180,process)
  finally:
   result=json.loads((folder/'result.json').read_text()) if (folder/'result.json').is_file() else {}
   idle=result.get('all_retained_kernels_exited') is True and process.get('exit_code') is not None
   row={'id':name,'mode':mode,'process':process,'result':result};report['steps'].append(row);(out/'report.json').write_text(json.dumps(report,indent=2))
  if not idle or process.get('exit_code')!=0 or result.get('primary_error') or result.get('cleanup_error'):raise ValueError('installation phase or normal cleanup failed: '+name)
 def retained():
  files={p.relative_to(data).as_posix():digest(p) for folder in [data/'library/builds',data/'sdk-runtime/backend/storage'] for p in folder.rglob('*') if p.is_file() and p.name!='.owner.lock'}
  if not files or not any(name.endswith('state.bin') for name in files):raise ValueError('missing real durable storage baseline')
  return files
 try:
  shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll');shutil.copyfile(ROOT/'Supervisor/Supervisor.exe.config',out/'Checks.exe.config')
  files=CHECK_FILES
  built=subprocess.run([str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'Checks.exe'),*[str(ROOT/name) for name in files]],capture_output=True,text=True)
  (out/'compile.log').write_text(built.stdout+built.stderr);built.check_returncode()
  report['checks_sha256']=digest(out/'Checks.exe')
  report['old_probe_build']=make_old_probe(args.previous_package,out/'old-modern.spatial');shutil.copyfile(args.package,out/'new-modern.spatial')
  sys.path.insert(0,str(ROOT/'tools/content'));from store_fixture import create
  create(out/'legacy.spatial');extract(previous/'SpatialWallpaperClient-windows-x64.zip',app);phase('01-old-baseline','seed')
  before=retained();report['old_durable_files']=before;move(app,out/'retired-old-program');extract(current/'SpatialWallpaperClient-windows-x64.zip',app);phase('02-complete-upgrade','upgrade')
  after=retained();assert all(after.get(name)==value for name,value in before.items()),'upgrade changed old content or storage';kept=after
  phase('03-restart-same-revision','restore');assert retained()==kept,'restart changed durable content'
  runtime=app/'player/sdk-runtime';preview=runtime/'SpatialWallpaper.Preview.exe';hidden=out/'missing-preview.exe'
  move(preview,hidden)
  try:phase('04-missing-runtime-file','reject','SpatialWallpaper.Preview.exe')
  finally:
   if idle:move(hidden,preview)
  assert retained()==kept;phase('05-restored-after-missing','restore')
  intact=out/'intact-runtime';move(runtime,intact);shutil.copytree(previous/'runtime',runtime)
  try:phase('06-mismatched-runtime','reject','runtime-distribution-identity')
  finally:
   if idle:move(runtime,out/'retired-mismatched-runtime');move(intact,runtime)
  assert retained()==kept;phase('07-restored-after-mismatch','restore')
  original=preview.read_bytes();preview.write_bytes(original+b'!')
  try:phase('08-corrupt-runtime-file','reject','runtime-distribution-drift:SpatialWallpaper.Preview.exe')
  finally:
   if idle:preview.write_bytes(original)
  assert retained()==kept;phase('09-restored-after-corruption','restore')
  move(app,out/'removed-program');assert data.is_dir() and retained()==kept,'program removal changed external data';extract(current/'SpatialWallpaperClient-windows-x64.zip',app);phase('10-portable-reinstall','restore');assert retained()==kept
  verify_candidate(current);verify_inventory(previous,old['files'],exclude=['assembly-report.json']);assert check_sources()==receipt,'installation source drift'
  report.update(status='passed-development-portable-upgrade-and-retained-data-not-uninstall',retained_files=kept)
 finally:(out/'report.json').write_text(json.dumps(report,indent=2));print(out,flush=True)

if __name__=='__main__':main()

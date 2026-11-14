"""Upgrade/remove/reinstall actual UPM tarballs in one owned author project, preserving all Assets."""
from pathlib import Path
import argparse,hashlib,json,os,shutil,subprocess,tarfile,uuid
from build_execution import ROOT,UNITY
from toolchain_process import run_toolchain
from verify_delivery import verify_candidate,verify_inventory

def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--delivery',type=Path,required=True);p.add_argument('--previous-delivery',type=Path,required=True);args=p.parse_args()
 current=args.delivery.resolve();previous=args.previous_delivery.resolve();verify_candidate(current);old=json.loads((previous/'assembly-report.json').read_text());verify_inventory(previous,old['files'],exclude=['assembly-report.json'])
 run=uuid.uuid4().hex;out=ROOT/'Validation/Content/sdk-runtime/G/sdk-upgrade'/run;out.mkdir(parents=True);project=ROOT/'Validation/Sdk'/('gsu'+run[:9]);project.mkdir();print(out,flush=True)
 inputs={name:digest(ROOT/name) for name in ['tools/sdk/check_sdk_upgrade.py','tools/sdk/experiments/SdkUpgradeChecks.cs','tools/sdk/toolchain_process.py','tools/sdk/toolchain_command.py','tools/sdk/case_process.py','tools/sdk/case_bootstrap.py']}
 (project/'sdk-upgrade-owner.json').write_text(json.dumps({'owner':'sdk-author-upgrade','output':str(out)}));report={'status':'failed','scope':__doc__,'project':str(project),'steps':[],'sources':inputs,'candidate_manifests':{str(c):digest(c/'assembly-report.json') for c in [previous,current]}}
 for folder in ['Assets/Editor','ProjectSettings','Packages']:(project/folder).mkdir(parents=True,exist_ok=True)
 shutil.copyfile(ROOT/'tools/sdk/experiments/SdkUpgradeChecks.cs',project/'Assets/Editor/SdkUpgradeChecks.cs');shutil.copyfile(ROOT/'Prototype/ProjectSettings/ProjectVersion.txt',project/'ProjectSettings/ProjectVersion.txt')
 old_tgz=next(previous.glob('*.tgz'));new_tgz=next(current.glob('*.tgz'))
 with tarfile.open(old_tgz) as tar:
  for member in tar.getmembers():
   prefix='package/Samples~/StarterKit/'
   if not member.name.startswith(prefix) or not member.isfile():continue
   target=(project/'Assets/ImportedSdkSamples'/member.name[len(prefix):]).resolve();assert target.is_relative_to((project/'Assets/ImportedSdkSamples').resolve());target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(tar.extractfile(member).read())
 def install(tgz):
  dependencies={}
  if tgz:dependencies['com.spatialwallpaper.sdk']='file:'+tgz.as_posix()
  (project/'Packages/manifest.json').write_text(json.dumps({'dependencies':dependencies},indent=2))
 def assets():return {f.relative_to(project).as_posix():digest(f) for f in (project/'Assets').rglob('*') if f.is_file()}
 def phase(name,mode,runtime,tarball=None):
  folder=out/name;folder.mkdir();env=os.environ.copy();env.update(SDK_UPGRADE_OUTPUT=str(folder),SDK_UPGRADE_MODE=mode,SDK_UPGRADE_RUNTIME=str(runtime),SDK_UPGRADE_CACHE=str(project/('v'+name[:2])),SDK_UPGRADE_ATTEMPTS=str(project/('a'+name[:2])));env.setdefault('ALLUSERSPROFILE',env.get('ProgramData','C:/ProgramData'));process={};command=[UNITY,'-batchmode','-projectPath',project,'-logFile',folder/'unity.log']
  if mode=='remove':command+=['-quit']
  else:command+=['-executeMethod','SdkUpgradeChecks.Run']
  error=None
  try:
   with (folder/'toolchain.log').open('w') as log:run_toolchain(command,ROOT,log,900,process,env=env)
  except subprocess.CalledProcessError as caught:error=str(caught)
  report['steps'].append({'id':name,'process':process,'command':[str(v) for v in command]});(out/'report.json').write_text(json.dumps(report,indent=2))
  lock=json.loads((project/'Packages/packages-lock.json').read_text())['dependencies'];log=(folder/'unity.log').read_text(errors='replace')
  if mode=='remove':assert error and 'com.spatialwallpaper.sdk' not in lock and "error CS0246: The type or namespace name 'SpatialWallpaper' could not be found" in log,'SDK removal did not produce the expected missing author API compile error';result={'sdk_removed_from_upm':True,'expected_compile_error':True}
  else:
   assert error is None,error;result=json.loads((folder/'result.json').read_text(encoding='utf-8-sig'));assert result['resources_released'];entry=lock['com.spatialwallpaper.sdk'];assert entry['source']=='local-tarball' and entry['version']=='file:'+tarball.as_posix(),'wrong effective SDK tarball'
  report['steps'][-1].update(result=result,packages_lock_sha256=digest(project/'Packages/packages-lock.json'));(folder/'packages-lock.json').write_text(json.dumps(lock,indent=2));(folder/'assets.json').write_text(json.dumps(assets(),indent=2));(out/'report.json').write_text(json.dumps(report,indent=2));return result
 try:
  install(old_tgz);seed=phase('01-old-author-project','seed',previous/'runtime',old_tgz);baseline=assets();assert 'Assets/OriginalAuthorExport.spatial' in baseline;report['authored_assets']=baseline
  install(new_tgz);upgrade=phase('02-upgrade-same-project','reopen',current/'runtime',new_tgz);assert assets()==baseline,'upgrade changed authored Assets';assert upgrade['material_guid']==seed['material_guid']
  install(None);phase('03-remove-sdk','remove',current/'runtime');assert assets()==baseline,'SDK removal changed authored Assets'
  install(new_tgz);restored=phase('04-reinstall-sdk','reopen',current/'runtime',new_tgz);assert assets()==baseline,'SDK reinstall changed authored Assets';assert restored['material_guid']==seed['material_guid']
  verify_candidate(current);verify_inventory(previous,old['files'],exclude=['assembly-report.json']);assert all(digest(ROOT/name)==value for name,value in inputs.items()),'upgrade test input drift';report['status']='passed-development-upm-upgrade-removal-reinstall-author-assets-retained-not-RC'
 finally:(out/'report.json').write_text(json.dumps(report,indent=2));print(out,flush=True)
if __name__=='__main__':main()

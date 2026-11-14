"""Verify historical candidate inventories; current products use tools/desktop."""
from pathlib import Path,PurePosixPath
import argparse,hashlib,json,shutil,subprocess,sys,tarfile,zipfile
from release_identity import ROOT,identity

def digest(path):return hashlib.sha256(Path(path).read_bytes()).hexdigest()
def inventory(root,exclude=()):
 return {p.relative_to(root).as_posix():digest(p) for p in Path(root).rglob('*') if p.is_file() and p.relative_to(root).as_posix() not in exclude}

def verify_inventory(root,files,exclude=()):
 root=Path(root).resolve()
 if not isinstance(files,dict) or not files:raise ValueError('delivery-empty-inventory')
 seen=set()
 for name,value in files.items():
  parts=PurePosixPath(name)
  if '\\' in name or ':' in name or parts.is_absolute() or any(p in ('..','.') for p in parts.parts) or parts.as_posix()!=name or name.casefold() in seen:raise ValueError('delivery-unsafe-inventory-name:'+name)
  seen.add(name.casefold());path=root.joinpath(*parts.parts)
  if not path.is_relative_to(root) or not path.is_file() or any(p.is_symlink() or p.is_junction() for p in [path,*path.parents] if p.is_relative_to(root)):raise ValueError('delivery-missing-or-linked-file:'+name)
  if not isinstance(value,str) or len(value)!=64 or digest(path)!=value:raise ValueError('delivery-file-hash:'+name)
 if inventory(root,exclude)!=files:raise ValueError('delivery-inventory-extra-or-missing')

def verify_component(receipt,expected,related,original_related=None):
 actual=dict(receipt.get('identity',{}));actual.pop('package_sha256',None)
 if actual!=expected:raise ValueError('delivery-component-identity')
 if receipt.get('mutated_sources_sha256') or receipt.get('diagnostic_mutations') or receipt.get('release_candidate') is False:raise ValueError('delivery-mutated-component')
 old=receipt.get('sources',{})
 if original_related is not None and set(original_related)!=set(related):raise ValueError('delivery-related-input-set-changed')
 for name,value in related.items():
  if old.get(name)!=value:raise ValueError('delivery-related-input-changed:'+name)

def require_rc(manifest):
 # Deliberately closed until the full frozen-policy runner and its final
 # candidate-bound evidence are available. No assembly or short probe is RC.
 raise ValueError('RC-acceptance-incomplete: full D--G evidence has not been certified')

def verify_candidate(directory):
 directory=Path(directory).resolve()
 report=json.loads((directory/'assembly-report.json').read_text())
 verify_inventory(directory,report['files'],exclude=['assembly-report.json'])
 expected=identity();expected.pop('package_sha256')
 if report['identity']!=expected or report.get('status')!='assembled-internal-development-candidate-not-RC':raise ValueError('delivery-candidate-identity-or-status')
 return report

def client_runtime_path(client):return Path(client)/'player/sdk-runtime'

def verify_client_files(client,files):
 for folder in ['player','manager']:
  prefix=folder+'/'
  verify_inventory(Path(client)/folder,{name[len(prefix):]:value for name,value in files.items() if name.startswith(prefix)})

def verify_client_inputs(inputs,root=ROOT):
 root=Path(root).resolve()
 def relevant(path):
  relative=path.relative_to(root)
  if relative.parts[:2]==('Packages','com.spatialwallpaper.sdk') and any(p in ['Documentation~','Samples~','Tests'] for p in relative.parts[2:]):return False
  return path.suffix.lower() in {'.cs','.asmdef','.asmref','.shader','.json','.mat','.asset','.meta','.dll','.exe','.otf','.ttf','.config','.cpp','.h','.py'} or path.name=='ProjectVersion.txt'
 old={Path(name).resolve():value for name,value in inputs.items() if Path(name).resolve().is_relative_to(root) and relevant(Path(name).resolve())}
 current=set(old)
 for relative in ['Packages/com.spatialwallpaper.sdk','Prototype/Assets/ContentRuntime','Prototype/Assets/ContentManager','Prototype/Assets/Tracking/Core','Prototype/Assets/ContentPlayer','Prototype/Assets/Evo']:
  base=root/relative
  for path in base.rglob('*'):
   if relative=='Prototype/Assets/ContentPlayer' and 'Editor' in path.relative_to(base).parts:continue
   if path.is_file() and relevant(path):current.add(path.resolve())
 if current!=set(old):raise ValueError('delivery-client-related-input-set-changed')
 for path,value in old.items():
  if not path.is_file() or digest(path)!=value:raise ValueError('delivery-client-related-input-changed:'+str(path))

def related_inputs(component,source_map=None):
 from provenance import sources
 current=sources() if source_map is None else source_map
 sdk='Packages/com.spatialwallpaper.sdk/'
 fixed=[];prefixes=()
 if component=='worker':
  prefixes=(sdk+'Runtime/',sdk+'ThirdParty/')
  fixed=[sdk+'Editor/TrustedRenderProfileBuilder.cs','tools/sdk/build_worker.py','tools/sdk/build_execution.py','tools/sdk/experiments/SdkWorkerBuild.cs','tools/sdk/experiments/StageCPipeline.cs','tools/sdk/render-profiles.json','tools/sdk/release_identity.py']
 elif component=='preview':
  prefixes=tuple(['Supervisor/'+f+'/' for f in ['Presentation','Preview','Client','Interactive']]+[sdk+'Runtime/Data/','Prototype/Assets/ContentRuntime/Core/'])
  fixed=[sdk+'Runtime/Hosting/'+n for n in ['RuntimeSession.cs','RuntimePackageInspection.cs','WorkerFrameCodec.cs','RuntimeLimits.Generated.cs']]
  fixed += [sdk+'Runtime/Hosting/Windows/'+n for n in ['PinnedProcessOwner.cs','TrustedAudioSource.cs','PreviewStatusFile.cs','TrustedRuntimeDistribution.cs','HostExitContract.cs','SupervisorManagerConnection.cs','SupervisorSessionClient.cs','ContentSurfaceConnection.cs']]
  fixed += [sdk+'Runtime/Capabilities/'+n for n in ['InputBroker.cs','InputGate.cs','InputSnapshot.cs','WindowsMachineClock.cs','SoundMix.cs']]
  fixed += ['Supervisor/Broker/DesktopInputChannel.cs',sdk+'Runtime/Hosting/Windows/PreviewInputChannel.cs',sdk+'Runtime/Native/Windows/WindowsPreviewInput.cs','Supervisor/Windows/Native.cs','Supervisor/Interactive/DesktopHitTester.cpp']
  fixed += ['tools/sdk/build_preview.py','tools/sdk/trusted_client_sources.py','tools/sdk/windows_framework.py','tools/sdk/release_identity.py','tools/sdk/experiments/native_build.py']
 elif component=='validator':
  prefixes=(sdk+'Runtime/Security/',sdk+'Runtime/Data/')
  fixed=['Supervisor/Validation/ValidatorMain.cs',sdk+'Runtime/Hosting/RuntimeLimits.Generated.cs',sdk+'Runtime/Hosting/WindowsWorkerBoundary.cs']
  fixed+=['tools/sdk/build_validator.py','tools/sdk/windows_framework.py','tools/sdk/release_identity.py']
 else:
  prefixes=tuple(['Supervisor/'+f+'/' for f in ['Session','Core','Windows','Hosting','Broker']]+[sdk+'Runtime/Capabilities/'])
  fixed=[sdk+'Runtime/Hosting/'+n for n in ['WorkerExitCodes.cs','WorkerFrameCodec.cs','RuntimeLimits.Generated.cs','RuntimeSession.cs','Windows/PinnedProcessOwner.cs']]
  fixed+=['Supervisor/Interactive/ProcessAudioSampler.cpp','Supervisor/PlayBroker.cs' if component=='play-broker' else 'Supervisor/Host.cs']
  if component=='play-broker':fixed+=['Supervisor/Preview/DeterministicLlmServer.cs']
  fixed+=[sdk+'Runtime/Data/SoundData.cs','tools/sdk/build_supervisor.py','tools/sdk/windows_framework.py','tools/sdk/release_identity.py','tools/sdk/experiments/native_build.py']
 selected={n:v for n,v in current.items() if n.startswith(prefixes) and (component=='worker' or n.endswith('.cs'))}
 for name in fixed:
  if name not in current:raise ValueError('delivery-related-input-missing:'+name)
  selected[name]=current[name]
 return selected

def component(root,kind,expected):
 receipt_name='report.json' if kind=='worker' else 'build.json';receipt=json.loads((root/receipt_name).read_text())
 verify_component(receipt,expected,related_inputs(kind),original_related=related_inputs(kind,receipt.get('sources',{})))
 if kind=='worker':
  if receipt.get('status')!='production-Worker-built-not-acceptance':raise ValueError('delivery-worker-not-production')
  verify_inventory(root/'player',receipt['runtime_files'])
  verify_inventory(root/'dependencies',receipt['dependency_files'])
 else:
  for name,value in receipt['files'].items():
   if digest(root/name)!=value:raise ValueError('delivery-component-file:'+kind+'/'+name)
 return receipt

def zip_tree(root,target):
 with zipfile.ZipFile(target,'w',zipfile.ZIP_DEFLATED) as archive:
  for p in sorted(root.rglob('*')):
   if p.is_file():archive.write(p,p.relative_to(root).as_posix())

def collect_notices(out,receipts):
 notices=out/'notices';notices.mkdir()
 files={};missing=[]
 for label,receipt in receipts.items():
  project=Path(receipt['project']);project=project if project.is_absolute() else ROOT/project
  # The original project can be reused for another build. Its current lock is
  # not evidence of what went into the retained binary.
  lock=json.loads((Path(receipt['archive'])/'dependencies/Packages/packages-lock.json').read_text())
  for name,dependency in lock['dependencies'].items():
   if dependency.get('source') not in ['registry','builtin']:continue
   package=project/'Library/PackageCache'/(name+'@'+dependency['version'])
   if not package.is_dir():missing.append({'component':label,'package':name,'version':dependency['version'],'reason':'original resolved package cache unavailable'});continue
   for source in package.rglob('*'):
    if not source.is_file() or source.suffix=='.meta' or not source.name.lower().startswith(('license','notice','copying','third party notice')):continue
    relative=Path(label)/package.name/source.relative_to(package);target=notices/relative;target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(source,target)
    files[relative.as_posix()]={'source':str(source),'sha256':digest(source)}
 for name,source in [('Cilbox-LICENSE',ROOT/'Packages/com.spatialwallpaper.sdk/ThirdParty/Cilbox/LICENSE'),('PlanarReflections-LICENSE.txt',ROOT/'Packages/com.spatialwallpaper.sdk/ThirdParty/PlanarReflections-LICENSE.txt'),('SourceHanSans-OFL.txt',ROOT/'Prototype/Assets/Calibration/Fonts/LICENSE.txt')]:
  shutil.copyfile(source,notices/name);files[name]={'source':str(source),'sha256':digest(source)}
 (notices/'index.json').write_text(json.dumps({'status':'collected-source-notices-not-public-license-clearance','files':files,'missing_caches':missing,'unresolved':['Project-wide distribution license has not been selected.','Evo UI follows the existing purchased asset authorization; no Evo source is included in this SDK tarball.','Unity engine/runtime redistribution remains subject to the existing Unity license.'],'excluded':'NPR candidate is not included or supported.'},indent=2))
 return notices

def main():
 p=argparse.ArgumentParser(description=__doc__);sub=p.add_subparsers(dest='command',required=True)
 a=sub.add_parser('assemble');a.add_argument('--output',type=Path,required=True);a.add_argument('--worker',type=Path,required=True);a.add_argument('--client',type=Path,required=True)
 v=sub.add_parser('verify');v.add_argument('directory',type=Path);v.add_argument('--require-rc',action='store_true')
 args=p.parse_args()
 if args.command=='assemble':raise SystemExit('Standalone ContentManager assembly is retired; build with tools/desktop/build.py and preserve_main_manager.py.')
 else:
  report=verify_candidate(args.directory)
  if args.require_rc:require_rc(report)
  print('verified internal candidate inventory; RC acceptance remains incomplete')

if __name__=='__main__':main()

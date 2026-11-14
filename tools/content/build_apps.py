"""Build standalone Manager and Runtime from platform code, never scene sources."""
from pathlib import Path
import os,json,subprocess,shutil,hashlib,tarfile
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'Validation/Content/p3';PROJECT=OUT/'platform-project'
SHARED_UI_FILES=['Calibration/Core/ViewportGeometry.cs','Calibration/Runtime/DisplayViewport.cs','Calibration/UI/CalibrationWidgets.cs','Calibration/Resources/ManagerSurface.mat','Calibration/Resources/ManagerSurface.mat.meta']
def current_sources():
 sources=[p for folder in ['Packages/com.spatialwallpaper.sdk','Prototype/Assets/ContentRuntime','Prototype/Assets/ContentManager','Prototype/Assets/ContentPlayer','Prototype/Assets/Tracking/Core'] for p in (ROOT/folder).rglob('*') if p.is_file() and p.suffix in ['.cs','.meta','.shader','.asmdef','.json']]
 sources += [ROOT/'Prototype/Assets'/name for name in SHARED_UI_FILES]
 return sources
def validate_receipt():
 compiled={k:v for k,v in json.loads((OUT/'reports/platform-source-receipt.json').read_text()).items() if Path(k).suffix in ['.cs','.meta','.shader','.asmdef','.json','.mat']}
 current={p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in current_sources()}
 assert compiled==current,'Platform apps are stale relative to source; rebuild before validation'
def copy(source,target):
 assert target.resolve().is_relative_to(PROJECT.resolve())
 if target.exists():shutil.rmtree('\\\\?\\'+str(target.resolve()))
 shutil.copytree(source,target)
def main():
 for relative in ['Assets/ContentSeriesProbe','Assets/Editor/SeriesObserverBuild.cs']:
  target=(PROJECT/relative).resolve();assert target.is_relative_to(PROJECT.resolve())
  if target.is_dir():shutil.rmtree(target)
  elif target.exists():target.unlink()
 probe=PROJECT/'Assets/Editor/TrainCameraIsolationBuild.cs'
 if probe.exists():probe.unlink()
 for folder in ['Assets/Editor','Assets/Fonts','Packages','ProjectSettings']:(PROJECT/folder).mkdir(parents=True,exist_ok=True)
 # Earlier author probes used this generated directory. Remove only their known
 # overlay so no scene-specific runtime or author Editor code enters the apps.
 for relative in ['Assets/Parking','Assets/Editor/ParkingContentExport.cs','Assets/ContentProbe','Assets/Editor/ReflectionIsolationBuild.cs','Assets/Editor/SkyboxIsolationBuild.cs','Assets/Editor/LightmapIsolationBuild.cs','Assets/Editor/MaterialIsolationBuild.cs','Assets/Editor/LightingDiagnosticsBuild.cs','Assets/Editor/StrictVariantBuild.cs','Assets/Editor/MainShadowIsolationBuild.cs','Assets/Editor/CascadeIsolationBuild.cs','Assets/Editor/VariantRetentionBuild.cs','Assets/Resources/ProbeLit0.mat','Assets/Resources/ProbeLit1.mat','Assets/Resources/ProbeNormal.asset']:
  target=(PROJECT/relative).resolve();assert target.is_relative_to(PROJECT.resolve())
  if target.is_dir():shutil.rmtree('\\\\?\\'+str(target))
  elif target.exists():target.unlink()
  metadata=Path(str(target)+'.meta')
  if metadata.exists():metadata.unlink()
 copy(ROOT/'Packages/com.spatialwallpaper.sdk',PROJECT/'Packages/com.spatialwallpaper.sdk')
 for name in ['ContentRuntime','ContentManager','ContentPlayer','Tracking/Core']:
  copy(ROOT/'Prototype/Assets'/name,PROJECT/'Assets'/name)
 # P2 bootstrap/build is an isolated diagnostic entry, not the Runtime scene.
 editor=PROJECT/'Assets/ContentPlayer/Editor'
 if editor.exists():shutil.rmtree(editor)
 for source in SHARED_UI_FILES:
  target=PROJECT/'Assets'/source;target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(ROOT/'Prototype/Assets'/source,target)
 copy(ROOT/'Prototype/Assets/Evo',PROJECT/'Assets/Evo')
 # Restore the UPM-provided TMP essentials before Evo shaders import. Shader.Find
 # alone can find a desktop shader while TMP's required mobile SDF is absent.
 essentials=PROJECT/'Library/PackageCache/com.unity.textmeshpro@3.0.6/Package Resources/TMP Essential Resources.unitypackage'
 if essentials.exists():
  with tarfile.open(essentials,'r:gz') as archive:
   members={m.name:m for m in archive.getmembers()}
   for name,member in members.items():
    if not name.endswith('/pathname'):continue
    relative=archive.extractfile(member).read().decode().strip('\x00\r\n');target=(PROJECT/relative).resolve()
    assert target.is_relative_to((PROJECT/'Assets').resolve())
    prefix=name.rsplit('/',1)[0]
    for suffix in ['asset','asset.meta']:
     source=members.get(prefix+'/'+suffix)
     if source:
      destination=Path(str(target)+('.meta' if suffix=='asset.meta' else ''));destination.parent.mkdir(parents=True,exist_ok=True);destination.write_bytes(archive.extractfile(source).read())
 for p in (ROOT/'Prototype/Assets/Calibration/Fonts').glob('SourceHanSansSC-Regular.otf*'):shutil.copy2(p,PROJECT/'Assets/Fonts'/p.name)
 manifest=json.loads((ROOT/'tools/content/probe-manifest.json').read_text());manifest['dependencies'].update({'com.spatialwallpaper.sdk':'file:com.spatialwallpaper.sdk','com.unity.textmeshpro':'3.0.6','com.unity.ugui':'1.0.0','com.unity.modules.audio':'1.0.0','com.unity.modules.ui':'1.0.0'})
 (PROJECT/'Packages/manifest.json').write_text(json.dumps(manifest,indent=2));shutil.copy2(ROOT/'Prototype/ProjectSettings/ProjectVersion.txt',PROJECT/'ProjectSettings/ProjectVersion.txt')
 shutil.copy2(ROOT/'Prototype/ProjectSettings/ProjectSettings.asset',PROJECT/'ProjectSettings/ProjectSettings.asset')
 sources=current_sources()
 receipt={p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in sources}
 env=os.environ.copy();env.setdefault('ALLUSERSPROFILE',env.get('ProgramData','C:/ProgramData'));env['CONTENT_BUILD_OUTPUT']=str(OUT/'apps')
 result=subprocess.run(['D:/Unity/Editor/2022.3.62f3/Editor/Unity.exe','-batchmode','-quit','-projectPath',str(PROJECT),'-executeMethod','ContentAppBuild.Run','-logFile',str(OUT/'apps-build.log')],env=env,timeout=1200)
 print('Apps Unity exit',result.returncode,flush=True)
 if result.returncode:raise SystemExit(result.returncode)
 assert receipt=={p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in sources},'Build inputs changed'
 (OUT/'reports/platform-source-receipt.json').write_text(json.dumps(receipt,indent=2)+'\n')
if __name__=='__main__':main()

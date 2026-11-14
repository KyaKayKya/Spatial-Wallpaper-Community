"""Adapt the existing Parking author source to the current SDK, without rebuilding art."""
from pathlib import Path
import subprocess,os,shutil
from build_apps import ROOT,OUT
PROJECT=OUT/'parking-author'
def copy(source,target):
 assert target.resolve().is_relative_to(PROJECT.resolve())
 if target.exists():shutil.rmtree('\\\\?\\'+str(target.resolve()))
 shutil.copytree(source,target)
def main():
 subprocess.run([__import__('sys').executable,str(ROOT/'tools/parking/local_assets.py'),'restore'],check=True)
 import json
 for folder in ['Assets/Editor','Packages','ProjectSettings']:(PROJECT/folder).mkdir(parents=True,exist_ok=True)
 manifest=json.loads((ROOT/'tools/content/probe-manifest.json').read_text());manifest['dependencies']['com.spatialwallpaper.sdk']='file:com.spatialwallpaper.sdk';(PROJECT/'Packages/manifest.json').write_text(json.dumps(manifest,indent=2));shutil.copy2(ROOT/'Prototype/ProjectSettings/ProjectVersion.txt',PROJECT/'ProjectSettings/ProjectVersion.txt')
 shutil.copy2(ROOT/'Prototype/ProjectSettings/ProjectSettings.asset',PROJECT/'ProjectSettings/ProjectSettings.asset')
 copy(ROOT.parents[1]/'Prototype/Assets/Bakery',PROJECT/'Assets/Bakery')
 for folder in ['Scenes','Prefabs','Materials','Meshes','Generated','Baked','Settings','Shaders']:
  copy(ROOT/'Prototype/Assets/Parking'/folder,PROJECT/'Assets/Parking'/folder)
 for name in ['ParkingAtmosphere','ParkingLampField','ParkingEnvironmentBinding']:
  for suffix in ['.cs','.cs.meta']:
   source=ROOT/'Prototype/Assets/Parking/Runtime'/(name+suffix);target=PROJECT/'Assets/Parking/Runtime'/source.name;target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source,target)
 copy(ROOT/'Packages/com.spatialwallpaper.sdk',PROJECT/'Packages/com.spatialwallpaper.sdk')
 target=PROJECT/'Assets/Editor/ParkingContentExport.cs';shutil.copy2(ROOT/'Prototype/Assets/Parking/Editor/ParkingContentExport.cs',target)
 env=os.environ.copy();env.setdefault('ALLUSERSPROFILE',env.get('ProgramData','C:/ProgramData'));env['CONTENT_BUILD_OUTPUT']=str(OUT)
 result=subprocess.run(['D:/Unity/Editor/2022.3.62f3/Editor/Unity.exe','-batchmode','-quit','-projectPath',str(PROJECT),'-executeMethod','ParkingContentExport.Run','-logFile',str(OUT/'parking-export.log')],env=env,timeout=1200);print('Parking export Unity exit',result.returncode);raise SystemExit(result.returncode)
if __name__=='__main__':main()

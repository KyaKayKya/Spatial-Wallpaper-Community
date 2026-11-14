"""Export the original CC0 schema5 example with the selected authoring Unity Editor.
Creator tool: Python 3.10+ and Unity 2022.3.62f3 are authoring dependencies;
installed wallpapers and NativeValidator.exe do not require Python or Unity Editor.
"""
import argparse,hashlib,json,os,subprocess
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
def digest(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--unity',type=Path,required=True);p.add_argument('--project',type=Path,default=ROOT/'Prototype');p.add_argument('--output',type=Path,required=True);p.add_argument('--validator',type=Path,required=True);a=p.parse_args();output=a.output.resolve();output.mkdir(parents=True,exist_ok=False);project=a.project.resolve();repo=project.parent
 sources=['Packages/com.spatialwallpaper.sdk/Runtime/Native/NativeExternalPackage.cs','Packages/com.spatialwallpaper.sdk/Editor/ExternalNativeExporter.cs','Packages/com.spatialwallpaper.sdk/Editor/ExternalNativeAuthorMetadata.cs','Prototype/Assets/NativeArtworks/Editor/OriginalNativeExample.cs','Prototype/Assets/NativeArtworks/Shaders/OriginalColor.shader'];before={s:digest(repo/s) for s in sources};commit=subprocess.check_output(['git','rev-parse','HEAD'],cwd=repo,text=True).strip();env=os.environ.copy();env['SPATIAL_EXTERNAL_EXAMPLE_OUTPUT']=str(output/'export');command=[str(a.unity.resolve()),'-batchmode','-quit','-force-d3d11','-projectPath',str(project),'-executeMethod','SpatialWallpaper.Desktop.Editor.OriginalNativeExample.Run','-logFile',str(output/'unity.log')];result=subprocess.run(command,env=env)
 receipt={'source_commit':commit,'sources':before,'unity_exit':result.returncode,'command':command,'scope':'creator export and pure validator; visual/runtime import separately checked'}
 if result.returncode==0:
  package=output/'export/original-cc0-triangle.spatial';validation=subprocess.run([str(a.validator.resolve()),'--package',str(package),'--platform-index','unused'],capture_output=True,text=True);receipt['validation']=json.loads(validation.stdout);receipt['validator_exit']=validation.returncode;receipt['passed']=validation.returncode==0 and receipt['validation']['license_share'] is True and before=={s:digest(repo/s) for s in sources};receipt['files']={str(f.relative_to(output)):digest(f) for f in (output/'export').rglob('*') if f.is_file()}
 else:receipt['passed']=False
 (output/'receipt.json').write_text(json.dumps(receipt,indent=2),encoding='utf-8');print(output/'receipt.json');return 0 if receipt['passed'] else 1
if __name__=='__main__':raise SystemExit(main())

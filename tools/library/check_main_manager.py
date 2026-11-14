"""Focused main-manager content checks; does not run the RC matrix."""
from pathlib import Path
import subprocess

ROOT=Path(__file__).resolve().parents[2]
DATA=Path('D:/Unity/Editor/2022.3.62f3/Editor/Data')
OUT=ROOT/'Validation/Content/sdk-runtime/main-manager'
OUT.mkdir(parents=True,exist_ok=True)
mono=DATA/'MonoBleedingEdge/bin/mono.exe'
sources=[ROOT/'tools/library/MainManagerContentChecks.cs',*list((ROOT/'Prototype/Assets/Library/Core').glob('*.cs')),ROOT/'Prototype/Assets/ContentRuntime/Core/ContentRenderSize.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Data/Framing.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/RuntimeLimits.Generated.cs']
command=[str(mono),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-out:'+str(OUT/'MainManagerContentChecks.exe'),*map(str,sources)]
result=subprocess.run(command,cwd=ROOT,capture_output=True,text=True)
log=result.stdout+result.stderr
if result.returncode==0:
 result=subprocess.run([str(mono),str(OUT/'MainManagerContentChecks.exe')],cwd=ROOT,capture_output=True,text=True)
 log+=result.stdout+result.stderr
name='catalog-green.log' if result.returncode==0 else 'catalog-red.log'
(OUT/name).write_text(log,encoding='utf-8')
print(log)
raise SystemExit(result.returncode)

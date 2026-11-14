from pathlib import Path
import subprocess
import os
import sys
root=Path(__file__).resolve().parents[2]
data=Path(os.environ.get('UNITY_EDITOR_DATA','D:/Unity/Editor/2022.3.62f3/Editor/Data'))
mono=data/('MonoBleedingEdge/bin/mono.exe' if sys.platform=='win32' else 'MonoBleedingEdge/bin/mono')
out=root/'Validation/Library/core'
out.mkdir(parents=True,exist_ok=True)
core=[root/'Packages/com.spatialwallpaper.sdk/Runtime/Data/Framing.cs',*list((root/'Prototype/Assets/Library/Core').glob('*.cs'))]
for name in ['LibraryChecks','WallpaperStopChecks','LibraryInteractionChecks']:
 sources=[root/'tools/library'/f'{name}.cs',*core]
 binary=out/f'{name}.exe'
 subprocess.run([str(mono),str(data/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-out:'+str(binary),*map(str,sources)],check=True)
 subprocess.run([str(mono),str(binary)],check=True)

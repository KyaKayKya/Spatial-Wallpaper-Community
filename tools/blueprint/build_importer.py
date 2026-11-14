"""Compile the small trusted Windows helper; final import needs no Python/Java."""
from pathlib import Path
import subprocess,shutil,sys
root=Path(__file__).resolve().parents[2];data=Path('D:/Unity/Editor/2022.3.62f3/Editor/Data');output=root/'Prototype/Assets/StreamingAssets/BlueprintTools';output.mkdir(parents=True,exist_ok=True)
mono=str(data/'MonoBleedingEdge/bin/mono.exe');csc=str(data/'MonoBleedingEdge/lib/mono/4.5/csc.exe');vendor=list((root/'Prototype/Assets/Blueprint/ThirdParty').rglob('*.cs'))
sys.path.insert(0,str(root/'tools/sdk'));from windows_framework import references
subprocess.run([mono,csc,'-nologo','-langversion:latest','-nullable:enable','-unsafe','-nowarn:0436','-target:library',*references(),'-out:'+str(output/'fNbt.dll'),*map(str,vendor)],check=True)
shutil.copy2(data/'Managed/Newtonsoft.Json.dll',output/'Newtonsoft.Json.dll')
sources=[root/'tools/blueprint/ImportWorker.cs',*list((root/'Prototype/Assets/Blueprint/Core').glob('*.cs'))]
subprocess.run([mono,csc,'-nologo','-langversion:latest','-platform:x64',*references(),'-r:'+str(output/'fNbt.dll'),'-r:'+str(output/'Newtonsoft.Json.dll'),'-out:'+str(output/'BlueprintImport.exe'),*map(str,sources)],check=True)
(output/'BlueprintImport.exe.config').write_bytes(b'<?xml version="1.0"?><configuration><startup><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8"/></startup></configuration>')
print('Importer files:',sum(p.stat().st_size for p in output.iterdir() if p.is_file()),'bytes')

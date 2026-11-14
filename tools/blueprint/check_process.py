from pathlib import Path
import subprocess,shutil,sys
r=Path(__file__).resolve().parents[2];data=Path('D:/Unity/Editor/2022.3.62f3/Editor/Data');out=r/'Validation/Blueprint/process-checks';out.mkdir(parents=True,exist_ok=True)
for name in ['fNbt.dll','Newtonsoft.Json.dll']:shutil.copy2(r/'Prototype/Assets/StreamingAssets/BlueprintTools'/name,out/name)
files=[r/'tools/blueprint/ProcessChecks.cs',r/'Prototype/Assets/Blueprint/Runtime/BlueprintImportProcess.cs',*list((r/'Prototype/Assets/Blueprint/Core').glob('*.cs'))]
sys.path.insert(0,str(r/'tools/sdk'));from windows_framework import references
subprocess.run([str(data/'MonoBleedingEdge/bin/mono.exe'),str(data/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-platform:x64',*references(),'-r:'+str(out/'fNbt.dll'),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'ProcessChecks.exe'),*map(str,files)],check=True)
subprocess.run([str(out/'ProcessChecks.exe'),str(r/'Prototype/Assets/StreamingAssets/BlueprintTools/BlueprintImport.exe'),str(r/'galacticlunairy-cherryblossomcottage.zip'),str(r/'Prototype/Assets/Blueprint/Resources/Blueprint/Catalog.json')],check=True)

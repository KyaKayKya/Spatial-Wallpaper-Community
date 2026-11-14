"""Compile the real engine-independent C# blueprint code and behavior checks."""
from pathlib import Path
import subprocess,shutil,sys,os
root=Path(__file__).resolve().parents[2]
data=Path(os.environ.get('UNITY_EDITOR_DATA','D:/Unity/Editor/2022.3.62f3/Editor/Data'))
out=root/'Validation/Blueprint/checks';out.mkdir(parents=True,exist_ok=True)
suite=sys.argv[1] if len(sys.argv)>1 else 'core'
if suite not in ['core','mesh','layout','environment','workspace','corpus','transport','footprint']:raise SystemExit('Suite not implemented: '+suite)
shutil.copy2(data/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
sources=[root/'tools/blueprint/Fixtures.cs',root/'tools/blueprint'/({'core':'BlueprintChecks.cs','mesh':'MeshChecks.cs','layout':'LayoutChecks.cs','environment':'EnvironmentChecks.cs','workspace':'WorkspaceChecks.cs','corpus':'CorpusChecks.cs','transport':'TransportChecks.cs','footprint':'FootprintChecks.cs'}[suite])]
sources+=list((root/'Prototype/Assets/Blueprint/Core').glob('*.cs'))
mono=str(data/'MonoBleedingEdge/bin/mono.exe');csc=str(data/'MonoBleedingEdge/lib/mono/4.5/csc.exe')
vendor=[mono,csc,'-nologo','-langversion:latest','-nullable:enable','-unsafe','-nowarn:0436','-target:library','-out:'+str(out/'fNbt.dll'),*map(str,(root/'Prototype/Assets/Blueprint/ThirdParty').rglob('*.cs'))]
result=subprocess.run(vendor,capture_output=True,text=True)
if result.returncode:print(result.stdout+result.stderr);raise SystemExit(result.returncode)
binary=out/(suite+'Checks.exe')
command=[mono,csc,'-nologo','-langversion:latest','-r:'+str(out/'fNbt.dll'),'-r:'+str(data/'MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll'),
 '-r:'+str(out/'Newtonsoft.Json.dll'),'-r:System.IO.Compression.dll','-r:System.IO.Compression.FileSystem.dll','-r:Microsoft.CSharp.dll','-out:'+str(binary),*map(str,sources)]
result=subprocess.run(command,capture_output=True,text=True);output=result.stdout+result.stderr
if result.returncode==0:
 arguments=[str(root/'galacticlunairy-cherryblossomcottage.zip')]
 if suite=='mesh':arguments.insert(0,str(root/'Prototype/Assets/Blueprint/Resources/Blueprint/Catalog.json'))
 result=subprocess.run([mono,str(binary),*arguments],capture_output=True,text=True);output+=result.stdout+result.stderr
(out/(suite+'-latest.log')).write_text(output,encoding='utf8');print(output[-12000:]);raise SystemExit(result.returncode)

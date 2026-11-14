"""Read PE metadata; never load candidate assemblies for execution."""
import subprocess,shutil,json
from pathlib import Path
from check_contracts import ROOT,DATA,OUT
def inspect(managed,worker="StageCWorker"):
 managed=Path(managed).resolve()
 OUT.mkdir(parents=True,exist_ok=True)
 cecil=DATA/'MonoBleedingEdge/lib/mono/gac/Mono.Cecil/0.11.1.0__0738eb9f132ed756/Mono.Cecil.dll'
 shutil.copyfile(cecil,OUT/'Mono.Cecil.dll');shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',OUT/'Newtonsoft.Json.dll')
 mono=str(DATA/'MonoBleedingEdge/bin/mono.exe');csc=str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe')
 subprocess.run([mono,csc,'-nologo','-r:'+str(cecil),'-r:'+str(OUT/'Newtonsoft.Json.dll'),'-r:'+str(DATA/'MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll'),'-out:'+str(OUT/'AssemblyInventory.exe'),str(ROOT/'tools/sdk/AssemblyInventory.cs')],check=True,capture_output=True,text=True)
 if worker not in ['StageCWorker','StageDWorker','SdkWorker']:raise ValueError('unknown Worker inventory profile')
 result=subprocess.run([mono,str(OUT/'AssemblyInventory.exe'),str(managed),*([worker] if worker!='StageCWorker' else [])],check=True,capture_output=True,text=True)
 return json.loads(result.stdout.lstrip('﻿'))

def identities(paths):
 paths=[Path(p).resolve() for p in paths]
 OUT.mkdir(parents=True,exist_ok=True)
 cecil=DATA/'MonoBleedingEdge/lib/mono/gac/Mono.Cecil/0.11.1.0__0738eb9f132ed756/Mono.Cecil.dll'
 shutil.copyfile(cecil,OUT/'Mono.Cecil.dll');shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',OUT/'Newtonsoft.Json.dll')
 mono=str(DATA/'MonoBleedingEdge/bin/mono.exe');csc=str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe')
 subprocess.run([mono,csc,'-nologo','-r:'+str(cecil),'-r:'+str(OUT/'Newtonsoft.Json.dll'),'-r:'+str(DATA/'MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll'),'-out:'+str(OUT/'AssemblyInventory.exe'),str(ROOT/'tools/sdk/AssemblyInventory.cs')],check=True,capture_output=True,text=True)
 result=subprocess.run([mono,str(OUT/'AssemblyInventory.exe'),'--identity',*map(str,paths)],check=True,capture_output=True,text=True)
 return json.loads(result.stdout.lstrip('\ufeff'))

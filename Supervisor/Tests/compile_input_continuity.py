"""Compile continuity Client without launching any native/Unity process."""
from pathlib import Path
import sys,subprocess,uuid,json,hashlib,shutil
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'tools/sdk'))
from check_contracts import DATA
from windows_framework import references,FRAMEWORK
from provenance import sources
out=ROOT/'Validation/Content/sdk-runtime/E/input-continuity-compile'/uuid.uuid4().hex;out.mkdir(parents=True);print(out,flush=True)
from native_input_client_sources import native_input_client_sources
files=native_input_client_sources(ROOT)
receipt=sources();report={'status':'failed','sources':receipt,'scope':'compile only; no helper/Player/input executed','client_sources':[p.relative_to(ROOT).as_posix() for p in files]}
try:
 shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
 command=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-r:'+str(FRAMEWORK/'Microsoft.CSharp.dll'),'-r:'+str(FRAMEWORK/'System.Windows.Forms.dll'),'-r:'+str(FRAMEWORK/'System.Drawing.dll'),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'Client.exe'),*map(str,files)]
 result=subprocess.run(command,capture_output=True,text=True,timeout=60);(out/'compile.log').write_text(result.stdout+result.stderr);print(result.stdout+result.stderr);result.check_returncode()
 assert sources()==receipt
 report.update(status='passed-compile-only',client_sha256=hashlib.sha256((out/'Client.exe').read_bytes()).hexdigest())
finally:(out/'report.json').write_text(json.dumps(report,indent=2))

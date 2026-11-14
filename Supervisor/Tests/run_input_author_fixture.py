"""Compile the actual author fixture with controlled frame inputs; no desktop I/O.

This checks the acceptance fixture, not the SDK/Worker implementation.
"""
from pathlib import Path
import sys, subprocess, uuid, json, hashlib
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'tools/sdk'))
from check_contracts import DATA
from windows_framework import references
from provenance import sources
out=ROOT/'Validation/Content/sdk-runtime/E/input-author-fixture'/uuid.uuid4().hex
out.mkdir(parents=True);print(out,flush=True)
files=[ROOT/'Supervisor/Tests/InputAuthorFixtureChecks.cs',ROOT/'tools/sdk/experiments/StageEInputRuntimeAuthor.cs']
def hashes():return {p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in files}
receipt=sources();inputs=hashes();report={'status':'failed','sources':receipt,'fixture_inputs':inputs,'exit_codes':{}}
try:
 commands=[('compile',[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-out:'+str(out/'Checks.exe'),*map(str,files)]),('result',[str(out/'Checks.exe')])]
 for name,command in commands:
  result=subprocess.run(command,capture_output=True,text=True,timeout=30)
  log=result.stdout+result.stderr;(out/(name+'.log')).write_text(log,encoding='utf-8');print(log)
  report['exit_codes'][name]=result.returncode;result.check_returncode()
 assert sources()==receipt and hashes()==inputs,'source drift'
 report['status']='partial-author-fixture-frame-semantics'
finally:(out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')

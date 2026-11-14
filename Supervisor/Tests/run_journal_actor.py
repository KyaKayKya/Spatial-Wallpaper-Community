from pathlib import Path
import subprocess,uuid,shutil,sys,json,hashlib
ROOT=Path(__file__).resolve().parents[2];DATA=Path('D:/Unity/Editor/2022.3.62f3/Editor/Data')
sys.path.insert(0,str(ROOT/'tools/sdk'));from provenance import sources
receipt=sources()
OUT=ROOT/'Validation/Content/sdk-runtime/D/journal-actor'/uuid.uuid4().hex;OUT.mkdir(parents=True)
shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',OUT/'Newtonsoft.Json.dll')
cmd=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-define:SDK_SUPERVISOR_TESTS','-r:'+str(OUT/'Newtonsoft.Json.dll'),'-r:'+str(DATA/'MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll'),'-out:'+str(OUT/'Checks.exe'),str(ROOT/'Supervisor/Tests/JournalActorChecks.cs'),str(ROOT/'Supervisor/Core/JournalActor.cs'),str(ROOT/'Supervisor/Core/FaultJournal.cs')]
r=subprocess.run(cmd,capture_output=True,text=True);print(r.stdout+r.stderr)
if r.returncode:raise SystemExit(r.returncode)
r=subprocess.run([str(OUT/'Checks.exe'),str(OUT)],check=True,timeout=30,capture_output=True,text=True);print(r.stdout+r.stderr)
assert sources()==receipt,'source changed during actor verification'
(OUT/'report.json').write_text(json.dumps({'status':'bounded-persistence-tests-only','sources':receipt,'executable_sha256':hashlib.sha256((OUT/'Checks.exe').read_bytes()).hexdigest(),'exit_code':r.returncode,'output':r.stdout.strip()},indent=2));print(OUT)

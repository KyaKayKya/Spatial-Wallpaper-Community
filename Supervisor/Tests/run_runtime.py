from pathlib import Path
import subprocess,uuid,shutil,sys,json
ROOT=Path(__file__).resolve().parents[2];DATA=Path('D:/Unity/Editor/2022.3.62f3/Editor/Data')
out=ROOT/'Validation/Content/sdk-runtime/D/runtime'/uuid.uuid4().hex;out.mkdir(parents=True)
shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
cmd=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-r:'+str(out/'Newtonsoft.Json.dll'),'-r:'+str(DATA/'MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll'),'-out:'+str(out/'Checks.exe'),str(ROOT/'Supervisor/Tests/RuntimeChecks.cs'),str(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerExitCodes.cs'),*map(str,(ROOT/'Supervisor/Core').glob('*.cs'))]
assert sys.argv[1:] in [[],['--negative-final-observation'],['--negative-exited-kill'],['--negative-unpublished-exit'],['--negative-ready-after-stop']]
negative=sys.argv[1] if len(sys.argv)>1 else None
if negative:
 source=ROOT/'Supervisor/Core/SupervisorRuntime.cs';text=source.read_text(encoding='utf-8-sig')
 if negative=='--negative-final-observation':
  needle='var finalSample=session.Worker.Sample();session.Observe(finalSample);';replacement='var finalSample=session.Worker.Sample();';expected='final-sample-evidence-retained-after-monitor-error'
 elif negative=='--negative-ready-after-stop':
  needle='internal void Running(){lock(stateGate){if(!StopRequested)ready.TrySetResult(true);}}';replacement='internal void Running()=>ready.TrySetResult(true);';expected='ready-published-after-stop'
 elif negative=='--negative-unpublished-exit':
  needle='||sample.StopPublishedAt<session.StopAcceptedAt||sample.StopPublishedAt>at';replacement='';expected='unpublished-zero-exit-not-graceful'
 else:
  needle='if(sample.Alive){session.Forced(at,true);session.Worker.Kill();}';replacement='session.Forced(at,true);session.Worker.Kill();';expected='nonzero-stop-not-graceful'
 assert text.count(needle)==1
 copy=out/'SupervisorRuntime.cs';copy.write_text(text.replace(needle,replacement),encoding='utf-8');cmd=[str(copy) if v==str(source) else v for v in cmd]
subprocess.run(cmd,check=True)
r=subprocess.run([str(out/'Checks.exe'),str(out/'journal')],capture_output=True,text=True,timeout=30);log=r.stdout+r.stderr;(out/'result.log').write_text(log,encoding='utf-8');print(log);print(out)
if negative:assert r.returncode==1 and ('System.Exception: '+expected) in log,'wrong negative failure'
else:r.check_returncode()
(out/'result.json').write_text(json.dumps({'negative':negative,'exit_code':r.returncode,'expected_failure':expected if negative else None},indent=2))

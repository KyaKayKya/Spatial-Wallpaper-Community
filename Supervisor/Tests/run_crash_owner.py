from pathlib import Path
import subprocess,uuid,sys,shutil,json,time,ctypes as c,re,platform,hashlib
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'tools/sdk/experiments'))
from native_build import compile_probe
sys.path.insert(0,str(ROOT/'tools/sdk'))
from provenance import sources
receipt=sources()
from windows_sandbox import k,e,P,w,bind
DATA=Path('D:/Unity/Editor/2022.3.62f3/Editor/Data');OUT=ROOT/'Validation/Content/sdk-runtime/D/crash-owner'/uuid.uuid4().hex;OUT.mkdir(parents=True)
compile_probe(ROOT/'Supervisor/Tests/ProcessProbe.cpp',OUT/'ProcessProbe.exe');shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',OUT/'Newtonsoft.Json.dll')
cmd=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-r:'+str(OUT/'Newtonsoft.Json.dll'),'-r:'+str(DATA/'MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll'),'-out:'+str(OUT/'Checks.exe'),str(ROOT/'Supervisor/Tests/CrashOwnerChecks.cs'),str(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerFrameCodec.cs'),*map(str,(ROOT/'Supervisor/Windows').glob('*.cs')),str(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerExitCodes.cs'),*map(str,(ROOT/'Supervisor/Core').glob('*.cs'))]
subprocess.run(cmd,check=True)
bind(k,'OpenProcess',[w.DWORD,w.BOOL,w.DWORD],P)
handles=[];entries=[];owner=None
try:
 owner=subprocess.Popen([str(OUT/'Checks.exe'),'--owner',str(OUT)],stdout=subprocess.PIPE,stderr=subprocess.PIPE,creationflags=0x08000000)
 deadline=time.monotonic()+10
 while time.monotonic()<deadline:
  if owner.poll() is not None:raise RuntimeError('owner failed: '+str(owner.communicate()))
  try:entries=json.loads((OUT/'ready.json').read_text());break
  except (FileNotFoundError,json.JSONDecodeError):time.sleep(.02)
 if len(entries)!=2:raise RuntimeError('owner startup timeout')
 for entry in entries:
  handle=k.OpenProcess(0x100001,False,entry['pid'])
  if not handle:raise c.WinError(c.get_last_error())
  handles.append(handle)
 before=time.perf_counter();owner.kill();owner.wait(5)
 for handle in handles:
  if k.WaitForSingleObject(handle,2000)!=0:raise RuntimeError('orphan Worker survived owner crash')
 elapsed=(time.perf_counter()-before)*1000
 subprocess.run([str(OUT/'Checks.exe'),'--verify',str(OUT)],check=True,timeout=10)
 report={'workers':2,'both_jobs_terminated':True,'recovery_quarantined_all':True,'elapsed_owner_kill_to_both_exited_ms':elapsed}
 (OUT/'result.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2));print(OUT)
finally:
 if owner is not None and owner.poll() is None:owner.kill();owner.wait(5)
 for handle in handles:k.TerminateProcess(handle,125);k.WaitForSingleObject(handle,2000);k.CloseHandle(handle)
 profiles={entry['profile'] for entry in entries}
 journal=OUT/'journal/state.json'
 if journal.exists():
  state=json.loads(journal.read_text(encoding='utf-8-sig'))
  profiles.update('SpatialWallpaper.Worker.'+attempt for attempt in state['active'])
 for profile in profiles:
  if not re.fullmatch(r'SpatialWallpaper\.Worker\.[0-9a-f]{32}',profile):raise RuntimeError('unexpected generated profile')
  result=e.DeleteAppContainerProfile(profile)
  if result<0:raise RuntimeError('profile cleanup failed: '+hex(result&0xffffffff))

assert sources()==receipt,'source changed during owner crash probe'
report.update(status='native-owner-crash-fixture-only',sources=receipt,platform=platform.platform(),profile_cleanup_completed=True,binaries={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in [OUT/'Checks.exe',OUT/'ProcessProbe.exe']})
(OUT/'report.json').write_text(json.dumps(report,indent=2))
(ROOT/'Validation/Content/sdk-runtime/reports/stage-D-owner-crash-partial.json').write_text(json.dumps(report,indent=2))

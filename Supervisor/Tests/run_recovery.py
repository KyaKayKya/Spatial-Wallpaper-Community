"""Kill the real backend, observe both kernel exits, restart and reject a reimported canonical build."""
from pathlib import Path
import sys,subprocess,argparse,json,uuid,time,shutil,zipfile,ctypes,hashlib
ROOT=Path(__file__).resolve().parents[2];DATA=Path('D:/Unity/Editor/2022.3.62f3/Editor/Data');sys.path.insert(0,str(ROOT/'tools/sdk'));sys.path.insert(0,str(ROOT/'Supervisor/Tests'))
from provenance import sources
from run_production import packages
from endpoint_wait import wait_endpoint,read_endpoint
def main():
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--worker',type=Path,required=True);args=parser.parse_args();worker=args.worker.resolve();receipt=sources()
 run=uuid.uuid4().hex;out=ROOT/'Validation/Content/sdk-runtime/D/recovery'/run;out.mkdir(parents=True);build=packages(out)[0];installation=uuid.uuid4().hex
 reimport=out/'renamed-reimport.spatial'
 with zipfile.ZipFile(out/'healthy.spatial') as original,zipfile.ZipFile(reimport,'w',compression=zipfile.ZIP_DEFLATED) as rewritten:
  for name in reversed(original.namelist()):rewritten.writestr(name,original.read(name))
 assert (out/'healthy.spatial').read_bytes()!=reimport.read_bytes()
 with zipfile.ZipFile(reimport) as z:assert json.loads(z.read('manifest.json'))['build_id']==build
 subprocess.run([sys.executable,str(ROOT/'tools/sdk/build_supervisor.py'),'--output',str(out/'backend')],check=True)
 subprocess.run([sys.executable,str(ROOT/'tools/sdk/build_validator.py'),'--output',str(out/'validator')],check=True)
 shutil.copyfile(out/'backend/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
 subprocess.run([str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-r:'+str(out/'Newtonsoft.Json.dll'),'-r:'+str(DATA/'MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll'),'-out:'+str(out/'Client.exe'),str(ROOT/'Supervisor/Tests/RecoveryChecks.cs')],check=True)
 state=out/'state';attempts=ROOT/'Validation/Sdk'/('z'+run[:10]);config=out/'config.json';config.write_text(json.dumps({'image':str(worker/'player'),'inventory':str(worker/'report.json'),'validator':str(out/'validator'),'state':str(state),'attempts':str(attempts),'width':640,'height':360}))
 k=ctypes.WinDLL('kernel32',use_last_error=True);k.OpenProcess.argtypes=[ctypes.c_uint32,ctypes.c_bool,ctypes.c_uint32];k.OpenProcess.restype=ctypes.c_void_p;k.WaitForSingleObject.argtypes=[ctypes.c_void_p,ctypes.c_uint32];k.CloseHandle.argtypes=[ctypes.c_void_p]
 handles=[];host=None;active=None;started_processes=[];endpoint_events=[]
 def launch(previous=None):
  with (out/('host-'+str(len(started_processes))+'.log')).open('wb') as log:
   process=subprocess.Popen([str(out/'backend/Supervisor.exe'),str(config)],stdout=log,stderr=subprocess.STDOUT)
  started_processes.append(process)
  endpoint=wait_endpoint(lambda:read_endpoint(state/'endpoint.json'),lambda:process.poll() is None,previous,15,events=endpoint_events)
  return process,endpoint['auth']
 try:
  host,auth=launch();subprocess.run([str(out/'Client.exe'),'start',str(state/'endpoint.json'),str(out/'healthy.spatial'),build,str(out/'started.json'),installation],check=True,timeout=35)
  active=json.loads((state/'journal/state.json').read_text(encoding='utf-8-sig'))['active'];assert len(active)==2
  for record in active.values():
   handle=k.OpenProcess(0x100000,False,record['process']);assert handle;handles.append(handle)
  began=time.monotonic();host.terminate();host.wait(timeout=5);exits=[]
  for handle in handles:assert k.WaitForSingleObject(handle,5000)==0;exits.append(round((time.monotonic()-began)*1000,3))
  before={p.name for p in attempts.iterdir() if p.is_dir()};host,_=launch(auth)
  subprocess.run([str(out/'Client.exe'),'resume',str(state/'endpoint.json'),str(reimport),build,str(out/'recovered.json'),installation],check=True,timeout=15);assert host.wait(timeout=10)==0
  journal=json.loads((state/'journal/state.json').read_text(encoding='utf-8-sig'));assert not journal['active'];fault=journal['faults'][installation+'_'+build];assert fault['blocked'] and fault['last_reason']=='UncleanSupervisorExit'
  assert before=={p.name for p in attempts.iterdir() if p.is_dir()},'resume launched a new Worker'
  assert sources()==receipt,'recovery verification source changed'
  report={'status':'real-backend-crash-recovery-observations','sources':receipt,'worker_build_sha256':hashlib.sha256((worker/'report.json').read_bytes()).hexdigest(),'backend_build':json.loads((out/'backend/build.json').read_text()),'two_kernel_exit_ms':exits,'endpoint_discovery_retries':endpoint_events,'same_canonical_build_after_reimport':build,'original_archive_sha256':hashlib.sha256((out/'healthy.spatial').read_bytes()).hexdigest(),'reimport_archive_sha256':hashlib.sha256(reimport.read_bytes()).hexdigest(),'recovered':json.loads((out/'recovered.json').read_text())}
  (out/'report.json').write_text(json.dumps(report,indent=2));print(out)
 finally:
  for process in started_processes:
   if process.poll() is None:process.terminate();process.wait(timeout=5)
  all_exited=bool(active) and len(handles)==len(active) and all(k.WaitForSingleObject(h,0)==0 for h in handles)
  for handle in handles:k.CloseHandle(handle)
  if active and all_exited:
   delete=ctypes.WinDLL('userenv').DeleteAppContainerProfile;delete.argtypes=[ctypes.c_wchar_p];delete.restype=ctypes.c_long
   for attempt in active:
    # Only these two generated profiles, after both tracked kernel handles signalled.
    status=delete('SpatialWallpaper.Worker.'+attempt)
    if status<0:print('generated-profile cleanup HRESULT',hex(status&0xffffffff))
if __name__=='__main__':main()
